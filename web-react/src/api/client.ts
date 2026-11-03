// The only place that calls fetch. A sign-in is an HttpOnly cookie that the browser sends by itself, so this client
// holds no token and adds no header: scripts on the page can never read the credential. There is no refresh. When a
// request that needs a session comes back 401 the session is over (it expired after its 30 days, or the Site admin
// changed a permission), the listeners are told, and the page goes back to the sign-in form.

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
  /**
   * false for the calls that are not made inside a session (signing in, signing up, signing out): a 401 there is the
   * answer ("wrong password"), not the end of a session.
   */
  auth?: boolean;
}

export interface ApiClient {
  request<T = unknown>(path: string, options?: RequestOptions): Promise<T>;
  /** Called when a request that needed a session came back 401: the sign-in has ended. */
  onSessionExpired(listener: () => void): () => void;
}

export function createApiClient({
  fetchFn = (input, init) => globalThis.fetch(input, init),
}: { fetchFn?: FetchFn } = {}): ApiClient {
  const expiredListeners = new Set<() => void>();

  function send(path: string, { method = 'GET', body }: { method?: string; body?: unknown } = {}) {
    const headers: Record<string, string> = { Accept: 'application/json' };
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

  async function request<T = unknown>(path: string, { method = 'GET', body, auth = true }: RequestOptions = {}): Promise<T> {
    const response = await send(path, { method, body });

    if (response.status === 401 && auth) {
      // Nothing to renew: the session is over. The listeners send the person to sign in; the caller still gets the error.
      announceExpiry();
    }

    if (!response.ok) {
      throw await toError(response);
    }
    return (response.status === 204 ? null : await response.json()) as T;
  }

  return {
    request,
    onSessionExpired: (listener) => {
      expiredListeners.add(listener);
      return () => expiredListeners.delete(listener);
    },
  };
}
