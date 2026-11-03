import { useEffect, useRef, type ReactNode } from 'react';

const siteName = 'Platform';

/**
 * The heading of a page. It names the browser tab, and takes focus when the page appears so a screen reader
 * announces where the user has arrived (the page is not reloaded when they move between pages).
 */
export function PageTitle({ children, documentTitle, id }: { children: ReactNode; documentTitle: string; id?: string }) {
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    document.title = `${documentTitle} · ${siteName}`;
  }, [documentTitle]);

  useEffect(() => {
    heading.current?.focus();
  }, []);

  return (
    <h1 id={id} ref={heading} tabIndex={-1}>
      {children}
    </h1>
  );
}
