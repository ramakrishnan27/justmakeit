// How Content Studio words things: statuses, kinds of content and platforms as a person reads them. Plain functions over
// the server's values, so the wording is tested without a browser.

import {
  CONTENT_TYPES,
  type ContentStatus,
  type ContentType,
  type PostListItem,
  type SocialPlatform,
} from '@/api/content';
import type { BadgeTone } from '@/components/ui/Badge';
import { formatDate } from '@/lib/format';

const statusLabels: Record<ContentStatus, string> = {
  draft: 'Draft',
  scheduled: 'Scheduled',
  publishing: 'Publishing',
  published: 'Published',
  failed: 'Failed',
  archived: 'Archived',
};

/** A status from the API as a label. A status this client does not know is shown as given, capitalised. */
export function statusLabel(status: string): string {
  return Object.hasOwn(statusLabels, status) ? statusLabels[status as ContentStatus] : status.charAt(0).toUpperCase() + status.slice(1);
}

const statusTones: Record<ContentStatus, BadgeTone> = {
  draft: 'muted',
  scheduled: 'trial',
  publishing: 'accent',
  published: 'paid',
  failed: 'danger',
  archived: 'model',
};

/** The badge colour of a status. The word is always shown as well, so colour is never the only signal. */
export function statusTone(status: string): BadgeTone {
  return Object.hasOwn(statusTones, status) ? statusTones[status as ContentStatus] : 'muted';
}

/** "Social Post" for social_post. Unknown values are shown as given. */
export function contentTypeLabel(type: string): string {
  return CONTENT_TYPES.find((entry) => entry.value === type)?.label ?? type;
}

export function isContentType(value: string | null | undefined): value is ContentType {
  return CONTENT_TYPES.some((entry) => entry.value === value);
}

const platformNames: Record<SocialPlatform, string> = {
  facebook: 'Facebook',
  instagram: 'Instagram',
  x: 'X',
  youtube: 'YouTube',
  linkedin: 'LinkedIn',
  tiktok: 'TikTok',
  pinterest: 'Pinterest',
  threads: 'Threads',
};

export function platformName(platform: string): string {
  return Object.hasOwn(platformNames, platform) ? platformNames[platform as SocialPlatform] : platform;
}

/**
 * The line under a listed post's status: when it goes out, when it went out, or nothing. Always the viewer's own time zone,
 * with the time, because "Nov 8" alone is not enough to know whether a scheduled post is later today.
 */
export function statusDetail(post: Pick<PostListItem, 'status' | 'scheduledAt' | 'publishedAt'>): string | null {
  if (post.status === 'scheduled' && post.scheduledAt) {
    return `for ${formatDate(post.scheduledAt, { withTime: true })}`;
  }
  if (post.status === 'published' && post.publishedAt) {
    return formatDate(post.publishedAt, { withTime: true });
  }
  return null;
}

/** The choices of the list's date filter: what they are called, and how many days back each one goes (null: any time). */
export const DATE_RANGES = [
  { value: '', label: 'Any time', days: null },
  { value: '7', label: 'Last 7 days', days: 7 },
  { value: '30', label: 'Last 30 days', days: 30 },
  { value: '90', label: 'Last 90 days', days: 90 },
] as const;

/** The start of a date range as the server wants it (an instant), or undefined for "any time" or a value it does not know. */
export function rangeStart(value: string | null | undefined, now: Date = new Date()): string | undefined {
  const range = DATE_RANGES.find((entry) => entry.value === value);
  return range?.days ? new Date(now.getTime() - range.days * 24 * 60 * 60 * 1000).toISOString() : undefined;
}

/** The statuses the list can be narrowed to, with their labels, in the order of a post's life. */
export const STATUS_FILTERS: ReadonlyArray<{ value: ContentStatus; label: string }> = (
  ['draft', 'scheduled', 'publishing', 'published', 'failed', 'archived'] as const
).map((value) => ({ value, label: statusLabel(value) }));
