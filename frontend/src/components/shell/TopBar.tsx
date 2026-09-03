import { formatDateHeading } from '../../lib/format'

export function TopBar({
  dateIso,
  busyLabel,
  onRefresh,
}: {
  dateIso: string
  /** null when idle; otherwise what Refresh is doing right now — "Scanning git…" then "Deriving…". */
  busyLabel: string | null
  onRefresh: () => void
}) {
  return (
    <div className="flex items-center justify-between">
      <div>
        <h1 className="text-xl font-semibold text-ink">{formatDateHeading(dateIso)}</h1>
        <p className="text-xs text-faint">what you attended to, against what you shipped</p>
      </div>

      <button
        onClick={onRefresh}
        disabled={busyLabel !== null}
        className="rounded-full border border-line bg-raised px-4 py-2 text-xs text-muted transition-colors hover:border-accent-dim hover:text-ink disabled:opacity-50"
      >
        {busyLabel ?? 'Refresh'}
      </button>
    </div>
  )
}
