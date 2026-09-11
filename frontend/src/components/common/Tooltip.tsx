import type { ReactNode } from 'react'

/**
 * Pure-CSS hover tooltip — `group`/`group-hover`, no JS measurement, no
 * portal. Matches DailyBars' existing "flex handles it, no viewBox" stance:
 * a chart library or positioning library isn't earned by a handful of
 * evenly-spaced bars/rows. Centered above the trigger, which can clip at the
 * viewport edge on the outermost bar in a narrow window — accepted, since
 * there are at most 31 bars in a fixed-width card and JS edge-detection
 * isn't worth it for that.
 */
export function Tooltip({ label, children }: { label: ReactNode; children: ReactNode }) {
  return (
    <div className="group relative w-full">
      {children}
      <div className="pointer-events-none absolute bottom-full left-1/2 z-10 mb-1.5 -translate-x-1/2 scale-95 whitespace-nowrap rounded-md border border-line bg-raised px-2 py-1 text-[11px] text-ink opacity-0 shadow-lg transition-all duration-100 group-hover:scale-100 group-hover:opacity-100">
        {label}
      </div>
    </div>
  )
}
