import { SOCIAL_PLATFORMS, type PlatformRule } from '@/api/content';
import type { Workspace } from '@/api/types';
import { UiIcon } from '@/components/icons/UiIcon';
import { DATE_RANGES, STATUS_FILTERS, platformName } from '@/lib/content';
import { isNarrowed, type ListFilters } from './listFilters';

type WorkspaceChoice = Pick<Workspace, 'id' | 'name'>;

/**
 * The row of filters above the list: status, platform, campaign, date range, a search box and Reset. Choosing one changes the
 * address (through `onChange`), which changes the list. The campaign filter is switched off while there are no campaigns. The
 * workspace filter is there only for an organization with more than one (`workspaces`, with the word its kind of organization uses).
 */
export function ContentFilters({
  filters,
  search,
  onSearch,
  onChange,
  onReset,
  rules,
  campaignsAvailable,
  workspaces,
  workspaceAll,
}: {
  filters: ListFilters;
  search: string;
  onSearch: (value: string) => void;
  onChange: (change: Partial<ListFilters>) => void;
  onReset: () => void;
  rules: readonly PlatformRule[];
  campaignsAvailable: boolean;
  workspaces: readonly WorkspaceChoice[];
  /** "All products" or "All clients". */
  workspaceAll: string;
}) {
  const enabled = new Set(rules.filter((rule) => rule.enabled).map((rule) => rule.platform));

  return (
    <div className="content-filters" role="search" aria-label="Filter content">
      {workspaces.length > 1 ? (
        <div className="filter">
          <label className="visually-hidden" htmlFor="filter-workspace">
            {workspaceAll}
          </label>
          <select id="filter-workspace" value={filters.workspaceId} onChange={(event) => onChange({ workspaceId: event.target.value, page: 1 })}>
            <option value="">{workspaceAll}</option>
            {workspaces.map((workspace) => (
              <option key={workspace.id} value={workspace.id}>
                {workspace.name}
              </option>
            ))}
          </select>
        </div>
      ) : null}

      <div className="filter">
        <label className="visually-hidden" htmlFor="filter-status">
          Status
        </label>
        <select id="filter-status" value={filters.status} onChange={(event) => onChange({ status: event.target.value, page: 1 })}>
          <option value="">All Status</option>
          {STATUS_FILTERS.map((status) => (
            <option key={status.value} value={status.value}>
              {status.label}
            </option>
          ))}
        </select>
      </div>

      <div className="filter">
        <label className="visually-hidden" htmlFor="filter-platform">
          Platform
        </label>
        <select id="filter-platform" value={filters.platform} onChange={(event) => onChange({ platform: event.target.value, page: 1 })}>
          <option value="">All Platforms</option>
          {SOCIAL_PLATFORMS.map((platform) => (
            <option key={platform} value={platform}>
              {platformName(platform)}
              {enabled.has(platform) ? '' : ' (not enabled)'}
            </option>
          ))}
        </select>
      </div>

      <div className="filter">
        <label className="visually-hidden" htmlFor="filter-campaign">
          Campaign
        </label>
        <select id="filter-campaign" disabled={!campaignsAvailable} defaultValue="" title={campaignsAvailable ? undefined : 'Campaigns are not available yet'}>
          <option value="">All Campaigns</option>
        </select>
      </div>

      <div className="filter">
        <label className="visually-hidden" htmlFor="filter-range">
          Date range
        </label>
        <select id="filter-range" value={filters.range} onChange={(event) => onChange({ range: event.target.value, page: 1 })}>
          {DATE_RANGES.map((range) => (
            <option key={range.value} value={range.value}>
              {range.label}
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
          aria-label="Search content"
          placeholder="Search content…"
          autoComplete="off"
          maxLength={200}
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
