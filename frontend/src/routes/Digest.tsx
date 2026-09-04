import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { getDigest } from '../api/digest'
import { StatCard } from '../components/stats/StatCard'
import { EmptyState } from '../components/common/EmptyState'
import { ErrorState } from '../components/common/ErrorState'
import { daysAgoIso, formatDuration, formatHours, todayIso } from '../lib/format'

type RangePreset = 'week' | 'month'

function rangeFor(preset: RangePreset): { from: string; to: string } {
  return { from: daysAgoIso(preset === 'week' ? 6 : 29), to: todayIso() }
}

/**
 * The brag document. A shape, not an analysis — every number here is
 * deterministic (Devlog.Core.Metrics.MetricsCalculator), and the Markdown is
 * the exact text `devlog digest` would write for the same range. Copy pastes
 * that string verbatim rather than re-composing it from the cards, so the two
 * surfaces can never say different things.
 */
export function Digest() {
  const [preset, setPreset] = useState<RangePreset>('week')
  const [copied, setCopied] = useState(false)
  const { from, to } = rangeFor(preset)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['digest', from, to],
    queryFn: () => getDigest(from, to),
  })

  const onCopy = async () => {
    if (!data) return
    await navigator.clipboard.writeText(data.markdown)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-ink">Digest</h1>
          <p className="text-xs text-faint">what you shipped, in a shape you can paste into a review</p>
        </div>

        <div className="flex overflow-hidden rounded-full border border-line">
          {(['week', 'month'] as const).map((p) => (
            <button
              key={p}
              onClick={() => setPreset(p)}
              className={`px-4 py-1.5 text-xs transition-colors ${
                preset === p ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
              }`}
            >
              Last {p}
            </button>
          ))}
        </div>
      </div>

      {isPending && <div className="animate-pulse rounded-[var(--radius-card)] border border-line bg-surface py-16" />}

      {isError && <ErrorState error={error} onRetry={() => refetch()} />}

      {data && data.sessionCount === 0 && (
        <EmptyState title="Nothing tracked in this range." detail="Try a wider range, or check back once you've worked a bit." />
      )}

      {data && data.sessionCount > 0 && (
        <>
          <div className="grid grid-cols-4 gap-4">
            <StatCard label="Deep work" value={formatHours(data.deepSeconds)} caption={formatDuration(data.deepSeconds)} />
            <StatCard label="Sessions" value={String(data.sessionCount)} caption={`${data.activeDays} active days`} />
            <StatCard
              label="Shipped"
              value={String(data.commitCount)}
              caption={data.commitCount > 0 ? `+${data.insertions}/-${data.deletions}` : undefined}
            />
            <StatCard label="Interruptions" value={String(data.interruptionsTotal)} caption={`${data.interruptionsPerActiveDay.toFixed(1)}/active day`} />
          </div>

          <div className="rounded-[var(--radius-card)] border border-line bg-surface p-4">
            <div className="mb-3 flex items-center justify-between">
              <span className="text-xs text-faint">Markdown — pastes directly into a review</span>
              <button
                onClick={onCopy}
                className="rounded-full border border-line px-3 py-1 text-xs text-muted transition-colors hover:border-accent-dim hover:text-ink"
              >
                {copied ? 'Copied' : 'Copy'}
              </button>
            </div>
            <pre className="max-h-[32rem] overflow-auto whitespace-pre-wrap text-xs text-ink">{data.markdown}</pre>
          </div>
        </>
      )}
    </div>
  )
}
