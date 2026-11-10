import { describe, expect, it } from 'vitest';
import { CONTENT_STATUSES, CONTENT_TYPES, MEDIA_ROLES, SOCIAL_PLATFORMS, VISIBILITIES, type ContentStatus, type PostMediaItem, type RichDoc } from '@/api/content';
import { contentTypeLabel, isContentType, platformName, rangeStart, statusDetail, statusLabel, statusTone } from '@/lib/content';
import { isCurrentPath } from '@/navigation/menu';
import { platformRules, post, target } from '@/test/content';
import { readRepositoryFile } from '@/test/repo';
import { PAGE_SIZE, filtersFromSearch, isNarrowed, listParams, noFilters, searchFromFilters } from './listFilters';
import { earliestLocalInput, fromLocalInput, toLocalInput } from './localTime';
import {
  MAX_MEDIA,
  POST_MEDIA_TYPES,
  addMedia,
  draftFromPost,
  draftsEqual,
  hasContent,
  isWebAddress,
  moveMedia,
  newDraft,
  removeMedia,
  publishProblems,
  textFor,
  titleFor,
  toSaveBody,
  togglePlatform,
  type PostDraft,
} from './postDraft';
import { availableActions, isEditable } from './rowActionRules';
import {
  MARK_TYPES,
  MAX_HASHTAGS,
  NODE_TYPES,
  countedLength,
  docFromText,
  emptyDoc,
  isDocEmpty,
  isSafeLink,
  normalizeHashtag,
  plainText,
  withHashtag,
} from './richText/doc';

/** The values listed in one of the server's enums, in order: [DbValue("x")]. */
const dbValues = (file: string): string[] => [...readRepositoryFile(`src/Platform.Content/Enums/${file}`).matchAll(/\[DbValue\("([a-z_]+)"\)\]/g)].map((match) => match[1] ?? '');

describe('the lists in the page match the server', () => {
  it('content types', () => {
    expect(CONTENT_TYPES.map((type) => type.value)).toEqual(dbValues('ContentType.cs'));
  });
  it('statuses', () => {
    expect([...CONTENT_STATUSES]).toEqual(dbValues('ContentStatus.cs'));
  });
  it('visibilities', () => {
    expect([...VISIBILITIES]).toEqual(dbValues('ContentVisibility.cs'));
  });
  it('platforms', () => {
    expect([...SOCIAL_PLATFORMS]).toEqual(dbValues('SocialPlatform.cs'));
  });
  it('media roles', () => {
    expect([...MEDIA_ROLES]).toEqual(dbValues('MediaRole.cs'));
  });
  it('the most files on a post, and the kinds of file a post can carry', () => {
    const policy = readRepositoryFile('src/Platform.Content/Features/Media/MediaPolicy.cs');
    expect(MAX_MEDIA).toBe(Number(/MaxMedia\s*=\s*(\d+);/.exec(policy)?.[1]));
    expect(POST_MEDIA_TYPES).toEqual(['image', 'video']);
    expect(policy).toMatch(/Image\s*=\s*"image"/);
    expect(policy).toMatch(/Video\s*=\s*"video"/);
  });
  it('the kinds of node and mark the editor document may hold', () => {
    const source = readRepositoryFile('src/Platform.Content/Features/Posts/RichTextDocument.cs');
    const nodeTypes = [...source.matchAll(/^\s*\["([A-Za-z]+)"\]\s*=/gm)].map((match) => match[1] ?? '');
    expect(nodeTypes.length).toBeGreaterThan(0);
    expect([...NODE_TYPES].sort()).toEqual([...nodeTypes].sort());
    expect([...MARK_TYPES]).toEqual(/MarkTypes\s*=\s*\[([^\]]+)\]/.exec(source)?.[1]?.match(/"([A-Za-z]+)"/g)?.map((quoted) => quoted.replaceAll('"', '')));
  });
  it('the limits of every platform', () => {
    const source = readRepositoryFile('src/Platform.Content/Features/Publishing/PlatformRules.cs');
    const rows = [...source.matchAll(/new\(SocialPlatform\.(\w+), "([^"]+)", (\d+), (null|\d+), (null|\d+)\)/g)];
    expect(rows).toHaveLength(SOCIAL_PLATFORMS.length);
    for (const [, , name, caption, title, hashtags] of rows) {
      const rule = platformRules.find((entry) => entry.name === name);
      expect(rule, name).toBeDefined();
      expect(rule?.captionLimit, name).toBe(Number(caption));
      expect(rule?.titleLimit, name).toBe(title === 'null' ? null : Number(title));
      expect(rule?.maxHashtags, name).toBe(hashtags === 'null' ? null : Number(hashtags));
      expect(platformName(rule?.platform ?? ''), name).toBe(name);
    }
  });
  it('which statuses can be edited', () => {
    const source = readRepositoryFile('src/Platform.Content/Features/Scheduling/ContentStatusMachine.cs');
    const editable = /IsEditable[^;]*?=>([^;]+);/s.exec(source)?.[1] ?? '';
    for (const status of CONTENT_STATUSES) {
      const name = status.charAt(0).toUpperCase() + status.slice(1);
      expect(isEditable(status), status).toBe(editable.includes(`ContentStatus.${name}`));
    }
  });
});

