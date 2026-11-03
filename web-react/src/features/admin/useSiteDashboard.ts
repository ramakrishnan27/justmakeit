import { useCallback, useEffect, useState } from 'react';
import type { PagedResult, SiteOrganization, SiteOverview } from '@/api/types';
import { useApi } from '@/session/SessionContext';

/** How many organizations the dashboard lists at a time. */
export const pageSize = 10;

export type SiteDashboardState =
  | { status: 'loading' }
  | { status: 'error'; error: unknown }
  | { status: 'ready'; overview: SiteOverview; organizations: PagedResult<SiteOrganization> };

/**
 * Loads the platform totals and one page of organizations together (the server calls an organization an agency).
 * `reload` reads them again, and `goTo` moves to another page of organizations.
 */
export function useSiteDashboard() {
  const api = useApi();
  const [state, setState] = useState<SiteDashboardState>({ status: 'loading' });
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    Promise.all([
      api.request<SiteOverview>('/v1/site/overview'),
      api.request<PagedResult<SiteOrganization>>(`/v1/site/agencies?page=${page}&pageSize=${pageSize}`),
    ]).then(
      ([overview, organizations]) => {
        if (active) {
          setState({ status: 'ready', overview, organizations });
        }
      },
      (error: unknown) => {
        if (active) {
          setState({ status: 'error', error });
        }
      },
    );
    return () => {
      active = false;
    };
  }, [api, page, attempt]);

  const reload = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((count) => count + 1);
  }, []);

  const goTo = useCallback((next: number) => {
    setState({ status: 'loading' });
    setPage(Math.max(1, next));
  }, []);

  return { state, reload, goTo };
}
