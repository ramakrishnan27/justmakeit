// Guards for the frontend rules in CLAUDE.md: tokens only in memory, and no innerHTML with API data.
// They scan the source, so a change that breaks a rule fails here before it reaches a browser.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const webRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

function listFiles(directory, extension) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? listFiles(path, extension) : path.endsWith(extension) ? [path] : [];
  });
}

/** Source with comments removed, so a comment that mentions a banned API does not count as using it. */
function code(path) {
  return readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
}

const scripts = listFiles(join(webRoot, 'js'), '.js');

describe('frontend rules', () => {
  it('finds the scripts to check', () => {
    assert.ok(scripts.length >= 8, `found only ${scripts.length}`);
  });

  it('never writes a token or any session data to browser storage or a script-readable cookie', () => {
    const banned = /\b(localStorage|sessionStorage|indexedDB|document\.cookie|caches\b|openDatabase)\b/;
    for (const script of scripts) {
      assert.doesNotMatch(code(script), banned, relative(webRoot, script));
    }
  });

  it('never builds markup from strings: no innerHTML, outerHTML, insertAdjacentHTML or document.write', () => {
    const banned = /\b(innerHTML|outerHTML|insertAdjacentHTML|document\.write|createContextualFragment)\b/;
    for (const script of scripts) {
      assert.doesNotMatch(code(script), banned, relative(webRoot, script));
    }
  });

  it('never evaluates strings as code', () => {
    const banned = /\b(eval|new Function)\s*\(|setTimeout\s*\(\s*['"`]|setInterval\s*\(\s*['"`]/;
    for (const script of scripts) {
      assert.doesNotMatch(code(script), banned, relative(webRoot, script));
    }
  });

  it('calls fetch only in the api client', () => {
    for (const script of scripts.filter((file) => !file.endsWith(join('core', 'api.js')))) {
      assert.doesNotMatch(code(script), /\bfetch\s*\(/, relative(webRoot, script));
    }
  });

  it('starts the router only after the session has settled, so a page renders once and reads one-time values once', () => {
    const main = code(join(webRoot, 'js', 'main.js'));
    const restored = main.indexOf('await session.restore()');
    const created = main.indexOf('createRouter(');

    assert.ok(restored > 0 && created > 0, 'main.js restores the session and creates the router');
    assert.ok(restored < created, 'the router is created after the session is restored');
  });

  it('the page ships a content security policy that allows scripts and styles from its own origin only', () => {
    const html = readFileSync(join(webRoot, 'index.html'), 'utf8');
    const policy = html.match(/Content-Security-Policy"\s+content="([^"]+)"/)?.[1] ?? '';

    assert.match(policy, /default-src 'self'/);
    assert.match(policy, /script-src 'self'(;|$)/);
    assert.match(policy, /style-src 'self'(;|$)/);
    assert.doesNotMatch(policy, /unsafe-inline|unsafe-eval|\*/);
  });

  it('the page has no inline script or style, which that policy would block', () => {
    const html = readFileSync(join(webRoot, 'index.html'), 'utf8');

    assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/i);
    assert.doesNotMatch(html, /<style[\s>]/i);
    assert.doesNotMatch(html, /\sstyle=/i);
    assert.doesNotMatch(html, /\son[a-z]+=/i);
  });
});
