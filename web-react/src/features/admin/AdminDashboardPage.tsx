import { Loading } from '@/components/ui/Notice';
import { PageTitle } from '@/components/ui/PageTitle';
import { Section } from '@/components/ui/Section';
import { AdminSessionSection } from './AdminSessionSection';
import { OrganizationsSection } from './OrganizationsSection';
import { OverviewStats } from './OverviewStats';
import { useSiteDashboard } from './useSiteDashboard';

/**
 * The Site admin dashboard: platform totals, a page of every organization, and the claims of the current access token.
 * The frame around it (header, menu, sign out) is the app shell. What is shown is read through /v1/site, which the
 * server answers only to a Site admin (and checks the database each time).
 */
export default function AdminDashboardPage() {
  const { state, reload, goTo } = useSiteDashboard();

  return (
    <>
      <PageTitle documentTitle="Dashboard">Dashboard</PageTitle>
      <p className="muted">A view across every organization on the platform.</p>

      <Section title="Overview">
        <div aria-live="polite">
          {state.status === 'loading' ? <Loading /> : null}
          {state.status === 'ready' ? <OverviewStats overview={state.overview} /> : null}
        </div>
      </Section>

      <Section title="Organizations">
        <div aria-live="polite">
          <OrganizationsSection state={state} onReload={reload} onPage={goTo} />
        </div>
      </Section>

      <AdminSessionSection />
    </>
  );
}
