const HOUR = 3_600_000

/** Coarser ticks as the window widens, so labels never overlap. */
function tickIntervalMs(windowMs: number): number {
  if (windowMs <= 4 * HOUR) return 0.5 * HOUR
  if (windowMs <= 12 * HOUR) return HOUR
  if (windowMs <= 18 * HOUR) return 2 * HOUR
  return 3 * HOUR
}

function computeTicks(windowStart: number, windowEnd: number): number[] {
  const interval = tickIntervalMs(windowEnd - windowStart)
  const first = Math.ceil(windowStart / interval) * interval
  const ticks: number[] = []
  for (let t = first; t < windowEnd; t += interval) ticks.push(t)
  return ticks
}

export function TimelineAxis({ windowStart, windowEnd }: { windowStart: number; windowEnd: number }) {
  const span = windowEnd - windowStart
  const ticks = computeTicks(windowStart, windowEnd)

  return (
    <div className="relative h-4">
      {ticks.map((t) => (
        <span
          key={t}
          className="absolute -translate-x-1/2 text-[10px] text-faint"
          style={{ left: `${((t - windowStart) / span) * 100}%` }}
        >
          {new Date(t).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })}
        </span>
      ))}
    </div>
  )
}
