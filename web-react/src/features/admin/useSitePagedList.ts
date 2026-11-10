import { useCallback, useEffect, useState } from 'react';
import type { PagedResult } from '@/api/types';
import { useApi } from '@/session/SessionContext';

/** How many rows a list on the Users page shows at a time. */
export const listPageSize = 10;

export type PagedListState<T> = { status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; result: PagedResult<T> };

/**
 * Loads one page of a /v1/site list (members, organizations) and keeps the page number. `reload` reads the same page again
 * and `goTo` moves to another one. The list is read again from page 1 when `path` changes, so each list starts at its
 * own first page.
 */
export function useSitePagedList<T>(path: string) {
  const api = useApi();
  const [state, setState] = useState<PagedListState<T>>({ status: 'loading' });
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    api.request<PagedResult<T>>(`${path}?page=${page}&pageSize=${listPageSize}`).then(
      (result) => {
        if (active) {
          setState({ status: 'ready', result });
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
  }, [api, path, page, attempt]);

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
