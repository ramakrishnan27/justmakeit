// The permission keys the server defines, and the checks made against the list the session holds. A leaf module
// (it imports nothing), so the session, the routes and the components can all use it.
//
// The server decides every request from the permissions in the access token. What the client does with them only
// chooses what to show and which page to open, so a wrong guess here can hide or show a link, never grant anything.

/** The catalog keys (PermissionKeys on the server). A test keeps this list equal to what the server defines. */
export const PERMISSIONS = Object.freeze({
  dashboardView: 'dashboard.view',
  workspacesView: 'workspaces.view',
  workspacesCreate: 'workspaces.create',
  dataRead: 'data.read',
  dataWrite: 'data.write',
  organizationManage: 'organization.manage',
  membersManage: 'members.manage',
} as const);

export type PermissionKey = (typeof PERMISSIONS)[keyof typeof PERMISSIONS];

/**
 * Whether the session's permission list holds a key. A list that is missing (not loaded yet, or signed out) holds
 * nothing, so a check made too early fails closed instead of opening a page.
 */
export function hasPermission(permissions: readonly string[] | null | undefined, key: string): boolean {
  return Array.isArray(permissions) && typeof key === 'string' && permissions.includes(key);
}

/**
 * What a component asks for. Everything given must hold (permission, anyOf and allOf are combined with AND), and a
 * rule that asks for nothing allows nothing: an empty list never opens a door by accident.
 */
export interface PermissionRule {
  /** This one permission. */
  permission?: PermissionKey;
  /** At least one of these. */
  anyOf?: readonly PermissionKey[];
  /** Every one of these. */
  allOf?: readonly PermissionKey[];
}

export function satisfies(permissions: readonly string[] | null | undefined, rule: PermissionRule): boolean {
  const { permission, anyOf, allOf } = rule;
  if (permission === undefined && anyOf === undefined && allOf === undefined) {
    return false;
  }
  if (permission !== undefined && !hasPermission(permissions, permission)) {
    return false;
  }
  if (anyOf !== undefined && !anyOf.some((key) => hasPermission(permissions, key))) {
    return false;
  }
  if (allOf !== undefined && (allOf.length === 0 || !allOf.every((key) => hasPermission(permissions, key)))) {
    return false;
  }
  return true;
}
