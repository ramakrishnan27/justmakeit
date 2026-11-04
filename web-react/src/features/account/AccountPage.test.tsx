import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adminApp } from '@/navigation/apps';
import { jsonResponse, requestLine, type Handler } from '@/test/helpers';
import { adminServer, memberServer, renderApp } from '@/test/renderApp';

const CHANGE = 'POST /v1/auth/change-password';

const open = (extra: Record<string, Handler> = {}, permissions?: string[]) =>
  renderApp('/account', memberServer(permissions ? { permissions } : {}, { [CHANGE]: () => jsonResponse(200, { expiresAt: '2030-02-01T00:00:00Z' }), ...extra }));

function fill(current: string, next: string, confirm = next) {
  fireEvent.change(screen.getByLabelText('Current password'), { target: { value: current } });
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: next } });
  fireEvent.change(screen.getByLabelText('Type the new password again'), { target: { value: confirm } });
}

const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Change password' }));
const bodyOf = (call: { init: RequestInit }) => JSON.parse(call.init.body as string);

describe('the account page', () => {
  it('names the person and offers to change the password', async () => {
    await open();

    expect(await screen.findByRole('heading', { level: 1, name: 'Account' })).toBeTruthy();
    expect(screen.getByText('Signed in as ana@example.com.')).toBeTruthy();
    expect(screen.getByRole('heading', { level: 2, name: 'Change password' })).toBeTruthy();
  });

  it('is open to every member, whatever permissions their token holds', async () => {
    await open({}, []);

    expect(await screen.findByRole('heading', { level: 1, name: 'Account' })).toBeTruthy();
  });

  it('is reached from a link under the menu, which marks the page when it is open', async () => {
    await renderApp('/dashboard', memberServer());

    const link = await screen.findByRole('link', { name: 'Account' });
    expect(link.getAttribute('href')).toBe('/account');
    expect(link.getAttribute('aria-current')).toBeNull();

    fireEvent.click(link);

    expect(await screen.findByRole('heading', { level: 1, name: 'Account' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Account' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('navigation', { name: 'Account' })).toBeTruthy();
  });

  it('is not offered in the Site admin app, which has no account page', async () => {
    await renderApp('/admin', adminServer(), adminApp);
    await screen.findByRole('heading', { level: 1, name: 'Dashboard' });

    expect(screen.queryByRole('link', { name: 'Account' })).toBeNull();
    expect(screen.queryByRole('navigation', { name: 'Account' })).toBeNull();
  });

  it('asks the browser to keep the new password for this address, and not to autofill the old one into the wrong field', async () => {
    await open();
    await screen.findByRole('heading', { name: 'Account' });

    expect(screen.getByLabelText('Current password').getAttribute('autocomplete')).toBe('current-password');
    expect(screen.getByLabelText('New password').getAttribute('autocomplete')).toBe('new-password');
    expect(screen.getByLabelText('Type the new password again').getAttribute('autocomplete')).toBe('new-password');
    const username = document.querySelector('input[name=username]') as HTMLInputElement;
    expect(username.value).toBe('ana@example.com');
    expect(username.getAttribute('aria-hidden')).toBe('true');
  });
});

describe('changing the password', () => {
  it.each([
    ['', 'a brand new password', 'a brand new password', 'Enter your current password.', 'Current password'],
    ['current one', 'short', 'short', 'Choose a password of at least 10 characters.', 'New password'],
    ['current password', 'current password', 'current password', 'Choose a password you are not using now.', 'New password'],
    ['current one', 'a brand new password', 'a brand new passwore', 'The two new passwords do not match.', 'Type the new password again'],
  ])('checks the form before it sends anything (%#)', async (current, next, confirm, message, field) => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { name: 'Account' });

    fill(current, next, confirm);
    submit();

    expect(screen.getByRole('alert').textContent).toBe(message);
    expect(document.activeElement).toBe(screen.getByLabelText(field));
    expect(fetchFn.calls.map(requestLine)).not.toContain(CHANGE);
  });

  it('sends the current and the new password, says so, and forgets what was typed', async () => {
    const { fetchFn } = await open();
    await screen.findByRole('heading', { name: 'Account' });

    fill('current one 1', 'a brand new password');
    submit();

    expect(await screen.findByText(/Everywhere else you were signed in/)).toBeTruthy();
    const call = fetchFn.calls.find((candidate) => requestLine(candidate) === CHANGE)!;
    expect(bodyOf(call)).toEqual({ currentPassword: 'current one 1', newPassword: 'a brand new password' }); // the repeat never leaves the page
    for (const label of ['Current password', 'New password', 'Type the new password again']) {
      expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe('');
    }
    expect(document.body.innerHTML).not.toContain('a brand new password');
    expect((screen.getByRole('button', { name: 'Change password' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('takes the success message away again as soon as the person tries once more', async () => {
    await open();
    await screen.findByRole('heading', { name: 'Account' });
    fill('current one 1', 'a brand new password');
    submit();
    await screen.findByText(/Everywhere else you were signed in/);

    submit(); // an empty form this time

    expect(screen.queryByText(/Everywhere else you were signed in/)).toBeNull();
    expect(screen.getByRole('alert').textContent).toBe('Enter your current password.');
  });

  it.each([
    [400, { errors: { currentPassword: ['Current password is incorrect.'] } }, 'Current password is incorrect.', 'Current password'],
    [400, { errors: { newPassword: ['Password must be at least 10 characters.'] } }, 'Password must be at least 10 characters.', 'New password'],
    [409, { code: 'no_password' }, 'This account signs in with Google and has no password to change.', null],
    [409, { code: 'password_changed' }, 'Your password was changed somewhere else a moment ago. Try again.', null],
    [429, { code: 'rate_limited' }, 'Too many requests. Wait a minute, then try again.', null],
    [500, {}, 'The password could not be changed. Try again in a moment.', null],
  ])('tells the person when the server answers %i, keeps what they typed, and lets them try again', async (status, body, message, field) => {
    await open({ [CHANGE]: () => jsonResponse(status, body) });
    await screen.findByRole('heading', { name: 'Account' });
    fill('current one 1', 'a brand new password');

    submit();

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(message));
    if (field) {
      expect(document.activeElement).toBe(screen.getByLabelText(field));
    }
    expect((screen.getByLabelText('New password') as HTMLInputElement).value).toBe('a brand new password');
    expect((screen.getByRole('button', { name: 'Change password' }) as HTMLButtonElement).disabled).toBe(false);
    expect(screen.queryByText(/Everywhere else you were signed in/)).toBeNull();
  });

  it('says so when the server cannot be reached', async () => {
    await open({
      [CHANGE]: () => {
        throw new TypeError('Failed to fetch');
      },
    });
    await screen.findByRole('heading', { name: 'Account' });
    fill('current one 1', 'a brand new password');

    submit();

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/Could not reach the server/));
  });

  it('sends a person whose session has ended to the sign-in page, as every signed-in call does', async () => {
    await open({ [CHANGE]: () => jsonResponse(401, { code: 'session_ended' }) });
    await screen.findByRole('heading', { name: 'Account' });
    fill('current one 1', 'a brand new password');

    submit();

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login'));
  });

  it('cannot be sent twice while a request is on its way', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { fetchFn } = await open({
      [CHANGE]: async () => {
        await gate;
        return jsonResponse(200, { expiresAt: '2030-02-01T00:00:00Z' });
      },
    });
    await screen.findByRole('heading', { name: 'Account' });
    fill('current one 1', 'a brand new password');

    submit();
    await waitFor(() => expect((screen.getByRole('button', { name: 'Change password' }) as HTMLButtonElement).disabled).toBe(true));
    submit();
    release();

    await screen.findByText(/Everywhere else you were signed in/);
    expect(fetchFn.calls.map(requestLine).filter((line) => line === CHANGE)).toHaveLength(1);
  });
});
