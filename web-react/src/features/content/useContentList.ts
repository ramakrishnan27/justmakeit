import { useCallback, useEffect, useState } from 'react';
import type { PostPage } from '@/api/content';
import { useContentApi } from './hooks';
import { listParams, type ListFilters } from './listFilters';

export type ListState =
  | { status: 'loading'; previous: PostPage | null }
  | { status: 'error'; error: unknown }
  | { status: 'ready'; page: PostPage };

/**
 * Reads the list for some filters, again whenever they change. While a new page loads the one before stays on screen (so the table
 * does not jump to "Loading…" at every keystroke of the search), and an answer that arrives after the filters changed again is
 * ignored. `reload` reads the same page again, after something was done to a post.
 */
export function useContentList(filters: ListFilters) {
  const content = useContentApi();
  const [state, setState] = useState<ListState>({ status: 'loading', previous: null });
  const [attempt, setAttempt] = useState(0);
  const { type, status, platform, range, q, workspaceId, page } = filters;

  useEffect(() => {
    let active = true;
    setState((current) => ({ status: 'loading', previous: current.status === 'ready' ? current.page : current.status === 'loading' ? current.previous : null }));
    content.list(listParams({ type, status, platform, range, q, workspaceId, page })).then(
      (result) => {
        if (active) {
          setState({ status: 'ready', page: result });
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
  }, [content, type, status, platform, range, q, workspaceId, page, attempt]);

  const reload = useCallback(() => setAttempt((count) => count + 1), []);

  return { state, reload };
}
