import { describe, expect, it } from 'vitest';
import { ana, ben, cara, dev, invitation, member, stale, team } from '@/test/team';
import { allFilters, buildRows, createdText, displayName, expiryText, filterId, filterLabel, visibleRows, type Filter } from './teamRows';

const rows = buildRows(team([cara, ana, ben], [stale, dev]));
const emails = (list: readonly { email: string }[]) => list.map((row) => row.email);

describe('buildRows', () => {
  it('makes a row for each person and each invitation: the people by name, then the invitations by address', () => {
    expect(emails(rows)).toEqual(['ana@example.com', 'ben@example.com', 'cara@example.com', 'dev@example.com', 'old@example.com']);
    expect(rows.map((row) => row.kind)).toEqual(['member', 'member', 'member', 'invitation', 'invitation']);
  });

  it('gives each row a key that cannot collide across kinds, even when an id is the same', () => {
    const clash = buildRows({ members: [member('x1', 'Same Id', 'contentwriter')], invitations: [invitation('x1', 'same@example.com', 'contentwriter')] });

    expect(new Set(clash.map((row) => row.key)).size).toBe(2);
  });

  it('marks an invitation whose time has run out as expired, and a live one as invited', () => {
    expect(rows.find((row) => row.email === 'dev@example.com')?.status).toBe('invited');
    expect(rows.find((row) => row.email === 'old@example.com')?.status).toBe('expired');
    expect(rows.find((row) => row.email === 'ben@example.com')?.status).toBe('active');
  });

  it('carries who the person looking at the page is', () => {
    expect(rows.filter((row) => row.isYou).map((row) => row.email)).toEqual(['ana@example.com']);
  });

  it('sorts names without regard to letter case', () => {
    const sorted = buildRows(team([member('a', 'bob Low', 'contentwriter'), member('b', 'Adam High', 'contentwriter'), member('c', 'carl Mid', 'contentwriter')], []));

    expect(sorted.map((row) => row.name)).toEqual(['Adam High', 'bob Low', 'carl Mid']);
  });
});

describe('displayName', () => {
  it('is the name, or the address for an invitation, whose person has not given one', () => {
    expect(displayName({ name: 'Ben Jones', email: 'ben@example.com' })).toBe('Ben Jones');
    expect(displayName({ name: '', email: 'dev@example.com' })).toBe('dev@example.com');
    expect(displayName({ name: '   ', email: 'dev@example.com' })).toBe('dev@example.com');
  });
});

describe('createdText', () => {
  it('is the date the person was added, or the invitation was made', () => {
    expect(createdText({ createdAt: '2026-03-05T10:30:00Z' })).toBe('Mar 5, 2026');
    expect(rows.map((row) => createdText(row))).toEqual(['Mar 5, 2026', 'Mar 5, 2026', 'Apr 1, 2026', 'Nov 1, 2026', 'Nov 1, 2026']);
  });
});

describe('expiryText', () => {
  it('says when an invitation ends or ended, and nothing for a person', () => {
    expect(expiryText({ status: 'invited', expiresAt: '2030-01-15T12:00:00Z' })).toBe('Expires Jan 15, 2030');
    expect(expiryText({ status: 'expired', expiresAt: '2026-10-01T12:00:00Z' })).toBe('Expired Oct 1, 2026');
    expect(expiryText({ status: 'active', expiresAt: null })).toBeNull();
    expect(expiryText({ status: 'inactive', expiresAt: null })).toBeNull();
  });
});

