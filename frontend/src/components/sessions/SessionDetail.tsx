import { useQuery } from '@tanstack/react-query'
import { getSession } from '../../api/timeline'
import { CATEGORY_LABEL } from '../../lib/categories'
import { formatDuration, formatTime } from '../../lib/format'

export function SessionDetail({ sessionId }: { sessionId: number }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['session', sessionId],
    queryFn: () => getSession(sessionId),
  })

  if (isPending) {
    return <div className="rounded-[var(--radius-card)] border border-line bg-surface p-4 text-sm text-faint">Loading session…</div>
  }

  if (isError) {
    return <div className="rounded-[var(--radius-card)] border border-line bg-surface p-4 text-sm text-warn">Could not load this session.</div>
  }

  const { session, activities, commits } = data

  return (
    <div className="flex flex-col gap-4 rounded-[var(--radius-card)] border border-line bg-surface p-4">
      <div className="flex items-baseline justify-between">
        <h2 className="text-sm font-semibold text-ink">
          {session.project ?? CATEGORY_LABEL[session.category]}
          <span className="ml-2 text-xs font-normal text-faint">
            {formatTime(session.startIso)}–{formatTime(session.endIso)}
          </span>
        </h2>
        <span className="text-xs text-muted">
          {formatDuration(session.durationSeconds)} · {session.interruptions} interruption
          {session.interruptions === 1 ? '' : 's'}
        </span>
      </div>

      {commits.length > 0 ? (
        <div className="flex flex-col gap-1.5">
          {commits.map((c) => (
            <div key={c.sha} className="flex items-center justify-between text-xs">
              <span className="truncate text-muted">{c.message ?? c.sha.slice(0, 7)}</span>
              <span className="shrink-0 pl-3 text-accent-dim">
                +{c.insertions}/-{c.deletions}
              </span>
            </div>
          ))}
        </div>
      ) : (
        // Zero-output sessions are a real finding — usually debugging or
        // research — not a gap to apologise for.
        <p className="text-xs text-faint">No commits in this session.</p>
      )}

      <div className="flex flex-col gap-1 border-t border-line pt-3">
        {activities.map((a) => (
          <div key={a.id} className="flex items-center justify-between text-xs">
            <span className="truncate text-muted">{a.sampleTitle ?? a.processName ?? a.context}</span>
            <span className="shrink-0 pl-3 text-faint">{formatDuration(a.durationSeconds)}</span>
          </div>
        ))}
      </div>
    </div>
  )
}
