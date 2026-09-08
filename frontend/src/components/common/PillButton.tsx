import type { ComponentPropsWithoutRef } from 'react'

/** The pill button shape, repeated across ~9 sites: Copy, Refresh, day/range nav, Narrate, Ask, model reload. */
export function PillButton({ className = '', ...rest }: ComponentPropsWithoutRef<'button'>) {
  return (
    <button
      className={`rounded-full border border-line px-3 py-1 text-xs text-muted transition-colors hover:border-accent-dim hover:text-ink disabled:opacity-50 disabled:hover:border-line disabled:hover:text-muted ${className}`}
      {...rest}
    />
  )
}
