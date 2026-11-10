import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { adminFallbackItems, isCurrent, isSafePath, parseMenu } from '../js/core/menu.js';
import { adminApp } from '../js/core/router.js';

describe('isSafePath', () => {
  it('accepts an address of this site', () => {
    for (const path of ['/', '/dashboard', '/workspaces', '/admin/menus', '/email-lists/new', '/a_b/c-d/E1']) {
      assert.equal(isSafePath(path), true, path);
    }
  });

  it('refuses anything that could leave the site or break the page', () => {
    const refused = [
      'https://evil.example/',
      'http://evil.example',
      '//evil.example',
      '/a//b',
      'javascript:alert(1)',
      'data:text/html,x',
      'dashboard',
      '',
      '/a b',
      '/a?x=1',
      '/a#top',
      '/a/../b',
      '/a.b',
      '/a\\b',
      '/<script>',
      '/a\nb',
      null,
      undefined,
      42,
      {},
    ];
    for (const path of refused) {
      assert.equal(isSafePath(path), false, String(path));
    }
  });
});

describe('parseMenu', () => {
  const dashboard = { id: 'i1', pageKey: 'dashboard', label: 'Dashboard', icon: 'home', path: '/dashboard' };

  it('keeps the items and the permissions the server sent, in order', () => {
    const menu = parseMenu({ items: [dashboard, { ...dashboard, id: 'i2', pageKey: 'workspaces', label: 'Workspaces', path: '/workspaces' }], permissions: ['a.b', 'c.d'] });

    assert.deepEqual(
      menu.items.map((item) => item.label),
      ['Dashboard', 'Workspaces'],
    );
    assert.deepEqual(menu.permissions, ['a.b', 'c.d']);
  });

  it('copies only the fields the page uses', () => {
    const [item] = parseMenu({ items: [{ ...dashboard, secret: 'x', onclick: 'alert(1)' }], permissions: [] }).items;

    assert.deepEqual(item, dashboard);
  });

  it('drops an item with an address the page must not link to', () => {
    const menu = parseMenu({
      items: [dashboard, { ...dashboard, id: 'x', path: 'https://evil.example' }, { ...dashboard, id: 'y', path: '//evil.example' }],
      permissions: [],
    });

    assert.deepEqual(menu.items, [dashboard]);
  });

  it('drops an item without an id, or without words to show', () => {
    const menu = parseMenu({
      items: [dashboard, { ...dashboard, id: '' }, { ...dashboard, id: undefined }, { ...dashboard, id: 'z', label: '   ' }, { ...dashboard, id: 'w', label: 7 }],
      permissions: [],
    });

    assert.deepEqual(menu.items, [dashboard]);
  });

  it('draws an item whose icon is missing or not text with the plain circle', () => {
    const menu = parseMenu({ items: [{ ...dashboard, icon: undefined }, { ...dashboard, id: 'i2', icon: 5 }], permissions: [] });

    assert.deepEqual(
      menu.items.map((item) => item.icon),
      ['circle', 'circle'],
    );
  });

  it('keeps an icon name it does not know: drawing it is the icon module’s job', () => {
    assert.equal(parseMenu({ items: [{ ...dashboard, icon: 'rocket' }], permissions: [] }).items[0].icon, 'rocket');
  });

  it('keeps only the permissions that are text', () => {
    assert.deepEqual(parseMenu({ items: [], permissions: ['a.b', 7, null, undefined, {}, 'c.d'] }).permissions, ['a.b', 'c.d']);
  });

  it('survives an answer that is not shaped like a menu', () => {
    for (const body of [null, undefined, 'text', 7, [], {}, { items: 'x', permissions: 'y' }, { items: [null, 1, 'a', []] }]) {
      assert.deepEqual(parseMenu(body), { items: [], permissions: [] }, JSON.stringify(body));
    }
  });
});

describe('isCurrent', () => {
  it('is true for the page that is open, with or without a trailing slash', () => {
    assert.equal(isCurrent('/workspaces', '/workspaces'), true);
    assert.equal(isCurrent('/workspaces', '/workspaces/'), true);
    assert.equal(isCurrent('/workspaces/', '/workspaces'), true);
    assert.equal(isCurrent('/', '/'), true);
  });

  it('is false for another page, or one that merely starts the same way', () => {
    assert.equal(isCurrent('/workspaces', '/dashboard'), false);
    assert.equal(isCurrent('/admin', '/admin/menus'), false);
    assert.equal(isCurrent('/admin/menus', '/admin'), false);
    assert.equal(isCurrent('/work', '/workspaces'), false);
  });

  it('is false when either address is missing', () => {
    assert.equal(isCurrent('/workspaces', undefined), false);
    assert.equal(isCurrent(null, '/workspaces'), false);
  });
});

describe('adminFallbackItems', () => {
  it('are the three pages that repair a menu, and each is a page the admin app has', () => {
    assert.deepEqual(
      adminFallbackItems.map((item) => item.path),
      ['/admin', '/admin/menus', '/admin/permissions'],
    );
    for (const item of adminFallbackItems) {
      assert.ok(adminApp.routes.some((route) => route.path === item.path && route.requiresAuth), item.path);
    }
  });

  it('are safe addresses, with words and an id each', () => {
    for (const item of adminFallbackItems) {
      assert.equal(isSafePath(item.path), true);
      assert.ok(item.label.length > 0 && item.id.length > 0 && item.icon.length > 0);
    }
  });

  it('cannot be changed, so one session cannot alter the list another is given', () => {
    assert.ok(Object.isFrozen(adminFallbackItems));
    assert.ok(adminFallbackItems.every((item) => Object.isFrozen(item)));
  });
});
