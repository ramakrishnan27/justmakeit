import { useCallback, useEffect, useRef, useState } from 'react';
import type { Post } from '@/api/content';
import { useContentApi } from './hooks';

/**
 * `version` counts the times the post was read from the server (0 for a post the page was handed on arrival). The editor starts afresh
 * whenever it changes, so a reload always shows what was read, whether or not the page went through "loading" on the way.
 */
export type SourceState = { status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; post: Post; version: number };

/**
 * Reads the post with this id for the editor. `seed` is a post the page was handed on arrival (just saved, or just duplicated), used
 * instead of asking again. `reload` reads it again, to retry or to take the latest version.
 */
export function usePostSource(id: string | undefined, seed: Post | null) {
  const content = useContentApi();
  const [state, setState] = useState<SourceState>(() => (seed && seed.id === id ? { status: 'ready', post: seed, version: 0 } : { status: 'loading' }));
  const [attempt, setAttempt] = useState(0);
  const reads = useRef(0);

  useEffect(() => {
    if (id === undefined) {
      return;
    }
    if (attempt === 0 && seed && seed.id === id) {
      setState({ status: 'ready', post: seed, version: 0 });
      return;
    }
    let active = true;
    setState({ status: 'loading' });
    content.get(id).then(
      (post) => {
        if (active) {
          reads.current += 1;
          setState({ status: 'ready', post, version: reads.current });
        }
      },
      (error: unknown) => active && setState({ status: 'error', error }),
    );
    return () => {
      active = false;
    };
    // `seed` is read when the address changes, not watched: a new object each render must not read the post again.
  }, [content, id, attempt]);

  const reload = useCallback(() => setAttempt((count) => count + 1), []);
  return { state, reload };
}
