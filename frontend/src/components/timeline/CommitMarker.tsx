import type { CommitDto } from '../../types/api'

/**
 * Commits sit on the strip, not in a list beside it — that adjacency is the
 * whole thesis: attention on one axis, output on the same one.
 */
export function CommitMarker({
  commit,
  windowStart,
  span,
}: {
  commit: CommitDto
  windowStart: number
  span: number
}) {
  const t = new Date(commit.timestampIso).getTime()
  const left = ((t - windowStart) / span) * 100

  return (
    <div
      className="absolute -top-3 -translate-x-1/2 text-accent"
      style={{ left: `${left}%` }}
      title={`${commit.message ?? commit.sha.slice(0, 7)}\n+${commit.insertions}/-${commit.deletions}  ${commit.branch ?? ''}`}
    >
      <svg width="8" height="8" viewBox="0 0 8 8" className="drop-shadow-[0_0_2px_rgba(0,0,0,0.6)]">
        <path d="M4 0 L8 8 L0 8 Z" fill="currentColor" />
      </svg>
    </div>
  )
}
