// A small History API router with a sign-in guard. The decision of what to show is a pure function
// (resolveNavigation) so it can be tested without a browser; createRouter only wires it to the window.

export const routes = [
  { name: 'dashboard', path: '/admin', requiresAuth: true, element: 'admin-dashboard', title: 'Dashboard' },
  { name: 'login', path: '/admin/login', requiresAuth: false, element: 'admin-login', title: 'Sign in' },
];

export const homePath = '/admin';
export const loginPath = '/admin/login';

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
export function resolveNavigation({ pathname, status, table = routes }) {
  if (normalizePath(pathname) === '/') {
    return { type: 'redirect', to: homePath };
  }

  const route = matchRoute(pathname, table);
  if (!route) {
    return { type: 'notFound' };
  }

  const signedIn = status === 'signedIn';
  if (route.requiresAuth && !signedIn) {
    return { type: 'redirect', to: loginPath };
  }

  // A signed-in user has no use for the sign-in page.
  if (route.name === 'login' && signedIn) {
    return { type: 'redirect', to: homePath };
  }

  return { type: 'render', route };
}

/**
 * @param {{ win: Window, session: { state: { status: string }, subscribe: (fn: () => void) => void }, render: (decision: object) => void }} deps
 */
export function createRouter({ win, session, render }) {
  let lastStatus = session.state.status;

  function resolve() {
    const decision = resolveNavigation({ pathname: win.location.pathname, status: session.state.status });
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
