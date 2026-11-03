import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { Pager } from '@/components/ui/Pager';
import { messageForSiteLoadError } from '@/lib/messages';
import { OrganizationList } from './OrganizationList';
import type { SiteDashboardState } from './useSiteDashboard';

/** A page of every organization on the platform, with paging, a Refresh button, and an error with Try again. */
export function OrganizationsSection({
  state,
  onReload,
  onPage,
}: {
  state: SiteDashboardState;
  onReload: () => void;
  onPage: (page: number) => void;
}) {
  if (state.status === 'loading') {
    return <Loading />;
  }

  if (state.status === 'error') {
    return (
      <ErrorNotice message={messageForSiteLoadError(state.error)}>
        <button className="button" type="button" onClick={onReload}>
          Try again
        </button>
      </ErrorNotice>
    );
  }

  const { items, page, pageSize, totalCount, totalPages } = state.organizations;
  const refresh = (
    <button className="button" type="button" onClick={onReload}>
      Refresh
    </button>
  );

  if (items.length === 0 && totalCount === 0) {
    return (
      <div className="notice">
        <p>No organizations yet. They appear here as people register.</p>
        {refresh}
      </div>
    );
  }

  return (
    <div>
      <OrganizationList organizations={items} />
      <Pager label="Organization pages" page={page} pageSize={pageSize} totalCount={totalCount} totalPages={totalPages} onPage={onPage} />
      <p className="pager-refresh">{refresh}</p>
    </div>
  );
}
