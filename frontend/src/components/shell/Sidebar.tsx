import { ROUTE_LABEL, type Route } from '../../lib/routes'

const PRIMARY: Route[] = ['today', 'week', 'month', 'brag', 'chat']
const FOOTER: Route[] = ['settings']

function NavButton({
  route,
  active,
  dim,
  onClick,
}: {
  route: Route
  active: boolean
  dim?: boolean
  onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      title={dim ? 'AI is off' : undefined}
      className={`flex items-center gap-2 rounded-full px-3 py-2 text-left text-sm transition-colors ${
        active ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
      }`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${active && !dim ? 'bg-accent' : 'bg-line'}`} />
      {ROUTE_LABEL[route]}
    </button>
  )
}

/**
 * Six routes, Settings visually separated below a divider. Nav items never
 * appear or disappear based on AI reachability — a menu that reshuffles
 * while you're reaching for it is the most disorienting failure available
 * here. `chatDim` only changes a dot colour, never removes the item.
 */
export function Sidebar({
  route,
  onNavigate,
  chatDim,
}: {
  route: Route
  onNavigate: (route: Route) => void
  chatDim?: boolean
}) {
  return (
    <aside className="flex w-56 shrink-0 flex-col gap-6 border-r border-line bg-surface px-4 py-6">
      <div className="flex items-center gap-2 px-2">
        <span className="h-2 w-2 rounded-full bg-accent" />
        <span className="text-sm font-semibold tracking-wide">devlog</span>
      </div>

      <nav className="flex flex-col gap-1">
        {PRIMARY.map((r) => (
          <NavButton
            key={r}
            route={r}
            active={route === r}
            dim={r === 'chat' ? chatDim : undefined}
            onClick={() => onNavigate(r)}
          />
        ))}
      </nav>

      <nav className="mt-auto flex flex-col gap-1 border-t border-line pt-4">
        {FOOTER.map((r) => (
          <NavButton key={r} route={r} active={route === r} onClick={() => onNavigate(r)} />
        ))}
      </nav>
    </aside>
  )
}
