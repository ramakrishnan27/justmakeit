import { describe, expect, it } from 'vitest';
import { PERMISSIONS } from '@/access/permissions';
import { readRepositoryFile } from '@/test/repo';
import { adminPages } from './adminPages';
import { pages } from './pages';

// The pages a menu may link to are seeded by the database (005_menus_permissions.sql, and the Team page by 007_team_members.sql); the pages that exist are the
// page table's rows. They must say the same thing, or a menu item would open a page the client does not have, or the
// client would guard a page with another permission than the server's menu does. This reads the seed script and compares.
const script = [
  'src/Platform.Core/Infrastructure/Migrations/005_menus_permissions.sql',
  'src/Platform.Core/Infrastructure/Migrations/007_team_members.sql',
]
  .map(readRepositoryFile)
  .join('\n');

// ('member', 'workspaces',  '/workspaces',  'Workspaces',  'workspaces.view',  'Workspaces', 'folder', 20, false)
const seeded = [...script.matchAll(/\('(member|admin)',\s*'([a-z-]+)',\s*'(\/[^']*)',\s*'[^']*',\s*(NULL|'[^']*')/g)].map((match) => ({
  app: match[1],
  key: match[2],
  path: match[3],
  permission: match[4] === 'NULL' ? undefined : match[4]?.slice(1, -1),
}));
const seededMember = seeded.filter((page) => page.app === 'member');
const seededAdmin = seeded.filter((page) => page.app === 'admin');

describe('the pages the database lets a menu link to', () => {
  it('finds them in the seed script', () => {
    expect(seeded.map((page) => `${page.app}:${page.path}`)).toEqual(['member:/dashboard', 'member:/workspaces', 'admin:/admin', 'admin:/admin/menus', 'admin:/admin/permissions', 'member:/team']);
  });

  it('are all pages this app has (the Site admin pages belong to the Site admin app)', () => {
    for (const page of seededMember) {
      expect(
        pages.some((candidate) => candidate.path === page.path && candidate.key === page.key),
        `${page.path} has no page`,
      ).toBe(true);
    }
  });

  it('are guarded by the same permission here as in the database', () => {
    for (const page of seededMember) {
      expect(pages.find((candidate) => candidate.path === page.path)?.permission, page.path).toBe(page.permission);
    }
  });

  it('are every page this app has: a page without a seed row could never be put in a menu', () => {
    for (const page of pages) {
      expect(
        seededMember.some((candidate) => candidate.path === page.path),
        `${page.path} is not seeded as a menu page`,
      ).toBe(true);
    }
  });
});

describe('the pages of the Site admin app', () => {
  it('are every page this app has: a page without a seed row could never be put in a menu', () => {
    for (const page of adminPages) {
      expect(
        seededAdmin.some((candidate) => candidate.path === page.path && candidate.key === page.key),
        `${page.path} is not seeded as a menu page`,
      ).toBe(true);
    }
  });

  it('name no permission, as in the database: the token of a Site admin carries none', () => {
    expect(seededAdmin.every((page) => page.permission === undefined)).toBe(true);
  });

  it('lack only the editors that are not ported yet (this list shrinks as they are added to adminPages.ts)', () => {
    const missing = seededAdmin.filter((page) => !adminPages.some((candidate) => candidate.path === page.path)).map((page) => page.path);

    expect(missing).toEqual(['/admin/menus', '/admin/permissions']);
  });

  it('have each path and key once', () => {
    expect(new Set(adminPages.map((page) => page.path)).size).toBe(adminPages.length);
    expect(new Set(adminPages.map((page) => page.key)).size).toBe(adminPages.length);
  });
});

describe('the page table', () => {
  it('names a permission for every page, and it is one the catalog has', () => {
    for (const page of pages) {
      expect(Object.values(PERMISSIONS), page.key).toContain(page.permission);
    }
  });

  it('has each path and key once', () => {
    expect(new Set(pages.map((page) => page.path)).size).toBe(pages.length);
    expect(new Set(pages.map((page) => page.key)).size).toBe(pages.length);
  });
});
