import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { googleLoginPath, googleRegisterPath, loadProviders, takeErrorCode } from '../js/core/google.js';
import { jsonResponse, routedFetch } from './helpers.js';
import { createApiClient } from '../js/core/api.js';

function fakeWindow(pathname, search) {
  const replaced = [];
  return {
    location: { pathname, search },
    history: { replaceState: (_state, _title, url) => replaced.push(url) },
    replaced,
  };
}

describe('the Google addresses', () => {
  it('sends sign-in to our own server, never to Google directly', () => {
    assert.equal(googleLoginPath, '/v1/auth/google/start?intent=login');
    assert.ok(googleLoginPath.startsWith('/'));
  });

  it('carries what the person chose on the sign-up form, trimmed and encoded', () => {
    const path = googleRegisterPath({ businessModel: 'creator', organizationName: '  Maya & Co / "Videos"  ' });

    const url = new URL(path, 'http://localhost');
    assert.equal(url.pathname, '/v1/auth/google/start');
    assert.equal(url.searchParams.get('intent'), 'register');
    assert.equal(url.searchParams.get('businessModel'), 'creator');
    assert.equal(url.searchParams.get('organizationName'), 'Maya & Co / "Videos"');
  });

  it('carries no password, token or email', () => {
    const path = googleRegisterPath({ businessModel: 'company', organizationName: 'Northwind' });

    assert.doesNotMatch(path, /password|token|email/i);
  });
});

describe('takeErrorCode', () => {
  it('reads the error the server left in the address, and takes it out so a reload does not repeat it', () => {
    const win = fakeWindow('/login', '?error=google_denied');

    assert.equal(takeErrorCode(win), 'google_denied');
    assert.deepEqual(win.replaced, ['/login']);
  });

  it('keeps any other part of the query', () => {
    const win = fakeWindow('/register', '?error=google_failed&plan=trial');

    assert.equal(takeErrorCode(win), 'google_failed');
    assert.deepEqual(win.replaced, ['/register?plan=trial']);
  });

  it('is null, and leaves the address alone, when there is no error', () => {
    const win = fakeWindow('/login', '');

    assert.equal(takeErrorCode(win), null);
    assert.deepEqual(win.replaced, []);
  });
});

describe('loadProviders', () => {
  const clientFor = (routes) => createApiClient({ fetchFn: routedFetch(routes) });

  it('says Google is on when the server says so, asking without a token', async () => {
    const fetchFn = routedFetch({ 'GET /v1/auth/providers': jsonResponse(200, { google: true }) });

    assert.deepEqual(await loadProviders(createApiClient({ fetchFn })), { google: true });
    assert.equal(fetchFn.calls[0].headers.Authorization, undefined);
  });

  it('says Google is off when the server says so', async () => {
    assert.deepEqual(await loadProviders(clientFor({ 'GET /v1/auth/providers': jsonResponse(200, { google: false }) })), { google: false });
  });

  it('only believes a plain true', async () => {
    for (const value of ['true', 1, 'yes', null, {}]) {
      const api = clientFor({ 'GET /v1/auth/providers': jsonResponse(200, { google: value }) });
      assert.deepEqual(await loadProviders(api), { google: false }, String(value));
    }
  });

  it('offers email and password only, when the server cannot be asked', async () => {
    assert.deepEqual(await loadProviders(clientFor({ 'GET /v1/auth/providers': jsonResponse(500, {}) })), { google: false });
    assert.deepEqual(
      await loadProviders(clientFor({ 'GET /v1/auth/providers': () => { throw new TypeError('network down'); } })),
      { google: false },
    );
  });
});
