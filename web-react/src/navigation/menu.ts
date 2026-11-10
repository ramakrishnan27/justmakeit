// The sidebar's data: what the API sends is checked here before the page draws a single link from it. Plain
// functions, so the rules are tested without a browser. The menu is held in memory by the session; nothing here
// touches storage.

export interface MenuItem {
  id: string;
  pageKey: string;
  label: string;
  icon: string;
  path: string;
}

export interface Menu {
  items: MenuItem[];
  permissions: string[];
}

const pathShape = /^\/[A-Za-z0-9/_-]*$/;

/**
 * Whether an address from the API is one this app may link to: an address of this site, made of letters, digits,
 * hyphens, underscores and slashes only. A scheme, a host, a protocol-relative address ("//host"), a dot or a
 * query never passes, so a link from the server can only ever stay on this site.
 */
export function isSafePath(path: unknown): path is string {
  return typeof path === 'string' && pathShape.test(path) && !path.includes('//');
}

function parseItem(raw: unknown): MenuItem[] {
  if (raw === null || typeof raw !== 'object') {
    return [];
  }
  const { id, pageKey, label, icon, path } = raw as Record<string, unknown>;
  if (typeof id !== 'string' || id.length === 0 || typeof label !== 'string' || label.trim().length === 0 || !isSafePath(path)) {
    return [];
  }
  return [{ id, pageKey: typeof pageKey === 'string' ? pageKey : '', label, icon: typeof icon === 'string' ? icon : 'circle', path }];
}

/**
 * The Site admin's sidebar when the server could not send it, so the person can still get around. Only the Site admin
 * app uses it; a member without a menu has no way to know which pages they may open. The old app's fallback also held
 * the menu and permission editors, which this app gets when they are ported.
 */
export const adminFallbackItems: readonly MenuItem[] = Object.freeze([
  Object.freeze({ id: 'fallback-dashboard', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' }),
]);

/**
 * The menu response as the page uses it. An item the page could not draw safely is dropped; permissions that are
 * not text are dropped; anything else missing leaves an empty list.
 */
export function parseMenu(body: unknown): Menu {
  const { items, permissions } = (body ?? {}) as { items?: unknown; permissions?: unknown };
  return {
    items: Array.isArray(items) ? items.flatMap(parseItem) : [],
    permissions: Array.isArray(permissions) ? permissions.filter((permission): permission is string => typeof permission === 'string') : [],
  };
}

/**
 * Whether the menu item at `path` is the one for the page open at `pathname`. The page itself, or a page below it (the Content Studio
 * editor at /content-studio/new belongs to the Content Studio item), unless another item of the menu claims that address more closely
 * (the Site admin's /admin/users is the Users item, not Dashboard at /admin).
 */
export function isCurrentPath(path: string, pathname: string, itemPaths: readonly string[]): boolean {
  if (pathname === path) {
    return true;
  }
  if (!pathname.startsWith(`${path}/`)) {
    return false;
  }
  return !itemPaths.some((other) => other.length > path.length && (pathname === other || pathname.startsWith(`${other}/`)));
}
