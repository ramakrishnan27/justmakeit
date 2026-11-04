// The team page's table as plain data: one row for each person and one for each invitation still waiting, the search box
// and the filters. No React here, so the rules are tested without a browser.

import type { BusinessModel, OrganizationRole, Team } from '@/api/types';
import { formatDate } from '@/lib/format';
import { rolesFor, roleTitle } from '@/lib/roles';

export type RowStatus = 'active' | 'invited' | 'expired';

export interface TeamRow {
  /** Unique across both kinds (a person and an invitation could share an id by accident, never a key). */
  key: string;
  kind: 'member' | 'invitation';
  /** The user id of a member, the id of an invitation. */
  id: string;
  /** Empty for an invitation: nobody has told us their name yet. */
  name: string;
  email: string;
  role: OrganizationRole;
  status: RowStatus;
  /** When the person joined, or when the invitation ends (or ended). */
  date: string;
  /** The person looking at the page. Nobody can change or remove themselves. */
  isYou: boolean;
}

const statusTitles: Record<RowStatus, string> = { active: 'Active', invited: 'Invited', expired: 'Expired' };

export const statuses: readonly RowStatus[] = ['active', 'invited', 'expired'];

export function statusTitle(status: RowStatus): string {
  return statusTitles[status];
}

/** What a person is called in a sentence: their name, or their address when they have not given one. */
export function displayName(row: Pick<TeamRow, 'name' | 'email'>): string {
  return row.name.trim().length > 0 ? row.name : row.email;
}

/** The table's rows: the people (by name, you included) and then the waiting invitations (by address). */
export function buildRows(team: Team): TeamRow[] {
  const people: TeamRow[] = team.members.map((member) => ({
    key: `member:${member.userId}`,
    kind: 'member',
    id: member.userId,
    name: member.fullName,
    email: member.email,
    role: member.role,
    status: 'active',
    date: member.joinedAt,
    isYou: member.isYou,
  }));
  const invited: TeamRow[] = team.invitations.map((invitation) => ({
    key: `invitation:${invitation.id}`,
    kind: 'invitation',
    id: invitation.id,
    name: '',
    email: invitation.email,
    role: invitation.role,
    status: invitation.isExpired ? 'expired' : 'invited',
    date: invitation.expiresAt,
    isYou: false,
  }));

  const byName = (a: TeamRow, b: TeamRow) =>
    displayName(a).localeCompare(displayName(b), 'en', { sensitivity: 'base' }) || a.email.localeCompare(b.email);
  return [...people.sort(byName), ...invited.sort((a, b) => a.email.localeCompare(b.email))];
}

/** "Joined Mar 5, 2026", "Expires Mar 12, 2026" or "Expired Mar 1, 2026". */
export function dateText(row: Pick<TeamRow, 'status' | 'date'>): string {
  const lead = row.status === 'active' ? 'Joined' : row.status === 'invited' ? 'Expires' : 'Expired';
  return `${lead} ${formatDate(row.date)}`;
}

export type Filter = { kind: 'role'; value: OrganizationRole } | { kind: 'status'; value: RowStatus };

export const filterId = (filter: Filter) => `${filter.kind}:${filter.value}`;

/** "role is Marketing Manager", "status is Invited": the words on a filter chip. */
export function filterLabel(filter: Filter): string {
  return filter.kind === 'role' ? `role is ${roleTitle(filter.value)}` : `status is ${statusTitle(filter.value)}`;
}

/** Every filter the "+" button can add for this kind of organization: each role it allows, then each status. */
export function allFilters(model: BusinessModel | string): Filter[] {
  return [
    ...rolesFor(model).map((value): Filter => ({ kind: 'role', value })),
    ...statuses.map((value): Filter => ({ kind: 'status', value })),
  ];
}

/**
 * The rows that match the search and the filters. A search matches part of a name, an address or a role's title, in any
 * letter case. Filters of one kind are alternatives (role is X or role is Y); the two kinds must both hold.
 */
export function visibleRows(rows: readonly TeamRow[], search: string, filters: readonly Filter[]): TeamRow[] {
  const needle = search.trim().toLowerCase();
  const roles = filters.filter((filter) => filter.kind === 'role').map((filter) => filter.value);
  const wanted = filters.filter((filter) => filter.kind === 'status').map((filter) => filter.value);

  return rows.filter((row) => {
    if (roles.length > 0 && !roles.includes(row.role)) {
      return false;
    }
    if (wanted.length > 0 && !wanted.includes(row.status)) {
      return false;
    }
    return needle.length === 0 || [row.name, row.email, roleTitle(row.role)].some((text) => text.toLowerCase().includes(needle));
  });
}
