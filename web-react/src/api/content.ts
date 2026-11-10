// Content Studio's side of the API (/v1/content): its types and one function per endpoint. Every request goes through the
// API client, which is the only place that calls fetch. The values are the server's own snake_case names; a test reads the
// server's source and fails if a list here drifts from it.

import type { ApiClient } from '@/api/client';

/** What a piece of content is. Same values as ContentType.cs. */
export const CONTENT_TYPES = [
  { value: 'social_post', label: 'Social Post', plural: 'Social Posts' },
  { value: 'blog_post', label: 'Blog Post', plural: 'Blog Posts' },
  { value: 'video_script', label: 'Video Script', plural: 'Videos' },
  { value: 'carousel', label: 'Carousel', plural: 'Carousels' },
  { value: 'short_reel', label: 'Shorts / Reels', plural: 'Shorts / Reels' },
] as const;
export type ContentType = (typeof CONTENT_TYPES)[number]['value'];

/** Same values as ContentStatus.cs. */
export const CONTENT_STATUSES = ['draft', 'scheduled', 'publishing', 'published', 'failed', 'archived'] as const;
export type ContentStatus = (typeof CONTENT_STATUSES)[number];

/** Same values as MediaRole.cs: what a file is for on a post. The server works it out; nothing here chooses one. */
export const MEDIA_ROLES = ['attachment', 'cover', 'thumbnail', 'slide', 'video'] as const;
export type MediaRole = (typeof MEDIA_ROLES)[number];

/** Same values as ContentVisibility.cs. */
export const VISIBILITIES = ['public', 'private', 'unlisted'] as const;
export type Visibility = (typeof VISIBILITIES)[number];

/** Same values as SocialPlatform.cs, and the keys the publishers of the channel projects announce themselves with. */
export const SOCIAL_PLATFORMS = ['facebook', 'instagram', 'x', 'youtube', 'linkedin', 'tiktok', 'pinterest', 'threads'] as const;
export type SocialPlatform = (typeof SOCIAL_PLATFORMS)[number];

// --- The editor's document ------------------------------------------------------------------------------------------

export interface RichMark {
  type: string;
  attrs?: Record<string, unknown>;
}

/** One part of the editor's document (a ProseMirror node). The server accepts only the kinds in RichTextDocument.cs. */
export interface RichNode {
  type: string;
  attrs?: Record<string, unknown>;
  content?: RichNode[];
  marks?: RichMark[];
  text?: string;
}

/** The whole document: a node of type "doc". */
export type RichDoc = RichNode;

// --- What the API sends ---------------------------------------------------------------------------------------------

export interface TagItem {
  id: string;
  name: string;
  color: string;
}

export interface PostPlatformItem {
  platform: SocialPlatform;
  caption: string | null;
  title: string | null;
  settings: Record<string, unknown>;
  publishStatus: 'pending' | 'scheduled' | 'publishing' | 'published' | 'failed';
  scheduledAt: string | null;
  publishedAt: string | null;
  externalPostUrl: string | null;
  lastError: string | null;
  retryCount: number;
}

export interface PollItem {
  question: string;
  endsAt: string | null;
  options: string[];
}

export interface CtaItem {
  buttonText: string;
  targetUrl: string;
}

export interface MentionItem {
  platform: SocialPlatform;
  name: string;
}

export interface LocationItem {
  name: string;
  latitude: number | null;
  longitude: number | null;
}

/**
 * A file of the Media Library on a post, with what the library says about it, so the editor can show it without a second call. The
 * bytes are at `mediaFileUrl(assetId)`. A file the library could not describe has empty text and a size of 0.
 */
export interface PostMediaItem {
  assetId: string;
  role: MediaRole;
  displayOrder: number;
  caption: string;
  fileName: string;
  mediaType: string;
  mimeType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
}

/** A post as the editor needs it: everything. */
export interface Post {
  id: string;
  workspaceId: string;
  campaignId: string | null;
  contentType: ContentType;
  title: string;
  bodyDoc: RichDoc | null;
  bodyText: string;
  excerpt: string;
  status: ContentStatus;
  visibility: Visibility;
  languageCode: string;
  timezone: string;
  scheduledAt: string | null;
  publishedAt: string | null;
  archivedAt: string | null;
  seoTitle: string | null;
  seoDescription: string | null;
  seoSlug: string | null;
  seoKeywords: string[];
  hashtags: string[];
  tags: TagItem[];
  platforms: PostPlatformItem[];
  poll: PollItem | null;
  ctas: CtaItem[];
  mentions: MentionItem[];
  location: LocationItem | null;
  media: PostMediaItem[];
  settings: Record<string, unknown>;
  createdBy: string | null;
  updatedBy: string | null;
  createdAt: string;
  updatedAt: string;
}

