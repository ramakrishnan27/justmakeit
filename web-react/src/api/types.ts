// The shapes the API sends and receives. They mirror the server's DTOs (Platform.Auth/DTOs); the server's JSON is
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
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
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
