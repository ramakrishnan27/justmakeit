import { Link } from 'react-router-dom';
import { MEDIA_TYPES, type MediaTypeCounts } from '@/api/media';
import { formatNumber } from '@/lib/format';
import { searchFromFilters, type MediaFilters } from './mediaFilters';
import { MediaTypeIcon } from './MediaThumb';

/**
 * The kinds of file as tabs with their counts ("Images (180)"). Each is a link to the same page with that kind chosen, so the choice is in
 * the address. The counts come from the server under every filter except the kind, so they stay while a tab is open.
 */
export function MediaTabs({ filters, counts }: { filters: MediaFilters; counts: MediaTypeCounts | null }) {
  const tabs = [{ value: '', label: 'All Media', key: 'all' as const }, ...MEDIA_TYPES.map((type) => ({ value: type.value, label: type.label, key: type.value }))];

  return (
    <nav className="content-tabs" aria-label="Kind of media">
      <ul>
        {tabs.map((tab) => {
          const current = filters.type === tab.value;
          const count = counts ? counts[tab.key] : undefined;
          return (
            <li key={tab.key}>
              <Link
                className="content-tab"
                to={{ search: searchFromFilters({ ...filters, type: tab.value, page: 1 }).toString() }}
                aria-current={current ? 'page' : undefined}
              >
                <MediaTypeIcon type={tab.value === '' ? 'image' : tab.value} />
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
