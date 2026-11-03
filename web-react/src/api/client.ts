// The only place that calls fetch. It keeps the access token in memory (never in storage), adds it to
// each request, and when a request comes back 401 it refreshes the session once and retries.

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly errors: Record<string, string[]> | null;

  /**
   * @param status HTTP status.
   * @param message What the API said, or a generic message.
   */
  constructor(status: number, message: string, details: { code?: string | null; errors?: Record<string, string[]> | null } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = details.code ?? null;
    this.errors = details.errors ?? null;
  }
}

export type FetchFn = (input: string, init?: RequestInit) => Promise<Response>;

export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** false for the sign-in call itself: no token is sent, and a 401 is the answer, not a reason to refresh. */
  auth?: boolean;
}

export interface ApiClient {
  request<T = unknown>(path: string, options?: RequestOptions): Promise<T>;
  /** Gets a new access token from the refresh cookie. Resolves true when a new token is now held. */
  refresh(): Promise<boolean>;
  getAccessToken(): string | null;
  setAccessToken(token: string | null): void;
  /** Called when a request failed with 401 and the session could not be renewed. */
  onSessionExpired(listener: () => void): () => void;
  /**
   * Called each time a refresh gives the page a new access token, whoever asked for it. The new token carries the
   * permissions as they are now, so the session reloads its menu then.
   */
  onTokenRefreshed(listener: () => void): () => void;
}

export function createApiClient({
  fetchFn = (input, init) => globalThis.fetch(input, init),
  refreshPath = '/v1/auth/refresh',
}: { fetchFn?: FetchFn; refreshPath?: string } = {}): ApiClient {
  let accessToken: string | null = null;
  let refreshInFlight: Promise<boolean> | null = null;
  const expiredListeners = new Set<() => void>();
  const refreshedListeners = new Set<() => void>();

  function send(path: string, { method = 'GET', body, token = null }: { method?: string; body?: unknown; token?: string | null } = {}) {
    const headers: Record<string, string> = { Accept: 'application/json' };
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

  async function toError(response: Response) {
    let problem: { detail?: unknown; code?: unknown; errors?: unknown } | null = null;
    try {
      problem = await response.json();
    } catch {
      // not JSON: fall through to the generic message
    }
    const message = (problem && typeof problem.detail === 'string' && problem.detail) || 'The request failed.';
    return new ApiError(response.status, message, {
      code: typeof problem?.code === 'string' ? problem.code : null,
      errors: problem?.errors && typeof problem.errors === 'object' ? (problem.errors as Record<string, string[]>) : null,
    });
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

  // Concurrent callers share one request, because the refresh token rotates: a second request with the same
  // cookie would look like a stolen token.
  function refresh() {
    if (!refreshInFlight) {
      refreshInFlight = (async () => {
        try {
          const response = await send(refreshPath, { method: 'POST' });
          if (!response.ok) {
            accessToken = null;
            return false;
          }
          const body = (await response.json()) as { accessToken?: unknown };
          accessToken = typeof body.accessToken === 'string' ? body.accessToken : null;
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

  async function request<T = unknown>(path: string, { method = 'GET', body, auth = true }: RequestOptions = {}): Promise<T> {
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
    return (response.status === 204 ? null : await response.json()) as T;
  }

  return {
    request,
    refresh,
    getAccessToken: () => accessToken,
    setAccessToken: (token) => {
      accessToken = token;
    },
    onSessionExpired: (listener) => {
      expiredListeners.add(listener);
      return () => expiredListeners.delete(listener);
    },
    onTokenRefreshed: (listener) => {
      refreshedListeners.add(listener);
      return () => refreshedListeners.delete(listener);
    },
  };
}
