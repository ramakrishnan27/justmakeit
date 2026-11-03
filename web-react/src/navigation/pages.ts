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
  {
    key: 'workspaces',
    path: '/workspaces',
    title: 'Workspaces',
    permission: PERMISSIONS.workspacesView,
    Component: lazy(() => import('@/features/workspaces/WorkspacesPage')),
  },
];
