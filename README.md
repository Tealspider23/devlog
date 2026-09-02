# devlog

A local-first developer activity logger that correlates **what you paid attention to** against **what you actually shipped** — and turns it into a brag document.

Existing trackers (RescueTime, WakaTime, ActivityWatch) tell you where your hours went. None of them tell you what those hours *produced*. devlog joins your foreground activity to your git history, so a week becomes:

> *18h deep work across 3 repos. 14 commits, 1,840 lines, C# and TypeScript. Longest block 2h40m on the sessionizer. 2.5h reading Redis Streams before implementing it. Interrupted 6.2×/day, mostly Slack.*

**Everything stays on your machine.** No cloud, no accounts, no telemetry, no network egress.

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

Deliberately **not** used: Redis, message brokers, Docker, auth, cloud.

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
devlog derive                         # rebuild from the raw log
devlog unknowns                       # identities awaiting a verdict
devlog classify "Google Search" Other
devlog scan-git 90                    # import commits from configured repos
```

Flags still work — `devlog --sessions 20` is the same as `devlog sessions 20`.

The database lands at `%LOCALAPPDATA%\devlog\devlog.db`. Real local repo paths
belong in `appsettings.local.json` (gitignored), never in `appsettings.json`.

---

## Privacy

- Nothing leaves your machine.
- `ExcludedProcesses` / `ExcludedTitlePatterns` in `appsettings.json` are **never recorded** — not recorded-then-filtered.
- Pause from the tray icon at any time.
- `*.db` is gitignored. Do not commit your activity log.

---

## Status

Built in phases. See [docs/architecture.md](docs/architecture.md) and [docs/sessionization.md](docs/sessionization.md).

- [x] **Phase 1** — collector, storage, seeder
- [x] **Phase 2** — activities + sessionizer + classification
- [x] **Phase 3** — git enrichment
- [ ] **Phase 4** — local API + React UI
- [ ] **Phase 5** — wins + weekly digest
