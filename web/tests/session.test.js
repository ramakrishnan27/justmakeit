import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError, createApiClient } from '../js/core/api.js';
import { createSession, memberEndpoints } from '../js/core/session.js';
import { jsonResponse, makeJwt, routedFetch } from './helpers.js';

const me = { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin' };
const accessToken = makeJwt({ user_id: 'u1', role: 'siteadmin', exp: 1_900_000_000 });

const adminItems = [
  { id: 'i1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' },
  { id: 'i2', pageKey: 'menus', label: 'Menus', icon: 'menu', path: '/admin/menus' },
  { id: 'i3', pageKey: 'permissions', label: 'Permissions', icon: 'shield', path: '/admin/permissions' },
];
const adminMenu = { items: adminItems, permissions: [] };

function setup(routes) {
  const fetchFn = routedFetch({ 'GET /v1/site/menu': jsonResponse(200, adminMenu), ...routes });
  const api = createApiClient({ fetchFn });
  const session = createSession(api);
  return { fetchFn, api, session };
}

describe('session', () => {
  it('starts with an unknown status', () => {
    assert.deepEqual(setup({}).session.state, { status: 'unknown', user: null, role: null });
  });

  describe('signIn', () => {
    it('signs in: logs in without a token, holds the access token, then loads the user and role', async () => {
      const { fetchFn, api, session } = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken, tokenType: 'Bearer', expiresIn: 900 }),
        'GET /v1/site/me': jsonResponse(200, me),
      });

      await session.signIn('root@example.com', 'a password');

      assert.deepEqual(session.state, { status: 'signedIn', user: me.user, role: 'siteadmin', menu: adminItems, permissions: [] });
      assert.equal(api.getAccessToken(), accessToken);
      const [login, meCall] = fetchFn.calls;
      assert.equal(login.headers.Authorization, undefined);
      assert.deepEqual(JSON.parse(login.init.body), { email: 'root@example.com', password: 'a password' });
      assert.equal(meCall.headers.Authorization, `Bearer ${accessToken}`);
    });

    it('stays signed out, with no token, when the credentials are wrong', async () => {
      const { api, session } = setup({
        'POST /v1/site/login': jsonResponse(401, { detail: 'Email or password is incorrect.', code: 'invalid_credentials' }),
      });

      await assert.rejects(session.signIn('root@example.com', 'wrong'), (error) => error instanceof ApiError && error.status === 401);

      assert.equal(session.state.status, 'unknown');
      assert.equal(api.getAccessToken(), null);
    });

    it('forgets the token again if the user cannot be loaded after signing in', async () => {
      const { api, session } = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(500, {}),
      });

      await assert.rejects(session.signIn('root@example.com', 'a password'));

      assert.equal(api.getAccessToken(), null);
      assert.notEqual(session.state.status, 'signedIn');
    });

    it('tells subscribers about the change, and lets them stop listening', async () => {
      const { session } = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(200, me),
      });
      const seen = [];
      const stop = session.subscribe((state) => seen.push(state.status));

      await session.signIn('root@example.com', 'a password');
      stop();
      await session.signOut().catch(() => {});

      assert.deepEqual(seen, ['signedIn']);
    });
  });

  describe('restore', () => {
    it('turns the refresh cookie back into a session when the page starts', async () => {
      const { session } = setup({
        'POST /v1/site/refresh': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(200, me),
      });

      assert.equal(await session.restore(), true);

      assert.equal(session.state.status, 'signedIn');
      assert.equal(session.state.role, 'siteadmin');
    });

    it('is signed out when there is no valid refresh cookie', async () => {
      const { api, session } = setup({ 'POST /v1/site/refresh': jsonResponse(401, { code: 'invalid_refresh_token' }) });

      assert.equal(await session.restore(), false);

      assert.deepEqual(session.state, { status: 'signedOut', user: null, role: null });
      assert.equal(api.getAccessToken(), null);
    });

    it('is signed out when the user cannot be loaded after refreshing', async () => {
      const { api, session } = setup({
        'POST /v1/site/refresh': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(403, { code: 'not_site_admin' }),
      });

      assert.equal(await session.restore(), false);

      assert.equal(session.state.status, 'signedOut');
      assert.equal(api.getAccessToken(), null);
    });
  });

  describe('signOut', () => {
    async function signedIn(extraRoutes = {}) {
      const parts = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(200, me),
        ...extraRoutes,
      });
      await parts.session.signIn('root@example.com', 'a password');
      return parts;
    }

    it('asks the API to end the session, then forgets it', async () => {
      const { fetchFn, api, session } = await signedIn({ 'POST /v1/site/logout': new Response(null, { status: 204 }) });

      await session.signOut();

      assert.deepEqual(session.state, { status: 'signedOut', user: null, role: null });
      assert.equal(api.getAccessToken(), null);
      assert.equal(fetchFn.calls.at(-1).path, '/v1/site/logout');
    });

    it('forgets the session even when the server cannot be reached', async () => {
      const { api, session } = await signedIn({
        'POST /v1/site/logout': () => {
          throw new TypeError('network down');
        },
      });

      await session.signOut();

      assert.equal(session.state.status, 'signedOut');
      assert.equal(api.getAccessToken(), null);
    });
  });

  describe('expiry', () => {
    it('becomes signed out when a request finds the session can no longer be renewed', async () => {
      const { api, session } = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(200, me),
        'GET /v1/site/overview': jsonResponse(401, {}),
        'POST /v1/site/refresh': jsonResponse(401, {}),
      });
      await session.signIn('root@example.com', 'a password');
      const seen = [];
      session.subscribe((state) => seen.push(state.status));

      // The page and the session share one API client, so a dashboard request can end the session.
      await assert.rejects(api.request('/v1/site/overview'), (error) => error.status === 401);

      assert.deepEqual(seen, ['signedOut']);
      assert.deepEqual(session.state, { status: 'signedOut', user: null, role: null });
    });

    it('does not announce anything when the user was not signed in anyway', async () => {
      const { api, session } = setup({
        'GET /v1/site/overview': jsonResponse(401, {}),
        'POST /v1/site/refresh': jsonResponse(401, {}),
      });
      const seen = [];
      session.subscribe((state) => seen.push(state.status));

      await assert.rejects(api.request('/v1/site/overview'));

      assert.deepEqual(seen, []);
      assert.equal(session.state.status, 'unknown');
    });
  });

  describe('getClaims', () => {
    it('reads the user id and the role from the current access token', async () => {
      const { session } = setup({
        'POST /v1/site/login': jsonResponse(200, { accessToken }),
        'GET /v1/site/me': jsonResponse(200, me),
      });
      await session.signIn('root@example.com', 'a password');

      const claims = session.getClaims();

      assert.equal(claims.user_id, 'u1');
      assert.equal(claims.role, 'siteadmin');
      assert.equal(claims.agency_id, undefined);
    });

    it('is null when signed out', () => {
      assert.equal(setup({}).session.getClaims(), null);
    });
  });
});

