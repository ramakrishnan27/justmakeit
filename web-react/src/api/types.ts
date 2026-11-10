// The shapes the API sends and receives. They mirror the server's DTOs (Platform.Core/Auth/DTOs); the server's JSON is
// camelCase, and the roles and business models are lower-case words.

export type BusinessModel = 'company' | 'agency' | 'creator';

export type OrganizationRole = 'organizationadmin' | 'clientmanager' | 'marketingmanager' | 'contentwriter';

export interface User {
  id: string;
  email: string;
  fullName: string;
}

/** The organization a token is for. The server's code calls it an agency; anything a person sees says organization. */
export interface Organization {
  id: string;
  name: string;
  businessModel: BusinessModel;
  plan: string;
}

export interface Membership {
  agencyId: string;
  agencyName: string;
  businessModel: BusinessModel;
  role: OrganizationRole;
}

export interface MeResponse {
  user: User;
  agency: Organization;
  role: OrganizationRole;
  memberships: Membership[];
  /** When the sign-in ends and the person has to sign in again (the token's own expiry), as an ISO date. */
  sessionExpiresAt: string | null;
}

/**
 * What signing in answers with. The access token is not here and never reaches a script: the server keeps it in an
 * HttpOnly cookie, and the page learns who is signed in from `me`.
 */
export interface SessionResponse {
  expiresAt: string;
}

export interface Workspace {
  id: string;
  name: string;
  createdAt: string;
}

export interface RegisterDetails {
  email: string;
  password: string;
  fullName: string | null;
  organizationName: string;
  businessModel: BusinessModel;
}

/** Which sign-in methods the server offers besides email and password. */
export interface Providers {
  google: boolean;
}

// --- The Site admin's side: a platform-wide account above every organization (SiteController on the server) ---

export interface SiteMeResponse {
  user: User;
  /** Always `siteadmin`: it is what the access token's role claim carries. */
  role: 'siteadmin';
  /** When the sign-in ends and the Site admin has to sign in again, as an ISO date. */
  sessionExpiresAt: string | null;
}

/** Platform totals. */
export interface SiteOverview {
  agencies: number;
  trialAgencies: number;
  paidAgencies: number;
  users: number;
  workspaces: number;
  memberships: number;
}

/** One organization as the Site admin sees it. */
export interface SiteOrganization {
  id: string;
  name: string;
  businessModel: BusinessModel;
  plan: string;
  createdAt: string;
  members: number;
  workspaces: number;
  /** False when the Site admin has deactivated the organization: nobody in it can sign in. */
  isActive: boolean;
  /** How many channels are enabled for the organization. */
  channels: number;
}

/** One person as the Site admin sees them, with the organization they belong to. A person in two organizations is two of these. */
export interface SiteMember {
  userId: string;
  fullName: string;
  email: string;
  /** The organization (the server's code calls it an agency). */
  agencyId: string;
  agencyName: string;
  businessModel: BusinessModel;
  role: OrganizationRole;
  /** False when an organization admin has deactivated the person: they keep their place but cannot sign in. */
  isActive: boolean;
  /** When the person was added to the organization. */
  joinedAt: string;
}

// --- Channels, managed by the Site admin (SiteChannelsController and SiteOrganizationsController on the server) ---

/** A channel as the Site admin's list shows it. Its icon is a built-in glyph (`iconKey`) or an uploaded image (`hasIconImage`). */
export interface SiteChannel {
  id: string;
  name: string;
  description: string;
  /** The built-in icon the app draws, or null when the channel has an uploaded image. */
  iconKey: string | null;
  hasIconImage: boolean;
  /** Who created it, or null for one of the built-in channels. */
  createdByName: string | null;
  createdAt: string;
  /** How many organizations have the channel enabled. */
  organizationCount: number;
}

/** A channel as one organization sees it: whether the Site admin has turned it on for them. */
export interface OrganizationChannel {
  id: string;
  name: string;
  description: string;
  iconKey: string | null;
  hasIconImage: boolean;
  isEnabled: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// --- The team: the people in an organization and the invitations still waiting (MembersController on the server) ---

/** A person in the organization. */
export interface TeamMember {
  userId: string;
  email: string;
  fullName: string;
  role: OrganizationRole;
  /** When the person was added to the organization (the date shown as "created"). */
  joinedAt: string;
  /** True for the person looking at the page: nobody can change or remove themselves. */
  isYou: boolean;
  /** False when an admin has deactivated the person: they keep their place but cannot sign in. */
  isActive: boolean;
}

/** An invitation nobody has accepted yet. The secret of its link is not here: the server keeps only a hash of it. */
export interface Invitation {
  id: string;
  /** The name the admin typed. Empty for an invitation made before names were asked for. */
  fullName: string;
  email: string;
  role: OrganizationRole;
  createdAt: string;
  expiresAt: string;
  isExpired: boolean;
}

export interface Team {
  members: TeamMember[];
  invitations: Invitation[];
}

/** A new invitation. `token` is the secret for the link, and this answer is the only time the server sends it. */
export interface CreatedInvitation {
  invitation: Invitation;
  token: string;
}

/** What the invitation page shows before the person chooses a name and password. */
export interface InvitationPreview {
  organizationName: string;
  businessModel: BusinessModel;
  role: OrganizationRole;
  email: string;
  expiresAt: string;
  /** The name the admin typed, offered on the form. Empty for an older invitation. */
  fullName: string;
}

export interface AcceptInvitationDetails {
  token: string;
  fullName: string;
  password: string;
}
