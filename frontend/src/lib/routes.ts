import { useCallback, useEffect, useState } from 'react'

export const ROUTES = ['today', 'week', 'month', 'chat', 'settings'] as const
export type Route = (typeof ROUTES)[number]

export const ROUTE_LABEL: Record<Route, string> = {
  today: 'Today',
  week: 'Week',
  month: 'Month',
  chat: 'Chat',
  settings: 'Settings',
}

function parseHash(): Route {
  const raw = window.location.hash.replace(/^#\/?/, '') as Route
  return (ROUTES as readonly string[]).includes(raw) ? raw : 'today'
}

/**
 * Route-only URL sync — no router dependency. Deliberately does not carry the
 * selected day, the period offset, or the chat transcript: those are
 * ephemeral view state, and encoding them means a param parser and
 * validation for a single-user local app with no sharing story. A reload or
 * an HMR pass simply returns you to whichever route you were on.
 */
export function useHashRoute(): [Route, (route: Route) => void] {
  const [route, setRoute] = useState<Route>(parseHash)

  useEffect(() => {
    const onHashChange = () => setRoute(parseHash())
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  const navigate = useCallback((next: Route) => {
    window.location.hash = `/${next}`
  }, [])

  return [route, navigate]
}
