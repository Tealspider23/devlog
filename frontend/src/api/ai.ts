import { api } from './client'
import type {
  AiModelsDto,
  AiStatusDto,
  AskResponseDto,
  NarrateRequestDto,
  NarrateResultDto,
  NarrativeDto,
} from '../types/api'

/** Runs a live provider probe (up to two endpoints, 10s connect timeout) — not a cheap read, see hooks/useAiStatus.ts. */
export function getAiStatus(signal?: AbortSignal): Promise<AiStatusDto> {
  return api.get<AiStatusDto>('/v1/ai/status', signal)
}

/** Returns `{models: []}` on any failure — indistinguishable from success-with-zero. Callers must consult AiStatusDto to tell the two apart. */
export function getAiModels(signal?: AbortSignal): Promise<AiModelsDto> {
  return api.get<AiModelsDto>('/v1/ai/models', signal)
}

/**
 * Always 200, even on failure — Job G withholds an answer rather than risk a
 * wrong number, and that is reported as `success:false`, not an HTTP error.
 * A signal here stops the browser waiting ("Stop"); it does not stop the
 * model calls already issued to the provider — the UI must say so.
 */
export function ask(question: string, model?: string, signal?: AbortSignal): Promise<AskResponseDto> {
  return api.post<AskResponseDto>('/v1/ask', { question, model }, signal)
}

export function getNarratives(fromIso: string, toIso: string, signal?: AbortSignal): Promise<NarrativeDto[]> {
  return api.get<NarrativeDto[]>(`/v1/narratives?from=${fromIso}&to=${toIso}`, signal)
}

/**
 * One model call per session, sequential — ships window titles, commit
 * messages and branch names to the provider. Defaults the body to `{}` so
 * the empty-body-fails-binding trap can never be hit from the UI. Never
 * pass a signal — see `ask` above.
 */
export function narrate(req: NarrateRequestDto = {}): Promise<NarrateResultDto> {
  return api.post<NarrateResultDto>('/v1/narrate', req)
}
