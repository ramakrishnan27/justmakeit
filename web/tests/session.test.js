import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError, createApiClient } from '../js/core/api.js';
import { createSession, memberEndpoints } from '../js/core/session.js';
import { jsonResponse, makeJwt, routedFetch } from './helpers.js';

const me = { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin' };
const accessToken = makeJwt({ user_id: 'u1', role: 'siteadmin', exp: 1_900_000_000 });

function setup(routes) {
  const fetchFn = routedFetch(routes);
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

      assert.deepEqual(session.state, { status: 'signedIn', user: me.user, role: 'siteadmin' });
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

  function memberSetup(routes) {
    const fetchFn = routedFetch(routes);
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
      ['/v1/auth/login', '/v1/auth/me'],
    );
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
