import { MEDIA_SIZES, type MediaTagCount } from '@/api/media';
import { UiIcon } from '@/components/icons/UiIcon';
import { DATE_RANGES } from '@/lib/content';
import { isNarrowed, type MediaFilters } from './mediaFilters';

/**
 * The row of filters above the files: size, date, tag, a search box and Reset. Choosing one changes the address (through `onChange`), which
 * changes the page. The search matches the name of a file or of one of its tags.
 */
export function MediaFiltersBar({
  filters,
  search,
  onSearch,
  onChange,
  onReset,
  tags,
}: {
  filters: MediaFilters;
  search: string;
  onSearch: (value: string) => void;
  onChange: (change: Partial<MediaFilters>) => void;
  onReset: () => void;
  tags: readonly MediaTagCount[];
}) {
  return (
    <div className="content-filters" role="search" aria-label="Filter media">
      <div className="filter">
        <label className="visually-hidden" htmlFor="media-filter-size">
          Size
        </label>
        <select id="media-filter-size" value={filters.size} onChange={(event) => onChange({ size: event.target.value, page: 1 })}>
          <option value="">All Sizes</option>
          {MEDIA_SIZES.map((size) => (
            <option key={size.value} value={size.value}>
              {size.label}
            </option>
          ))}
        </select>
      </div>

      <div className="filter">
        <label className="visually-hidden" htmlFor="media-filter-range">
          Date added
        </label>
        <select id="media-filter-range" value={filters.range} onChange={(event) => onChange({ range: event.target.value, page: 1 })}>
          <option value="">All Dates</option>
          {DATE_RANGES.filter((range) => range.value !== '').map((range) => (
            <option key={range.value} value={range.value}>
              {range.label}
            </option>
          ))}
        </select>
      </div>

      <div className="filter">
        <label className="visually-hidden" htmlFor="media-filter-tag">
          Tag
        </label>
        <select id="media-filter-tag" value={filters.tagId} onChange={(event) => onChange({ tagId: event.target.value, page: 1 })}>
          <option value="">All Tags</option>
          {tags.map((tag) => (
            <option key={tag.id} value={tag.id}>
              #{tag.name}
            </option>
          ))}
        </select>
      </div>

      <div className="search filter-search">
        <span className="search-icon">
          <UiIcon name="search" />
        </span>
        <input
          type="search"
          name="search"
          aria-label="Search media"
          placeholder="Search files or tags…"
          autoComplete="off"
          maxLength={100}
          value={search}
          onChange={(event) => onSearch(event.target.value)}
        />
      </div>

      <button className="button" type="button" onClick={onReset} disabled={!isNarrowed(filters) && search.trim() === ''}>
        Reset
      </button>
    </div>
  );
}
