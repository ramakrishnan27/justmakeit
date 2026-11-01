import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createRouter, homePath, loginPath, matchRoute, normalizePath, resolveNavigation, routes } from '../js/core/router.js';

describe('normalizePath', () => {
  it('drops trailing slashes but keeps the root', () => {
    assert.equal(normalizePath('/admin/'), '/admin');
    assert.equal(normalizePath('/admin///'), '/admin');
    assert.equal(normalizePath('/admin/login/'), '/admin/login');
    assert.equal(normalizePath('/'), '/');
    assert.equal(normalizePath(''), '/');
    assert.equal(normalizePath(undefined), '/');
  });
});

describe('matchRoute', () => {
  it('finds the route for a path, with or without a trailing slash', () => {
    assert.equal(matchRoute('/admin').name, 'dashboard');
    assert.equal(matchRoute('/admin/').name, 'dashboard');
    assert.equal(matchRoute('/admin/login').name, 'login');
  });

  it('returns null for an unknown path', () => {
    assert.equal(matchRoute('/admin/nothing'), null);
    assert.equal(matchRoute('/v1/site/overview'), null);
    assert.equal(matchRoute('/ADMIN'), null);
  });

  it('declares exactly one public route: the sign-in page', () => {
    assert.deepEqual(routes.filter((route) => !route.requiresAuth).map((route) => route.name), ['login']);
  });
});

describe('resolveNavigation', () => {
  const decide = (pathname, status) => resolveNavigation({ pathname, status });

  it('sends the site root to the dashboard', () => {
    assert.deepEqual(decide('/', 'signedIn'), { type: 'redirect', to: homePath });
    assert.deepEqual(decide('/', 'signedOut'), { type: 'redirect', to: homePath });
  });

  it('shows the dashboard to a signed-in user', () => {
    const decision = decide('/admin', 'signedIn');
    assert.equal(decision.type, 'render');
    assert.equal(decision.route.element, 'admin-dashboard');
  });

  it('turns a visitor who is not signed in away from the dashboard', () => {
    assert.deepEqual(decide('/admin', 'signedOut'), { type: 'redirect', to: loginPath });
    assert.deepEqual(decide('/admin', 'unknown'), { type: 'redirect', to: loginPath });
  });

  it('shows the sign-in page to a visitor who is not signed in', () => {
    for (const status of ['signedOut', 'unknown']) {
      const decision = decide('/admin/login', status);
      assert.equal(decision.type, 'render');
      assert.equal(decision.route.element, 'admin-login');
    }
  });

  it('sends a signed-in user from the sign-in page to the dashboard', () => {
    assert.deepEqual(decide('/admin/login', 'signedIn'), { type: 'redirect', to: homePath });
  });

  it('reports a path with no route as not found, signed in or not', () => {
    assert.deepEqual(decide('/admin/missing', 'signedIn'), { type: 'notFound' });
    assert.deepEqual(decide('/admin/missing', 'signedOut'), { type: 'notFound' });
  });
});

/** A window with just enough history and events for the router. */
function fakeWindow(pathname) {
  const listeners = {};
  const win = {
    location: { pathname },
    history: {
      entries: [pathname],
      pushState: (_state, _title, path) => {
        win.location.pathname = path;
        win.history.entries.push(path);
      },
      replaceState: (_state, _title, path) => {
        win.location.pathname = path;
        win.history.entries[win.history.entries.length - 1] = path;
      },
    },
    addEventListener: (type, listener) => {
      listeners[type] = listener;
    },
    fire: (type) => listeners[type](),
  };
  return win;
}

function fakeSession(status) {
  const listeners = [];
  return {
    state: { status },
    subscribe: (listener) => listeners.push(listener),
    set(next) {
      this.state = { status: next };
      listeners.forEach((listener) => listener(this.state));
    },
  };
}

describe('createRouter', () => {
  function setup(pathname, status) {
    const win = fakeWindow(pathname);
    const session = fakeSession(status);
    const rendered = [];
    const router = createRouter({ win, session, render: (decision) => rendered.push(decision) });
    return { win, session, rendered, router };
  }

  it('renders the route for the current path when it starts', () => {
    const { rendered, router } = setup('/admin', 'signedIn');

    router.start();

    assert.equal(rendered.length, 1);
    assert.equal(rendered[0].route.name, 'dashboard');
  });

  it('replaces the address when the guard redirects, so Back does not return to the refused page', () => {
    const { win, rendered, router } = setup('/admin', 'signedOut');

    router.start();

    assert.equal(win.location.pathname, loginPath);
    assert.deepEqual(win.history.entries, [loginPath]);
    assert.equal(rendered.length, 1);
    assert.equal(rendered[0].route.name, 'login');
  });

  it('adds a history entry when navigating, and renders there', () => {
    const { win, rendered, router } = setup('/admin/login', 'signedOut');
    router.start();

    router.navigate('/admin/missing');

    assert.equal(win.history.entries.at(-1), '/admin/missing');
    assert.equal(rendered.at(-1).type, 'notFound');
  });

  it('can navigate without adding a history entry', () => {
    const { win, router } = setup('/admin/login', 'signedOut');
    router.start();

    router.navigate('/admin/missing', { replace: true });

    assert.deepEqual(win.history.entries, ['/admin/missing']);
  });

  it('renders again when the user goes Back or Forward', () => {
    const { win, rendered, router } = setup('/admin/login', 'signedOut');
    router.start();
    win.location.pathname = '/admin/missing';

    win.fire('popstate');

    assert.equal(rendered.at(-1).type, 'notFound');
  });

  it('moves from the sign-in page to the dashboard when the user signs in', () => {
    const { win, session, rendered, router } = setup('/admin/login', 'signedOut');
    router.start();

    session.set('signedIn');

    assert.equal(win.location.pathname, homePath);
    assert.equal(rendered.at(-1).route.name, 'dashboard');
  });

  it('moves from the dashboard to the sign-in page when the user signs out or the session ends', () => {
    const { win, session, rendered, router } = setup('/admin', 'signedIn');
    router.start();

    session.set('signedOut');

    assert.equal(win.location.pathname, loginPath);
    assert.equal(rendered.at(-1).route.name, 'login');
  });

  it('does not render again for a state change that is not a change of status', () => {
    const { session, rendered, router } = setup('/admin', 'signedIn');
    router.start();

    session.set('signedIn');

    assert.equal(rendered.length, 1);
  });

  it('settles after a redirect instead of looping', () => {
    const { rendered, router } = setup('/', 'signedOut');

    router.start();

    assert.equal(rendered.length, 1);
    assert.equal(rendered[0].route.name, 'login');
  });
});
