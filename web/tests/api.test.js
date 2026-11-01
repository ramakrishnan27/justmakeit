import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError, createApiClient } from '../js/core/api.js';
import { fakeFetch, jsonResponse, routedFetch } from './helpers.js';

const authorizationOf = (call) => call.headers.Authorization;

describe('api client', () => {
  it('adds the access token as a bearer header, and sends JSON bodies as JSON', async () => {
    const fetchFn = fakeFetch([jsonResponse(200, { ok: true })]);
    const api = createApiClient({ fetchFn });
    api.setAccessToken('token-1');

    const result = await api.request('/v1/thing', { method: 'POST', body: { a: 1 } });

    assert.deepEqual(result, { ok: true });
    const [call] = fetchFn.calls;
    assert.equal(call.path, '/v1/thing');
    assert.equal(call.init.method, 'POST');
    assert.equal(authorizationOf(call), 'Bearer token-1');
    assert.equal(call.headers['Content-Type'], 'application/json');
    assert.equal(call.init.body, '{"a":1}');
  });

  it('sends the cookie only to its own origin', async () => {
    const fetchFn = fakeFetch([jsonResponse(200, {})]);

    await createApiClient({ fetchFn }).request('/v1/thing');

    assert.equal(fetchFn.calls[0].init.credentials, 'same-origin');
  });

  it('sends no Authorization header when it holds no token, or when asked not to', async () => {
    const fetchFn = fakeFetch([jsonResponse(200, {}), jsonResponse(200, {})]);
    const api = createApiClient({ fetchFn });

    await api.request('/v1/a');
    api.setAccessToken('token-1');
    await api.request('/v1/b', { auth: false });

    assert.equal(authorizationOf(fetchFn.calls[0]), undefined);
    assert.equal(authorizationOf(fetchFn.calls[1]), undefined);
  });

  it('returns null for 204 No Content', async () => {
    const api = createApiClient({ fetchFn: fakeFetch([new Response(null, { status: 204 })]) });

    assert.equal(await api.request('/v1/logout', { method: 'POST' }), null);
  });

  it('refreshes once on a 401 and retries the request with the new token', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': (call) => (authorizationOf(call) === 'Bearer new-token' ? jsonResponse(200, { value: 42 }) : jsonResponse(401, {})),
      'POST /v1/site/refresh': jsonResponse(200, { accessToken: 'new-token' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old-token');

    const result = await api.request('/v1/data');

    assert.deepEqual(result, { value: 42 });
    assert.equal(api.getAccessToken(), 'new-token');
    assert.deepEqual(
      fetchFn.calls.map((call) => `${call.init.method ?? 'GET'} ${call.path}`),
      ['GET /v1/data', 'POST /v1/site/refresh', 'GET /v1/data'],
    );
  });

  it('shares one refresh between requests that fail at the same time', async () => {
    let refreshes = 0;
    const fetchFn = routedFetch({
      'GET /v1/a': (call) => (authorizationOf(call) === 'Bearer fresh' ? jsonResponse(200, { a: 1 }) : jsonResponse(401, {})),
      'GET /v1/b': (call) => (authorizationOf(call) === 'Bearer fresh' ? jsonResponse(200, { b: 2 }) : jsonResponse(401, {})),
      'GET /v1/c': (call) => (authorizationOf(call) === 'Bearer fresh' ? jsonResponse(200, { c: 3 }) : jsonResponse(401, {})),
      'POST /v1/site/refresh': async () => {
        refreshes += 1;
        await new Promise((resolve) => setTimeout(resolve, 10));
        return jsonResponse(200, { accessToken: 'fresh' });
      },
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('stale');

    const results = await Promise.all([api.request('/v1/a'), api.request('/v1/b'), api.request('/v1/c')]);

    // The refresh token rotates: a second refresh with the same cookie would look like a stolen token.
    assert.equal(refreshes, 1);
    assert.deepEqual(results, [{ a: 1 }, { b: 2 }, { c: 3 }]);
  });

  it('can refresh again later, once the first refresh has finished', async () => {
    let refreshes = 0;
    const fetchFn = routedFetch({
      'POST /v1/site/refresh': () => {
        refreshes += 1;
        return jsonResponse(200, { accessToken: `token-${refreshes}` });
      },
    });
    const api = createApiClient({ fetchFn });

    assert.equal(await api.refresh(), true);
    assert.equal(await api.refresh(), true);

    assert.equal(refreshes, 2);
    assert.equal(api.getAccessToken(), 'token-2');
  });

  it('gives up when the refresh fails: clears the token, tells listeners, and throws the 401', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': jsonResponse(401, { detail: 'nope', code: 'x' }),
      'POST /v1/site/refresh': jsonResponse(401, { code: 'invalid_refresh_token' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old');
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await assert.rejects(api.request('/v1/data'), (error) => error instanceof ApiError && error.status === 401);

    assert.equal(api.getAccessToken(), null);
    assert.equal(expired, 1);
  });

  it('retries only once: a second 401 after a successful refresh is an error, not a loop', async () => {
    const calls = [];
    // Answers 401 to the data call every time, and a fresh token to the refresh call.
    const fetchFn = async (path, init) => {
      calls.push(`${init?.method ?? 'GET'} ${path}`);
      return path === '/v1/data' ? jsonResponse(401, {}) : jsonResponse(200, { accessToken: 'new' });
    };
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old');

    await assert.rejects(api.request('/v1/data'), (error) => error.status === 401);

    assert.deepEqual(calls, ['GET /v1/data', 'POST /v1/site/refresh', 'GET /v1/data']);
  });

  it('treats a network failure during refresh as a failed refresh', async () => {
    const fetchFn = async (path) => {
      if (path === '/v1/site/refresh') {
        throw new TypeError('network down');
      }
      return jsonResponse(401, {});
    };
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old');

    assert.equal(await api.refresh(), false);
    assert.equal(api.getAccessToken(), null);
  });

  it('does not refresh for errors other than 401', async () => {
    for (const status of [400, 403, 404, 429, 500]) {
      const fetchFn = fakeFetch([jsonResponse(status, { detail: 'x' })]);
      const api = createApiClient({ fetchFn });
      api.setAccessToken('t');

      await assert.rejects(api.request('/v1/data'), (error) => error.status === status);

      assert.equal(fetchFn.calls.length, 1, `status ${status}`);
    }
  });

  it('does not refresh, or announce an expiry, when the sign-in call itself fails with 401', async () => {
    const fetchFn = fakeFetch([jsonResponse(401, { detail: 'Email or password is incorrect.', code: 'invalid_credentials' })]);
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await assert.rejects(
      api.request('/v1/site/login', { method: 'POST', body: {}, auth: false }),
      (error) => error.status === 401 && error.code === 'invalid_credentials',
    );

    assert.equal(fetchFn.calls.length, 1);
    assert.equal(expired, 0);
  });

  it('turns a problem response into an ApiError with its message, code and field errors', async () => {
    const api = createApiClient({
      fetchFn: fakeFetch([
        jsonResponse(400, { detail: 'One or more fields are invalid.', code: 'validation_failed', errors: { email: ['Enter a valid email address.'] } }),
      ]),
    });

    await assert.rejects(api.request('/v1/x'), (error) => {
      assert.ok(error instanceof ApiError);
      assert.equal(error.status, 400);
      assert.equal(error.message, 'One or more fields are invalid.');
      assert.equal(error.code, 'validation_failed');
      assert.deepEqual(error.errors, { email: ['Enter a valid email address.'] });
      return true;
    });
  });

  it('gives a generic message when an error body is not JSON', async () => {
    const api = createApiClient({ fetchFn: fakeFetch([new Response('<html>oops</html>', { status: 502 })]) });

    await assert.rejects(api.request('/v1/x'), (error) => {
      assert.equal(error.status, 502);
      assert.equal(error.message, 'The request failed.');
      assert.equal(error.code, null);
      assert.equal(error.errors, null);
      return true;
    });
  });

  it('lets a listener stop listening', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': jsonResponse(401, {}),
      'POST /v1/site/refresh': jsonResponse(401, {}),
    });
    const api = createApiClient({ fetchFn });
    let expired = 0;
    const stop = api.onSessionExpired(() => {
      expired += 1;
    });
    stop();

    await assert.rejects(api.request('/v1/data'));

    assert.equal(expired, 0);
  });
});
