import { Sidebar } from './components/shell/Sidebar'
import { Today } from './routes/Today'

export function App() {
  return (
    <div className="flex min-h-screen bg-page text-ink">
      <Sidebar />
      <main className="flex-1 p-8">
        <Today />
      </main>
    </div>
  )
}
