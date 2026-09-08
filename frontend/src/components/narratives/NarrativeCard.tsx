import type { NarrativeDto } from '../../types/api'
import { formatTime } from '../../lib/format'
import { KIND_COLOR, KIND_LABEL, confidenceBucket } from '../../lib/narrativeKinds'

function ConfidenceBars({ confidence }: { confidence: number }) {
  const bucket = confidenceBucket(confidence)
  const filled = bucket === 'low' ? 1 : bucket === 'medium' ? 2 : 3
  return (
    <span
      className="inline-flex items-center gap-0.5"
      title={`model-reported confidence ${confidence.toFixed(2)} — not a measured figure`}
    >
      {[0, 1, 2].map((i) => (
        <span key={i} className={`h-1 w-2.5 rounded-full ${i < filled ? 'bg-accent-dim' : 'bg-line'}`} />
      ))}
    </span>
  )
}

/**
 * One narrative — Job B's output. Used inline on Today's session detail
 * ('inline') and as a row in Week/Month's Story list ('row'). Evidence is
 * collapsed by default: it's raw window titles, and this page gets
 * screen-shared during reviews.
 */
export function NarrativeCard({ narrative, variant }: { narrative: NarrativeDto; variant: 'inline' | 'row' }) {
  const low = confidenceBucket(narrative.confidence) === 'low'

  const header = (
    <div className="flex flex-wrap items-center gap-2 text-[11px]">
      <span className="inline-flex items-center gap-1.5 text-muted">
        <span className="h-1.5 w-1.5 rounded-full" style={{ background: KIND_COLOR[narrative.kind] }} />
        {KIND_LABEL[narrative.kind]}
      </span>
      {narrative.workstream && <span className="text-faint">· {narrative.workstream}</span>}
      <ConfidenceBars confidence={narrative.confidence} />
    </div>
  )

  const body = (
    <p className={`text-sm leading-relaxed ${low ? 'text-muted' : 'text-ink'}`}>{narrative.narrative}</p>
  )

  const evidence = (
    <details className="text-xs">
      <summary className="cursor-pointer text-faint">{narrative.evidence.length} pieces of evidence</summary>
      <div className="mt-1.5 flex flex-col gap-1">
        {narrative.evidence.map((e, i) => (
          <div key={i} className="truncate font-mono text-[11px] text-faint" title={e}>
            {e}
          </div>
        ))}
      </div>
    </details>
  )

  if (variant === 'inline') {
    return (
      <div className="flex flex-col gap-2">
        {header}
        {body}
        {evidence}
      </div>
    )
  }

  return (
    <div className="flex gap-3 border-t border-line pt-3 first:border-t-0 first:pt-0">
      <span className="w-12 shrink-0 pt-0.5 text-[11px] tabular-nums text-faint">{formatTime(narrative.sessionStart)}</span>
      <div className="flex flex-1 flex-col gap-2">
        {header}
        {body}
        {evidence}
      </div>
    </div>
  )
}
