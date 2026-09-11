import type { RangeKind } from './range'

/**
 * Not an abstraction, a plain object literal — becomes necessary the moment a
 * narrate run needs to invalidate `narratives` and `session`, the first real
 * use of `useQueryClient` in this codebase.
 */
export const qk = {
  timeline: (date: string) => ['timeline', date] as const,
  session: (id: number) => ['session', id] as const,
  /**
   * Keyed on (kind, offset), not on resolved dates — at offset 0 the dates
   * are computed from `new Date()` at render time, and keying on them would
   * silently change the cache entry's identity across a midnight boundary
   * mid-session. Keyed this way the entry means "the current week", which is
   * what the user means.
   */
  digest: (kind: RangeKind, offset: number) => ['digest', kind, offset] as const,
  digestProse: (kind: RangeKind, offset: number) => ['digest', kind, offset, 'prose'] as const,
  narratives: (from: string, to: string) => ['narratives', from, to] as const,
  weeklyWins: (from: string, to: string) => ['weeklyWins', from, to] as const,
  aiStatus: () => ['ai', 'status'] as const,
  aiModels: () => ['ai', 'models'] as const,
  chatTranscript: () => ['chat', 'transcript'] as const,
}
