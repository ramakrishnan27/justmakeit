import { useCallback, useEffect, useState } from 'react';
import type { Workspace } from '@/api/types';
import { useApi } from '@/session/SessionContext';

export type WorkspacesState = { status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; workspaces: Workspace[] };

/** Loads the workspaces of the person's organization. `reload` reads them again (after one was added, or to retry). */
export function useWorkspaces() {
  const api = useApi();
  const [state, setState] = useState<WorkspacesState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    api.request<Workspace[]>('/v1/workspaces').then(
      (workspaces) => {
        if (active) {
          setState({ status: 'ready', workspaces });
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
  }, [api, attempt]);

  const reload = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((count) => count + 1);
  }, []);

  return { state, reload };
}
