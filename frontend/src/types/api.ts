/**
 * Hand-mirrors backend/src/Devlog.Api/Contracts. Field names are camelCase
 * because ASP.NET Core's minimal-API default JSON options apply
 * JsonNamingPolicy.CamelCase — verified against a live response, not assumed.
 *
 * Timestamps are local-time ISO 8601 strings (Devlog.Api already converted
 * from UTC), so this layer does no timezone arithmetic of its own.
 */

export type ActivityCategory =
  | 'Other'
  | 'Coding'
  | 'Learning'
  | 'Communication'
  | 'Meeting'
  | 'FileManagement'
  | 'Distraction'
  | 'Personal'

export interface SessionDto {
  id: number
  startIso: string
  endIso: string
  durationSeconds: number
  project: string | null
  category: ActivityCategory
  interruptions: number
  deepSeconds: number
  label: string | null
  activityCount: number
  commitCount: number
  insertions: number
  deletions: number
  isZeroOutput: boolean
}

export interface ActivityDto {
  id: number
  startIso: string
  endIso: string
  durationSeconds: number
  processName: string | null
  context: string | null
  siteIdentity: string | null
  category: ActivityCategory
  engagement: 'Producing' | 'Consuming' | 'Idle' | 'Away'
  titleChanges: number
  sampleTitle: string | null
}

export interface CommitDto {
  sha: string
  repo: string
  project: string
  timestampIso: string
  message: string | null
  branch: string | null
  filesChanged: number
  insertions: number
  deletions: number
  languages: string | null
  isMerge: boolean
  sessionId: number | null
}

export interface TimelineDto {
  date: string
  sessions: SessionDto[]
  commits: CommitDto[]
  unclassifiedSeconds: number
}

export interface SessionDetailDto {
  session: SessionDto
  activities: ActivityDto[]
  commits: CommitDto[]
}

export interface GitScanResultDto {
  scanned: number
  skipped: number
  reposFailed: number
}

export interface DeriveResultDto {
  rawEvents: number
  afterNoise: number
  activities: number
  sessions: number
  pendingIdentities: number
  unclassifiedSeconds: number
  commitsLinked: number
  commitsUnattached: number
  elapsedMs: number
}
