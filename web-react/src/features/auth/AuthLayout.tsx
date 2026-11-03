import type { ReactNode } from 'react';
import { PageTitle } from '@/components/ui/PageTitle';

/** The frame of the sign-in and sign-up pages: the logo, and one card with the page heading, an introduction and a link to the other page. */
export function AuthLayout({
  titleId,
  title,
  documentTitle,
  intro,
  wide = false,
  footer,
  children,
}: {
  titleId: string;
  title: string;
  documentTitle: string;
  intro: string;
  wide?: boolean;
  footer: ReactNode;
  children: ReactNode;
}) {
  return (
    <main id="main" className="auth-page auth-brand">
      <div className="auth-header">
        <img className="brand-mark" src="/brand-mark.svg" alt="" width={32} height={32} />
        <span>Platform</span>
      </div>
      <section className={`card auth-card${wide ? ' auth-card-wide' : ''}`} aria-labelledby={titleId}>
        <PageTitle id={titleId} documentTitle={documentTitle}>
          {title}
        </PageTitle>
        <p className="muted">{intro}</p>
        {children}
        <p className="auth-switch">{footer}</p>
      </section>
    </main>
  );
}
