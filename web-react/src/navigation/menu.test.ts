import { describe, expect, it } from 'vitest';
import { isSafePath, parseMenu } from './menu';

describe('isSafePath', () => {
  it('accepts an address of this site', () => {
    for (const path of ['/', '/dashboard', '/workspaces', '/a/b_c-d', '/Team2']) {
      expect(isSafePath(path), path).toBe(true);
    }
  });

  it('refuses anything that could leave the site or break the page', () => {
    const bad = ['', 'dashboard', 'https://evil.example/', '//evil.example', '/a//b', 'javascript:alert(1)', '/a.b', '/a?x=1', '/a#x', '/a b', '/../x', '\\evil'];
    for (const path of bad) {
      expect(isSafePath(path), path).toBe(false);
    }
    for (const notText of [undefined, null, 5, {}, ['/a']]) {
      expect(isSafePath(notText), String(notText)).toBe(false);
    }
  });
});

describe('parseMenu', () => {
  it('keeps good items and permissions in order', () => {
    const body = {
      items: [{ id: '1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' }],
      permissions: ['dashboard.view'],
    };

    expect(parseMenu(body)).toEqual(body);
  });

  it('draws the circle for an item with no icon, and leaves the page key empty when it is missing', () => {
    expect(parseMenu({ items: [{ id: '1', label: 'X', path: '/x' }], permissions: [] }).items).toEqual([{ id: '1', pageKey: '', label: 'X', icon: 'circle', path: '/x' }]);
  });

  it('drops items it could not draw safely', () => {
    const items = [
      { id: '', label: 'No id', path: '/a' },
      { id: '2', label: '   ', path: '/b' },
      { id: '3', label: 'Elsewhere', path: 'https://evil.example/' },
      null,
      'text',
      { id: '4', label: 'Fine', path: '/fine' },
    ];

    expect(parseMenu({ items, permissions: [] }).items.map((item) => item.id)).toEqual(['4']);
  });

  it('gives an empty menu for an answer that is not a menu', () => {
    for (const body of [undefined, null, 'x', 5, {}, { items: 'x', permissions: 'y' }]) {
      expect(parseMenu(body), String(body)).toEqual({ items: [], permissions: [] });
    }
  });

  it('drops permissions that are not text', () => {
    expect(parseMenu({ items: [], permissions: ['a.b', 7, null, 'c.d'] }).permissions).toEqual(['a.b', 'c.d']);
  });
});
