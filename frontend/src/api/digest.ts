import { api } from './client'
import type { DigestDto } from '../types/api'

/** Same generator as `devlog digest` — see Devlog.Core.Metrics.DigestBuilder. */
export function getDigest(fromIso: string, toIso: string): Promise<DigestDto> {
  return api.get<DigestDto>(`/v1/digest?from=${fromIso}&to=${toIso}`)
}
