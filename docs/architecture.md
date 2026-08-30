# Architecture

## The layers

"Activity" means different things at different levels, so the vocabulary is fixed:

| Layer | Name | What it is | Volume/day |
|---|---|---|---|
| 0 | **Sample** | What the OS reports at instant T | ~28,000 (never stored) |
| 1 | **Event** | A moment where something *changed* | 500–2,000 (**stored**) |
| 2 | **Activity** | A continuous stretch of one context | 200–600 (derived) |
| 3 | **Session** | A meaningful unit of work | ~30 (derived) |

## Observable surface

At any instant, Win32 gives exactly this. Everything else is inference:

- Foreground window handle, and its title
- Its PID → process name, and (best-effort) exe path
- Seconds since last keyboard/mouse input
- Session locked / unlocked
- Machine suspending / resuming

**Window titles never contain URLs.** That is the hard ceiling on browser data, and the reason a browser extension is a later addition rather than a nice-to-have.

## Definition

> An **Activity** is a maximal continuous time interval during which the **activity key** stays constant, bounded by a focus change, a lock, a suspend, or the collector stopping.

## The activity key

| Candidate | Verdict |
|---|---|
| `process_name` alone | Too coarse — all of Chrome becomes one activity |
| `process_name + window_title` | Too fine — a 90-min refactor shatters into 200 fragments |
| **`process_name + extracted_context`** | **Correct** — stable identity, volatile detail kept as an attribute |

Normalization extracts the stable part:

| Raw title | Stable context | Volatile |
|---|---|---|
| `auth.cs - devlog - Visual Studio Code` | project `devlog` | file `auth.cs` |
| `amit@DESKTOP: ~/source/repos/devlog` | cwd `devlog` | — |
| `Slack \| general \| PalTech` | channel `general` | — |

## Engagement is not binary

Reading docs produces almost no input. A naive *"no input for 2 min = idle"* rule would delete exactly the learning time the brag document most needs.

Five states: **producing** (recent input) · **consuming** (no input, foreground stable and recent) · **idle** · **locked** · **away**.

**Therefore `idle_seconds` is stored as a raw measurement, never as a derived flag.** Thresholds are applied at derivation time, so they can be retuned against already-collected data.

## What is *not* an activity

1. **Sub-threshold blips** — a notification stealing focus for 300ms, alt-tab flicker. Under ~8s merges into its neighbour.
2. **Background processes** — only the foreground window counts.
3. **Excluded contexts** — never recorded at all, not recorded-then-filtered.

## Storage contract

| Table | Status | Rebuilt? |
|---|---|---|
| `raw_event` | source of truth | never — append only |
| `win` | source of truth | never |
| `session_override` | user corrections | never |
| `activity` | derived | dropped & rebuilt on demand |
| `session` | derived | dropped & rebuilt on demand |
| `commit_record` | derived (re-scannable) | rebuilt on demand |

`session_override` is keyed by `(session_start_utc, activity_key)` and re-applied after every rebuild, so manual relabels survive re-derivation.

## Store transitions, not polls

Writing a row on every 3-second poll would produce ~28,000 near-duplicate rows/day. Writing only when the foreground context or state **changes** gives ~500–2,000, and duration is derived from the following row.

A heartbeat row every 5 minutes bounds the damage from an unclean shutdown — without it, a crash mid-session leaves an unbounded open span.

## Project layout

```
Devlog.Core             domain + derivation logic — references NOTHING
Devlog.Infrastructure   SQLite, Win32 P/Invoke, LibGit2Sharp
Devlog.Api              Contracts/ (shared) + Endpoints/ (local)
Devlog.Host             exe: tray, hosted services, seeder, API host
```

`Devlog.Core` has zero package references by design. Derivation is the code that changes most, so it must be testable without a database or an OS.

## Gotchas that cost time if missed

1. **Never run the collector as a Windows Service.** Services live in session 0, isolated from the user desktop — `GetForegroundWindow` returns nothing useful. It must run in the user session as a tray/startup app.
2. **WAL mode before the first write.** A second process (the UI) shares this file.
3. **`Process.MainModule` throws Access Denied** for elevated processes when you aren't elevated. `exe_path` is best-effort; `process_name` still resolves.
4. **`Process.GetProcessById` throws** if the process exits between PID lookup and query.
5. **UTC unix milliseconds**, never local `DateTime`.
