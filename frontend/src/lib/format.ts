/** Mirrors StatsReporter.FormatHeld — the terminal and the UI describe a duration the same way. */
export function formatDuration(seconds: number): string {
  if (seconds <= 0) return '—'
  if (seconds < 60) return `${seconds}s`
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m${String(seconds % 60).padStart(2, '0')}s`
  return `${Math.floor(seconds / 3600)}h${String(Math.floor((seconds % 3600) / 60)).padStart(2, '0')}m`
}

export function formatHours(seconds: number): string {
  return `${(seconds / 3600).toFixed(1)}h`
}

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
}

/** YYYY-MM-DD in the viewer's own local calendar — what /v1/timeline?date= expects. */
export function todayIso(): string {
  return dateIso(new Date())
}

/** YYYY-MM-DD for a date N days before today, local calendar — the digest's default range. */
export function daysAgoIso(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return dateIso(d)
}

function dateIso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function formatDateHeading(dateIso: string): string {
  const d = new Date(`${dateIso}T00:00:00`)
  return d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })
}
