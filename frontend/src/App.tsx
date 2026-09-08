import { Sidebar } from './components/shell/Sidebar'
import { Today } from './routes/Today'
import { Range } from './routes/Range'
import { Chat } from './routes/Chat'
import { Settings } from './routes/Settings'
import { useHashRoute } from './lib/routes'
import { useAiStatus } from './hooks/useAiStatus'

export function App() {
  const [route, navigate] = useHashRoute()
  // Prefetched here so Chat's model picker and Settings are warm without
  // either route's own render waiting on a live provider probe.
  const { data: aiStatus } = useAiStatus()

  return (
    <div className="flex min-h-screen bg-page text-ink">
      <Sidebar route={route} onNavigate={navigate} chatDim={aiStatus ? !aiStatus.enabled : false} />
      <main className="flex-1 p-8">
        {route === 'today' && <Today />}
        {route === 'week' && <Range key="week" kind="week" />}
        {route === 'month' && <Range key="month" kind="month" />}
        {route === 'chat' && <Chat />}
        {route === 'settings' && <Settings />}
      </main>
    </div>
  )
}
