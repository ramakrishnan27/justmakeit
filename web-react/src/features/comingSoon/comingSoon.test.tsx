import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PERMISSIONS, type PermissionKey } from '@/access/permissions';
import { requestLine } from '@/test/helpers';
import {
  aiStudioItem,
  calendarItem,
  contentStudioItem,
  dashboardItem,
  mediaLibraryItem,
  memberServer,
  renderApp,
} from '@/test/renderApp';

// AI Studio and Calendar are placeholders for features that are not built yet: the menu item and the permission are
// real, the page says it is coming soon. (Content Studio and the Media Library were two until their pages were built: see features/content and
// features/media.) The sidebar
// still shows all four items, so `sidebarPages` keeps Content Studio in the order of the menu.
const studioPages = [
  { path: '/ai-studio', title: 'AI Studio', permission: PERMISSIONS.aiStudioView, item: aiStudioItem, icon: 'sparkles' },
  { path: '/calendar', title: 'Calendar', permission: PERMISSIONS.calendarView, item: calendarItem, icon: 'calendar' },
] as const;

const sidebarPages = [
  { permission: PERMISSIONS.contentStudioView, item: contentStudioItem },
  { permission: PERMISSIONS.mediaLibraryView, item: mediaLibraryItem },
  ...studioPages,
];

const everyStudioPermission: PermissionKey[] = sidebarPages.map((page) => page.permission);
const everyStudioItem = sidebarPages.map((page) => page.item);
const navigationLinks = () => within(screen.getByRole('navigation', { name: 'Main' })).getAllByRole('link');

describe.each(studioPages)('the $title page', (page) => {
  it('says it is coming soon, under its own heading, and does nothing else', async () => {
    const { fetchFn } = await renderApp(
      page.path,
      memberServer({ permissions: [PERMISSIONS.dashboardView, page.permission], items: [dashboardItem, page.item] }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: page.title })).toBeTruthy();
    const section = screen.getByRole('region', { name: `${page.title}: coming soon` });
    expect(within(section).getByText('Coming soon')).toBeTruthy();
    expect(within(section).getByText(/not built yet/)).toBeTruthy();
    expect(within(screen.getByRole('main')).queryAllByRole('button')).toHaveLength(0);
    expect(screen.getByRole('main').querySelector('form')).toBeNull();
    // Only the session is read: the page makes no request of its own.
    expect(fetchFn.calls.map(requestLine).sort()).toEqual(['GET /v1/auth/me', 'GET /v1/menu']);
  });

  it('names the browser tab, and is marked in the sidebar with its own icon', async () => {
    await renderApp(page.path, memberServer({ permissions: [PERMISSIONS.dashboardView, page.permission], items: [dashboardItem, page.item] }));
    await screen.findByRole('heading', { level: 1, name: page.title });

    expect(document.title).toBe(`${page.title} · Platform`);
    const link = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: page.title });
    expect(link.getAttribute('href')).toBe(page.path);
    expect(link.getAttribute('aria-current')).toBe('page');
    expect(link.querySelector('svg')).not.toBeNull();
  });

  it('shows the no-access page instead, with no link, for a person who lacks its permission, even holding the other three', async () => {
    const others = everyStudioPermission.filter((key) => key !== page.permission);
    const { fetchFn } = await renderApp(
      page.path,
      memberServer({ permissions: [PERMISSIONS.dashboardView, ...others], items: [dashboardItem, ...everyStudioItem.filter((item) => item !== page.item)] }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
    expect(screen.queryByRole('region', { name: `${page.title}: coming soon` })).toBeNull();
    expect(within(screen.getByRole('navigation', { name: 'Main' })).queryByRole('link', { name: page.title })).toBeNull();
    expect(fetchFn.calls.map(requestLine).sort()).toEqual(['GET /v1/auth/me', 'GET /v1/menu']);
  });

  it('does not let another permission stand in for it', async () => {
    await renderApp(
      page.path,
      memberServer({
        permissions: [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.dataRead, PERMISSIONS.dataWrite, PERMISSIONS.organizationManage, PERMISSIONS.platformsView],
        items: [dashboardItem],
      }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeTruthy();
  });

  it.each([
    ['company', 'organizationadmin'],
    ['agency', 'clientmanager'],
    ['creator', 'contentwriter'],
  ] as const)('opens for a %s %s who holds its permission', async (businessModel, role) => {
    await renderApp(
      page.path,
      memberServer({ businessModel, role, permissions: [PERMISSIONS.dashboardView, page.permission], items: [dashboardItem, page.item] }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: page.title })).toBeTruthy();
  });
});

describe('the sidebar with the four pages (Content Studio and the Media Library are real pages now)', () => {
  it('lists them right after Dashboard, in the order the server sent', async () => {
    await renderApp('/dashboard', memberServer({ permissions: [PERMISSIONS.dashboardView, ...everyStudioPermission], items: [dashboardItem, ...everyStudioItem] }));
    await screen.findByRole('heading', { level: 1 });

    expect(navigationLinks().map((link) => link.textContent)).toEqual(['Dashboard', 'Content Studio', 'Media Library', 'AI Studio', 'Calendar']);
    expect(navigationLinks().map((link) => link.getAttribute('href'))).toEqual(['/dashboard', '/content-studio', '/media-library', '/ai-studio', '/calendar']);
  });

  it('draws a different icon beside each page', async () => {
    await renderApp('/dashboard', memberServer({ permissions: [PERMISSIONS.dashboardView, ...everyStudioPermission], items: [dashboardItem, ...everyStudioItem] }));
    await screen.findByRole('heading', { level: 1 });

    const drawings = navigationLinks().map((link) => [...(link.querySelector('svg')?.querySelectorAll('path') ?? [])].map((path) => path.getAttribute('d')).join('|'));
    expect(drawings).toHaveLength(5);
    expect(new Set(drawings).size).toBe(5);
    expect(drawings.every((drawing) => drawing.length > 0)).toBe(true);
  });
});
