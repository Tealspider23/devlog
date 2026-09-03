/** A day with genuinely no data. Not zeros, not a blank rectangle — say so plainly. */
export function EmptyState({ title, detail }: { title: string; detail?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-1 rounded-[var(--radius-card)] border border-line bg-surface py-16 text-center">
      <p className="text-sm text-muted">{title}</p>
      {detail && <p className="text-xs text-faint">{detail}</p>}
    </div>
  )
}