describe('words', () => {
  it('labels and tones the known statuses and shows an unknown one as given', () => {
    expect(statusLabel('scheduled')).toBe('Scheduled');
    expect(statusLabel('on_hold')).toBe('On_hold');
    expect(statusTone('failed')).toBe('danger');
    expect(statusTone('draft')).toBe('muted');
    expect(statusTone('nonsense')).toBe('muted');
  });
  it('every status has its own label', () => {
    expect(new Set(CONTENT_STATUSES.map((status) => statusLabel(status))).size).toBe(CONTENT_STATUSES.length);
  });
  it('content types and platforms', () => {
    expect(contentTypeLabel('short_reel')).toBe('Shorts / Reels');
    expect(contentTypeLabel('podcast')).toBe('podcast');
    expect(isContentType('blog_post')).toBe(true);
    expect(isContentType('podcast')).toBe(false);
    expect(isContentType(null)).toBe(false);
    expect(platformName('youtube')).toBe('YouTube');
    expect(platformName('myspace')).toBe('myspace');
  });
  it('says when a scheduled or published post goes out, and nothing for the others', () => {
    expect(statusDetail({ status: 'scheduled', scheduledAt: '2026-11-10T09:00:00Z', publishedAt: null })).toMatch(/^for /);
    expect(statusDetail({ status: 'published', scheduledAt: null, publishedAt: '2026-11-10T09:00:00Z' })).not.toBeNull();
    expect(statusDetail({ status: 'draft', scheduledAt: null, publishedAt: null })).toBeNull();
    expect(statusDetail({ status: 'scheduled', scheduledAt: null, publishedAt: null })).toBeNull();
  });
  it('turns a date range into the start of it', () => {
    const now = new Date('2026-11-08T12:00:00Z');
    expect(rangeStart('7', now)).toBe('2026-11-01T12:00:00.000Z');
    expect(rangeStart('', now)).toBeUndefined();
    expect(rangeStart('999', now)).toBeUndefined();
    expect(rangeStart(null, now)).toBeUndefined();
  });
});

describe('list filters in the address', () => {
  it('read what the address holds and ignore what the list does not know', () => {
    const filters = filtersFromSearch(
      new URLSearchParams('type=blog_post&status=draft&platform=x&range=30&q=dream&page=3&workspace=0f8fad5b-d9cb-469f-a165-70867728950e'),
    );
    expect(filters).toEqual({ type: 'blog_post', status: 'draft', platform: 'x', range: '30', q: 'dream', workspaceId: '0f8fad5b-d9cb-469f-a165-70867728950e', page: 3 });

    expect(filtersFromSearch(new URLSearchParams('type=podcast&status=zzz&platform=myspace&range=5&page=-2&workspace=<script>'))).toEqual(noFilters);
    expect(filtersFromSearch(new URLSearchParams('page=abc')).page).toBe(1);
    expect(filtersFromSearch(new URLSearchParams(`q=${'a'.repeat(500)}`)).q).toHaveLength(200);
  });
  it('write only what is set, and round-trip', () => {
    expect(searchFromFilters(noFilters).toString()).toBe('');
    const filters = { ...noFilters, type: 'carousel', status: 'scheduled', q: ' hello ', page: 2 };
    const search = searchFromFilters(filters);
    expect(search.toString()).toBe('type=carousel&status=scheduled&q=hello&page=2');
    expect(filtersFromSearch(search)).toEqual({ ...filters, q: 'hello' });
  });
  it('knows what Reset would undo, which is everything but the tab', () => {
    expect(isNarrowed(noFilters)).toBe(false);
    expect(isNarrowed({ ...noFilters, type: 'blog_post' })).toBe(false);
    expect(isNarrowed({ ...noFilters, q: '   ' })).toBe(false);
    expect(isNarrowed({ ...noFilters, status: 'draft' })).toBe(true);
    expect(isNarrowed({ ...noFilters, q: 'x' })).toBe(true);
  });
  it('sends only what is set to the server', () => {
    expect(listParams(noFilters)).toEqual({
      workspaceId: undefined,
      type: undefined,
      status: undefined,
      platform: undefined,
      from: undefined,
      q: undefined,
      page: 1,
      pageSize: PAGE_SIZE,
    });
    const params = listParams({ ...noFilters, range: '7', q: ' hi ', page: 2 }, new Date('2026-11-08T12:00:00Z'));
    expect(params).toMatchObject({ from: '2026-11-01T12:00:00.000Z', q: 'hi', page: 2 });
  });
});

