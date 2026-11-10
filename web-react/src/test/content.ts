// A fake Content Studio server for the page tests: it keeps the posts in memory and answers the way the real one does (the list with
// its filters, paging and per-type counts; saving with the version check; the status changes), so the tests drive the page against
// behaviour, not against canned answers. Built on the member server of renderApp, with a Proxy for the addresses that carry a query
// string, which the fake fetch otherwise only matches exactly.

import type {
  Capabilities,
  MediaRole,
  ContentStatus,
  ContentType,
  PlatformRule,
  Post,
  PostListItem,
  PostMediaItem,
  PostPlatformItem,
  SocialPlatform,
  Template,
} from '@/api/content';
import { PERMISSIONS } from '@/access/permissions';
import type { MediaAsset } from '@/api/media';
import { jsonResponse, type Handler, type RecordedCall } from './helpers';
import { mediaServer, type MediaServer } from './media';
import { contentStudioItem, dashboardItem, memberServer, type MemberOptions } from './renderApp';

export const now = '2026-11-08T10:00:00.0000000+00:00';

export function post(id: string, overrides: Partial<Post> = {}): Post {
  return {
    id,
    workspaceId: 'w1',
    campaignId: null,
    contentType: 'social_post',
    title: `Post ${id}`,
    bodyDoc: { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: `Text of ${id}` }] }] },
    bodyText: `Text of ${id}`,
    excerpt: `Text of ${id}`,
    status: 'draft',
    visibility: 'public',
    languageCode: 'en',
    timezone: 'UTC',
    scheduledAt: null,
    publishedAt: null,
    archivedAt: null,
    seoTitle: null,
    seoDescription: null,
    seoSlug: null,
    seoKeywords: [],
    hashtags: [],
    tags: [],
    platforms: [],
    poll: null,
    ctas: [],
    mentions: [],
    location: null,
    media: [],
    settings: {},
    createdBy: 'u2',
    updatedBy: 'u2',
    createdAt: '2026-11-07T09:00:00.0000000+00:00',
    updatedAt: now,
    ...overrides,
  };
}

export function target(platform: SocialPlatform, overrides: Partial<PostPlatformItem> = {}): PostPlatformItem {
  return {
    platform,
    caption: null,
    title: null,
    settings: {},
    publishStatus: 'pending',
    scheduledAt: null,
    publishedAt: null,
    externalPostUrl: null,
    lastError: null,
    retryCount: 0,
    ...overrides,
  };
}

export function rule(platform: SocialPlatform, name: string, overrides: Partial<PlatformRule> = {}): PlatformRule {
  return { platform, name, enabled: true, captionLimit: 2200, titleLimit: null, maxHashtags: null, ...overrides };
}

/** The platforms the Site admin enabled in these tests (Instagram, X, YouTube), and the rest that exist but are off. */
export const platformRules: PlatformRule[] = [
  rule('facebook', 'Facebook', { enabled: false, captionLimit: 63206 }),
  rule('instagram', 'Instagram', { captionLimit: 2200, maxHashtags: 30 }),
  rule('x', 'X', { captionLimit: 280 }),
  rule('youtube', 'YouTube', { captionLimit: 5000, titleLimit: 100 }),
  rule('linkedin', 'LinkedIn', { enabled: false, captionLimit: 3000 }),
  rule('tiktok', 'TikTok', { enabled: false }),
  rule('pinterest', 'Pinterest', { enabled: false, captionLimit: 500, titleLimit: 100 }),
  rule('threads', 'Threads', { enabled: false, captionLimit: 500 }),
];

/** What the real API says: there is a Media Library, nothing else is built. */
export const capabilities: Capabilities = { media: true, ai: false, campaigns: false, publishing: 'fake' };

export function template(id: string, overrides: Partial<Template> = {}): Template {
  return {
    id,
    workspaceId: 'w1',
    name: `Template ${id}`,
    description: '',
    contentType: 'social_post',
    title: `Title ${id}`,
    bodyDoc: { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: `Body ${id}` }] }] },
    bodyText: `Body ${id}`,
    hashtags: ['FromTemplate'],
    createdAt: now,
    updatedAt: now,
    ...overrides,
  };
}

