// The Media Library's side of the API (/v1/media): its types and one function per endpoint. Every request goes through the API client,
// which is the only place that calls fetch. The values are the server's own lower-case names; a test reads the server's source and
// fails if a list here drifts from it.

import type { ApiClient } from '@/api/client';
import { queryString } from '@/api/content';

/** The groups the tabs show. Same values as MediaType.cs. */
export const MEDIA_TYPES = [
  { value: 'image', label: 'Images' },
  { value: 'video', label: 'Videos' },
  { value: 'document', label: 'Documents' },
  { value: 'other', label: 'Others' },
] as const;
export type MediaType = (typeof MEDIA_TYPES)[number]['value'];

/** The sizes the list can be narrowed to. Same values as MediaListQuery.Sizes. */
export const MEDIA_SIZES = [
  { value: 'small', label: 'Under 1 MB' },
  { value: 'medium', label: '1 to 10 MB' },
  { value: 'large', label: 'Over 10 MB' },
] as const;
export type MediaSize = (typeof MEDIA_SIZES)[number]['value'];

/** The orders the list can take. Same values as MediaListQuery.Sorts. */
export const MEDIA_SORTS = [
  { value: 'newest', label: 'Newest First' },
  { value: 'oldest', label: 'Oldest First' },
  { value: 'name', label: 'Name (A to Z)' },
  { value: 'size', label: 'Largest First' },
] as const;
export type MediaSort = (typeof MEDIA_SORTS)[number]['value'];

// --- What the API sends ---------------------------------------------------------------------------------------------

export interface MediaTagRef {
  id: string;
  name: string;
}

/** One file. Its bytes are at mediaFileUrl(id). */
export interface MediaAsset {
  id: string;
  workspaceId: string;
  fileName: string;
  mimeType: string;
  mediaType: MediaType;
  sizeBytes: number;
  /** Pixels, for an image whose size could be read. */
  width: number | null;
  height: number | null;
  /** How many things (posts, templates) use the file; a file in use cannot be deleted. */
  usageCount: number;
  createdAt: string;
  tags: MediaTagRef[];
  folderIds: string[];
}

export interface MediaTypeCounts {
  all: number;
  image: number;
  video: number;
  document: number;
  other: number;
}

export interface MediaPage {
  items: MediaAsset[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  /** Per tab, under every filter except the type, so the tabs keep their numbers while one is open. */
  typeCounts: MediaTypeCounts;
}

export interface MediaFolder {
  id: string;
  name: string;
  parentFolderId: string | null;
  assetCount: number;
}

export interface MediaTagCount {
  id: string;
  name: string;
  assetCount: number;
}

/** The side panel: the folders and tags of a workspace, and the counts of "All Media" and "Uncategorized". */
export interface MediaOverview {
  allCount: number;
  uncategorizedCount: number;
  folders: MediaFolder[];
  tags: MediaTagCount[];
}

/** What the page needs to check a file before sending it. */
export interface MediaConfig {
  maxFileBytes: number;
  /** Lower-case, with the dot: ".png". */
  extensions: string[];
}

// --- What the page sends --------------------------------------------------------------------------------------------

/** The list's filters as they go into the query string. Empty values are left out. */
export interface MediaListParams {
  workspaceId: string;
  type?: string;
  folderId?: string;
  uncategorized?: boolean;
  tagId?: string;
  q?: string;
  size?: string;
  from?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

export interface UploadInput {
  workspaceId: string;
  file: File;
  folderId?: string;
  tags?: readonly string[];
}

export type OrganizeMode = 'move' | 'copy';

/** Where the bytes of a file are. The browser sends the sign-in cookie with the request, so an <img> or <video> can use it as it is. */
export function mediaFileUrl(id: string, { download = false }: { download?: boolean } = {}): string {
  return `/v1/media/assets/${encodeURIComponent(id)}/file${download ? '?download=true' : ''}`;
}

const assets = '/v1/media/assets';

/** One function per endpoint of /v1/media. Made from the API client the session gave the page. */
export function mediaApi(api: ApiClient) {
  return {
    config: () => api.request<MediaConfig>('/v1/media/config'),
    overview: (workspaceId: string) => api.request<MediaOverview>(`/v1/media/overview${queryString({ workspaceId })}`),
    // Spelled out, not spread, so the order of the query string is always the same whatever order the caller built the object in.
    list: (p: MediaListParams) =>
      api.request<MediaPage>(
        `${assets}${queryString({
          workspaceId: p.workspaceId,
          type: p.type,
          folderId: p.folderId,
          uncategorized: p.uncategorized ? 'true' : undefined,
          tagId: p.tagId,
          q: p.q,
          size: p.size,
          from: p.from,
          sort: p.sort,
          page: p.page,
          pageSize: p.pageSize,
        })}`,
      ),
    get: (id: string) => api.request<MediaAsset>(`${assets}/${id}`),
    /** One file, as the form the server reads: the people's tags go one field each. */
    upload: ({ workspaceId, file, folderId, tags = [] }: UploadInput) => {
      const form = new FormData();
      form.append('workspaceId', workspaceId);
      if (folderId) {
        form.append('folderId', folderId);
      }
      for (const tag of tags) {
        form.append('tags', tag);
      }
      form.append('file', file, file.name);
      return api.request<MediaAsset>(assets, { method: 'POST', body: form });
    },
    remove: (id: string) => api.request<null>(`${assets}/${id}`, { method: 'DELETE' }),
    /** `copy` adds the files to a folder; `move` takes them out of every folder first (no folder: Uncategorized). */
    organize: (assetIds: readonly string[], folderId: string | null, mode: OrganizeMode) =>
      api.request<null>(`${assets}/organize`, { method: 'POST', body: { assetIds, folderId, mode } }),
    createFolder: (workspaceId: string, name: string) =>
      api.request<MediaFolder>('/v1/media/folders', { method: 'POST', body: { workspaceId, name } }),
    removeFolder: (id: string) => api.request<null>(`/v1/media/folders/${id}`, { method: 'DELETE' }),
  };
}

export type MediaApi = ReturnType<typeof mediaApi>;
