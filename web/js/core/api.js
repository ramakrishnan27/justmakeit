// The only place that calls fetch. A sign-in is an HttpOnly cookie that the browser sends by itself, so this client
// holds no token and adds no header: scripts on the page can never read the credential. There is no refresh. When a
// request that needs a session comes back 401 the session is over (it ran out after its 30 days, or the Site admin
// changed a permission), the listeners are told, and the page goes back to the sign-in form.

export class ApiError extends Error {
  /**
   * @param {number} status HTTP status.
   * @param {string} message What the API said, or a generic message.
   * @param {{ code?: string | null, errors?: Record<string, string[]> | null }} [details]
   */
  constructor(status, message, { code = null, errors = null } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.errors = errors;
  }
}

/**
 * @param {{ fetchFn?: typeof fetch }} [options]
 */
export function createApiClient({ fetchFn = (...args) => globalThis.fetch(...args) } = {}) {
  const expiredListeners = new Set();

  function send(path, { method = 'GET', body } = {}) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    // same-origin: the browser sends the session cookie, and only to the API that set it.
    return fetchFn(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin',
    });
  }

  async function toError(response) {
    let problem = null;
    try {
      problem = await response.json();
    } catch {
      // not JSON: fall through to the generic message
    }
    const message = (problem && typeof problem.detail === 'string' && problem.detail) || 'The request failed.';
    return new ApiError(response.status, message, { code: problem?.code ?? null, errors: problem?.errors ?? null });
  }

  // A listener that throws must not hide the 401 from the caller, or stop the others from hearing of it.
  function announceExpiry() {
    for (const listener of expiredListeners) {
      try {
        listener();
      } catch {
        // The listener's own problem.
      }
    }
  }

  /**
   * @param {string} path
   * @param {{ method?: string, body?: unknown, auth?: boolean }} [options] auth:false for the calls made outside a
   * session (signing in, signing up, signing out): a 401 there is the answer, not the end of a session.
   */
  async function request(path, { method = 'GET', body, auth = true } = {}) {
    const response = await send(path, { method, body });

    if (response.status === 401 && auth) {
      // Nothing to renew: the session is over. The listeners send the person to sign in; the caller still gets the error.
      announceExpiry();
    }

    if (!response.ok) {
      throw await toError(response);
    }
    return response.status === 204 ? null : response.json();
  }

  return {
    request,
    /** Called when a request that needed a session came back 401: the sign-in has ended. */
    onSessionExpired: (listener) => {
      expiredListeners.add(listener);
      return () => expiredListeners.delete(listener);
    },
  };
}