export const allContentPermissions = [
  PERMISSIONS.dashboardView,
  PERMISSIONS.workspacesView,
  PERMISSIONS.contentStudioView,
  PERMISSIONS.contentStudioCreate,
  PERMISSIONS.contentStudioPublish,
];

/** Content Studio and the right to look into the Media Library and add to it. */
export const contentWithMediaPermissions = [...allContentPermissions, PERMISSIONS.mediaLibraryView, PERMISSIONS.mediaLibraryCreate];

export interface ContentServerOptions {
  posts?: Post[];
  templates?: Template[];
  workspaces?: Array<{ id: string; name: string; createdAt: string }>;
  rules?: PlatformRule[];
  capabilities?: Capabilities;
  /** The files of the Media Library. Giving the list (even an empty one) puts a fake library behind /v1/media. */
  assets?: MediaAsset[];
  /** Folders of the fake library. */
  folders?: Array<{ id: string; name: string; parentFolderId: string | null }>;
  /** What the person's access token carries. */
  permissions?: string[];
  member?: MemberOptions;
}

export interface ContentServer {
  /** The routes, for renderApp. */
  routes: Record<string, Handler>;
  /** The posts as the server holds them now. */
  posts: Post[];
  templates: Template[];
  /** Every request made to /v1/content, as "METHOD path". */
  requests: string[];
  /** The JSON body of every request that had one, in order, by "METHOD path-without-query". */
  bodies: Array<{ route: string; body: unknown }>;
  /** The fake Media Library behind /v1/media, when `assets` was given. Its `assets` carry the usage count the posts make. */
  library: MediaServer | null;
}

/** What each file is for, by the same rule as the server (MediaRoles.Assign): never chosen by the page. */
function rolesFor(type: ContentType, kinds: string[]): MediaRole[] {
  let cover = false;
  let thumbnail = false;
  return kinds.map((kind): MediaRole => {
    if (type === 'carousel') {
      return 'slide';
    }
    if (type === 'blog_post' && kind === 'image' && !cover) {
      cover = true;
      return 'cover';
    }
    if ((type === 'video_script' || type === 'short_reel') && kind === 'video') {
      return 'video';
    }
    if ((type === 'video_script' || type === 'short_reel') && kind === 'image' && !thumbnail) {
      thumbnail = true;
      return 'thumbnail';
    }
    return 'attachment';
  });
}

const toListItem = (p: Post): PostListItem => ({
  id: p.id,
  workspaceId: p.workspaceId,
  contentType: p.contentType,
  title: p.title,
  excerpt: p.excerpt,
  status: p.status,
  visibility: p.visibility,
  platforms: p.platforms.map((t) => ({ platform: t.platform, publishStatus: t.publishStatus })),
  tags: p.tags,
  scheduledAt: p.scheduledAt,
  publishedAt: p.publishedAt,
  createdAt: p.createdAt,
  updatedAt: p.updatedAt,
});

const bodyOf = (call: RecordedCall): Record<string, unknown> => JSON.parse(String(call.init.body ?? '{}')) as Record<string, unknown>;

