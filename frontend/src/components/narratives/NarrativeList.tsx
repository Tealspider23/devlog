import type { NarrativeDto } from '../../types/api'
import { NarrativeCard } from './NarrativeCard'
import { NarrateButton } from './NarrateButton'
import { Card } from '../common/Card'
import { EmptyState } from '../common/EmptyState'

/** Groups narratives by day, ordered by session start. Sessions without a story are never interleaved — gaps every third row would defeat the point. */
function groupByDay(narratives: NarrativeDto[]): [string, NarrativeDto[]][] {
  const groups = new Map<string, NarrativeDto[]>()
  for (const n of [...narratives].sort((a, b) => a.sessionStart.localeCompare(b.sessionStart))) {
    const day = n.sessionStart.slice(0, 10)
    const list = groups.get(day) ?? []
    list.push(n)
    groups.set(day, list)
  }
  return [...groups.entries()]
}

export function NarrativeList({
  narratives,
  totalSessions,
  from,
}: {
  narratives: NarrativeDto[]
  totalSessions: number
  from: string
}) {
  const days = groupByDay(narratives)

  const allSameKind =
    narratives.length >= 4 && narratives.every((n) => n.kind === narratives[0].kind)

  if (narratives.length === 0) {
    return (
      <EmptyState
        title="No stories written for this range yet."
        detail="Narration reads each session's titles and commits and writes one sentence about it. It runs when you ask it to."
      >
        <div className="mt-3">
          <NarrateButton from={from} hasExisting={false} />
        </div>
      </EmptyState>
    )
  }

  return (
    <Card className="flex flex-col gap-4 p-4">
      <div className="flex items-start justify-between gap-4">
        <span className="text-xs text-faint">Story of the range</span>
        <NarrateButton from={from} hasExisting />
      </div>

      {allSameKind && (
        <p className="text-xs text-faint">
          Every story in this range is "{narratives[0].kind}". Narratives written before the classification fix
          all came out that way — re-write them to reclassify.
        </p>
      )}

      <div className="flex flex-col gap-3">
        {days.map(([day, items]) => (
          <div key={day} className="flex flex-col gap-1.5">
            <span className="text-[11px] uppercase tracking-wide text-faint">
              {new Date(`${day}T00:00:00`).toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' })}
            </span>
            <div className="flex flex-col">
              {items.map((n) => (
                <NarrativeCard key={`${n.sessionId}-${n.sessionStart}`} narrative={n} variant="row" />
              ))}
            </div>
          </div>
        ))}
      </div>

      <span className="text-xs text-faint">
        {narratives.length} of {totalSessions} sessions in this range have a story.
      </span>
    </Card>
  )
}
