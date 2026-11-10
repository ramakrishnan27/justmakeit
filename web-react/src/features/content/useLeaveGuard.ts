import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * While there is something unsaved: the browser asks before the tab is closed or reloaded, and a click on any link of this app (the
 * sidebar, the header, a "Back" link) is held back and turned into a question, so a draft is never thrown away by accident. The
 * browser's Back button is not covered (the router has no hook for it here); a reload or close is.
 */
export function useLeaveGuard(dirty: boolean) {
  const navigate = useNavigate();
  const [target, setTarget] = useState<string | null>(null);

  useEffect(() => {
    if (!dirty) {
      return;
    }

    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
      // Some browsers want a value; none show it.
      event.returnValue = '';
    }

    function onClick(event: MouseEvent) {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
        return;
      }
      const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null;
      if (!anchor || anchor.getAttribute('target') === '_blank') {
        return;
      }
      const href = anchor.getAttribute('href') ?? '';
      // Only links inside this app: a path that starts with a single slash. Mail links, other sites and "#" are left alone.
      if (!href.startsWith('/') || href.startsWith('//')) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      setTarget(href);
    }

    window.addEventListener('beforeunload', onBeforeUnload);
    // Capture phase, so it runs before the router's own handler on the link.
    document.addEventListener('click', onClick, true);
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload);
      document.removeEventListener('click', onClick, true);
    };
  }, [dirty]);

  const stay = useCallback(() => setTarget(null), []);
  const leave = useCallback(() => {
    if (target !== null) {
      const where = target;
      setTarget(null);
      navigate(where);
    }
  }, [navigate, target]);

  return { target, stay, leave };
}
