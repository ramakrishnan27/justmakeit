// A fake Media Library server for the page tests: it keeps the files, folders and tags in memory and answers the way the real one does (the
// list with its filters, sorting, paging and per-type counts; the overview; uploading a form; moving and copying into folders; deleting
// with the in-use rule), so the tests drive the page against behaviour, not against canned answers. Built on the member server of
// renderApp, with a Proxy for the addresses that carry a query string, which the fake fetch otherwise only matches exactly.

import { PERMISSIONS } from '@/access/permissions';
import type { MediaAsset, MediaConfig, MediaFolder, MediaTagRef, MediaType } from '@/api/media';
import { jsonResponse, type Handler, type RecordedCall } from './helpers';
import { dashboardItem, mediaLibraryItem, memberServer, type MemberOptions } from './renderApp';

export const now = '2026-11-08T10:00:00.0000000+00:00';

export function asset(id: string, overrides: Partial<MediaAsset> = {}): MediaAsset {
  return {
    id,
    workspaceId: 'b1000000',
    fileName: `file-${id}.png`,
    mimeType: 'image/png',
    mediaType: 'image',
    sizeBytes: 1_500_000,
    width: null,
    height: null,
    usageCount: 0,
    createdAt: '2026-11-07T09:00:00.0000000+00:00',
    tags: [],
    folderIds: [],
    ...overrides,
  };
}

export const tag = (id: string, name: string): MediaTagRef => ({ id, name });
export const folder = (id: string, name: string): Omit<MediaFolder, 'assetCount'> => ({ id, name, parentFolderId: null });

export const config: MediaConfig = {
  maxFileBytes: 5 * 1024 * 1024,
  extensions: ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.mp4', '.mov', '.webm', '.pdf', '.mp3', '.wav'],
};

export const allMediaPermissions = [PERMISSIONS.dashboardView, PERMISSIONS.mediaLibraryView, PERMISSIONS.mediaLibraryCreate, PERMISSIONS.mediaLibraryDelete];

export interface MediaServerOptions {
  assets?: MediaAsset[];
  folders?: Array<Omit<MediaFolder, 'assetCount'>>;
  workspaces?: Array<{ id: string; name: string; createdAt: string }>;
  config?: MediaConfig;
  /** What the person's access token carries. */
  permissions?: string[];
  member?: MemberOptions;
}

export interface MediaServer {
  /** The routes, for renderApp. */
  routes: Record<string, Handler>;
  assets: MediaAsset[];
  folders: Array<Omit<MediaFolder, 'assetCount'>>;
  /** Every request made to /v1/media, as "METHOD path". */
  requests: string[];
  /** The JSON body of every request that had one, in order. */
  bodies: Array<{ route: string; body: unknown }>;
  /** Every form that was uploaded: its text fields and the name of its file. */
  uploads: Array<{ fields: Record<string, string[]>; fileName: string }>;
}

const mediaTypeOf = (name: string): MediaType => {
  const extension = name.slice(name.lastIndexOf('.')).toLowerCase();
  if (['.mp4', '.mov', '.webm'].includes(extension)) {
    return 'video';
  }
  return extension === '.pdf' ? 'document' : ['.mp3', '.wav'].includes(extension) ? 'other' : 'image';
};

const bodyOf = (call: RecordedCall): Record<string, unknown> => JSON.parse(String(call.init.body ?? '{}')) as Record<string, unknown>;

