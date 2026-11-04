import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { InvitationPreview } from '@/api/types';
import { jsonResponse, requestLine, type Handler, type RecordedCall } from '@/test/helpers';
import { memberServer, renderApp, visitorServer } from '@/test/renderApp';

const secret = 'abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE';
const preview: InvitationPreview = {
  organizationName: 'Northwind Foods',
  businessModel: 'company',
  role: 'marketingmanager',
  email: 'dev@example.com',
  expiresAt: '2030-01-15T12:00:00Z',
};

const bodyOf = (call: RecordedCall) => JSON.parse(call.init.body as string);

function server(extra: Record<string, Handler> = {}) {
  return visitorServer({
    'POST /v1/auth/invitations/preview': () => jsonResponse(200, preview),
    ...extra,
  });
}

const open = (hash = `#${secret}`, extra: Record<string, Handler> = {}) => renderApp(`/accept-invite${hash}`, server(extra));

function fill(name: string, password: string) {
  fireEvent.change(screen.getByLabelText('Your name'), { target: { value: name } });
  fireEvent.change(screen.getByLabelText('Choose a password'), { target: { value: password } });
}

describe('the accept-invitation page', () => {
  it('shows who invited the person and in which role, and their address, before asking for anything', async () => {
    await open();

    expect(await screen.findByRole('heading', { level: 1, name: 'Join Northwind Foods' })).toBeTruthy();
    expect(screen.getByText(/You were invited as Marketing Manager/)).toBeTruthy();
    expect(screen.getByText(/The invitation ends Jan 15, 2030/)).toBeTruthy();
    const email = screen.getByLabelText('Your email address') as HTMLInputElement;
    expect(email.value).toBe('dev@example.com');
    expect(email.readOnly).toBe(true);
  });

  it('sends the secret in the body of a POST, never in an address, and takes it out of the address bar', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { name: 'Join Northwind Foods' });

    const call = fetchFn.calls.find((candidate) => requestLine(candidate) === 'POST /v1/auth/invitations/preview')!;
    expect(bodyOf(call)).toEqual({ token: secret });
    expect(fetchFn.calls.every((candidate) => !candidate.path.includes(secret))).toBe(true);
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/accept-invite'));
  });

  it('is open to a signed-in person too, and tells them that accepting replaces their sign-in', async () => {
    await renderApp(`/accept-invite#${secret}`, memberServer({}, { 'POST /v1/auth/invitations/preview': () => jsonResponse(200, preview) }));

    expect(await screen.findByRole('heading', { name: 'Join Northwind Foods' })).toBeTruthy();
    expect(screen.getByText(/signs you out of the account you are using now/)).toBeTruthy();
  });

  it('asks for a name and a password of at least 10 characters before it sends anything', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { name: 'Join Northwind Foods' });

    fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));
    expect(screen.getByRole('alert').textContent).toBe('Enter your name.');
    expect(document.activeElement).toBe(screen.getByLabelText('Your name'));

    fill('Dev Kumar', 'short');
    fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));
    expect(screen.getByRole('alert').textContent).toBe('Choose a password of at least 10 characters.');
    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/auth/invitations/accept');
  });

  it('creates the account, signs the person in and takes them to the dashboard', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { name: 'Join Northwind Foods' });

    fill('  Dev Kumar ', 'a long enough password');
    fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/dashboard'));
    const accept = fetchFn.calls.find((candidate) => requestLine(candidate) === 'POST /v1/auth/invitations/accept')!;
    expect(bodyOf(accept)).toEqual({ token: secret, fullName: 'Dev Kumar', password: 'a long enough password' });
    expect(await screen.findByRole('heading', { level: 1, name: /Welcome/ })).toBeTruthy();
  });

  it.each([
    [409, { code: 'email_taken' }, /already has an account/],
    [429, { code: 'rate_limited' }, /Too many attempts/],
    [400, { errors: { password: ['The password is too common.'] } }, /too common/],
    [500, {}, /could not be accepted/],
  ])('tells the person when the server answers %i, and lets them try again', async (status, body, message) => {
    await open(`#${secret}`, { 'POST /v1/auth/invitations/accept': () => jsonResponse(status, body) });
    await screen.findByRole('heading', { name: 'Join Northwind Foods' });
    fill('Dev Kumar', 'a long enough password');

    fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(message));
    expect((screen.getByRole('button', { name: 'Create my account' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('says the link is no longer valid when the server does not know it, and shows no form', async () => {
    await open(`#${secret}`, { 'POST /v1/auth/invitations/preview': () => jsonResponse(404, { code: 'invitation_invalid' }) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer valid.*Ask your organization admin for a new link/);
    expect(screen.queryByLabelText('Your name')).toBeNull();
    expect(screen.getByRole('link', { name: 'Sign in' }).getAttribute('href')).toBe('/login');
  });

  it('switches to the same message when the invitation is used up between the preview and the submit', async () => {
    await open(`#${secret}`, { 'POST /v1/auth/invitations/accept': () => jsonResponse(404, { code: 'invitation_invalid' }) });
    await screen.findByRole('heading', { name: 'Join Northwind Foods' });
    fill('Dev Kumar', 'a long enough password');

    fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer valid/);
    expect(screen.queryByLabelText('Your name')).toBeNull();
  });

  it.each([['no link at all', ''], ['an empty one', '#'], ['one that cannot be a secret', '#<script>alert(1)</script>'], ['one that is too long', `#${'a'.repeat(201)}`]])(
    'asks for the link from the invitation, and calls no server, when the address has %s',
    async (_label, hash) => {
      const { fetchFn } = await open(hash);

      expect((await screen.findByRole('alert')).textContent).toMatch(/needs the link from your invitation/);
      expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/auth/invitations/preview');
      expect(screen.queryByLabelText('Your name')).toBeNull();
    },
  );

  it('says it could not check the invitation when the server cannot be reached', async () => {
    await open(`#${secret}`, { 'POST /v1/auth/invitations/preview': () => jsonResponse(500, {}) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be checked/);
  });
});
