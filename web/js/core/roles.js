// What the member app says about business models and roles. Plain data and functions, so the wording is
// tested without a browser. The server decides what a role may do; the checks here only choose what to show.

import { hasPermission, permissionKeys } from './permission-keys.js';

/** The three kinds of organization, in the order the sign-up form offers them. */
export const businessModels = [
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

const roleTitles = {
  organizationadmin: 'Organization Admin',
  clientmanager: 'Client Manager',
  marketingmanager: 'Marketing Manager',
  contentwriter: 'Content Writer',
};

/** What each role is responsible for, in each kind of organization. */
const responsibilitiesByModel = {
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
const upcomingByRole = {
  organizationadmin: [
    ['Team and permissions', 'Add people and decide what each one may do.'],
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
export function roleTitle(role) {
  if (typeof role !== 'string' || role.length === 0) {
    return '–';
  }
  return Object.hasOwn(roleTitles, role) ? roleTitles[role] : role;
}

/** What the person does in their organization, or null when this client has nothing for that combination. */
export function responsibilities(model, role) {
  const forModel = Object.hasOwn(responsibilitiesByModel, model) ? responsibilitiesByModel[model] : null;
  return forModel && Object.hasOwn(forModel, role) ? forModel[role] : null;
}

/** The areas still to come for a role, as `{ title, text }`. Empty for an unknown role. */
export function upcomingAreas(role) {
  return (Object.hasOwn(upcomingByRole, role) ? upcomingByRole[role] : []).map(([title, text]) => ({ title, text }));
}

/** What to call a workspace in this kind of organization: an agency has one per client. */
export function workspaceWords(model) {
  if (model === 'agency') {
    return { one: 'client workspace', many: 'Client workspaces' };
  }
  if (model === 'creator') {
    return { one: 'workspace', many: 'Your workspace' };
  }
  return { one: 'workspace', many: 'Workspaces' };
}

/** The label of the organization name field while filling in the sign-up form. */
export function organizationNameLabel(model) {
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
export function abilities(permissions) {
  return {
    read: hasPermission(permissions, permissionKeys.dataRead),
    write: hasPermission(permissions, permissionKeys.dataWrite),
    manage: hasPermission(permissions, permissionKeys.organizationManage),
  };
}

/** Whether an organization of this kind may have more than one workspace. */
export function allowsSeveralWorkspaces(model) {
  return model !== 'creator';
}
