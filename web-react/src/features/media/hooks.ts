import { useCallback, useEffect, useMemo, useState } from 'react';
import { mediaApi, type MediaApi, type MediaConfig, type MediaOverview, type MediaPage } from '@/api/media';
import { useApi } from '@/session/SessionContext';
import { listParams, type MediaFilters } from './mediaFilters';

/** The Media Library endpoints, made from the session's API client. The same object for the life of the page. */
export function useMediaApi(): MediaApi {
  const api = useApi();
  return useMemo(() => mediaApi(api), [api]);
}

export type LoadState<T> = { status: 'loading'; previous: T | null } | { status: 'error'; error: unknown } | { status: 'ready'; data: T };

/**
 * Reads something again whenever `key` changes. While the next answer is on its way the last one stays (`previous`), so a list does not
 * jump to "Loading…" at every keystroke of the search, and an answer that arrives after the key changed again is ignored. `reload` reads
 * the same thing again, after something was changed. While `enabled` is false nothing is asked for (the workspace is not known yet).
 */
function useLoad<T>(load: () => Promise<T>, key: string, enabled = true) {
  const [state, setState] = useState<LoadState<T>>({ status: 'loading', previous: null });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!enabled) {
      return;
    }
    let active = true;
    setState((current) => ({ status: 'loading', previous: current.status === 'ready' ? current.data : current.status === 'loading' ? current.previous : null }));
    load().then(
      (data) => {
        if (active) {
          setState({ status: 'ready', data });
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
    // The key says when to read again; `load` is made new at every draw.
  }, [key, attempt, enabled]);

  const reload = useCallback(() => setAttempt((count) => count + 1), []);
  return { state, reload };
}

/** The page of files for some choices, in a workspace. */
export function useMediaList(filters: MediaFilters, workspaceId: string) {
  const media = useMediaApi();
  const params = listParams(filters, workspaceId);
  return useLoad<MediaPage>(() => media.list(params), JSON.stringify(params), workspaceId !== '');
}

/** The folders and tags of a workspace, with their counts. */
export function useMediaOverview(workspaceId: string) {
  const media = useMediaApi();
  return useLoad<MediaOverview>(() => media.overview(workspaceId), workspaceId, workspaceId !== '');
}

/** The size limit and the extensions the server accepts. Read once. */
export function useMediaConfig() {
  const media = useMediaApi();
  return useLoad<MediaConfig>(() => media.config(), 'config');
}
