import { useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getDigest } from '../api/digest'
import { getNarratives } from '../api/ai'
import { useAiStatus } from '../hooks/useAiStatus'
import { StatCard } from '../components/stats/StatCard'
import { DailyBars } from '../components/stats/DailyBars'
import { BreakdownList } from '../components/stats/BreakdownList'
import { NarrativeList } from '../components/narratives/NarrativeList'
import { PageHeader } from '../components/shell/PageHeader'
import { PillButton } from '../components/common/PillButton'
import { Card } from '../components/common/Card'
import { Skeleton } from '../components/common/Skeleton'
import { EmptyState } from '../components/common/EmptyState'
import { ErrorState } from '../components/common/ErrorState'
import { qk } from '../lib/queryKeys'
import { rangeFor, rangeLabel, type RangeKind } from '../lib/range'
import { formatDuration, formatHours } from '../lib/format'

const SKIPPED_NOTE = /\*Note: Prose summary was skipped \((.+)\)\*/

/** The shared Week/Month view — replaces the old Digest.tsx and its trailing-N-days RangePreset. */
export function Range({ kind }: { kind: RangeKind }) {
  const [offset, setOffset] = useState(0)
  const [showProse, setShowProse] = useState(false)
  const [copied, setCopied] = useState(false)

  const { data: aiStatus } = useAiStatus()

  // At offset 0 the backend resolves the current period through
  // Devlog.Core.Metrics.CalendarRange — the frontend does not compute "this
  // week" itself, so the two can never disagree on what the word means.
  // Only earlier periods need lib/range.ts, where the backend has no opinion.
  const params = offset === 0 ? { range: kind } : rangeFor(kind, offset)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: qk.digest(kind, offset),
    queryFn: ({ signal }) => getDigest(params, signal),
  })

  const proseMutation = useMutation({
    mutationFn: () => getDigest({ from: data?.from, to: data?.to, prose: true }),
    onSuccess: () => setShowProse(true),
  })

  // The prose result is tied to a specific from/to — navigating to a
  // different period must fall back to the deterministic markdown rather
  // than keep showing prose generated for a range no longer on screen.
  useEffect(() => {
    setShowProse(false)
    proseMutation.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [offset, kind])

  const narrativesQuery = useQuery({
    queryKey: qk.narratives(data?.from ?? '', data?.to ?? ''),
    queryFn: ({ signal }) => getNarratives(data!.from, data!.to, signal),
    enabled: !!data,
  })

  const skippedMatch = proseMutation.data?.markdown.match(SKIPPED_NOTE)
  const markdownToShow = showProse && proseMutation.data && !skippedMatch ? proseMutation.data.markdown : data?.markdown

  const onCopy = async () => {
    if (!markdownToShow) return
    await navigator.clipboard.writeText(markdownToShow)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  const label = data ? rangeLabel(kind, data.from, data.to) : '…'

  const actions = (
    <>
      <PillButton onClick={() => setOffset((o) => o - 1)}>‹</PillButton>
      <PillButton onClick={() => setOffset((o) => Math.min(0, o + 1))} disabled={offset === 0}>
        ›
      </PillButton>
      {offset !== 0 && <PillButton onClick={() => setOffset(0)}>This {kind}</PillButton>}
    </>
  )

  if (isPending) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title={kind === 'week' ? 'Week' : 'Month'} subtitle="what this period amounted to" actions={actions} />
        <Skeleton />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title={kind === 'week' ? 'Week' : 'Month'} subtitle="what this period amounted to" actions={actions} />
        <ErrorState error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={label} subtitle="what this period amounted to" actions={actions} />

      {data.sessionCount === 0 ? (
        <EmptyState title="Nothing tracked in this range." detail="Try a wider range, or check back once you've worked a bit." />
      ) : (
        <>
          <div className="grid grid-cols-4 gap-4">
            <StatCard label="Deep work" value={formatHours(data.deepSeconds)} caption={formatDuration(data.deepSeconds)} />
            <StatCard label="Sessions" value={String(data.sessionCount)} caption={`${data.activeDays} active days`} />
            <StatCard
              label="Shipped"
              value={String(data.commitCount)}
              caption={data.commitCount > 0 ? `+${data.insertions}/-${data.deletions}` : undefined}
            />
            <StatCard
              label="Interruptions"
              value={String(data.interruptionsTotal)}
              caption={`${data.interruptionsPerActiveDay.toFixed(1)}/active day`}
            />
          </div>

          <Card className="p-4">
            <DailyBars days={data.dailyBreakdown} />
          </Card>

          {(data.timeByProject.length > 0 || data.timeByCategory.length > 0) && (
            <div className="grid grid-cols-2 gap-4">
              <Card className="p-4">
                <BreakdownList title="Time by project" projects={data.timeByProject} />
              </Card>
              <Card className="p-4">
                <BreakdownList title="Time by category" categories={data.timeByCategory} />
              </Card>
            </div>
          )}

          {(data.longestBlock || data.bestDay) && (
            <Card className="flex flex-col gap-1 p-4 text-xs text-muted">
              {data.bestDay && (
                <span>
                  Best day: <span className="text-ink">{new Date(`${data.bestDay.date}T00:00:00`).toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' })}</span> — {formatHours(data.bestDay.deepSeconds)} deep work
                </span>
              )}
              {data.longestBlock && (
                <span>
                  Longest block: <span className="text-ink">{formatDuration(data.longestBlock.deepSeconds)}</span>
                  {data.longestBlock.project && ` on ${data.longestBlock.project}`}
                </span>
              )}
            </Card>
          )}

          {aiStatus?.enabled && (
            <NarrativeList
              narratives={narrativesQuery.data ?? []}
              totalSessions={data.sessionCount}
              from={data.from}
            />
          )}

          <Card className="p-4">
            <div className="mb-3 flex items-center justify-between gap-3">
              <span className="text-xs text-faint">
                {showProse && markdownToShow === proseMutation.data?.markdown
                  ? 'Markdown with an AI summary — every figure in it is still computed by devlog.'
                  : 'Markdown — pastes directly into a review'}
              </span>
              <div className="flex items-center gap-2">
                {aiStatus?.enabled && aiStatus.jobDigestEnabled && (
                  <PillButton onClick={() => proseMutation.mutate()} disabled={proseMutation.isPending}>
                    {proseMutation.isPending ? 'Generating…' : showProse ? 'Regenerate summary' : 'Generate summary'}
                  </PillButton>
                )}
                <PillButton onClick={onCopy}>{copied ? 'Copied' : 'Copy'}</PillButton>
              </div>
            </div>

            {skippedMatch && (
              <div className="mb-3 rounded-lg border border-line bg-raised px-3 py-2 text-xs text-muted">
                The summary was skipped — this is the deterministic digest. {skippedMatch[1]}
              </div>
            )}

            <pre className="max-h-[32rem] overflow-auto whitespace-pre-wrap text-xs text-ink">{markdownToShow}</pre>
          </Card>
        </>
      )}
    </div>
  )
}
