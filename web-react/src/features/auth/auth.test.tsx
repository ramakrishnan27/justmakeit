import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { jsonResponse, requestLine } from '@/test/helpers';
import { renderApp, visitorServer } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const type = (label: string | RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
const alert = () => screen.getByRole('alert', { hidden: true });

describe('the sign-in page', () => {
  it('asks for the missing field before it sends anything', async () => {
    const { fetchFn } = await renderApp('/login', visitorServer());
    await heading('Welcome back');

    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('alert').textContent).toBe('Enter your email address.');
    expect(document.activeElement).toBe(screen.getByLabelText('Email'));

    type('Email', 'ana@example.com');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('alert').textContent).toBe('Enter your password.');
    expect(document.activeElement).toBe(screen.getByLabelText('Password'));

    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/auth/login');
  });

  it('says so, clears the password and focuses it when the credentials are wrong', async () => {
    await renderApp('/login', visitorServer({ 'POST /v1/auth/login': () => jsonResponse(401, { code: 'invalid_credentials' }) }));
    await heading('Welcome back');

    type('Email', 'ana@example.com');
    type('Password', 'wrong password');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('Email or password is incorrect.'));
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('');
    expect(document.activeElement).toBe(screen.getByLabelText('Password'));
    expect(screen.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeTruthy();
  });

  it('signs in and shows the dashboard, sending the trimmed email and the password as typed', async () => {
    // visitorServer() is a server for a browser with no sign-in cookie: it answers 401 until the sign-in works.
    const { fetchFn } = await renderApp('/login', visitorServer());
    await heading('Welcome back');

    type('Email', '  ana@example.com ');
    type('Password', ' a password ');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await heading('Welcome, Ana')).toBeTruthy();
    const login = fetchFn.calls.find((call) => call.path === '/v1/auth/login');
    expect(JSON.parse(login!.init.body as string)).toEqual({ email: 'ana@example.com', password: ' a password ' });
  });

  it('shows the password only while Show is on', async () => {
    await renderApp('/login', visitorServer());
    await heading('Welcome back');
    const password = screen.getByLabelText('Password') as HTMLInputElement;

    expect(password.type).toBe('password');
    fireEvent.click(screen.getByRole('button', { name: 'Show' }));
    expect(password.type).toBe('text');
    expect(screen.getByRole('button', { name: 'Hide' }).getAttribute('aria-pressed')).toBe('true');
    fireEvent.click(screen.getByRole('button', { name: 'Hide' }));
    expect(password.type).toBe('password');
  });

  it('offers Continue with Google only when the server has it set up', async () => {
    const without = await renderApp('/login', visitorServer());
    await heading('Welcome back');
    await waitFor(() => expect(without.fetchFn.calls.map(requestLine)).toContain('GET /v1/auth/providers'));
    expect(screen.queryByRole('link', { name: 'Continue with Google' })).toBeNull();
    without.unmount();

    await renderApp('/login', visitorServer({ 'GET /v1/auth/providers': () => jsonResponse(200, { google: true }) }));
    const link = await screen.findByRole('link', { name: 'Continue with Google' });
    expect(link.getAttribute('href')).toBe('/v1/auth/google/start?intent=login');
  });

  it('explains what Google sent the person back with, and takes it out of the address', async () => {
    await renderApp('/login?error=google_email_taken&next=1', visitorServer());

    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/already has an account/));
    // Only the code goes: a reload must not show the message again, and nothing else in the query is touched.
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login?next=1'));
    expect(screen.getByRole('alert').textContent).toMatch(/already has an account/);
  });

  it('links to the sign-up page', async () => {
    await renderApp('/login', visitorServer());
    await heading('Welcome back');

    fireEvent.click(screen.getByRole('link', { name: 'Create an account' }));

    expect(await heading('Create your account')).toBeTruthy();
  });
});

