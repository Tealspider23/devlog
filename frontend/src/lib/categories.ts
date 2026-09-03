import type { ActivityCategory } from '../types/api'

/**
 * One definition per category, consumed by the timeline band, the legend and
 * session detail alike. Colours mirror the CSS custom properties in
 * index.css (--color-cat-*) — kept in sync by hand since Tailwind v4's
 * @theme values are not importable into TS.
 */
export const CATEGORY_COLOR: Record<ActivityCategory, string> = {
  Coding: '#c3f53c',
  Learning: '#5fd3f3',
  Communication: '#a78bfa',
  Meeting: '#f472b6',
  FileManagement: '#94a3b8',
  Distraction: '#ff8c42',
  Personal: '#fbbf24',
  Other: '#3f3f46',
}

export const CATEGORY_LABEL: Record<ActivityCategory, string> = {
  Coding: 'Coding',
  Learning: 'Learning',
  Communication: 'Communication',
  Meeting: 'Meeting',
  FileManagement: 'Files',
  Distraction: 'Distraction',
  Personal: 'Personal',
  Other: 'Unclassified',
}

/** Deep/producing work vs everything else — the meaning behind the lime/orange pair. */
export function isProductiveCategory(category: ActivityCategory): boolean {
  return category === 'Coding' || category === 'Learning'
}
