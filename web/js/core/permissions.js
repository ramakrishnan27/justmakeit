// What the Site admin's permission screen does that is not drawing: grouping the slots of the matrix, reading and
// changing a grant, and wording the errors. Plain data and functions, so they are tested without a browser. The keys
// themselves, and the check against the session's list, are in permission-keys.js.

import { ApiError } from './api.js';
import { businessModelLabel } from './format.js';
import { roleTitle } from './roles.js';

/** The slots of the permission matrix grouped by kind of organization, in the order the server sends them. */
export function groupSlots(slots) {
  const groups = [];
  for (const slot of Array.isArray(slots) ? slots : []) {
    let group = groups.find((candidate) => candidate.businessModel === slot.businessModel);
    if (!group) {
      group = { businessModel: slot.businessModel, title: businessModelLabel(slot.businessModel), roles: [] };
      groups.push(group);
    }
    group.roles.push({ role: slot.role, title: roleTitle(slot.role) });
  }
  return groups;
}

/** Whether one slot holds a permission, from a permission row of the matrix. */
export function isGranted(permission, businessModel, role) {
  const grant = permission?.grants?.find((candidate) => candidate.businessModel === businessModel && candidate.role === role);
  return grant?.granted === true;
}

/** The spoken name of one checkbox of the matrix, for example "Create workspaces: Marketing agency, Client Manager". */
export function cellLabel(permission, businessModel, role) {
  return `${permission.title}: ${businessModelLabel(businessModel)}, ${roleTitle(role)}`;
}

/** A copy of the matrix with one grant changed. The matrix held by the page is never edited in place. */
export function withGrant(matrix, permissionKey, businessModel, role, granted) {
  return {
    ...matrix,
    permissions: matrix.permissions.map((permission) =>
      permission.key !== permissionKey
        ? permission
        : {
            ...permission,
            grants: permission.grants.map((grant) =>
              grant.businessModel === businessModel && grant.role === role ? { ...grant, granted } : grant,
            ),
          },
    ),
  };
}

/** What to tell the Site admin when loading or changing a permission failed. */
export function messageForPermissionError(error) {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    if (error.status === 429) {
      return 'Too many changes at once. Wait a moment, then try again.';
    }
    if (error.code === 'permission_protected') {
      return 'This permission is protected: every role keeps it.';
    }
    if (error.code === 'invalid_slot') {
      return 'That role does not exist in that kind of organization.';
    }
    if (error.status === 404) {
      return 'That permission no longer exists. Reload the page.';
    }
    if (error.status === 400 && error.errors) {
      const first = Object.values(error.errors).flat()[0];
      if (typeof first === 'string') {
        return first;
      }
    }
    return 'The permission could not be changed. Try again in a moment.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** What to show when the matrix could not be loaded. */
export function messageForMatrixLoadError(error) {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    if (error.status === 429) {
      return 'Too many requests. Wait a minute, then try again.';
    }
    return 'The permissions could not be loaded.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}
