import type { ReactNode } from 'react'

/** The one header shape shared by all five routes — replaces the Today-specific TopBar.tsx. */
export function PageHeader({ title, subtitle, actions }: { title: string; subtitle: string; actions?: ReactNode }) {
  return (
    <div className="flex items-center justify-between">
      <div>
        <h1 className="text-xl font-semibold text-ink">{title}</h1>
        <p className="text-xs text-faint">{subtitle}</p>
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  )
}
