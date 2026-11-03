// The browser side of Continue with Google. There is no script from Google on the page: the buttons are plain
// links to our own server, which sends the browser to Google and brings it back signed in. These are the
// addresses, the error code Google's return leaves behind, and the question of whether the server has Google set up.

import type { ApiClient } from '@/api/client';
import type { Providers } from '@/api/types';

/** The address the Google button on the sign-in form points at. */
export const googleLoginPath = '/v1/auth/google/start?intent=login';

/** The address the Google button on the sign-up form goes to: the organization chosen so far travels with it. */
export function googleRegisterPath({ businessModel, organizationName }: { businessModel: string; organizationName: string }): string {
  const params = new URLSearchParams({ intent: 'register', businessModel, organizationName: organizationName.trim() });
  return `/v1/auth/google/start?${params}`;
}

/**
 * Reads the error code the server left in the address when it sent the person back (`?error=google_denied`), and
 * the rest of the query without it, so the address can be cleaned and a reload does not show the message again.
 */
export function splitErrorCode(search: string): { code: string | null; rest: string } {
  const params = new URLSearchParams(search);
  const code = params.get('error');
  params.delete('error');
  const rest = params.toString();
  return { code, rest: rest ? `?${rest}` : '' };
}

/** Which sign-in methods the server offers. A server that does not answer simply offers email and password. */
export async function loadProviders(api: ApiClient): Promise<Providers> {
  try {
    const providers = await api.request<{ google?: unknown } | null>('/v1/auth/providers', { auth: false });
    return { google: providers?.google === true };
  } catch {
    return { google: false };
  }
}
