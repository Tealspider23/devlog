import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { ask } from '../api/ai'
import { useAiStatus } from '../hooks/useAiStatus'
import { useElapsed, formatElapsed } from '../hooks/useElapsed'
import { classifyAskResult, summariseTools, type AskOutcome } from '../lib/askResult'
import { qk } from '../lib/queryKeys'
import { PageHeader } from '../components/shell/PageHeader'
import { Card } from '../components/common/Card'
import { PillButton } from '../components/common/PillButton'
import { Markdown } from '../components/ai/Markdown'
import { ModelPicker, loadPreferredModel, savePreferredModel } from '../components/ai/ModelPicker'
import { CollectorUnreachableError } from '../api/client'

const STARTERS = [
  'What did I actually ship last week?',
  'Which project got most of my deep work this month?',
  'What was my longest uninterrupted session in the last 30 days, and what came out of it?',
  'Turn my last week into three bullets for a standup.',
  'Where did my time go on the days I shipped nothing?',
  'Was I interrupted more this week than last?',
  'What work do I have no commits to show for — reviews, debugging, meetings?',
  'Which identities are still unclassified?',
]

interface Turn {
  id: string
  question: string
  status: 'pending' | 'stopped' | 'done'
  startedAt: number
  elapsedAtStop?: number
  outcome?: AskOutcome
}

function TurnCard({ turn }: { turn: Turn }) {
  const elapsed = useElapsed(turn.status === 'pending')

  return (
    <Card className="flex flex-col gap-3 p-4">
      <p className="text-sm text-ink">{turn.question}</p>

      {turn.status === 'pending' && (
        <div className="flex flex-col gap-1 border-t border-line pt-3">
          <div className="h-0.5 w-full animate-pulse rounded-full bg-accent/30" />
          <span className="tabular-nums text-xs text-faint">
            {elapsed < 45
              ? `Thinking… ${formatElapsed(elapsed)}`
              : elapsed < 150
                ? `Still working. Up to six lookups can run in sequence. ${formatElapsed(elapsed)}`
                : `This is longer than usual — stopping and asking something narrower often helps. ${formatElapsed(elapsed)}`}
          </span>
        </div>
      )}

      {turn.status === 'stopped' && (
        <p className="border-t border-line pt-3 text-xs text-faint">
          Stopped waiting after {formatElapsed(turn.elapsedAtStop ?? 0)}. The collector may still be finishing this
          request.
        </p>
      )}

      {turn.status === 'done' && turn.outcome && <OutcomeView outcome={turn.outcome} />}
    </Card>
  )
}

function OutcomeView({ outcome }: { outcome: AskOutcome }) {
  switch (outcome.kind) {
    case 'answer':
      return (
        <div className="flex flex-col gap-3 border-t border-line pt-3">
          <Markdown source={outcome.answer} />
          <div className="flex flex-wrap items-center gap-2 border-t border-line pt-3 text-[11px] text-faint">
            <span>Answered from</span>
            {outcome.toolsUsed.length > 0 && (
              <span className="rounded-full border border-line px-2 py-0.5">{summariseTools(outcome.toolsUsed)}</span>
            )}
            <span>·</span>
            <span>{outcome.toolRounds} lookup round{outcome.toolRounds === 1 ? '' : 's'}</span>
            {outcome.model && <span>· {outcome.model}</span>}
          </div>
        </div>
      )
    case 'noToolsUsed':
      return (
        <div className="flex flex-col gap-3 border-t border-line pt-3">
          <Markdown source={outcome.answer} />
          <p className="border-t border-line pt-3 text-[11px] text-warn">
            No devlog data was consulted. This answer came from the model's general knowledge, not your log.
          </p>
        </div>
      )
    case 'withheld':
      return (
        <div className="flex flex-col gap-2 border-l-2 border-l-warn border-t border-line pt-3 pl-3">
          <span className="text-xs font-medium text-warn">Answer held back</span>
          <p className="text-sm text-muted">{outcome.message}</p>
          <div className="flex flex-wrap gap-1">
            {outcome.numbers.map((n, i) => (
              <span key={i} className="rounded bg-raised px-1.5 py-0.5 text-[11px] text-warn">
                {n}
              </span>
            ))}
          </div>
          <p className="text-[11px] text-faint">This guard is why the figures you do see can be trusted.</p>
        </div>
      )
    case 'rateLimited':
      return (
        <div className="border-t border-line pt-3 text-sm text-muted">
          <p>The AI provider is rate limiting. Wait a minute and ask again.</p>
          <details className="mt-2 text-xs text-faint">
            <summary className="cursor-pointer">Provider response</summary>
            <pre className="mt-1 whitespace-pre-wrap">{outcome.raw.slice(0, 200)}</pre>
          </details>
        </div>
      )
    case 'modelMissing':
      return (
        <p className="border-t border-line pt-3 text-sm text-muted">
          That model isn't available on this key. Pick another model and ask again.
        </p>
      )
    case 'unreachable':
      return (
        <p className="border-t border-line pt-3 text-sm text-muted">
          Can't reach the AI provider. See Settings to diagnose.
        </p>
      )
    case 'disabled':
      return (
        <p className="border-t border-line pt-3 text-sm text-muted">Ask is turned off in configuration. See Settings.</p>
      )
    case 'timeout':
      return (
        <p className="border-t border-line pt-3 text-sm text-muted">
          The model didn't answer within 120 seconds. A narrower question usually does.
        </p>
      )
    case 'failed':
      return <p className="border-t border-line pt-3 text-sm text-muted">{outcome.message}</p>
  }
}

