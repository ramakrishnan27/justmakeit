import type { Invitation, Team, TeamMember } from '@/api/types';
import { PERMISSIONS } from '@/access/permissions';
import { jsonResponse, type Handler } from './helpers';
import { dashboardItem, memberServer, renderApp, teamItem, workspacesItem, type MemberOptions } from './renderApp';

// People and invitations for the team page tests.
export const member = (userId: string, fullName: string, role: TeamMember['role'], overrides: Partial<TeamMember> = {}): TeamMember => ({
  userId,
  email: `${fullName.split(' ')[0]?.toLowerCase()}@example.com`,
  fullName,
  role,
  joinedAt: '2026-03-05T10:30:00Z',
  isYou: false,
  isActive: true,
  ...overrides,
});

export const invitation = (id: string, email: string, role: Invitation['role'], overrides: Partial<Invitation> = {}): Invitation => ({
  id,
  // An invitation made before names were asked for has none; a test that wants one passes it.
  fullName: '',
  email,
  role,
  createdAt: '2026-11-01T10:00:00Z',
  expiresAt: '2030-01-15T12:00:00Z',
  isExpired: false,
  ...overrides,
});

export const ana = member('u2', 'Ana Smith', 'organizationadmin', { isYou: true });
export const ben = member('u3', 'Ben Jones', 'marketingmanager');
export const cara = member('u4', 'Cara Lee', 'contentwriter', { joinedAt: '2026-04-01T09:00:00Z' });
export const dev = invitation('i1', 'dev@example.com', 'contentwriter');
export const stale = invitation('i2', 'old@example.com', 'marketingmanager', { expiresAt: '2026-10-01T12:00:00Z', isExpired: true });

export const manager: MemberOptions = {
  role: 'organizationadmin',
  permissions: [PERMISSIONS.dashboardView, PERMISSIONS.workspacesView, PERMISSIONS.membersManage],
  items: [dashboardItem, workspacesItem, teamItem],
};

export const team = (members: TeamMember[] = [ana, ben, cara], invitations: Invitation[] = [dev, stale]): Team => ({ members, invitations });

/** Opens the team page as a signed-in member, with the server answering `GET /v1/members` with this team. */
export function openTeam(options: MemberOptions = manager, data: Team = team(), extra: Record<string, Handler> = {}) {
  return renderApp('/team', memberServer(options, { 'GET /v1/members': () => jsonResponse(200, data), ...extra }));
}
