import { describe, expect, it } from 'vitest';
import { jsonResponse, requestLine, routedFetch } from '@/test/helpers';
import { ApiError, createApiClient } from './client';

describe('api client', () => {
  it('sends JSON bodies as JSON, and nothing that looks like a credential', async () => {
    const fetchFn = routedFetch({ 'POST /v1/thing': jsonResponse(200, { ok: true }) });
    const api = createApiClient({ fetchFn });

    const result = await api.request('/v1/thing', { method: 'POST', body: { a: 1 } });

    expect(result).toEqual({ ok: true });
    const [call] = fetchFn.calls;
    expect(call!.headers['Content-Type']).toBe('application/json');
    expect(call!.headers.Accept).toBe('application/json');
    expect(call!.init.body).toBe('{"a":1}');
    // The sign-in is a cookie the browser sends by itself; the page holds no token to put in a header.
    expect(Object.keys(call!.headers).map((name) => name.toLowerCase())).not.toContain('authorization');
  });

  it('sends the cookie only to its own origin', async () => {
    const fetchFn = routedFetch({ 'GET /v1/thing': jsonResponse(200, {}) });

    await createApiClient({ fetchFn }).request('/v1/thing');

    expect(fetchFn.calls[0]!.init.credentials).toBe('same-origin');
  });

  it('has no way to hold, read or renew a token', () => {
    const api = createApiClient({ fetchFn: routedFetch({}) }) as unknown as Record<string, unknown>;

    for (const name of ['setAccessToken', 'getAccessToken', 'refresh', 'onTokenRefreshed']) {
      expect(api[name], name).toBeUndefined();
    }
  });

  it('returns null for 204 No Content', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'POST /v1/logout': new Response(null, { status: 204 }) }) });

    expect(await api.request('/v1/logout', { method: 'POST' })).toBeNull();
  });

  it('does not try to renew anything on a 401: it tells the listeners the session is over and throws the 401', async () => {
    const fetchFn = routedFetch({ 'GET /v1/data': () => jsonResponse(401, { detail: 'nope', code: 'x' }) });
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401, code: 'x' });

    expect(expired).toBe(1);
    expect(fetchFn.calls.map(requestLine)).toEqual(['GET /v1/data']); // no refresh, no retry
  });

  it('treats a permission change the same way: the 401 permissions_changed ends the session', async () => {
    const fetchFn = routedFetch({ 'GET /v1/menu': () => jsonResponse(401, { detail: 'Your permissions changed. Sign in again.', code: 'permissions_changed' }) });
    const api = createApiClient({ fetchFn });
    let expired = 0;
    api.onSessionExpired(() => {
      expired += 1;
    });

    await expect(api.request('/v1/menu')).rejects.toMatchObject({ status: 401, code: 'permissions_changed' });

    expect(expired).toBe(1);
  });

  it('does not announce an expiry for errors other than 401', async () => {
    for (const status of [400, 403, 404, 429, 500]) {
      const fetchFn = routedFetch({ 'GET /v1/data': () => jsonResponse(status, { detail: 'x' }) });
      const api = createApiClient({ fetchFn });
      let expired = 0;
      api.onSessionExpired(() => {
        expired += 1;
      });

      await expect(api.request('/v1/data')).rejects.toMatchObject({ status });

      expect(expired, `status ${status}`).toBe(0);
      expect(fetchFn.calls.length, `status ${status}`).toBe(1);
    }
  });

  it('does not announce an expiry when the sign-in call itself fails with 401', async () => {
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

  it('lets a listener stop listening', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'GET /v1/data': () => jsonResponse(401, {}) }) });
    let expired = 0;
    const stop = api.onSessionExpired(() => {
      expired += 1;
    });
    stop();

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

    expect(expired).toBe(0);
  });

  it('still throws the 401, and lets the other listeners hear, when a listener throws', async () => {
    const api = createApiClient({ fetchFn: routedFetch({ 'GET /v1/data': () => jsonResponse(401, {}) }) });
    let heard = false;
    api.onSessionExpired(() => {
      throw new Error('a listener with a bug');
    });
    api.onSessionExpired(() => {
      heard = true;
    });

    await expect(api.request('/v1/data')).rejects.toMatchObject({ status: 401 });

    expect(heard).toBe(true);
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
