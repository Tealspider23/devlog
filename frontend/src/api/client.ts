/**
 * devlog's own API, never anything external. See docs/LLM.md for the
 * separate, unrelated client that will eventually talk to a model — this
 * file only talks to the collector on this machine.
 *
 * The token travels as a header when the page has one of its own (the
 * shipped build, where the collector injects it into the HTML) and is
 * otherwise omitted, relying on the Vite dev proxy to attach it — see
 * vite.config.ts. One code path either way; no build-time branching.
 */

declare global {
  interface Window {
    __DEVLOG_TOKEN__?: string
  }
}

function authHeaders(): HeadersInit {
  const token = window.__DEVLOG_TOKEN__
  return token ? { 'X-Devlog-Token': token } : {}
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

/** Thrown when the request never reached a server at all, so callers can render "collector not running" rather than a generic error. */
export class CollectorUnreachableError extends Error {
  constructor() {
    super('Cannot reach the devlog collector on 127.0.0.1:5111')
    this.name = 'CollectorUnreachableError'
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, {
      ...init,
      headers: { ...authHeaders(), ...init?.headers },
    })
  } catch {
    throw new CollectorUnreachableError()
  }

  if (!response.ok) {
    const body = await response.text().catch(() => '')
    throw new ApiError(response.status, body || response.statusText)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, {
      method: 'POST',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    }),
}
