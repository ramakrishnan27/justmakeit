import type { ReactNode } from 'react';
import type { PagedResult } from '@/api/types';
import { ErrorNotice, Loading } from '@/components/ui/Notice';
import { Pager } from '@/components/ui/Pager';
import { messageForSiteLoadError } from '@/lib/messages';
import type { PagedListState } from './useSitePagedList';

/**
 * One page of a list the Site admin reads, with what goes around it: a loading line, an error with Try again, a message
 * when there is nothing yet, the pager, and a Refresh button. What a row looks like is the caller's: `children` draws the
 * rows of the page.
 */
export function SitePagedList<T>({
  state,
  subject,
  pagerLabel,
  empty,
  onReload,
  onPage,
  children,
}: {
  state: PagedListState<T>;
  /** Names the list in the error message: "The members" gives "The members could not be loaded." */
  subject: string;
  /** Names the pager for a screen reader, for example "Member pages". */
  pagerLabel: string;
  /** What to say when the list is empty. */
  empty: string;
  onReload: () => void;
  onPage: (page: number) => void;
  children: (items: PagedResult<T>['items']) => ReactNode;
}) {
  if (state.status === 'loading') {
    return <Loading />;
  }

  if (state.status === 'error') {
    return (
      <ErrorNotice message={messageForSiteLoadError(state.error, subject)}>
        <button className="button" type="button" onClick={onReload}>
          Try again
        </button>
      </ErrorNotice>
    );
  }

  const { items, page, pageSize, totalCount, totalPages } = state.result;
  const refresh = (
    <button className="button" type="button" onClick={onReload}>
      Refresh
    </button>
  );

  if (items.length === 0 && totalCount === 0) {
    return (
      <div className="notice">
        <p>{empty}</p>
        {refresh}
      </div>
    );
  }

  return (
    <div>
      {children(items)}
      <Pager label={pagerLabel} page={page} pageSize={pageSize} totalCount={totalCount} totalPages={totalPages} onPage={onPage} />
      <p className="pager-refresh">{refresh}</p>
    </div>
  );
}
