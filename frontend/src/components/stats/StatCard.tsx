import type { ReactNode } from 'react'

/**
 * Four of these, always: Deep work, Sessions, Shipped, Context switches.
 * Deliberately not five — no "Unclassified" card, no invented "Meetings" card
 * with avatar stacks devlog has no data for. See the plan's scope note on
 * 2026-09-03 for why.
 */
export function StatCard({
  label,
  value,
  caption,
  children,
}: {
  label: string
  value: string
  caption?: string
  children?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-2 rounded-[var(--radius-card)] border border-line bg-surface p-4">
      <span className="text-xs text-faint">{label}</span>
      <span className="text-2xl font-semibold text-ink">{value}</span>
      {caption && <span className="text-xs text-muted">{caption}</span>}
      {children}
    </div>
  )
}
