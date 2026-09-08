import type { ComponentPropsWithoutRef } from 'react'

/** The `rounded-[var(--radius-card)] border border-line bg-surface` shape, repeated across ~12 sites in the finished app. */
export function Card({ className = '', ...rest }: ComponentPropsWithoutRef<'div'>) {
  return <div className={`rounded-[var(--radius-card)] border border-line bg-surface ${className}`} {...rest} />
}
