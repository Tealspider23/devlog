import { useState } from 'react'
import { Sidebar, type Route } from './components/shell/Sidebar'
import { Today } from './routes/Today'
import { Digest } from './routes/Digest'

export function App() {
  const [route, setRoute] = useState<Route>('today')

  return (
    <div className="flex min-h-screen bg-page text-ink">
      <Sidebar route={route} onNavigate={setRoute} />
      <main className="flex-1 p-8">{route === 'today' ? <Today /> : <Digest />}</main>
    </div>
  )
}
