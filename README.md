# devlog

A local-first developer activity logger that correlates **what you paid attention to** against **what you actually shipped** — and turns it into a brag document.

Existing trackers (RescueTime, WakaTime, ActivityWatch) tell you where your hours went. None of them tell you what those hours *produced*. devlog joins your foreground activity to your git history, so a week becomes:

> *18h deep work across 3 repos. 14 commits, 1,840 lines, C# and TypeScript. Longest block 2h40m on the sessionizer. 2.5h reading Redis Streams before implementing it. Interrupted 6.2×/day, mostly Slack.*

**Core tracking and derivation stay fully local, always.** No accounts, no telemetry, no sync. The one deliberate exception: the AI layer (narration, classification, digest prose, chat) is off by default and, once turned on, sends window titles, commit messages and branch names to whichever model provider is configured — see [Privacy](#privacy) below.

---

## How it works

```
 Win32 foreground window
          │
          ▼
    ┌───────────┐   raw, unnormalized
    │ raw_event │   ~800 rows/day        SOURCE OF TRUTH (append-only)
    └───────────┘
          │  derive
          ▼
    ┌───────────┐   one continuous stretch of one context
    │ activity  │   ~300/day             DERIVED (disposable)
    └───────────┘
          │  derive
          ▼
    ┌───────────┐   a meaningful unit of work
    │  session  │   ~30/day              DERIVED (disposable)
    └───────────┘
          ▲
          │  joined by timestamp overlap
    ┌───────────┐
    │  commits  │   from LibGit2Sharp    ARTIFACTS
    └───────────┘
```

### Activity vs artifact

A commit is **not** an activity — it's an **artifact**. The collector only ever sees focus changes (`Code` → `Terminal` → `Code`); it has no idea a commit happened. The git scanner discovers commits independently and the two are joined by timestamp. Keeping those axes separate is what allows *"4 hours spent, 200 lines shipped"*.

### Raw is source of truth, derived is disposable

`raw_event` stores **raw window titles** and **raw `idle_seconds`** — never a pre-computed category or an idle boolean. Every threshold and normalization rule is a config value, so changing your mind costs a re-derivation (`POST /v1/derive`), never a re-collection.

---

## Stack

| Layer | Choice |
|---|---|
| Collector + API | .NET 10 worker, Win32 P/Invoke, tray app (user session) |
| Storage | SQLite (WAL) + Dapper + hand-rolled migrations |
| Git | LibGit2Sharp |
| API | ASP.NET Core minimal API on `127.0.0.1`, token-guarded |
| Frontend | React 19, TypeScript, Vite, Tailwind, shadcn/ui, TanStack Query |
| AI (optional) | Any OpenAI-compatible endpoint — local (Ollama, LM Studio) or hosted (e.g. Gemini) |

Deliberately **not** used: Redis, message brokers, Docker, a database server, mandatory cloud.

---

## Running it

```powershell
.\scripts\install.ps1
```

That publishes both executables to `%LOCALAPPDATA%\devlog\bin` and puts `devlog`
on your PATH. Then:

```powershell
devlog                      # what it can do, and whether capture is alive
devlog startup --enable     # run the collector at logon
```

There are two programs, and the split matters:

| | |
|---|---|
| **`Devlog.Host.exe`** | The collector. Lives in the tray, owns the Win32 hooks, and is the only thing that records. Started at logon; takes no arguments. |
| **`devlog`** | Everything else — reads, rebuilds, classifies. Never captures, and deliberately cannot start the collector. |

They are separate because the collector must be a GUI-subsystem app (no console
window at logon), and a GUI-subsystem process does not hold the shell: output
lands after the prompt and cannot be piped or redirected. `devlog` is a console
app, so it behaves like any other command.

```powershell
devlog stats                          # capture health, hook status
devlog sessions 20                    # derived sessions with commits
devlog commits 20                     # commits and which session each attached to
devlog derive                         # rebuild activities/sessions from the raw log
devlog scan-git 90                    # import commits from configured repos
devlog unknowns                       # identities awaiting a verdict
devlog classify "Google Search" Other
devlog unclassify "Google Search"     # delete a stored verdict
devlog config                         # resolved paths, exclusions, configured repos
devlog startup --enable               # run the collector at logon
```

Flags still work — `devlog --sessions 20` is the same as `devlog sessions 20`.

The database lands at `%LOCALAPPDATA%\devlog\devlog.db`. Real local repo paths
belong in `appsettings.local.json` (gitignored), never in `appsettings.json`.

### Opening the dashboard

The frontend is built into the collector itself — no separate server. Once
`Devlog.Host.exe` is running, open the dashboard from the tray icon
("Open dashboard") or just navigate to **http://127.0.0.1:5111** (always
loopback-only, never reachable from another machine).

