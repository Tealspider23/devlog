import type { DayStatDto } from '../../types/api'
import { formatDuration } from '../../lib/format'
import { Tooltip } from '../common/Tooltip'

/**
 * `dailyBreakdown` as flex bars, not SVG. A chart library or SVG earns its
 * place in TimelineBand because sessions sit at arbitrary fractional offsets
 * in a continuous window — this is a uniform categorical series of at most
 * 31 members, so plain flex handles it with no viewBox and no scaling math.
 */
export function DailyBars({ days }: { days: DayStatDto[] }) {
  if (days.length === 0) return null

  const max = Math.max(...days.map((d) => d.trackedSeconds), 1)
  const showEveryLabel = days.length <= 9

  return (
    <div className="flex items-end gap-1.5">
      {days.map((d, i) => {
        const trackedPct = (d.trackedSeconds / max) * 100
        const deepPct = (d.deepSeconds / max) * 100
        const date = new Date(`${d.date}T00:00:00`)
        const label = showEveryLabel ? date.toLocaleDateString(undefined, { weekday: 'narrow' }) : String(date.getDate())

        return (
          <div key={d.date} className="flex flex-1 flex-col items-center gap-1">
            <Tooltip
              label={
                <div className="flex flex-col gap-0.5">
                  <div>{formatDuration(d.trackedSeconds)} tracked</div>
                  <div>{formatDuration(d.deepSeconds)} deep</div>
                  {d.commitCount > 0 && (
                    <div>
                      {d.commitCount} commit{d.commitCount === 1 ? '' : 's'}
                    </div>
                  )}
                </div>
              }
            >
              <div
                className="relative w-full origin-bottom rounded-sm bg-line transition-transform group-hover:scale-105"
                style={{ height: '6rem' }}
              >
                <div
                  className="absolute bottom-0 w-full rounded-sm bg-line"
                  style={{ height: `${trackedPct}%` }}
                />
                <div
                  className="absolute bottom-0 w-full rounded-sm bg-accent"
                  style={{ height: `${deepPct}%` }}
                />
              </div>
            </Tooltip>
            {d.commitCount > 0 && <span className="h-1 w-1 rounded-full bg-accent-dim" />}
            <span className="text-[10px] text-faint">{showEveryLabel || i % 5 === 0 ? label : ''}</span>
          </div>
        )
      })}
    </div>
  )
}
