// The signed-in state of the page. The sign-in itself is an HttpOnly cookie the browser holds and sends, so this store
// has no token: it holds only what the server says about the person (`me`) and is rebuilt after a reload by asking
// `me` again. Nothing about the session is ever written to storage.
//
// There is no refresh. A sign-in lasts 30 days; when a request comes back 401 (it ran out, or the Site admin changed a
// permission and every older token is refused) the store becomes signed out and the page shows the sign-in form.
//
// A signed-in session also holds the sidebar and the permissions of the person, read once when the person signs in or
// the page restores them. Moving between pages reads the copy in memory, never the server.
//
// There are two apps and they never share a session: the member app (a role inside one organization) and the Site
// admin app (a platform-wide account). Each has its own endpoints and its own cookie, and the page starts the
// session of the app its address belongs to (see main.tsx).
//
// This is a plain store, not a React component, so its rules are tested without a browser. React reads it through
// useSession() in SessionContext.tsx.

import type { ApiClient } from '@/api/client';
import type { AcceptInvitationDetails, MeResponse, Membership, Organization, OrganizationRole, RegisterDetails, SiteMeResponse, User } from '@/api/types';
import { adminFallbackItems, parseMenu, type Menu, type MenuItem } from '@/navigation/menu';

/** A role inside one organization, with the organization (and what kind it is) and every membership of the person. */
export interface MemberProfile {
  kind: 'member';
  user: User;
  role: OrganizationRole;
  organization: Organization;
  memberships: Membership[];
}

/** The platform-wide Site admin: just a user and a role. Their token carries no organization and no permissions. */
export interface AdminProfile {
  kind: 'admin';
  user: User;
  role: 'siteadmin';
}

export type Profile = MemberProfile | AdminProfile;

interface Loaded {
  status: 'signedIn';
  menu: MenuItem[];
  permissions: string[];
  /** When the sign-in ends and the person has to sign in again, as an ISO date. Null when the server did not say. */
  expiresAt: string | null;
}

export interface MemberState extends MemberProfile, Loaded {}
export interface AdminState extends AdminProfile, Loaded {}
export type SignedInState = MemberState | AdminState;

export type SessionState = { status: 'unknown' } | { status: 'signedOut' } | SignedInState;

export interface Session {
  getState(): SessionState;
  subscribe(listener: () => void): () => void;
  /** Signs in. Throws the API's ApiError (for example 401 for a wrong password) and stays signed out. */
  signIn(email: string, password: string): Promise<void>;
  /** Creates an account and signs in. Only the member app has one. Throws the API's ApiError (409 when the email is taken). */
  register(details: RegisterDetails): Promise<void>;
  /** Accepts an invitation (the person chooses a name and password) and signs them in. Only the member app has invitations. Throws the API's ApiError. */
  acceptInvitation(details: AcceptInvitationDetails): Promise<void>;
  /** Called once when the page starts: asks the server who the cookie, if there is one, signs in. */
  restore(): Promise<boolean>;
  /** Reads the sidebar and the permissions again. Callers that overlap share one read. */
  refreshMenu(): Promise<void>;
  signOut(): Promise<void>;
}

/** What differs between the two apps: where they talk to, and how the answer of `me` becomes a profile. */
export interface SessionEndpoints {
  login: string;
  logout: string;
  me: string;
  menu: string;
  /** Null when the app has no sign-up. */
  register: string | null;
  /** Null when the app has no invitations. */
  acceptInvitation: string | null;
  /**
   * The sidebar to use when the server cannot send one. Null: do not guess. A member without permissions cannot tell
   * which pages are theirs, so a menu that cannot be read stops the sign-in.
   */
  fallbackMenu: readonly MenuItem[] | null;
  toProfile(me: unknown): Profile;
}

export const memberEndpoints: SessionEndpoints = {
  login: '/v1/auth/login',
  logout: '/v1/auth/logout',
  me: '/v1/auth/me',
  menu: '/v1/menu',
  register: '/v1/auth/register',
  acceptInvitation: '/v1/auth/invitations/accept',
  fallbackMenu: null,
  toProfile: (me) => {
    const { user, role, agency, memberships } = me as MeResponse;
    return { kind: 'member', user, role, organization: agency, memberships };
  },
};

