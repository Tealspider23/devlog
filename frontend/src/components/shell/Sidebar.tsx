export type Route = 'today' | 'digest'

const NAV: { route: Route; label: string }[] = [
  { route: 'today', label: 'Today' },
  { route: 'digest', label: 'Digest' },
]

/**
 * Two real routes now. No Week/Sessions/Unknowns nav items — a link that goes
 * nowhere is worse than no link, and those routes are explicitly deferred
 * (see the plan's 4b.3).
 */
export function Sidebar({ route, onNavigate }: { route: Route; onNavigate: (route: Route) => void }) {
  return (
    <aside className="flex w-56 shrink-0 flex-col gap-6 border-r border-line bg-surface px-4 py-6">
      <div className="flex items-center gap-2 px-2">
        <span className="h-2 w-2 rounded-full bg-accent" />
        <span className="text-sm font-semibold tracking-wide">devlog</span>
      </div>

      <nav className="flex flex-col gap-1">
        {NAV.map((item) => (
          <button
            key={item.route}
            onClick={() => onNavigate(item.route)}
            className={`flex items-center gap-2 rounded-full px-3 py-2 text-left text-sm transition-colors ${
              route === item.route ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
            }`}
          >
            <span className={`h-1.5 w-1.5 rounded-full ${route === item.route ? 'bg-accent' : 'bg-line'}`} />
            {item.label}
          </button>
        ))}
      </nav>
    </aside>
  )
}
