// The only place that calls fetch. It keeps the access token in memory (never in storage), adds it to
// each request, and when a request comes back 401 it refreshes the session once and retries.

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
 * @param {{ fetchFn?: typeof fetch, refreshPath?: string }} [options]
 */
export function createApiClient({ fetchFn = (...args) => globalThis.fetch(...args), refreshPath = '/v1/site/refresh' } = {}) {
  /** @type {string | null} */
  let accessToken = null;
  /** @type {Promise<boolean> | null} */
  let refreshInFlight = null;
  const expiredListeners = new Set();
  const refreshedListeners = new Set();

  function send(path, { method = 'GET', body, token = null } = {}) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }

    // same-origin: the refresh cookie travels only to the API that set it.
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

  // A listener that throws must not turn a good refresh into a failed one.
  function announceRefresh() {
    for (const listener of refreshedListeners) {
      try {
        listener();
      } catch {
        // The listener's own problem; the new token is held either way.
      }
    }
  }

  /**
   * Gets a new access token from the refresh cookie. Concurrent callers share one request, because the
   * refresh token rotates: a second request with the same cookie would look like a stolen token.
   * @returns {Promise<boolean>} true when a new access token is now held.
   */
  function refresh() {
    if (!refreshInFlight) {
      refreshInFlight = (async () => {
        try {
          const response = await send(refreshPath, { method: 'POST' });
          if (!response.ok) {
            accessToken = null;
            return false;
          }
          accessToken = (await response.json()).accessToken ?? null;
          if (accessToken !== null) {
            announceRefresh();
          }
          return accessToken !== null;
        } catch {
          accessToken = null;
          return false;
        } finally {
          refreshInFlight = null;
        }
      })();
    }
    return refreshInFlight;
  }

  /**
   * @param {string} path
   * @param {{ method?: string, body?: unknown, auth?: boolean }} [options] auth:false for the sign-in call itself.
   */
  async function request(path, { method = 'GET', body, auth = true } = {}) {
    let response = await send(path, { method, body, token: auth ? accessToken : null });

    if (response.status === 401 && auth) {
      if (await refresh()) {
        response = await send(path, { method, body, token: accessToken });
      } else {
        expiredListeners.forEach((listener) => listener());
      }
    }

    if (!response.ok) {
      throw await toError(response);
    }
    return response.status === 204 ? null : response.json();
  }

  return {
    request,
    refresh,
    getAccessToken: () => accessToken,
    setAccessToken: (token) => {
      accessToken = token;
    },
    /** Called when a request failed with 401 and the session could not be renewed. */
    onSessionExpired: (listener) => {
      expiredListeners.add(listener);
      return () => expiredListeners.delete(listener);
    },
    /**
     * Called each time a refresh gives the page a new access token, whoever asked for it. The new token carries the
     * permissions as they are now, so the session reloads its menu then.
     */
    onTokenRefreshed: (listener) => {
      refreshedListeners.add(listener);
      return () => refreshedListeners.delete(listener);
    },
  };
}
