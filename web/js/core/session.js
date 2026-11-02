// The signed-in state of the page. It lives in memory only: reloading the page asks the API to
// restore it from the refresh cookie, so nothing about the session is ever written to storage.
//
// A signed-in session also holds the sidebar and the permissions of the person. They are read once when the
// person signs in or the page restores them, and again each time the access token is renewed (which is when a
// change the Site admin made reaches them). Moving between pages reads the copy in memory, never the server.

import { decodeJwtPayload } from './jwt.js';
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

  async function loadMe() {
    const [me, menu] = await Promise.all([api.request(endpoints.me), readMenuToStart()]);
    setState({ status: 'signedIn', ...endpoints.toState(me), menu: menu.items, permissions: menu.permissions });
  }

  // Sign-in and sign-up both end the same way: an access token in memory, then the user, role and menu from the API.
  async function start(path, body) {
    const tokens = await api.request(path, { method: 'POST', body, auth: false });
    api.setAccessToken(tokens.accessToken);
    try {
      await loadMe();
    } catch (error) {
      api.setAccessToken(null);
      throw error;
    }
  }

  // Reads the menu again. If that fails the person keeps the one they have: a failed refresh of the menu is not a
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

  // A request that failed with 401 and could not renew the session means the user is signed out.
  api.onSessionExpired(() => {
    if (state.status === 'signedIn') {
      setState(signedOutState);
    }
  });

  // A renewed token carries the permissions as they are now, so what the person sees is read again with it. Not while
  // signing in or restoring (the status is not yet signedIn then; those read the menu themselves).
  api.onTokenRefreshed(() => {
    if (state.status === 'signedIn') {
      void session.refreshMenu();
    }
  });

  const session = {
    get state() {
      return state;
    },

    /** The claims of the current access token, for display. Null when signed out. */
    getClaims: () => decodeJwtPayload(api.getAccessToken()),

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

    /** Called once when the page starts: turns the refresh cookie, if there is one, back into a session. */
    async restore() {
      if (await api.refresh()) {
        try {
          await loadMe();
          return true;
        } catch {
          api.setAccessToken(null);
        }
      }
      setState(signedOutState);
      return false;
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
      try {
        await api.request(endpoints.logout, { method: 'POST', auth: false });
      } catch {
        // The server could not be reached. The page still forgets the session below.
      }
      api.setAccessToken(null);
      setState(signedOutState);
    },
  };

  return session;
}
