import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, requestLine } from '@/test/helpers';
import { dashboardItem, memberServer, platformsItem, renderApp, type MemberOptions } from '@/test/renderApp';

const heading = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const platform = (id: string, name: string, isEnabled: boolean, extra: Record<string, unknown> = {}) => ({
  id,
  name,
  description: `About ${name}`,
  iconKey: name.toLowerCase(),
  hasIconImage: false,
  isEnabled,
  ...extra,
});
const withView = [PERMISSIONS.dashboardView, PERMISSIONS.platformsView];

const list = [platform('c1', 'Instagram', true), platform('c2', 'Email', true), platform('c3', 'SMS', false)];

function open(options: MemberOptions = {}, platforms: unknown[] = list, extra = {}) {
  return renderApp(
    '/platforms',
    memberServer({ permissions: withView, items: [dashboardItem, platformsItem], ...options }, { 'GET /v1/platforms': () => jsonResponse(200, platforms), ...extra }),
  );
}

describe('the Platform page', () => {
  it('lists every platform with its name, what it is for, and whether it is enabled', async () => {
    await open();

    expect(await heading('Platforms')).toBeTruthy();
    const grid = await screen.findByRole('list', { name: 'Platforms' });
    const tiles = within(grid).getAllByRole('listitem');
    expect(tiles.map((tile) => within(tile).getByText(/^(Instagram|Email|SMS)$/).textContent)).toEqual(['Instagram', 'Email', 'SMS']);
    expect(within(tiles[0]!).getByText('About Instagram')).toBeTruthy();
    expect(within(tiles[0]!).getByText('Enabled')).toBeTruthy();
    expect(within(tiles[2]!).getByText('Not enabled')).toBeTruthy();
    expect(screen.getByText('2 of 3 enabled')).toBeTruthy();
  });

  it('says Platform in the sidebar, as the server sent it, for every kind of organization', async () => {
    for (const businessModel of ['company', 'agency', 'creator'] as const) {
      const { unmount } = await open({ businessModel, role: businessModel === 'creator' ? 'contentwriter' : 'organizationadmin' });
      await heading('Platforms');

      const link = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Platform' });
      expect(link.getAttribute('href')).toBe('/platforms');
      expect(link.getAttribute('aria-current')).toBe('page');
      unmount();
    }
  });

  it('draws an uploaded icon from the member address, and a built-in one from its strokes', async () => {
    const { container } = await open({}, [platform('c1', 'Telegram', false, { iconKey: null, hasIconImage: true }), platform('c2', 'Email', true)]);
    await screen.findByText('Telegram');

    const images = container.querySelectorAll('img.channel-image');
    expect(images).toHaveLength(1);
    expect(images[0]?.getAttribute('src')).toBe('/v1/platforms/c1/icon');
    expect(container.querySelectorAll('svg.channel-icon')).toHaveLength(1);
    expect(container.innerHTML).not.toContain('/v1/site/');
  });

  it('is read only: no button, no switch, no form', async () => {
    await open();
    await screen.findByText('Instagram');

    const main = screen.getByRole('main');
    expect(within(main).queryAllByRole('button')).toHaveLength(0);
    expect(within(main).queryAllByRole('switch')).toHaveLength(0);
    expect(main.querySelector('form')).toBeNull();
  });

  it('says so when there are no platforms', async () => {
    await open({}, []);

    expect(await screen.findByText('There are no platforms yet.')).toBeTruthy();
    expect(screen.queryByText(/enabled$/)).toBeNull();
  });

  it('says so when the list cannot be loaded, and loads it on Try again', async () => {
    let failing = true;
    await open({}, [], { 'GET /v1/platforms': () => (failing ? jsonResponse(500, {}) : jsonResponse(200, list)) });

    expect((await screen.findByRole('alert')).textContent).toContain('The platforms could not be loaded.');

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Instagram')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('says what a refusal means', async () => {
    await open({}, [], { 'GET /v1/platforms': () => jsonResponse(403, { code: 'insufficient_permission' }) });

    expect((await screen.findByRole('alert')).textContent).toContain('Your role cannot see the platforms.');
  });
});

describe('the permission that guards the Platform page', () => {
  it('shows the no-access page instead, makes no request, and has no link, for a person without platforms.view', async () => {
    const { fetchFn } = await renderApp(
      '/platforms',
      memberServer({ permissions: [PERMISSIONS.dashboardView], items: [dashboardItem] }, { 'GET /v1/platforms': () => jsonResponse(200, list) }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).not.toContain('GET /v1/platforms');
    expect(screen.queryByText('Instagram')).toBeNull();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).queryByRole('link', { name: 'Platform' })).toBeNull();
  });

  it('does not let another permission stand in for it', async () => {
    const { fetchFn } = await renderApp(
      '/platforms',
      memberServer(
        { permissions: [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.dataRead, PERMISSIONS.organizationManage], items: [dashboardItem] },
        { 'GET /v1/platforms': () => jsonResponse(200, list) },
      ),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
    expect(fetchFn.calls.map(requestLine)).not.toContain('GET /v1/platforms');
  });

  it('shows the page, and reads the platforms once, for a person who holds it', async () => {
    const { fetchFn } = await open();

    expect(await heading('Platforms')).toBeTruthy();
    await screen.findByText('Instagram');
    expect(fetchFn.calls.map(requestLine).filter((line) => line === 'GET /v1/platforms')).toHaveLength(1);
  });
});
