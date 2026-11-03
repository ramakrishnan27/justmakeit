import { Link } from 'react-router-dom';
import { PageTitle } from '@/components/ui/PageTitle';
import { useApp } from '@/navigation/AppContext';

/** Shown while the session is being restored from the refresh cookie, before anyone knows whether to show a page or the sign-in form. */
export function BootScreen() {
  return (
    <p className="boot" role="status">
      Loading…
    </p>
  );
}

/** A short page of words and a way home, for the places a signed-in person cannot go. */
function MessagePage({ title, documentTitle, text }: { title: string; documentTitle: string; text: string }) {
  const { homePath } = useApp();

  return (
    <section>
      <PageTitle documentTitle={documentTitle}>{title}</PageTitle>
      <p className="muted">{text}</p>
      <Link className="button button-primary" to={homePath}>
        Go to the dashboard
      </Link>
    </section>
  );
}

/**
 * The permission the page needs is not in the person's token. The sidebar already leaves the link out; this is for a
 * typed address or an old bookmark. The server refuses the page's requests as well.
 */
export function ForbiddenPage() {
  return (
    <MessagePage
      title="You do not have access to this page"
      documentTitle="No access"
      text="Your role does not include the permission this page needs. If you think it should, ask an organization admin."
    />
  );
}

export function NotFoundPage() {
  return <MessagePage title="Page not found" documentTitle="Not found" text="There is nothing at this address." />;
}

/** The not-found page of a visitor who is not signed in: no frame, and a way to the sign-in form. */
export function BareNotFoundPage() {
  const { homePath } = useApp();

  return (
    <main id="main" className="auth-page">
      <section className="card auth-card">
        <PageTitle documentTitle="Not found">Page not found</PageTitle>
        <p className="muted">There is nothing at this address.</p>
        <Link className="button button-primary button-block" to={homePath}>
          Go to the dashboard
        </Link>
      </section>
    </main>
  );
}
