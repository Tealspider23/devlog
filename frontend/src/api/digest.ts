import { api } from './client'
import type { DigestDto } from '../types/api'

export interface DigestParams {
  /** 'week' | 'month' — resolved server-side through Devlog.Core.Metrics.CalendarRange, so the frontend never computes the current period itself. */
  range?: 'week' | 'month'
  /** Explicit dates, for previous periods. Override `range` when present. */
  from?: string
  to?: string
  /**
   * Splices Job C's prose into the markdown — a live call to the AI provider.
   * Callers must gate this behind an explicit action; see the "prose must
   * never ride the digest useQuery" rule in the frontend plan.
   */
  prose?: boolean
}

/** Same generator as `devlog digest` — see Devlog.Core.Metrics.DigestBuilder. */
export function getDigest(params: DigestParams, signal?: AbortSignal): Promise<DigestDto> {
  const qs = new URLSearchParams()
  if (params.range) qs.set('range', params.range)
  if (params.from) qs.set('from', params.from)
  if (params.to) qs.set('to', params.to)
  if (params.prose) qs.set('prose', 'true')
  return api.get<DigestDto>(`/v1/digest?${qs.toString()}`, signal)
}
