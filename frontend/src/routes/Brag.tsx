import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { getDigest } from '../api/digest'
import { getNarratives } from '../api/ai'
import { useAiStatus } from '../hooks/useAiStatus'
import { NarrativeCard } from '../components/narratives/NarrativeCard'
import { PageHeader } from '../components/shell/PageHeader'
import { PillButton } from '../components/common/PillButton'
import { Card } from '../components/common/Card'
import { Skeleton } from '../components/common/Skeleton'
import { EmptyState } from '../components/common/EmptyState'
import { ErrorState } from '../components/common/ErrorState'
import { qk } from '../lib/queryKeys'
import { rangeFor, rangeLabel } from '../lib/range'
import { confidenceBucket } from '../lib/narrativeKinds'

/**
 * A read-only rollup for review season: high-confidence feature-work
 * narratives plus first-time-technology moments, for a chosen month. No
 * writes, no confirm/edit step — that's the deferred `win`-table workflow.
 * This just surfaces what devlog already knows well enough to say out loud.
 */
export function Brag() {
  const [offset, setOffset] = useState(0)
  const { data: aiStatus } = useAiStatus()

  const params = offset === 0 ? { range: 'month' as const } : rangeFor('month', offset)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: qk.digest('month', offset),
    queryFn: ({ signal }) => getDigest(params, signal),
  })

  const narrativesQuery = useQuery({
    queryKey: qk.narratives(data?.from ?? '', data?.to ?? ''),
    queryFn: ({ signal }) => getNarratives(data!.from, data!.to, signal),
    enabled: !!data && !!aiStatus?.enabled,
  })

  const wins = (narrativesQuery.data ?? []).filter(
    (n) => n.kind === 'feature-work' && confidenceBucket(n.confidence) !== 'low',
  )
  const firstTimeLanguages = data?.firstTimeLanguages ?? []

  const label = data ? rangeLabel('month', data.from, data.to) : '…'

  const actions = (
    <>
      <PillButton onClick={() => setOffset((o) => o - 1)}>‹</PillButton>
      <PillButton onClick={() => setOffset((o) => Math.min(0, o + 1))} disabled={offset === 0}>
        ›
      </PillButton>
      {offset !== 0 && <PillButton onClick={() => setOffset(0)}>This month</PillButton>}
    </>
  )

  if (isPending) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title="Brag Document" subtitle="what would actually go in a review" actions={actions} />
        <Skeleton />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title="Brag Document" subtitle="what would actually go in a review" actions={actions} />
        <ErrorState error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={label} subtitle="what would actually go in a review" actions={actions} />

      {!aiStatus?.enabled ? (
        <EmptyState
          title="AI is off, so there are no stories to roll up."
          detail="This page reads session narratives (Job B). Turn AI on in Settings to start writing them."
        />
      ) : wins.length === 0 && firstTimeLanguages.length === 0 ? (
        <EmptyState
          title="Nothing brag-worthy yet this month."
          detail="This rolls up high-confidence feature work and first-time technologies. Write narratives on Week or Month first, or check back after more work is done."
        />
      ) : (
        <>
          {wins.length > 0 && (
            <Card className="flex flex-col gap-3 p-4">
              <span className="text-xs text-faint">Wins</span>
              <div className="flex flex-col">
                {wins.map((n) => (
                  <NarrativeCard key={`${n.sessionId}-${n.sessionStart}`} narrative={n} variant="row" />
                ))}
              </div>
            </Card>
          )}

          {firstTimeLanguages.length > 0 && (
            <Card className="flex flex-col gap-3 p-4">
              <span className="text-xs text-faint">First time using</span>
              <div className="flex flex-wrap gap-2">
                {firstTimeLanguages.map((lang) => (
                  <span key={lang} className="rounded-full border border-line bg-raised px-3 py-1 text-xs text-ink">
                    {lang}
                  </span>
                ))}
              </div>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
