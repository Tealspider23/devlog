import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { derive, getTimeline } from '../api/timeline'
import { SessionDetail } from '../components/sessions/SessionDetail'
import { StatCard } from '../components/stats/StatCard'
import { TopBar } from '../components/shell/TopBar'
import { TimelineStrip } from '../components/timeline/TimelineStrip'
import { EmptyState } from '../components/common/EmptyState'
import { ErrorState } from '../components/common/ErrorState'
import { formatDuration, formatHours, todayIso } from '../lib/format'

export function Today() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const dateIso = todayIso()

  // Derive-then-fetch as one query: derivation is idempotent and ~160ms
  // measured, so the page can simply always be current rather than showing
  // stale derived data with a separate "refresh" step to remember.
  const { data, isPending, isError, error, isRefetching, refetch } = useQuery({
    queryKey: ['timeline', dateIso],
    queryFn: async () => {
      await derive()
      return getTimeline(dateIso)
    },
  })

  if (isPending) {
    return (
      <div className="flex flex-col gap-6">
        <TopBar dateIso={dateIso} isRefetching onRefresh={() => refetch()} />
        <div className="animate-pulse rounded-[var(--radius-card)] border border-line bg-surface py-16" />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="flex flex-col gap-6">
        <TopBar dateIso={dateIso} isRefetching={false} onRefresh={() => refetch()} />
        <ErrorState error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const { sessions, commits } = data

  const deepSeconds = sessions.reduce((sum, s) => sum + s.deepSeconds, 0)
  const commitCount = sessions.reduce((sum, s) => sum + s.commitCount, 0)
  const insertions = sessions.reduce((sum, s) => sum + s.insertions, 0)
  const deletions = sessions.reduce((sum, s) => sum + s.deletions, 0)
  const interruptions = sessions.reduce((sum, s) => sum + s.interruptions, 0)

  return (
    <div className="flex flex-col gap-6">
      <TopBar dateIso={dateIso} isRefetching={isRefetching} onRefresh={() => refetch()} />

      {sessions.length === 0 ? (
        <EmptyState
          title="Nothing tracked yet today."
          detail="The collector records as you work — check back once you've switched windows a few times."
        />
      ) : (
        <>
          <div className="grid grid-cols-4 gap-4">
            <StatCard label="Deep work" value={formatHours(deepSeconds)} caption={formatDuration(deepSeconds)} />
            <StatCard label="Sessions" value={String(sessions.length)} />
            <StatCard
              label="Shipped"
              value={String(commitCount)}
              caption={commitCount > 0 ? `+${insertions}/-${deletions}` : undefined}
            />
            <StatCard label="Context switches" value={String(interruptions)} />
          </div>

          <TimelineStrip sessions={sessions} commits={commits} selectedId={selectedId} onSelect={setSelectedId} />

          {selectedId !== null && <SessionDetail sessionId={selectedId} />}
        </>
      )}
    </div>
  )
}
