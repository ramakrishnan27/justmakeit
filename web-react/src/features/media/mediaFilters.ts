// The Media Library's choices live in the address (?type=image&folder=<id>&tag=<id>&size=small&range=30&q=beach&sort=name&view=list&page=2), so a
// reload, the back button and a copied link all show the same page. These functions are the only place the address and the choices meet; what
// the address holds is checked here, because a person can type anything into it.

import { MEDIA_SIZES, MEDIA_SORTS, MEDIA_TYPES, type MediaListParams } from '@/api/media';
import { DATE_RANGES, rangeStart } from '@/lib/content';

export const PAGE_SIZE = 12;
const MAX_SEARCH = 100;

/** What `folder` holds in the address for "files that are in no folder". */
export const UNCATEGORIZED = 'none';

export type MediaView = 'grid' | 'list';

export interface MediaFilters {
  /** The workspace the library shows, or empty for the first one the organization has. */
  workspaceId: string;
  /** A tab (a kind of file), or empty for all. */
  type: string;
  /** A folder's id, `none` for files in no folder, or empty for all. */
  folder: string;
  tagId: string;
  size: string;
  /** The date range's value (days back), or empty for any time. */
  range: string;
  q: string;
  sort: string;
  view: MediaView;
  page: number;
}

export const noFilters: MediaFilters = { workspaceId: '', type: '', folder: '', tagId: '', size: '', range: '', q: '', sort: 'newest', view: 'grid', page: 1 };

const oneOf = (value: string | null, allowed: readonly string[]) => (value !== null && allowed.includes(value) ? value : '');
const idLike = (value: string | null) => (value !== null && /^[0-9a-f-]{8,40}$/i.test(value) ? value : '');

/** The choices an address holds. Anything that is not a value the page knows is ignored, as if it were not there. */
export function filtersFromSearch(search: URLSearchParams): MediaFilters {
  const page = Number.parseInt(search.get('page') ?? '', 10);
  const folder = search.get('folder');
  const sort = oneOf(search.get('sort'), MEDIA_SORTS.map((entry) => entry.value));
  return {
    workspaceId: idLike(search.get('workspace')),
    type: oneOf(search.get('type'), MEDIA_TYPES.map((entry) => entry.value)),
    folder: folder === UNCATEGORIZED ? UNCATEGORIZED : idLike(folder),
    tagId: idLike(search.get('tag')),
    size: oneOf(search.get('size'), MEDIA_SIZES.map((entry) => entry.value)),
    range: oneOf(search.get('range'), DATE_RANGES.map((entry) => entry.value)),
    q: (search.get('q') ?? '').slice(0, MAX_SEARCH),
    sort: sort === '' ? 'newest' : sort,
    view: search.get('view') === 'list' ? 'list' : 'grid',
    page: Number.isInteger(page) && page >= 1 ? page : 1,
  };
}

/** The address for some choices: only what is not the default, in a fixed order, so the same choices always give the same address. */
export function searchFromFilters(filters: MediaFilters): URLSearchParams {
  const search = new URLSearchParams();
  const set = (key: string, value: string) => {
    if (value.length > 0) {
      search.set(key, value);
    }
  };
  set('workspace', filters.workspaceId);
  set('type', filters.type);
  set('folder', filters.folder);
  set('tag', filters.tagId);
  set('size', filters.size);
  set('range', filters.range);
  set('q', filters.q.trim());
  if (filters.sort !== 'newest') {
    search.set('sort', filters.sort);
  }
  if (filters.view === 'list') {
    search.set('view', 'list');
  }
  if (filters.page > 1) {
    search.set('page', String(filters.page));
  }
  return search;
}

/** Whether anything the filter row sets narrows the list: what "Reset" would undo. The tab, the folder and the workspace are not undone by it. */
export function isNarrowed(filters: MediaFilters): boolean {
  return filters.tagId !== '' || filters.size !== '' || filters.range !== '' || filters.q.trim() !== '';
}

/** What goes to the server for these choices. `now` is only for the date range. */
export function listParams(filters: MediaFilters, workspaceId: string, now: Date = new Date()): MediaListParams {
  return {
    workspaceId,
    type: filters.type || undefined,
    folderId: filters.folder !== '' && filters.folder !== UNCATEGORIZED ? filters.folder : undefined,
    uncategorized: filters.folder === UNCATEGORIZED,
    tagId: filters.tagId || undefined,
    q: filters.q.trim() || undefined,
    size: filters.size || undefined,
    from: rangeStart(filters.range, now),
    sort: filters.sort === 'newest' ? undefined : filters.sort,
    page: filters.page,
    pageSize: PAGE_SIZE,
  };
}
