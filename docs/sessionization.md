# Sessionization

How raw focus events become meaningful work sessions, what the thresholds are,
and — more usefully — what failed on the way.

Written during Phase 2, against real capture from 2026-08-30.

---

## The pipeline

```
raw_event  ──filter──▶  ──span──▶  ──merge──▶  activity  ──group──▶  session
  667                     noise      same-key      126                  94
                          dropped     + blips
                            9
```

Four stages, in this order. **The order is load-bearing** — see *Cascade merging*
below.

---

## 1. Noise filtering

Runs **before** spans are computed, so activities either side of a dropped row
become adjacent and merge across the hole.

Dropped: `ShellExperienceHost` (toast notifications), `LockApp`, `TextInputHost`,
`SearchHost`, `StartMenuExperienceHost`, `ApplicationFrameHost`, plus explorer's
`System tray overflow window.` / `Program Manager` / null-title rows and
`New tab - Google Chrome`.

Structural events (`lock`, `suspend`, `collector_stop`) are **never** dropped.
They carry the timeline's boundaries; removing them lets durations run straight
through a shutdown.

**This is not privacy filtering.** Privacy exclusions run at capture and are never
stored at all. Noise rules run here because they get retuned every time Windows
invents a new popup, and filtering at capture would mean re-collecting the week
each time.

Effect on real data: 667 → 658 rows, about 1.3%. Lower than the ~15% estimated
from eyeballing `--events`, because much of what looked like noise there was
heartbeat rows rather than shell chrome.

---

## 2. Span computation and terminators

Each observation runs until the next event of any kind.

**`collector_stop`, `lock` and `suspend` end the open span and start nothing.**

This is the single most important rule in the file. Before it existed, a naive
gap-to-next calculation produced this:

```
12:10:13   9h44m   trimstray/the-book-of-secret-knowledge — Google Chrome
```

A browser tab credited with nearly ten hours, because the span ran straight
through a reboot. One duration like that corrupts every KPI downstream — deep
work hours, focus ratio, time per project, all of it.

`ActivityBuilderTests` asserts explicitly that no span crosses any of the three
terminators. Those tests are the reason this rule cannot quietly regress.

The **final** event in the log is also dropped rather than extended, because its
extent is genuinely unknown. Guessing one is how unbounded spans get created.

---

## 3. Context extraction

The stable identity inside a volatile title, so a 90-minute refactor across
twelve files stays one activity instead of twelve.

| Process | Format | Where the project is |
|---|---|---|
| `Code` | `{file} - {project} - Visual Studio Code` | second-to-last segment |
| `Code` | `✻ [Claude Code] C:\…\repos\devlog\…` | **only in the path** |
| `Antigravity IDE` | `{project} - Antigravity IDE[ - {file}]` | **first** segment |
| `devenv` | `{project} - Microsoft Visual Studio` | before the suffix |
| `WindowsTerminal` | `user@HOST: ~/source/repos/devlog/backend` | repo root, not cwd |
| `chrome` | `{page} - Google Chrome` | *(no project — a site)* |
| `explorer` | `{folder} - File Explorer` | folder name |

### What failed

**Splitting on `-` instead of `" - "`.** Breaks every hyphenated project name —
`orderbook-api` became `orderbook`. Splitting on space-hyphen-space keeps it
intact.

**Assuming one IDE layout.** VS Code puts the project second; Antigravity IDE
puts it **first**. Applying VS Code's rule to Antigravity attributed all that time
to a project called "Antigravity IDE".

**Assuming every editor title contains its project.** VS Code's terminal panel
shows `✻ [Claude Code] C:\…\repos\devlog\backend\src\…` with no ` - devlog - `
anywhere. Without a path fallback, every terminal-panel row is silently orphaned
— and that is most of a Claude Code session.

**The path fallback marker list — this one bit twice.** The first version was
`repos|projects|source|src|git|dev|workspace`. Both `source` and `src` are
actively harmful:

```
~/source/repos/devlog                    →  matched /source/  →  project "repos"
…/repos/devlog/backend/src/Devlog.Host   →  matched /src/     →  project "Devlog.Host"
```

Regex alternation takes the **leftmost** match, not the most specific one, so
ordering the alternatives does not help. The only fix is to not list ambiguous
segments at all: `repos|repositories|projects|workspace|dev`.

Caught by `ContextExtractorTests`, not by inspection. Fixing it merged 14 sessions
that had been wrongly split by subdirectory (108 → 94).

**Using the terminal's current directory.** `~/source/repos/devlog/backend` gave
a project called `backend`, so one repo fragmented into a session per
subdirectory you `cd` into. Resolves to the repo root now.

---

