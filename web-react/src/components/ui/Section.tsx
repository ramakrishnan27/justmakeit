import { useId, type ReactNode } from 'react';

/** A titled part of a page: a <section> named by its own heading, so a screen reader can jump to it. */
export function Section({ title, children }: { title: string; children: ReactNode }) {
  const titleId = useId();

  return (
    <section aria-labelledby={titleId}>
      <h2 id={titleId}>{title}</h2>
      {children}
    </section>
  );
}
