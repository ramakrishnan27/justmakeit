// The page holds two small apps that never share a session: the Site admin app under /admin, and the member app (sign in,
// create an account, the pages behind the sidebar) everywhere else. Each has its own addresses, its own sign-in and its
// own pages; main.tsx starts the one the address belongs to.

import { adminPages } from './adminPages';
import { pages } from './pages';

export interface AppDefinition {
  kind: 'member' | 'admin';
  /** Names the browser tab: "Dashboard · Platform", "Dashboard · Site admin". */
  label: string;
  /** Where a signed-in person lands, and where "go back" leads. */
  homePath: string;
  /** Where a visitor who asks for a page behind sign-in is sent. */
  loginPath: string;
  /** The pages behind sign-in. The menu shows only links to these: a link to a page this app lacks would open "not found". */
  pagePaths: readonly string[];
}

export const memberApp: AppDefinition = {
  kind: 'member',
  label: 'Platform',
  homePath: '/dashboard',
  loginPath: '/login',
  pagePaths: pages.map((page) => page.path),
};

export const adminApp: AppDefinition = {
  kind: 'admin',
  label: 'Site admin',
  homePath: '/admin',
  loginPath: '/admin/login',
  pagePaths: adminPages.map((page) => page.path),
};

/** Which app an address belongs to: /admin and everything under it is the Site admin app, anything else the member app. */
export function appForPath(pathname: string): AppDefinition {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  return path === '/admin' || path.startsWith('/admin/') ? adminApp : memberApp;
}
