// The wording of what the sign-in form tells the user. Plain functions, so the wording is tested
// without a browser, and nothing here reveals more than the API itself does.

import { ApiError } from './api.js';

/**
 * Checks the form before anything is sent.
 * @returns {{ field: 'email' | 'password', message: string } | null} The first problem found, or null.
 */
export function validateSignIn(email, password) {
  if (typeof email !== 'string' || email.trim().length === 0) {
    return { field: 'email', message: 'Enter your email address.' };
  }
  if (!email.includes('@')) {
    return { field: 'email', message: 'Enter a valid email address.' };
  }
  if (typeof password !== 'string' || password.length === 0) {
    return { field: 'password', message: 'Enter your password.' };
  }
  return null;
}

/** What to tell the user when signing in failed. */
export function messageForSignInError(error) {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return 'Email or password is incorrect.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute, then try again.';
    }
    if (error.status === 400 && error.errors) {
      const first = Object.values(error.errors).flat()[0];
      if (typeof first === 'string') {
        return first;
      }
    }
    return 'Sign-in failed. Try again in a moment.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** What to show when the dashboard data could not be loaded. */
export function messageForLoadError(error) {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      return 'Too many requests. Wait a minute, then try again.';
    }
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    return 'The dashboard could not be loaded.';
  }
  return 'Could not reach the server. Check your connection and try again.';
}