describe('what a row offers', () => {
  const writer = { canCreate: true, canPublish: false };
  const publisher = { canCreate: true, canPublish: true };
  const reader = { canCreate: false, canPublish: false };

  it('lets a writer edit, copy and delete a draft, and nothing that publishes', () => {
    expect(availableActions('draft', writer)).toEqual(['edit', 'duplicate', 'delete']);
    expect(availableActions('scheduled', writer)).toEqual(['edit', 'duplicate']);
    expect(availableActions('failed', writer)).toEqual(['edit', 'duplicate']);
    expect(availableActions('archived', writer)).toEqual(['open', 'duplicate']);
  });
  it('lets a publisher unschedule, archive, restore and delete what went through publishing', () => {
    expect(availableActions('draft', publisher)).toEqual(['edit', 'duplicate', 'archive', 'delete']);
    expect(availableActions('scheduled', publisher)).toEqual(['edit', 'duplicate', 'unschedule']);
    expect(availableActions('published', publisher)).toEqual(['open', 'duplicate', 'archive']);
    expect(availableActions('failed', publisher)).toEqual(['edit', 'duplicate', 'archive', 'delete']);
    expect(availableActions('archived', publisher)).toEqual(['open', 'duplicate', 'restore', 'delete']);
    expect(availableActions('publishing', publisher)).toEqual(['open', 'duplicate']);
  });
  it('lets a reader only open', () => {
    for (const status of CONTENT_STATUSES) {
      expect(availableActions(status, reader), status).toEqual(['open']);
    }
  });
  it('never offers to delete a scheduled, publishing or published post', () => {
    for (const status of ['scheduled', 'publishing', 'published'] as ContentStatus[]) {
      expect(availableActions(status, publisher), status).not.toContain('delete');
    }
  });
});