/** Makes a fake server. Use `server.routes` with renderApp, and read `server.posts`, `server.requests` and `server.bodies` afterwards. */
export function contentServer(options: ContentServerOptions = {}, overrides: Record<string, Handler> = {}): ContentServer {
  const {
    posts = [],
    templates = [],
    workspaces = [{ id: 'w1', name: 'Northwind Foods', createdAt: '2026-03-05T10:30:00Z' }],
    rules = platformRules,
    capabilities: caps = capabilities,
    permissions = allContentPermissions,
    member = {},
  } = options;

  const server: ContentServer = { routes: {}, posts: [...posts], templates: [...templates], requests: [], bodies: [], library: null };
  if (options.assets !== undefined) {
    server.library = mediaServer({ assets: options.assets, folders: options.folders, workspaces });
  }
  let counter = 0;
  const find = (id: string) => server.posts.find((p) => p.id === id);
  const missing = () => jsonResponse(404, { code: 'post_not_found', detail: 'That post was not found.' });
  const conflict = (code: string, detail = 'Not allowed.') => jsonResponse(409, { code, detail });
  const record = (call: RecordedCall, hasBody: boolean) => {
    server.requests.push(`${call.init.method ?? 'GET'} ${call.path}`);
    if (hasBody) {
      server.bodies.push({ route: `${call.init.method} ${call.path.split('?')[0]}`, body: bodyOf(call) });
    }
  };
  const syncUsage = () => {
    for (const file of server.library?.assets ?? []) {
      file.usageCount = server.posts.filter((p) => p.media.some((m) => m.assetId === file.id)).length;
    }
  };
  const touch = (p: Post, change: Partial<Post>) => Object.assign(p, change, { updatedAt: `2026-11-08T10:00:0${(counter += 1) % 10}.0000000+00:00` });

  function listHandler(call: RecordedCall): Response {
    record(call, false);
    const query = new URL(call.path, 'http://x').searchParams;
    const type = query.get('type');
    const status = query.get('status');
    const platform = query.get('platform');
    const workspace = query.get('workspaceId');
    const words = (query.get('q') ?? '').toLowerCase().split(/\s+/).filter(Boolean);
    const page = Number(query.get('page') ?? 1);
    const pageSize = Number(query.get('pageSize') ?? 20);
    const from = query.get('from');

    const matches = (p: Post, ignoreType: boolean) =>
      (ignoreType || !type || p.contentType === type) &&
      (!status || p.status === status) &&
      (!platform || p.platforms.some((t) => t.platform === platform)) &&
      (!workspace || p.workspaceId === workspace) &&
      (!from || p.createdAt >= from) &&
      words.every((w) => `${p.title} ${p.bodyText}`.toLowerCase().includes(w));

    const counts: Record<string, number> = { social_post: 0, blog_post: 0, video_script: 0, carousel: 0, short_reel: 0 };
    for (const p of server.posts.filter((entry) => matches(entry, true))) {
      counts[p.contentType] = (counts[p.contentType] ?? 0) + 1;
    }
    const all = Object.values(counts).reduce((sum, n) => sum + n, 0);
    const filtered = server.posts.filter((entry) => matches(entry, false)).sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    const items = filtered.slice((page - 1) * pageSize, page * pageSize).map(toListItem);
    return jsonResponse(200, {
      items,
      page,
      pageSize,
      totalCount: filtered.length,
      totalPages: Math.ceil(filtered.length / pageSize),
      typeCounts: { ...counts, all },
    });
  }

  function save(call: RecordedCall, existing: Post | undefined): Response {
    record(call, true);
    const body = bodyOf(call) as Record<string, any>;
    if (existing) {
      if (body.expectedUpdatedAt !== existing.updatedAt) {
        return conflict('content_changed', 'This post was changed after you opened it.');
      }
    }
    if (!existing && (!body.title && !body.bodyDoc)) {
      return jsonResponse(400, { code: 'validation_failed', detail: 'One or more fields are invalid.', errors: { content: ['Add a title or some text before saving.'] } });
    }
    const sent: Array<{ assetId: string; caption?: string }> = body.media ?? [];
    const library = server.library?.assets ?? [];
    const problem = (code: string, message: string) => jsonResponse(400, { code, detail: message, errors: { media: [message] } });
    const workspaceId: string = existing?.workspaceId ?? body.workspaceId;
    let media: PostMediaItem[] = [];
    if (sent.length > 0) {
      if (!caps.media) {
        return problem('media_not_available', 'There is no Media Library to take files from.');
      }
      const files = sent.map((entry) => library.find((a) => a.id === entry.assetId));
      if (files.some((file) => file === undefined)) {
        return problem('media_asset_not_found', 'A file you chose is no longer in the Media Library.');
      }
      if (files.some((file) => file!.workspaceId !== workspaceId)) {
        return problem('media_workspace_mismatch', 'Files must come from the same workspace as the post.');
      }
      if (files.some((file) => file!.mediaType !== 'image' && file!.mediaType !== 'video')) {
        return problem('media_type_not_allowed', 'Only images and videos can be added to a post.');
      }
      const roles = rolesFor((body.contentType ?? existing?.contentType ?? 'social_post') as ContentType, files.map((file) => file!.mediaType));
      media = files.map((file, index) => ({
        assetId: file!.id,
        role: roles[index] as MediaRole,
        displayOrder: index,
        caption: sent[index]?.caption ?? '',
        fileName: file!.fileName,
        mediaType: file!.mediaType,
        mimeType: file!.mimeType,
        sizeBytes: file!.sizeBytes,
        width: file!.width,
        height: file!.height,
      }));
    }
    const text = (doc: any): string => (doc?.content ?? []).map((n: any) => (n.content ?? []).map((c: any) => c.text ?? '').join('')).join('\n\n');
    const fields: Partial<Post> = {
      contentType: (body.contentType ?? existing?.contentType ?? 'social_post') as ContentType,
      title: body.title ?? '',
      bodyDoc: body.bodyDoc ?? null,
      bodyText: text(body.bodyDoc),
      excerpt: text(body.bodyDoc).slice(0, 160),
      visibility: body.visibility ?? 'private',
      scheduledAt: body.scheduledAt ?? null,
      hashtags: body.hashtags ?? [],
      tags: (body.tags ?? []).map((name: string, i: number) => ({ id: `t${i}`, name, color: '#1F7A55' })),
      platforms: (body.platforms ?? []).map((t: any) => target(t.platform, { caption: t.caption, title: t.title })),
      poll: body.poll ? { question: body.poll.question, options: body.poll.options, endsAt: body.poll.endsAt ?? null } : null,
      ctas: body.ctas ?? [],
      mentions: body.mentions ?? [],
      location: body.location ?? null,
      media,
    };
    if (existing) {
      touch(existing, fields);
      syncUsage();
      return jsonResponse(200, existing);
    }
    const created = post(`new-${counter + 1}`, { ...fields, workspaceId: body.workspaceId, createdAt: now });
    counter += 1;
    server.posts.push(created);
    syncUsage();
    return jsonResponse(201, created);
  }

  const action = (change: (p: Post) => Response | Partial<Post>): Handler => (call) => {
    record(call, call.init.body !== undefined);
    const id = call.path.split('/')[4] ?? '';
    const p = find(id);
    if (!p) {
      return missing();
    }
    const result = change(p);
    if (result instanceof Response) {
      return result;
    }
    touch(p, result);
    return jsonResponse(200, p);
  };

  const dynamic: Array<[RegExp, Handler]> = [
    [/^GET \/v1\/content\/posts(\?.*)?$/, listHandler],
    [
      /^GET \/v1\/content\/posts\/[^/?]+$/,
      (call) => {
        record(call, false);
        const p = find(call.path.split('/')[4] ?? '');
        return p ? jsonResponse(200, p) : missing();
      },
    ],
    [/^POST \/v1\/content\/posts$/, (call) => save(call, undefined)],
    [
      /^PUT \/v1\/content\/posts\/[^/?]+$/,
      (call) => {
        const p = find(call.path.split('/')[4] ?? '');
        return p ? save(call, p) : (record(call, true), missing());
      },
    ],
    [
      /^DELETE \/v1\/content\/posts\/[^/?]+$/,
      (call) => {
        record(call, false);
        const p = find(call.path.split('/')[4] ?? '');
        if (!p) {
          return missing();
        }
        if (p.status === 'published' || p.status === 'scheduled' || p.status === 'publishing') {
          return conflict('post_not_deletable', 'A published post cannot be deleted.');
        }
        server.posts.splice(server.posts.indexOf(p), 1);
        syncUsage();
        return new Response(null, { status: 204 });
      },
    ],
    [/^POST \/v1\/content\/posts\/[^/]+\/archive$/, action((p) => (p.status === 'scheduled' || p.status === 'publishing' || p.status === 'archived' ? conflict('invalid_transition') : { status: 'archived' as ContentStatus, archivedAt: now }))],
    [/^POST \/v1\/content\/posts\/[^/]+\/restore$/, action((p) => (p.status !== 'archived' ? conflict('invalid_transition') : { status: (p.publishedAt ? 'published' : 'draft') as ContentStatus, archivedAt: null }))],
    [/^POST \/v1\/content\/posts\/[^/]+\/unschedule$/, action((p) => (p.status !== 'scheduled' ? conflict('invalid_transition') : { status: 'draft' as ContentStatus }))],
    [
      /^POST \/v1\/content\/posts\/[^/]+\/schedule$/,
      (call) => {
        record(call, true);
        const p = find(call.path.split('/')[4] ?? '');
        if (!p) {
          return missing();
        }
        const at = (bodyOf(call) as { scheduledAt?: string }).scheduledAt ?? now;
        touch(p, { status: 'scheduled' as ContentStatus, scheduledAt: at, platforms: p.platforms.map((t) => ({ ...t, publishStatus: 'scheduled' as const })) });
        return jsonResponse(200, p);
      },
    ],
    [/^POST \/v1\/content\/posts\/[^/]+\/publish-now$/, action((p) => ({ status: 'publishing' as ContentStatus, platforms: p.platforms.map((t) => ({ ...t, publishStatus: 'scheduled' as const })) }))],
    [
      /^POST \/v1\/content\/posts\/[^/]+\/duplicate$/,
      (call) => {
        record(call, false);
        const original = find(call.path.split('/')[4] ?? '');
        if (!original) {
          return missing();
        }
        counter += 1;
        const copy = post(`copy-${counter}`, { ...original, id: `copy-${counter}`, title: `Copy of ${original.title}`, status: 'draft', scheduledAt: null, publishedAt: null, archivedAt: null });
        server.posts.push(copy);
        syncUsage();
        return jsonResponse(201, copy);
      },
    ],
    [
      /^GET \/v1\/content\/templates(\?.*)?$/,
      (call) => {
        record(call, false);
        const type = new URL(call.path, 'http://x').searchParams.get('type');
        return jsonResponse(200, server.templates.filter((t) => !type || t.contentType === type));
      },
    ],
    [
      /^POST \/v1\/content\/templates$/,
      (call) => {
        record(call, true);
        const body = bodyOf(call) as Record<string, any>;
        const created = template(`saved-${server.templates.length + 1}`, { name: body.name, description: body.description, title: body.title, contentType: body.contentType, bodyDoc: body.bodyDoc, hashtags: body.hashtags });
        server.templates.push(created);
        return jsonResponse(201, created);
      },
    ],
    [
      /^DELETE \/v1\/content\/templates\/[^/]+$/,
      (call) => {
        record(call, false);
        const at = server.templates.findIndex((t) => t.id === call.path.split('/')[4]);
        if (at < 0) {
          return jsonResponse(404, { code: 'template_not_found', detail: 'That template was not found.' });
        }
        server.templates.splice(at, 1);
        return new Response(null, { status: 204 });
      },
    ],
    [/^GET \/v1\/content\/tags(\?.*)?$/, (call) => (record(call, false), jsonResponse(200, [{ id: 'tg1', name: 'Motivation', color: '#1F7A55' }]))],
    [/^GET \/v1\/content\/hashtags\/suggestions(\?.*)?$/, (call) => (record(call, false), jsonResponse(200, [{ hashtag: 'JustMakeIt', uses: 3 }]))],
  ];

  const fixed: Record<string, Handler> = {
    'GET /v1/content/capabilities': (call) => (record(call, false), jsonResponse(200, caps)),
    'GET /v1/content/platform-rules': (call) => (record(call, false), jsonResponse(200, rules)),
    'GET /v1/workspaces': () => jsonResponse(200, workspaces),
    ...overrides,
  };

  const base = memberServer(
    { permissions, items: [dashboardItem, contentStudioItem], ...member },
    fixed,
  );

  server.routes = new Proxy(base, {
    get(target, key) {
      if (typeof key !== 'string') {
        return undefined;
      }
      if (key in target) {
        return target[key];
      }
      return dynamic.find(([pattern]) => pattern.test(key))?.[1] ?? (key.includes('/v1/media/') ? server.library?.routes[key] : undefined);
    },
  });
  syncUsage();
  return server;
}
