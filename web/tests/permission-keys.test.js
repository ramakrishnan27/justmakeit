import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import { hasPermission, permissionKeys } from '../js/core/permission-keys.js';

// The server defines the catalog. This reads its source, so a key added or renamed there fails here until the client follows.
const serverSource = readFileSync(new URL('../../src/Platform.Core/Helpers/Constants/PermissionKeys.cs', import.meta.url), 'utf8');
const serverKeys = [...serverSource.matchAll(/public const string \w+ = "([a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+)";/g)].map((match) => match[1]);

describe('permissionKeys', () => {
  it('finds the keys the server defines', () => {
    assert.equal(serverKeys.length, 16);
  });

  it('is exactly the catalog of the server', () => {
    assert.deepEqual(Object.values(permissionKeys).sort(), [...serverKeys].sort());
  });

  it('cannot be changed by the page', () => {
    assert.ok(Object.isFrozen(permissionKeys));
  });
});

describe('hasPermission', () => {
  const held = ['dashboard.view', 'workspaces.view'];

  it('is true for a key the list holds', () => {
    assert.equal(hasPermission(held, 'workspaces.view'), true);
  });

  it('is false for a key it does not hold', () => {
    assert.equal(hasPermission(held, 'workspaces.create'), false);
  });

  it('matches exactly, never by prefix, case or a wildcard', () => {
    for (const key of ['workspaces', 'Workspaces.View', 'workspaces.view ', 'workspaces.*', '*', '']) {
      assert.equal(hasPermission(held, key), false, key);
    }
  });

  it('fails closed on a list that is missing or is not a list', () => {
    for (const list of [undefined, null, 'workspaces.view', {}, 3, true]) {
      assert.equal(hasPermission(list, 'workspaces.view'), false, String(list));
    }
  });

  it('fails closed on a key that is not text', () => {
    for (const key of [undefined, null, 3, {}, ['workspaces.view']]) {
      assert.equal(hasPermission(held, key), false, String(key));
    }
  });

  it('is false for an empty list', () => {
    assert.equal(hasPermission([], 'dashboard.view'), false);
  });
});