| Route | Shows |
|---|---|
| **Today** | Timeline of the current day: sessions against commits, deep-work/shipped/interruption stats |
| **Week / Month** | The same, rolled up over a range, plus daily bars, a category/project breakdown, and (Month) per-week AI summaries |
| **Brag Document** | A read-only rollup of high-confidence feature work and first-time-technology moments for a chosen month |
| **Chat** | Ask free-form questions about your own activity data |
| **Settings** | AI provider/model status, and an exact table of what each AI job sends and to whom |

Nav items never disappear based on whether the AI provider is reachable —
only a status dot dims.

### The AI layer (optional, off until you configure it)

Classification, session narration, digest prose, and chat are a separate,
explicitly-triggered layer on top of the core (always-local) tracking. It
targets any OpenAI-compatible endpoint — configure `Ai:Endpoint`,
`Ai:Model`, and (for a hosted provider) `Ai:ApiKey` in the gitignored
`appsettings.local.json`. With no reachable endpoint, devlog is fully
functional and simply reports the AI features as off.

```powershell
devlog llm                            # provider/model/reachability/job status
devlog classify-ai --dry-run          # preview AI verdicts for pending identities
devlog narrate --since 7d             # generate session narratives
devlog digest --week --prose          # deterministic figures + AI prose, as Markdown
devlog ask "what did I ship this week?"
```

`classify-ai` also runs automatically as a step inside the dashboard's
Refresh button — see [Privacy](#privacy) for what that call sends.

---

## Privacy

- **Core tracking never leaves your machine.** Capture, derivation, sessionization, and git enrichment are pure local SQLite reads/writes — no network call, ever.
- `ExcludedProcesses` / `ExcludedTitlePatterns` in `appsettings.json` are **never recorded** — not recorded-then-filtered.
- Pause from the tray icon at any time.
- `*.db` is gitignored. Do not commit your activity log.
- **The AI layer is the one deliberate exception**, and it is off until you point it at a provider. Each job sends only what it needs, and only when explicitly triggered:
  - `classify` / `classify-ai` — a pending window title/process identity, nothing else.
  - `narrate` — a session's window titles and (if any) attached commit messages.
  - `digest --prose` — the same figures already shown deterministically, plus narratives for the range.
  - `ask` — your typed question plus whatever data it needs to answer it.
  - The Settings page in the dashboard shows the exact per-job list. Whether that data may leave your machine under your employer's policy is a question worth checking before you turn the AI layer on — devlog just makes the sending explicit and disclosed, it doesn't decide it's fine.

---

## Status

Built in phases. See [docs/architecture.md](docs/architecture.md), [docs/sessionization.md](docs/sessionization.md), and [docs/LLM.md](docs/LLM.md) (the AI layer's full contract).

- [x] **Phases 1–3** — collector, storage, derivation, sessionizer, git enrichment
- [x] **Phases 4–6** — local API, `devlog` CLI, deterministic digest, React dashboard served from the collector
- [x] **Phases 9–11** — AI layer: classification, session narration, digest prose, natural-language chat
- [x] **Phase 12** — calendar-accurate Week/Month views, narratives surfaced in the UI
- [x] **Phase 13** — newest-first narratives, weekly Win summaries, interactive charts, `classify-ai` in Refresh, the Brag Document route

Deferred, not scheduled: inline session/block labelling, a writable wins
confirm/edit workflow, deeper git analysis (uncommitted-work sampling,
blame-based read-vs-write), eval-driven prompt tuning, an installer, light
mode.
