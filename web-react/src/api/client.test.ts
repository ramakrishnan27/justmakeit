import { describe, expect, it } from 'vitest';
import { jsonResponse, requestLine, routedFetch, type RecordedCall } from '@/test/helpers';
import { ApiError, createApiClient } from './client';

const authorizationOf = (call: RecordedCall) => call.headers.Authorization;

describe('api client', () => {
  it('adds the access token as a bearer header, and sends JSON bodies as JSON', async () => {
    const fetchFn = routedFetch({ 'POST /v1/thing': jsonResponse(200, { ok: true }) });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('token-1');

    const result = await api.request('/v1/thing', { method: 'POST', body: { a: 1 } });

    expect(result).toEqual({ ok: true });
    const [call] = fetchFn.calls;
    expect(authorizationOf(call!)).toBe('Bearer token-1');
    expect(call!.headers['Content-Type']).toBe('application/json');
    expect(call!.init.body).toBe('{"a":1}');
  });

  it('sends the cookie only to its own origin', async () => {
    const fetchFn = routedFetch({ 'GET /v1/thing': jsonResponse(200, {}) });

    await createApiClient({ fetchFn }).request('/v1/thing');

    expect(fetchFn.calls[0]!.init.credentials).toBe('same-origin');
  });

  it('sends no Authorization header when it holds no token, or when asked not to', async () => {
    const fetchFn = routedFetch({ 'GET /v1/a': jsonResponse(200, {}), 'GET /v1/b': jsonResponse(200, {}) });
    const api = createApiClient({ fetchFn });

    await api.request('/v1/a');
    api.setAccessToken('token-1');
    await api.request('/v1/b', { auth: false });

    expect(authorizationOf(fetchFn.calls[0]!)).toBeUndefined();
    expect(authorizationOf(fetchFn.calls[1]!)).toBeUndefined();
  });

  it('returns null for 204 No Content', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/logout': new Response(null, { status: 204 }) }) });

    expect(await api.request('/v1/logout', { method: 'POST' })).toBeNull();
  });

  it('refreshes once on a 401 and retries the request with the new token', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': (call) => (authorizationOf(call) === 'Bearer new-token' ? jsonResponse(200, { value: 42 }) : jsonResponse(401, {})),
      'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 'new-token' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old-token');

    const result = await api.request('/v1/data');

    expect(result).toEqual({ value: 42 });
    expect(api.getAccessToken()).toBe('new-token');
    expect(fetchFn.calls.map(requestLine)).toEqual(['GET /v1/data', 'POST /v1/auth/refresh', 'GET /v1/data']);
  });

  it('shares one refresh between requests that fail at the same time', async () => {
    let refreshes = 0;
    const fresh = (body: unknown) => (call: RecordedCall) => (authorizationOf(call) === 'Bearer fresh' ? jsonResponse(200, body) : jsonResponse(401, {}));
    const fetchFn = routedFetch({
      'GET /v1/a': fresh({ a: 1 }),
      'GET /v1/b': fresh({ b: 2 }),
      'GET /v1/c': fresh({ c: 3 }),
      'POST /v1/auth/refresh': async () => {
        refreshes += 1;
        await new Promise((resolve) => setTimeout(resolve, 10));
        return jsonResponse(200, { accessToken: 'fresh' });
      },
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('stale');

    const results = await Promise.all([api.request('/v1/a'), api.request('/v1/b'), api.request('/v1/c')]);

    // The refresh token rotates: a second refresh with the same cookie would look like a stolen token.
    expect(refreshes).toBe(1);
    expect(results).toEqual([{ a: 1 }, { b: 2 }, { c: 3 }]);
  });

  it('gives up when the refresh fails: clears the token, tells listeners, and throws the 401', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': () => jsonResponse(401, { detail: 'nope', code: 'x' }),
      'POST /v1/auth/refresh': () => jsonResponse(401, { code: 'invalid_refresh_token' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old');
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

    expect(api.getAccessToken()).toBeNull();
    expect(expired).toBe(1);
  });

  it('retries only once: a second 401 after a successful refresh is an error, not a loop', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': () => jsonResponse(401, {}),
      'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 'new' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old');

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

    expect(fetchFn.calls.map(requestLine)).toEqual(['GET /v1/data', 'POST /v1/auth/refresh', 'GET /v1/data']);
  });

  it('treats a network failure during refresh as a failed refresh', async () => {
    const api = createApiClient({
      fetchFn: async (path) => {
        if (path === '/v1/auth/refresh') {
          throw new TypeError('network down');
        }
        return jsonResponse(401, {});
      },
    });
    api.setAccessToken('old');

    expect(await api.refresh()).toBe(false);
    expect(api.getAccessToken()).toBeNull();
  });

  it('does not refresh for errors other than 401', async () => {
    for (const status of [400, 403, 404, 429, 500]) {
      const fetchFn = routedFetch({ 'GET /v1/data': () => jsonResponse(status, { detail: 'x' }) });
      const api = createApiClient({ fetchFn });
      api.setAccessToken('t');

      await expect(api.request('/v1/data')).rejects.toMatchObject({ status });

      expect(fetchFn.calls.length, `status ${status}`).toBe(1);
    }
  });

  it('does not refresh, or announce an expiry, when the sign-in call itself fails with 401', async () => {
    const fetchFn = routedFetch({
      'POST /v1/auth/login': () => jsonResponse(401, { detail: 'Email or password is incorrect.', code: 'invalid_credentials' }),
    });
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await expect(api.request('/v1/auth/login', { method: 'POST', body: {}, auth: false })).rejects.toMatchObject({ status: 401, code: 'invalid_credentials' });

    expect(fetchFn.calls.length).toBe(1);
    expect(expired).toBe(0);
  });

  it('turns a problem response into an ApiError with its message, code and field errors', async () => {
    const api = createApiClient({
      fetchFn: routedFetch({
        'GET /v1/x': () => jsonResponse(400, { detail: 'One or more fields are invalid.', code: 'validation_failed', errors: { email: ['Enter a valid email address.'] } }),
      }),
    });

    const error = await api.request('/v1/x').catch((failure: unknown) => failure);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      status: 400,
      message: 'One or more fields are invalid.',
      code: 'validation_failed',
      errors: { email: ['Enter a valid email address.'] },
    });
  });

  it('gives a generic message when an error body is not JSON', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'GET /v1/x': () => new Response('<html>oops</html>', { status: 502 }) }) });

    await expect(api.request('/v1/x')).rejects.toMatchObject({ status: 502, message: 'The request failed.', code: null, errors: null });
  });
});

