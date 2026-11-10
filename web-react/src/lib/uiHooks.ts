// Small hooks for pieces of interface that more than one feature has: a value that waits for typing to pause, and a menu or picker that
// closes on Escape or on a press outside. They hold no feature's data, so they live here and not in a feature's folder.

import { useEffect, useState, type RefObject } from 'react';

/** A value that follows `value` after it has stopped changing for `delay` milliseconds. */
export function useDebounced<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/**
 * Closes something that opened on a click (a menu, a picker) when Escape is pressed, or when a press lands outside `ref`. Escape
 * gives focus back to `returnFocus`, which is the control that opened it.
 */
export function useDismiss(open: boolean, close: () => void, ref: RefObject<HTMLElement | null>, returnFocus?: RefObject<HTMLElement | null>) {
  useEffect(() => {
    if (!open) {
      return;
    }
    function onKeydown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        close();
        returnFocus?.current?.focus();
      }
    }
    function onPress(event: MouseEvent) {
      if (ref.current && event.target instanceof Node && !ref.current.contains(event.target)) {
        close();
      }
    }
    document.addEventListener('keydown', onKeydown);
    document.addEventListener('mousedown', onPress);
    return () => {
      document.removeEventListener('keydown', onKeydown);
      document.removeEventListener('mousedown', onPress);
    };
  }, [open, close, ref, returnFocus]);
}
