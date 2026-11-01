// The signed-in state of the page. It lives in memory only: reloading the page asks the API to
// restore it from the refresh cookie, so nothing about the session is ever written to storage.

import { decodeJwtPayload } from './jwt.js';

const signedOutState = { status: 'signedOut', user: null, role: null };

/** The site admin app: its own sign-in, its own endpoints, and a session that is just a user and a role. */
export const siteAdminEndpoints = {
  login: '/v1/site/login',
  logout: '/v1/site/logout',
  me: '/v1/site/me',
  register: null,
  toState: (me) => ({ user: me.user, role: me.role }),
};

/** The member app: a role inside one organization, with the organization (and what kind it is) and the other memberships. */
export const memberEndpoints = {
  login: '/v1/auth/login',
  logout: '/v1/auth/logout',
  me: '/v1/auth/me',
  register: '/v1/auth/register',
  toState: (me) => ({ user: me.user, role: me.role, organization: me.agency, memberships: me.memberships }),
};

/**
 * @param {ReturnType<import('./api.js').createApiClient>} api
 * @param {typeof siteAdminEndpoints} [endpoints] Which app's endpoints to use; the site admin app by default.
 */
export function createSession(api, endpoints = siteAdminEndpoints) {
  let state = { status: 'unknown', user: null, role: null };
  const listeners = new Set();

  function setState(next) {
    state = next;
    listeners.forEach((listener) => listener(state));
  }

  async function loadMe() {
    const me = await api.request(endpoints.me);
    setState({ status: 'signedIn', ...endpoints.toState(me) });
  }

  // Sign-in and sign-up both end the same way: an access token in memory, then the user and role from the API.
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

  // A request that failed with 401 and could not renew the session means the user is signed out.
  api.onSessionExpired(() => {
    if (state.status === 'signedIn') {
      setState(signedOutState);
    }
  });

  return {
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
}
