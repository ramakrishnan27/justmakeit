import { render } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { App } from '@/App';
import { createApiClient } from '@/api/client';
import type { BusinessModel, OrganizationRole } from '@/api/types';
import { AppProvider } from '@/navigation/AppContext';
import { memberApp, type AppDefinition } from '@/navigation/apps';
import { createSession, memberEndpoints, siteAdminEndpoints } from '@/session/session';
import { SessionProvider } from '@/session/SessionContext';
import { jsonResponse, routedFetch, type Handler, type RecordedCall } from './helpers';

/** When the sign-in the fake server hands out ends (the same moment for every test). */
export const sessionEndsAt = '2030-01-15T09:30:00Z';

export const dashboardItem = { id: 'm1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };
export const workspacesItem = { id: 'm2', pageKey: 'workspaces', label: 'Workspaces', icon: 'folder', path: '/workspaces' };

export const teamItem = { id: 'm3', pageKey: 'team', label: 'Team', icon: 'users', path: '/team' };

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

/** The answers of a server for a signed-in member: login, me and the menu. Individual routes can be overridden. */
export function memberServer(options: MemberOptions = {}, overrides: Record<string, Handler> = {}): Record<string, Handler> {
  const {
    role = 'marketingmanager',
    businessModel = 'company',
    fullName = 'Ana Smith',
    organizationName = 'Northwind Foods',
    permissions = ['dashboard.view', 'workspaces.view', 'data.read', 'data.write'],
    items = [dashboardItem, workspacesItem],
  } = options;

  return {
    'POST /v1/auth/login': () => jsonResponse(200, { expiresAt: sessionEndsAt }),
    'POST /v1/auth/register': () => jsonResponse(201, { expiresAt: sessionEndsAt }),
    'POST /v1/auth/invitations/accept': () => jsonResponse(200, { expiresAt: sessionEndsAt }),
    'POST /v1/auth/logout': () => new Response(null, { status: 204 }),
    'GET /v1/auth/me': () =>
      jsonResponse(200, {
        user: { id: 'u2', email: 'ana@example.com', fullName },
        agency: { id: 'a1', name: organizationName, businessModel, plan: 'trial' },
        role,
        memberships: [{ agencyId: 'a1', agencyName: organizationName, businessModel, role }],
        sessionExpiresAt: sessionEndsAt,
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

/** The answers of a server for a signed-in Site admin: login, me, the menu, the totals and one page of organizations. */
export function adminServer(options: AdminOptions = {}, overrides: Record<string, Handler> = {}): Record<string, Handler> {
  const { items = [adminDashboardItem] } = options;

  return {
    'POST /v1/site/login': () => jsonResponse(200, { expiresAt: sessionEndsAt }),
    'POST /v1/site/logout': () => new Response(null, { status: 204 }),
    'GET /v1/site/me': () =>
      jsonResponse(200, { user: { id: 'u1', email: 'root@example.com', fullName: 'Root' }, role: 'siteadmin', sessionExpiresAt: sessionEndsAt }),
    'GET /v1/site/menu': () => jsonResponse(200, { items, permissions: [] }),
    'GET /v1/site/overview': () => jsonResponse(200, overview),
    'GET /v1/site/agencies?page=1&pageSize=10': () => jsonResponse(200, paged([organization('Northwind Foods')])),
    ...overrides,
  };
}

const respond = (handler: Handler, call: RecordedCall) => (typeof handler === 'function' ? handler(call) : handler.clone());

/**
 * Makes a server answer 401 to everything that needs a session until one of the sign-in routes has been called, as a
 * server does for a browser that holds no cookie. The routes in `open` answer anyone. A route a test passes in its own
 * overrides replaces the wrapped one, so a test can still make a sign-in fail.
 */
function untilSignedIn(server: Record<string, Handler>, signIns: string[], open: string[]): Record<string, Handler> {
  let signedIn = false;
  const gated: Record<string, Handler> = {};

  for (const [route, handler] of Object.entries(server)) {
    if (signIns.includes(route)) {
      gated[route] = async (call) => {
        const response = await respond(handler, call);
        signedIn = signedIn || response.ok;
        return response;
      };
    } else if (open.includes(route)) {
      gated[route] = handler;
    } else {
      gated[route] = (call) => (signedIn ? respond(handler, call) : jsonResponse(401, { code: 'unauthenticated' }));
    }
  }
  return gated;
}

/** A server for a Site admin who is not signed in: there is no cookie, so everything answers 401 until signing in works. */
export function adminVisitorServer(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return { ...untilSignedIn(adminServer(), ['POST /v1/site/login'], ['POST /v1/site/logout']), ...overrides };
}

/** A server for a visitor who is not signed in: there is no cookie, so everything answers 401 until signing in, signing up or accepting an invitation works. */
export function visitorServer(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return {
    ...untilSignedIn(memberServer(), ['POST /v1/auth/login', 'POST /v1/auth/register', 'POST /v1/auth/invitations/accept'], ['POST /v1/auth/logout', 'GET /v1/auth/providers']),
    ...overrides,
  };
}

/** Shows where the router is, so a test can see that an address was changed. */
function LocationProbe() {
  const { pathname, search } = useLocation();
  return <output data-testid="location">{pathname + search}</output>;
}

/**
 * Starts the app as the page does: restores the session from the sign-in cookie first, then draws the routes at `path`.
 * Returns the fake fetch (to see which requests were made) and the session.
 */
export async function renderApp(path: string, server: Record<string, Handler>, app: AppDefinition = memberApp) {
  const fetchFn = routedFetch(server);
  const endpoints = app.kind === 'admin' ? siteAdminEndpoints : memberEndpoints;
  const api = createApiClient({ fetchFn });
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
