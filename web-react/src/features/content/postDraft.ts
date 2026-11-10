// What the Create/Edit page holds while a person works on a post, and how it goes to and from the API. Plain TypeScript, no React,
// so the rules are tested directly. The server checks everything again; the problems found here only decide what the page tells
// the person before they ask.

import type {
  ContentType,
  CtaItem,
  MentionItem,
  PlatformRule,
  Post,
  PostMediaItem,
  RichDoc,
  SavePostBody,
  SocialPlatform,
  Visibility,
} from '@/api/content';
import { platformName } from '@/lib/content';
import { MAX_HASHTAGS, countedLength, emptyDoc, isDocEmpty, plainText } from './richText/doc';

export interface DraftTarget {
  platform: SocialPlatform;
  /** The platform's own text; empty means "the post's own text". */
  caption: string;
  title: string;
  settings: Record<string, unknown>;
}

export interface DraftPoll {
  question: string;
  options: readonly string[];
  endsAt: string | null;
}

export interface DraftLocation {
  name: string;
  latitude: number | null;
  longitude: number | null;
}

/**
 * A file of the Media Library on the draft, with what the editor needs to show it. The caption has no editor yet; it is carried so a
 * save sends back what the post already had (an edit sends the whole post).
 */
export interface DraftMedia {
  assetId: string;
  caption: string;
  fileName: string;
  mediaType: string;
  mimeType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
}

/** Fields of a post the page does not show but must send back unchanged, so editing a post made elsewhere never erases them. */
export interface KeptFields {
  languageCode: string;
  campaignId: string | null;
  seoTitle: string | null;
  seoDescription: string | null;
  seoSlug: string | null;
  seoKeywords: readonly string[];
  settings: Record<string, unknown>;
}

export interface PostDraft {
  workspaceId: string;
  contentType: ContentType;
  title: string;
  bodyDoc: RichDoc;
  visibility: Visibility;
  /** When the post is meant to go out, as an instant; null while it has no date. Becomes the schedule only on "Schedule". */
  scheduledAt: string | null;
  hashtags: readonly string[];
  tags: readonly string[];
  platforms: readonly DraftTarget[];
  poll: DraftPoll | null;
  ctas: readonly CtaItem[];
  mentions: readonly MentionItem[];
  location: DraftLocation | null;
  /** The files of the library on the post, in the order they show. */
  media: readonly DraftMedia[];
  kept: KeptFields;
}

export const MAX_CTAS = 3;
export const MAX_MENTIONS = 20;
export const MAX_POLL_OPTIONS = 4;

/** The most files on one post. A test compares it with `MediaPolicy.MaxMedia` on the server. */
export const MAX_MEDIA = 10;
export const MIN_POLL_OPTIONS = 2;

export function newDraft({ contentType, workspaceId }: { contentType: ContentType; workspaceId: string }): PostDraft {
  return {
    workspaceId,
    contentType,
    title: '',
    bodyDoc: emptyDoc(),
    visibility: 'public',
    scheduledAt: null,
    hashtags: [],
    tags: [],
    platforms: [],
    poll: null,
    ctas: [],
    mentions: [],
    location: null,
    media: [],
    kept: { languageCode: 'en', campaignId: null, seoTitle: null, seoDescription: null, seoSlug: null, seoKeywords: [], settings: {} },
  };
}

export function draftFromPost(post: Post): PostDraft {
  return {
    workspaceId: post.workspaceId,
    contentType: post.contentType,
    title: post.title,
    bodyDoc: post.bodyDoc ?? emptyDoc(),
    visibility: post.visibility,
    scheduledAt: post.scheduledAt,
    hashtags: post.hashtags,
    tags: post.tags.map((tag) => tag.name),
    platforms: post.platforms.map((target) => ({
      platform: target.platform,
      caption: target.caption ?? '',
      title: target.title ?? '',
      settings: target.settings,
    })),
    poll: post.poll ? { question: post.poll.question, options: post.poll.options, endsAt: post.poll.endsAt } : null,
    ctas: post.ctas,
    mentions: post.mentions,
    location: post.location,
    media: [...post.media].sort((a, b) => a.displayOrder - b.displayOrder).map(draftMediaFrom),
    kept: {
      languageCode: post.languageCode,
      campaignId: post.campaignId,
      seoTitle: post.seoTitle,
      seoDescription: post.seoDescription,
      seoSlug: post.seoSlug,
      seoKeywords: post.seoKeywords,
      settings: post.settings,
    },
  };
}