/** One row of the list. */
export interface PostListItem {
  id: string;
  workspaceId: string;
  contentType: ContentType;
  title: string;
  excerpt: string;
  status: ContentStatus;
  visibility: Visibility;
  platforms: Array<{ platform: SocialPlatform; publishStatus: string }>;
  tags: TagItem[];
  scheduledAt: string | null;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

/** A page of the list. `typeCounts` has a count for every type and `all`, under every filter except the type. */
export interface PostPage {
  items: PostListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  typeCounts: Record<string, number>;
}

export interface PlatformRule {
  platform: SocialPlatform;
  name: string;
  /** Whether the Site admin has enabled the platform for this organization. */
  enabled: boolean;
  captionLimit: number;
  titleLimit: number | null;
  maxHashtags: number | null;
}

/** What is real in this installation. A panel whose part is false is shown switched off. */
export interface Capabilities {
  media: boolean;
  ai: boolean;
  campaigns: boolean;
  publishing: 'none' | 'fake' | 'live';
}

export interface Template {
  id: string;
  workspaceId: string;
  name: string;
  description: string;
  contentType: ContentType;
  title: string;
  bodyDoc: RichDoc | null;
  bodyText: string;
  hashtags: string[];
  createdAt: string;
  updatedAt: string;
}

export interface HashtagSuggestion {
  hashtag: string;
  uses: number;
}

// --- What the page sends --------------------------------------------------------------------------------------------

export interface SavePostBody {
  /** Required when creating; ignored when editing. */
  workspaceId?: string;
  contentType: ContentType;
  title: string;
  bodyDoc: RichDoc | null;
  visibility: Visibility;
  languageCode: string;
  timezone: string;
  scheduledAt: string | null;
  campaignId: string | null;
  seoTitle: string | null;
  seoDescription: string | null;
  seoSlug: string | null;
  seoKeywords: string[];
  hashtags: string[];
  tags: string[];
  platforms: Array<{ platform: SocialPlatform; caption: string | null; title: string | null; settings: Record<string, unknown> }>;
  poll: { question: string; options: string[]; endsAt: string | null } | null;
  ctas: CtaItem[];
  mentions: MentionItem[];
  location: { name: string; latitude: number | null; longitude: number | null } | null;
  /** The files on the post, in order. The whole list on every save: what it leaves out is taken off the post. */
  media: Array<{ assetId: string; caption: string }>;
  settings: Record<string, unknown>;
  /** The `updatedAt` the page loaded. Required when editing: if someone saved since, the answer is 409 `content_changed`. */
  expectedUpdatedAt?: string;
}

export interface SaveTemplateBody {
  workspaceId: string;
  name: string;
  description: string;
  contentType: ContentType;
  title: string;
  bodyDoc: RichDoc | null;
  hashtags: string[];
}

/** The list page's filters, as they go into the query string. Empty values are left out. */
export interface PostListParams {
  workspaceId?: string;
  type?: string;
  status?: string;
  platform?: string;
  from?: string;
  to?: string;
  q?: string;
  page?: number;
  pageSize?: number;
}

/** `?a=1&b=2` from the values that are set, in a fixed order, or an empty string. */
export function queryString(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && String(value).length > 0) {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text.length === 0 ? '' : `?${text}`;
}

const posts = '/v1/content/posts';

/** One function per endpoint of /v1/content. Made from the API client the session gave the page. */
export function contentApi(api: ApiClient) {
  return {
    list: (params: PostListParams = {}) => api.request<PostPage>(`${posts}${queryString({ ...params })}`),
    get: (id: string) => api.request<Post>(`${posts}/${id}`),
    create: (body: SavePostBody) => api.request<Post>(posts, { method: 'POST', body }),
    update: (id: string, body: SavePostBody) => api.request<Post>(`${posts}/${id}`, { method: 'PUT', body }),
    remove: (id: string) => api.request<null>(`${posts}/${id}`, { method: 'DELETE' }),
    duplicate: (id: string) => api.request<Post>(`${posts}/${id}/duplicate`, { method: 'POST' }),
    archive: (id: string) => api.request<Post>(`${posts}/${id}/archive`, { method: 'POST' }),
    restore: (id: string) => api.request<Post>(`${posts}/${id}/restore`, { method: 'POST' }),
    schedule: (id: string, scheduledAt: string) => api.request<Post>(`${posts}/${id}/schedule`, { method: 'POST', body: { scheduledAt } }),
    unschedule: (id: string) => api.request<Post>(`${posts}/${id}/unschedule`, { method: 'POST' }),
    publishNow: (id: string) => api.request<Post>(`${posts}/${id}/publish-now`, { method: 'POST' }),
    capabilities: () => api.request<Capabilities>('/v1/content/capabilities'),
    platformRules: () => api.request<PlatformRule[]>('/v1/content/platform-rules'),
    templates: (params: { workspaceId?: string; type?: string } = {}) =>
      api.request<Template[]>(`/v1/content/templates${queryString({ ...params })}`),
    createTemplate: (body: SaveTemplateBody) => api.request<Template>('/v1/content/templates', { method: 'POST', body }),
    deleteTemplate: (id: string) => api.request<null>(`/v1/content/templates/${id}`, { method: 'DELETE' }),
    tags: (workspaceId?: string) => api.request<TagItem[]>(`/v1/content/tags${queryString({ workspaceId })}`),
    hashtagSuggestions: (params: { workspaceId?: string; q?: string; limit?: number }) =>
      api.request<HashtagSuggestion[]>(`/v1/content/hashtags/suggestions${queryString({ ...params })}`),
  };
}

export type ContentApi = ReturnType<typeof contentApi>;
