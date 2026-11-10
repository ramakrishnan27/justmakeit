import { Link } from 'react-router-dom';
import { CONTENT_TYPES } from '@/api/content';
import { formatNumber } from '@/lib/format';
import { ContentTypeIcon } from './icons';
import { searchFromFilters, type ListFilters } from './listFilters';

/**
 * The kinds of content as tabs with their counts ("Social Posts (10)"). Each is a link to the same list with that kind chosen, so the
 * choice is in the address. The counts come from the server under every filter except the kind, so they stay while a tab is open.
 */
export function ContentTabs({ filters, counts }: { filters: ListFilters; counts: Record<string, number> | null }) {
  const tabs = [{ value: '', label: 'All Content', type: null }, ...CONTENT_TYPES.map((type) => ({ value: type.value, label: type.plural, type: type.value }))];

  return (
    <nav className="content-tabs" aria-label="Kind of content">
      <ul>
        {tabs.map((tab) => {
          const current = filters.type === tab.value;
          const count = counts ? counts[tab.value === '' ? 'all' : tab.value] : undefined;
          return (
            <li key={tab.value || 'all'}>
              <Link
                className="content-tab"
                to={{ search: searchFromFilters({ ...filters, type: tab.value, page: 1 }).toString() }}
                aria-current={current ? 'page' : undefined}
              >
                {tab.type ? <ContentTypeIcon type={tab.type} /> : null}
                {tab.label}
                {/* A space between the two, for the name a screen reader reads; the layout ignores it. */}
                {count === undefined ? null : (
                  <>
                    {' '}
                    <span className="content-tab-count">({formatNumber(count)})</span>
                  </>
                )}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
