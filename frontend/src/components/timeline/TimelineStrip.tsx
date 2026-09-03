import { CATEGORY_COLOR, CATEGORY_LABEL } from '../../lib/categories'
import type { ActivityCategory, CommitDto, SessionDto } from '../../types/api'
import { CommitMarker } from './CommitMarker'
import { TimelineAxis } from './TimelineAxis'
import { TimelineBand } from './TimelineBand'

const LEGEND_ORDER: ActivityCategory[] = [
  'Coding',
  'Learning',
  'Communication',
  'Meeting',
  'Personal',
  'Distraction',
  'FileManagement',
  'Other',
]

/**
 * Scaled to the day's own active window (first session start → last session
 * end), not a fixed 00:00–24:00. On a 9.4h day that is 2.5x the pixels per
 * minute of a fixed frame, for free.
 */
export function TimelineStrip({
  sessions,
  commits,
  selectedId,
  onSelect,
}: {
  sessions: SessionDto[]
  commits: CommitDto[]
  selectedId: number | null
  onSelect: (id: number) => void
}) {
  const starts = sessions.map((s) => new Date(s.startIso).getTime())
  const ends = sessions.map((s) => new Date(s.endIso).getTime())
  const commitTimes = commits.map((c) => new Date(c.timestampIso).getTime())

  const windowStart = Math.min(...starts, ...commitTimes)
  const windowEnd = Math.max(...ends, ...commitTimes)
  const span = Math.max(windowEnd - windowStart, 60_000)

  const usedCategories = new Set(sessions.map((s) => s.category))

  return (
    <div className="flex flex-col gap-3 rounded-[var(--radius-card)] border border-line bg-surface p-4">
      <div className="relative pt-3">
        {commits.map((c) => (
          <CommitMarker key={c.sha} commit={c} windowStart={windowStart} span={span} />
        ))}

        <TimelineBand
          sessions={sessions}
          windowStart={windowStart}
          span={span}
          selectedId={selectedId}
          onSelect={onSelect}
        />
      </div>

      <TimelineAxis windowStart={windowStart} windowEnd={windowEnd} />

      <div className="flex flex-wrap gap-x-4 gap-y-1 border-t border-line pt-3">
        {LEGEND_ORDER.filter((c) => usedCategories.has(c)).map((c) => (
          <div key={c} className="flex items-center gap-1.5">
            <span
              className={`h-2 w-2 rounded-sm ${c === 'Other' ? 'hatched' : ''}`}
              style={{ backgroundColor: c === 'Other' ? undefined : CATEGORY_COLOR[c] }}
            />
            <span className="text-[11px] text-muted">{CATEGORY_LABEL[c]}</span>
          </div>
        ))}
        <div className="flex items-center gap-1.5">
          <span className="text-accent">▲</span>
          <span className="text-[11px] text-muted">commit</span>
        </div>
      </div>
    </div>
  )
}
