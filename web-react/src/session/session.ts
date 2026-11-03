// The signed-in state of the page. It lives in memory only: reloading the page asks the API to restore it from the
// refresh cookie, so nothing about the session is ever written to storage.
//
// A signed-in session also holds the sidebar and the permissions of the person. They are read once when the person
// signs in or the page restores them, and again each time the access token is renewed (which is when a change the
// Site admin made reaches them). Moving between pages reads the copy in memory, never the server.
//
// This is a plain store, not a React component, so its rules are tested without a browser. React reads it through
// useSession() in SessionContext.tsx.

import type { ApiClient } from '@/api/client';
import type { MeResponse, Membership, Organization, OrganizationRole, RegisterDetails, TokenResponse, User } from '@/api/types';
import { parseMenu, type Menu, type MenuItem } from '@/navigation/menu';
import { decodeJwtPayload } from './jwt';

export interface SignedInState {
  status: 'signedIn';
  user: User;
  role: OrganizationRole;
  organization: Organization;
  memberships: Membership[];
  menu: MenuItem[];
  permissions: string[];
}

export type SessionState = { status: 'unknown' } | { status: 'signedOut' } | SignedInState;

export interface Session {
  getState(): SessionState;
  subscribe(listener: () => void): () => void;
  /** The claims of the current access token, for display. Null when signed out. */
  getClaims(): Record<string, unknown> | null;
  /** Signs in. Throws the API's ApiError (for example 401 for a wrong password) and stays signed out. */
  signIn(email: string, password: string): Promise<void>;
  /** Creates an account and signs in. Throws the API's ApiError (409 when the email is taken). */
  register(details: RegisterDetails): Promise<void>;
  /** Called once when the page starts: turns the refresh cookie, if there is one, back into a session. */
  restore(): Promise<boolean>;
  /** Reads the sidebar and the permissions again. Callers that overlap share one read. */
  refreshMenu(): Promise<void>;
  signOut(): Promise<void>;
}

const unknownState: SessionState = { status: 'unknown' };
const signedOutState: SessionState = { status: 'signedOut' };

const endpoints = {
  login: '/v1/auth/login',
  logout: '/v1/auth/logout',
  me: '/v1/auth/me',
  menu: '/v1/menu',
  register: '/v1/auth/register',
};

export function createSession(api: ApiClient): Session {
  let state: SessionState = unknownState;
  const listeners = new Set<() => void>();
  let reloading: Promise<void> | null = null;
  let restoring: Promise<boolean> | null = null;

  function setState(next: SessionState) {
    state = next;
    listeners.forEach((listener) => listener());
  }

  // Without the permissions the page cannot tell which pages are the person's, so it does not guess:
  // a menu that cannot be read stops the sign-in.
  async function readMenu(): Promise<Menu> {
    return parseMenu(await api.request(endpoints.menu));
  }

  async function loadMe() {
    const [me, menu] = await Promise.all([api.request<MeResponse>(endpoints.me), readMenu()]);
    setState({
      status: 'signedIn',
      user: me.user,
      role: me.role,
      organization: me.agency,
      memberships: me.memberships,
      menu: menu.items,
      permissions: menu.permissions,
    });
  }

  // Sign-in and sign-up both end the same way: an access token in memory, then the user, role and menu from the API.
  async function start(path: string, body: unknown) {
    const tokens = await api.request<TokenResponse>(path, { method: 'POST', body, auth: false });
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
    let menu: Menu;
    try {
      menu = await readMenu();
    } catch {
      return;
    }
    // Read the state again: the person may have signed out while the menu was on its way.
    const current = state;
    if (current.status === 'signedIn') {
      setState({ ...current, menu: menu.items, permissions: menu.permissions });
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

  const session: Session = {
    getState: () => state,

    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },

    getClaims: () => decodeJwtPayload(api.getAccessToken()),

    async signIn(email, password) {
      await start(endpoints.login, { email, password });
    },

    async register(details) {
      await start(endpoints.register, details);
    },

    // Callers that overlap share one attempt: the refresh token rotates, so two attempts with one cookie would look
    // like a stolen token.
    restore() {
      restoring ??= (async () => {
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
      })().finally(() => {
        restoring = null;
      });
      return restoring;
    },

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
