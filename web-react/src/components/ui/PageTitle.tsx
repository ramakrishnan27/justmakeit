import { useEffect, useRef, type ReactNode } from 'react';
import { useApp } from '@/navigation/AppContext';

/**
 * The heading of a page. It names the browser tab ("Dashboard · Platform", or "· Site admin" in that app), and takes
 * focus when the page appears so a screen reader announces where the user has arrived (the page is not reloaded when
 * they move between pages).
 */
export function PageTitle({ children, documentTitle, id }: { children: ReactNode; documentTitle: string; id?: string }) {
  const heading = useRef<HTMLHeadingElement>(null);
  const { label } = useApp();

  useEffect(() => {
    document.title = `${documentTitle} · ${label}`;
  }, [documentTitle, label]);

  useEffect(() => {
    heading.current?.focus();
  }, []);

  return (
    <h1 id={id} ref={heading} tabIndex={-1}>
      {children}
    </h1>
  );
}