describe('the member session', () => {
  const memberMe = {
    user: { id: 'u2', email: 'ana@example.com', fullName: 'Ana Smith' },
    agency: { id: 'a1', name: 'Northwind Foods', businessModel: 'company', plan: 'trial' },
    role: 'marketingmanager',
    memberships: [{ agencyId: 'a1', agencyName: 'Northwind Foods', businessModel: 'company', role: 'marketingmanager' }],
  };
  const memberToken = makeJwt({ user_id: 'u2', agency_id: 'a1', role: 'marketingmanager', exp: 1_900_000_000 });

  const memberItems = [
    { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' },
    { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' },
  ];
  const memberPermissions = ['dashboard.view', 'workspaces.view', 'data.read', 'data.write'];
  const memberMenu = { items: memberItems, permissions: memberPermissions };

  function memberSetup(routes) {
    const fetchFn = routedFetch({ 'GET /v1/menu': jsonResponse(200, memberMenu), ...routes });
    const api = createApiClient({ fetchFn, refreshPath: '/v1/auth/refresh' });
    return { fetchFn, api, session: createSession(api, memberEndpoints) };
  }

  it('signs in through the member endpoints, and keeps the organization with the user and role', async () => {
    const { fetchFn, session } = memberSetup({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
    });

    await session.signIn('ana@example.com', 'a password');

    assert.equal(session.state.status, 'signedIn');
    assert.equal(session.state.role, 'marketingmanager');
    assert.deepEqual(session.state.user, memberMe.user);
    assert.deepEqual(session.state.organization, memberMe.agency);
    assert.deepEqual(session.state.memberships, memberMe.memberships);
    assert.deepEqual(
      fetchFn.calls.map((call) => call.path),
      ['/v1/auth/login', '/v1/auth/me', '/v1/menu'],
    );
  });

  it('holds the sidebar and the permissions of the token, so a page reads them from memory', async () => {
    const { session } = memberSetup({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
    });

    await session.signIn('ana@example.com', 'a password');

    assert.deepEqual(session.state.menu, memberItems);
    assert.deepEqual(session.state.permissions, memberPermissions);
  });

  it('never calls the site admin endpoints', async () => {
    const { fetchFn, session } = memberSetup({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
      'POST /v1/auth/logout': new Response(null, { status: 204 }),
    });
    await session.signIn('ana@example.com', 'a password');
    await session.signOut();

    assert.ok(fetchFn.calls.every((call) => !call.path.startsWith('/v1/site')));
  });

  it('creates an account and signs in with it, sending the organization and the kind of business', async () => {
    const { fetchFn, api, session } = memberSetup({
      'POST /v1/auth/register': jsonResponse(201, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
    });
    const details = {
      email: 'ana@example.com',
      password: 'ten chars!!',
      fullName: 'Ana Smith',
      organizationName: 'Northwind Foods',
      businessModel: 'company',
    };

    await session.register(details);

    assert.equal(session.state.status, 'signedIn');
    assert.equal(api.getAccessToken(), memberToken);
    const [register] = fetchFn.calls;
    assert.equal(register.headers.Authorization, undefined);
    assert.deepEqual(JSON.parse(register.init.body), details);
  });

  it('stays signed out, holding no token, when the email is taken', async () => {
    const { api, session } = memberSetup({
      'POST /v1/auth/register': jsonResponse(409, { detail: 'An account with this email already exists.', code: 'email_taken' }),
    });

    await assert.rejects(
      session.register({ email: 'ana@example.com', password: 'ten chars!!', organizationName: 'X', businessModel: 'company' }),
      (error) => error instanceof ApiError && error.status === 409,
    );

    assert.notEqual(session.state.status, 'signedIn');
    assert.equal(api.getAccessToken(), null);
  });

  it('forgets the token again if the new account cannot be loaded', async () => {
    const { api, session } = memberSetup({
      'POST /v1/auth/register': jsonResponse(201, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(500, {}),
    });

    await assert.rejects(session.register({ email: 'a@b.co', password: 'ten chars!!', organizationName: 'X', businessModel: 'creator' }));

    assert.equal(api.getAccessToken(), null);
    assert.notEqual(session.state.status, 'signedIn');
  });

  it('restores the session from the member refresh cookie', async () => {
    const { session } = memberSetup({
      'POST /v1/auth/refresh': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
    });

    assert.equal(await session.restore(), true);

    assert.equal(session.state.organization.businessModel, 'company');
  });

  it('reads the user id and the role from the access token', async () => {
    const { session } = memberSetup({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
    });
    await session.signIn('ana@example.com', 'a password');

    const claims = session.getClaims();

    assert.equal(claims.user_id, 'u2');
    assert.equal(claims.role, 'marketingmanager');
    assert.equal(claims.agency_id, 'a1');
  });

  it('ends the session, and forgets everything, on sign-out', async () => {
    const { api, session } = memberSetup({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
      'POST /v1/auth/logout': new Response(null, { status: 204 }),
    });
    await session.signIn('ana@example.com', 'a password');

    await session.signOut();

    assert.equal(session.state.status, 'signedOut');
    assert.equal(session.state.user, null);
    assert.equal(session.state.role, null);
    assert.equal(api.getAccessToken(), null);
  });

  it('has no sign-up in the site admin app', async () => {
    const { session } = setup({});

    await assert.rejects(session.register({}), /no sign-up/);
  });
});

describe('the sidebar and the permissions in the session', () => {
  const memberMe = {
    user: { id: 'u2', email: 'ana@example.com', fullName: 'Ana Smith' },
    agency: { id: 'a1', name: 'Northwind Foods', businessModel: 'company', plan: 'trial' },
    role: 'marketingmanager',
    memberships: [],
  };
  const memberToken = makeJwt({ user_id: 'u2', agency_id: 'a1', role: 'marketingmanager', exp: 1_900_000_000 });
  const dashboard = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
  const workspaces = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };
  const bothPermissions = ['dashboard.view', 'workspaces.view'];

  /** Lets work that a listener started without waiting for finish. */
  const settle = () => new Promise((resolve) => setImmediate(resolve));

  const countOf = (fetchFn, path) => fetchFn.calls.filter((call) => call.path === path).length;

  function member(routes) {
    const fetchFn = routedFetch({
      'POST /v1/auth/login': jsonResponse(200, { accessToken: memberToken }),
      'POST /v1/auth/refresh': jsonResponse(200, { accessToken: memberToken }),
      'GET /v1/auth/me': jsonResponse(200, memberMe),
      'GET /v1/menu': jsonResponse(200, { items: [dashboard, workspaces], permissions: bothPermissions }),
      ...routes,
    });
    const api = createApiClient({ fetchFn, refreshPath: '/v1/auth/refresh' });
    return { fetchFn, api, session: createSession(api, memberEndpoints) };
  }

  function admin(routes) {
    const fetchFn = routedFetch({
      'POST /v1/site/login': jsonResponse(200, { accessToken }),
      'GET /v1/site/me': jsonResponse(200, me),
      ...routes,
    });
    const api = createApiClient({ fetchFn });
    return { fetchFn, api, session: createSession(api) };
  }

  describe('when it is read', () => {
    it('reads the menu once at sign-in, and not again as the page moves around', async () => {
      const { fetchFn, session } = member({});

      await session.signIn('ana@example.com', 'a password');
      for (let page = 0; page < 5; page += 1) {
        void session.state.menu;
        void session.state.permissions;
        await settle();
      }

      assert.equal(countOf(fetchFn, '/v1/menu'), 1);
    });

    it('reads it once when the page restores the session, not twice', async () => {
      const { fetchFn, session } = member({});

      assert.equal(await session.restore(), true);
      await settle();

      // The refresh that restores the session also renews the token, but nobody is signed in yet to reload for.
      assert.equal(countOf(fetchFn, '/v1/menu'), 1);
      assert.deepEqual(session.state.permissions, bothPermissions);
    });

    it('reads it again when a request renews the token, because the new token carries the permissions as they are now', async () => {
      let menuReads = 0;
      const { fetchFn, api, session } = member({
        'GET /v1/menu': () =>
          jsonResponse(200, menuReads++ === 0 ? { items: [dashboard, workspaces], permissions: bothPermissions } : { items: [dashboard], permissions: ['dashboard.view'] }),
        'POST /v1/auth/refresh': jsonResponse(200, { accessToken: 'renewed-token' }),
        'GET /v1/data': (call) => (call.headers.Authorization === 'Bearer renewed-token' ? jsonResponse(200, {}) : jsonResponse(401, {})),
      });
      await session.signIn('ana@example.com', 'a password');
      assert.deepEqual(session.state.permissions, bothPermissions);

      await api.request('/v1/data'); // the old token is refused, the session renews it, the request is retried
      await settle();

      assert.equal(countOf(fetchFn, '/v1/menu'), 2);
      assert.deepEqual(session.state.permissions, ['dashboard.view']);
      assert.deepEqual(session.state.menu, [dashboard]);
      assert.equal(session.state.status, 'signedIn');
    });

    it('forgets the menu and the permissions on sign-out', async () => {
      const { session } = member({ 'POST /v1/auth/logout': new Response(null, { status: 204 }) });
      await session.signIn('ana@example.com', 'a password');

      await session.signOut();

      assert.deepEqual(session.state, { status: 'signedOut', user: null, role: null });
    });
  });

  describe('refreshMenu', () => {
    it('replaces the menu and the permissions, tells subscribers, and leaves the person signed in', async () => {
      let reads = 0;
      const { session } = member({
        'GET /v1/menu': () => jsonResponse(200, reads++ === 0 ? { items: [dashboard], permissions: ['dashboard.view'] } : { items: [dashboard, workspaces], permissions: bothPermissions }),
      });
      await session.signIn('ana@example.com', 'a password');
      const seen = [];
      session.subscribe((state) => seen.push([state.status, state.menu.length]));

      await session.refreshMenu();

      assert.deepEqual(seen, [['signedIn', 2]]);
      assert.deepEqual(session.state.permissions, bothPermissions);
      assert.equal(session.state.user.id, 'u2');
    });

    it('keeps the menu the person has when it cannot be read again', async () => {
      let reads = 0;
      const { session } = member({
        'GET /v1/menu': () => (reads++ === 0 ? jsonResponse(200, { items: [dashboard], permissions: ['dashboard.view'] }) : jsonResponse(500, {})),
      });
      await session.signIn('ana@example.com', 'a password');

      await session.refreshMenu();

      assert.deepEqual(session.state.menu, [dashboard]);
      assert.equal(session.state.status, 'signedIn');
    });

    it('asks for nothing when nobody is signed in', async () => {
      const { fetchFn, session } = member({});

      await session.refreshMenu();

      assert.equal(fetchFn.calls.length, 0);
    });

    it('lets callers who overlap share one read', async () => {
      const { fetchFn, session } = member({});
      await session.signIn('ana@example.com', 'a password');

      await Promise.all([session.refreshMenu(), session.refreshMenu(), session.refreshMenu()]);

      assert.equal(countOf(fetchFn, '/v1/menu'), 2); // the sign-in read and one shared read
    });

    it('does not bring a session back that ended while it was reading', async () => {
      let release;
      const gate = new Promise((resolve) => {
        release = resolve;
      });
      let reads = 0;
      const { session } = member({
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

      assert.equal(session.state.status, 'signedOut');
      assert.equal(session.state.menu, undefined);
    });
  });

  describe('what the page will draw', () => {
    it('drops an item with an address that could leave the site or break the page', async () => {
      const { session } = member({
        'GET /v1/menu': jsonResponse(200, {
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

      assert.deepEqual(session.state.menu, [dashboard, workspaces]);
      assert.deepEqual(session.state.permissions, ['dashboard.view']);
    });
  });

  describe('when the menu cannot be had', () => {
    it('stops a member from signing in: without permissions the page cannot tell which pages are theirs', async () => {
      const { api, session } = member({ 'GET /v1/menu': jsonResponse(500, {}) });

      await assert.rejects(session.signIn('ana@example.com', 'a password'));

      assert.equal(api.getAccessToken(), null);
      assert.notEqual(session.state.status, 'signedIn');
    });

    it('leaves a member signed out when restoring the page cannot read the menu', async () => {
      const { api, session } = member({ 'GET /v1/menu': jsonResponse(500, {}) });

      assert.equal(await session.restore(), false);

      assert.equal(session.state.status, 'signedOut');
      assert.equal(api.getAccessToken(), null);
    });

    it('gives a site admin the pages that repair a menu, and signs them in all the same', async () => {
      const { session } = admin({ 'GET /v1/site/menu': jsonResponse(500, {}) });

      await session.signIn('root@example.com', 'a password');

      assert.equal(session.state.status, 'signedIn');
      assert.deepEqual(
        session.state.menu.map((item) => item.path),
        ['/admin', '/admin/menus', '/admin/permissions'],
      );
      assert.deepEqual(session.state.permissions, []);
    });

    it('does the same for a site admin whose menu came back empty', async () => {
      const { session } = admin({ 'GET /v1/site/menu': jsonResponse(200, { items: [], permissions: [] }) });

      await session.signIn('root@example.com', 'a password');

      assert.equal(session.state.menu.length, 3);
    });

    it('keeps the fallback list from being changed by anyone who holds the session', async () => {
      const { session } = admin({ 'GET /v1/site/menu': jsonResponse(500, {}) });
      await session.signIn('root@example.com', 'a password');

      session.state.menu.push({ id: 'x', label: 'Extra', icon: 'home', path: '/extra' });

      const again = admin({ 'GET /v1/site/menu': jsonResponse(500, {}) });
      await again.session.signIn('root@example.com', 'a password');
      assert.equal(again.session.state.menu.length, 3);
    });
  });
});