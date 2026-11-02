import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError } from '../js/core/api.js';
import { iconNames } from '../js/core/icons.js';
import {
  canMove,
  defaultLabelFor,
  maxLabelLength,
  menus,
  messageForMenuError,
  messageForMenuLoadError,
  moveItem,
  shouldReloadAfter,
  validateMenuItem,
} from '../js/core/menu-admin.js';

describe('menus', () => {
  it('are the two the server has, the members’ first', () => {
    assert.deepEqual(
      menus.map((menu) => menu.key),
      ['member', 'admin'],
    );
    assert.ok(menus.every((menu) => menu.title.length > 0 && menu.description.length > 10));
  });
});

describe('canMove and moveItem', () => {
  const ids = ['a', 'b', 'c'];

  it('moves an item up or down one place', () => {
    assert.deepEqual(moveItem(ids, 'b', 'up'), ['b', 'a', 'c']);
    assert.deepEqual(moveItem(ids, 'b', 'down'), ['a', 'c', 'b']);
    assert.deepEqual(moveItem(ids, 'c', 'up'), ['a', 'c', 'b']);
    assert.deepEqual(moveItem(ids, 'a', 'down'), ['b', 'a', 'c']);
  });

  it('cannot move the first item up or the last item down', () => {
    assert.equal(canMove(ids, 'a', 'up'), false);
    assert.equal(canMove(ids, 'c', 'down'), false);
    assert.deepEqual(moveItem(ids, 'a', 'up'), ids);
    assert.deepEqual(moveItem(ids, 'c', 'down'), ids);
  });

  it('can move the others both ways', () => {
    assert.equal(canMove(ids, 'b', 'up'), true);
    assert.equal(canMove(ids, 'b', 'down'), true);
    assert.equal(canMove(ids, 'a', 'down'), true);
    assert.equal(canMove(ids, 'c', 'up'), true);
  });

  it('cannot move an item that is not in the list, or in a direction that does not exist', () => {
    assert.equal(canMove(ids, 'z', 'up'), false);
    assert.equal(canMove(ids, 'b', 'sideways'), false);
    assert.deepEqual(moveItem(ids, 'z', 'up'), ids);
    assert.deepEqual(moveItem(ids, 'b', 'sideways'), ids);
  });

  it('never changes the list it was given', () => {
    const original = ['a', 'b', 'c'];

    moveItem(original, 'b', 'up');

    assert.deepEqual(original, ['a', 'b', 'c']);
  });

  it('keeps every id exactly once, whatever is moved', () => {
    let current = ['a', 'b', 'c', 'd'];
    for (const [id, direction] of [['d', 'up'], ['a', 'down'], ['c', 'up'], ['b', 'down']]) {
      current = moveItem(current, id, direction);
    }

    assert.deepEqual([...current].sort(), ['a', 'b', 'c', 'd']);
  });

  it('handles a list of one and an empty list', () => {
    assert.deepEqual(moveItem(['a'], 'a', 'up'), ['a']);
    assert.deepEqual(moveItem([], 'a', 'down'), []);
  });
});

describe('validateMenuItem', () => {
  const good = { label: 'Workspaces', icon: 'folder', pageKey: 'workspaces' };

  it('accepts a good item, with or without a page', () => {
    assert.equal(validateMenuItem(good, { requirePage: true }), null);
    assert.equal(validateMenuItem({ label: 'Workspaces', icon: 'folder' }), null);
  });

  it('asks for a page only when one is being added', () => {
    assert.deepEqual(validateMenuItem({ label: 'x', icon: 'home' }, { requirePage: true }), { field: 'pageKey', message: 'Choose a page to add.' });
    assert.deepEqual(validateMenuItem({ ...good, pageKey: '' }, { requirePage: true })?.field, 'pageKey');
    assert.equal(validateMenuItem({ label: 'x', icon: 'home' }), null);
  });

  it('asks for words', () => {
    for (const label of [undefined, null, '', '   ', 5]) {
      assert.equal(validateMenuItem({ ...good, label })?.field, 'label', String(label));
    }
  });

  it('allows the longest label the server allows, and refuses one more', () => {
    assert.equal(maxLabelLength, 60);
    assert.equal(validateMenuItem({ ...good, label: 'a'.repeat(60) }), null);
    assert.equal(validateMenuItem({ ...good, label: 'a'.repeat(61) })?.field, 'label');
  });

  it('measures the words after trimming, as the server does', () => {
    assert.equal(validateMenuItem({ ...good, label: `  ${'a'.repeat(60)}  ` }), null);
  });

  it('refuses line breaks and other control characters in the words', () => {
    for (const label of ['two\nlines', 'tab\there', 'bell\u0007', 'null\u0000byte', 'delete\u007f', 'c1\u0085control']) {
      assert.equal(validateMenuItem({ ...good, label })?.field, 'label', JSON.stringify(label));
    }
  });

  it('accepts ordinary punctuation and other languages', () => {
    for (const label of ['Équipes & clients', 'Q&A (beta) — 2026', '日本語', 'It’s ready']) {
      assert.equal(validateMenuItem({ ...good, label }), null, label);
    }
  });

  it('accepts every icon the client can draw, and nothing else', () => {
    for (const icon of iconNames) {
      assert.equal(validateMenuItem({ ...good, icon }), null, icon);
    }
    for (const icon of [undefined, null, '', 'sparkles', 'Home', 'home ', '<svg>']) {
      assert.equal(validateMenuItem({ ...good, icon })?.field, 'icon', String(icon));
    }
  });

  it('reports the first problem it finds: the page, then the words, then the icon', () => {
    assert.equal(validateMenuItem({ label: '', icon: 'nope' }, { requirePage: true })?.field, 'pageKey');
    assert.equal(validateMenuItem({ pageKey: 'p', label: '', icon: 'nope' }, { requirePage: true })?.field, 'label');
    assert.equal(validateMenuItem({ pageKey: 'p', label: 'x', icon: 'nope' }, { requirePage: true })?.field, 'icon');
  });
});

