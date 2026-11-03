// Guards for the frontend rules in CLAUDE.md: tokens only in memory, and no markup built from strings (React escapes
// what it renders; these keep it that way). They scan the source, so a change that breaks a rule fails here before it
// reaches a browser.

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

const appRoot = resolve(process.cwd());
const sourceRoot = join(appRoot, 'src');

function listFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? listFiles(path) : [path];
  });
}

/** Source with comments removed, so a comment that mentions a banned API does not count as using it. */
function code(path: string): string {
  return readFileSync(path, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
}

const isTestFile = (file: string) => /\.test\.tsx?$/.test(file) || file.startsWith(join(sourceRoot, 'test') + sep);
const sources = listFiles(sourceRoot).filter((file) => /\.tsx?$/.test(file) && !isTestFile(file));
const name = (file: string) => relative(appRoot, file);

function expectNone(pattern: RegExp, files = sources) {
  for (const file of files) {
    expect(code(file), name(file)).not.toMatch(pattern);
  }
}

describe('frontend rules', () => {
  it('finds the sources to check', () => {
    expect(sources.length).toBeGreaterThanOrEqual(30);
  });

  it('never writes a token or any session data to browser storage or a script-readable cookie', () => {
    expectNone(/\b(localStorage|sessionStorage|indexedDB|document\.cookie|caches\b|openDatabase)\b/);
  });

  it('never builds markup from strings: no innerHTML, outerHTML, insertAdjacentHTML, document.write or dangerouslySetInnerHTML', () => {
    expectNone(/\b(innerHTML|outerHTML|insertAdjacentHTML|document\.write|createContextualFragment|dangerouslySetInnerHTML)\b/);
  });

  it('never evaluates strings as code', () => {
    expectNone(/\b(eval|new Function)\s*\(|setTimeout\s*\(\s*['"`]|setInterval\s*\(\s*['"`]/);
  });

  it('calls fetch only in the api client', () => {
    expectNone(/\bfetch\s*\(/, sources.filter((file) => file !== join(sourceRoot, 'api', 'client.ts')));
  });

  it('never sets an inline style: styling lives in the stylesheet', () => {
    expectNone(/\bstyle\s*=\s*\{|\.style\b/);
  });

  it('does not import anything from the older web/ app', () => {
    expectNone(/from\s+['"][^'"]*\/web\//);
  });

  it('starts the session before it draws, so a page is never drawn and then replaced by the sign-in form', () => {
    const main = code(join(sourceRoot, 'main.tsx'));
    const restored = main.indexOf('session.restore()');
    const drawn = main.indexOf('.render(');

    expect(restored).toBeGreaterThan(0);
    expect(drawn).toBeGreaterThan(restored);
  });

  it('keeps the two page tables the only places that import a page lazily', () => {
    const tables = [join(sourceRoot, 'navigation', 'pages.ts'), join(sourceRoot, 'navigation', 'adminPages.ts')];
    expectNone(/\blazy\s*\(/, sources.filter((file) => !tables.includes(file)));
  });
});

describe('the page', () => {
  const html = readFileSync(join(appRoot, 'index.html'), 'utf8');

  it('has no inline script or style, which the content security policy would block', () => {
    expect(html).not.toMatch(/<script(?![^>]*\bsrc=)[^>]*>/i);
    expect(html).not.toMatch(/<style[\s>]/i);
    expect(html).not.toMatch(/\sstyle=/i);
    expect(html).not.toMatch(/\son[a-z]+=/i);
  });

  it('ships a content security policy (added to the built page) that allows scripts and styles from its own origin only', () => {
    const config = readFileSync(join(appRoot, 'vite.config.ts'), 'utf8');
    const policy = [...config.matchAll(/^\s*"((?:default|script|style|img|connect|object|base|form)-[a-z]+ [^"]*)",$/gm)].map((match) => match[1]).join('; ');

    expect(policy).toMatch(/default-src 'self'/);
    expect(policy).toMatch(/script-src 'self'(;|$)/);
    expect(policy).toMatch(/style-src 'self'(;|$)/);
    expect(policy).toMatch(/object-src 'none'/);
    expect(policy).not.toMatch(/unsafe-inline|unsafe-eval|\*/);
  });
});
