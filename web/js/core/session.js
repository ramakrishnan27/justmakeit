// The signed-in state of the page. It lives in memory only: reloading the page asks the API to
// restore it from the refresh cookie, so nothing about the session is ever written to storage.

import { decodeJwtPayload } from './jwt.js';

const signedOutState = { status: 'signedOut', user: null, role: null };

/**
 * @param {ReturnType<import('./api.js').createApiClient>} api
 */
export function createSession(api) {
  let state = { status: 'unknown', user: null, role: null };
  const listeners = new Set();

  function setState(next) {
    state = next;
    listeners.forEach((listener) => listener(state));
  }

  async function loadMe() {
    const me = await api.request('/v1/site/me');
    setState({ status: 'signedIn', user: me.user, role: me.role });
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
      const tokens = await api.request('/v1/site/login', { method: 'POST', body: { email, password }, auth: false });
      api.setAccessToken(tokens.accessToken);
      try {
        await loadMe();
      } catch (error) {
        api.setAccessToken(null);
        throw error;
      }
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
        await api.request('/v1/site/logout', { method: 'POST', auth: false });
      } catch {
        // The server could not be reached. The page still forgets the session below.
      }
      api.setAccessToken(null);
      setState(signedOutState);
    },
  };
}
