// Reads the claims out of an access token for DISPLAY only. The page never trusts them for anything:
// the API checks the signature and decides what the caller may do.

/**
 * @param {string | null | undefined} token
 * @returns {Record<string, unknown> | null} The payload, or null when the token is not a readable JWT.
 */
export function decodeJwtPayload(token) {
  if (typeof token !== 'string') {
    return null;
  }

  const parts = token.split('.');
  if (parts.length !== 3 || parts[1].length === 0) {
    return null;
  }

  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));
    const payload = JSON.parse(new TextDecoder().decode(bytes));
    return payload !== null && typeof payload === 'object' && !Array.isArray(payload) ? payload : null;
  } catch {
    return null;
  }
}
