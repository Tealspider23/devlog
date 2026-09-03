import { api } from './client'
import type { DeriveResultDto, GitScanResultDto, SessionDetailDto, TimelineDto } from '../types/api'

export function getTimeline(dateIso: string): Promise<TimelineDto> {
  return api.get<TimelineDto>(`/v1/timeline?date=${dateIso}`)
}

export function getSession(id: number): Promise<SessionDetailDto> {
  return api.get<SessionDetailDto>(`/v1/sessions/${id}`)
}

/**
 * Idempotent and ~160ms measured — see docs/LLM.md's neighbour, the main plan,
 * for why the page calls this on every load rather than showing stale derived
 * data. Git scanning is not part of this: it hits disk across every configured
 * repo and stays a manual action.
 */
export function derive(): Promise<DeriveResultDto> {
  return api.post<DeriveResultDto>('/v1/derive')
}

/**
 * Walks every configured repo on disk — the slow half of git enrichment.
 * Deliberately not part of the automatic load-time query; triggered only by
 * the Refresh button, which then re-derives so the new commits attach to
 * sessions before the timeline refetches.
 */
export function scanGit(): Promise<GitScanResultDto> {
  return api.post<GitScanResultDto>('/v1/scan-git')
}
