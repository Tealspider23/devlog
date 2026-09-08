import { dateIso } from './format'

export type RangeKind = 'week' | 'month'

/**
 * Calendar week/month math for **previous periods only**. At the current
 * period (offset 0) the frontend does not compute a range at all — it calls
 * `getDigest({range: kind})` and lets the backend resolve it through
 * `Devlog.Core.Metrics.CalendarRange`, then renders the heading from the
 * response's own `from`/`to`. So the view looked at almost all the time has
 * exactly one definition of "week", and it lives in C#.
 *
 * This function exists only for `offset < 0`, where the backend has no
 * opinion and explicit `from`/`to` are required — and it mirrors
 * `CalendarRange.cs`'s Monday rule exactly, so the two never drift:
 * `((int)date.DayOfWeek + 6) % 7` gives days since Monday, Sunday being 0.
 *
 * One deliberate asymmetry, worth knowing before "fixing" it: at offset 0 a
 * week runs Monday–today (partial); at any earlier offset it runs
 * Monday–Sunday (complete). "Last week" means the whole of last week.
 */
export function rangeFor(kind: RangeKind, offset: number): { from: string; to: string } {
  const today = new Date()

  if (kind === 'week') {
    const daysSinceMonday = (today.getDay() + 6) % 7
    const thisMonday = addDays(today, -daysSinceMonday)
    const from = addDays(thisMonday, offset * 7)
    const to = offset === 0 ? today : addDays(from, 6)
    return { from: dateIso(from), to: dateIso(to) }
  }

  const thisMonthStart = new Date(today.getFullYear(), today.getMonth(), 1)
  const from = new Date(thisMonthStart.getFullYear(), thisMonthStart.getMonth() + offset, 1)
  const to = offset === 0 ? today : new Date(from.getFullYear(), from.getMonth() + 1, 0)
  return { from: dateIso(from), to: dateIso(to) }
}

function addDays(d: Date, n: number): Date {
  const copy = new Date(d)
  copy.setDate(copy.getDate() + n)
  return copy
}

/** Heading for a range, from resolved from/to dates — used at every offset, current or previous. */
export function rangeLabel(kind: RangeKind, fromIso: string, toIso: string): string {
  if (kind === 'month') {
    return new Date(`${fromIso}T00:00:00`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  }
  const from = new Date(`${fromIso}T00:00:00`)
  const to = new Date(`${toIso}T00:00:00`)
  const fromStr = from.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  const toStr = to.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
  return `${fromStr} – ${toStr}`
}