/** What the post says about a file, as the draft holds it. */
export function draftMediaFrom(file: PostMediaItem): DraftMedia {
  return {
    assetId: file.assetId,
    caption: file.caption,
    fileName: file.fileName,
    mediaType: file.mediaType,
    mimeType: file.mimeType,
    sizeBytes: file.sizeBytes,
    width: file.width,
    height: file.height,
  };
}

/** The kinds of file a post can carry, as the library names them. */
export const POST_MEDIA_TYPES = ['image', 'video'] as const;

/**
 * Puts files on the end of the list, in the order given, skipping one that is already there and anything past the limit. The same list
 * when nothing was added, so a state setter given it does not redraw.
 */
export function addMedia(list: readonly DraftMedia[], files: readonly Omit<DraftMedia, 'caption'>[]): readonly DraftMedia[] {
  const next = [...list];
  for (const file of files) {
    if (next.length >= MAX_MEDIA) {
      break;
    }
    if (!next.some((existing) => existing.assetId === file.assetId)) {
      next.push({ ...file, caption: '' });
    }
  }
  return next.length === list.length ? list : next;
}

export function removeMedia(list: readonly DraftMedia[], assetId: string): readonly DraftMedia[] {
  return list.some((file) => file.assetId === assetId) ? list.filter((file) => file.assetId !== assetId) : list;
}

/** Moves a file one place earlier (`-1`) or later (`1`). The same list when it cannot move, which is how the first and last stop. */
export function moveMedia(list: readonly DraftMedia[], assetId: string, by: -1 | 1): readonly DraftMedia[] {
  const from = list.findIndex((file) => file.assetId === assetId);
  const to = from + by;
  if (from < 0 || to < 0 || to >= list.length) {
    return list;
  }
  const next = [...list];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved as DraftMedia);
  return next;
}

/** The body of a save. An edit sends the whole post (what it leaves out is cleared), plus the version it started from. */
export function toSaveBody(draft: PostDraft, { timezone, expectedUpdatedAt }: { timezone: string; expectedUpdatedAt?: string }): SavePostBody {
  const options = draft.poll ? draft.poll.options.map((option) => option.trim()).filter((option) => option.length > 0) : [];
  const question = draft.poll?.question.trim() ?? '';

  return {
    ...(expectedUpdatedAt === undefined ? { workspaceId: draft.workspaceId } : { expectedUpdatedAt }),
    contentType: draft.contentType,
    title: draft.title.trim(),
    bodyDoc: isDocEmpty(draft.bodyDoc) ? null : draft.bodyDoc,
    visibility: draft.visibility,
    languageCode: draft.kept.languageCode,
    timezone,
    scheduledAt: draft.scheduledAt,
    campaignId: draft.kept.campaignId,
    seoTitle: draft.kept.seoTitle,
    seoDescription: draft.kept.seoDescription,
    seoSlug: draft.kept.seoSlug,
    seoKeywords: [...draft.kept.seoKeywords],
    hashtags: [...draft.hashtags],
    tags: [...draft.tags],
    platforms: draft.platforms.map((target) => ({
      platform: target.platform,
      caption: target.caption.trim().length > 0 ? target.caption.trim() : null,
      title: target.title.trim().length > 0 ? target.title.trim() : null,
      settings: target.settings,
    })),
    poll: draft.poll && (question.length > 0 || options.length > 0) ? { question, options, endsAt: draft.poll.endsAt } : null,
    // A row nobody filled in is not a button: the panel starts with an empty one, and leaving it empty must not fail the save.
    ctas: draft.ctas
      .map((cta) => ({ buttonText: cta.buttonText.trim(), targetUrl: cta.targetUrl.trim() }))
      .filter((cta) => cta.buttonText.length > 0 || cta.targetUrl.length > 0),
    mentions: draft.mentions.map((mention) => ({ platform: mention.platform, name: mention.name.trim() })),
    location: draft.location && draft.location.name.trim().length > 0 ? { ...draft.location, name: draft.location.name.trim() } : null,
    media: draft.media.map((file) => ({ assetId: file.assetId, caption: file.caption })),
    settings: draft.kept.settings,
  };
}

/** Whether two drafts say the same: what decides whether there is anything unsaved. */
export function draftsEqual(a: PostDraft, b: PostDraft): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

/** Whether there is anything worth saving: a title or some text (the server asks the same). */
export function hasContent(draft: PostDraft): boolean {
  return draft.title.trim().length > 0 || !isDocEmpty(draft.bodyDoc);
}

