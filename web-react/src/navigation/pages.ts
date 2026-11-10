// The pages behind sign-in. Each names the permission a person needs to open it, and its component is loaded only
// when that page is opened by someone who holds the permission: a person without it never downloads the page's code.
//
// The pages a menu may link to are seeded by the database (005_menus_permissions.sql); this table must say the same
// thing (path and permission), and a test reads the seed script and compares. A page without a seed row could never
// be put in a menu, and a seeded page without a component here would open a "not found".

import { lazy, type ComponentType, type LazyExoticComponent } from 'react';
import { PERMISSIONS, type PermissionKey } from '@/access/permissions';

export interface PageDefinition {
  /** The key of the page in the database's registry of menu pages (`menu_pages.key`). */
  key: string;
  path: string;
  title: string;
  /** What the person's access token must hold. It is the permission the page's own API calls need as well. */
  permission: PermissionKey;
  Component: LazyExoticComponent<ComponentType>;
}

export const pages: readonly PageDefinition[] = [
  {
    key: 'dashboard',
    path: '/dashboard',
    title: 'Dashboard',
    permission: PERMISSIONS.dashboardView,
    Component: lazy(() => import('@/features/dashboard/DashboardPage')),
  },
  // Content Studio and the Media Library are real pages now (013_member_studio_menu.sql seeded their menu items). AI Studio and Calendar
  // below are still placeholders for features that are not built yet: the menu item and the permission are real, the page says it is
  // coming soon. They sit here in the order of the menu.
  {
    key: 'content-studio',
    path: '/content-studio',
    title: 'Content Studio',
    permission: PERMISSIONS.contentStudioView,
    Component: lazy(() => import('@/features/content/ContentStudioPage')),
  },
  {
    key: 'media-library',
    path: '/media-library',
    title: 'Media Library',
    permission: PERMISSIONS.mediaLibraryView,
    Component: lazy(() => import('@/features/media/MediaLibraryPage')),
  },
  {
    key: 'ai-studio',
    path: '/ai-studio',
    title: 'AI Studio',
    permission: PERMISSIONS.aiStudioView,
    Component: lazy(() => import('@/features/comingSoon/AiStudioPage')),
  },
  {
    key: 'calendar',
    path: '/calendar',
    title: 'Calendar',
    permission: PERMISSIONS.calendarView,
    Component: lazy(() => import('@/features/comingSoon/CalendarPage')),
  },
  {
    key: 'workspaces',
    path: '/workspaces',
    title: 'Workspaces',
    permission: PERMISSIONS.workspacesView,
    Component: lazy(() => import('@/features/workspaces/WorkspacesPage')),
  },
  {
    key: 'platforms',
    path: '/platforms',
    title: 'Platforms',
    permission: PERMISSIONS.platformsView,
    Component: lazy(() => import('@/features/platforms/PlatformsPage')),
  },
  {
    key: 'team',
    path: '/team',
    title: 'Team',
    permission: PERMISSIONS.membersManage,
    Component: lazy(() => import('@/features/team/TeamPage')),
  },
];

/**
 * Pages behind sign-in that are NOT menu items: they have no row in the database's registry of menu pages (menu_pages), so a test
 * that compares the table above with the seeds does not look at them. Each names the permission it needs and is loaded the same
 * way. The Content Studio editor lives under the list's address: /content-studio/new makes a post, /content-studio/:id opens one
 * (the static address wins over the parameter). Opening a post needs only contentstudio.view and is read only without
 * contentstudio.create; making one needs contentstudio.create.
 */
export const subPages: readonly PageDefinition[] = [
  {
    key: 'content-studio-new',
    path: '/content-studio/new',
    title: 'Create New Post',
    permission: PERMISSIONS.contentStudioCreate,
    Component: lazy(() => import('@/features/content/NewPostPage')),
  },
  {
    key: 'content-studio-post',
    path: '/content-studio/:id',
    title: 'Post',
    permission: PERMISSIONS.contentStudioView,
    Component: lazy(() => import('@/features/content/EditPostPage')),
  },
];
