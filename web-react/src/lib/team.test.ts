import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import { readRepositoryFile } from '@/test/repo';
import {
  messageForAcceptError,
  messageForInvitationPreviewError,
  messageForInviteError,
  messageForTeamActionError,
  messageForTeamLoadError,
  validateAcceptance,
  validateInvite,
} from './messages';
import { hasTeam, rolesFor, upcomingAreas } from './roles';

// The roles an organization may give its people are the server's rule (BusinessModelRules.AllowedRoles, which the
// database repeats as a CHECK). The invite form and the role selects offer what rolesFor says, so they must agree.
const rules = readRepositoryFile('src/Platform.Auth/Services/BusinessModelRules.cs');

function serverRoles(model: 'Company' | 'Agency' | 'Creator'): string[] {
  const list = new RegExp(`${model}Roles\\s*=\\s*\\[([^\\]]*)\\]`, 's').exec(rules)?.[1] ?? '';
  return [...list.matchAll(/OrganizationRole\.(\w+)/g)].map((match) => (match[1] ?? '').toLowerCase());
}

describe('rolesFor', () => {
  it('finds the server rule to compare with', () => {
    expect(serverRoles('Company')).toHaveLength(3);
    expect(serverRoles('Agency')).toHaveLength(4);
    expect(serverRoles('Creator')).toHaveLength(1);
  });

  it('is the server rule, in the same order', () => {
    expect(rolesFor('company')).toEqual(serverRoles('Company'));
    expect(rolesFor('agency')).toEqual(serverRoles('Agency'));
    expect(rolesFor('creator')).toEqual(serverRoles('Creator'));
  });

  it('gives nothing for a kind of organization it does not know', () => {
    for (const model of ['', 'constructor', 'toString', undefined, null]) {
      expect(rolesFor(model), String(model)).toEqual([]);
    }
  });
});

describe('hasTeam', () => {
  it('is true for a company and a marketing agency, false for an individual creator and anything unknown', () => {
    expect(hasTeam('company')).toBe(true);
    expect(hasTeam('agency')).toBe(true);
    expect(hasTeam('creator')).toBe(false);
    expect(hasTeam('constructor')).toBe(false);
    expect(hasTeam(undefined)).toBe(false);
  });
});

describe('the dashboard', () => {
  it('no longer lists the team as coming soon: the Team page exists', () => {
    expect(upcomingAreas('organizationadmin').map((area) => area.title)).not.toContain('Team and permissions');
  });
});

describe('validateInvite', () => {
  it('asks for an address, a valid one, and a role, in that order', () => {
    expect(validateInvite('', 'contentwriter')).toEqual({ field: 'email', message: 'Enter your email address.' });
    expect(validateInvite('nobody', 'contentwriter')).toEqual({ field: 'email', message: 'Enter a valid email address.' });
    expect(validateInvite('a@b.co', '')).toEqual({ field: 'role', message: 'Choose a role for the new employee.' });
    expect(validateInvite(`${'a'.repeat(250)}@b.co`, 'contentwriter')?.field).toBe('email');
    expect(validateInvite('a@b.co', 'contentwriter')).toBeNull();
  });
});

describe('validateAcceptance', () => {
  it('asks for a name, and a password of 10 to 128 characters', () => {
    expect(validateAcceptance('  ', 'ten chars!!')).toEqual({ field: 'fullName', message: 'Enter your name.' });
    expect(validateAcceptance('x'.repeat(201), 'ten chars!!')?.field).toBe('fullName');
    expect(validateAcceptance('Ana', 'short')).toEqual({ field: 'password', message: 'Choose a password of at least 10 characters.' });
    expect(validateAcceptance('Ana', 'p'.repeat(129))?.field).toBe('password');
    expect(validateAcceptance('Ana', 'ten chars!!')).toBeNull();
  });
});

describe('the team wording', () => {
  const failure = (status: number, code: string | null = null, errors: Record<string, string[]> | null = null) => new ApiError(status, 'x', { code, errors });
  const offline = 'Could not reach the server. Check your connection and try again.';

  it('explains why an invitation could not be made', () => {
    expect(messageForInviteError(failure(409, 'email_taken'))).toMatch(/already has an account/);
    expect(messageForInviteError(failure(409, 'invitation_exists'))).toMatch(/already waiting/);
    expect(messageForInviteError(failure(409, 'team_not_available'))).toMatch(/works alone/);
    expect(messageForInviteError(failure(403, 'insufficient_permission'))).toMatch(/cannot manage the team/);
    expect(messageForInviteError(failure(400, null, { role: ['That role is not available in this kind of organization.'] }))).toMatch(/not available/);
    expect(messageForInviteError(failure(500))).toMatch(/could not be made/);
    expect(messageForInviteError(new Error('offline'))).toBe(offline);
  });

  it('explains why a role change, a removal or a cancellation did not go through', () => {
    expect(messageForTeamActionError(failure(409, 'last_organization_admin'))).toMatch(/at least one organization admin/);
    expect(messageForTeamActionError(failure(409, 'cannot_change_self'))).toMatch(/cannot change or remove yourself/);
    expect(messageForTeamActionError(failure(404, 'member_not_found'))).toMatch(/no longer in the organization/);
    expect(messageForTeamActionError(failure(404, 'invitation_not_found'))).toMatch(/not waiting any more/);
    expect(messageForTeamActionError(failure(400, 'role_not_allowed'))).toMatch(/not available in this kind/);
    expect(messageForTeamActionError(failure(403))).toMatch(/cannot manage the team/);
    expect(messageForTeamActionError(failure(429))).toMatch(/Too many requests/);
    expect(messageForTeamActionError(failure(500))).toMatch(/did not work/);
    expect(messageForTeamActionError('offline')).toBe(offline);
  });

  it('explains why the team could not be loaded', () => {
    expect(messageForTeamLoadError(failure(403))).toMatch(/cannot manage the team/);
    expect(messageForTeamLoadError(failure(429))).toMatch(/Too many requests/);
    expect(messageForTeamLoadError(failure(500))).toBe('The team could not be loaded.');
    expect(messageForTeamLoadError(new Error('offline'))).toBe(offline);
  });

  it('answers a link the server does not know, or that is used up, the same way', () => {
    const unknown = messageForInvitationPreviewError(failure(404, 'invitation_invalid'));

    expect(unknown).toMatch(/no longer valid/);
    expect(messageForAcceptError(failure(404, 'invitation_invalid'))).toBe(unknown);
    expect(messageForAcceptError(failure(409, 'email_taken'))).toMatch(/already has an account/);
    expect(messageForAcceptError(failure(400, null, { password: ['Too common.'] }))).toBe('Too common.');
    expect(messageForAcceptError(failure(500))).toMatch(/could not be accepted/);
    expect(messageForInvitationPreviewError(failure(429))).toMatch(/Too many requests/);
    expect(messageForInvitationPreviewError(failure(500))).toMatch(/could not be checked/);
  });
});
