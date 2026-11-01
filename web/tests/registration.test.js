import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError } from '../js/core/api.js';
import {
  messageForGoogleError,
  messageForMemberLoadError,
  messageForRegisterError,
  messageForWorkspaceError,
  minPasswordLength,
  validateRegistration,
} from '../js/core/messages.js';

const good = {
  businessModel: 'company',
  organizationName: 'Northwind Foods',
  method: 'email',
  fullName: 'Ana Smith',
  email: 'ana@example.com',
  password: 'ten chars!!',
};

describe('validateRegistration', () => {
  it('accepts a complete email sign-up', () => {
    assert.equal(validateRegistration(good), null);
  });

  it('asks for the kind of organization first, then its name, then how to sign up', () => {
    assert.equal(validateRegistration({ ...good, businessModel: '' }).field, 'businessModel');
    assert.equal(validateRegistration({ ...good, businessModel: undefined }).field, 'businessModel');
    assert.equal(validateRegistration({ ...good, organizationName: '   ' }).field, 'organizationName');
    assert.equal(validateRegistration({ ...good, method: '' }).field, 'method');
    assert.equal(validateRegistration({ ...good, method: 'facebook' }).field, 'method');
  });

  it('accepts a Google sign-up with only the organization: Google supplies the name, the email and the sign-in', () => {
    const google = { businessModel: 'creator', organizationName: 'Maya Makes Videos', method: 'google', fullName: '', email: '', password: '' };

    assert.equal(validateRegistration(google), null);
  });

  it('does not ask a Google sign-up for an email or a password', () => {
    assert.equal(validateRegistration({ ...good, method: 'google', email: '', password: '' }), null);
  });

  it('still asks a Google sign-up for the organization', () => {
    assert.equal(validateRegistration({ ...good, method: 'google', organizationName: '' }).field, 'organizationName');
  });

  it('checks the email and the password of an email sign-up', () => {
    assert.equal(validateRegistration({ ...good, email: '' }).field, 'email');
    assert.equal(validateRegistration({ ...good, email: 'no-at-sign' }).field, 'email');
    assert.equal(validateRegistration({ ...good, password: '' }).field, 'password');
    assert.equal(validateRegistration({ ...good, password: '123456789' }).field, 'password');
  });

  it('wants the same minimum password length as the server', () => {
    assert.equal(minPasswordLength, 10);
    assert.equal(validateRegistration({ ...good, password: '0123456789' }), null);
    assert.match(validateRegistration({ ...good, password: '012345678' }).message, /at least 10/);
  });

  it('refuses names and passwords longer than the server keeps', () => {
    assert.equal(validateRegistration({ ...good, organizationName: 'x'.repeat(201) }).field, 'organizationName');
    assert.equal(validateRegistration({ ...good, organizationName: 'x'.repeat(200) }), null);
    assert.equal(validateRegistration({ ...good, fullName: 'x'.repeat(201) }).field, 'fullName');
    assert.equal(validateRegistration({ ...good, password: 'p'.repeat(129) }).field, 'password');
    assert.equal(validateRegistration({ ...good, password: 'p'.repeat(128) }), null);
  });

  it('lets the name be left out', () => {
    assert.equal(validateRegistration({ ...good, fullName: '' }), null);
    assert.equal(validateRegistration({ ...good, fullName: undefined }), null);
  });
});

describe('messageForRegisterError', () => {
  it('says the email is taken, and points to sign-in', () => {
    assert.match(messageForRegisterError(new ApiError(409, 'x', { code: 'email_taken' })), /already has an account/);
  });

  it('asks the person to wait after too many attempts', () => {
    assert.match(messageForRegisterError(new ApiError(429, 'x')), /Too many attempts/);
  });

  it('shows the first thing the server objected to', () => {
    const error = new ApiError(400, 'x', { errors: { password: ['Password must be at least 10 characters.'] } });

    assert.equal(messageForRegisterError(error), 'Password must be at least 10 characters.');
  });

  it('is plain about a server error and about no connection', () => {
    assert.match(messageForRegisterError(new ApiError(500, 'x')), /could not be created/);
    assert.match(messageForRegisterError(new TypeError('network down')), /Could not reach the server/);
  });

  it('never repeats what the server said word for word beyond field messages', () => {
    assert.doesNotMatch(messageForRegisterError(new ApiError(500, 'stack trace here')), /stack trace/);
  });
});

describe('messageForGoogleError', () => {
  const codes = [
    'google_unavailable',
    'google_invalid_request',
    'google_denied',
    'google_failed',
    'google_state_invalid',
    'google_email_unverified',
    'google_email_taken',
    'google_no_account',
    'google_no_organization',
  ];

  it('has a message for every code the server can send back', () => {
    for (const code of codes) {
      assert.ok(messageForGoogleError(code)?.length > 20, code);
    }
  });

  it('gives each code its own message', () => {
    assert.equal(new Set(codes.map(messageForGoogleError)).size, codes.length);
  });

  it('tells a person whose email already has an account to sign in with their password', () => {
    assert.match(messageForGoogleError('google_email_taken'), /email and password/);
  });

  it('tells a person with no account to create one first', () => {
    assert.match(messageForGoogleError('google_no_account'), /Create one first/);
  });

  it('shows nothing when there is no error, and something generic for a code it does not know', () => {
    for (const value of [null, undefined, '']) {
      assert.equal(messageForGoogleError(value), null, String(value));
    }
    assert.equal(messageForGoogleError('google_something_new'), 'Google sign-in did not work. Try again.');
  });

  it('never shows what was put in the address as if it were a message', () => {
    assert.doesNotMatch(messageForGoogleError('<script>alert(1)</script>'), /script/);
    assert.equal(messageForGoogleError('constructor'), 'Google sign-in did not work. Try again.');
  });
});

describe('messageForWorkspaceError', () => {
  it('explains each refusal', () => {
    assert.match(messageForWorkspaceError(new ApiError(403, 'x')), /role cannot add/);
    assert.match(messageForWorkspaceError(new ApiError(409, 'x')), /one workspace/);
    assert.match(messageForWorkspaceError(new ApiError(429, 'x')), /Too many requests/);
  });

  it('shows what the server objected to in the name', () => {
    const error = new ApiError(400, 'x', { errors: { name: ['Name is required.'] } });

    assert.equal(messageForWorkspaceError(error), 'Name is required.');
  });

  it('falls back to something plain', () => {
    assert.match(messageForWorkspaceError(new ApiError(500, 'x')), /could not be added/);
    assert.match(messageForWorkspaceError(new TypeError('down')), /could not be added/);
  });
});

describe('messageForMemberLoadError', () => {
  it('is plain about every kind of failure', () => {
    assert.match(messageForMemberLoadError(new ApiError(429, 'x')), /Too many requests/);
    assert.match(messageForMemberLoadError(new ApiError(500, 'x')), /could not be loaded/);
    assert.match(messageForMemberLoadError(new TypeError('down')), /Could not reach the server/);
  });
});