describe('api client: renewed tokens', () => {
  it('tells listeners when a refresh gives it a new token, whoever asked for it', async () => {
    const fetchFn = routedFetch({
      'GET /v1/data': (call) => (authorizationOf(call) === 'Bearer new-token' ? jsonResponse(200, {}) : jsonResponse(401, {})),
      'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 'new-token' }),
    });
    const api = createApiClient({ fetchFn });
    api.setAccessToken('old-token');
    let announced = 0;
    api.onTokenRefreshed(() => {
      announced += 1;
      expect(api.getAccessToken()).toBe('new-token'); // the new token is already held when they hear of it
    });

    await api.request('/v1/data'); // renewed because a request was refused
    await api.refresh(); // renewed because the page asked, as it does when it restores a session

    expect(announced).toBe(2);
  });

  it('says nothing when the refresh fails or the answer holds no token', async () => {
    for (const answer of [jsonResponse(401, {}), jsonResponse(200, {})]) {
      const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/auth/refresh': () => answer.clone() }) });
      let announced = 0;
      api.onTokenRefreshed(() => {
        announced += 1;
      });

      expect(await api.refresh()).toBe(false);
      expect(announced).toBe(0);
    }
  });

  it('announces once for callers that share one refresh', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 't' }) }) });
    let announced = 0;
    api.onTokenRefreshed(() => {
      announced += 1;
    });

    await Promise.all([api.refresh(), api.refresh(), api.refresh()]);

    expect(announced).toBe(1);
  });

  it('keeps the new token when a listener throws, and lets the other listeners hear', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 't' }) }) });
    let heard = false;
    api.onTokenRefreshed(() => {
      throw new Error('a listener with a bug');
    });
    api.onTokenRefreshed(() => {
      heard = true;
    });

    expect(await api.refresh()).toBe(true);

    expect(api.getAccessToken()).toBe('t');
    expect(heard).toBe(true);
  });

  it('lets a listener stop listening', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken: 't' }) }) });
    let announced = 0;
    const stop = api.onTokenRefreshed(() => {
      announced += 1;
    });
    stop();

    await api.refresh();

    expect(announced).toBe(0);
  });
});
