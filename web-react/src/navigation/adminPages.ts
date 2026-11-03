// The pages of the Site admin app behind sign-in. Like the member pages (pages.ts) each is loaded only when it is opened,
// but none names a permission: a Site admin's token carries no permissions, and the server lets only a Site admin
// through every /v1/site route (and checks the database each time), so being signed in to this app is the whole gate.
//
// The pages a menu may link to are seeded by the database (005_menus_permissions.sql). The menu and permission
// editors are seeded but not ported yet, so the menu leaves their links out (see AppShell) until they are added here.

import { lazy, type ComponentType, type LazyExoticComponent } from 'react';

export interface AdminPageDefinition {
  /** The key of the page in the database's registry of menu pages (`menu_pages.key`). */
  key: string;
  path: string;
  title: string;
  Component: LazyExoticComponent<ComponentType>;
}

export const adminPages: readonly AdminPageDefinition[] = [
  {
    key: 'dashboard',
    path: '/admin',
    title: 'Dashboard',
    Component: lazy(() => import('@/features/admin/AdminDashboardPage')),
  },
];
