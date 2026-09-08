import type { AskResponseDto } from '../types/api'

export type AskOutcome =
  | { kind: 'answer'; answer: string; toolsUsed: string[]; toolRounds: number; model: string | null }
  | { kind: 'noToolsUsed'; answer: string; toolRounds: number; model: string | null }
  | { kind: 'withheld'; numbers: string[]; message: string }
  | { kind: 'rateLimited'; raw: string }
  | { kind: 'modelMissing'; raw: string }
  | { kind: 'unreachable'; message: string }
  | { kind: 'disabled'; message: string }
  | { kind: 'timeout'; message: string }
  | { kind: 'failed'; message: string }

/**
 * Classifies a raw AskResponseDto into exactly one UI state. Order matters:
 * the withheld case must be checked before the generic HTTP-code parse, or a
 * withheld answer (which carries no HTTP code at all) would fall through to
 * "failed" and lose its distinct, non-error treatment.
 */
export function classifyAskResult(res: AskResponseDto): AskOutcome {
  if (res.success) {
    const answer = res.answer ?? ''
    return res.toolsUsed.length === 0
      ? { kind: 'noToolsUsed', answer, toolRounds: res.toolRounds, model: res.model }
      : { kind: 'answer', answer, toolsUsed: res.toolsUsed, toolRounds: res.toolRounds, model: res.model }
  }

  if (res.unverifiedNumbers.length > 0) {
    return { kind: 'withheld', numbers: res.unverifiedNumbers, message: res.error ?? '' }
  }

  const error = res.error ?? ''
  const httpMatch = error.match(/^HTTP (\d+)/)
  if (httpMatch) {
    const code = httpMatch[1]
    if (code === '429') return { kind: 'rateLimited', raw: error }
    if (code === '404') return { kind: 'modelMissing', raw: error }
    return { kind: 'failed', message: error }
  }

  if (error.includes('not reachable')) return { kind: 'unreachable', message: error }
  if (error.includes('disabled in configuration')) return { kind: 'disabled', message: error }
  if (error.toLowerCase().includes('timed out')) return { kind: 'timeout', message: error }

  return { kind: 'failed', message: error || 'The request failed.' }
}

/** Deduped tool names in first-appearance order, with counts: "metrics ×3 · sessions". */
const TOOL_LABEL: Record<string, string> = {
  getSessions: 'sessions',
  getSessionDetail: 'session detail',
  getCommits: 'commits',
  getMetrics: 'metrics',
  getNarratives: 'narratives',
  getPendingIdentities: 'unclassified identities',
}

export function summariseTools(toolsUsed: string[]): string {
  const counts = new Map<string, number>()
  for (const t of toolsUsed) counts.set(t, (counts.get(t) ?? 0) + 1)
  return [...counts.entries()].map(([t, n]) => `${TOOL_LABEL[t] ?? t}${n > 1 ? ` ×${n}` : ''}`).join(' · ')
}