describe('the sign-up page', () => {
  const chooseKind = (title: string) => fireEvent.click(screen.getByLabelText(new RegExp(title)));

  it('asks for the kind of organization, then names its name for that kind', async () => {
    await renderApp('/register', visitorServer());
    await heading('Create your account');

    expect(screen.getByLabelText('Organization name')).toBeTruthy();
    chooseKind('Marketing agency');
    expect(screen.getByLabelText('Agency name')).toBeTruthy();
    chooseKind('Individual creator');
    expect(screen.getByLabelText('Your name, brand or channel')).toBeTruthy();
  });

  it('shows the email fields at once when the server has no Google, and no choice of method', async () => {
    await renderApp('/register', visitorServer());
    await heading('Create your account');

    expect(await screen.findByLabelText('Email')).toBeTruthy();
    expect(screen.getByLabelText('Password')).toBeTruthy();
    expect(screen.queryByText('How do you want to sign up?')).toBeNull();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeTruthy();
  });

  it('with Google on offer, preselects neither method and shows the fields for the one chosen', async () => {
    await renderApp('/register', visitorServer({ 'GET /v1/auth/providers': () => jsonResponse(200, { google: true }) }));
    await heading('Create your account');
    await screen.findByText('How do you want to sign up?');

    expect(screen.queryByLabelText('Email')).toBeNull();
    expect(screen.queryByRole('button', { name: /Create account|Continue with Google/ })).toBeNull();

    fireEvent.click(screen.getByLabelText('Email and password'));
    expect(screen.getByLabelText('Email')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Create account' })).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Continue with Google'));
    expect(screen.queryByLabelText('Email')).toBeNull();
    expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeTruthy();
  });

  it('points at the first thing missing, and sends nothing', async () => {
    const { fetchFn } = await renderApp('/register', visitorServer());
    await screen.findByLabelText('Email');

    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(alert().textContent).toBe('Choose what kind of organization this is.');

    chooseKind('Company');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(alert().textContent).toBe('Enter the name of your organization.');
    expect(document.activeElement).toBe(screen.getByLabelText('Company name'));

    type('Company name', 'Northwind Foods');
    type('Email', 'ana@example.com');
    type('Password', 'short');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(alert().textContent).toBe('Choose a password of at least 10 characters.');
    expect(document.activeElement).toBe(screen.getByLabelText('Password'));

    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/auth/register');
  });

  it('creates the account with the organization and the kind of business, and shows the dashboard', async () => {
    const { fetchFn } = await renderApp('/register', visitorServer());
    await screen.findByLabelText('Email');

    chooseKind('Company');
    type('Company name', '  Northwind Foods ');
    type('Your name (optional)', ' Ana Smith ');
    type('Email', ' ana@example.com ');
    type('Password', 'ten chars!!');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await heading('Welcome, Ana')).toBeTruthy();
    const register = fetchFn.calls.find((call) => call.path === '/v1/auth/register');
    expect(JSON.parse(register!.init.body as string)).toEqual({
      email: 'ana@example.com',
      password: 'ten chars!!',
      fullName: 'Ana Smith',
      organizationName: 'Northwind Foods',
      businessModel: 'company',
    });
  });

  it('says the email is taken, and clears the password', async () => {
    await renderApp('/register', visitorServer({ 'POST /v1/auth/register': () => jsonResponse(409, { code: 'email_taken' }) }));
    await screen.findByLabelText('Email');

    chooseKind('Individual creator');
    type('Your name, brand or channel', 'Ana');
    type('Email', 'ana@example.com');
    type('Password', 'ten chars!!');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

    await waitFor(() => expect(alert().textContent).toMatch(/already has an account/));
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('');
  });

  it('lets the message go once the person starts fixing it', async () => {
    await renderApp('/register', visitorServer());
    await screen.findByLabelText('Email');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(screen.getByRole('alert').textContent).not.toBe('');

    fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'N' } });

    expect(alert().hasAttribute('hidden')).toBe(true);
  });
});