describe('the editor document', () => {
  const doc: RichDoc = {
    type: 'doc',
    content: [
      { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'Hello' }] },
      { type: 'paragraph', content: [{ type: 'text', text: 'one', marks: [{ type: 'bold' }] }, { type: 'hardBreak' }, { type: 'text', text: 'two' }] },
      { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'a' }] }] }, { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'b' }] }] }] },
      { type: 'orderedList', attrs: { start: 3 }, content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'c' }] }] }] },
      { type: 'blockquote', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'quoted' }] }] },
    ],
  };

  it('has a plain text with blocks apart, lists as lines and breaks as line breaks', () => {
    expect(plainText(doc)).toBe('Hello\n\none\ntwo\n\n• a\n• b\n\n3. c\n\nquoted');
  });
  it('is empty with no text or only blank paragraphs', () => {
    expect(isDocEmpty(emptyDoc())).toBe(true);
    expect(isDocEmpty(null)).toBe(true);
    expect(isDocEmpty(docFromText('   '))).toBe(true);
    expect(isDocEmpty(docFromText('x'))).toBe(false);
  });
  it('makes paragraphs from plain text', () => {
    const made = docFromText('first\nsecond\n\nthird');
    expect(made.content).toHaveLength(2);
    expect(plainText(made)).toBe('first\nsecond\n\nthird');
    expect(docFromText('').content).toEqual([{ type: 'paragraph' }]);
  });
  it('allows only web and mail links', () => {
    expect(isSafeLink('https://example.com/a?b=1')).toBe(true);
    expect(isSafeLink('http://example.com')).toBe(true);
    expect(isSafeLink('mailto:a@example.com')).toBe(true);
    for (const bad of ['javascript:alert(1)', 'JaVaScRiPt:alert(1)', 'data:text/html,hi', 'ftp://example.com', '//example.com', 'example.com', 'https://exa mple.com', 'https://example.com/\u0000', '', null, undefined, `https://example.com/${'a'.repeat(2100)}`]) {
      expect(isSafeLink(bad), String(bad)).toBe(false);
    }
  });
  it('normalises hashtags and refuses what is not one', () => {
    expect(normalizeHashtag(' ##Motivation ')).toBe('Motivation');
    expect(normalizeHashtag('#')).toBeNull();
    expect(normalizeHashtag('two words')).toBeNull();
    expect(normalizeHashtag('a#b')).toBeNull();
    expect(normalizeHashtag('a'.repeat(101))).toBeNull();
  });
  it('adds a hashtag once, whatever the letter case, up to the limit', () => {
    const one = withHashtag([], '#Fun');
    expect(one).toEqual(['Fun']);
    expect(withHashtag(one, 'fun')).toBe(one);
    expect(withHashtag(one, 'two words')).toBe(one);
    const full = Array.from({ length: MAX_HASHTAGS }, (_, i) => `t${i}`);
    expect(withHashtag(full, 'extra')).toBe(full);
  });
  it('counts the text and the hashtags as a platform does', () => {
    expect(countedLength('abc', [])).toBe(3);
    // "abc" + blank line + "#a #bb": 3 + 2 + (2 + 3 + 1) = 11.
    expect(countedLength('abc', ['a', 'bb'])).toBe(11);
  });
});

describe('local times', () => {
  it('turn an instant into a datetime-local value and back', () => {
    const local = toLocalInput('2026-11-10T09:30:00Z');
    expect(local).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
    expect(fromLocalInput(local)).toBe('2026-11-10T09:30:00.000Z');
  });
  it('treat nothing and nonsense as empty', () => {
    expect(toLocalInput(null)).toBe('');
    expect(toLocalInput('not a date')).toBe('');
    expect(fromLocalInput('')).toBeNull();
    expect(fromLocalInput('2026-13-45T99:99')).toBeNull();
    expect(fromLocalInput('2026-11-10')).toBeNull();
  });
  it('the earliest schedule is ahead of now', () => {
    const now = new Date('2026-11-08T12:00:00Z');
    const earliest = fromLocalInput(earliestLocalInput(now, 5));
    expect(new Date(earliest ?? '').getTime() - now.getTime()).toBe(5 * 60 * 1000);
  });
});