export function Chat() {
  const queryClient = useQueryClient()
  const { data: aiStatus } = useAiStatus()

  const [transcript, setTranscript] = useState<Turn[]>(
    () => queryClient.getQueryData<Turn[]>(qk.chatTranscript()) ?? [],
  )
  const [question, setQuestion] = useState('')
  const [model, setModel] = useState(() => loadPreferredModel(aiStatus?.configuredModel ?? ''))
  const [collectorDown, setCollectorDown] = useState(false)
  const abortRef = useRef<AbortController | null>(null)

  const persist = (next: Turn[]) => {
    setTranscript(next)
    queryClient.setQueryData(qk.chatTranscript(), next)
  }

  const pending = transcript.some((t) => t.status === 'pending')

  // aiStatus is still loading on first render, so `model` may have
  // initialised empty — fill it in once the configured default arrives.
  // A render-phase update, not an effect: React re-renders immediately with
  // the corrected value rather than flashing an empty picker for a frame.
  if (model === '' && aiStatus?.configuredModel) {
    setModel(loadPreferredModel(aiStatus.configuredModel))
  }

  const onModelChange = (m: string) => {
    setModel(m)
    savePreferredModel(m)
  }

  const send = async (q: string) => {
    if (!q.trim() || pending) return
    const id = crypto.randomUUID()
    const turn: Turn = { id, question: q.trim(), status: 'pending', startedAt: Date.now() }
    persist([turn, ...transcript])
    setQuestion('')

    const controller = new AbortController()
    abortRef.current = controller

    try {
      const res = await ask(q.trim(), model || undefined, controller.signal)
      persist([{ ...turn, status: 'done', outcome: classifyAskResult(res) }, ...transcript])
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') return
      if (err instanceof CollectorUnreachableError) {
        setCollectorDown(true)
        persist(transcript.filter((t) => t.id !== id))
        return
      }
      persist([
        { ...turn, status: 'done', outcome: { kind: 'failed', message: 'The request failed unexpectedly.' } },
        ...transcript,
      ])
    }
  }

  const stop = () => {
    abortRef.current?.abort()
    setTranscript((prev) => {
      const next = prev.map((t) =>
        t.status === 'pending'
          ? { ...t, status: 'stopped' as const, elapsedAtStop: Math.floor((Date.now() - t.startedAt) / 1000) }
          : t,
      )
      queryClient.setQueryData(qk.chatTranscript(), next)
      return next
    })
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      send(question)
    } else if (e.key === 'ArrowUp' && question === '' && transcript.length > 0) {
      e.preventDefault()
      setQuestion(transcript[0].question)
    }
  }

  if (collectorDown) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title="Ask" subtitle="questions answered from your own log, not from memory" />
        <Card className="flex flex-col items-center justify-center gap-2 py-16 text-center">
          <p className="text-sm text-ink">The devlog collector is not running.</p>
          <PillButton onClick={() => setCollectorDown(false)} className="mt-2 px-4 py-1.5">
            Retry
          </PillButton>
        </Card>
      </div>
    )
  }

  if (aiStatus && !aiStatus.enabled) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader title="Ask" subtitle="questions answered from your own log, not from memory" />
        <Card className="flex flex-col gap-3 p-6">
          <p className="text-sm text-ink">Ask is turned off.</p>
          <p className="text-xs text-faint">
            Set Ai:Enabled and Ai:Jobs.Ask in appsettings.local.json, then restart the collector.
          </p>
          <div className="mt-2 flex flex-col gap-1">
            <span className="text-xs text-faint">What you'd be able to ask</span>
            {STARTERS.map((s) => (
              <span key={s} className="text-xs text-faint">
                {s}
              </span>
            ))}
          </div>
        </Card>
      </div>
    )
  }

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <PageHeader title="Ask" subtitle="questions answered from your own log, not from memory" />

      {aiStatus && !aiStatus.reachable && (
        <p className="text-xs text-warn">The AI provider wasn't reachable when last checked. Asking may fail.</p>
      )}

      <Card className="p-4">
        <textarea
          rows={3}
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={onKeyDown}
          placeholder="What did I actually ship last week?"
          className="w-full resize-none bg-transparent text-sm text-ink placeholder:text-faint outline-none"
        />
        <div className="mt-3 flex items-center justify-between">
          <ModelPicker configuredDefault={aiStatus?.configuredModel ?? ''} value={model} onChange={onModelChange} />
          {pending ? (
            <PillButton onClick={stop} className="border-warn/40 text-warn">
              Stop
            </PillButton>
          ) : (
            <PillButton
              onClick={() => send(question)}
              disabled={!question.trim()}
              className="border-line bg-raised px-4 py-1.5"
            >
              Ask
            </PillButton>
          )}
        </div>
      </Card>

      {transcript.length === 0 ? (
        <div className="flex flex-col gap-2">
          <span className="text-xs text-faint">Try one of these</span>
          <div className="flex flex-wrap gap-2">
            {STARTERS.map((s) => (
              <PillButton key={s} onClick={() => setQuestion(s)}>
                {s}
              </PillButton>
            ))}
          </div>
        </div>
      ) : (
        <p className="text-xs text-faint">
          Each question is answered on its own — devlog doesn't carry context between questions.
        </p>
      )}

      <div className="flex flex-col gap-4">
        {transcript.map((t) => (
          <TurnCard key={t.id} turn={t} />
        ))}
      </div>
    </div>
  )
}
