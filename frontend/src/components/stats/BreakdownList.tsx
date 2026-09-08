import type { CategoryTimeDto, ProjectTimeDto } from '../../types/api'
import { CATEGORY_COLOR, CATEGORY_LABEL } from '../../lib/categories'
import { formatHours } from '../../lib/format'

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
          <div key={p.project} className="flex items-center gap-2 text-xs">
            <span className="w-24 shrink-0 truncate text-muted">{p.project}</span>
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-line">
              <div
                className="h-full rounded-full bg-accent"
                style={{ width: `${total > 0 ? (p.seconds / total) * 100 : 0}%` }}
              />
            </div>
            <span className="w-10 shrink-0 text-right text-faint">{formatHours(p.seconds)}</span>
          </div>
        ))}
        {categories?.map((c) => (
          <div key={c.category} className="flex items-center gap-2 text-xs">
            <span className="w-24 shrink-0 truncate text-muted">{CATEGORY_LABEL[c.category]}</span>
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-line">
              <div
                className="h-full rounded-full"
                style={{
                  width: `${total > 0 ? (c.seconds / total) * 100 : 0}%`,
                  backgroundColor: CATEGORY_COLOR[c.category],
                }}
              />
            </div>
            <span className="w-10 shrink-0 text-right text-faint">{formatHours(c.seconds)}</span>
          </div>
        ))}
      </div>
    </div>
  )
}
