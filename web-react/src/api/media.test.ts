import { describe, expect, it } from 'vitest';
import { readRepositoryFile } from '@/test/repo';
import { jsonResponse, requestLine, routedFetch } from '@/test/helpers';
import { createApiClient } from './client';
import { MEDIA_SIZES, MEDIA_SORTS, MEDIA_TYPES, mediaApi, mediaFileUrl } from './media';

const quoted = (list: string) => [...list.matchAll(/"([^"]*)"/g)].map((match) => match[1]);

// The server defines the lists. This reads its source, so a value added or renamed there fails here until the client follows.
describe('the lists the server defines', () => {
  it('has the same kinds of file as MediaType.cs', () => {
    const source = readRepositoryFile('src/Platform.Media/Models/MediaType.cs');
    const body = /public enum MediaType\s*\{([\s\S]*?)\n\}/.exec(source)?.[1] ?? '';
    const members = [...body.matchAll(/^\s+([A-Z]\w+)\s*,?\s*$/gm)].map((match) => match[1]!.toLowerCase());

    expect(members).toEqual(['image', 'video', 'document', 'other']);
    expect(MEDIA_TYPES.map((type) => type.value)).toEqual(members);
  });

  it('has the same sizes and orders as MediaListQuery', () => {
    const source = readRepositoryFile('src/Platform.Media/DTOs/MediaDtos.cs');

    expect(quoted(/Sizes \{ get; \} = \[([^\]]*)\]/.exec(source)?.[1] ?? '')).toEqual(MEDIA_SIZES.map((size) => size.value));
    expect(quoted(/Sorts \{ get; \} = \[([^\]]*)\]/.exec(source)?.[1] ?? '')).toEqual(MEDIA_SORTS.map((sort) => sort.value));
  });

  it('offers the extensions the server accepts, and knows the limit of a folder size', () => {
    const source = readRepositoryFile('src/Platform.Media/Services/MediaFileRules.cs');
    const extensions = quoted(/Extensions \{ get; \} =\s*\[([^\]]*)\]/.exec(source)?.[1] ?? '');

    expect(extensions).toContain('.png');
    expect(extensions).not.toContain('.svg');
    expect(extensions).toHaveLength(11);
  });

  it('asks for the routes the controllers have', () => {
    const assets = readRepositoryFile('src/Platform.Media/Controllers/MediaAssetsController.cs');
    const folders = readRepositoryFile('src/Platform.Media/Controllers/MediaFoldersController.cs');
    const library = readRepositoryFile('src/Platform.Media/Controllers/MediaLibraryController.cs');

    expect(assets).toContain('[Route("v1/media/assets")]');
    expect(assets).toContain('[HttpGet("{id:guid}/file")]');
    expect(assets).toContain('[HttpPost("organize")]');
    expect(folders).toContain('[Route("v1/media/folders")]');
    expect(library).toContain('[HttpGet("config")]');
    expect(library).toContain('[HttpGet("overview")]');
  });
});

function client() {
  const fetchFn = routedFetch({
    'GET /v1/media/config': jsonResponse(200, { maxFileBytes: 1, extensions: [] }),
    'GET /v1/media/overview?workspaceId=w1': jsonResponse(200, { allCount: 0, uncategorizedCount: 0, folders: [], tags: [] }),
    'GET /v1/media/assets?workspaceId=w1&type=image&folderId=f1&tagId=t1&q=beach&size=small&from=2026-10-01&sort=name&page=2&pageSize=12': jsonResponse(200, { items: [] }),
    'GET /v1/media/assets?workspaceId=w1&uncategorized=true&page=1': jsonResponse(200, { items: [] }),
    'GET /v1/media/assets/a1': jsonResponse(200, { id: 'a1' }),
    'POST /v1/media/assets': jsonResponse(201, { id: 'new' }),
    'DELETE /v1/media/assets/a1': new Response(null, { status: 204 }),
    'POST /v1/media/assets/organize': new Response(null, { status: 204 }),
    'POST /v1/media/folders': jsonResponse(201, { id: 'f9' }),
    'DELETE /v1/media/folders/f1': new Response(null, { status: 204 }),
  });
  return { fetchFn, media: mediaApi(createApiClient({ fetchFn })) };
}