describe('the draft of a post', () => {
  const base = (): PostDraft => ({ ...newDraft({ contentType: 'social_post', workspaceId: 'w1' }), bodyDoc: docFromText('Hello world') });
  const withPlatform = (draft: PostDraft, platform: 'instagram' | 'x' | 'youtube' | 'facebook') => ({ ...draft, platforms: togglePlatform(draft.platforms, platform) });

  it('starts empty, with the type and workspace given', () => {
    const draft = newDraft({ contentType: 'blog_post', workspaceId: 'w9' });
    expect(draft).toMatchObject({ contentType: 'blog_post', workspaceId: 'w9', title: '', visibility: 'public' });
    expect(hasContent(draft)).toBe(false);
    expect(hasContent({ ...draft, title: ' t ' })).toBe(true);
    expect(hasContent({ ...draft, bodyDoc: docFromText('x') })).toBe(true);
  });

  it('a new post is sent with its workspace and no version; an edit with its version and no workspace', () => {
    const created = toSaveBody(base(), { timezone: 'Asia/Kolkata' });
    expect(created).toMatchObject({ workspaceId: 'w1', timezone: 'Asia/Kolkata' });
    expect(created).not.toHaveProperty('expectedUpdatedAt');

    const edited = toSaveBody(base(), { timezone: 'UTC', expectedUpdatedAt: '2026-11-08T10:00:00Z' });
    expect(edited).toMatchObject({ expectedUpdatedAt: '2026-11-08T10:00:00Z' });
    expect(edited).not.toHaveProperty('workspaceId');
  });

  it('sends nothing for the rows nobody filled in', () => {
    const draft: PostDraft = {
      ...base(),
      title: '  Title  ',
      bodyDoc: emptyDoc(),
      poll: { question: '  ', options: ['', ' '], endsAt: null },
      ctas: [{ buttonText: '', targetUrl: '' }],
      location: { name: '  ', latitude: null, longitude: null },
      platforms: [{ platform: 'x', caption: '   ', title: ' ', settings: {} }],
    };
    const body = toSaveBody(draft, { timezone: 'UTC' });
    expect(body.title).toBe('Title');
    expect(body.bodyDoc).toBeNull();
    expect(body.poll).toBeNull();
    expect(body.ctas).toEqual([]);
    expect(body.location).toBeNull();
    expect(body.platforms).toEqual([{ platform: 'x', caption: null, title: null, settings: {} }]);
  });

  it('keeps and trims what was filled in', () => {
    const draft: PostDraft = {
      ...base(),
      poll: { question: ' Which? ', options: [' a ', '', 'b'], endsAt: null },
      ctas: [{ buttonText: ' Buy ', targetUrl: ' https://example.com ' }],
      mentions: [{ platform: 'instagram', name: ' ana ' }],
      location: { name: ' Chennai ', latitude: 13.08, longitude: 80.27 },
    };
    const body = toSaveBody(draft, { timezone: 'UTC' });
    expect(body.poll).toEqual({ question: 'Which?', options: ['a', 'b'], endsAt: null });
    expect(body.ctas).toEqual([{ buttonText: 'Buy', targetUrl: 'https://example.com' }]);
    expect(body.mentions).toEqual([{ platform: 'instagram', name: 'ana' }]);
    expect(body.location).toEqual({ name: 'Chennai', latitude: 13.08, longitude: 80.27 });
  });

  it('editing a post returns what the page does not show unchanged', () => {
    const source = post('p1', { languageCode: 'ta', seoTitle: 'SEO', seoKeywords: ['a'], campaignId: 'c1', settings: { pinned: true } });
    const body = toSaveBody(draftFromPost(source), { timezone: 'UTC', expectedUpdatedAt: source.updatedAt });
    expect(body).toMatchObject({ languageCode: 'ta', seoTitle: 'SEO', seoKeywords: ['a'], campaignId: 'c1', settings: { pinned: true } });
  });

  it('knows when a draft is unchanged', () => {
    const source = post('p1');
    expect(draftsEqual(draftFromPost(source), draftFromPost(source))).toBe(true);
    expect(draftsEqual(draftFromPost(source), { ...draftFromPost(source), title: 'other' })).toBe(false);
  });

  it('toggles a platform on and off', () => {
    const on = togglePlatform([], 'x');
    expect(on).toEqual([{ platform: 'x', caption: '', title: '', settings: {} }]);
    expect(togglePlatform(on, 'x')).toEqual([]);
  });

  it('uses the platform\'s own text and title, or the post\'s', () => {
    const draft = { ...base(), title: 'Post title' };
    expect(textFor(draft, { platform: 'x', caption: '', title: '', settings: {} })).toBe('Hello world');
    expect(textFor(draft, { platform: 'x', caption: ' Own ', title: '', settings: {} })).toBe('Own');
    expect(titleFor(draft, { platform: 'x', caption: '', title: '', settings: {} })).toBe('Post title');
    expect(titleFor(draft, { platform: 'x', caption: '', title: 'Own', settings: {} })).toBe('Own');
  });

  it('accepts only web addresses for buttons', () => {
    expect(isWebAddress('https://example.com')).toBe(true);
    for (const bad of ['', 'example.com', 'ftp://example.com', 'javascript:alert(1)', 'https://a b.com', `https://x.com/${'a'.repeat(2100)}`]) {
      expect(isWebAddress(bad), bad).toBe(false);
    }
  });

  describe('is fit to go out', () => {
    it('with text and a platform', () => {
      expect(publishProblems(withPlatform(base(), 'instagram'), platformRules)).toEqual([]);
    });
    it('not without text, or a platform', () => {
      expect(publishProblems({ ...base(), bodyDoc: emptyDoc() }, platformRules).map((p) => p.message)).toContain('Write some text before scheduling.');
      expect(publishProblems(base(), platformRules).map((p) => p.message)).toContain('Choose at least one platform.');
    });
    it('a blog post and a script need a title', () => {
      for (const contentType of ['blog_post', 'video_script'] as const) {
        const problems = publishProblems(withPlatform({ ...base(), contentType }, 'youtube'), platformRules);
        expect(problems.map((p) => p.message), contentType).toContain('Give it a title before scheduling.');
      }
    });
    it('not on a platform the organization has not enabled', () => {
      const problems = publishProblems(withPlatform(base(), 'facebook'), platformRules);
      expect(problems).toEqual([{ step: 'create', message: 'Facebook is not enabled for your organization.' }]);
    });
    it('not with more text than the platform takes, hashtags counted, and it points at the Customize step', () => {
      const draft = { ...withPlatform(base(), 'x'), bodyDoc: docFromText('a'.repeat(275)), hashtags: ['abc'] };
      // 275 + blank line + "#abc" = 281.
      expect(publishProblems(draft, platformRules)).toEqual([{ step: 'customize', message: 'X: the text is 281 characters; the limit is 280.' }]);
      // The platform's own shorter text fits.
      const own = { ...draft, platforms: [{ platform: 'x' as const, caption: 'short', title: '', settings: {} }] };
      expect(publishProblems(own, platformRules)).toEqual([]);
    });
    it('not with a title longer than the platform takes', () => {
      const draft = { ...withPlatform(base(), 'youtube'), title: 'y'.repeat(101) };
      expect(publishProblems(draft, platformRules).map((p) => p.message)).toEqual(['YouTube: the title is 101 characters; the limit is 100.']);
    });
    it('not with too many hashtags for the platform', () => {
      const draft = { ...withPlatform(base(), 'instagram'), hashtags: Array.from({ length: 31 }, (_, i) => `t${i}`) };
      const messages = publishProblems(draft, platformRules).map((p) => p.message);
      expect(messages).toContain('A post can have at most 30 hashtags.');
    });
    it('ignores a poll and a button nobody filled in, and checks the ones somebody did', () => {
      const blank = { ...withPlatform(base(), 'instagram'), poll: { question: '', options: ['', ''], endsAt: null }, ctas: [{ buttonText: '', targetUrl: '' }] };
      expect(publishProblems(blank, platformRules)).toEqual([]);

      const half = { ...blank, poll: { question: 'Which?', options: ['only one', ''], endsAt: null }, ctas: [{ buttonText: 'Buy', targetUrl: 'nope' }] };
      expect(publishProblems(half, platformRules).map((p) => p.message)).toEqual([
        'A poll needs a question and at least two options.',
        'A button needs text and a web address starting with http:// or https://.',
      ]);
    });
  });
});

