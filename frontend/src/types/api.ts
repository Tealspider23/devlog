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
  /** The repo, when one was genuinely resolved. Null for a browser tab or an app with no extraction rule. */
  project: string | null
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

export interface LongestBlockDto {
  startIso: string
  endIso: string
  project: string | null
  deepSeconds: number
}

export interface BestDayDto {
  date: string
  deepSeconds: number
}

export interface ProjectTimeDto {
  project: string
  seconds: number
}

export interface CategoryTimeDto {
  category: ActivityCategory
  seconds: number
}

/**
 * Mirrors Devlog.Api.Contracts.DigestDto. `markdown` is the exact text
 * `devlog digest` would write to a file for the same range — see
 * Devlog.Core.Metrics.DigestBuilder. Render cards from the structured fields;
 * the Copy button copies `markdown` verbatim so the two can never disagree.
 */
export interface DigestDto {
  from: string
  to: string
  trackedSeconds: number
  deepSeconds: number
  focusRatio: number
  sessionCount: number
  activeDays: number
  interruptionsTotal: number
  interruptionsPerActiveDay: number
  longestBlock: LongestBlockDto | null
  bestDay: BestDayDto | null
  timeByProject: ProjectTimeDto[]
  timeByCategory: CategoryTimeDto[]
  /** Coding time that resolved to no repo — a browser tab, SSMS, a bare shell. Reported, never dropped. */
  unattributedCodingSeconds: number
  zeroOutputSessionCount: number
  zeroOutputSeconds: number
  commitCount: number
  insertions: number
  deletions: number
  projectsShipped: string[]
  languages: string[]
  firstTimeLanguages: string[]
  ticketIds: string[]
  unattachedCommitsInRange: number
  unclassifiedSeconds: number
  markdown: string
}
