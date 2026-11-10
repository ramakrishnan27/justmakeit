import { describe, expect, it } from 'vitest';
import {
  checkFile,
  dimensionsLabel,
  extensionOf,
  formatBytes,
  formatDuration,
  isMediaType,
  MAX_TAG_LENGTH,
  MAX_TAGS,
  mediaTypeLabel,
  parseTags,
  tagTone,
} from './media';

describe('formatBytes', () => {
  it.each([
    [0, '0 B'],
    [812, '812 B'],
    [1023, '1023 B'],
    [1024, '1 KB'],
    [1536, '1.5 KB'],
    [2_516_582, '2.4 MB'],
    [10 * 1024 * 1024, '10 MB'],
    [1_288_490_189, '1.2 GB'],
    [5 * 1024 ** 4, '5120 GB'],
  ])('writes %d bytes as %s', (bytes, text) => expect(formatBytes(bytes)).toBe(text));

  it('writes a number that is not a size as a dash', () => {
    expect(formatBytes(Number.NaN)).toBe('–');
    expect(formatBytes(-1)).toBe('–');
    expect(formatBytes(Number.POSITIVE_INFINITY)).toBe('–');
  });
});

describe('formatDuration', () => {
  it.each([
    [45, '0:45'],
    [80, '1:20'],
    [59.6, '1:00'],
    [3723, '1:02:03'],
    [3600, '1:00:00'],
  ])('writes %d seconds as %s', (seconds, text) => expect(formatDuration(seconds)).toBe(text));

  it('says nothing for a length that is not a positive number (a video that has not told its length yet)', () => {
    expect(formatDuration(0)).toBeNull();
    expect(formatDuration(-3)).toBeNull();
    expect(formatDuration(Number.NaN)).toBeNull();
    expect(formatDuration(Number.POSITIVE_INFINITY)).toBeNull();
  });
});

describe('the words for a kind of file', () => {
  it('names each kind in the singular, and shows an unknown one as given', () => {
    expect(mediaTypeLabel('image')).toBe('Image');
    expect(mediaTypeLabel('video')).toBe('Video');
    expect(mediaTypeLabel('document')).toBe('Document');
    expect(mediaTypeLabel('other')).toBe('Other');
    expect(mediaTypeLabel('hologram')).toBe('Hologram');
  });

  it('knows which values are kinds, strictly', () => {
    expect(isMediaType('image')).toBe(true);
    expect(isMediaType('Image')).toBe(false);
    expect(isMediaType('')).toBe(false);
    expect(isMediaType(null)).toBe(false);
  });
});

describe('extensionOf', () => {
  it.each([
    ['photo.png', '.png'],
    ['Holiday.PNG', '.png'],
    ['archive.tar.gz', '.gz'],
    ['noextension', ''],
    ['.hidden', ''],
    ['ends.', ''],
    ['', ''],
  ])('reads %j as %j', (name, extension) => expect(extensionOf(name)).toBe(extension));
});

describe('dimensionsLabel', () => {
  it('writes the size of an image, and nothing for a file without one', () => {
    expect(dimensionsLabel({ width: 1920, height: 1080 })).toBe('1920 × 1080');
    expect(dimensionsLabel({ width: null, height: null })).toBeNull();
    expect(dimensionsLabel({ width: 100, height: null })).toBeNull();
  });
});

describe('parseTags', () => {
  it('splits at commas, trims, drops the # and repeats in any letter case, and keeps the first spelling', () => {
    expect(parseTags(' Summer, #beach day ,summer,, SUMMER ,  ')).toEqual({ tags: ['Summer', 'beach day'], problem: null });
  });

  it('is empty for nothing', () => {
    expect(parseTags('')).toEqual({ tags: [], problem: null });
    expect(parseTags(' , ,')).toEqual({ tags: [], problem: null });
  });

  it('allows the most the server allows', () => {
    const names = Array.from({ length: MAX_TAGS }, (_, i) => `tag${i}`).join(',');
    expect(parseTags(names).problem).toBeNull();
    expect(parseTags(`${names},one-too-many`).problem).toBe(`Use at most ${MAX_TAGS} tags.`);
  });

  it('refuses a tag that is too long, and still returns the names so the field keeps its text', () => {
    const result = parseTags(`ok,${'x'.repeat(MAX_TAG_LENGTH + 1)}`);

    expect(result.problem).toBe(`A tag is at most ${MAX_TAG_LENGTH} characters.`);
    expect(result.tags).toHaveLength(2);
    expect(parseTags('x'.repeat(MAX_TAG_LENGTH)).problem).toBeNull();
  });
});

describe('tagTone', () => {
  it('gives the same tone to the same name whatever its letter case, and always one of six', () => {
    expect(tagTone('Summer')).toBe(tagTone('summer'));
    for (const name of ['a', 'beach', 'Product Launch', 'सूरज', '😀', '']) {
      expect(tagTone(name)).toBeGreaterThanOrEqual(0);
      expect(tagTone(name)).toBeLessThan(6);
      expect(Number.isInteger(tagTone(name))).toBe(true);
    }
  });

  it('uses more than one tone', () => {
    expect(new Set(['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'].map(tagTone)).size).toBeGreaterThan(1);
  });
});

describe('checkFile', () => {
  const config = { maxFileBytes: 5 * 1024 * 1024, extensions: ['.png', '.jpg', '.pdf'] };

  it('lets a file of an accepted kind and size through', () => {
    expect(checkFile({ name: 'a.png', size: 100 }, config)).toBeNull();
    expect(checkFile({ name: 'A.PDF', size: config.maxFileBytes }, config)).toBeNull();
  });

  it('refuses an empty file, one that is too large and one of a kind that is not accepted, each in its own words', () => {
    expect(checkFile({ name: 'a.png', size: 0 }, config)).toBe('This file is empty.');
    expect(checkFile({ name: 'a.png', size: config.maxFileBytes + 1 }, config)).toBe('This file is larger than 5 MB.');
    expect(checkFile({ name: 'logo.svg', size: 10 }, config)).toBe('This type of file is not supported. Use .png, .jpg, .pdf.');
    expect(checkFile({ name: 'noextension', size: 10 }, config)).toContain('not supported');
  });
});
