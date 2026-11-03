// Reads the claims out of an access token for DISPLAY only. The page never trusts them for anything:
// the API checks the signature and decides what the caller may do.

export function decodeJwtPayload(token: string | null | undefined): Record<string, unknown> | null {
  if (typeof token !== 'string') {
    return null;
  }

  const parts = token.split('.');
  const payload = parts[1];
  if (parts.length !== 3 || !payload) {
    return null;
  }

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));
    const decoded: unknown = JSON.parse(new TextDecoder().decode(bytes));
    return decoded !== null && typeof decoded === 'object' && !Array.isArray(decoded) ? (decoded as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}