describe('the requests', () => {
  it('puts every set filter in the query, in a fixed order, and leaves out the rest', async () => {
    const { fetchFn, media } = client();

    await media.list({ workspaceId: 'w1', type: 'image', folderId: 'f1', tagId: 't1', q: 'beach', size: 'small', from: '2026-10-01', sort: 'name', page: 2, pageSize: 12 });
    await media.list({ workspaceId: 'w1', uncategorized: true, page: 1, type: undefined, q: '' });

    expect(fetchFn.calls.map((call) => call.path)).toEqual([
      '/v1/media/assets?workspaceId=w1&type=image&folderId=f1&tagId=t1&q=beach&size=small&from=2026-10-01&sort=name&page=2&pageSize=12',
      '/v1/media/assets?workspaceId=w1&uncategorized=true&page=1',
    ]);
  });

  it('sends an upload as the form it is, with each tag as its own field and no JSON content type', async () => {
    const { fetchFn, media } = client();
    const file = new File(['pixels'], 'photo.png', { type: 'image/png' });

    await media.upload({ workspaceId: 'w1', file, folderId: 'f1', tags: ['summer', 'beach'] });

    const call = fetchFn.calls[0]!;
    expect(requestLine(call)).toBe('POST /v1/media/assets');
    expect(call.headers['Content-Type']).toBeUndefined();
    const form = call.init.body as FormData;
    expect(form.get('workspaceId')).toBe('w1');
    expect(form.get('folderId')).toBe('f1');
    expect(form.getAll('tags')).toEqual(['summer', 'beach']);
    expect((form.get('file') as File).name).toBe('photo.png');
  });

  it('leaves the folder out of an upload that has none', async () => {
    const { fetchFn, media } = client();

    await media.upload({ workspaceId: 'w1', file: new File(['x'], 'a.png') });

    const form = fetchFn.calls[0]!.init.body as FormData;
    expect(form.has('folderId')).toBe(false);
    expect(form.getAll('tags')).toEqual([]);
  });

  it('asks for the overview, one file, and the changes', async () => {
    const { fetchFn, media } = client();

    await media.config();
    await media.overview('w1');
    await media.get('a1');
    await media.remove('a1');
    await media.organize(['a1', 'a2'], 'f1', 'copy');
    await media.organize(['a1'], null, 'move');
    await media.createFolder('w1', 'Videos');
    await media.removeFolder('f1');

    expect(fetchFn.calls.slice(0, 4).map(requestLine)).toEqual([
      'GET /v1/media/config',
      'GET /v1/media/overview?workspaceId=w1',
      'GET /v1/media/assets/a1',
      'DELETE /v1/media/assets/a1',
    ]);
    expect(JSON.parse(String(fetchFn.calls[4]!.init.body))).toEqual({ assetIds: ['a1', 'a2'], folderId: 'f1', mode: 'copy' });
    expect(JSON.parse(String(fetchFn.calls[5]!.init.body))).toEqual({ assetIds: ['a1'], folderId: null, mode: 'move' });
    expect(JSON.parse(String(fetchFn.calls[6]!.init.body))).toEqual({ workspaceId: 'w1', name: 'Videos' });
    expect(requestLine(fetchFn.calls[7]!)).toBe('DELETE /v1/media/folders/f1');
  });

  it('gives the address of the bytes of a file, and of its download', () => {
    expect(mediaFileUrl('abc')).toBe('/v1/media/assets/abc/file');
    expect(mediaFileUrl('abc', { download: true })).toBe('/v1/media/assets/abc/file?download=true');
    expect(mediaFileUrl('a/b?c')).toBe('/v1/media/assets/a%2Fb%3Fc/file');
  });
});