## 4. Engagement — the finding that reshaped the design

The original model was:

- *producing* = recent input
- *consuming* = **no** input, foreground stable → i.e. reading

**This is wrong, and real data proved it.** A genuine documentation-reading
session (MCP docs, 21:57–21:58) reported `idle_seconds = 0` on every single row,
because **scrolling is input**. `GetLastInputInfo` cannot distinguish reading from
typing.

Under the original rule, `consuming` would almost never fire for the activity it
was built to detect — and every hour of learning would have been counted as
coding. For a tool whose output is a brag document, that is precisely backwards:
it would erase the learning time you most want evidence of.

**So engagement comes from the activity's category**, and idle is used only for
what it is genuinely good at: detecting absence. A real lock showed
`idle = 5526s`, which is unambiguous.

```
Away    ← bounded by lock/suspend
Idle    ← idle_seconds >= AwayIdleSeconds
        ← otherwise, by category:
Producing   Coding, FileManagement
Consuming   Learning, Communication, Meeting
```

Storing the raw measurement rather than a derived flag is what made this
revision free — no re-collection, just a re-derivation.

---

## 5. Merging, and why it must loop

Consecutive spans sharing an activity key collapse into one activity, counting
the title changes they hid. Then activities shorter than `MinActivitySeconds`
fold into their longer neighbour.

**Blip merging must repeat until stable.** Absorbing one blip can make two
same-key neighbours adjacent, which must then merge, which can expose another
blip. A single pass leaves the timeline half-collapsed:

```
Code(devlog) · explorer(2s) · Code(devlog) · explorer(2s) · Code(devlog)
   pass 1 →  Code(devlog) · Code(devlog) · Code(devlog)
   pass 2 →  Code(devlog)                                  ← only now correct
```

`CascadingBlips_CollapseCompletely` covers this.

---

## 6. Sessions — project-scoped with excursion folding

Chosen over two alternatives:

| Model | Rejected because |
|---|---|
| **Strict project-scoped** | A 20-second glance at a browser splits a two-hour block in half |
| **Time-scoped work blocks** | Good focus metrics, but "I shipped X on project Y" becomes underivable |
| **Project-scoped + folding** ✓ | Keeps per-project totals meaningful *and* survives normal switching |

- Coding sessions are keyed by `(Coding, project)` — two repositories never merge
- Non-coding is keyed by **category alone**, so consecutive documentation pages
  form one learning block rather than a session per page
- A detour under `ExcursionSeconds` that returns to the same context is folded
  back in and counted as an interruption; its time is excluded from `deepSeconds`
- Session ids are assigned in the builder, not by the database, so activities can
  be stamped without a round-trip — and an unchanged rebuild is byte-identical,
  which is what makes idempotency checkable

---

## Thresholds

**All of these are first-draft.** There is still no real deep-work block or long
reading session in the data to tune against — every real row so far is one evening
of rapid switching. Treat them as placeholders.

| Threshold | Value | Reasoning | Confidence |
|---|---|---|---|
| `MinActivitySeconds` | 8 | Residue that capture-time debouncing missed | medium |
| `ExcursionSeconds` | 120 | A lookup is under two minutes; a real context switch is not | **low — untested** |
| `SessionGapMinutes` | 15 | Long enough to survive a coffee, short enough to end a session | **low — untested** |
| `AwayIdleSeconds` | 300 | A real lock showed 5526s; 300 separates that from a pause | medium |

Because `raw_event` stores raw titles and raw idle measurements, changing any of
these costs one `--derive`, never a re-collection. That is the whole point of the
storage split.

---

## Validation

Against the evening of 2026-08-30, which is known in detail from `--events`:

```
21:52–21:54  1m54s  Coding    devlog
21:54–21:57  2m15s  Other     GitHub            ← pending classification
21:57–21:58  1m29s  Learning  MCP docs
21:58–22:01  2m31s  Coding    orderbook-ui      1 interruption
22:01–22:03  2m17s  Coding    orderbook-api
22:03–22:06  3m08s  Coding    devlog
```

*(Project names anonymised.)*

Matches memory. Three distinct projects resolved, the learning block separated
from the coding, and no session anywhere near long enough to suggest a span
crossed a boundary.

### Still open

- **Excursion and gap thresholds are untested against a real workday.** Re-derive
  after a full week and check whether a genuine deep-work block survives intact.
- **Sibling repositories of one product split into separate sessions.** Correct
  under project-scoping, but they are arguably one piece of work. Revisit if it
  proves annoying in the UI.
- **Unclassified time is 4h14m**, almost all seeded browser rows. Real coverage
  will only be clear once local-LLM classification fills the pending identities.
