import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { derive, getTimeline, scanGit } from '../api/timeline'
import { SessionDetail } from '../components/sessions/SessionDetail'
import { StatCard } from '../components/stats/StatCard'
import { PageHeader } from '../components/shell/PageHeader'
import { PillButton } from '../components/common/PillButton'
import { Skeleton } from '../components/common/Skeleton'
import { TimelineStrip } from '../components/timeline/TimelineStrip'
import { EmptyState } from '../components/common/EmptyState'
import { ErrorState } from '../components/common/ErrorState'
import { qk } from '../lib/queryKeys'
import { addDaysIso, formatDateHeading, formatDuration, formatHours, todayIso } from '../lib/format'

export function Today() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [dateIso, setDateIso] = useState(todayIso())
  const isToday = dateIso === todayIso()

  // Derive-then-fetch as one query: derivation is idempotent and ~160ms
  // measured, so the page can simply always be current rather than showing
  // stale derived data with a separate "refresh" step to remember.
  const { data, isPending, isError, error, isRefetching, refetch } = useQuery({
    queryKey: qk.timeline(dateIso),
    queryFn: async () => {
      await derive()
      return getTimeline(dateIso)
    },
  })

  // The Refresh button's full pipeline: walk every configured repo for new
  // commits, then let the timeline query's own refetch re-derive (so those
  // commits attach to sessions) and re-fetch. scanGit is deliberately not
  // part of the automatic load-time query above — it hits disk across every
  // repo and is too slow to run on every page load.
  const scanAndRefresh = useMutation({
    mutationFn: scanGit,
    onSuccess: () => refetch(),
  })

  const busyLabel = scanAndRefresh.isPending ? 'Scanning git…' : isRefetching ? 'Deriving…' : null

  const onRefresh = () => scanAndRefresh.mutate()

  const goToDay = (next: string) => {
    setSelectedId(null)
    setDateIso(next)
  }

  const actions = (
    <>
      <PillButton onClick={() => goToDay(addDaysIso(dateIso, -1))} title="Previous day">
        ‹
      </PillButton>
      <PillButton onClick={() => goToDay(addDaysIso(dateIso, 1))} disabled={isToday} title="Next day">
        ›
      </PillButton>
      <PillButton
        onClick={onRefresh}
        disabled={busyLabel !== null}
        className="border-line bg-raised px-4 py-2"
      >
        {busyLabel ?? 'Refresh'}
      </PillButton>
    </>
  )

  if (isPending) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title={formatDateHeading(dateIso)}
          subtitle="what you attended to, against what you shipped"
          actions={actions}
        />
        <Skeleton />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title={formatDateHeading(dateIso)}
          subtitle="what you attended to, against what you shipped"
          actions={actions}
        />
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
      <PageHeader
        title={formatDateHeading(dateIso)}
        subtitle="what you attended to, against what you shipped"
        actions={actions}
      />

      {sessions.length === 0 ? (
        <EmptyState
          title={isToday ? 'Nothing tracked yet today.' : 'Nothing tracked on this day.'}
          detail={
            isToday
              ? "The collector records as you work — check back once you've switched windows a few times."
              : undefined
          }
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
            <StatCard label="Interruptions" value={String(interruptions)} />
          </div>

          <TimelineStrip sessions={sessions} commits={commits} selectedId={selectedId} onSelect={setSelectedId} />

          {selectedId !== null && <SessionDetail sessionId={selectedId} />}
        </>
      )}
    </div>
  )
}
