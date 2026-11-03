import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adminApp } from '@/navigation/apps';
import { jsonResponse, requestLine } from '@/test/helpers';
import { adminServer, adminVisitorServer, organization, paged, renderApp } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const type = (label: string | RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
const open = (path: string, server: ReturnType<typeof adminServer>) => renderApp(path, server, adminApp);
const location = () => screen.getByTestId('location').textContent;
const noMenu = () => {
  expect(screen.queryByRole('navigation', { hidden: true })).toBeNull();
  expect(document.getElementById('shell-nav')).toBeNull();
};

describe('the Site admin sign-in page', () => {
  it('is a bare page: the heading and the form, with no header, no menu and no sign-up', async () => {
    await open('/admin/login', adminVisitorServer());

    expect(await heading('Site admin sign in')).toBeTruthy();
    expect(document.title).toBe('Sign in · Site admin');
    noMenu();
    expect(screen.queryByRole('button', { name: 'Sign out' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Create an account' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Continue with Google' })).toBeNull();
  });

  it('asks for the missing field before it sends anything', async () => {
    const { fetchFn } = await open('/admin/login', adminVisitorServer());
    await heading('Site admin sign in');

    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('alert').textContent).toBe('Enter your email address.');
    expect(document.activeElement).toBe(screen.getByLabelText('Email'));

    type('Email', 'root@example.com');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('alert').textContent).toBe('Enter your password.');

    expect(fetchFn.calls.map(requestLine)).not.toContain('POST /v1/site/login');
  });

  it('signs in and shows the Site admin dashboard in the frame, with the menu', async () => {
    const { fetchFn } = await open('/admin/login', adminVisitorServer());
    await heading('Site admin sign in');

    type('Email', ' root@example.com ');
    type('Password', 'a password');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await heading('Dashboard')).toBeTruthy();
    expect(location()).toBe('/admin');
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();
    expect(screen.getByText('Site admin', { selector: '.badge' })).toBeTruthy();

    const login = fetchFn.calls.find((call) => call.path === '/v1/site/login');
    expect(JSON.parse(login!.init.body as string)).toEqual({ email: 'root@example.com', password: 'a password' });
  });

  it('never calls a member endpoint: the two apps do not share a session', async () => {
    const { fetchFn } = await open('/admin/login', adminVisitorServer());
    await heading('Site admin sign in');

    type('Email', 'root@example.com');
    type('Password', 'a password');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await heading('Dashboard');

    expect(fetchFn.calls.every((call) => call.path.startsWith('/v1/site/'))).toBe(true);
  });

  it('says so, clears the password and focuses it when the credentials are wrong', async () => {
    await open('/admin/login', adminVisitorServer({ 'POST /v1/site/login': () => jsonResponse(401, { code: 'invalid_credentials' }) }));
    await heading('Site admin sign in');

    type('Email', 'someone@example.com');
    type('Password', 'wrong password');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('Email or password is incorrect.'));
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('');
    expect(document.activeElement).toBe(screen.getByLabelText('Password'));
    expect(location()).toBe('/admin/login');
  });

  it('sends a signed-in Site admin away from it, to the dashboard', async () => {
    await open('/admin/login', adminServer());

    expect(await heading('Dashboard')).toBeTruthy();
    expect(location()).toBe('/admin');
  });
});

describe('the route guards of the Site admin app', () => {
  it('send a visitor who asks for a page to the Site admin sign-in page, not the member one', async () => {
    await open('/admin', adminVisitorServer());

    expect(await heading('Site admin sign in')).toBeTruthy();
    expect(location()).toBe('/admin/login');
  });

  it('treat a trailing slash as the same address', async () => {
    await open('/admin/', adminServer());

    expect(await heading('Dashboard')).toBeTruthy();
  });

  it('show "not found" inside the frame to a signed-in Site admin, and bare to a visitor', async () => {
    const signedIn = await open('/admin/nothing-here', adminServer());
    expect(await heading('Page not found')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy();
    signedIn.unmount();

    await open('/admin/nothing-here', adminVisitorServer());
    expect(await heading('Page not found')).toBeTruthy();
    noMenu();
  });

  it('return to the Site admin sign-in page after sign-out', async () => {
    await open('/admin', adminServer());
    await heading('Dashboard');

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await heading('Site admin sign in')).toBeTruthy();
    expect(location()).toBe('/admin/login');
    noMenu();
  });
});

describe('the Site admin shell', () => {
  it('names the account in the header, and the tab after the app', async () => {
    await open('/admin', adminServer());
    await heading('Dashboard');

    expect(screen.getByText('Site admin', { selector: '.badge' })).toBeTruthy();
    expect(screen.getByText('root@example.com')).toBeTruthy();
    expect(document.title).toBe('Dashboard · Site admin');
  });

  it('leaves out the links to pages this app has not got yet, so none opens "not found"', async () => {
    const items = [
      { id: 'a1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' },
      { id: 'a2', pageKey: 'menus', label: 'Menus', icon: 'menu', path: '/admin/menus' },
      { id: 'a3', pageKey: 'permissions', label: 'Permissions', icon: 'shield', path: '/admin/permissions' },
    ];
    await open('/admin', adminServer({ items }));
    await heading('Dashboard');

    const menu = screen.getByRole('navigation', { name: 'Main' });
    expect(within(menu).getAllByRole('link').map((link) => link.textContent)).toEqual(['Dashboard']);
  });

  it('falls back to the dashboard link when the server cannot send a menu, and still signs the person in', async () => {
    await open('/admin', adminServer({}, { 'GET /v1/site/menu': () => jsonResponse(500, {}) }));
    await heading('Dashboard');

    const menu = screen.getByRole('navigation', { name: 'Main' });
    expect(within(menu).getAllByRole('link').map((link) => link.textContent)).toEqual(['Dashboard']);
  });
});

