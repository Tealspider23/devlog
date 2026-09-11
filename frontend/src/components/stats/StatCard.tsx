import type { ReactNode } from 'react'
import { Card } from '../common/Card'

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
  captionNode,
  children,
}: {
  label: string
  value: string
  caption?: string
  /** A pre-styled caption, e.g. two differently-coloured spans. Takes priority over `caption` when given. */
  captionNode?: ReactNode
  children?: ReactNode
}) {
  return (
    <Card className="flex flex-col gap-2 p-4">
      <span className="text-xs text-faint">{label}</span>
      <span className="text-2xl font-semibold text-ink">{value}</span>
      {captionNode ?? (caption && <span className="text-xs text-muted">{caption}</span>)}
      {children}
    </Card>
  )
}
