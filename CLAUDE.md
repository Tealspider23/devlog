# devlog — standing instructions

Read this before editing anything. It exists because this project accumulated
real conventions over several sessions, and they stopped being obvious to
anyone — including me — the moment they weren't being said out loud each time.

If something here conflicts with an old habit, this file wins. If something
here turns out to be wrong, fix it here, not just in the code.

---

## The process rule that overrides everything else

**Ask before starting each build phase. Never chain phases automatically.**

At the end of a phase: report what was built, show the checkpoint result, then
stop and wait. The user decides whether to proceed, adjust the next phase, or
stop entirely. This has been the standing agreement since Phase 1 and it still
holds.

**The plan is a living document, not a spec written once.** It lives at
`C:\Users\<you>\.claude\plans\ok-make-a-plan-synthetic-spindle.md`. When a
phase surfaces something that invalidates a downstream assumption — and it
often does — say so, revise the plan file, and re-confirm before continuing.
Don't quietly build to a plan you already know is stale.

**Give it a shape, not an analysis, until the LLM exists.** (2026-09-03) The UI
and any deterministic code should render what already exists. Don't build
classification UI, insight generation, or "what does this mean" logic ahead of
the model that's meant to do that job — see `docs/LLM.md`. Blindly adding
speculative analysis features is explicitly not wanted.

---

## Architecture invariants — do not casually break these

**Raw is source of truth. Derived is disposable.**
`raw_event` stores raw window titles and raw `idle_seconds` — never a
precomputed category, never an idle boolean. `activity`, `session`, and
`commit_record` are dropped and rebuilt wholesale on every derivation. This is
what makes every threshold and rule retunable by re-deriving instead of
re-collecting. Never add a column to a derived table that can't be rebuilt from
raw data plus config.

**`Devlog.Core` has zero dependencies.** No SQLite, no Win32, no ASP.NET. This
is what keeps derivation unit-testable without a database, and it's the layer
that gets iterated on most. Don't let a convenience import erode it.

**`Devlog.Api` depends only on `Devlog.Core`** — never on `Devlog.Infrastructure`
(Windows-only) or `Devlog.Host` (would be circular, since `Devlog.Host` already
references `Devlog.Api` to map routes). When an endpoint needs something that
currently only exists as a concrete class in Infrastructure or Host (e.g.
`ClassificationRuleStore`, `DerivationRunner`), the fix is: define an interface
in `Devlog.Core.Abstractions` covering only what the API needs, implement it on
the existing concrete class, and register the interface a second way in DI
pointing at the same singleton. Not a new object — a second door into the one
that exists. `ISessionReader`, `IClassificationRuleStore`, `IDerivationRunner`
are the precedent.

**Two executables, deliberately separate, never merge them:**
- `Devlog.Host.exe` — the collector. Tray icon, owns the Win32 hooks, hosts the
  API in-process. Takes no arguments. The *only* thing that ever writes to
  `raw_event`. Single-instance enforced by a named Mutex — a second instance
  would double-record every focus change, which has already happened once.
- `devlog` (`Devlog.Cli`) — reads and rebuilds. Console-subsystem so it can be
  piped and redirected, unlike the WinExe tray app. **Must never be able to
  start the collector.** An unrecognised command exits 2; it does not fall
  through to tray mode. That fall-through is exactly how the duplicate-collector
  incident happened.

**The API is loopback-only, always.** `IPAddress.Loopback`, never `0.0.0.0`,
never configurable to anything else. Every route but `/health` requires the
token in `api-token.txt` (gitignored, generated on first run, never printed to
a log or console). This is not paranoia — a page you visit in any browser can
address `127.0.0.1` and the browser will let the request through. Loopback
stops other computers, not other websites.

**Timestamps: UTC unix milliseconds in the database and domain layer, always.**
Never a local `DateTime` in `Devlog.Core.Domain`. Conversion to local time and
to ISO 8601 strings happens only at the API boundary (`Devlog.Api.Contracts`)
and in the CLI's own formatting code — never earlier.

