import type { z } from 'zod';

import { API_URL } from '../config';
import { clearSession, getSession, saveSession } from '../auth/session';
import { authResponseSchema, problemSchema } from './schemas';

/** Error with the API's machine code (RFC 9457 problem+json `code`), so screens branch on codes, not strings. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly retryAfterSeconds?: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  /** Client errors are deterministic: retrying them only wastes battery and quota. */
  get isRetryable() {
    return this.status === 0 || this.status === 408 || this.status === 429 || this.status >= 500;
  }
}

interface RequestOptions<S extends z.ZodType> {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  schema?: S;
  /** Attach the bearer token (default true when signed in). */
  auth?: boolean;
  /** Makes a POST safe to retry: the server returns the original result for a repeated key. */
  idempotencyKey?: string;
  signal?: AbortSignal;
  timeoutMs?: number;
}

const DEFAULT_TIMEOUT_MS = 10_000;
const ETAG_CACHE_LIMIT = 200;

/**
 * Conditional-GET cache: url -> (ETag, parsed body). On revalidation the server answers 304 with no body
 * and we reuse the parsed value: list refreshes cost a few hundred bytes instead of the full payload.
 * Only anonymous (public) GETs are stored, so one user's data is never replayed to another.
 */
const etagCache = new Map<string, { etag: string; data: unknown }>();

function remember(url: string, etag: string, data: unknown) {
  if (etagCache.size >= ETAG_CACHE_LIMIT) etagCache.delete(etagCache.keys().next().value!);
  etagCache.set(url, { etag, data });
}

let refreshing: Promise<boolean> | null = null;

/**
 * Single-flight token refresh: ten parallel 401s trigger ONE /auth/refresh. Concurrent refreshes
 * would present the same rotating refresh token twice, which the server treats as theft.
 */
export function refreshAccessToken(): Promise<boolean> {
  refreshing ??= (async () => {
    const session = getSession();
    if (!session) return false;
    try {
      const res = await fetch(`${API_URL}/api/v1/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ refreshToken: session.refreshToken }),
      });
      if (!res.ok) {
        if (res.status === 401) await clearSession(); // refresh token revoked/expired: sign out
        return false;
      }
      await saveSession(authResponseSchema.parse(await res.json()));
      return true;
    } catch {
      return false; // offline: keep the session, try again later
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

function withTimeout(signal: AbortSignal | undefined, ms: number) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new Error('timeout')), ms);
  const onAbort = () => controller.abort(signal?.reason);
  signal?.addEventListener('abort', onAbort);
  return { signal: controller.signal, dispose: () => { clearTimeout(timer); signal?.removeEventListener('abort', onAbort); } };
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export async function api<S extends z.ZodType = z.ZodUnknown>(path: string, options: RequestOptions<S> = {}): Promise<z.infer<S>> {
  const method = options.method ?? 'GET';
  // Retries here are only for idempotent writes; query retries are owned by TanStack Query (one policy, not two stacked).
  const maxAttempts = options.idempotencyKey ? 3 : 1;
  let attempt = 0;
  let refreshed = false;

  for (;;) {
    attempt++;
    try {
      return await once(path, method, options, refreshed);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && options.auth !== false && !refreshed && getSession()) {
        refreshed = true;
        if (await refreshAccessToken()) continue; // retry once with the rotated token
      }
      if (error instanceof ApiError && error.isRetryable && attempt < maxAttempts) {
        // Exponential backoff with full jitter; honour the server's Retry-After when rate limited.
        const base = error.retryAfterSeconds ? error.retryAfterSeconds * 1000 : Math.min(8000, 400 * 2 ** attempt);
        await sleep(Math.random() * base);
        continue;
      }
      throw error;
    }
  }
}

async function once<S extends z.ZodType>(path: string, method: string, options: RequestOptions<S>, afterRefresh: boolean): Promise<z.infer<S>> {
  const url = `${API_URL}/api/v1${path}`;
  let session = getSession();
  // Proactive refresh: don't send a token that expires in-flight.
  if (options.auth !== false && session && !afterRefresh && session.accessTokenExpiresAt - Date.now() < 30_000) {
    await refreshAccessToken();
    session = getSession();
  }
  const useAuth = options.auth !== false && !!session;
  const cacheable = method === 'GET' && !useAuth;
  const cached = cacheable ? etagCache.get(url) : undefined;

  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Correlation-Id': Math.random().toString(36).slice(2) + Date.now().toString(36),
  };
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (useAuth) headers.Authorization = `Bearer ${session!.accessToken}`;
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (cached) headers['If-None-Match'] = cached.etag;

  const timeout = withTimeout(options.signal, options.timeoutMs ?? DEFAULT_TIMEOUT_MS);
  let res: Response;
  try {
    res = await fetch(url, { method, headers, body: options.body === undefined ? undefined : JSON.stringify(options.body), signal: timeout.signal });
  } catch (e) {
    if (options.signal?.aborted) throw e; // caller cancelled (screen unmounted / query superseded): not an error to show
    throw new ApiError(0, 'NETWORK', "Can't reach Medora. Check your connection and try again.");
  } finally {
    timeout.dispose();
  }

  if (res.status === 304 && cached) return cached.data as z.infer<S>;
  if (!res.ok) {
    const problem = problemSchema.safeParse(await res.json().catch(() => ({})));
    const retryAfter = Number(res.headers.get('Retry-After')) || undefined;
    const code = problem.success && problem.data.code ? problem.data.code : res.status === 429 ? 'RATE_LIMITED' : `HTTP_${res.status}`;
    const message = (problem.success && (problem.data.detail ?? problem.data.title)) ||
      (res.status === 429 ? 'Too many requests. Please wait a moment.' : 'Something went wrong. Please try again.');
    throw new ApiError(res.status, code, message, retryAfter);
  }
  if (res.status === 204) return undefined as z.infer<S>;

  const json: unknown = await res.json();
  const data = options.schema ? options.schema.parse(json) : json;
  const etag = res.headers.get('ETag');
  if (cacheable && etag) remember(url, etag, data);
  return data as z.infer<S>;
}

/** Random, collision-resistant idempotency key (one per user intent, reused across that intent's retries). */
export const newIdempotencyKey = () =>
  `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`;
