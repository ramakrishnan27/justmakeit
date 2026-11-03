// The signed-in state of the page. The sign-in itself is an HttpOnly cookie the browser holds and sends, so this
// store has no token: it holds only what the server says about the person (`me`) and is rebuilt after a reload by
// asking `me` again. Nothing about the session is ever written to storage.
//
// There is no refresh. A sign-in lasts 30 days; when a request comes back 401 (it ran out, or the Site admin changed
// a permission and every older token is refused) the store becomes signed out and the page shows the sign-in form.
//
// A signed-in session also holds the sidebar and the permissions of the person, read once when the person signs in
// or the page restores them. Moving between pages reads the copy in memory, never the server.

import { adminFallbackItems, parseMenu } from './menu.js';

const signedOutState = { status: 'signedOut', user: null, role: null };

/** The site admin app: its own sign-in, its own endpoints, and a session that is just a user and a role. */
export const siteAdminEndpoints = {
  login: '/v1/site/login',
  logout: '/v1/site/logout',
  me: '/v1/site/me',
  menu: '/v1/site/menu',
  register: null,
  // If the sidebar cannot be had, a site admin still gets the pages that repair it.
  fallbackMenu: adminFallbackItems,
  toState: (me) => ({ user: me.user, role: me.role }),
};

/** The member app: a role inside one organization, with the organization (and what kind it is) and the other memberships. */
export const memberEndpoints = {
  login: '/v1/auth/login',
  logout: '/v1/auth/logout',
  me: '/v1/auth/me',
  menu: '/v1/menu',
  register: '/v1/auth/register',
  // No fallback: without the permissions the page cannot tell which pages the person may open, so it does not guess.
  fallbackMenu: null,
  toState: (me) => ({ user: me.user, role: me.role, organization: me.agency, memberships: me.memberships }),
};

/**
 * @param {ReturnType<import('./api.js').createApiClient>} api
 * @param {typeof siteAdminEndpoints} [endpoints] Which app's endpoints to use; the site admin app by default.
 */
export function createSession(api, endpoints = siteAdminEndpoints) {
  let state = { status: 'unknown', user: null, role: null };
  const listeners = new Set();
  let reloading = null;

  function setState(next) {
    state = next;
    listeners.forEach((listener) => listener(state));
  }

  async function readMenu() {
    return parseMenu(await api.request(endpoints.menu));
  }

  // The menu a person starts with. The member app has no way to carry on without it; the site admin app falls back to
  // the pages that repair a menu, so a broken one can always be reached.
  async function readMenuToStart() {
    try {
      const menu = await readMenu();
      return endpoints.fallbackMenu && menu.items.length === 0 ? { items: [...endpoints.fallbackMenu], permissions: menu.permissions } : menu;
    } catch (error) {
      if (endpoints.fallbackMenu) {
        return { items: [...endpoints.fallbackMenu], permissions: [] };
      }
      throw error;
    }
  }

  // Who is signed in, then their menu. One after the other: with no session the first answer is 401 and a second
  // request would only repeat it.
  async function loadMe() {
    const me = await api.request(endpoints.me);
    const menu = await readMenuToStart();
    setState({ status: 'signedIn', ...endpoints.toState(me), menu: menu.items, permissions: menu.permissions, expiresAt: me.sessionExpiresAt ?? null });
  }

  async function endSession() {
    try {
      await api.request(endpoints.logout, { method: 'POST', auth: false });
    } catch {
      // The server could not be reached. The caller still forgets the session.
    }
  }

  // Sign-in and sign-up both end the same way: the server sets the cookie, then the user, role and menu come from the API.
  async function start(path, body) {
    await api.request(path, { method: 'POST', body, auth: false });
    try {
      await loadMe();
    } catch (error) {
      // The cookie is set but the page cannot go on (a member whose menu cannot be read): do not leave a session behind
      // that the next reload would restore into the same failure.
      await endSession();
      throw error;
    }
  }

  // Reads the menu again. If that fails the person keeps the one they have: a failed read of the menu is not a
  // reason to take it away, and a session that really ended is announced by the request itself.
  async function reloadMenu() {
    if (state.status !== 'signedIn') {
      return;
    }
    let menu;
    try {
      menu = await readMenuToStart();
    } catch {
      return;
    }
    if (state.status === 'signedIn') {
      setState({ ...state, menu: menu.items, permissions: menu.permissions });
    }
  }

  // A request that failed with 401 means the sign-in is over: the user is signed out.
  api.onSessionExpired(() => {
    if (state.status === 'signedIn') {
      setState(signedOutState);
    }
  });

  const session = {
    get state() {
      return state;
    },

    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    /** Signs in. Throws the API's ApiError (for example 401 for a wrong password) and stays signed out. */
    async signIn(email, password) {
      await start(endpoints.login, { email, password });
    },

    /** Creates an account and signs in. Only the member app has one. Throws the API's ApiError (409 when the email is taken). */
    async register(details) {
      if (!endpoints.register) {
        throw new Error('This app has no sign-up.');
      }
      await start(endpoints.register, details);
    },

    /** Called once when the page starts: asks the server who the cookie, if there is one, signs in. */
    async restore() {
      try {
        await loadMe();
        return true;
      } catch {
        // No cookie, an expired one, or a server that cannot answer: the person is not signed in.
        setState(signedOutState);
        return false;
      }
    },

    /**
     * Reads the sidebar and the permissions again, for example after the Site admin edited the menu on this very
     * page. Callers that overlap share one read.
     */
    refreshMenu() {
      reloading ??= reloadMenu().finally(() => {
        reloading = null;
      });
      return reloading;
    },

    async signOut() {
      await endSession();
      setState(signedOutState);
    },
  };

  return session;
}
