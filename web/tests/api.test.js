import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError, createApiClient } from '../js/core/api.js';
import { fakeFetch, jsonResponse, routedFetch } from './helpers.js';

describe('api client', () => {
  it('sends JSON bodies as JSON, and nothing that looks like a credential', async () => {
    const fetchFn = fakeFetch([jsonResponse(200, { ok: true })]);
    const api = createApiClient({ fetchFn });

    const result = await api.request('/v1/thing', { method: 'POST', body: { a: 1 } });

    assert.deepEqual(result, { ok: true });
    const [call] = fetchFn.calls;
    assert.equal(call.path, '/v1/thing');
    assert.equal(call.init.method, 'POST');
    assert.equal(call.headers['Content-Type'], 'application/json');
    assert.equal(call.headers.Accept, 'application/json');
    assert.equal(call.init.body, '{"a":1}');
    // The sign-in is a cookie the browser sends by itself; the page holds no token to put in a header.
    assert.ok(!Object.keys(call.headers).some((name) => name.toLowerCase() === 'authorization'));
  });

  it('sends the cookie only to its own origin', async () => {
    const fetchFn = fakeFetch([jsonResponse(200, {})]);

    await createApiClient({ fetchFn }).request('/v1/thing');

    assert.equal(fetchFn.calls[0].init.credentials, 'same-origin');
  });

  it('has no way to hold, read or renew a token', () => {
    const api = createApiClient({ fetchFn: fakeFetch([]) });

    for (const name of ['setAccessToken', 'getAccessToken', 'refresh', 'onTokenRefreshed']) {
      assert.equal(api[name], undefined, name);
    }
  });

  it('returns null for 204 No Content', async () => {
    const api = createApiClient({ fetchFn: fakeFetch([new Response(null, { status: 204 })]) });

    assert.equal(await api.request('/v1/logout', { method: 'POST' }), null);
  });

  it('does not try to renew anything on a 401: it tells the listeners the session is over and throws the 401', async () => {
    const fetchFn = routedFetch({ 'GET /v1/data': () => jsonResponse(401, { detail: 'nope', code: 'x' }) });
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await assert.rejects(api.request('/v1/data'), (error) => error instanceof ApiError && error.status === 401 && error.code === 'x');

    assert.equal(expired, 1);
    assert.deepEqual(
      fetchFn.calls.map((call) => `${call.init.method ?? 'GET'} ${call.path}`),
      ['GET /v1/data'], // no refresh, no retry
    );
  });

  it('treats a permission change the same way: the 401 permissions_changed ends the session', async () => {
    const fetchFn = fakeFetch([jsonResponse(401, { detail: 'Your permissions changed. Sign in again.', code: 'permissions_changed' })]);
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await assert.rejects(api.request('/v1/menu'), (error) => error.status === 401 && error.code === 'permissions_changed');

    assert.equal(expired, 1);
  });

  it('does not announce an expiry for errors other than 401', async () => {
    for (const status of [400, 403, 404, 429, 500]) {
      const fetchFn = fakeFetch([jsonResponse(status, { detail: 'x' })]);
      const api = createApiClient({ fetchFn });
      let expired = 0;
      api.onSessionExpired(() => {
        expired += 1;
      });

      await assert.rejects(api.request('/v1/data'), (error) => error.status === status);

      assert.equal(expired, 0, `status ${status}`);
      assert.equal(fetchFn.calls.length, 1, `status ${status}`);
    }
  });

  it('does not announce an expiry when the sign-in call itself fails with 401', async () => {
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

  it('lets a listener stop listening', async () => {
    const api = createApiClient({ fetchFn: fakeFetch([jsonResponse(401, {})]) });
    let expired = 0;
    const stop = api.onSessionExpired(() => {
      expired += 1;
    });
    stop();

    await assert.rejects(api.request('/v1/data'));

    assert.equal(expired, 0);
  });

  it('still throws the 401, and lets the other listeners hear, when a listener throws', async () => {
    const api = createApiClient({ fetchFn: fakeFetch([jsonResponse(401, {})]) });
    let heard = false;
    api.onSessionExpired(() => {
      throw new Error('a listener with a bug');
    });
    api.onSessionExpired(() => {
      heard = true;
    });

    await assert.rejects(api.request('/v1/data'), (error) => error.status === 401);

    assert.equal(heard, true);
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
});
