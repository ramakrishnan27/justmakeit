// The team page's table as plain data: one row for each person and one for each invitation still waiting, the search box
// and the filters. No React here, so the rules are tested without a browser.

import type { BusinessModel, OrganizationRole, Team } from '@/api/types';
import { formatDate } from '@/lib/format';
import { rolesFor, roleTitle } from '@/lib/roles';

/** A person is active or deactivated; an invitation is waiting or its link has expired. */
export type RowStatus = 'active' | 'inactive' | 'invited' | 'expired';

export interface TeamRow {
  /** Unique across both kinds (a person and an invitation could share an id by accident, never a key). */
  key: string;
  kind: 'member' | 'invitation';
  /** The user id of a member, the id of an invitation. */
  id: string;
  /** What the admin typed for an invitation, or the person's own name. Empty when nobody gave one. */
  name: string;
  email: string;
  role: OrganizationRole;
  status: RowStatus;
  /** When the person was added to the organization, or the invitation was made. */
  createdAt: string;
  /** When the link of an invitation ends (or ended). Null for a person. */
  expiresAt: string | null;
  /** The person looking at the page. Nobody can change, deactivate or delete themselves. */
  isYou: boolean;
}

const statusTitles: Record<RowStatus, string> = { active: 'Active', inactive: 'Inactive', invited: 'Invited', expired: 'Expired' };

export const statuses: readonly RowStatus[] = ['active', 'inactive', 'invited', 'expired'];

export function statusTitle(status: RowStatus): string {
  return statusTitles[status];
}

/** What a person is called in a sentence: their name, or their address when they have not given one. */
export function displayName(row: Pick<TeamRow, 'name' | 'email'>): string {
  return row.name.trim().length > 0 ? row.name : row.email;
}

/** The table's rows: the people (by name, you included) and then the waiting invitations (by name, then address). */
export function buildRows(team: Team): TeamRow[] {
  const people: TeamRow[] = team.members.map((member) => ({
    key: `member:${member.userId}`,
    kind: 'member',
    id: member.userId,
    name: member.fullName,
    email: member.email,
    role: member.role,
    status: member.isActive ? 'active' : 'inactive',
    createdAt: member.joinedAt,
    expiresAt: null,
    isYou: member.isYou,
  }));
  const invited: TeamRow[] = team.invitations.map((invitation) => ({
    key: `invitation:${invitation.id}`,
    kind: 'invitation',
    id: invitation.id,
    name: invitation.fullName,
    email: invitation.email,
    role: invitation.role,
    status: invitation.isExpired ? 'expired' : 'invited',
    createdAt: invitation.createdAt,
    expiresAt: invitation.expiresAt,
    isYou: false,
  }));

  const byName = (a: TeamRow, b: TeamRow) =>
    displayName(a).localeCompare(displayName(b), 'en', { sensitivity: 'base' }) || a.email.localeCompare(b.email);
  return [...people.sort(byName), ...invited.sort(byName)];
}

/** "Mar 5, 2026": the date the person or the invitation was created. */
export function createdText(row: Pick<TeamRow, 'createdAt'>): string {
  return formatDate(row.createdAt);
}

/** "Expires Mar 12, 2026" or "Expired Mar 1, 2026" for an invitation; nothing for a person. */
export function expiryText(row: Pick<TeamRow, 'status' | 'expiresAt'>): string | null {
  if (row.expiresAt === null) {
    return null;
  }
  return `${row.status === 'expired' ? 'Expired' : 'Expires'} ${formatDate(row.expiresAt)}`;
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
