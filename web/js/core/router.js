// A small History API router with a sign-in guard and a permission guard. The decision of what to show is a pure
// function (resolveNavigation) so it can be tested without a browser; createRouter only wires it to the window.

import { hasPermission, permissionKeys } from './permission-keys.js';

// The page holds two small apps that never share a session: the site admin app under /admin, and the
// member app (sign in, create an account, the pages behind the sidebar) everywhere else. Each has its own routes.
//
// A route may name the `permission` a person needs to open it. It is the same permission the page's own API calls
// need, so the client turns the person away before the server would. The server still checks every request.

export const routes = [
  { name: 'dashboard', path: '/admin', requiresAuth: true, element: 'admin-dashboard', title: 'Dashboard' },
  { name: 'menus', path: '/admin/menus', requiresAuth: true, element: 'admin-menus', title: 'Menus' },
  { name: 'permissions', path: '/admin/permissions', requiresAuth: true, element: 'admin-permissions', title: 'Permissions' },
  { name: 'login', path: '/admin/login', requiresAuth: false, guestOnly: true, element: 'admin-login', title: 'Sign in' },
];

export const homePath = '/admin';
export const loginPath = '/admin/login';

export const adminApp = { name: 'admin', label: 'Site admin', routes, homePath, loginPath };

export const memberRoutes = [
  {
    name: 'dashboard',
    path: '/dashboard',
    requiresAuth: true,
    permission: permissionKeys.dashboardView,
    element: 'member-dashboard',
    title: 'Dashboard',
  },
  {
    name: 'workspaces',
    path: '/workspaces',
    requiresAuth: true,
    permission: permissionKeys.workspacesView,
    element: 'member-workspaces',
    title: 'Workspaces',
  },
  { name: 'login', path: '/login', requiresAuth: false, guestOnly: true, element: 'member-login', title: 'Sign in' },
  { name: 'register', path: '/register', requiresAuth: false, guestOnly: true, element: 'member-register', title: 'Create account' },
];

export const memberApp = { name: 'member', label: 'Platform', routes: memberRoutes, homePath: '/dashboard', loginPath: '/login' };

/** Which app a path belongs to: /admin and everything under it is the site admin app, anything else the member app. */
export function appForPath(pathname) {
  const path = normalizePath(pathname);
  return path === '/admin' || path.startsWith('/admin/') ? adminApp : memberApp;
}

/** Drops a trailing slash so /admin/ and /admin are the same route. */
export function normalizePath(pathname) {
  const path = typeof pathname === 'string' && pathname.length > 0 ? pathname : '/';
  return path.length > 1 ? path.replace(/\/+$/, '') || '/' : path;
}

export function matchRoute(pathname, table = routes) {
  const path = normalizePath(pathname);
  return table.find((route) => route.path === path) ?? null;
}

/**
 * What the page should do for a path, given whether the user is signed in and which permissions they hold.
 * A route that needs a permission is forbidden unless the list holds it; a list that is missing holds nothing.
 * @returns {{ type: 'redirect', to: string } | { type: 'render', route: object } | { type: 'forbidden', route: object } | { type: 'notFound' }}
 */
export function resolveNavigation({ pathname, status, app = adminApp, table = app.routes, permissions = null }) {
  if (normalizePath(pathname) === '/') {
    return { type: 'redirect', to: app.homePath };
  }

  const route = matchRoute(pathname, table);
  if (!route) {
    return { type: 'notFound' };
  }

  const signedIn = status === 'signedIn';
  if (route.requiresAuth && !signedIn) {
    return { type: 'redirect', to: app.loginPath };
  }

  // A signed-in user has no use for the sign-in or sign-up pages.
  if (route.guestOnly && signedIn) {
    return { type: 'redirect', to: app.homePath };
  }

  if (route.permission && !hasPermission(permissions, route.permission)) {
    return { type: 'forbidden', route };
  }

  return { type: 'render', route };
}

// What a decision shows, as text, so two decisions can be compared.
function describe(decision) {
  return `${decision.type}:${decision.route?.name ?? decision.to ?? ''}`;
}

function permissionsKey(permissions) {
  return Array.isArray(permissions) ? [...permissions].sort().join(' ') : '';
}

/**
 * @param {{ win: Window, session: { state: { status: string, permissions?: string[] }, subscribe: (fn: (state: object) => void) => void }, render: (decision: object) => void, app?: typeof adminApp }} deps
 */
export function createRouter({ win, session, render, app = adminApp }) {
  let lastStatus = session.state.status;
  let lastPermissions = permissionsKey(session.state.permissions);
  let shown = null;

  function decide() {
    return resolveNavigation({
      pathname: win.location.pathname,
      status: session.state.status,
      app,
      permissions: session.state.permissions,
    });
  }

  function resolve() {
    const decision = decide();
    if (decision.type === 'redirect') {
      // Replace, so Back does not return to a page the guard just turned away.
      win.history.replaceState({}, '', decision.to);
      resolve();
      return;
    }
    shown = describe(decision);
    render(decision);
  }

  function navigate(path, { replace = false } = {}) {
    win.history[replace ? 'replaceState' : 'pushState']({}, '', path);
    resolve();
  }

  win.addEventListener('popstate', resolve);

  session.subscribe((state) => {
    // Signing in or out moves the user to the right page by itself.
    if (state.status !== lastStatus) {
      lastStatus = state.status;
      lastPermissions = permissionsKey(state.permissions);
      resolve();
      return;
    }

    // A change of permissions (the menu was read again) matters only when it changes what this very address shows: a
    // page the person just lost becomes "no access", one they just gained opens. Otherwise the page is left alone,
    // so nothing they are typing is thrown away.
    const key = permissionsKey(state.permissions);
    if (key !== lastPermissions) {
      lastPermissions = key;
      if (describe(decide()) !== shown) {
        resolve();
      }
    }
  });

  return { start: resolve, navigate };
}
