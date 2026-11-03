import { describe, expect, it } from 'vitest';
import { ApiError, createApiClient } from '@/api/client';
import { jsonResponse, makeJwt, routedFetch, type Handler } from '@/test/helpers';
import { createSession, siteAdminEndpoints, type AdminState, type MemberState, type SignedInState } from './session';

const me = {
  user: { id: 'u2', email: 'ana@example.com', fullName: 'Ana Smith' },
  agency: { id: 'a1', name: 'Northwind Foods', businessModel: 'company', plan: 'trial' },
  role: 'marketingmanager',
  memberships: [{ agencyId: 'a1', agencyName: 'Northwind Foods', businessModel: 'company', role: 'marketingmanager' }],
};
const accessToken = makeJwt({ user_id: 'u2', agency_id: 'a1', role: 'marketingmanager', exp: 1_900_000_000 });

const dashboard = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
const workspaces = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };
const bothPermissions = ['dashboard.view', 'workspaces.view'];

/** Lets work that a listener started without waiting for finish. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup(routes: Record<string, Handler> = {}) {
  const fetchFn = routedFetch({
    'POST /v1/auth/login': () => jsonResponse(200, { accessToken }),
    'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken }),
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
    it('logs in without a token, holds the access token, then loads the user, role, organization, menu and permissions', async () => {
      const { fetchFn, api, session } = setup();

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
      expect(api.getAccessToken()).toBe(accessToken);

      const [login] = fetchFn.calls;
      expect(login!.headers.Authorization).toBeUndefined();
      expect(JSON.parse(login!.init.body as string)).toEqual({ email: 'ana@example.com', password: 'a password' });
      expect(fetchFn.calls.map((call) => call.path).sort()).toEqual(['/v1/auth/login', '/v1/auth/me', '/v1/menu']);
    });

    it('stays signed out, with no token, when the credentials are wrong', async () => {
      const { api, session } = setup({
        'POST /v1/auth/login': () => jsonResponse(401, { detail: 'Email or password is incorrect.', code: 'invalid_credentials' }),
      });

      await expect(session.signIn('ana@example.com', 'wrong')).rejects.toBeInstanceOf(ApiError);

      expect(session.getState().status).toBe('unknown');
      expect(api.getAccessToken()).toBeNull();
    });

    it('forgets the token again if the user cannot be loaded after signing in', async () => {
      const { api, session } = setup({ 'GET /v1/auth/me': () => jsonResponse(500, {}) });

      await expect(session.signIn('ana@example.com', 'a password')).rejects.toBeInstanceOf(ApiError);

      expect(api.getAccessToken()).toBeNull();
      expect(session.getState().status).not.toBe('signedIn');
    });

    it('tells subscribers about the change, and lets them stop listening', async () => {
      const { session } = setup({ 'POST /v1/auth/logout': new Response(null, { status: 204 }) });
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
      const { fetchFn, api, session } = setup({ 'POST /v1/auth/register': () => jsonResponse(201, { accessToken }) });

      await session.register(details);

      expect(session.getState().status).toBe('signedIn');
      expect(api.getAccessToken()).toBe(accessToken);
      const [register] = fetchFn.calls;
      expect(register!.headers.Authorization).toBeUndefined();
      expect(JSON.parse(register!.init.body as string)).toEqual(details);
    });

    it('stays signed out, holding no token, when the email is taken', async () => {
      const { api, session } = setup({ 'POST /v1/auth/register': () => jsonResponse(409, { detail: 'An account with this email already exists.', code: 'email_taken' }) });

      await expect(session.register(details)).rejects.toMatchObject({ status: 409 });

      expect(session.getState().status).not.toBe('signedIn');
      expect(api.getAccessToken()).toBeNull();
    });
  });

  describe('restore', () => {
    it('turns the refresh cookie back into a session when the page starts', async () => {
      const { session } = setup();

      expect(await session.restore()).toBe(true);

      expect(asMember(session).organization.businessModel).toBe('company');
    });

    it('reads the menu once, not twice: the refresh that restores the session also renews the token, but nobody is signed in yet to reload for', async () => {
      const { fetchFn, session } = setup();

      await session.restore();
      await settle();

      expect(countOf(fetchFn, '/v1/menu')).toBe(1);
    });

    it('is signed out when there is no valid refresh cookie', async () => {
      const { api, session } = setup({ 'POST /v1/auth/refresh': () => jsonResponse(401, { code: 'invalid_refresh_token' }) });

      expect(await session.restore()).toBe(false);

      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(api.getAccessToken()).toBeNull();
    });

    it('is signed out when the user cannot be loaded after refreshing', async () => {
      const { api, session } = setup({ 'GET /v1/auth/me': () => jsonResponse(403, {}) });

      expect(await session.restore()).toBe(false);

      expect(session.getState().status).toBe('signedOut');
      expect(api.getAccessToken()).toBeNull();
    });

    it('makes one attempt for callers that overlap, because the refresh token rotates', async () => {
      const { fetchFn, session } = setup();

      await Promise.all([session.restore(), session.restore()]);

      expect(countOf(fetchFn, '/v1/auth/refresh')).toBe(1);
    });
  });

  describe('signOut', () => {
    it('asks the API to end the session, then forgets everything', async () => {
      const { fetchFn, api, session } = setup({ 'POST /v1/auth/logout': new Response(null, { status: 204 }) });
      await session.signIn('ana@example.com', 'a password');

      await session.signOut();

      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(api.getAccessToken()).toBeNull();
      expect(fetchFn.calls.at(-1)!.path).toBe('/v1/auth/logout');
    });

    it('forgets the session even when the server cannot be reached', async () => {
      const { api, session } = setup({
        'POST /v1/auth/logout': () => {
          throw new TypeError('network down');
        },
      });
      await session.signIn('ana@example.com', 'a password');

      await session.signOut();

      expect(session.getState().status).toBe('signedOut');
      expect(api.getAccessToken()).toBeNull();
    });
  });

  describe('expiry', () => {
    it('becomes signed out when a request finds the session can no longer be renewed', async () => {
      let cookieIsGood = true;
      const { api, session } = setup({
        'GET /v1/data': () => jsonResponse(401, {}),
        'POST /v1/auth/refresh': () => (cookieIsGood ? jsonResponse(200, { accessToken }) : jsonResponse(401, {})),
      });
      await session.signIn('ana@example.com', 'a password');
      const seen: string[] = [];
      session.subscribe(() => seen.push(session.getState().status));
      cookieIsGood = false;

      // The page and the session share one API client, so any page's request can end the session.
      await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

      expect(seen).toEqual(['signedOut']);
      expect(session.getState()).toEqual({ status: 'signedOut' });
      expect(api.getAccessToken()).toBeNull();
    });

    it('does not announce anything when the user was not signed in anyway', async () => {
      const { api, session } = setup({ 'GET /v1/data': () => jsonResponse(401, {}), 'POST /v1/auth/refresh': () => jsonResponse(401, {}) });
      const seen: string[] = [];
      session.subscribe(() => seen.push(session.getState().status));

      await expect(api.request('/v1/data')).rejects.toBeInstanceOf(ApiError);

      expect(seen).toEqual([]);
      expect(session.getState().status).toBe('unknown');
    });
  });

  describe('getClaims', () => {
    it('reads the user id, the role and the organization from the access token', async () => {
      const { session } = setup();
      await session.signIn('ana@example.com', 'a password');

      expect(session.getClaims()).toMatchObject({ user_id: 'u2', role: 'marketingmanager', agency_id: 'a1' });
    });

    it('is null when signed out', () => {
      expect(setup().session.getClaims()).toBeNull();
    });
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

  it('reads it again when a request renews the token, because the new token carries the permissions as they are now', async () => {
    let menuReads = 0;
    const { fetchFn, api, session } = setup({
      'GET /v1/menu': () => jsonResponse(200, menuReads++ === 0 ? { items: [dashboard, workspaces], permissions: bothPermissions } : { items: [dashboard], permissions: ['dashboard.view'] }),
      'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 'renewed-token' }),
      'GET /v1/data': (call) => (call.headers.Authorization === 'Bearer renewed-token' ? jsonResponse(200, {}) : jsonResponse(401, {})),
    });
    await session.signIn('ana@example.com', 'a password');
    expect(signedIn(session).permissions).toEqual(bothPermissions);

    await api.request('/v1/data'); // the old token is refused, the session renews it, the request is retried
    await settle();

    expect(countOf(fetchFn, '/v1/menu')).toBe(2);
    expect(signedIn(session).permissions).toEqual(['dashboard.view']);
    expect(signedIn(session).menu).toEqual([dashboard]);
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
        'POST /v1/auth/logout': new Response(null, { status: 204 }),
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
    const { api, session } = setup({ 'GET /v1/menu': () => jsonResponse(500, {}) });

    await expect(session.signIn('ana@example.com', 'a password')).rejects.toBeInstanceOf(ApiError);

    expect(api.getAccessToken()).toBeNull();
    expect(session.getState().status).not.toBe('signedIn');
  });

  it('leaves a member signed out when restoring the page cannot read the menu', async () => {
    const { api, session } = setup({ 'GET /v1/menu': () => jsonResponse(500, {}) });

    expect(await session.restore()).toBe(false);

    expect(session.getState().status).toBe('signedOut');
    expect(api.getAccessToken()).toBeNull();
  });
});

describe('the Site admin session', () => {
  const adminMe = { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin' };
  const adminToken = makeJwt({ user_id: 'u1', role: 'siteadmin', exp: 1_900_000_000 });
  const adminItems = [
    { id: 'i1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' },
    { id: 'i2', pageKey: 'menus', label: 'Menus', icon: 'menu', path: '/admin/menus' },
  ];

  function adminSetup(routes: Record<string, Handler> = {}) {
    const fetchFn = routedFetch({
      'POST /v1/site/login': () => jsonResponse(200, { accessToken: adminToken }),
      'POST /v1/site/refresh': () => jsonResponse(200, { accessToken: adminToken }),
      'POST /v1/site/logout': () => new Response(null, { status: 204 }),
      'GET /v1/site/me': () => jsonResponse(200, adminMe),
      'GET /v1/site/menu': () => jsonResponse(200, { items: adminItems, permissions: [] }),
      ...routes,
    });
    const api = createApiClient({ fetchFn, refreshPath: siteAdminEndpoints.refresh });
    return { fetchFn, api, session: createSession(api, siteAdminEndpoints) };
  }

  const asAdmin = (session: ReturnType<typeof createSession>) => session.getState() as AdminState;

  it('signs in through the site endpoints, and is a user and a role with no organization and no permissions', async () => {
    const { fetchFn, api, session } = adminSetup();

    await session.signIn('root@example.com', 'a password');

    const state = asAdmin(session);
    expect(state).toMatchObject({ status: 'signedIn', kind: 'admin', role: 'siteadmin', user: adminMe.user, menu: adminItems, permissions: [] });
    expect('organization' in state).toBe(false);
    expect(api.getAccessToken()).toBe(adminToken);
    expect(fetchFn.calls.map((call) => call.path).sort()).toEqual(['/v1/site/login', '/v1/site/me', '/v1/site/menu']);
  });

  it('restores from the Site admin refresh cookie', async () => {
    const { fetchFn, session } = adminSetup();

    expect(await session.restore()).toBe(true);

    expect(asAdmin(session).kind).toBe('admin');
    expect(fetchFn.calls[0]!.path).toBe('/v1/site/refresh');
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

  it('stays signed out, holding no token, when the credentials are wrong', async () => {
    const { api, session } = adminSetup({ 'POST /v1/site/login': () => jsonResponse(401, { code: 'invalid_credentials' }) });

    await expect(session.signIn('root@example.com', 'wrong')).rejects.toMatchObject({ status: 401 });

    expect(session.getState().status).not.toBe('signedIn');
    expect(api.getAccessToken()).toBeNull();
  });

  it('has no sign-up', async () => {
    const { session } = adminSetup();

    await expect(session.register({ email: 'a@b.co', password: 'ten chars!!', fullName: null, organizationName: 'X', businessModel: 'company' })).rejects.toThrow(/no sign-up/);
  });

  it('reads the user id and the role from the token, which names no organization', async () => {
    const { session } = adminSetup();
    await session.signIn('root@example.com', 'a password');

    const claims = session.getClaims();

    expect(claims).toMatchObject({ user_id: 'u1', role: 'siteadmin' });
    expect(claims).not.toHaveProperty('agency_id');
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
