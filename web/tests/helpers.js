// Shared test doubles: a fake fetch that records requests and answers from a script.

export function jsonResponse(status, body) {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
  });
}

/**
 * A fetch that answers each call with the next response from `answers` (a function gets the request
 * and returns a response, so a test can answer by path). Every call is recorded in `calls`.
 * @param {Array<Response | ((call: { path: string, init: RequestInit }) => Response | Promise<Response>)>} answers
 */
export function fakeFetch(answers) {
  const calls = [];
  const queue = [...answers];

  async function fetchFn(path, init = {}) {
    const call = { path, init, headers: init.headers ?? {} };
    calls.push(call);
    const next = queue.shift();
    if (next === undefined) {
      throw new Error(`Unexpected request: ${init.method ?? 'GET'} ${path}`);
    }
    return typeof next === 'function' ? next(call) : next;
  }

  fetchFn.calls = calls;
  return fetchFn;
}

/** A fetch that answers by path and method, for flows with several calls. */
export function routedFetch(routes) {
  const calls = [];

  async function fetchFn(path, init = {}) {
    const call = { path, init, headers: init.headers ?? {} };
    calls.push(call);
    const key = `${init.method ?? 'GET'} ${path}`;
    const handler = routes[key];
    if (handler === undefined) {
      throw new Error(`Unexpected request: ${key}`);
    }
    return typeof handler === 'function' ? handler(call) : handler;
  }

  fetchFn.calls = calls;
  return fetchFn;
}
