// The list page's filters live in the address (?type=blog_post&status=draft&q=dream&page=2), so a reload, the back button and a copied
// link all show the same list. These two functions are the only place the address and the filters meet; what the address holds is
// checked here, because a person can type anything into it.

import { CONTENT_STATUSES, CONTENT_TYPES, SOCIAL_PLATFORMS, type PostListParams } from '@/api/content';
import { DATE_RANGES, rangeStart } from '@/lib/content';

export const PAGE_SIZE = 10;
const MAX_SEARCH = 200;

export interface ListFilters {
  /** A kind of content (the tab), or empty for all. */
  type: string;
  status: string;
  platform: string;
  /** The date range's value (days back), or empty for any time. */
  range: string;
  q: string;
  /** A workspace, or empty for all of the organization's. */
  workspaceId: string;
  page: number;
}

export const noFilters: ListFilters = { type: '', status: '', platform: '', range: '', q: '', workspaceId: '', page: 1 };

const oneOf = (value: string | null, allowed: readonly string[]) => (value !== null && allowed.includes(value) ? value : '');

/** The filters an address holds. Anything that is not a value the list knows is ignored, as if it were not there. */
export function filtersFromSearch(search: URLSearchParams): ListFilters {
  const page = Number.parseInt(search.get('page') ?? '', 10);
  const workspaceId = search.get('workspace') ?? '';
  return {
    type: oneOf(
      search.get('type'),
      CONTENT_TYPES.map((entry) => entry.value),
    ),
    status: oneOf(search.get('status'), CONTENT_STATUSES),
    platform: oneOf(search.get('platform'), SOCIAL_PLATFORMS),
    range: oneOf(
      search.get('range'),
      DATE_RANGES.map((entry) => entry.value),
    ),
    q: (search.get('q') ?? '').slice(0, MAX_SEARCH),
    workspaceId: /^[0-9a-f-]{8,40}$/i.test(workspaceId) ? workspaceId : '',
    page: Number.isInteger(page) && page >= 1 ? page : 1,
  };
}

/** The address for some filters: only what is set, in a fixed order, so the same filters always give the same address. */
export function searchFromFilters(filters: ListFilters): URLSearchParams {
  const search = new URLSearchParams();
  const set = (key: string, value: string) => {
    if (value.length > 0) {
      search.set(key, value);
    }
  };
  set('type', filters.type);
  set('status', filters.status);
  set('platform', filters.platform);
  set('range', filters.range);
  set('workspace', filters.workspaceId);
  set('q', filters.q.trim());
  if (filters.page > 1) {
    search.set('page', String(filters.page));
  }
  return search;
}

/** Whether anything but the tab narrows the list: what "Reset" would undo. */
export function isNarrowed(filters: ListFilters): boolean {
  return filters.status !== '' || filters.platform !== '' || filters.range !== '' || filters.q.trim() !== '' || filters.workspaceId !== '';
}

/** What goes to the server for these filters. `now` is only for the date range. */
export function listParams(filters: ListFilters, now: Date = new Date()): PostListParams {
  return {
    workspaceId: filters.workspaceId || undefined,
    type: filters.type || undefined,
    status: filters.status || undefined,
    platform: filters.platform || undefined,
    from: rangeStart(filters.range, now),
    q: filters.q.trim() || undefined,
    page: filters.page,
    pageSize: PAGE_SIZE,
  };
}
