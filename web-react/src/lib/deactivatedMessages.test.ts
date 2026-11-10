import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import { readRepositoryFile } from '@/test/repo';
import { messageForGoogleError, messageForSignInError } from './messages';
import {
  messageForOrganizationChannelError,
  messageForOrganizationChannelsLoadError,
  messageForOrganizationStatusError,
  statusChangedText,
} from './organizationAdmin';

describe('what signing in says to a person whose account or organization was deactivated', () => {
  it('names the organization when the Site admin deactivated it, and the account when an organization admin did', () => {
    expect(messageForSignInError(new ApiError(403, 'x', { code: 'organization_deactivated' }))).toBe(
      'Your organization has been deactivated. Contact the platform administrator.',
    );
    expect(messageForSignInError(new ApiError(403, 'x', { code: 'account_deactivated' }))).toBe(
      'Your account has been deactivated. Ask your organization admin to activate it.',
    );
  });

  it('says the same through Google, in the same words', () => {
    expect(messageForGoogleError('google_organization_deactivated')).toBe(messageForSignInError(new ApiError(403, 'x', { code: 'organization_deactivated' })));
    expect(messageForGoogleError('google_account_deactivated')).toBe(messageForSignInError(new ApiError(403, 'x', { code: 'account_deactivated' })));
  });

  it('has words for every error code the server sends back from Google (the server source is the list)', () => {
    const source = readRepositoryFile('src/Platform.Core/Auth/Services/Google/GoogleSignInService.cs');
    const codes = [...source.matchAll(/public const string \w+ = "(google_[a-z_]+)"/g)].map((match) => match[1] as string);

    expect(codes).toContain('google_organization_deactivated');
    expect(codes).toContain('google_account_deactivated');
    for (const code of codes) {
      // google_unavailable is the one the page never receives from the callback, but it has words too.
      expect(messageForGoogleError(code), code).not.toBe('Google sign-in did not work. Try again.');
    }
  });
});

describe('what the Site admin is told about an organization', () => {
  it('words a channel that could not be changed', () => {
    expect(messageForOrganizationChannelError(new ApiError(500, 'x'), 'SMS')).toBe('SMS could not be changed. Try again in a moment.');
    expect(messageForOrganizationChannelError(new ApiError(404, 'x', { code: 'channel_not_found' }), 'SMS')).toBe('SMS no longer exists. Reload the list.');
    expect(messageForOrganizationChannelError(new ApiError(404, 'x', { code: 'organization_not_found' }), 'SMS')).toMatch(/organization no longer exists/);
    expect(messageForOrganizationChannelError(new ApiError(403, 'x'), 'SMS')).toMatch(/no longer a site admin/);
    expect(messageForOrganizationChannelError(new ApiError(429, 'x'), 'SMS')).toMatch(/Wait a minute/);
    expect(messageForOrganizationChannelError(new TypeError('down'), 'SMS')).toMatch(/Could not reach the server/);
  });

  it('words the channels that could not be loaded', () => {
    expect(messageForOrganizationChannelsLoadError(new ApiError(500, 'x'))).toBe('The channels could not be loaded.');
    expect(messageForOrganizationChannelsLoadError(new ApiError(404, 'x', { code: 'organization_not_found' }))).toMatch(/no longer exists/);
    expect(messageForOrganizationChannelsLoadError(new ApiError(403, 'x'))).toMatch(/no longer a site admin/);
    expect(messageForOrganizationChannelsLoadError(new ApiError(429, 'x'))).toMatch(/Wait a minute/);
    expect(messageForOrganizationChannelsLoadError(new TypeError('down'))).toMatch(/Could not reach the server/);
  });

  it('words a status change that failed, by whether it was a deactivation or an activation', () => {
    expect(messageForOrganizationStatusError(new ApiError(500, 'x'), 'Acme', false)).toBe('Acme could not be deactivated. Try again in a moment.');
    expect(messageForOrganizationStatusError(new ApiError(500, 'x'), 'Acme', true)).toBe('Acme could not be activated. Try again in a moment.');
    expect(messageForOrganizationStatusError(new ApiError(404, 'x', { code: 'organization_not_found' }), 'Acme', false)).toBe('Acme no longer exists. Reload the list.');
    expect(messageForOrganizationStatusError(new TypeError('down'), 'Acme', false)).toMatch(/Could not reach the server/);
  });

  it('says what a deactivation did, which is to sign the members out, and what an activation did', () => {
    expect(statusChangedText('Acme', false)).toBe('Acme was deactivated. Its members were signed out and cannot sign in until you activate it.');
    expect(statusChangedText('Acme', true)).toBe('Acme is active again. Its members can sign in.');
  });
});

describe('the deactivation, and the server', () => {
  it('has the routes the page calls', () => {
    const controller = readRepositoryFile('src/Platform.Core/Auth/Controllers/SiteOrganizationsController.cs');

    expect(controller).toContain('[Route("v1/site/agencies")]');
    expect(controller).toContain('[HttpGet("{agencyId:guid}/channels")]');
    expect(controller).toContain('[HttpPut("{agencyId:guid}/channels/{channelId:guid}")]');
    expect(controller).toContain('[HttpPut("{agencyId:guid}/status")]');
  });

  it('sends the codes the page words, from the server\'s own source', () => {
    const auth = readRepositoryFile('src/Platform.Core/Auth/Services/AuthService.cs');
    const repository = readRepositoryFile('src/Platform.Core/Auth/Repositories/SiteChannelRepository.cs');

    expect(auth).toContain('"organization_deactivated"');
    expect(auth).toContain('"account_deactivated"');
    expect(repository).toContain('"organization_not_found"');
    expect(repository).toContain('"channel_not_found"');
  });
});
