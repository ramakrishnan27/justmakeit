import { describe, expect, it } from 'vitest';
import { ApiError, createApiClient } from '@/api/client';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, routedFetch } from '@/test/helpers';
import { businessModelLabel, countLabel, formatDate, formatNumber, planLabel } from './format';
import { googleLoginPath, googleRegisterPath, loadProviders, splitErrorCode } from './google';
import {
  messageForGoogleError,
  messageForRegisterError,
  messageForSignInError,
  messageForWorkspaceError,
  messageForWorkspacesLoadError,
  validateRegistration,
  validateSignIn,
  type RegistrationForm,
} from './messages';
import { abilities, allowsSeveralWorkspaces, businessModels, organizationNameLabel, responsibilities, roleTitle, upcomingAreas, workspaceMenuLabel, workspaceWords } from './roles';

describe('format', () => {
  it('formats numbers, and shows a dash for what is not one', () => {
    expect(formatNumber(1234)).toBe('1,234');
    expect(formatNumber(Number.NaN)).toBe('–');
  });

  it('formats a date in the given time zone, with the time on request', () => {
    expect(formatDate('2026-03-05T10:30:00Z', { timeZone: 'UTC' })).toBe('Mar 5, 2026');
    expect(formatDate('2026-03-05T10:30:00Z', { timeZone: 'UTC', withTime: true })).toMatch(/Mar 5, 2026.*10:30/);
    for (const bad of [undefined, null, 'not a date']) {
      expect(formatDate(bad), String(bad)).toBe('–');
    }
  });

  it('labels plans and business models, and shows an unknown one as given', () => {
    expect(planLabel('trial')).toBe('Trial');
    expect(planLabel('')).toBe('–');
    expect(businessModelLabel('agency')).toBe('Marketing agency');
    expect(businessModelLabel('franchise')).toBe('Franchise');
    expect(businessModelLabel('constructor')).toBe('Constructor');
    expect(businessModelLabel(undefined)).toBe('–');
  });

  it('counts', () => {
    expect(countLabel(1, 'workspace')).toBe('1 workspace');
    expect(countLabel(3, 'workspace')).toBe('3 workspaces');
    expect(countLabel(0, 'client workspace')).toBe('0 client workspaces');
  });
});

describe('roles', () => {
  it('offers the three kinds of organization', () => {
    expect(businessModels.map((model) => model.value)).toEqual(['company', 'agency', 'creator']);
  });

  it('titles a role, and shows an unknown one as given', () => {
    expect(roleTitle('organizationadmin')).toBe('Organization Admin');
    expect(roleTitle('auditor')).toBe('auditor');
    expect(roleTitle('constructor')).toBe('constructor');
    expect(roleTitle(undefined)).toBe('–');
  });

  it('says what a role is for in each kind of organization, and nothing for a pair that does not exist', () => {
    expect(responsibilities('company', 'organizationadmin')).toMatch(/company products/);
    expect(responsibilities('agency', 'clientmanager')).toMatch(/client/);
    expect(responsibilities('creator', 'contentwriter')).toMatch(/personal publishing/);
    expect(responsibilities('creator', 'clientmanager')).toBeNull();
    expect(responsibilities('company', 'clientmanager')).toBeNull();
    expect(responsibilities('toString', 'constructor')).toBeNull();
  });

  it('lists what is still to come for a role, and nothing for an unknown one', () => {
    for (const role of ['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter']) {
      expect(upcomingAreas(role).length, role).toBeGreaterThan(0);
    }
    expect(upcomingAreas('constructor')).toEqual([]);
    expect(upcomingAreas(undefined)).toEqual([]);
  });

  it('words workspaces for the kind of organization', () => {
    expect(workspaceWords('agency')).toEqual({ one: 'client', many: 'Clients' });
    expect(workspaceWords('company')).toEqual({ one: 'product', many: 'Products' });
    expect(workspaceWords('creator')).toEqual({ one: 'workspace', many: 'Your workspace' });
    expect(workspaceWords(null)).toEqual({ one: 'workspace', many: 'Workspaces' });
    expect(allowsSeveralWorkspaces('creator')).toBe(false);
    expect(allowsSeveralWorkspaces('agency')).toBe(true);
    expect(organizationNameLabel('agency')).toBe('Agency name');
    expect(organizationNameLabel('')).toBe('Organization name');
  });

  it('words the sidebar link by kind of organization, and keeps a label the Site admin renamed', () => {
    expect(workspaceMenuLabel('company', 'Workspaces')).toBe('Products');
    expect(workspaceMenuLabel('agency', 'Workspaces')).toBe('Clients');
    expect(workspaceMenuLabel('creator', 'Workspaces')).toBe('Workspaces');
    expect(workspaceMenuLabel('company', 'Brands')).toBe('Brands');
    expect(workspaceMenuLabel(null, 'Workspaces')).toBe('Workspaces');
  });

  it('reads the three baseline abilities from the permission list, and allows nothing without one', () => {
    expect(abilities([PERMISSIONS.dataRead, PERMISSIONS.dataWrite])).toEqual({ read: true, write: true, manage: false });
    expect(abilities([PERMISSIONS.organizationManage])).toEqual({ read: false, write: false, manage: true });
    expect(abilities(undefined)).toEqual({ read: false, write: false, manage: false });
  });
});