describe('a deactivated person and a named invitation', () => {
  const named = buildRows(
    team([ana, member('u9', 'Zed Zane', 'contentwriter', { isActive: false })], [invitation('i9', 'q@example.com', 'contentwriter', { fullName: 'Aria Quinn' })]),
  );

  it('is Inactive, and stays in the list with their role', () => {
    const zed = named.find((row) => row.name === 'Zed Zane')!;

    expect(zed.status).toBe('inactive');
    expect(zed.role).toBe('contentwriter');
    expect(zed.expiresAt).toBeNull();
  });

  it('shows the name the admin typed for an invitation, and falls back to the address when there is none', () => {
    const aria = named.find((row) => row.kind === 'invitation')!;

    expect(aria.name).toBe('Aria Quinn');
    expect(displayName(aria)).toBe('Aria Quinn');
    expect(displayName(rows.find((row) => row.email === 'dev@example.com')!)).toBe('dev@example.com');
  });

  it('sorts the invitations by name, not by address', () => {
    const unsorted = buildRows(
      team([ana], [invitation('i1', 'a@example.com', 'contentwriter', { fullName: 'Zoe' }), invitation('i2', 'z@example.com', 'contentwriter', { fullName: 'Abe' })]),
    );

    expect(unsorted.filter((row) => row.kind === 'invitation').map((row) => row.name)).toEqual(['Abe', 'Zoe']);
  });

  it('can be filtered by the Inactive status', () => {
    expect(visibleRows(named, '', [{ kind: 'status', value: 'inactive' }]).map((row) => row.name)).toEqual(['Zed Zane']);
  });

  it('is found by searching for the name of an invited person', () => {
    expect(visibleRows(named, 'aria', []).map((row) => row.email)).toEqual(['q@example.com']);
  });
});

describe('visibleRows', () => {
  const role = (value: Filter['value']): Filter => ({ kind: 'role', value: value as 'contentwriter' });
  const status = (value: Filter['value']): Filter => ({ kind: 'status', value: value as 'active' });

  it('shows everything when there is no search and no filter', () => {
    expect(visibleRows(rows, '', [])).toEqual(rows);
    expect(visibleRows(rows, '   ', [])).toEqual(rows);
  });

  it('searches part of a name, an address or a role title, in any letter case', () => {
    expect(emails(visibleRows(rows, 'bEN', []))).toEqual(['ben@example.com']);
    expect(emails(visibleRows(rows, 'example.com', []))).toHaveLength(5);
    expect(emails(visibleRows(rows, 'content writer', []))).toEqual(['cara@example.com', 'dev@example.com']);
    expect(visibleRows(rows, 'nobody', [])).toEqual([]);
  });

  it('treats the search as plain text, not a pattern', () => {
    expect(visibleRows(rows, '.*', [])).toEqual([]);
    expect(visibleRows(rows, '(', [])).toEqual([]);
  });

  it('takes filters of one kind as alternatives', () => {
    expect(emails(visibleRows(rows, '', [role('contentwriter'), role('marketingmanager')]))).toEqual([
      'ben@example.com',
      'cara@example.com',
      'dev@example.com',
      'old@example.com',
    ]);
  });

  it('needs the filters of different kinds, and the search, to hold together', () => {
    expect(emails(visibleRows(rows, '', [role('contentwriter'), status('active')]))).toEqual(['cara@example.com']);
    expect(emails(visibleRows(rows, 'old', [status('expired')]))).toEqual(['old@example.com']);
    expect(visibleRows(rows, 'cara', [status('invited')])).toEqual([]);
  });
});

describe('the filters', () => {
  it('offer each role the organization allows, then each status', () => {
    expect(allFilters('company').map(filterId)).toEqual([
      'role:organizationadmin',
      'role:marketingmanager',
      'role:contentwriter',
      'status:active',
      'status:inactive',
      'status:invited',
      'status:expired',
    ]);
    expect(allFilters('agency').map(filterId)).toContain('role:clientmanager');
    expect(allFilters('company').map(filterId)).not.toContain('role:clientmanager');
  });

  it('are worded as the chips of the design: "role is Marketing Manager", "status is Invited"', () => {
    expect(filterLabel({ kind: 'role', value: 'marketingmanager' })).toBe('role is Marketing Manager');
    expect(filterLabel({ kind: 'status', value: 'invited' })).toBe('status is Invited');
  });
});
