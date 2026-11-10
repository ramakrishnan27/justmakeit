import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import { contentApi, type Capabilities, type ContentApi, type PlatformRule } from '@/api/content';
import { useApi } from '@/session/SessionContext';

/** The Content Studio endpoints, made from the session's API client. The same object for the life of the page. */
export function useContentApi(): ContentApi {
  const api = useApi();
  return useMemo(() => contentApi(api), [api]);
}

export type MetaState =
  | { status: 'loading' }
  | { status: 'error'; error: unknown }
  | { status: 'ready'; capabilities: Capabilities; rules: PlatformRule[] };

/** What both pages need first: what is real here (media, AI, campaigns, publishing) and each platform's limits and whether it is enabled. */
export function useContentMeta() {
  const content = useContentApi();
  const [state, setState] = useState<MetaState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    Promise.all([content.capabilities(), content.platformRules()]).then(
      ([capabilities, rules]) => {
        if (active) {
          setState({ status: 'ready', capabilities, rules });
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
  }, [content, attempt]);

  const reload = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((count) => count + 1);
  }, []);

  return { state, reload };
}

/** Keeps the latest value in a ref, for a callback that must not change but must see the newest state. */
export function useLatest<T>(value: T): RefObject<T> {
  const ref = useRef(value);
  useEffect(() => {
    ref.current = value;
  });
  return ref;
}