describe('sign-in and sign-up wording', () => {
  const form: RegistrationForm = { businessModel: 'company', organizationName: 'Northwind', method: 'email', fullName: '', email: 'ana@example.com', password: 'ten chars!!' };

  it('checks the sign-in form before anything is sent', () => {
    expect(validateSignIn('', 'x')?.field).toBe('email');
    expect(validateSignIn('nobody', 'x')?.message).toBe('Enter a valid email address.');
    expect(validateSignIn('a@b.co', '')?.field).toBe('password');
    expect(validateSignIn('a@b.co', 'x')).toBeNull();
  });

  it('checks the sign-up form, one problem at a time and in the order of the form', () => {
    expect(validateRegistration({ ...form, businessModel: '' })?.field).toBe('businessModel');
    expect(validateRegistration({ ...form, organizationName: '  ' })?.field).toBe('organizationName');
    expect(validateRegistration({ ...form, organizationName: 'x'.repeat(201) })?.field).toBe('organizationName');
    expect(validateRegistration({ ...form, method: '' })?.field).toBe('method');
    expect(validateRegistration({ ...form, email: 'nope' })?.field).toBe('email');
    expect(validateRegistration({ ...form, password: 'short' })?.field).toBe('password');
    expect(validateRegistration({ ...form, password: 'x'.repeat(129) })?.field).toBe('password');
    expect(validateRegistration(form)).toBeNull();
  });

  it('needs only the organization when Google does the rest', () => {
    expect(validateRegistration({ ...form, method: 'google', email: '', password: '' })).toBeNull();
  });

  it('tells the person what went wrong, and no more than the API did', () => {
    expect(messageForSignInError(new ApiError(401, 'x'))).toBe('Email or password is incorrect.');
    expect(messageForSignInError(new ApiError(429, 'x'))).toMatch(/Too many attempts/);
    expect(messageForSignInError(new ApiError(400, 'x', { errors: { email: ['Enter a valid email address.'] } }))).toBe('Enter a valid email address.');
    expect(messageForSignInError(new ApiError(500, 'x'))).toMatch(/Sign-in failed/);
    expect(messageForSignInError(new TypeError('network down'))).toMatch(/Could not reach the server/);

    expect(messageForRegisterError(new ApiError(409, 'x'))).toMatch(/already has an account/);
    expect(messageForRegisterError(new ApiError(500, 'x'))).toMatch(/could not be created/);
    expect(messageForWorkspacesLoadError(new ApiError(429, 'x'))).toMatch(/Too many requests/);
    expect(messageForWorkspacesLoadError(new ApiError(500, 'x'))).toMatch(/could not be loaded/);
    expect(messageForWorkspaceError(new ApiError(403, 'x'))).toMatch(/cannot add workspaces/);
    expect(messageForWorkspaceError(new ApiError(409, 'x'))).toMatch(/has one workspace/);
    expect(messageForWorkspaceError(new Error('x'))).toMatch(/could not be added/);
  });

  it('explains what Google sent the person back with, and says nothing when there is no error', () => {
    expect(messageForGoogleError('google_denied')).toBe('Google sign-in was cancelled.');
    expect(messageForGoogleError('something_new')).toBe('Google sign-in did not work. Try again.');
    expect(messageForGoogleError('constructor')).toBe('Google sign-in did not work. Try again.');
    expect(messageForGoogleError(null)).toBeNull();
    expect(messageForGoogleError('')).toBeNull();
  });
});

describe('google', () => {
  it('points the buttons at our own server', () => {
    expect(googleLoginPath).toBe('/v1/auth/google/start?intent=login');
    const path = googleRegisterPath({ businessModel: 'agency', organizationName: '  Acme & Sons  ' });
    const params = new URL(path, 'http://localhost').searchParams;
    expect(params.get('intent')).toBe('register');
    expect(params.get('businessModel')).toBe('agency');
    expect(params.get('organizationName')).toBe('Acme & Sons');
  });

  it('takes the error code out of the query and keeps the rest', () => {
    expect(splitErrorCode('?error=google_denied')).toEqual({ code: 'google_denied', rest: '' });
    expect(splitErrorCode('?a=1&error=x&b=2')).toEqual({ code: 'x', rest: '?a=1&b=2' });
    expect(splitErrorCode('')).toEqual({ code: null, rest: '' });
  });

  it('offers Google only when the server says it is set up', async () => {
    const answer = (body: unknown) => createApiClient({ fetchFn: routedFetch({ 'GET /v1/auth/providers': () => jsonResponse(200, body) }) });

    expect(await loadProviders(answer({ google: true }))).toEqual({ google: true });
    expect(await loadProviders(answer({ google: 'yes' }))).toEqual({ google: false });
    expect(await loadProviders(answer(null))).toEqual({ google: false });
    expect(await loadProviders(createApiClient({ fetchFn: routedFetch({}) }))).toEqual({ google: false }); // the server does not answer
  });
});
