import { render } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { App } from '@/App';
import { createApiClient } from '@/api/client';
import type { BusinessModel, OrganizationRole } from '@/api/types';
import { AppProvider } from '@/navigation/AppContext';
import { memberApp, type AppDefinition } from '@/navigation/apps';
import { createSession, memberEndpoints, siteAdminEndpoints } from '@/session/session';
import { SessionProvider } from '@/session/SessionContext';
import { jsonResponse, makeJwt, routedFetch, type Handler } from './helpers';

export const dashboardItem = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
export const workspacesItem = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };

export interface MemberOptions {
  role?: OrganizationRole;
  businessModel?: BusinessModel;
  fullName?: string;
  organizationName?: string;
  /** What the access token carries. */
  permissions?: string[];
  /** What the server puts in the sidebar. */
  items?: Array<typeof dashboardItem>;
}

/** The answers of a server for a signed-in member: refresh, login, me and the menu. Individual routes can be overridden. */
export function memberServer(options: MemberOptions = {}, overrides: Record<string, Handler> = {}): Record<string, Handler> {
  const {
    role = 'marketingmanager',
    businessModel = 'company',
    fullName = 'Ana Smith',
    organizationName = 'Northwind Foods',
    permissions = ['dashboard.view', 'workspaces.view', 'data.read', 'data.write'],
    items = [dashboardItem, workspacesItem],
  } = options;
  const accessToken = makeJwt({ user_id: 'u2', agency_id: 'a1', role, exp: 1_900_000_000 });

  return {
    'POST /v1/auth/refresh': () => jsonResponse(200, { accessToken }),
    'POST /v1/auth/login': () => jsonResponse(200, { accessToken }),
    'POST /v1/auth/register': () => jsonResponse(201, { accessToken }),
    'POST /v1/auth/logout': () => new Response(null, { status: 204 }),
    'GET /v1/auth/me': () =>
      jsonResponse(200, {
        user: { id: 'u2', email: 'ana@example.com', fullName },
        agency: { id: 'a1', name: organizationName, businessModel, plan: 'trial' },
        role,
        memberships: [{ agencyId: 'a1', agencyName: organizationName, businessModel, role }],
      }),
    'GET /v1/menu': () => jsonResponse(200, { items, permissions }),
    'GET /v1/auth/providers': () => jsonResponse(200, { google: false }),
    ...overrides,
  };
}

export interface AdminOptions {
  /** What the server puts in the Site admin's sidebar. */
  items?: Array<{ id: string; pageKey: string; label: string; icon: string; path: string }>;
}

export const adminDashboardItem = { id: 'a1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/admin' };

export const overview = { agencies: 12, trialAgencies: 9, paidAgencies: 3, users: 40, workspaces: 25, memberships: 44 };

export function organization(name: string, overrides: Record<string, unknown> = {}) {
  return { id: `o-${name}`, name, businessModel: 'company', plan: 'trial', createdAt: '2026-03-05T10:30:00Z', members: 2, workspaces: 1, ...overrides };
}

export function paged<T>(items: T[], overrides: Record<string, unknown> = {}) {
  return { items, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1, ...overrides };
}

/** The answers of a server for a signed-in Site admin: refresh, login, me, the menu, the totals and one page of organizations. */
export function adminServer(options: AdminOptions = {}, overrides: Record<string, Handler> = {}): Record<string, Handler> {
  const { items = [adminDashboardItem] } = options;
  const accessToken = makeJwt({ user_id: 'u1', role: 'siteadmin', exp: 1_900_000_000 });

  return {
    'POST /v1/site/refresh': () => jsonResponse(200, { accessToken }),
    'POST /v1/site/login': () => jsonResponse(200, { accessToken }),
    'POST /v1/site/logout': () => new Response(null, { status: 204 }),
    'GET /v1/site/me': () => jsonResponse(200, { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin' }),
    'GET /v1/site/menu': () => jsonResponse(200, { items, permissions: [] }),
    'GET /v1/site/overview': () => jsonResponse(200, overview),
    'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged([organization('Northwind Foods')])),
    ...overrides,
  };
}

/** A server for a Site admin who is not signed in: the refresh cookie is no good, but signing in works. */
export function adminVisitorServer(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return { ...adminServer(), 'POST /v1/site/refresh': () => jsonResponse(401, { code: 'invalid_refresh_token' }), ...overrides };
}

/** A server for a visitor who is not signed in: the refresh cookie is no good. */
export function visitorServer(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return { ...memberServer(), 'POST /v1/auth/refresh': () => jsonResponse(401, { code: 'invalid_refresh_token' }), ...overrides };
}

/** Shows where the router is, so a test can see that an address was changed. */
function LocationProbe() {
  const { pathname, search } = useLocation();
  return <output data-testid="location">{pathname + search}</output>;
}

/**
 * Starts the app as the page does: restores the session from the refresh cookie first, then draws the routes at `path`.
 * Returns the fake fetch (to see which requests were made) and the session.
 */
export async function renderApp(path: string, server: Record<string, Handler>, app: AppDefinition = memberApp) {
  const fetchFn = routedFetch(server);
  const endpoints = app.kind === 'admin' ? siteAdminEndpoints : memberEndpoints;
  const api = createApiClient({ fetchFn, refreshPath: endpoints.refresh });
  const session = createSession(api, endpoints);
  await session.restore();

  const view = render(
    <AppProvider app={app}>
      <SessionProvider api={api} session={session}>
        <MemoryRouter initialEntries={[path]}>
          <App />
          <LocationProbe />
        </MemoryRouter>
      </SessionProvider>
    </AppProvider>,
  );
  return { ...view, fetchFn, api, session };
}
