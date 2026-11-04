import { describe, expect, it } from 'vitest';
import { ApiError, createApiClient } from '@/api/client';
import { jsonResponse, routedFetch, type Handler } from '@/test/helpers';
import { createSession, siteAdminEndpoints, type AdminState, type MemberState, type SignedInState } from './session';

const endsAt = '2026-12-05T12:00:00Z';

const me = {
  user: { id: 'u2', email: 'ana@example.com', fullName: 'Ana Smith' },
  agency: { id: 'a1', name: 'Northwind Foods', businessModel: 'company', plan: 'trial' },
  role: 'marketingmanager',
  memberships: [{ agencyId: 'a1', agencyName: 'Northwind Foods', businessModel: 'company', role: 'marketingmanager' }],
  sessionExpiresAt: endsAt,
};

const dashboard = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
const workspaces = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };
const bothPermissions = ['dashboard.view', 'workspaces.view'];

/** Lets work that a listener started without waiting for finish. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup(routes: Record<string, Handler> = {}) {
  const fetchFn = routedFetch({
    'POST /v1/auth/login': () => jsonResponse(200, { expiresAt: endsAt }),
    'POST /v1/auth/logout': () => new Response(null, { status: 204 }),
    'GET /v1/auth/me': () => jsonResponse(200, me),
    'GET /v1/menu': () => jsonResponse(200, { items: [dashboard, workspaces], permissions: bothPermissions }),
    ...routes,
  });
  const api = createApiClient({ fetchFn });
  return { fetchFn, api, session: createSession(api) };
}

const countOf = (fetchFn: { calls: { path: string }[] }, path: string) => fetchFn.calls.filter((call) => call.path === path).length;
const signedIn = (session: ReturnType<typeof createSession>) => session.getState() as SignedInState;
const asMember = (session: ReturnType<typeof createSession>) => session.getState() as MemberState;

describe('session', () => {
  it('starts with an unknown status', () => {
    expect(setup().session.getState()).toEqual({ status: 'unknown' });
  });

  describe('signIn', () => {
    it('logs in, then loads the user, role, organization, menu, permissions and when the sign-in ends', async () => {
      const { fetchFn, session } = setup();

      await session.signIn('ana@example.com', 'a password');

      const state = signedIn(session);
      expect(state.status).toBe('signedIn');
      expect(state.role).toBe('marketingmanager');
      expect(state.user).toEqual(me.user);
      expect(state.kind).toBe('member');
      expect(asMember(session).organization).toEqual(me.agency);
      expect(asMember(session).memberships).toEqual(me.memberships);
      expect(state.menu).toEqual([dashboard, workspaces]);
      expect(state.permissions).toEqual(bothPermissions);
      expect(state.expiresAt).toBe(endsAt);

      const [login] = fetchFn.calls;
      expect(JSON.parse(login!.init.body as string)).toEqual({ email: 'ana@example.com', password: 'a password' });
      expect(fetchFn.calls.map((call) => call.path)).toEqual(['/v1/auth/login', '/v1/auth/me', '/v1/menu']);
    });

    it('holds no token: nothing the page can read is the credential, and no call sends one', async () => {
      const { fetchFn, session } = setup();

      await session.signIn('ana@example.com', 'a password');

      for (const call of fetchFn.calls) {
        expect(Object.keys(call.headers).map((name) => name.toLowerCase()), call.path).not.toContain('authorization');
      }
      expect(JSON.stringify(session.getState())).not.toMatch(/token/i);
    });

    it('does not know when the sign-in ends if the server does not say', async () => {
      const { session } = setup({ 'GET /v1/auth/me': () => jsonResponse(200, { ...me, sessionExpiresAt: undefined }) });

      await session.signIn('ana@example.com', 'a password');

      expect(signedIn(session).expiresAt).toBeNull();
    });

    it('stays signed out when the credentials are wrong', async () => {
      const { session } = setup({
        'POST /v1/auth/login': () => jsonResponse(401, { detail: 'Email or password is incorrect.', code: 'invalid_credentials' }),
      });

      await expect(session.signIn('ana@example.com', 'wrong')).rejects.toBeInstanceOf(ApiError);

      expect(session.getState().status).toBe('unknown');
    });

    it('ends the session the server just began if the user cannot be loaded after signing in', async () => {
      const { fetchFn, session } = setup({ 'GET /v1/auth/me': () => jsonResponse(500, {}) });

      await expect(session.signIn('ana@example.com', 'a password')).rejects.toBeInstanceOf(ApiError);

      // The cookie was set by the login; without this a reload would restore a session the page cannot use.
      expect(fetchFn.calls.at(-1)!.path).toBe('/v1/auth/logout');
      expect(session.getState().status).not.toBe('signedIn');
    });

    it('tells subscribers about the change, and lets them stop listening', async () => {
      const { session } = setup();
      const seen: string[] = [];
      const stop = session.subscribe(() => seen.push(session.getState().status));

      await session.signIn('ana@example.com', 'a password');
      stop();
      await session.signOut();

      expect(seen).toEqual(['signedIn']);
    });
  });

  describe('register', () => {
    const details = { email: 'ana@example.com', password: 'ten chars!!', fullName: 'Ana Smith', organizationName: 'Northwind Foods', businessModel: 'company' } as const;

    it('creates an account and signs in with it, sending the organization and the kind of business', async () => {
      const { fetchFn, session } = setup({ 'POST /v1/auth/register': () => jsonResponse(201, { expiresAt: endsAt }) });

      await session.register(details);

      expect(session.getState().status).toBe('signedIn');
      const [register] = fetchFn.calls;
      expect(register!.path).toBe('/v1/auth/register');
      expect(JSON.parse(register!.init.body as string)).toEqual(details);
    });

    it('stays signed out when the email is taken', async () => {
      const { session } = setup({ 'POST /v1/auth/register': () => jsonResponse(409, { detail: 'An account with this email already exists.', code: 'email_taken' }) });

      await expect(session.register(details)).rejects.toMatchObject({ status: 409 });

      expect(session.getState().status).not.toBe('signedIn');
    });
  });

  describe('acceptInvitation', () => {
    const details = { token: 'abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE', fullName: 'Ana Smith', password: 'ten chars!!' };

    it('sends the secret and the chosen name and password in the body of a POST, then signs in', async () => {
      const { fetchFn, session } = setup({ 'POST /v1/auth/invitations/accept': () => jsonResponse(200, { expiresAt: endsAt }) });

      await session.acceptInvitation(details);

      expect(session.getState().status).toBe('signedIn');
      const [accept] = fetchFn.calls;
      expect(accept!.path).toBe('/v1/auth/invitations/accept');
      expect(accept!.init.method).toBe('POST');
      expect(JSON.parse(accept!.init.body as string)).toEqual(details);
      expect(fetchFn.calls.every((call) => !call.path.includes(details.token))).toBe(true);
    });

    it('stays signed out when the invitation is no longer valid', async () => {
      const { session } = setup({ 'POST /v1/auth/invitations/accept': () => jsonResponse(404, { code: 'invitation_invalid' }) });

      await expect(session.acceptInvitation(details)).rejects.toMatchObject({ status: 404, code: 'invitation_invalid' });

      expect(session.getState().status).not.toBe('signedIn');
    });

    it('takes the cookie away again when the invitation worked but the page cannot go on', async () => {
      const { fetchFn, session } = setup({
        'POST /v1/auth/invitations/accept': () => jsonResponse(200, { expiresAt: endsAt }),
        'GET /v1/menu': () => jsonResponse(500, {}),
      });

      await expect(session.acceptInvitation(details)).rejects.toBeInstanceOf(ApiError);

      expect(session.getState().status).not.toBe('signedIn');
      expect(fetchFn.calls.map((call) => call.path)).toContain('/v1/auth/logout');
    });
  });

  describe('restore', () => {
    it('asks the server who the cookie signs in when the page starts', async () => {
      const { fetchFn, session } = setup();

      expect(await session.restore()).toBe(true);

      expect(asMember(session).organization.businessModel).toBe('company');
      expect(signedIn(session).expiresAt).toBe(endsAt);
      expect(fetchFn.calls.map((call) => call.path)).toEqual(['/v1/auth/me', '/v1/menu']);
    });

    it('reads the menu once', async () => {
      const { fetchFn, session } = setup();

      await session.restore();
      await settle();

      expect(countOf(fetchFn, '/v1/menu')).toBe(1);
    });

    it('is signed out when there is no valid cookie, and asks nothing more than who is signed in', async () => {
      const { fetchFn, session } = setup({ 'GET /v1/auth/me': () => jsonResponse(401, { code: 'unauthenticated' }) });

      expect(await session.restore()).toBe(false);

      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(fetchFn.calls.map((call) => call.path)).toEqual(['/v1/auth/me']); // the menu is not asked for as well
    });

    it('is signed out when the cookie is for a sign-in the server no longer accepts', async () => {
      // 30 days ran out, the Site admin changed a permission, or the person left the organization.
      for (const code of ['permissions_changed', 'membership_ended']) {
        const { session } = setup({ 'GET /v1/auth/me': () => jsonResponse(401, { code }) });

        expect(await session.restore(), code).toBe(false);

        expect(session.getState(), code).toEqual({ status: 'signedOut' });
      }
    });

    it('is signed out when the user cannot be loaded', async () => {
      const { session } = setup({ 'GET /v1/auth/me': () => jsonResponse(403, {}) });

      expect(await session.restore()).toBe(false);

      expect(session.getState().status).toBe('signedOut');
    });

    it('makes one attempt for callers that overlap', async () => {
      const { fetchFn, session } = setup();

      await Promise.all([session.restore(), session.restore()]);

      expect(countOf(fetchFn, '/v1/auth/me')).toBe(1);
    });
  });

  describe('signOut', () => {
    it('asks the API to remove the cookie, then forgets everything', async () => {
      const { fetchFn, session } = setup();
      await session.signIn('ana@example.com', 'a password');

      await session.signOut();

      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(fetchFn.calls.at(-1)!.path).toBe('/v1/auth/logout');
    });

    it('forgets the session even when the server cannot be reached', async () => {
      const { session } = setup({
        'POST /v1/auth/logout': () => {
          throw new TypeError('network down');
        },
      });
      await session.signIn('ana@example.com', 'a password');

      await session.signOut();

      expect(session.getState().status).toBe('signedOut');
    });
  });

  describe('expiry', () => {
    it('becomes signed out when any request finds the sign-in is over, with no attempt to renew it', async () => {
      const { fetchFn, api, session } = setup({ 'GET /v1/data': () => jsonResponse(401, {}) });
      await session.signIn('ana@example.com', 'a password');
      const seen: string[] = [];
      session.subscribe(() => seen.push(session.getState().status));

      // The page and the session share one API client, so any page's request can end the session.
      await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

      expect(seen).toEqual(['signedOut']);
      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(fetchFn.calls.at(-1)!.path).toBe('/v1/data'); // nothing was asked for afterwards
    });

    it('becomes signed out the same way when the Site admin changed a permission', async () => {
      const { api, session } = setup({ 'GET /v1/data': () => jsonResponse(401, { code: 'permissions_changed' }) });
      await session.signIn('ana@example.com', 'a password');

      await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401, code: 'permissions_changed' });

      expect(session.getState()).toEqual({ status: 'signedOut' });
    });

    it('does not announce anything when the user was not signed in anyway', async () => {
      const { api, session } = setup({ 'GET /v1/data': () => jsonResponse(401, {}) });
      const seen: string[] = [];
      session.subscribe(() => seen.push(session.getState().status));

      await expect(api.request('/v1/data')).rejects.toBeInstanceOf(ApiError);

      expect(seen).toEqual([]);
      expect(session.getState().status).toBe('unknown');
    });
  });

  it('has no claims to read: the page never sees the token', () => {
    const session = setup().session as unknown as Record<string, unknown>;

    expect(session.getClaims).toBeUndefined();
  });
});

describe('the menu and the permissions in the session', () => {
  it('reads the menu once at sign-in, and not again as the page moves around', async () => {
    const { fetchFn, session } = setup();

    await session.signIn('ana@example.com', 'a password');
    for (let page = 0; page < 5; page += 1) {
      void signedIn(session).menu;
      void signedIn(session).permissions;
      await settle();
    }

    expect(countOf(fetchFn, '/v1/menu')).toBe(1);
  });

  it('does not read the menu again by itself: a change of permissions ends the session instead', async () => {
    const { fetchFn, api, session } = setup({ 'GET /v1/data': () => jsonResponse(401, { code: 'permissions_changed' }) });
    await session.signIn('ana@example.com', 'a password');

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });
    await settle();

    expect(countOf(fetchFn, '/v1/menu')).toBe(1);
  });

  describe('refreshMenu', () => {
    it('replaces the menu and the permissions, tells subscribers, and leaves the person signed in', async () => {
      let reads = 0;
      const { session } = setup({
        'GET /v1/menu': () => jsonResponse(200, reads++ === 0 ? { items: [dashboard], permissions: ['dashboard.view'] } : { items: [dashboard, workspaces], permissions: bothPermissions }),
      });
      await session.signIn('ana@example.com', 'a password');
      const seen: number[] = [];
      session.subscribe(() => seen.push(signedIn(session).menu.length));

      await session.refreshMenu();

      expect(seen).toEqual([2]);
      expect(signedIn(session).permissions).toEqual(bothPermissions);
      expect(signedIn(session).user.id).toBe('u2');
    });

    it('keeps the menu the person has when it cannot be read again', async () => {
      let reads = 0;
      const { session } = setup({
        'GET /v1/menu': () => (reads++ === 0 ? jsonResponse(200, { items: [dashboard], permissions: ['dashboard.view'] }) : jsonResponse(500, {})),
      });
      await session.signIn('ana@example.com', 'a password');

      await session.refreshMenu();

      expect(signedIn(session).menu).toEqual([dashboard]);
    });

    it('asks for nothing when nobody is signed in', async () => {
      const { fetchFn, session } = setup();

      await session.refreshMenu();

      expect(fetchFn.calls).toHaveLength(0);
    });

    it('lets callers who overlap share one read', async () => {
      const { fetchFn, session } = setup();
      await session.signIn('ana@example.com', 'a password');

      await Promise.all([session.refreshMenu(), session.refreshMenu(), session.refreshMenu()]);

      expect(countOf(fetchFn, '/v1/menu')).toBe(2); // the sign-in read and one shared read
    });

    it('does not bring a session back that ended while it was reading', async () => {
      let release: () => void = () => undefined;
      const gate = new Promise<void>((resolve) => {
        release = resolve;
      });
      let reads = 0;
      const { session } = setup({
        'GET /v1/menu': async () => {
          if (reads++ > 0) {
            await gate;
          }
          return jsonResponse(200, { items: [dashboard], permissions: ['dashboard.view'] });
        },
      });
      await session.signIn('ana@example.com', 'a password');

      const reading = session.refreshMenu();
      await session.signOut();
      release();
      await reading;

      expect(session.getState()).toEqual({ status: 'signedOut' });
    });
  });

  it('drops an item with an address that could leave the site or break the page', async () => {
    const { session } = setup({
      'GET /v1/menu': () =>
        jsonResponse(200, {
          items: [
            dashboard,
            { id: 'x1', label: 'Elsewhere', icon: 'home', path: 'https://evil.example/' },
            { id: 'x2', label: 'Same scheme', icon: 'home', path: '//evil.example' },
            { id: 'x3', label: 'Script', icon: 'home', path: 'javascript:alert(1)' },
            { id: 'x4', label: '', icon: 'home', path: '/blank' },
            null,
            'text',
            workspaces,
          ],
          permissions: ['dashboard.view', 7, null],
        }),
    });

    await session.signIn('ana@example.com', 'a password');

    expect(signedIn(session).menu).toEqual([dashboard, workspaces]);
    expect(signedIn(session).permissions).toEqual(['dashboard.view']);
  });

  it('stops a member from signing in when the menu cannot be had: without permissions the page cannot tell which pages are theirs', async () => {
    const { fetchFn, session } = setup({ 'GET /v1/menu': () => jsonResponse(500, {}) });

    await expect(session.signIn('ana@example.com', 'a password')).rejects.toBeInstanceOf(ApiError);

    expect(session.getState().status).not.toBe('signedIn');
    expect(fetchFn.calls.at(-1)!.path).toBe('/v1/auth/logout'); // and the cookie the login set is removed again
  });

  it('leaves a member signed out when restoring the page cannot read the menu', async () => {
    const { session } = setup({ 'GET /v1/menu': () => jsonResponse(500, {}) });

    expect(await session.restore()).toBe(false);

    expect(session.getState().status).toBe('signedOut');
  });
});

describe('the Site admin session', () => {
  const adminMe = { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin', sessionExpiresAt: endsAt };
  const adminItems = [
    { id: 'i1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' },
    { id: 'i2', pageKey: 'menus', label: 'Menus', icon: 'menu', path: '/admin/menus' },
  ];

  function adminSetup(routes: Record<string, Handler> = {}) {
    const fetchFn = routedFetch({
      'POST /v1/site/login': () => jsonResponse(200, { expiresAt: endsAt }),
      'POST /v1/site/logout': () => new Response(null, { status: 204 }),
      'GET /v1/site/me': () => jsonResponse(200, adminMe),
      'GET /v1/site/menu': () => jsonResponse(200, { items: adminItems, permissions: [] }),
      ...routes,
    });
    const api = createApiClient({ fetchFn });
    return { fetchFn, api, session: createSession(api, siteAdminEndpoints) };
  }

  const asAdmin = (session: ReturnType<typeof createSession>) => session.getState() as AdminState;

  it('signs in through the site endpoints, and is a user and a role with no organization and no permissions', async () => {
    const { fetchFn, session } = adminSetup();

    await session.signIn('root@example.com', 'a password');

    const state = asAdmin(session);
    expect(state).toMatchObject({ status: 'signedIn', kind: 'admin', role: 'siteadmin', user: adminMe.user, menu: adminItems, permissions: [], expiresAt: endsAt });
    expect('organization' in state).toBe(false);
    expect(fetchFn.calls.map((call) => call.path)).toEqual(['/v1/site/login', '/v1/site/me', '/v1/site/menu']);
  });

  it('restores from the Site admin cookie', async () => {
    const { fetchFn, session } = adminSetup();

    expect(await session.restore()).toBe(true);

    expect(asAdmin(session).kind).toBe('admin');
    expect(fetchFn.calls[0]!.path).toBe('/v1/site/me');
  });

  it('is signed out when the Site admin cookie is no good', async () => {
    const { session } = adminSetup({ 'GET /v1/site/me': () => jsonResponse(401, {}) });

    expect(await session.restore()).toBe(false);

    expect(session.getState()).toEqual({ status: 'signedOut' });
  });

  it('never calls the member endpoints: the two apps do not share a session', async () => {
    const { fetchFn, session } = adminSetup();

    await session.restore();
    await session.signOut();
    await session.signIn('root@example.com', 'a password');
    await session.signOut();

    expect(fetchFn.calls.length).toBeGreaterThan(0);
    expect(fetchFn.calls.every((call) => call.path.startsWith('/v1/site/'))).toBe(true);
  });

  it('stays signed out when the credentials are wrong', async () => {
    const { session } = adminSetup({ 'POST /v1/site/login': () => jsonResponse(401, { code: 'invalid_credentials' }) });

    await expect(session.signIn('root@example.com', 'wrong')).rejects.toMatchObject({ status: 401 });

    expect(session.getState().status).not.toBe('signedIn');
  });

  it('has no invitations either', async () => {
    const { session } = adminSetup();

    await expect(session.acceptInvitation({ token: 'abc', fullName: 'X', password: 'ten chars!!' })).rejects.toThrow(/no invitations/);
  });

  it('has no sign-up', async () => {
    const { session } = adminSetup();

    await expect(session.register({ email: 'a@b.co', password: 'ten chars!!', fullName: null, organizationName: 'X', businessModel: 'company' })).rejects.toThrow(/no sign-up/);
  });

  it('gives the Site admin the pages it can always show when the menu cannot be had, and signs them in all the same', async () => {
    const { session } = adminSetup({ 'GET /v1/site/menu': () => jsonResponse(500, {}) });

    await session.signIn('root@example.com', 'a password');

    expect(session.getState().status).toBe('signedIn');
    expect(signedIn(session).menu.map((item) => item.path)).toEqual(['/admin']);
    expect(signedIn(session).permissions).toEqual([]);
  });

  it('does the same when the menu came back empty', async () => {
    const { session } = adminSetup({ 'GET /v1/site/menu': () => jsonResponse(200, { items: [], permissions: [] }) });

    await session.signIn('root@example.com', 'a password');

    expect(signedIn(session).menu).toHaveLength(1);
  });

  it('keeps the fallback list from being changed by anyone who holds the session', async () => {
    const first = adminSetup({ 'GET /v1/site/menu': () => jsonResponse(500, {}) });
    await first.session.signIn('root@example.com', 'a password');

    signedIn(first.session).menu.push({ id: 'x', pageKey: '', label: 'Extra', icon: 'home', path: '/extra' });

    const again = adminSetup({ 'GET /v1/site/menu': () => jsonResponse(500, {}) });
    await again.session.signIn('root@example.com', 'a password');
    expect(signedIn(again.session).menu).toHaveLength(1);
  });
});
