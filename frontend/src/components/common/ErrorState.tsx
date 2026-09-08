import { CollectorUnreachableError } from '../../api/client'
import { Card } from './Card'
import { PillButton } from './PillButton'

/** Killing the collector must show an honest "not running" state, not a spinner forever or a stack trace. */
export function ErrorState({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const unreachable = error instanceof CollectorUnreachableError

  return (
    <Card className="flex flex-col items-center justify-center gap-2 py-16 text-center">
      <p className="text-sm text-ink">
        {unreachable ? 'The devlog collector is not running.' : 'Something went wrong reading the log.'}
      </p>
      <p className="max-w-md text-xs text-faint">
        {unreachable
          ? 'Start it from the tray, or run devlog stats to check on it.'
          : error instanceof Error
            ? error.message
            : String(error)}
      </p>
      <PillButton onClick={onRetry} className="mt-2 px-4 py-1.5">
        Retry
      </PillButton>
    </Card>
  )
}
