import { useCallback, useEffect, useState } from 'react';
import type { SiteChannel } from '@/api/types';
import { useApi } from '@/session/SessionContext';

export type SiteChannelsState =
  | { status: 'loading' }
  | { status: 'error'; error: unknown }
  | { status: 'ready'; channels: SiteChannel[] };

/**
 * Loads every channel (a few dozen at most, so one read, no paging). `reload` reads them again, and `create` sends a new
 * channel as a form (it carries the icon file) and, once the server has made it, puts it at the end of the list: the list is
 * oldest first, so that is where the server would put it too.
 */
export function useSiteChannels() {
  const api = useApi();
  const [state, setState] = useState<SiteChannelsState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    api.request<SiteChannel[]>('/v1/site/channels').then(
      (channels) => {
        if (active) {
          setState({ status: 'ready', channels });
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

  const create = useCallback(
    async (form: FormData) => {
      const created = await api.request<SiteChannel>('/v1/site/channels', { method: 'POST', body: form });
      setState((current) => (current.status === 'ready' ? { status: 'ready', channels: [...current.channels, created] } : current));
      return created;
    },
    [api],
  );

  return { state, reload, create };
}
