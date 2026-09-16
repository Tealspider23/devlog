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
  | 'Admin'

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
  narrative: NarrativeDto | null
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

/** One calendar day's share of a digest range — the per-day bar on Week/Month. */
export interface DayStatDto {
  date: string
  deepSeconds: number
  trackedSeconds: number
  commitCount: number
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
  dailyBreakdown: DayStatDto[]
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

// ---------------------------------------------------------------------------
// AI — mirrors Devlog.Api.Contracts.AiDto. See docs/LLM.md for what each job
// sends and why "the model never computes a number" governs every one of
// these shapes: AskResponseDto carries figures only by quoting them from
// devlog's own data, never by producing them itself.
// ---------------------------------------------------------------------------

export interface AiStatusDto {
  enabled: boolean
  configuredModel: string
  apiKeyPresent: boolean
  jobClassifyEnabled: boolean
  jobNarrateEnabled: boolean
  jobDigestEnabled: boolean
  jobAskEnabled: boolean
  reachable: boolean
  endpoint: string | null
  offMachine: boolean | null
  host: string | null
  requestsToday: number
  requestsPerDay: number
}

export interface AiModelsDto {
  models: string[]
}

export interface SetAiApiKeyRequestDto {
  apiKey: string | null
}

export interface AiApiKeyResultDto {
  apiKeyPresent: boolean
}

export interface AskRequestDto {
  question: string
  model?: string
}

export interface AskResponseDto {
  success: boolean
  answer: string | null
  model: string | null
  toolRounds: number
  toolsUsed: string[]
  unverifiedNumbers: string[]
  error: string | null
}

/** `kind` — see docs/LLM.md section 5.4 for the full definition of each. */
export type NarrativeKind =
  | 'feature-work'
  | 'bugfix'
  | 'mr-review'
  | 'research'
  | 'meeting-followup'
  | 'admin'
  | 'context-thrash'
  | 'unclear'

export interface NarrativeDto {
  sessionStart: string
  sessionEnd: string
  sessionId: number | null
  narrative: string
  kind: NarrativeKind
  workstream: string | null
  evidence: string[]
  /** Model-reported, 0–1. Not a measurement — see lib/narrativeKinds.ts. */
  confidence: number
  model: string
}

export interface NarrateRequestDto {
  since?: string
  limit?: number
  dryRun?: boolean
  force?: boolean
}

export interface NarrateOutcomeDto {
  sessionId: number
  sessionStart: string
  project: string | null
  durationSeconds: number
  accepted: boolean
  narrative: NarrativeDto | null
  rejectionReason: string | null
}

export interface NarrateResultDto {
  dryRun: boolean
  acceptedCount: number
  rejectedCount: number
  outcomes: NarrateOutcomeDto[]
  /** True when the run stopped before attempting every eligible session because the provider rate-limited it — the rest are untouched, not skipped. */
  stoppedEarly: boolean
  stopReason: string | null
}

export interface WeeklyWinsRequestDto {
  from: string
  to: string
  force?: boolean
}

export interface WeeklyWinDto {
  weekFrom: string
  weekTo: string
  summary: string
  /** Point-wise genuine wins for the week — the model's evidence-backed highlights, not generic notes. */
  highlights: string[]
  model: string
}

export interface WeekOutcomeDto {
  weekFrom: string
  weekTo: string
  accepted: boolean
  skippedAsUpToDate: boolean
  win: WeeklyWinDto | null
  rejectionReason: string | null
}

export interface WeeklyWinsResultDto {
  weeks: WeekOutcomeDto[]
  stoppedEarly: boolean
  stopReason: string | null
}

export interface ClassifyAiRequestDto {
  dryRun?: boolean
  limit?: number
  force?: boolean
}

export interface ClassifyAiVerdictOutcomeDto {
  identity: string
  category: ActivityCategory
  confidence: number
  reason: string
}

export interface ClassifyAiResultDto {
  dryRun: boolean
  reachable: boolean
  processedCount: number
  skippedCount: number
  totalPendingRemaining: number
  verdicts: ClassifyAiVerdictOutcomeDto[]
  discards: string[]
  unreachableReason: string | null
}