/** An absolute http or https address, no spaces, at most 2048 characters. Same rule as the server's buttons. */
export function isWebAddress(url: string): boolean {
  if (url.length === 0 || url.length > 2048 || /\s/.test(url)) {
    return false;
  }
  try {
    const parsed = new URL(url);
    return (parsed.protocol === 'http:' || parsed.protocol === 'https:') && parsed.hostname.length > 0;
  } catch {
    return false;
  }
}

/** The text a platform will show: its own caption, or the post's. */
export function textFor(draft: PostDraft, target: DraftTarget): string {
  return target.caption.trim().length > 0 ? target.caption.trim() : plainText(draft.bodyDoc);
}

/** The title a platform will show: its own, or the post's, or none. */
export function titleFor(draft: PostDraft, target: DraftTarget): string {
  return target.title.trim().length > 0 ? target.title.trim() : draft.title.trim();
}

export interface Problem {
  /** Which step of the flow the person should go back to. */
  step: 'create' | 'customize';
  message: string;
}

/**
 * What is wrong with a post that is about to be scheduled or published, as the server will judge it (ScheduleReadiness, the
 * platform rules, the editor's own limits). The first problems a person would hit; the server's answer is the last word.
 */
export function publishProblems(draft: PostDraft, rules: readonly PlatformRule[]): Problem[] {
  const problems: Problem[] = [];
  const body = plainText(draft.bodyDoc);

  if (body.length === 0) {
    problems.push({ step: 'create', message: 'Write some text before scheduling.' });
  }
  if ((draft.contentType === 'blog_post' || draft.contentType === 'video_script') && draft.title.trim().length === 0) {
    problems.push({ step: 'create', message: 'Give it a title before scheduling.' });
  }
  if (draft.platforms.length === 0) {
    problems.push({ step: 'create', message: 'Choose at least one platform.' });
  }
  if (draft.hashtags.length > MAX_HASHTAGS) {
    problems.push({ step: 'create', message: `A post can have at most ${MAX_HASHTAGS} hashtags.` });
  }

  // A poll the panel started and nobody filled in is not a poll.
  const pollOptions = draft.poll ? draft.poll.options.map((option) => option.trim()).filter((option) => option.length > 0) : [];
  if (draft.poll && (draft.poll.question.trim().length > 0 || pollOptions.length > 0)) {
    const options = pollOptions;
    if (draft.poll.question.trim().length === 0 || options.length < MIN_POLL_OPTIONS) {
      problems.push({ step: 'create', message: 'A poll needs a question and at least two options.' });
    }
  }
  const buttons = draft.ctas.filter((cta) => cta.buttonText.trim().length > 0 || cta.targetUrl.trim().length > 0);
  if (buttons.some((cta) => cta.buttonText.trim().length === 0 || !isWebAddress(cta.targetUrl.trim()))) {
    problems.push({ step: 'create', message: 'A button needs text and a web address starting with http:// or https://.' });
  }

  for (const target of draft.platforms) {
    const rule = rules.find((entry) => entry.platform === target.platform);
    const name = platformName(target.platform);
    if (!rule) {
      continue;
    }
    if (!rule.enabled) {
      problems.push({ step: 'create', message: `${name} is not enabled for your organization.` });
      continue;
    }
    const length = countedLength(textFor(draft, target), draft.hashtags);
    if (length > rule.captionLimit) {
      problems.push({ step: 'customize', message: `${name}: the text is ${length} characters; the limit is ${rule.captionLimit}.` });
    }
    const title = titleFor(draft, target);
    if (rule.titleLimit !== null && title.length > rule.titleLimit) {
      problems.push({ step: 'customize', message: `${name}: the title is ${title.length} characters; the limit is ${rule.titleLimit}.` });
    }
    if (rule.maxHashtags !== null && draft.hashtags.length > rule.maxHashtags) {
      problems.push({ step: 'create', message: `${name}: at most ${rule.maxHashtags} hashtags.` });
    }
  }

  return problems;
}

/** Adds the platform to the draft, or takes it away if it is there. The same list when nothing changes. */
export function togglePlatform(platforms: readonly DraftTarget[], platform: SocialPlatform): readonly DraftTarget[] {
  return platforms.some((target) => target.platform === platform)
    ? platforms.filter((target) => target.platform !== platform)
    : [...platforms, { platform, caption: '', title: '', settings: {} }];
}
