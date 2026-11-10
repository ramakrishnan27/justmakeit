// The permission keys the server defines, and the one check made against the list the session holds. A leaf module
// (it imports nothing), so the router, the roles and the screens can all use it without importing one another.
//
// The server decides every request from the permissions in the access token. What the client does with them only
// chooses what to show and which page to open, so a wrong guess here can hide or show a link, never grant anything.

/** The catalog keys (PermissionKeys on the server). A test keeps this list equal to what the server seeds. */
export const permissionKeys = Object.freeze({
  dashboardView: 'dashboard.view',
  workspacesView: 'workspaces.view',
  workspacesCreate: 'workspaces.create',
  dataRead: 'data.read',
  dataWrite: 'data.write',
  organizationManage: 'organization.manage',
  membersManage: 'members.manage',
  platformsView: 'platforms.view',
  contentStudioView: 'contentstudio.view',
  mediaLibraryView: 'medialibrary.view',
  aiStudioView: 'aistudio.view',
  calendarView: 'calendar.view',
  contentStudioCreate: 'contentstudio.create',
  contentStudioPublish: 'contentstudio.publish',
  mediaLibraryCreate: 'medialibrary.create',
  mediaLibraryDelete: 'medialibrary.delete',
});

/**
 * Whether the session's permission list holds a key. A list that is missing (not loaded yet, or signed out) holds
 * nothing, so a check made too early fails closed instead of opening a page.
 */
export function hasPermission(permissions, key) {
  return Array.isArray(permissions) && typeof key === 'string' && permissions.includes(key);
}
