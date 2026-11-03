// Shared test doubles: a fake fetch that answers by path and method and records every call, and a JWT builder.

import type { FetchFn } from '@/api/client';

export function jsonResponse(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
  });
}

export interface RecordedCall {
  path: string;
  init: RequestInit;
  headers: Record<string, string>;
}

export type Handler = Response | ((call: RecordedCall) => Response | Promise<Response>);

export interface RoutedFetch extends FetchFn {
  calls: RecordedCall[];
}

/** A fetch that answers by "METHOD /path". A handler may be a response, or a function that makes one (a body can be read once). */
export function routedFetch(routes: Record<string, Handler>): RoutedFetch {
  const calls: RecordedCall[] = [];

  const fetchFn: FetchFn = async (path, init = {}) => {
    const call: RecordedCall = { path, init, headers: (init.headers ?? {}) as Record<string, string> };
    calls.push(call);
    const key = `${init.method ?? 'GET'} ${path}`;
    const handler = routes[key];
    if (handler === undefined) {
      throw new Error(`Unexpected request: ${key}`);
    }
    return typeof handler === 'function' ? handler(call) : handler.clone();
  };

  return Object.assign(fetchFn, { calls });
}

/** An unsigned-looking JWT with the given payload, for display tests. */
export function makeJwt(payload: Record<string, unknown>): string {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(payload)}.signature`;
}

export function requestLine(call: RecordedCall): string {
  return `${call.init.method ?? 'GET'} ${call.path}`;
}
