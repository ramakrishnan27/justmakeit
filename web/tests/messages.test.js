import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError } from '../js/core/api.js';
import { messageForLoadError, messageForSignInError, validateSignIn } from '../js/core/messages.js';

describe('validateSignIn', () => {
  it('accepts an email and a password', () => {
    assert.equal(validateSignIn('root@example.com', 'x'), null);
  });

  it('asks for the email first, then for the password', () => {
    assert.deepEqual(validateSignIn('', ''), { field: 'email', message: 'Enter your email address.' });
    assert.deepEqual(validateSignIn('   ', 'x'), { field: 'email', message: 'Enter your email address.' });
    assert.deepEqual(validateSignIn('not-an-email', 'x'), { field: 'email', message: 'Enter a valid email address.' });
    assert.deepEqual(validateSignIn('root@example.com', ''), { field: 'password', message: 'Enter your password.' });
  });

  it('does not judge the password beyond it being there', () => {
    assert.equal(validateSignIn('root@example.com', ' '), null);
    assert.equal(validateSignIn('root@example.com', 'a'), null);
  });
});

describe('messageForSignInError', () => {
  it('says the same thing for every credentials failure', () => {
    assert.equal(messageForSignInError(new ApiError(401, 'x', { code: 'invalid_credentials' })), 'Email or password is incorrect.');
  });

  it('tells the user to wait when they are rate limited', () => {
    assert.match(messageForSignInError(new ApiError(429, 'x')), /Too many attempts/);
  });

  it('shows the first field message of a validation failure', () => {
    const error = new ApiError(400, 'x', { errors: { email: ['Enter a valid email address.'], password: ['Password is required.'] } });
    assert.equal(messageForSignInError(error), 'Enter a valid email address.');
  });

  it('gives a generic message for any other API error, never the API text or a stack', () => {
    for (const status of [400, 403, 500, 503]) {
      assert.equal(messageForSignInError(new ApiError(status, 'internal detail: secret')), 'Sign-in failed. Try again in a moment.');
    }
  });

  it('says the server could not be reached for a network failure', () => {
    assert.match(messageForSignInError(new TypeError('Failed to fetch')), /Could not reach the server/);
  });
});

describe('messageForLoadError', () => {
  it('explains a rate limit', () => {
    assert.match(messageForLoadError(new ApiError(429, 'x')), /Too many requests/);
  });

  it('explains a lost site admin role', () => {
    assert.equal(messageForLoadError(new ApiError(403, 'x', { code: 'not_site_admin' })), 'This account is no longer a site admin.');
  });

  it('is generic for anything else', () => {
    assert.equal(messageForLoadError(new ApiError(500, 'x')), 'The dashboard could not be loaded.');
    assert.match(messageForLoadError(new TypeError('Failed to fetch')), /Could not reach the server/);
  });
});
