// The wording of what the forms tell the user. Plain functions, so the wording is tested without a browser,
// and nothing here reveals more than the API itself does.

import { ApiError } from '@/api/client';

export interface FieldProblem {
  field: string;
  message: string;
}

const offline = 'Could not reach the server. Check your connection and try again.';
const tooMany = 'Too many requests. Wait a minute, then try again.';

/** The first field message of a 400 response, if the API sent one. */
function firstFieldError(error: ApiError): string | null {
  if (error.status === 400 && error.errors) {
    const first = Object.values(error.errors).flat()[0];
    if (typeof first === 'string') {
      return first;
    }
  }
  return null;
}

/** Checks the sign-in form before anything is sent. @returns The first problem found, or null. */
export function validateSignIn(email: string, password: string): FieldProblem | null {
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
export function messageForSignInError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return 'Email or password is incorrect.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute, then try again.';
    }
    return firstFieldError(error) ?? 'Sign-in failed. Try again in a moment.';
  }
  return offline;
}

const googleErrors: Record<string, string> = {
  google_unavailable: 'Sign-in with Google is not available right now. Use your email and password.',
  google_invalid_request: 'Choose what kind of organization this is and enter its name, then try Google again.',
  google_denied: 'Google sign-in was cancelled.',
  google_failed: 'Google sign-in could not be completed. Try again.',
  google_state_invalid: 'That Google sign-in took too long, or was started somewhere else. Start again.',
  google_email_unverified: 'Google has not verified that email address, so it cannot be used here.',
  google_email_taken: 'That email address already has an account. Sign in with your email and password instead.',
  google_no_account: 'There is no account for that Google address yet. Create one first.',
  google_no_organization: 'That account does not belong to an organization yet.',
};

/** What to tell the person when Google sent them back with an error code. Null when there is no error to show. */
export function messageForGoogleError(code: string | null | undefined): string | null {
  if (typeof code !== 'string' || code.length === 0) {
    return null;
  }
  return Object.hasOwn(googleErrors, code) ? (googleErrors[code] ?? null) : 'Google sign-in did not work. Try again.';
}

export const minPasswordLength = 10;

export interface RegistrationForm {
  businessModel: string;
  organizationName: string;
  /** `email` or `google`: Google supplies the name, the email and the sign-in itself, so only the organization is needed there. */
  method: string;
  fullName: string;
  email: string;
  password: string;
}

/** Checks the sign-up form before anything is sent. @returns The first problem found, or null. */
export function validateRegistration({ businessModel, organizationName, method, fullName, email, password }: RegistrationForm): FieldProblem | null {
  if (typeof businessModel !== 'string' || businessModel.length === 0) {
    return { field: 'businessModel', message: 'Choose what kind of organization this is.' };
  }
  if (typeof organizationName !== 'string' || organizationName.trim().length === 0) {
    return { field: 'organizationName', message: 'Enter the name of your organization.' };
  }
  if (organizationName.trim().length > 200) {
    return { field: 'organizationName', message: 'The organization name must be at most 200 characters.' };
  }
  if (method !== 'email' && method !== 'google') {
    return { field: 'method', message: 'Choose how you want to sign up.' };
  }
  if (method === 'google') {
    return null;
  }

  if (typeof fullName === 'string' && fullName.trim().length > 200) {
    return { field: 'fullName', message: 'Your name must be at most 200 characters.' };
  }
  const signIn = validateSignIn(email, 'x');
  if (signIn) {
    return signIn;
  }
  if (typeof password !== 'string' || password.length < minPasswordLength) {
    return { field: 'password', message: `Choose a password of at least ${minPasswordLength} characters.` };
  }
  if (password.length > 128) {
    return { field: 'password', message: 'The password must be at most 128 characters.' };
  }
  return null;
}

/** What to tell the person when creating the account failed. */
export function messageForRegisterError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return 'That email address already has an account. Sign in instead.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute, then try again.';
    }
    return firstFieldError(error) ?? 'The account could not be created. Try again in a moment.';
  }
  return offline;
}

/** What to show when the Site admin dashboard could not be loaded. */
export function messageForSiteLoadError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      return tooMany;
    }
    if (error.status === 403) {
      return 'This account is no longer a site admin.';
    }
    return 'The dashboard could not be loaded.';
  }
  return offline;
}

/** What to show when the workspaces could not be loaded. */
export function messageForWorkspacesLoadError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.status === 429 ? tooMany : 'Your workspaces could not be loaded.';
  }
  return offline;
}

/** What to tell an organization admin when adding a workspace failed. */
export function messageForWorkspaceError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return 'Your role cannot add workspaces.';
    }
    if (error.status === 409) {
      return 'This kind of organization has one workspace.';
    }
    if (error.status === 429) {
      return tooMany;
    }
    const field = firstFieldError(error);
    if (field) {
      return field;
    }
  }
  return 'The workspace could not be added. Try again in a moment.';
}