**The CLI and the API must never be able to disagree about what a session is.**
Both render through `ISessionReader` — one query, two renderers. If a new field
is needed for one, extend the shared reader, don't build a second query path.

---

## Privacy and what gets committed

- `backend/tests/` **is committed** — `Devlog.slnx` references those projects,
  so a clone cannot build without them. Every fixture string in there is
  synthetic and chosen to preserve the property its test guards; a real project
  name, server, domain or colleague never goes in, even in a comment.
- `backend/src/Devlog.Host/Seed/` stays gitignored — nothing outside that folder
  references `EventSeeder`, so the Host builds without it. Don't `git add -f` it.
- `docs/llm-evals/*.json` is gitignored: those are real captured sessions,
  hand-labelled. Only `docs/llm-evals/README.md`, whose examples are fabricated,
  is committed.
- Real local repo paths, API keys, and any machine-specific config belong in
  `appsettings.local.json` (gitignored) — never in the public `appsettings.json`.
- `api-token.txt` is gitignored. Never echo it to console output, logs, or a
  committed file. `devlog config` shows *whether* it exists, never its value.
- The GitHub repo (`Tealspider23/devlog`) is **public**. Assume anything
  committed is visible to anyone.
- Window titles are genuinely sensitive — server names, colleague names, ticket
  contents. The user has explicitly declined redaction of sensitive titles in
  their own local data ("we will have some another KPI for the same") — that
  was a decision about their own machine, not a license to relax privacy
  defaults in the code (`ExcludedProcesses`, `ExcludedTitlePatterns`,
  `[excluded]` placeholder) which stay conservative by default.
- Git identity for this repo is local to it: `Tealspider23` /
  `123721031+Tealspider23@users.noreply.github.com`. Never touch the global
  work identity (the machine's own `user.email`, configured outside this repo).

---

## Config file convention

Every real key in `appsettings.json` gets a `"//KeyName"` sibling string
explaining what it does and why the default is what it is. Section-level
context goes in a `"//_sectionName"` key at the top of the section. Follow the
existing sections (`Devlog`, `Derivation`, `Git`, `Api`) as the template.

---

## Verification bar before calling a step done

- `dotnet build backend\Devlog.slnx` clean, `dotnet test` green — currently
  254 tests (212 Core + 42 Host). Check the actual number; it grows.
- For anything touching output the CLI already prints: capture the baseline
  first, diff after. A refactor that changes formatting wasn't "just a
  refactor" — say so.
- For anything touching the API: verify live, not just "it builds" — hit the
  actual endpoints (401 with no/wrong token, 200 with the right one), check
  `netstat` for the bind address, don't take the code's word for it.
- Don't claim a fix based on reasoning alone when it's cheap to check for real
  (e.g. hook-timing regressions, security bindings). If a live check genuinely
  isn't possible in the environment, say so explicitly rather than asserting
  success.

---

## Style

- No emojis unless the user asks.
- Comments explain *why*, only when non-obvious — a hidden constraint, a past
  bug, a subtle invariant. Never *what* the code does; identifiers do that.
- Don't add speculative abstraction, config knobs, or error handling for cases
  that can't happen. Three similar lines beat a premature helper.
- When a bug is found by inspecting real captured data rather than by
  assumption, that's the pattern to keep using — this project has repeatedly
  been wrong about its own assumptions until real data was checked (identity
  counts, session fragmentation, hook timing, config loading).

---

## Where things are

- Plan (source of truth for "what's next"): the Claude plan file mentioned
  above, not this file.
- Architecture notes: `docs/architecture.md`, `docs/sessionization.md`.
- LLM contract (for the Omen agent, when that work starts): `docs/LLM.md`.
- Install/publish: `scripts/install.ps1` — publishes both exes to
  `%LOCALAPPDATA%\devlog\bin` and adds it to PATH. Restarting the collector
  after a rebuild is a separate, deliberate step, not automatic.
