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
    if (error.code === 'account_deactivated') {
      return 'Your account has been deactivated. Ask your organization admin to activate it.';
    }
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

/** What to show when the team could not be loaded. */
export function messageForTeamLoadError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return 'Your role cannot manage the team.';
    }
    return error.status === 429 ? tooMany : 'The team could not be loaded.';
  }
  return offline;
}

/** Checks the invite form before anything is sent. @returns The first problem found, or null. */
export function validateInvite(fullName: string, email: string, role: string): FieldProblem | null {
  if (fullName.trim().length === 0) {
    return { field: 'fullName', message: "Enter the new employee's name." };
  }
  if (fullName.trim().length > 200) {
    return { field: 'fullName', message: 'The name must be at most 200 characters.' };
  }
  const problem = validateSignIn(email, 'x');
  if (problem) {
    return problem;
  }
  if (email.trim().length > 254) {
    return { field: 'email', message: 'The email address must be at most 254 characters.' };
  }
  if (role.length === 0) {
    return { field: 'role', message: 'Choose a role for the new employee.' };
  }
  return null;
}

/** What to tell an organization admin when an invitation could not be made. */
export function messageForInviteError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'email_taken':
        return 'That email address already has an account, so it cannot be invited.';
      case 'invitation_exists':
        return 'An invitation for that address is already waiting. Cancel it first to make another.';
      case 'team_not_available':
        return 'An individual creator works alone and cannot invite people.';
      default:
        break;
    }
    if (error.status === 403) {
      return 'Your role cannot manage the team.';
    }
    if (error.status === 429) {
      return tooMany;
    }
    const field = firstFieldError(error);
    if (field) {
      return field;
    }
  }
  return error instanceof ApiError ? 'The invitation could not be made. Try again in a moment.' : offline;
}

/** What to tell an organization admin when changing a role, removing a person or cancelling an invitation failed. */
export function messageForTeamActionError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'last_organization_admin':
        return 'An organization needs at least one active organization admin.';
      case 'email_taken':
        return 'That email address now has an account, so this invitation cannot be activated. Delete the invitation.';
      case 'cannot_change_self':
        return 'You cannot change or remove yourself.';
      case 'member_not_found':
        return 'That person is no longer in the organization. The list was refreshed.';
      case 'invitation_not_found':
        return 'That invitation is not waiting any more. The list was refreshed.';
      case 'role_not_allowed':
        return 'That role is not available in this kind of organization.';
      default:
        break;
    }
    if (error.status === 403) {
      return 'Your role cannot manage the team.';
    }
    if (error.status === 429) {
      return tooMany;
    }
    const field = firstFieldError(error);
    if (field) {
      return field;
    }
  }
  return error instanceof ApiError ? 'That did not work. Try again in a moment.' : offline;
}

/** Checks the default password an admin types to activate an invited person. @returns The first problem found, or null. */
export function validateDefaultPassword(password: string): FieldProblem | null {
  if (password.length < minPasswordLength) {
    return { field: 'password', message: `Choose a password of at least ${minPasswordLength} characters.` };
  }
  if (password.length > 128) {
    return { field: 'password', message: 'The password must be at most 128 characters.' };
  }
  return null;
}

/** Checks the change-password form before anything is sent. @returns The first problem found, or null. */
export function validateChangePassword({ current, next, confirm }: { current: string; next: string; confirm: string }): FieldProblem | null {
  if (current.length === 0) {
    return { field: 'currentPassword', message: 'Enter your current password.' };
  }
  if (next.length < minPasswordLength) {
    return { field: 'newPassword', message: `Choose a password of at least ${minPasswordLength} characters.` };
  }
  if (next.length > 128) {
    return { field: 'newPassword', message: 'The password must be at most 128 characters.' };
  }
  if (next === current) {
    return { field: 'newPassword', message: 'Choose a password you are not using now.' };
  }
  if (confirm !== next) {
    return { field: 'confirmPassword', message: 'The two new passwords do not match.' };
  }
  return null;
}

/**
 * What to tell the person when changing the password failed, and which field it is about (empty when it is about none).
 * A wrong current password arrives as a field error of the current password, never as a 401.
 */
export function problemForChangePasswordError(error: unknown): FieldProblem {
  if (!(error instanceof ApiError)) {
    return { field: '', message: offline };
  }
  if (error.status === 400 && error.errors) {
    for (const [field, messages] of Object.entries(error.errors)) {
      if (typeof messages[0] === 'string') {
        return { field, message: messages[0] };
      }
    }
  }
  if (error.code === 'no_password') {
    return { field: '', message: 'This account signs in with Google and has no password to change.' };
  }
  if (error.code === 'password_changed') {
    return { field: '', message: 'Your password was changed somewhere else a moment ago. Try again.' };
  }
  if (error.status === 429) {
    return { field: '', message: tooMany };
  }
  return { field: '', message: 'The password could not be changed. Try again in a moment.' };
}

/** What to show on the invitation page when the link could not be read. */
export function messageForInvitationPreviewError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) {
      return 'This invitation is no longer valid. It may have expired, been used or been cancelled. Ask your organization admin for a new link.';
    }
    return error.status === 429 ? tooMany : 'The invitation could not be checked. Try again in a moment.';
  }
  return offline;
}

/** Checks the accept form before anything is sent. @returns The first problem found, or null. */
export function validateAcceptance(fullName: string, password: string): FieldProblem | null {
  if (fullName.trim().length === 0) {
    return { field: 'fullName', message: 'Enter your name.' };
  }
  if (fullName.trim().length > 200) {
    return { field: 'fullName', message: 'Your name must be at most 200 characters.' };
  }
  if (password.length < minPasswordLength) {
    return { field: 'password', message: `Choose a password of at least ${minPasswordLength} characters.` };
  }
  if (password.length > 128) {
    return { field: 'password', message: 'The password must be at most 128 characters.' };
  }
  return null;
}

/** What to tell the person when accepting an invitation failed. */
export function messageForAcceptError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) {
      return messageForInvitationPreviewError(error);
    }
    if (error.status === 409) {
      return 'That email address already has an account. Sign in instead.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute, then try again.';
    }
    return firstFieldError(error) ?? 'The invitation could not be accepted. Try again in a moment.';
  }
  return offline;
}
