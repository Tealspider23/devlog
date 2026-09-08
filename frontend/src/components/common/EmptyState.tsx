import type { ReactNode } from 'react'
import { Card } from './Card'

/** A day with genuinely no data. Not zeros, not a blank rectangle — say so plainly. */
export function EmptyState({ title, detail, children }: { title: string; detail?: string; children?: ReactNode }) {
  return (
    <Card className="flex flex-col items-center justify-center gap-1 py-16 text-center">
      <p className="text-sm text-muted">{title}</p>
      {detail && <p className="max-w-md text-xs text-faint">{detail}</p>}
      {children}
    </Card>
  )
}