describe('the files on a draft', () => {
  const file = (id: string, overrides: Partial<PostMediaItem> = {}): PostMediaItem => ({
    assetId: id, role: 'attachment', displayOrder: 0, caption: '', fileName: `${id}.png`, mediaType: 'image', mimeType: 'image/png', sizeBytes: 1000, width: 10, height: 20, ...overrides,
  });
  const entry = (id: string) => ({ assetId: id, fileName: `${id}.png`, mediaType: 'image', mimeType: 'image/png', sizeBytes: 1000, width: null, height: null });

  it('start empty, and come back from a post in display order whatever order the server listed them in', () => {
    expect(newDraft({ contentType: 'social_post', workspaceId: 'w1' }).media).toEqual([]);

    const draft = draftFromPost(post('p1', { media: [file('b', { displayOrder: 1 }), file('a', { displayOrder: 0 }), file('c', { displayOrder: 2 })] }));

    expect(draft.media.map((m) => m.assetId)).toEqual(['a', 'b', 'c']);
  });

  it('go to the server as ids and captions in order, and nothing the page only shows', () => {
    const draft = { ...newDraft({ contentType: 'carousel', workspaceId: 'w1' }), media: [{ ...entry('a'), caption: 'First' }, { ...entry('b'), caption: '' }] };

    const body = toSaveBody(draft, { timezone: 'UTC' });

    expect(body.media).toEqual([{ assetId: 'a', caption: 'First' }, { assetId: 'b', caption: '' }]);
    expect(JSON.stringify(body.media)).not.toContain('fileName');
  });

  it('are sent as an empty list when there are none, so an edit takes them off the post', () => {
    expect(toSaveBody(newDraft({ contentType: 'social_post', workspaceId: 'w1' }), { timezone: 'UTC', expectedUpdatedAt: '2026-11-08T10:00:00Z' }).media).toEqual([]);
  });

  it('round trip: what a post had comes back unchanged in a save', () => {
    const source = post('p1', { media: [file('a', { caption: 'Hello' }), file('b', { displayOrder: 1 })] });

    const body = toSaveBody(draftFromPost(source), { timezone: 'UTC', expectedUpdatedAt: source.updatedAt });

    expect(body.media).toEqual([{ assetId: 'a', caption: 'Hello' }, { assetId: 'b', caption: '' }]);
  });

  it('count as a change of the draft, in any way: adding, removing and moving', () => {
    const base = draftFromPost(post('p1', { media: [file('a'), file('b', { displayOrder: 1 })] }));

    expect(draftsEqual(base, { ...base })).toBe(true);
    expect(draftsEqual(base, { ...base, media: addMedia(base.media, [entry('c')]) })).toBe(false);
    expect(draftsEqual(base, { ...base, media: removeMedia(base.media, 'a') })).toBe(false);
    expect(draftsEqual(base, { ...base, media: moveMedia(base.media, 'a', 1) })).toBe(false);
  });

  describe('adding', () => {
    it('puts new files on the end, in the order given, with an empty caption', () => {
      const list = addMedia([], [entry('a'), entry('b')]);

      expect(list.map((m) => m.assetId)).toEqual(['a', 'b']);
      expect(list.every((m) => m.caption === '')).toBe(true);
    });

    it('skips a file that is already there, and returns the same list when nothing was added', () => {
      const list = addMedia([], [entry('a')]);

      expect(addMedia(list, [entry('a')])).toBe(list);
      expect(addMedia(list, [])).toBe(list);
    });

    it('stops at the limit', () => {
      const full = addMedia([], Array.from({ length: MAX_MEDIA + 3 }, (_, i) => entry(`f${i}`)));

      expect(full).toHaveLength(MAX_MEDIA);
      expect(addMedia(full, [entry('extra')])).toBe(full);
    });
  });

  describe('removing', () => {
    it('takes the file out and keeps the others in order', () => {
      expect(removeMedia(addMedia([], [entry('a'), entry('b'), entry('c')]), 'b').map((m) => m.assetId)).toEqual(['a', 'c']);
    });

    it('returns the same list for a file that is not there', () => {
      const list = addMedia([], [entry('a')]);

      expect(removeMedia(list, 'zzz')).toBe(list);
    });
  });

  describe('moving', () => {
    const three = addMedia([], [entry('a'), entry('b'), entry('c')]);

    it('moves a file one place earlier or later', () => {
      expect(moveMedia(three, 'c', -1).map((m) => m.assetId)).toEqual(['a', 'c', 'b']);
      expect(moveMedia(three, 'a', 1).map((m) => m.assetId)).toEqual(['b', 'a', 'c']);
    });

    it('returns the same list at the ends, and for a file that is not there', () => {
      expect(moveMedia(three, 'a', -1)).toBe(three);
      expect(moveMedia(three, 'c', 1)).toBe(three);
      expect(moveMedia(three, 'zzz', 1)).toBe(three);
    });

    it('never changes the list it was given', () => {
      const before = three.map((m) => m.assetId);

      moveMedia(three, 'b', 1);

      expect(three.map((m) => m.assetId)).toEqual(before);
    });
  });
});

describe('which menu item a page belongs to', () => {
  const items = ['/dashboard', '/content-studio', '/workspaces'];
  it('the item itself, and the pages below it', () => {
    expect(isCurrentPath('/content-studio', '/content-studio', items)).toBe(true);
    expect(isCurrentPath('/content-studio', '/content-studio/new', items)).toBe(true);
    expect(isCurrentPath('/content-studio', '/content-studio/0f8fad5b', items)).toBe(true);
  });
  it('not a different page that only starts with the same letters', () => {
    expect(isCurrentPath('/content-studio', '/content-studios', items)).toBe(false);
    expect(isCurrentPath('/dashboard', '/content-studio', items)).toBe(false);
  });
  it('a page below two items belongs to the closer one', () => {
    const nested = ['/admin', '/admin/users'];
    expect(isCurrentPath('/admin', '/admin/users', nested)).toBe(false);
    expect(isCurrentPath('/admin/users', '/admin/users', nested)).toBe(true);
    expect(isCurrentPath('/admin', '/admin/other', nested)).toBe(true);
  });
});

describe('the helper server for the tests', () => {
  it('targets have a pending status until publishing says otherwise', () => {
    expect(target('x').publishStatus).toBe('pending');
  });
});
