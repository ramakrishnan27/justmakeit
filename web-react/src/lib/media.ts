// How the Media Library words things and checks files: sizes, durations, tags and the check of a file before it is sent. Plain functions
// over plain values, so they are tested without a browser. The server checks everything again; what is checked here only saves a trip.

import type { MediaAsset, MediaConfig, MediaType } from '@/api/media';
import { MEDIA_TYPES } from '@/api/media';

/** "812 B", "1.5 KB", "2.4 MB", "1.2 GB". One decimal from a kilobyte up, and none when it would be ".0". */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '–';
  }
  if (bytes < 1024) {
    return `${Math.round(bytes)} B`;
  }
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const text = value.toFixed(1);
  return `${text.endsWith('.0') ? text.slice(0, -2) : text} ${units[unit]}`;
}

/** "0:45", "1:20", "1:02:03". Nothing for a length that is not a positive number. */
export function formatDuration(seconds: number): string | null {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return null;
  }
  const total = Math.round(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = String(total % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${rest}` : `${minutes}:${rest}`;
}

/** "Image" for image. A type this client does not know is shown as given, capitalised. */
export function mediaTypeLabel(type: string): string {
  const known = MEDIA_TYPES.find((entry) => entry.value === type);
  return known ? known.label.replace(/s$/, '') : type.charAt(0).toUpperCase() + type.slice(1);
}

export function isMediaType(value: string | null | undefined): value is MediaType {
  return MEDIA_TYPES.some((entry) => entry.value === value);
}

/** ".png" for "Holiday.PNG"; empty when the name has no extension. */
export function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot > 0 && dot < fileName.length - 1 ? fileName.slice(dot).toLowerCase() : '';
}

/** "1920 × 1080" for an image whose size is known. */
export function dimensionsLabel(asset: Pick<MediaAsset, 'width' | 'height'>): string | null {
  return asset.width && asset.height ? `${asset.width} × ${asset.height}` : null;
}

export const MAX_TAGS = 10;
export const MAX_TAG_LENGTH = 50;

/**
 * The tags a person typed ("summer, #beach day"): split at commas, trimmed, without a leading #, repeats in any letter case left out.
 * `problem` is what to tell them when there are too many or one is too long; the names are still returned, so the field keeps its text.
 */
export function parseTags(text: string): { tags: string[]; problem: string | null } {
  const tags: string[] = [];
  const seen = new Set<string>();
  for (const part of text.split(',')) {
    const name = part.trim().replace(/^#+/, '').trim();
    if (name.length > 0 && !seen.has(name.toLowerCase())) {
      seen.add(name.toLowerCase());
      tags.push(name);
    }
  }
  if (tags.some((tag) => tag.length > MAX_TAG_LENGTH)) {
    return { tags, problem: `A tag is at most ${MAX_TAG_LENGTH} characters.` };
  }
  if (tags.length > MAX_TAGS) {
    return { tags, problem: `Use at most ${MAX_TAGS} tags.` };
  }
  return { tags, problem: null };
}

/** One of six colours for a tag, the same for the same name (without regard to letter case), so a tag looks the same everywhere. */
export function tagTone(name: string): number {
  let hash = 0;
  for (const character of name.toLowerCase()) {
    hash = (hash * 31 + (character.codePointAt(0) ?? 0)) >>> 0;
  }
  return hash % 6;
}

/**
 * What is wrong with a file that was chosen for upload, or null when it can be sent. Looks at the name's extension and the size only; the
 * server reads the file itself and decides what it really is.
 */
export function checkFile(file: { name: string; size: number }, config: Pick<MediaConfig, 'maxFileBytes' | 'extensions'>): string | null {
  if (file.size <= 0) {
    return 'This file is empty.';
  }
  if (file.size > config.maxFileBytes) {
    return `This file is larger than ${formatBytes(config.maxFileBytes)}.`;
  }
  if (!config.extensions.includes(extensionOf(file.name))) {
    return `This type of file is not supported. Use ${config.extensions.join(', ')}.`;
  }
  return null;
}
