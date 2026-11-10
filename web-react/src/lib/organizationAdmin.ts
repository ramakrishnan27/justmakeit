// The wording of what the Site admin sees when they turn a channel on or off for an organization, or deactivate one. Plain
// functions, so the wording is tested without a browser, and nothing here says more than the API did.

import { ApiError } from '@/api/client';

const offline = 'Could not reach the server. Check your connection and try again.';

/** What to tell the Site admin when changing a channel for an organization failed (the switch has been put back). */
export function messageForOrganizationChannelError(error: unknown, channelName: string): string {
  if (!(error instanceof ApiError)) {
    return offline;
  }

  if (error.code === 'organization_not_found') {
    return 'That organization no longer exists. Reload the list.';
  }
  if (error.code === 'channel_not_found') {
    return `${channelName} no longer exists. Reload the list.`;
  }
  if (error.status === 403) {
    return 'This account is no longer a site admin.';
  }
  if (error.status === 429) {
    return 'Too many requests. Wait a minute, then try again.';
  }
  return `${channelName} could not be changed. Try again in a moment.`;
}

/** What to tell the Site admin when the channels of an organization could not be loaded. */
export function messageForOrganizationChannelsLoadError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return offline;
  }
  if (error.code === 'organization_not_found') {
    return 'That organization no longer exists. Reload the list.';
  }
  if (error.status === 403) {
    return 'This account is no longer a site admin.';
  }
  if (error.status === 429) {
    return 'Too many requests. Wait a minute, then try again.';
  }
  return 'The channels could not be loaded.';
}

/** What to tell the Site admin when deactivating or activating an organization failed. */
export function messageForOrganizationStatusError(error: unknown, name: string, activating: boolean): string {
  if (!(error instanceof ApiError)) {
    return offline;
  }

  if (error.code === 'organization_not_found') {
    return `${name} no longer exists. Reload the list.`;
  }
  if (error.status === 403) {
    return 'This account is no longer a site admin.';
  }
  if (error.status === 429) {
    return 'Too many requests. Wait a minute, then try again.';
  }
  return `${name} could not be ${activating ? 'activated' : 'deactivated'}. Try again in a moment.`;
}

/** What is said after an organization was deactivated or activated. */
export function statusChangedText(name: string, isActive: boolean): string {
  return isActive
    ? `${name} is active again. Its members can sign in.`
    : `${name} was deactivated. Its members were signed out and cannot sign in until you activate it.`;
}
