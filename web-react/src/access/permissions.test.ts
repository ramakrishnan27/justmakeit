import { describe, expect, it } from 'vitest';
import { readRepositoryFile } from '@/test/repo';
import { hasPermission, PERMISSIONS, satisfies, type PermissionKey } from './permissions';

// The server defines the catalog. This reads its source, so a key added or renamed there fails here until the client follows.
const serverSource = readRepositoryFile('src/Platform.Helpers/Constants/PermissionKeys.cs');
const serverKeys = [...serverSource.matchAll(/public const string \w+ = "([a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+)";/g)].map((match) => match[1]);

describe('PERMISSIONS', () => {
  it('finds the keys the server defines', () => {
    expect(serverKeys).toHaveLength(7);
  });

  it('is exactly the catalog of the server', () => {
    expect(Object.values(PERMISSIONS).sort()).toEqual([...serverKeys].sort());
  });

  it('cannot be changed by the page', () => {
    expect(Object.isFrozen(PERMISSIONS)).toBe(true);
  });
});

describe('hasPermission', () => {
  const held = ['dashboard.view', 'workspaces.view'];

  it('is true for a key the list holds, false for one it does not', () => {
    expect(hasPermission(held, 'workspaces.view')).toBe(true);
    expect(hasPermission(held, 'workspaces.create')).toBe(false);
  });

  it('matches exactly, never by prefix, case or a wildcard', () => {
    for (const key of ['workspaces', 'Workspaces.View', 'workspaces.view ', 'workspaces.*', '*', '']) {
      expect(hasPermission(held, key), key).toBe(false);
    }
  });

  it('fails closed on a list that is missing or is not a list', () => {
    for (const list of [undefined, null, 'workspaces.view', {}, 3, true]) {
      expect(hasPermission(list as unknown as string[], 'workspaces.view'), String(list)).toBe(false);
    }
  });

  it('fails closed on a key that is not text', () => {
    for (const key of [undefined, null, 3, {}, ['workspaces.view']]) {
      expect(hasPermission(held, key as unknown as string), String(key)).toBe(false);
    }
  });

  it('is false for an empty list', () => {
    expect(hasPermission([], 'dashboard.view')).toBe(false);
  });
});

describe('satisfies', () => {
  const held = [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.dataRead];
  const missing: PermissionKey = PERMISSIONS.organizationManage;

  it('permission: holds exactly that one', () => {
    expect(satisfies(held, { permission: PERMISSIONS.dataRead })).toBe(true);
    expect(satisfies(held, { permission: missing })).toBe(false);
  });

  it('anyOf: at least one of them', () => {
    expect(satisfies(held, { anyOf: [missing, PERMISSIONS.dataRead] })).toBe(true);
    expect(satisfies(held, { anyOf: [missing, PERMISSIONS.dataWrite] })).toBe(false);
  });

  it('allOf: every one of them', () => {
    expect(satisfies(held, { allOf: [PERMISSIONS.dataRead, PERMISSIONS.workspacesView] })).toBe(true);
    expect(satisfies(held, { allOf: [PERMISSIONS.dataRead, missing] })).toBe(false);
  });

  it('combines everything given with AND', () => {
    expect(satisfies(held, { permission: PERMISSIONS.dataRead, anyOf: [PERMISSIONS.dashboardView] })).toBe(true);
    expect(satisfies(held, { permission: PERMISSIONS.dataRead, anyOf: [missing] })).toBe(false);
  });

  it('allows nothing when the rule asks for nothing, or for an empty list', () => {
    expect(satisfies(held, {})).toBe(false);
    expect(satisfies(held, { anyOf: [] })).toBe(false);
    expect(satisfies(held, { allOf: [] })).toBe(false);
  });

  it('allows nothing to a person with no permission list', () => {
    expect(satisfies(null, { permission: PERMISSIONS.dashboardView })).toBe(false);
    expect(satisfies(undefined, { anyOf: [PERMISSIONS.dashboardView] })).toBe(false);
  });
});
