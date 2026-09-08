import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { getAiModels } from '../api/ai'
import { useAiStatus } from '../hooks/useAiStatus'
import { PageHeader } from '../components/shell/PageHeader'
import { Card } from '../components/common/Card'
import { PillButton } from '../components/common/PillButton'
import { ErrorState } from '../components/common/ErrorState'
import { ModelPicker, loadPreferredModel, savePreferredModel } from '../components/ai/ModelPicker'
import { qk } from '../lib/queryKeys'

function StatusDot({ tone }: { tone: 'on' | 'off' | 'warn' }) {
  const cls = tone === 'on' ? 'bg-accent' : tone === 'warn' ? 'bg-warn' : 'bg-line'
  return <span className={`h-1.5 w-1.5 rounded-full ${cls}`} />
}

const PRIVACY_ROWS: { job: string; sends: string }[] = [
  { job: 'Identity classification', sends: 'an app or site identity, plus three sample window titles' },
  { job: 'Session narration', sends: "one session's window titles, commit messages, branch names" },
  { job: 'Digest prose', sends: 'narratives and figures devlog already computed — no raw titles' },
  { job: 'Ask', sends: 'whatever the answering query reads' },
]

export function Settings() {
  const { data: status, isPending, isError, error, refetch, dataUpdatedAt } = useAiStatus()

  const modelsQuery = useQuery({
    queryKey: qk.aiModels(),
    queryFn: ({ signal }) => getAiModels(signal),
    staleTime: 5 * 60_000,
    enabled: !!status?.enabled,
  })

  const [defaultModel, setDefaultModel] = useState(() => loadPreferredModel(status?.configuredModel ?? ''))

  // status is still loading on first render if this route is opened
  // directly (e.g. a reload on #/settings) before App's prefetch resolves.
  if (defaultModel === '' && status?.configuredModel) {
    setDefaultModel(loadPreferredModel(status.configuredModel))
  }

  if (isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title="Settings" subtitle="what the AI layer is doing, and what leaves this machine" />
        <ErrorState error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const checkedAgo = dataUpdatedAt ? Math.max(0, Math.round((Date.now() - dataUpdatedAt) / 60_000)) : null

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <PageHeader
        title="Settings"
        subtitle="what the AI layer is doing, and what leaves this machine"
        actions={
          <>
            {checkedAgo !== null && (
              <span className="text-xs text-faint">Checked {checkedAgo === 0 ? 'just now' : `${checkedAgo}m ago`}</span>
            )}
            <PillButton onClick={() => refetch()} disabled={isPending}>
              Re-check
            </PillButton>
          </>
        }
      />

      <Card className="flex flex-col gap-2 p-4">
        <div className="flex items-center justify-between text-xs">
          <span className="text-faint">AI</span>
          <span className="flex items-center gap-1.5 text-muted">
            <StatusDot tone={status?.enabled ? 'on' : 'off'} />
            {status?.enabled ? 'enabled' : 'disabled'}
          </span>
        </div>
        <div className="flex items-center justify-between text-xs">
          <span className="text-faint">Provider</span>
          <span className="flex items-center gap-1.5 text-muted">
            <StatusDot tone={!status?.enabled ? 'off' : status.reachable ? 'on' : 'warn'} />
            {!status?.enabled ? '—' : status.reachable ? `reachable · ${status.host ?? ''}` : 'unreachable'}
          </span>
        </div>
        <div className="flex items-center justify-between text-xs">
          <span className="text-faint">Endpoint</span>
          <span className="text-muted">{status?.endpoint ?? '—'}</span>
        </div>
        <div className="flex items-center justify-between text-xs">
          <span className="text-faint">Model</span>
          <span className="text-muted">{status?.configuredModel ?? '—'}</span>
        </div>
        <div className="flex items-center justify-between text-xs">
          <span className="text-faint">API key</span>
          <span className="text-muted">{status?.apiKeyPresent ? 'present' : 'not found'}</span>
        </div>
      </Card>

      <Card className="flex flex-col gap-2 p-4">
        <span className="text-xs text-faint">Jobs</span>
        {[
          { on: status?.jobClassifyEnabled, label: 'Identity classification', where: 'sorts unknown apps and sites (CLI only today)' },
          { on: status?.jobNarrateEnabled, label: 'Session narration', where: "Today → session detail; Week/Month → Story of the range" },
          { on: status?.jobDigestEnabled, label: 'Digest prose', where: 'Week/Month → Generate summary' },
          { on: status?.jobAskEnabled, label: 'Ask', where: 'Ask route' },
        ].map((j) => (
          <div key={j.label} className={`flex items-center gap-2 text-xs ${j.on ? '' : 'text-faint'}`}>
            <StatusDot tone={j.on ? 'on' : 'off'} />
            <span className={j.on ? 'text-ink' : ''}>{j.label}</span>
            <span className="text-faint">— {j.where}{j.on ? '' : ' — off in configuration'}</span>
          </div>
        ))}
      </Card>

      <Card className="flex flex-col gap-2 bg-raised p-4">
        <span className="text-xs text-faint">What leaves this machine</span>
        <p className="text-sm text-ink">
          {status?.enabled
            ? `Data leaves this machine: YES → ${status.host ?? status.endpoint ?? 'the configured endpoint'}`
            : 'Data leaves this machine: NO — no AI job is enabled.'}
        </p>
        {status?.enabled && (
          <div className="mt-1 flex flex-col gap-1">
            {PRIVACY_ROWS.map((r) => (
              <div key={r.job} className="flex gap-3 text-xs">
                <span className="w-40 shrink-0 text-muted">{r.job}</span>
                <span className="text-faint">{r.sends}</span>
              </div>
            ))}
          </div>
        )}
      </Card>

      <Card className="flex flex-col gap-2 p-4">
        <span className="text-xs text-faint">Models</span>
        {!status?.enabled ? (
          <span className="text-xs text-faint">AI is off — nothing to list.</span>
        ) : modelsQuery.data && modelsQuery.data.models.length > 0 ? (
          <>
            <span className="text-xs text-muted">{modelsQuery.data.models.length} models available</span>
            <details className="text-xs">
              <summary className="cursor-pointer text-faint">Show all</summary>
              <div className="mt-1 flex flex-col gap-0.5 font-mono text-[11px] text-faint">
                {modelsQuery.data.models.map((m) => (
                  <span key={m}>{m.replace(/^models\//, '')}</span>
                ))}
              </div>
            </details>
            <span className="text-xs text-faint">Used by the model picker on Ask.</span>
          </>
        ) : (
          <span className="text-xs text-faint">Couldn't list models. That's usually the same problem as an unreachable provider.</span>
        )}
      </Card>

      <Card className="flex flex-col gap-2 p-4">
        <span className="text-xs text-faint">Default model for Ask</span>
        <ModelPicker
          configuredDefault={status?.configuredModel ?? ''}
          value={defaultModel}
          onChange={(m) => {
            setDefaultModel(m)
            savePreferredModel(m)
          }}
        />
        <span className="text-xs text-faint">Stored in this browser only. It does not change devlog's configuration.</span>
      </Card>

      <p className="text-xs text-faint">
        Read from appsettings.local.json next to devlog.exe. devlog's API has no write path for configuration —
        edit the file and restart the collector. Run <code className="font-mono">devlog llm</code> to test the
        connection from the command line.
      </p>
    </div>
  )
}
