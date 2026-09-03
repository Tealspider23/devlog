import { CATEGORY_COLOR, CATEGORY_LABEL } from '../../lib/categories'
import { formatDuration } from '../../lib/format'
import type { SessionDto } from '../../types/api'

/**
 * A continuous band, not discrete cards.
 *
 * Measured against 2026-08-31 (78 sessions in 9.4h, 46 of them under 5
 * minutes) a card-per-session layout produces dozens of slivers too thin to
 * label. Painting continuously means 54 consecutive Coding sessions read as a
 * few large lime regions; the session boundary becomes a hairline divider
 * (a seam, not a gap) rather than its own object.
 *
 * Positioned absolutely by real elapsed time, not laid out with flex — a gap
 * between sessions (a lock, a break, the reason there are 78 separate
 * sessions rather than one) has to render as genuine empty space. A flex row
 * would silently compress every gap to zero width and misrepresent the day.
 *
 * min-width: 2px on each segment is what keeps a 40-second session visible as
 * a sliver instead of rounding away to nothing — CSS honours min-width even
 * when width is a percentage.
 */
export function TimelineBand({
  sessions,
  windowStart,
  span,
  selectedId,
  onSelect,
}: {
  sessions: SessionDto[]
  windowStart: number
  span: number
  selectedId: number | null
  onSelect: (id: number) => void
}) {
  return (
    <div className="relative h-16 w-full rounded-2xl border border-line bg-page/40">
      {sessions.map((s) => {
        const start = new Date(s.startIso).getTime()
        const end = new Date(s.endIso).getTime()
        const leftPct = ((start - windowStart) / span) * 100
        const widthPct = ((end - start) / span) * 100
        const isUnclassified = s.category === 'Other'
        const showLabel = widthPct > 3.5
        const isSelected = s.id === selectedId

        return (
          <button
            key={s.id}
            onClick={() => onSelect(s.id)}
            title={`${s.project ?? CATEGORY_LABEL[s.category]} · ${formatDuration(s.durationSeconds)}`}
            className={`group absolute inset-y-0 border-r border-page/60 text-left transition-[filter] hover:brightness-125 focus:outline-none ${
              isUnclassified ? 'hatched' : ''
            } ${isSelected ? 'z-10 ring-2 ring-inset ring-accent' : ''}`}
            style={{
              left: `${leftPct}%`,
              width: `${widthPct}%`,
              minWidth: '2px',
              backgroundColor: isUnclassified ? undefined : CATEGORY_COLOR[s.category],
              opacity: isUnclassified ? 1 : 0.85,
            }}
          >
            {showLabel && (
              <span className="absolute inset-x-1 bottom-1 truncate text-[10px] font-medium text-page/80">
                {s.project ?? CATEGORY_LABEL[s.category]}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
