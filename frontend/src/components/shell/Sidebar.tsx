/**
 * Only "Today" exists. No Week/Sessions/Unknowns nav items — a link that goes
 * nowhere is worse than no link, and those routes are explicitly deferred
 * (see the plan's 4b.3). Add them here when they're real.
 */
export function Sidebar() {
  return (
    <aside className="flex w-56 shrink-0 flex-col gap-6 border-r border-line bg-surface px-4 py-6">
      <div className="flex items-center gap-2 px-2">
        <span className="h-2 w-2 rounded-full bg-accent" />
        <span className="text-sm font-semibold tracking-wide">devlog</span>
      </div>

      <nav className="flex flex-col gap-1">
        <div className="flex items-center gap-2 rounded-full bg-raised px-3 py-2 text-sm text-ink">
          <span className="h-1.5 w-1.5 rounded-full bg-accent" />
          Today
        </div>
      </nav>
    </aside>
  )
}