/** Makes a fake server. Use `server.routes` with renderApp, and read `server.assets`, `server.requests` and the rest afterwards. */
export function mediaServer(options: MediaServerOptions = {}, overrides: Record<string, Handler> = {}): MediaServer {
  const {
    assets = [],
    folders = [],
    workspaces = [{ id: 'b1000000', name: 'Northwind Foods', createdAt: '2026-03-05T10:30:00Z' }],
    config: serverConfig = config,
    permissions = allMediaPermissions,
    member = {},
  } = options;

  const server: MediaServer = { routes: {}, assets: [...assets], folders: [...folders], requests: [], bodies: [], uploads: [] };
  let counter = 0;
  const record = (call: RecordedCall, hasBody: boolean) => {
    server.requests.push(`${call.init.method ?? 'GET'} ${call.path}`);
    if (hasBody && !(call.init.body instanceof FormData)) {
      server.bodies.push({ route: `${call.init.method} ${call.path.split('?')[0]}`, body: bodyOf(call) });
    }
  };
  const missing = (code = 'asset_not_found') => jsonResponse(404, { code, detail: 'Not found.' });

  function listHandler(call: RecordedCall): Response {
    record(call, false);
    const query = new URL(call.path, 'http://x').searchParams;
    const workspace = query.get('workspaceId');
    const type = query.get('type');
    const folderId = query.get('folderId');
    const uncategorized = query.get('uncategorized') === 'true';
    const tagId = query.get('tagId');
    const words = (query.get('q') ?? '').toLowerCase();
    const size = query.get('size');
    const from = query.get('from');
    const sort = query.get('sort') ?? 'newest';
    const page = Number(query.get('page') ?? 1);
    const pageSize = Number(query.get('pageSize') ?? 12);

    const mb = 1024 * 1024;
    const matches = (a: MediaAsset, ignoreType: boolean) =>
      a.workspaceId === workspace &&
      (ignoreType || !type || a.mediaType === type) &&
      (!folderId || a.folderIds.includes(folderId)) &&
      (!uncategorized || a.folderIds.length === 0) &&
      (!tagId || a.tags.some((t) => t.id === tagId)) &&
      (!words || a.fileName.toLowerCase().includes(words) || a.tags.some((t) => t.name.toLowerCase().includes(words))) &&
      (!size || (size === 'small' ? a.sizeBytes < mb : size === 'medium' ? a.sizeBytes >= mb && a.sizeBytes < 10 * mb : a.sizeBytes >= 10 * mb)) &&
      (!from || a.createdAt >= from);

    const typeCounts = { all: 0, image: 0, video: 0, document: 0, other: 0 };
    for (const a of server.assets.filter((entry) => matches(entry, true))) {
      typeCounts[a.mediaType] += 1;
      typeCounts.all += 1;
    }
    const order: Record<string, (a: MediaAsset, b: MediaAsset) => number> = {
      newest: (a, b) => b.createdAt.localeCompare(a.createdAt),
      oldest: (a, b) => a.createdAt.localeCompare(b.createdAt),
      name: (a, b) => a.fileName.toLowerCase().localeCompare(b.fileName.toLowerCase()),
      size: (a, b) => b.sizeBytes - a.sizeBytes,
    };
    const filtered = server.assets.filter((entry) => matches(entry, false)).sort(order[sort] ?? order.newest!);
    return jsonResponse(200, {
      items: filtered.slice((page - 1) * pageSize, page * pageSize),
      page,
      pageSize,
      totalCount: filtered.length,
      totalPages: Math.ceil(filtered.length / pageSize),
      typeCounts,
    });
  }

  function overviewHandler(call: RecordedCall): Response {
    record(call, false);
    const workspace = new URL(call.path, 'http://x').searchParams.get('workspaceId');
    const own = server.assets.filter((a) => a.workspaceId === workspace);
    const tagCounts = new Map<string, { id: string; name: string; assetCount: number }>();
    for (const a of own) {
      for (const t of a.tags) {
        const entry = tagCounts.get(t.id) ?? { id: t.id, name: t.name, assetCount: 0 };
        entry.assetCount += 1;
        tagCounts.set(t.id, entry);
      }
    }
    return jsonResponse(200, {
      allCount: own.length,
      uncategorizedCount: own.filter((a) => a.folderIds.length === 0).length,
      folders: server.folders.map((f) => ({ ...f, assetCount: own.filter((a) => a.folderIds.includes(f.id)).length })),
      tags: [...tagCounts.values()].sort((a, b) => b.assetCount - a.assetCount || a.name.localeCompare(b.name)),
    });
  }

  function uploadHandler(call: RecordedCall): Response {
    record(call, true);
    const form = call.init.body as FormData;
    const file = form.get('file') as File;
    const fields: Record<string, string[]> = {};
    for (const key of ['workspaceId', 'folderId', 'tags']) {
      fields[key] = form.getAll(key).map(String);
    }
    server.uploads.push({ fields, fileName: file.name });

    if (file.name.startsWith('refused')) {
      return jsonResponse(400, {
        code: 'validation_failed',
        detail: 'One or more fields are invalid.',
        errors: { file: ['This type of file is not supported. Use PNG, JPEG, GIF, WebP, MP4, MOV, WebM, PDF, MP3 or WAV.'] },
      });
    }

    counter += 1;
    const tags = (fields.tags ?? []).map((name, i) => server.assets.flatMap((a) => a.tags).find((t) => t.name.toLowerCase() === name.toLowerCase()) ?? tag(`nt${counter}-${i}`, name));
    const created = asset(`up${counter}`, {
      workspaceId: String(form.get('workspaceId')),
      fileName: file.name,
      mediaType: mediaTypeOf(file.name),
      mimeType: 'image/png',
      sizeBytes: file.size,
      createdAt: now,
      tags,
      folderIds: fields.folderId?.[0] ? [fields.folderId[0]] : [],
    });
    server.assets.push(created);
    return jsonResponse(201, created);
  }

  const idOf = (call: RecordedCall) => call.path.split('?')[0]!.split('/')[4] ?? '';

  const dynamic: Array<[RegExp, Handler]> = [
    [/^GET \/v1\/media\/overview(\?.*)?$/, overviewHandler],
    [/^GET \/v1\/media\/assets(\?.*)?$/, listHandler],
    [/^POST \/v1\/media\/assets$/, uploadHandler],
    [
      /^POST \/v1\/media\/assets\/organize$/,
      (call) => {
        record(call, true);
        const body = bodyOf(call) as { assetIds: string[]; folderId: string | null; mode: 'move' | 'copy' };
        const chosen = server.assets.filter((a) => body.assetIds.includes(a.id));
        if (chosen.length !== body.assetIds.length) {
          return missing();
        }
        if (body.folderId !== null && !server.folders.some((f) => f.id === body.folderId)) {
          return missing('folder_not_found');
        }
        for (const a of chosen) {
          const kept = body.mode === 'move' ? [] : a.folderIds;
          a.folderIds = body.folderId === null || kept.includes(body.folderId) ? kept : [...kept, body.folderId];
        }
        return new Response(null, { status: 204 });
      },
    ],
    [
      /^DELETE \/v1\/media\/assets\/[^/?]+$/,
      (call) => {
        record(call, false);
        const found = server.assets.find((a) => a.id === idOf(call));
        if (!found) {
          return missing();
        }
        if (found.usageCount > 0) {
          return jsonResponse(409, { code: 'asset_in_use', detail: 'This file is used by content and cannot be deleted.' });
        }
        server.assets.splice(server.assets.indexOf(found), 1);
        return new Response(null, { status: 204 });
      },
    ],
    [
      /^POST \/v1\/media\/folders$/,
      (call) => {
        record(call, true);
        const body = bodyOf(call) as { name: string };
        if (server.folders.some((f) => f.name.toLowerCase() === body.name.toLowerCase())) {
          return jsonResponse(409, { code: 'folder_exists', detail: 'This workspace already has a folder with that name.' });
        }
        counter += 1;
        const created = folder(`nf${counter}`, body.name);
        server.folders.push(created);
        return jsonResponse(201, { ...created, assetCount: 0 });
      },
    ],
    [
      /^DELETE \/v1\/media\/folders\/[^/?]+$/,
      (call) => {
        record(call, false);
        const at = server.folders.findIndex((f) => f.id === idOf(call));
        if (at < 0) {
          return missing('folder_not_found');
        }
        const [removed] = server.folders.splice(at, 1);
        for (const a of server.assets) {
          a.folderIds = a.folderIds.filter((id) => id !== removed!.id);
        }
        return new Response(null, { status: 204 });
      },
    ],
  ];

  const fixed: Record<string, Handler> = {
    'GET /v1/media/config': (call) => (record(call, false), jsonResponse(200, serverConfig)),
    'GET /v1/workspaces': () => jsonResponse(200, workspaces),
    ...overrides,
  };

  const base = memberServer({ permissions, items: [dashboardItem, mediaLibraryItem], ...member }, fixed);

  server.routes = new Proxy(base, {
    get(target, key) {
      if (typeof key !== 'string') {
        return undefined;
      }
      if (key in target) {
        return target[key];
      }
      return dynamic.find(([pattern]) => pattern.test(key))?.[1];
    },
  });
  return server;
}
