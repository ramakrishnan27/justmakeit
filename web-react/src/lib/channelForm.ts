// The rules of the new-channel form and the words it uses. They repeat what the server enforces (FieldRules and
// ChannelIconRules on the server; a test reads that source and compares the limits), so a person hears about a problem
// before anything is sent. The server still checks everything: the type of an icon is read from the file's own bytes there.

import { ApiError } from '@/api/client';
import { type FieldProblem } from '@/lib/messages';

export const channelNameMax = 60;
export const channelDescriptionMax = 300;
export const channelIconMaxBytes = 100 * 1024;

/** The image types an icon may have. SVG and GIF are left out on purpose (an SVG can carry script). */
export const channelIconTypes: readonly string[] = ['image/png', 'image/jpeg', 'image/webp'];

/** What the file picker offers, built from the types above. */
export const channelIconAccept = channelIconTypes.join(',');

const iconTypeMessage = 'The icon must be a PNG, JPEG or WebP image.';
const iconSizeMessage = `The icon must be at most ${channelIconMaxBytes / 1024} KB.`;

/** The part of a chosen file the form checks. */
export interface ChosenIcon {
  type: string;
  size: number;
}

/** Checks the new-channel form before anything is sent. @returns The first problem found, or null. */
export function validateChannelForm(name: string, description: string, icon: ChosenIcon | null): FieldProblem | null {
  if (name.trim().length === 0) {
    return { field: 'name', message: 'Enter a name for the channel.' };
  }
  if (name.trim().length > channelNameMax) {
    return { field: 'name', message: `The name must be at most ${channelNameMax} characters.` };
  }
  if (description.trim().length > channelDescriptionMax) {
    return { field: 'description', message: `The description must be at most ${channelDescriptionMax} characters.` };
  }
  return icon ? validateChannelIcon(icon) : null;
}

/** Checks a chosen icon file on its own (when it is picked, before the rest of the form is filled in). */
export function validateChannelIcon(icon: ChosenIcon): FieldProblem | null {
  if (!channelIconTypes.includes(icon.type)) {
    return { field: 'icon', message: iconTypeMessage };
  }
  if (icon.size === 0) {
    return { field: 'icon', message: 'The icon file is empty.' };
  }
  if (icon.size > channelIconMaxBytes) {
    return { field: 'icon', message: iconSizeMessage };
  }
  return null;
}

/** The field of the form a problem belongs to; `form` is anything that belongs to none of them. */
export type ChannelFormField = 'name' | 'description' | 'icon' | 'form';

export interface ChannelFormProblem {
  field: ChannelFormField;
  message: string;
}

const serverFields: readonly Exclude<ChannelFormField, 'form'>[] = ['name', 'description', 'icon'];

/** What to tell the Site admin when a channel could not be created, and which field to put it under. */
export function messageForChannelError(error: unknown): ChannelFormProblem {
  if (!(error instanceof ApiError)) {
    return { field: 'form', message: 'Could not reach the server. Check your connection and try again.' };
  }

  if (error.code === 'channel_exists') {
    return { field: 'name', message: 'A channel with that name already exists.' };
  }

  if (error.status === 400 && error.errors) {
    for (const field of serverFields) {
      const message = error.errors[field]?.[0];
      if (typeof message === 'string') {
        return { field, message };
      }
    }
  }

  if (error.status === 413) {
    return { field: 'icon', message: iconSizeMessage };
  }
  if (error.status === 403) {
    return { field: 'form', message: 'This account is no longer a site admin.' };
  }
  if (error.status === 429) {
    return { field: 'form', message: 'Too many requests. Wait a minute, then try again.' };
  }
  return { field: 'form', message: 'The channel could not be added. Try again in a moment.' };
}

/** The address an uploaded icon is served from. */
export function channelIconUrl(channelId: string): string {
  return `/v1/site/channels/${encodeURIComponent(channelId)}/icon`;
}

/** The address an uploaded icon is served from for a member (the Platform page); the Site admin's own page uses channelIconUrl. */
export function platformIconUrl(channelId: string): string {
  return `/v1/platforms/${encodeURIComponent(channelId)}/icon`;
}
