import type { NarrativeKind } from '../types/api'

/**
 * Colours reused from lib/categories.ts / index.css wherever the meaning
 * genuinely matches, so a colour never means two things: feature-work gets
 * the accent (it produced), context-thrash gets warn (consumed, not
 * produced) — the same lime/orange rule the timeline already uses.
 */
export const KIND_COLOR: Record<NarrativeKind, string> = {
  'feature-work': '#c3f53c', // accent
  bugfix: '#5fd3f3', // cat-learning
  'mr-review': '#a78bfa', // cat-communication
  research: '#fbbf24', // cat-personal
  'meeting-followup': '#f472b6', // cat-meeting
  admin: '#94a3b8', // cat-filemanagement
  'context-thrash': '#ff8c42', // warn
  unclear: '#3f3f46', // cat-other — hatched treatment, "not yet known"
}

export const KIND_LABEL: Record<NarrativeKind, string> = {
  'feature-work': 'Feature work',
  bugfix: 'Bugfix',
  'mr-review': 'MR review',
  research: 'Research',
  'meeting-followup': 'Meeting follow-up',
  admin: 'Admin',
  'context-thrash': 'Context thrash',
  unclear: 'Unclear',
}

/** Bucketed, never a raw percentage — confidence is model-reported, not a measured figure. */
export function confidenceBucket(confidence: number): 'low' | 'medium' | 'high' {
  if (confidence < 0.5) return 'low'
  if (confidence < 0.8) return 'medium'
  return 'high'
}
