// The browser side of Continue with Google. There is no script from Google on the page: the buttons are plain
// links to our own server, which sends the browser to Google and brings it back signed in. These are the
// addresses, the error code Google's return leaves behind, and the question of whether the server has Google set up.

/** The address the Google button on the sign-in form points at. */
export const googleLoginPath = '/v1/auth/google/start?intent=login';

/** The address the Google button on the sign-up form goes to: the organization chosen so far travels with it. */
export function googleRegisterPath({ businessModel, organizationName }) {
  const params = new URLSearchParams({ intent: 'register', businessModel, organizationName: organizationName.trim() });
  return `/v1/auth/google/start?${params}`;
}

/**
 * Reads the error code the server left in the address when it sent the person back (`?error=google_denied`),
 * and removes it from the address so a reload does not show it again. Other query parts are kept.
 * @param {{ location: { pathname: string, search: string }, history: { replaceState: Function } }} [win]
 */
export function takeErrorCode(win = window) {
  const params = new URLSearchParams(win.location.search);
  const code = params.get('error');
  if (code !== null) {
    params.delete('error');
    const rest = params.toString();
    win.history.replaceState({}, '', win.location.pathname + (rest ? `?${rest}` : ''));
  }
  return code;
}

/** Which sign-in methods the server offers. A server that does not answer simply offers email and password. */
export async function loadProviders(api) {
  try {
    const providers = await api.request('/v1/auth/providers', { auth: false });
    return { google: providers?.google === true };
  } catch {
    return { google: false };
  }
}
