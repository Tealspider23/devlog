import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { narrate } from '../../api/ai'
import { PillButton } from '../common/PillButton'
import type { NarrateResultDto } from '../../types/api'

/**
 * The narrate trigger. One model call per batch of sessions (not per
 * session — see AiOptions.NarrateBatchSize), sequential, unbounded — so a
 * preflight confirmation is mandatory, naming both the cost and the privacy
 * disclosure. There's no endpoint that reports how many sessions are
 * eligible before running, so the confirmation is honest about that rather
 * than inventing a count.
 */
export function NarrateButton({
  from,
  hasExisting,
  onGenerated,
}: {
  from: string
  hasExisting: boolean
  onGenerated?: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const [result, setResult] = useState<NarrateResultDto | null>(null)
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: (dryRun: boolean) => narrate({ since: from, limit: 50, dryRun }),
    onSuccess: (data, dryRun) => {
      setResult(data)
      setConfirming(false)
      if (!dryRun) {
        queryClient.invalidateQueries({ queryKey: ['narratives'] })
        queryClient.invalidateQueries({ queryKey: ['session'] })
        onGenerated?.()
      }
    },
  })

  if (confirming) {
    return (
      <div className="flex flex-col gap-2">
        <p className="max-w-md text-xs text-faint">
          This calls the model once per session — likely a minute or more. Window titles, commit messages
          and branch names for those sessions leave this machine.
        </p>
        <div className="flex gap-2">
          <PillButton onClick={() => mutation.mutate(false)} disabled={mutation.isPending}>
            {mutation.isPending && mutation.variables === false ? 'Writing…' : hasExisting ? 'Write the missing ones' : 'Write the stories'}
          </PillButton>
          <PillButton onClick={() => mutation.mutate(true)} disabled={mutation.isPending}>
            {mutation.isPending && mutation.variables === true ? 'Previewing…' : 'Preview only'}
          </PillButton>
          <PillButton onClick={() => setConfirming(false)} disabled={mutation.isPending}>
            Cancel
          </PillButton>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <PillButton onClick={() => setConfirming(true)}>{hasExisting ? 'Write the missing ones' : 'Write the stories'}</PillButton>

      {result && (
        <div className="max-w-sm rounded-lg border border-line bg-raised px-3 py-2 text-right text-xs">
          {result.dryRun ? (
            <span className="text-muted">Preview — nothing was saved. {result.acceptedCount} would be written.</span>
          ) : result.stoppedEarly ? (
            <span className="text-warn">
              Wrote {result.acceptedCount} stories, then stopped — provider rate limit reached. The rest are
              untouched, not skipped; run again later to pick them up.
            </span>
          ) : result.acceptedCount > 0 && result.rejectedCount === 0 ? (
            <span className="text-ink">Wrote {result.acceptedCount} stories.</span>
          ) : result.acceptedCount > 0 ? (
            <span className="text-ink">
              Wrote {result.acceptedCount} stories. {result.rejectedCount} session
              {result.rejectedCount === 1 ? '' : 's'} skipped.
            </span>
          ) : (
            <span className="text-warn">
              Nothing was written. All {result.rejectedCount} session{result.rejectedCount === 1 ? '' : 's'} skipped.
            </span>
          )}
          {result.outcomes.some((o) => !o.accepted) && (
            <details className="mt-1 text-left">
              <summary className="cursor-pointer text-faint">why</summary>
              <div className="mt-1 flex flex-col gap-1 text-faint">
                {result.outcomes
                  .filter((o) => !o.accepted)
                  .map((o) => (
                    <div key={o.sessionId}>
                      {o.project ?? 'session'} — {o.rejectionReason}
                    </div>
                  ))}
              </div>
            </details>
          )}
        </div>
      )}
    </div>
  )
}
