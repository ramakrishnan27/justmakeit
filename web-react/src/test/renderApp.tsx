import { render } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { App } from '@/App';
import { createApiClient } from '@/api/client';
import type { BusinessModel, OrganizationRole } from '@/api/types';
import { createSession } from '@/session/session';
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
export async function renderApp(path: string, server: Record<string, Handler>) {
  const fetchFn = routedFetch(server);
  const api = createApiClient({ fetchFn });
  const session = createSession(api);
  await session.restore();

  const view = render(
    <SessionProvider api={api} session={session}>
      <MemoryRouter initialEntries={[path]}>
        <App />
        <LocationProbe />
      </MemoryRouter>
    </SessionProvider>,
  );
  return { ...view, fetchFn, api, session };
}
