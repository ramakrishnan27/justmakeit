// A small History API router with a sign-in guard. The decision of what to show is a pure function
// (resolveNavigation) so it can be tested without a browser; createRouter only wires it to the window.

// The page holds two small apps that never share a session: the site admin app under /admin, and the
// member app (sign in, create an account, dashboard) everywhere else. Each has its own routes.

export const routes = [
  { name: 'dashboard', path: '/admin', requiresAuth: true, element: 'admin-dashboard', title: 'Dashboard' },
  { name: 'login', path: '/admin/login', requiresAuth: false, guestOnly: true, element: 'admin-login', title: 'Sign in' },
];

export const homePath = '/admin';
export const loginPath = '/admin/login';

export const adminApp = { name: 'admin', label: 'Site admin', routes, homePath, loginPath };

export const memberRoutes = [
  { name: 'dashboard', path: '/dashboard', requiresAuth: true, element: 'member-dashboard', title: 'Dashboard' },
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
 * What the page should do for a path, given whether the user is signed in.
 * @returns {{ type: 'redirect', to: string } | { type: 'render', route: object } | { type: 'notFound' }}
 */
export function resolveNavigation({ pathname, status, app = adminApp, table = app.routes }) {
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

  return { type: 'render', route };
}

/**
 * @param {{ win: Window, session: { state: { status: string }, subscribe: (fn: () => void) => void }, render: (decision: object) => void, app?: typeof adminApp }} deps
 */
export function createRouter({ win, session, render, app = adminApp }) {
  let lastStatus = session.state.status;

  function resolve() {
    const decision = resolveNavigation({ pathname: win.location.pathname, status: session.state.status, app });
    if (decision.type === 'redirect') {
      // Replace, so Back does not return to a page the guard just turned away.
      win.history.replaceState({}, '', decision.to);
      resolve();
      return;
    }
    render(decision);
  }

  function navigate(path, { replace = false } = {}) {
    win.history[replace ? 'replaceState' : 'pushState']({}, '', path);
    resolve();
  }

  win.addEventListener('popstate', resolve);

  // Signing in or out moves the user to the right page by itself.
  session.subscribe((state) => {
    if (state.status !== lastStatus) {
      lastStatus = state.status;
      resolve();
    }
  });

  return { start: resolve, navigate };
}
