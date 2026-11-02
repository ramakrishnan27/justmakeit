// The pages a menu may link to are seeded by the database (005_menus_permissions.sql); the pages that exist are the
// router's routes. They must say the same thing, or a menu item would open a page the client does not have, or the client
// would guard a page with another permission than the server's menu does. This reads the seed script and compares.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import { adminApp, memberApp } from '../js/core/router.js';

const script = readFileSync(new URL('../../src/Platform.Infrastructure/Migrations/005_menus_permissions.sql', import.meta.url), 'utf8');

// ('member', 'workspaces',  '/workspaces',  'Workspaces',  'workspaces.view',  'Workspaces', 'folder', 20, false)
const seededPages = [...script.matchAll(/\('(member|admin)',\s*'([a-z-]+)',\s*'(\/[^']*)',\s*'[^']*',\s*(NULL|'[^']*')/g)].map((match) => ({
  app: match[1],
  key: match[2],
  path: match[3],
  permission: match[4] === 'NULL' ? undefined : match[4].slice(1, -1),
}));

const appOf = { member: memberApp, admin: adminApp };

describe('the pages the database lets a menu link to', () => {
  it('finds them in the seed script', () => {
    assert.equal(seededPages.length, 5);
    assert.deepEqual(
      seededPages.map((page) => `${page.app}:${page.path}`),
      ['member:/dashboard', 'member:/workspaces', 'admin:/admin', 'admin:/admin/menus', 'admin:/admin/permissions'],
    );
  });

  it('are all pages the client has, behind sign-in, in the right app', () => {
    for (const page of seededPages) {
      const route = appOf[page.app].routes.find((candidate) => candidate.path === page.path);
      assert.ok(route, `${page.app} ${page.path} has no route`);
      assert.equal(route.requiresAuth, true, page.path);
    }
  });

  it('are guarded by the same permission in the client as in the database', () => {
    for (const page of seededPages) {
      const route = appOf[page.app].routes.find((candidate) => candidate.path === page.path);
      assert.equal(route.permission, page.permission, `${page.app} ${page.path}`);
    }
  });

  it('are every page behind sign-in that the client has: a page without a seed row could never be put in a menu', () => {
    for (const app of [memberApp, adminApp]) {
      const key = app === memberApp ? 'member' : 'admin';
      for (const route of app.routes.filter((candidate) => candidate.requiresAuth)) {
        assert.ok(
          seededPages.some((page) => page.app === key && page.path === route.path),
          `${key} ${route.path} is not seeded as a menu page`,
        );
      }
    }
  });
});
