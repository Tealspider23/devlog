import { useQuery } from '@tanstack/react-query'
import { getAiModels } from '../../api/ai'
import { qk } from '../../lib/queryKeys'

const STORAGE_KEY = 'devlog.chat.model'

export function loadPreferredModel(configuredDefault: string): string {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored || configuredDefault
  } catch {
    return configuredDefault
  }
}

export function savePreferredModel(model: string) {
  try {
    localStorage.setItem(STORAGE_KEY, model)
  } catch {
    // localStorage can throw in a private window — the preference just won't persist.
  }
}

/**
 * Per-query model override for Chat, never written back as devlog config —
 * NarrateRunner treats a model change as cache invalidation, so a global
 * switch here would quietly stale every stored narrative.
 */
export function ModelPicker({
  configuredDefault,
  value,
  onChange,
}: {
  configuredDefault: string
  value: string
  onChange: (model: string) => void
}) {
  // /v1/ai/models returns [] on any failure — indistinguishable from
  // success-with-zero — so on an empty list we degrade the optional control
  // rather than guess: no select, just the configured default as a chip.
  const { data, refetch, isFetching } = useQuery({
    queryKey: qk.aiModels(),
    queryFn: ({ signal }) => getAiModels(signal),
    staleTime: 5 * 60_000,
  })

  const models = (data?.models ?? []).map((m) => m.replace(/^models\//, ''))
  const unique = [...new Set([configuredDefault, ...models])]

  if (models.length === 0) {
    return (
      <div className="flex items-center gap-2 text-[11px] text-faint">
        <span className="rounded-full border border-line bg-raised px-2 py-0.5 text-muted">{configuredDefault}</span>
        <span>Couldn't list models — using the configured default.</span>
        <button onClick={() => refetch()} disabled={isFetching} className="underline hover:text-ink">
          Retry
        </button>
      </div>
    )
  }

  const suggested = unique.filter((m) => m.includes('gemini') && !/embedding|imagen|image|tts|veo|aqa/.test(m))
  const rest = unique.filter((m) => !suggested.includes(m))

  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="rounded-full border border-line bg-raised px-3 py-1 text-xs text-muted"
    >
      <optgroup label="Suggested">
        {suggested.map((m) => (
          <option key={m} value={m}>
            {m === configuredDefault ? `${m} — configured default` : m}
          </option>
        ))}
      </optgroup>
      {rest.length > 0 && (
        <optgroup label="All models">
          {rest.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </optgroup>
      )}
    </select>
  )
}