describe('the Site admin dashboard', () => {
  it('shows the platform totals', async () => {
    await open('/admin', adminServer());
    await heading('Dashboard');

    const overview = await screen.findByText('Memberships');
    const stats = overview.closest('.stats') as HTMLElement;
    const tile = (label: string) => within(within(stats).getByText(label).closest('.stat') as HTMLElement);
    expect(tile('Organizations').getByText('12')).toBeTruthy();
    expect(tile('Organizations').getByText('9 trial · 3 paid')).toBeTruthy();
    expect(tile('Users').getByText('40')).toBeTruthy();
    expect(tile('Workspaces').getByText('25')).toBeTruthy();
    expect(tile('Memberships').getByText('44')).toBeTruthy();
  });

  it('lists the organizations with their kind, plan, members, workspaces and date', async () => {
    const rows = [organization('Northwind Foods'), organization('Acme Ltd', { businessModel: 'agency', plan: 'paid', members: 7, workspaces: 4 })];
    await open('/admin', adminServer({}, { 'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged(rows)) }));

    const acme = (await screen.findByRole('heading', { name: 'Acme Ltd' })).closest('li') as HTMLElement;
    expect(within(acme).getByText('Marketing agency')).toBeTruthy();
    expect(within(acme).getByText('Paid')).toBeTruthy();
    expect(within(acme).getByText('7')).toBeTruthy();
    expect(within(acme).getByText('4')).toBeTruthy();
    expect(within(acme).getByText('Mar 5, 2026')).toBeTruthy();

    const northwind = screen.getByRole('heading', { name: 'Northwind Foods' }).closest('li') as HTMLElement;
    expect(within(northwind).getByText('Company')).toBeTruthy();
    expect(within(northwind).getByText('Trial')).toBeTruthy();
  });

  it('pages through the organizations', async () => {
    const first = Array.from({ length: 10 }, (_, index) => organization(`Org ${index + 1}`));
    const second = Array.from({ length: 10 }, (_, index) => organization(`Org ${index + 11}`));
    const { fetchFn } = await open(
      '/admin',
      adminServer({}, {
        'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged(first, { totalCount: 25, totalPages: 3 })),
        'GET /v1/site/agencies?page=2&pageSize=10': () => jsonResponse(200, paged(second, { page: 2, totalCount: 25, totalPages: 3 })),
      }),
    );
    await screen.findByRole('heading', { name: 'Org 1' });
    const pager = screen.getByRole('navigation', { name: 'Organization pages' });

    expect(within(pager).getByText('1–10 of 25 · 3 pages')).toBeTruthy();
    expect((within(pager).getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(within(pager).getByRole('button', { name: 'Next' }));

    expect(await screen.findByRole('heading', { name: 'Org 11' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Org 1' })).toBeNull();
    expect(screen.getByText('11–20 of 25 · 3 pages')).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).toContain('GET /v1/site/agencies?page=2&pageSize=10');
  });

  it('disables Next on the last page', async () => {
    await open('/admin', adminServer({}, { 'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged([organization('Only one')])) }));
    await screen.findByRole('heading', { name: 'Only one' });

    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('says so when there are no organizations yet', async () => {
    await open('/admin', adminServer({}, { 'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged([])) }));

    expect(await screen.findByText(/No organizations yet/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeTruthy();
  });

  it('reads everything again on Refresh', async () => {
    const { fetchFn } = await open('/admin', adminServer());
    await screen.findByRole('heading', { name: 'Northwind Foods' });

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(fetchFn.calls.filter((call) => call.path === '/v1/site/overview')).toHaveLength(2));
    expect(await screen.findByRole('heading', { name: 'Northwind Foods' })).toBeTruthy();
  });

  it('says what went wrong, and loads again on Try again', async () => {
    let failing = true;
    await open(
      '/admin',
      adminServer({}, { 'GET /v1/site/overview': () => (failing ? jsonResponse(500, {}) : jsonResponse(200, { agencies: 1, trialAgencies: 1, paidAgencies: 0, users: 1, workspaces: 1, memberships: 1 })) }),
    );

    expect((await screen.findByRole('alert')).textContent).toMatch(/The dashboard could not be loaded\./);

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('heading', { name: 'Northwind Foods' })).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('tells a person who is no longer a Site admin so', async () => {
    await open('/admin', adminServer({}, { 'GET /v1/site/overview': () => jsonResponse(403, { code: 'not_site_admin' }) }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer a site admin/);
  });

  it('shows the claims of the token, which name no organization', async () => {
    await open('/admin', adminServer());
    await heading('Dashboard');

    const session = (await screen.findByRole('region', { name: 'Your session' })) as HTMLElement;
    expect(within(session).getByText('u1')).toBeTruthy();
    expect(within(session).getByText('siteadmin')).toBeTruthy();
    expect(within(session).queryByText('Plan')).toBeNull();
  });

  it('becomes the sign-in page when the session can no longer be renewed', async () => {
    let cookieIsGood = true;
    const { api } = await open(
      '/admin',
      adminServer({}, {
        'POST /v1/site/refresh': () => (cookieIsGood ? jsonResponse(200, { accessToken: 'again' }) : jsonResponse(401, {})),
        'GET /v1/site/anything': () => jsonResponse(401, {}),
      }),
    );
    await heading('Dashboard');
    cookieIsGood = false;

    await act(async () => {
      await api.request('/v1/site/anything').catch(() => undefined);
    });

    expect(await heading('Site admin sign in')).toBeTruthy();
  });
});
