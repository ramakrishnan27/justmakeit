import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { createIcon, iconLabel, iconNames, resolveIcon } from '../js/core/icons.js';

// The server decides which icons a menu item may use. This reads its list, so one added there fails here until it is drawn.
const serverSource = readFileSync(new URL('../../src/Platform.Core/Auth/Services/MenuIcons.cs', import.meta.url), 'utf8');
const serverIcons = [...(serverSource.match(/All\s*=\s*\[([^\]]*)\]/s)?.[1] ?? '').matchAll(/"([a-z][a-z0-9-]*)"/g)].map((match) => match[1]);

describe('iconNames', () => {
  it('finds the icons the server accepts', () => {
    assert.equal(serverIcons.length, 14);
  });

  it('are exactly the icons the server accepts, in the same order', () => {
    assert.deepEqual(iconNames, serverIcons);
  });

  it('are lower-case words, the shape the database accepts', () => {
    for (const name of iconNames) {
      assert.match(name, /^[a-z][a-z0-9-]{0,29}$/);
    }
  });
});

describe('resolveIcon', () => {
  it('keeps a name it can draw', () => {
    for (const name of iconNames) {
      assert.equal(resolveIcon(name), name);
    }
  });

  it('falls back to the circle for a name it cannot draw, and for anything that is not text', () => {
    for (const name of ['rocket', 'Home', 'Sparkles', '', 'constructor', 'toString', '__proto__', undefined, null, 5, {}]) {
      assert.equal(resolveIcon(name), 'circle', String(name));
    }
  });
});

describe('iconLabel', () => {
  it('capitalises the name for a list of choices', () => {
    assert.equal(iconLabel('chart'), 'Chart');
    assert.equal(iconLabel('home'), 'Home');
  });

  it('shows a dash for nothing', () => {
    assert.equal(iconLabel(''), '–');
    assert.equal(iconLabel(undefined), '–');
  });
});

describe('createIcon', () => {
  // A very small stand-in for the document: enough to see what is drawn, with no markup ever parsed.
  let original;

  function fakeElement(tag, namespace) {
    return {
      tag,
      namespace,
      attributes: {},
      children: [],
      setAttribute(name, value) {
        this.attributes[name] = value;
      },
      append(child) {
        this.children.push(child);
      },
    };
  }

  beforeEach(() => {
    original = globalThis.document;
    globalThis.document = { createElementNS: (namespace, tag) => fakeElement(tag, namespace) };
  });

  afterEach(() => {
    globalThis.document = original;
  });

  it('draws an svg that assistive technology skips and keyboard focus never enters', () => {
    const svg = createIcon('home');

    assert.equal(svg.tag, 'svg');
    assert.equal(svg.namespace, 'http://www.w3.org/2000/svg');
    assert.equal(svg.attributes['aria-hidden'], 'true');
    assert.equal(svg.attributes.focusable, 'false');
    assert.equal(svg.attributes.viewBox, '0 0 24 24');
    assert.equal(svg.attributes.class, 'icon');
  });

  it('draws every icon with at least one stroke, each an svg path with a drawing', () => {
    for (const name of iconNames) {
      const svg = createIcon(name);
      assert.ok(svg.children.length >= 1, name);
      for (const path of svg.children) {
        assert.equal(path.tag, 'path');
        assert.equal(path.namespace, 'http://www.w3.org/2000/svg');
        assert.match(path.attributes.d, /^[MmLlHhVvCcSsQqTtAaZz0-9\s.,-]+$/, name);
      }
    }
  });

  it('draws the circle for a name it does not know', () => {
    assert.deepEqual(
      createIcon('rocket').children.map((path) => path.attributes.d),
      createIcon('circle').children.map((path) => path.attributes.d),
    );
  });

  it('draws different things for different icons', () => {
    const drawings = iconNames.map((name) => createIcon(name).children.map((path) => path.attributes.d).join('|'));

    assert.equal(new Set(drawings).size, iconNames.length);
  });
});
