import type { CategoryTimeDto, ProjectTimeDto } from '../../types/api'
import { CATEGORY_COLOR, CATEGORY_LABEL } from '../../lib/categories'
import { formatHours } from '../../lib/format'
import { Tooltip } from '../common/Tooltip'

const pctOf = (seconds: number, total: number) => (total > 0 ? Math.round((seconds / total) * 100) : 0)

/** Time-by-project or time-by-category, computed by the backend and previously unrendered. */
export function BreakdownList({
  title,
  projects,
  categories,
}: {
  title: string
  projects?: ProjectTimeDto[]
  categories?: CategoryTimeDto[]
}) {
  const total = projects
    ? projects.reduce((sum, p) => sum + p.seconds, 0)
    : (categories ?? []).reduce((sum, c) => sum + c.seconds, 0)

  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs text-faint">{title}</span>
      <div className="flex flex-col gap-1.5">
        {projects?.map((p) => (
          <Tooltip
            key={p.project}
            label={
              <span>
                {p.project} — {formatHours(p.seconds)} ({pctOf(p.seconds, total)}%)
              </span>
            }
          >
            <div className="-mx-1 flex items-center gap-2 rounded-md px-1 text-xs transition-colors hover:bg-raised/50">
              <span className="w-24 shrink-0 truncate text-muted">{p.project}</span>
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-line">
                <div
                  className="h-full rounded-full bg-accent"
                  style={{ width: `${pctOf(p.seconds, total)}%` }}
                />
              </div>
              <span className="w-10 shrink-0 text-right text-faint">{formatHours(p.seconds)}</span>
            </div>
          </Tooltip>
        ))}
        {categories?.map((c) => (
          <Tooltip
            key={c.category}
            label={
              <span>
                {CATEGORY_LABEL[c.category]} — {formatHours(c.seconds)} ({pctOf(c.seconds, total)}%)
              </span>
            }
          >
            <div className="-mx-1 flex items-center gap-2 rounded-md px-1 text-xs transition-colors hover:bg-raised/50">
              <span className="w-24 shrink-0 truncate text-muted">{CATEGORY_LABEL[c.category]}</span>
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-line">
                <div
                  className="h-full rounded-full"
                  style={{
                    width: `${pctOf(c.seconds, total)}%`,
                    backgroundColor: CATEGORY_COLOR[c.category],
                  }}
                />
              </div>
              <span className="w-10 shrink-0 text-right text-faint">{formatHours(c.seconds)}</span>
            </div>
          </Tooltip>
        ))}
      </div>
    </div>
  )
}