export const siteAdminEndpoints: SessionEndpoints = {
  login: '/v1/site/login',
  logout: '/v1/site/logout',
  me: '/v1/site/me',
  menu: '/v1/site/menu',
  register: null,
  acceptInvitation: null,
  fallbackMenu: adminFallbackItems,
  toProfile: (me) => ({ kind: 'admin', user: (me as SiteMeResponse).user, role: 'siteadmin' }),
};

const unknownState: SessionState = { status: 'unknown' };
const signedOutState: SessionState = { status: 'signedOut' };

export function createSession(api: ApiClient, endpoints: SessionEndpoints = memberEndpoints): Session {
  let state: SessionState = unknownState;
  const listeners = new Set<() => void>();
  let reloading: Promise<void> | null = null;
  let restoring: Promise<boolean> | null = null;

  function setState(next: SessionState) {
    state = next;
    listeners.forEach((listener) => listener());
  }

  async function readMenu(): Promise<Menu> {
    return parseMenu(await api.request(endpoints.menu));
  }

  // The menu a person starts with. The member app has no way to carry on without it; the Site admin app falls back to
  // the pages it can always show, so a broken menu never locks the Site admin out.
  async function readMenuToStart(): Promise<Menu> {
    const { fallbackMenu } = endpoints;
    try {
      const menu = await readMenu();
      return fallbackMenu && menu.items.length === 0 ? { items: [...fallbackMenu], permissions: menu.permissions } : menu;
    } catch (error) {
      if (fallbackMenu) {
        return { items: [...fallbackMenu], permissions: [] };
      }
      throw error;
    }
  }

  // Who is signed in, then their menu. One after the other: with no session the first answer is 401 and a second
  // request would only repeat it.
  async function loadMe() {
    const me = await api.request(endpoints.me);
    const menu = await readMenuToStart();
    const expiresAt = (me as { sessionExpiresAt?: string | null }).sessionExpiresAt ?? null;
    setState({ status: 'signedIn', ...endpoints.toProfile(me), menu: menu.items, permissions: menu.permissions, expiresAt });
  }

  // Sign-in, sign-up and accepting an invitation all end the same way: the server sets the cookie, then the user, role and menu come from the API.
  async function start(path: string, body: unknown) {
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

  async function endSession() {
    try {
      await api.request(endpoints.logout, { method: 'POST', auth: false });
    } catch {
      // The server could not be reached. The caller still forgets the session.
    }
  }

  // Reads the menu again. If that fails the person keeps the one they have: a failed read of the menu is not a
  // reason to take it away, and a session that really ended is announced by the request itself.
  async function reloadMenu() {
    if (state.status !== 'signedIn') {
      return;
    }
    let menu: Menu;
    try {
      menu = await readMenuToStart();
    } catch {
      return;
    }
    // Read the state again: the person may have signed out while the menu was on its way.
    const current = state;
    if (current.status === 'signedIn') {
      setState({ ...current, menu: menu.items, permissions: menu.permissions });
    }
  }

  // A request that failed with 401 means the sign-in is over (it ran out, or permissions changed): the user is signed out.
  api.onSessionExpired(() => {
    if (state.status === 'signedIn') {
      setState(signedOutState);
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

    async signIn(email, password) {
      await start(endpoints.login, { email, password });
    },

    async register(details) {
      if (!endpoints.register) {
        throw new Error('This app has no sign-up.');
      }
      await start(endpoints.register, details);
    },

    async acceptInvitation(details) {
      if (!endpoints.acceptInvitation) {
        throw new Error('This app has no invitations.');
      }
      await start(endpoints.acceptInvitation, details);
    },

    // Callers that overlap share one attempt (React may start the page twice in development).
    restore() {
      restoring ??= (async () => {
        try {
          await loadMe();
          return true;
        } catch {
          // No cookie, an expired one, or a server that cannot answer: the person is not signed in.
          setState(signedOutState);
          return false;
        }
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
      await endSession();
      setState(signedOutState);
    },
  };

  return session;
}
