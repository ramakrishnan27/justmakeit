import { describe, expect, it } from 'vitest';
import { filtersFromSearch, isNarrowed, listParams, noFilters, searchFromFilters, UNCATEGORIZED } from './mediaFilters';

const read = (search: string) => filtersFromSearch(new URLSearchParams(search));
const write = (filters: Partial<typeof noFilters>) => searchFromFilters({ ...noFilters, ...filters }).toString();

describe('what the address holds', () => {
  it('is the defaults for an empty address', () => {
    expect(read('')).toEqual(noFilters);
  });

  it('reads every choice', () => {
    const id = '11111111-2222-3333-4444-555555555555';

    expect(read(`workspace=${id}&type=video&folder=${id}&tag=${id}&size=medium&range=30&q=beach&sort=name&view=list&page=3`)).toEqual({
      workspaceId: id,
      type: 'video',
      folder: id,
      tagId: id,
      size: 'medium',
      range: '30',
      q: 'beach',
      sort: 'name',
      view: 'list',
      page: 3,
    });
  });

  it('reads folder=none as the files that are in no folder', () => {
    expect(read(`folder=${UNCATEGORIZED}`).folder).toBe(UNCATEGORIZED);
  });

  it('ignores whatever the page does not know, as if it were not there', () => {
    expect(read('type=hologram&folder=../../etc&tag=x&workspace=%3Cscript%3E&size=huge&range=9&sort=random&view=table&page=-4')).toEqual(noFilters);
    expect(read('page=abc').page).toBe(1);
    expect(read('page=0').page).toBe(1);
    expect(read('page=2.5').page).toBe(2);
  });

  it('cuts a search to what the server accepts', () => {
    expect(read(`q=${'x'.repeat(300)}`).q).toHaveLength(100);
  });
});

describe('the address for some choices', () => {
  it('holds only what is not the default, in a fixed order', () => {
    expect(write({})).toBe('');
    expect(write({ page: 2, q: 'beach', type: 'image', sort: 'size', view: 'list' })).toBe('type=image&q=beach&sort=size&view=list&page=2');
    expect(write({ view: 'list', type: 'image' })).toBe(write({ type: 'image', view: 'list' }));
  });

  it('puts a blank search nowhere, and trims a real one', () => {
    expect(write({ q: '   ' })).toBe('');
    expect(write({ q: '  beach ' })).toBe('q=beach');
  });

  it('round-trips', () => {
    const chosen = { ...noFilters, type: 'document', folder: UNCATEGORIZED, size: 'small', range: '7', q: 'brief', sort: 'oldest', view: 'list' as const, page: 4 };

    expect(read(searchFromFilters(chosen).toString())).toEqual(chosen);
  });
});

describe('what narrows the list', () => {
  it('is the tag, the size, the date and the words, and not the tab, the folder or the workspace', () => {
    expect(isNarrowed(noFilters)).toBe(false);
    expect(isNarrowed({ ...noFilters, type: 'image', folder: UNCATEGORIZED, workspaceId: 'abcdef12', sort: 'name' })).toBe(false);
    expect(isNarrowed({ ...noFilters, tagId: 'abcdef12' })).toBe(true);
    expect(isNarrowed({ ...noFilters, size: 'small' })).toBe(true);
    expect(isNarrowed({ ...noFilters, range: '7' })).toBe(true);
    expect(isNarrowed({ ...noFilters, q: 'x' })).toBe(true);
    expect(isNarrowed({ ...noFilters, q: '   ' })).toBe(false);
  });
});

describe('what goes to the server', () => {
  const now = new Date('2026-11-08T10:00:00Z');

  it('has the workspace and the page size, and nothing the person did not choose', () => {
    expect(listParams(noFilters, 'w1', now)).toEqual({
      workspaceId: 'w1',
      type: undefined,
      folderId: undefined,
      uncategorized: false,
      tagId: undefined,
      q: undefined,
      size: undefined,
      from: undefined,
      sort: undefined,
      page: 1,
      pageSize: 12,
    });
  });

  it('turns a folder into folderId, and none into uncategorized', () => {
    expect(listParams({ ...noFilters, folder: 'f1' }, 'w1', now)).toMatchObject({ folderId: 'f1', uncategorized: false });
    expect(listParams({ ...noFilters, folder: UNCATEGORIZED }, 'w1', now)).toMatchObject({ folderId: undefined, uncategorized: true });
  });

  it('turns a date range into the instant it starts, and leaves out the default order', () => {
    expect(listParams({ ...noFilters, range: '7' }, 'w1', now).from).toBe('2026-11-01T10:00:00.000Z');
    expect(listParams({ ...noFilters, sort: 'newest' }, 'w1', now).sort).toBeUndefined();
    expect(listParams({ ...noFilters, sort: 'name', q: ' beach ', page: 3 }, 'w1', now)).toMatchObject({ sort: 'name', q: 'beach', page: 3 });
  });
});
