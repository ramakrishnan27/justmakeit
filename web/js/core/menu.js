// The sidebar's data: what the API sends is checked here before the page draws a single link from it. Plain
// functions, so the rules are tested without a browser. The menu is held in memory by the session; nothing here
// touches storage.

const pathShape = /^\/[A-Za-z0-9/_-]*$/;

/**
 * Whether an address from the API is one this app may link to: an address of this site, made of letters, digits,
 * hyphens, underscores and slashes only. A scheme, a host, a protocol-relative address ("//host"), a dot or a
 * query never passes, so a link from the server can only ever stay on this site.
 */
export function isSafePath(path) {
  return typeof path === 'string' && pathShape.test(path) && !path.includes('//');
}

function parseItem(raw) {
  if (raw === null || typeof raw !== 'object') {
    return [];
  }
  const { id, pageKey, label, icon, path } = raw;
  if (typeof id !== 'string' || id.length === 0 || typeof label !== 'string' || label.trim().length === 0 || !isSafePath(path)) {
    return [];
  }
  return [{ id, pageKey: typeof pageKey === 'string' ? pageKey : '', label, icon: typeof icon === 'string' ? icon : 'circle', path }];
}

/**
 * The menu response as the page uses it. An item the page could not draw safely is dropped; permissions that are
 * not text are dropped; anything else missing leaves an empty list.
 * @returns {{ items: Array<{ id: string, pageKey: string, label: string, icon: string, path: string }>, permissions: string[] }}
 */
export function parseMenu(body) {
  return {
    items: Array.isArray(body?.items) ? body.items.flatMap(parseItem) : [],
    permissions: Array.isArray(body?.permissions) ? body.permissions.filter((permission) => typeof permission === 'string') : [],
  };
}

function withoutTrailingSlash(path) {
  return path.length > 1 ? path.replace(/\/+$/, '') || '/' : path;
}

/** Whether a menu item's address is the page that is open. A trailing slash makes no difference. */
export function isCurrent(itemPath, currentPath) {
  return typeof itemPath === 'string' && typeof currentPath === 'string' && withoutTrailingSlash(itemPath) === withoutTrailingSlash(currentPath);
}

/**
 * The Site admin's sidebar when the server could not send it: the three pages that repair a menu, so a broken menu
 * can always be reached and fixed. Only the site admin app uses it; a member without a menu has no way to know
 * which pages they may open.
 */
export const adminFallbackItems = Object.freeze([
  Object.freeze({ id: 'fallback-dashboard', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' }),
  Object.freeze({ id: 'fallback-menus', pageKey: 'menus', label: 'Menus', icon: 'menu', path: '/admin/menus' }),
  Object.freeze({ id: 'fallback-permissions', pageKey: 'permissions', label: 'Permissions', icon: 'shield', path: '/admin/permissions' }),
]);
