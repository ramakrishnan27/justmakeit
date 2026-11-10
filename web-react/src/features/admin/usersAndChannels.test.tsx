import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { adminApp } from '@/navigation/apps';
import { jsonResponse, requestLine } from '@/test/helpers';
import { adminDashboardItem, adminServer, adminVisitorServer, organization, paged, renderApp, siteMember } from '@/test/renderApp';

const usersItem = { id: 'a4', pageKey: 'users', label: 'Users', icon: 'users', path: '/admin/users' };
const channelsItem = { id: 'a5', pageKey: 'channels', label: 'Channels', icon: 'list', path: '/admin/channels' };
const items = [adminDashboardItem, usersItem, channelsItem];

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const open = (path: string, overrides: Parameters<typeof adminServer>[1] = {}) => renderApp(path, adminServer({ items }, overrides), adminApp);
const location = () => screen.getByTestId('location').textContent;
const membersUrl = (page: number) => `GET /v1/site/members?page=${page}&pageSize=10`;
const organizationsUrl = (page: number) => `GET /v1/site/agencies?page=${page}&pageSize=10`;

describe('the Site admin menu', () => {
  it('links to the Users and Channels pages, which this app has', async () => {
    await open('/admin');
    await heading('Dashboard');

    const menu = screen.getByRole('navigation', { name: 'Main' });
    expect(within(menu).getAllByRole('link').map((link) => link.textContent)).toEqual(['Dashboard', 'Users', 'Channels']);
  });

  it('opens each page from its link and marks the open one', async () => {
    await open('/admin');
    await heading('Dashboard');
    const menu = screen.getByRole('navigation', { name: 'Main' });

    fireEvent.click(within(menu).getByRole('link', { name: 'Users' }));
    expect(await heading('Users')).toBeTruthy();
    expect(location()).toBe('/admin/users');
    expect(within(menu).getByRole('link', { name: 'Users' }).getAttribute('aria-current')).toBe('page');
    expect(document.title).toBe('Users · Site admin');

    fireEvent.click(within(menu).getByRole('link', { name: 'Channels' }));
    expect(await heading('Channels')).toBeTruthy();
    expect(location()).toBe('/admin/channels');
    expect(within(menu).getByRole('link', { name: 'Channels' }).getAttribute('aria-current')).toBe('page');
    expect(within(menu).getByRole('link', { name: 'Users' }).getAttribute('aria-current')).toBeNull();
    expect(document.title).toBe('Channels · Site admin');
  });

  it('sends a visitor who asks for either page to the Site admin sign-in page', async () => {
    for (const path of ['/admin/users', '/admin/channels']) {
      const view = await renderApp(path, adminVisitorServer(), adminApp);

      expect(await heading('Site admin sign in')).toBeTruthy();
      expect(location()).toBe('/admin/login');
      view.unmount();
    }
  });
});

