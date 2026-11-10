import { useCallback, useEffect, useState } from 'react';
import type { OrganizationChannel } from '@/api/types';
import { useApi } from '@/session/SessionContext';

export type PlatformsState =
  | { status: 'loading' }
  | { status: 'error'; error: unknown }
  | { status: 'ready'; platforms: OrganizationChannel[] };

/** Loads every platform (channel) with whether it is enabled for the person's organization. `reload` reads them again (to retry). */
export function usePlatforms() {
  const api = useApi();
  const [state, setState] = useState<PlatformsState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    api.request<OrganizationChannel[]>('/v1/platforms').then(
      (platforms) => {
        if (active) {
          setState({ status: 'ready', platforms });
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
