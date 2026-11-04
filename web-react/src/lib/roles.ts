// What the member app says about business models and roles. Plain data and functions, so the wording is
// tested without a browser. The server decides what a role may do; the checks here only choose what to show.

import { hasPermission, PERMISSIONS } from '@/access/permissions';
import type { BusinessModel, OrganizationRole } from '@/api/types';

export interface BusinessModelChoice {
  value: BusinessModel;
  title: string;
  description: string;
}

/** The three kinds of organization, in the order the sign-up form offers them. */
export const businessModels: readonly BusinessModelChoice[] = [
  {
    value: 'company',
    title: 'Company',
    description: 'One company with its own team. Roles: Organization Admin, Marketing Manager and Content Writer.',
  },
  {
    value: 'agency',
    title: 'Marketing agency',
    description: 'An agency that serves several clients, with a workspace for each. Adds the Client Manager role.',
  },
  {
    value: 'creator',
    title: 'Individual creator',
    description: 'Just you, with one account and one workspace. You work as the Content Writer.',
  },
];

const roleTitles: Record<OrganizationRole, string> = {
  organizationadmin: 'Organization Admin',
  clientmanager: 'Client Manager',
  marketingmanager: 'Marketing Manager',
  contentwriter: 'Content Writer',
};

/** What each role is responsible for, in each kind of organization. */
const responsibilitiesByModel: Record<BusinessModel, Partial<Record<OrganizationRole, string>>> = {
  company: {
    organizationadmin: 'Creates and manages the company workspace, users, permissions and social accounts.',
    marketingmanager: 'Plans campaigns, manages marketing strategy, reviews content and manages publishing schedules.',
    contentwriter: 'Generates content using AI, writes captions, creates post ideas and submits content for approval.',
  },
  agency: {
    organizationadmin: 'Manages agency workspace, employees, client accounts, permissions and integrations.',
    clientmanager: 'Manages individual client relationships, requirements, feedback and client approvals.',
    marketingmanager: 'Plans campaigns, assigns content tasks, reviews quality and oversees publishing.',
    contentwriter: 'Creates AI-generated and manually written content for assigned clients.',
  },
  creator: {
    contentwriter: 'Manages content ideas, AI generation, captions, editing, approval and personal publishing.',
  },
};

/** The areas a role will work in once they are built. None of them exist yet; the dashboard says so. */
const upcomingByRole: Record<OrganizationRole, ReadonlyArray<readonly [string, string]>> = {
  organizationadmin: [
    ['Connected accounts', 'Connect the accounts your organization publishes to.'],
  ],
  clientmanager: [
    ['Client requirements', 'Keep what each client needs in one place.'],
    ['Feedback and approvals', 'Collect client feedback and their approval of content.'],
  ],
  marketingmanager: [
    ['Campaign planning', 'Plan campaigns and assign the content they need.'],
    ['Review and publishing', 'Review content and manage publishing schedules.'],
  ],
  contentwriter: [
    ['Content ideas and writing', 'Write captions and post ideas, with AI help.'],
    ['Submit for approval', 'Send finished content on to be approved.'],
  ],
};

/** A role from the API (`organizationadmin`) as a title. A role this client does not know is shown as given. */
export function roleTitle(role: string | null | undefined): string {
  if (typeof role !== 'string' || role.length === 0) {
    return '–';
  }
  return Object.hasOwn(roleTitles, role) ? (roleTitles[role as OrganizationRole] ?? role) : role;
}

/** What the person does in their organization, or null when this client has nothing for that combination. */
export function responsibilities(model: string | null | undefined, role: string | null | undefined): string | null {
  const forModel = typeof model === 'string' && Object.hasOwn(responsibilitiesByModel, model) ? responsibilitiesByModel[model as BusinessModel] : null;
  return forModel && typeof role === 'string' && Object.hasOwn(forModel, role) ? (forModel[role as OrganizationRole] ?? null) : null;
}

export interface UpcomingArea {
  title: string;
  text: string;
}

/** The areas still to come for a role. Empty for an unknown role. */
export function upcomingAreas(role: string | null | undefined): UpcomingArea[] {
  const areas = typeof role === 'string' && Object.hasOwn(upcomingByRole, role) ? upcomingByRole[role as OrganizationRole] : [];
  return areas.map(([title, text]) => ({ title, text }));
}

/**
 * The roles an organization of this kind may give its people, in the order the invite form offers them. It is the
 * server's BusinessModelRules.AllowedRoles; a test reads that source and compares. A kind this client does not know, and an
 * individual creator (who works alone), get none.
 */
const rolesByModel: Record<BusinessModel, readonly OrganizationRole[]> = {
  company: ['organizationadmin', 'marketingmanager', 'contentwriter'],
  agency: ['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'],
  creator: ['contentwriter'],
};

export function rolesFor(model: string | null | undefined): readonly OrganizationRole[] {
  return typeof model === 'string' && Object.hasOwn(rolesByModel, model) ? rolesByModel[model as BusinessModel] : [];
}

/** Whether an organization of this kind has a team to manage: an individual creator works alone. */
export function hasTeam(model: string | null | undefined): boolean {
  return model === 'company' || model === 'agency';
}

/** What to call a workspace in this kind of organization: an agency has one per client. */
export function workspaceWords(model: string | null | undefined): { one: string; many: string } {
  if (model === 'agency') {
    return { one: 'client workspace', many: 'Client workspaces' };
  }
  if (model === 'creator') {
    return { one: 'workspace', many: 'Your workspace' };
  }
  return { one: 'workspace', many: 'Workspaces' };
}

/** The label of the organization name field while filling in the sign-up form. */
export function organizationNameLabel(model: string | null | undefined): string {
  if (model === 'company') {
    return 'Company name';
  }
  if (model === 'agency') {
    return 'Agency name';
  }
  if (model === 'creator') {
    return 'Your name, brand or channel';
  }
  return 'Organization name';
}

/**
 * What a person may do, for choosing what to show, from the permissions their access token carries (the list the
 * session holds). The three baselines are the ones behind the server's CanRead, CanWrite and CanManage; the Site admin
 * decides which role holds them. The server still checks every request, so a wrong guess here can only hide or show
 * a button. A list that is missing allows nothing.
 */
export function abilities(permissions: readonly string[] | null | undefined): { read: boolean; write: boolean; manage: boolean } {
  return {
    read: hasPermission(permissions, PERMISSIONS.dataRead),
    write: hasPermission(permissions, PERMISSIONS.dataWrite),
    manage: hasPermission(permissions, PERMISSIONS.organizationManage),
  };
}

/** Whether an organization of this kind may have more than one workspace. */
export function allowsSeveralWorkspaces(model: string | null | undefined): boolean {
  return model !== 'creator';
}