describe('the Users page', () => {
  it('starts with the members, names the two lists, and reads only the list that is shown', async () => {
    const { fetchFn } = await open('/admin/users');

    expect(await heading('Users')).toBeTruthy();
    const tabs = screen.getByRole('navigation', { name: 'Users lists' });
    expect(within(tabs).getAllByRole('link').map((link) => link.textContent)).toEqual(['Members', 'Organizations']);
    expect(within(tabs).getByRole('link', { name: 'Members' }).getAttribute('aria-current')).toBe('page');
    expect(within(tabs).getByRole('link', { name: 'Organizations' }).getAttribute('aria-current')).toBeNull();

    expect(await screen.findByRole('heading', { level: 3, name: 'Ana Smith' })).toBeTruthy();
    const requests = fetchFn.calls.map(requestLine);
    expect(requests).toContain(membersUrl(1));
    expect(requests.filter((line) => line.includes('/v1/site/agencies'))).toEqual([]);
  });

  it('shows each membership: the person, whether they can sign in, the organization, the role and the date', async () => {
    const rows = [
      siteMember('Ana Smith'),
      siteMember('Bo Chen', { agencyName: 'Acme Ltd', businessModel: 'agency', role: 'clientmanager', isActive: false, joinedAt: '2026-04-09T08:00:00Z' }),
    ];
    await open('/admin/users', { [membersUrl(1)]: () => jsonResponse(200, paged(rows)) });

    const ana = (await screen.findByRole('heading', { name: 'Ana Smith' })).closest('li') as HTMLElement;
    expect(within(ana).getByText('ana.smith@example.com')).toBeTruthy();
    expect(within(ana).getByText('Active')).toBeTruthy();
    expect(within(ana).getByText('Company')).toBeTruthy();
    expect(within(ana).getByText('Northwind Foods')).toBeTruthy();
    expect(within(ana).getByText('Marketing Manager')).toBeTruthy();
    expect(within(ana).getByText('Mar 5, 2026')).toBeTruthy();

    const bo = screen.getByRole('heading', { name: 'Bo Chen' }).closest('li') as HTMLElement;
    expect(within(bo).getByText('Inactive')).toBeTruthy();
    expect(within(bo).getByText('Marketing agency')).toBeTruthy();
    expect(within(bo).getByText('Acme Ltd')).toBeTruthy();
    expect(within(bo).getByText('Client Manager')).toBeTruthy();
    expect(within(bo).getByText('Apr 9, 2026')).toBeTruthy();
  });

  it('lists a person in two organizations once for each, and a person with no name by their address, once', async () => {
    const rows = [
      siteMember('Ana Smith', { agencyId: 'a-1', agencyName: 'Northwind Foods' }),
      siteMember('Ana Smith', { agencyId: 'a-2', agencyName: 'Acme Ltd' }),
      siteMember('', { userId: 'u-none', email: 'nameless@example.com' }),
    ];
    await open('/admin/users', { [membersUrl(1)]: () => jsonResponse(200, paged(rows)) });

    expect(await screen.findAllByRole('heading', { level: 3, name: 'Ana Smith' })).toHaveLength(2);
    const nameless = screen.getByRole('heading', { level: 3, name: 'nameless@example.com' }).closest('li') as HTMLElement;
    expect(within(nameless).getAllByText('nameless@example.com')).toHaveLength(1);
  });

  it('switches to the organizations, keeps the choice in the address, and reads them only then', async () => {
    const { fetchFn } = await open('/admin/users', {
      [organizationsUrl(1)]: () => jsonResponse(200, paged([organization('Acme Ltd', { businessModel: 'agency', plan: 'paid', members: 7, workspaces: 4 })])),
    });
    await screen.findByRole('heading', { level: 3, name: 'Ana Smith' });

    fireEvent.click(screen.getByRole('link', { name: 'Organizations' }));

    expect(await screen.findByRole('heading', { level: 3, name: 'Acme Ltd' })).toBeTruthy();
    expect(location()).toBe('/admin/users?tab=organizations');
    expect(screen.queryByRole('heading', { level: 3, name: 'Ana Smith' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Organizations' }).getAttribute('aria-current')).toBe('page');
    const acme = screen.getByRole('heading', { name: 'Acme Ltd' }).closest('li') as HTMLElement;
    expect(within(acme).getByText('Marketing agency')).toBeTruthy();
    expect(within(acme).getByText('Paid')).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).toContain(organizationsUrl(1));

    fireEvent.click(screen.getByRole('link', { name: 'Members' }));

    expect(await screen.findByRole('heading', { level: 3, name: 'Ana Smith' })).toBeTruthy();
    expect(location()).toBe('/admin/users');
  });

  it('opens on the organizations when the address says so, and on the members for any other tab name', async () => {
    const first = await open('/admin/users?tab=organizations');
    expect(await screen.findByRole('heading', { level: 3, name: 'Northwind Foods' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Organizations' }).getAttribute('aria-current')).toBe('page');
    first.unmount();

    await open('/admin/users?tab=nonsense');
    expect(await screen.findByRole('heading', { level: 3, name: 'Ana Smith' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Members' }).getAttribute('aria-current')).toBe('page');
  });

  it('pages through the members', async () => {
    const first = Array.from({ length: 10 }, (_, index) => siteMember(`Person ${index + 1}`));
    const second = Array.from({ length: 5 }, (_, index) => siteMember(`Person ${index + 11}`));
    const { fetchFn } = await open('/admin/users', {
      [membersUrl(1)]: () => jsonResponse(200, paged(first, { totalCount: 15, totalPages: 2 })),
      [membersUrl(2)]: () => jsonResponse(200, paged(second, { page: 2, totalCount: 15, totalPages: 2 })),
    });
    await screen.findByRole('heading', { name: 'Person 1' });
    const pager = screen.getByRole('navigation', { name: 'Member pages' });

    expect(within(pager).getByText('1–10 of 15 · 2 pages')).toBeTruthy();
    expect((within(pager).getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(within(pager).getByRole('button', { name: 'Next' }));

    expect(await screen.findByRole('heading', { name: 'Person 11' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Person 1' })).toBeNull();
    expect(screen.getByText('11–15 of 15 · 2 pages')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true);
    expect(fetchFn.calls.map(requestLine)).toContain(membersUrl(2));
  });

  it('says so when there are no members yet, and reads again on Refresh', async () => {
    const { fetchFn } = await open('/admin/users', { [membersUrl(1)]: () => jsonResponse(200, paged([])) });

    expect(await screen.findByText(/No members yet/)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(fetchFn.calls.map(requestLine).filter((line) => line === membersUrl(1))).toHaveLength(2));
  });

  it('says what went wrong, and loads again on Try again', async () => {
    let failing = true;
    await open('/admin/users', {
      [membersUrl(1)]: () => (failing ? jsonResponse(500, {}) : jsonResponse(200, paged([siteMember('Ana Smith')]))),
    });

    expect((await screen.findByRole('alert')).textContent).toMatch(/The members could not be loaded\./);

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('heading', { level: 3, name: 'Ana Smith' })).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('names the organizations when it is their list that failed', async () => {
    await open('/admin/users?tab=organizations', { [organizationsUrl(1)]: () => jsonResponse(500, {}) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/The organizations could not be loaded\./);
  });

  it('tells a person who is no longer a Site admin so', async () => {
    await open('/admin/users', { [membersUrl(1)]: () => jsonResponse(403, { code: 'not_site_admin' }) });

    expect((await screen.findByRole('alert')).textContent).toMatch(/no longer a site admin/);
  });
});