describe('defaultLabelFor', () => {
  it('offers the title of the page as the words', () => {
    assert.equal(defaultLabelFor({ key: 'workspaces', title: 'Workspaces' }), 'Workspaces');
  });

  it('never offers more than the server accepts', () => {
    assert.equal(defaultLabelFor({ title: 'a'.repeat(80) }).length, 60);
  });

  it('offers nothing when there is no page', () => {
    for (const page of [undefined, null, {}, { title: 5 }]) {
      assert.equal(defaultLabelFor(page), '', String(page));
    }
  });
});

describe('messageForMenuError', () => {
  const failure = (status, code, errors = null) => new ApiError(status, 'Detail the page must not show.', { code, errors });

  it('explains a locked item', () => {
    assert.match(messageForMenuError(failure(409, 'menu_item_locked')), /locked/i);
  });

  it('explains a page that is already in the menu, and one that cannot go there', () => {
    assert.match(messageForMenuError(failure(409, 'page_already_in_menu')), /already in the menu/);
    assert.match(messageForMenuError(failure(404, 'page_not_found')), /cannot go in this menu/);
  });

  it('says the list was reloaded when an item vanished or the menu changed', () => {
    assert.match(messageForMenuError(failure(404, 'menu_item_not_found')), /reloaded/);
    assert.match(messageForMenuError(failure(409, 'menu_changed')), /reloaded/);
  });

  it('says what happened to a site admin who is no longer one, and to one who clicks too fast', () => {
    assert.match(messageForMenuError(failure(403, 'not_site_admin')), /no longer a site admin/);
    assert.match(messageForMenuError(failure(429, 'rate_limited')), /Too many/);
  });

  it('shows the first field message of a bad request', () => {
    assert.equal(messageForMenuError(failure(400, 'validation_failed', { label: ['Label is required.'], icon: ['x'] })), 'Label is required.');
  });

  it('never shows what the server said in its detail', () => {
    for (const status of [400, 404, 409, 500, 503]) {
      assert.doesNotMatch(messageForMenuError(failure(status, 'something_new')), /Detail the page must not show/, String(status));
    }
  });

  it('says the server could not be reached for anything that is not an API answer', () => {
    assert.match(messageForMenuError(new TypeError('network down')), /Could not reach the server/);
  });
});

describe('messageForMenuLoadError', () => {
  it('words the common failures', () => {
    assert.match(messageForMenuLoadError(new ApiError(403, 'x')), /no longer a site admin/);
    assert.match(messageForMenuLoadError(new ApiError(429, 'x')), /Too many/);
    assert.equal(messageForMenuLoadError(new ApiError(500, 'x')), 'The menu could not be loaded.');
    assert.match(messageForMenuLoadError(new Error('offline')), /Could not reach the server/);
  });
});

describe('shouldReloadAfter', () => {
  it('reloads the list when it is out of date, and only then', () => {
    assert.equal(shouldReloadAfter(new ApiError(404, 'x', { code: 'menu_item_not_found' })), true);
    assert.equal(shouldReloadAfter(new ApiError(409, 'x', { code: 'menu_changed' })), true);
    assert.equal(shouldReloadAfter(new ApiError(409, 'x', { code: 'menu_item_locked' })), false);
    assert.equal(shouldReloadAfter(new ApiError(500, 'x')), false);
    assert.equal(shouldReloadAfter(new Error('offline')), false);
  });
});
