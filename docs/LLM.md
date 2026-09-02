# The LLM in devlog

What the model is for, what it must never be asked, and the exact contract it
answers on.

This file is written to be handed to an agent building against the model. It is
the only spec that matters for the AI side — everything else in `docs/` describes
the deterministic pipeline that runs whether a model exists or not.

---

## 1. The premise

devlog already knows *where your attention went* and *what you shipped*. What it
cannot do is read a sequence and tell you what was going on.

> You are in Teams. You switch to GitLab. You open the code. A commit lands.

Every existing rule sees three unrelated things and one artifact. A person sees a
merge request being picked up and fixed. **Closing that gap is why an LLM is in
this project.** It is not here to sort browser tabs into buckets — a substring
match already does that, for free.

---

## 2. What the LLM must never be asked

Non-negotiable, and the reason the AI cost stays near zero.

**Anything a substring can answer.** Phase 4a.1 removed 37 of 46 pending
identities using 28 keyword rules and no inference at all — unclassified time
went from 4h21m to 1h40m. A verdict a rule can reach is not worth a token.

**Anything arithmetic.** Every number in a digest is computed in C# and passed
in to be quoted verbatim. A model that writes *"18 hours"* when it was 14 has
destroyed the credibility of the one document this project exists to produce.

**Anything already stored.** Ticket IDs come out of branch names
(`fix/US-1569-Bug_Fixing`) with a regex. Project names come from path resolution.
Durations come from timestamps. Feed these to the model as *facts*, never as
questions.

### Precedence — enforced at write time, not just at read time

```
manual  >  llm  >  builtin-keyword  >  builtin-process  >  pending
```

An `llm` verdict may never overwrite a `manual` one. This is checked when
writing, because a read-time-only check leaks the moment anything else touches
the table.

---

## 3. The provider is a plug-in

devlog does not ship a model and does not assume one exists.

**Resolution order:**

1. `Ai:Endpoint` in `appsettings.local.json` — an explicit, user-supplied
   OpenAI-compatible endpoint
2. Probe `http://127.0.0.1:11434/v1` (Ollama) and `http://127.0.0.1:1234/v1`
   (LM Studio), 3-second connect timeout
3. **Disabled** — and it says so

With no provider, devlog is fully functional: capture, derivation, git
correlation, sessions, the timeline, deterministic metrics. The AI features are
simply reported as off. **It never silently degrades**, because silence is
indistinguishable from a quiet day, and that is the failure mode this whole
project is built to avoid.

```json
"Ai": {
  "Enabled": true,
  "Endpoint": "http://100.x.y.z:11434/v1",
  "Model": "gpt-oss:20b",
  "ApiKey": null,
  "ConnectTimeoutSeconds": 3,
  "RequestTimeoutSeconds": 120,
  "Jobs": { "Classify": true, "Narrate": true, "Digest": true, "Ask": true }
}
```

Any OpenAI-compatible server works — Ollama, LM Studio, vLLM, llama.cpp, or a
hosted provider. Moving between them is **two lines of config and zero lines of
code**. That is the entire reason the client is a plain `HttpClient` against
`/v1/chat/completions` rather than a vendor SDK.

### What leaves the machine

Stated plainly, because window titles are unusually sensitive — they contain
colleague names, database server names, and ticket contents.

| Job | Sent |
|---|---|
| A · Classify | a site identity plus up to 3 sample titles |
| B · Narrate | one session's titles, commit messages, branch names |
| C · Digest | narratives and pre-computed metrics — no raw titles |
| G · Ask | whatever the answering query touches |

With a self-hosted endpoint this stays on your own hardware. The table exists so
that a future decision to point at a hosted provider is made deliberately rather
than by accident.

---

## 4. The model: gpt-oss-20b

| | |
|---|---|
| Licence | Apache 2.0 |
| Parameters | ~21B total, ~3.6B active (mixture of experts) |
| Practical speed | closer to a 4B dense model than a 20B one |
| Memory | fits a 16GB card at MXFP4 |
| Context | ~128k |
| Tool calling | native |
| Reasoning effort | configurable — low / medium / high |

*Verify these on the Omen before depending on them.*

Two properties matter more than the parameter count:

**Configurable reasoning effort** maps directly onto the job table. Job A is a
lookup and should not burn thinking tokens; Job B is genuine inference and
should. One model serves both by changing a single field.

**128k context** is what makes Job C possible in a single call — a full week of
narrated sessions and metrics fits without chunking, so the digest can actually
compare Tuesday to Thursday instead of summarising summaries.

### Sizing, honestly

| Job | Minimum that works | Why |
|---|---|---|
| A · Classify | 3B | pattern matching with a category list |
| B · Narrate | **8B** | multi-step inference over a sequence |
| C · Digest | **8B**, long context | prose quality is the deliverable |
| G · Ask | 7B with reliable tool calling | schema adherence matters more than prose |

Job A never needed a GPU box. **Jobs B, C and G are where one earns its place.**

---

## 5. The four jobs

| | Job | Cached | Effort | Volume | Storage |
|---|---|---|---|---|---|
| **A** | Identity classification | per identity, forever | low | ~9 now, a few a day | `classification_rule` (exists) |
| **B** | Session narration | per session | high | ~5 a day worth narrating | `session_narrative` (**new**) |
| **C** | Weekly digest | per period | high | 1 a week | markdown file |
| **G** | Natural-language query | not cached | medium + tools | on demand | none |

---

### Job A — Identity classification

**Question:** *what category is this thing?*

Answered once per identity and cached forever. This is why it is cheap: three
pages of one docs site are one verdict, not three.

**Input** — batch of 10:

```json
{ "identities": [
  { "identity": "Dashboard",
    "process": "chrome",
    "totalSeconds": 564,
    "hits": 13,
    "sampleTitles": ["Me | Timesheet", "Attendance - Dashboard", "Dashboard"] }
]}
```

Sample titles are not optional. `Dashboard` alone is meaningless; `Dashboard`
next to `Me | Timesheet` is obviously an HR portal.

**Output:**

```json
{ "verdicts": [
  { "identity": "Dashboard",
    "category": "Other",
    "confidence": 0.86,
    "reason": "HR portal — timesheet and attendance pages" }
]}
```

`category` ∈ `Coding · Learning · Communication · Meeting · FileManagement ·
Distraction · Personal · Other · Unknown`

**`Unknown` is a first-class answer.** An unsure verdict leaves the identity
pending, which is honest. A confident wrong one silently distorts every KPI
downstream, and nobody ever goes looking for it.

---

### Job B — Session narration and intent

**Question:** *what was going on across this stretch of time?*

This is the job the project actually needed, and the one no per-identity rule can
approach. Everything it requires is already collected — ordered activities with
categories and titles, sessions with interruption counts and deep time, commits
carrying branch names. Only the step that reads them is missing.

**Input** — one session:

```json
{
  "sessionId": 412,
  "start": "2026-09-02T11:03:00+05:30",
  "durationSeconds": 1583,
  "project": "palpool-api",
  "deepSeconds": 1350,
  "interruptions": 5,
  "activities": [
    { "atSeconds": 0,    "durationSeconds": 240, "process": "ms-teams",
      "category": "Communication", "title": "Rahul | palpool-api | Microsoft Teams" },
    { "atSeconds": 240,  "durationSeconds": 420, "process": "chrome",
      "category": "Coding", "identity": "GitLab",
      "title": "Fix login redirect (!59) · Merge request" },
    { "atSeconds": 660,  "durationSeconds": 900, "process": "Code",
      "category": "Coding", "title": "AuthController.cs - palpool-api - Visual Studio Code" }
  ],
  "commits": [
    { "sha": "a1b2c3d", "message": "fix: login redirect loop",
      "branch": "fix/US-1569-Bug_Fixing", "files": 3, "insertions": 24, "deletions": 8 }
  ]
}
```

**Output:**

```json
{
  "sessionId": 412,
  "narrative": "Picked up merge request !59 after a Teams conversation, reviewed it in GitLab, then implemented the login redirect fix and shipped it.",
  "kind": "mr-review",
  "workstream": "US-1569",
  "evidence": [
    "Teams conversation immediately precedes the GitLab MR page",
    "MR !59 title matches the commit message subject",
    "branch fix/US-1569-Bug_Fixing carries the ticket"
  ],
  "confidence": 0.88
}
```

`kind` ∈ `feature-work · bugfix · mr-review · research · meeting-followup ·
admin · context-thrash · unclear`

**`unclear` must be used rather than guessed at.** A session that genuinely was
aimless should say so — `context-thrash` is a real and useful finding, and
pretending otherwise makes the brag document fiction.

#### The `evidence` field is load-bearing

The model must cite what in the input supports its reading. This is not
decoration:

- Hallucination that cannot point at a source becomes **visible** rather than
  plausible.
- The UI can show *why* a session was labelled, so you can disagree with it.
- The eval set can check reasoning, not just the final label.

An answer whose evidence does not appear in the input is a failed answer,
regardless of how good the sentence reads.

#### Storage — migration `004`

```sql
CREATE TABLE session_narrative (
  session_id    INTEGER PRIMARY KEY,
  narrative     TEXT NOT NULL,
  kind          TEXT NOT NULL,
  workstream    TEXT,
  evidence      TEXT,            -- JSON array
  confidence    REAL NOT NULL,
  model         TEXT NOT NULL,   -- e.g. 'gpt-oss:20b'
  generated_utc INTEGER NOT NULL
);
```

Derived and re-runnable, like every other derived table. `model` and
`generated_utc` are what let you re-narrate everything after a model change and
compare the two.

---

### Job C — Weekly digest

**Question:** *what does a week of this add up to?*

The brag document itself — the thing the whole project exists to produce.

**Input:** narrated sessions for the period, plus deterministic metrics computed
in C#, plus any captured wins.

**Output:** markdown, ready to paste into a review.

#### The one rule that matters

**The model may not compute, estimate, or adjust a single number.** Totals,
percentages, line counts and durations are all calculated in C# and passed in.
The model's job is to select what is worth saying and say it well.

Every figure in the output must appear verbatim in the input. This is checkable,
and it should be checked: a digest that inflates your hours is worse than no
digest at all, and you will not notice until someone else does.

---

### Job G — Natural-language query

**Question:** *whatever you feel like asking.*

> "How much time on palpool-api this month?"
> "What was I doing last Tuesday afternoon?"

Implemented with tool calling against a **small, fixed, read-only** surface:

```
getSessions(fromIso, toIso, project?, category?)
getCommits(fromIso, toIso, project?)
getMetrics(fromIso, toIso)
getNarratives(fromIso, toIso)
getPendingIdentities()
```

**No arbitrary SQL. Ever.** Not as a convenience, not behind a flag. A model with
a SQL socket into your activity log is a prompt-injection target sitting on the
most sensitive file on the machine — and the window titles it reads are attacker-
influenced by definition, since anyone can name a browser tab.

The loop: model calls tools → receives JSON → answers in prose, citing the
figures it was given.

---

## 6. Failure modes

Each of these will happen. Each has one correct response.

| Failure | Response |
|---|---|
| Endpoint unreachable | Report it, exit **0**. Not an error — the provider is absent most of the time by design. |
| Malformed JSON | Retry once, then skip the batch and leave it pending. Never regex prose. |
| Category not in the enum | Discard the verdict. Leave pending. |
| Evidence not present in the input | Discard. This is the hallucination check. |
| Confidence below threshold | Leave pending. Configurable, default 0.6. |
| Model contradicts a manual verdict | Manual wins, silently. It is not a conflict. |
| Timeout mid-batch | Whole batch is discarded. Partial writes are worse than none. |

Two properties make all of this safe: **the pending queue is durable** — it lives
in `classification_rule` and simply accumulates — and **nothing else in devlog
calls the model.** Capture, derivation, git scanning and the UI all behave
identically whether the LLM has ever run or not.

---

## 7. Determinism and provenance

- `temperature: 0`, and a fixed seed where the server supports one.
- `response_format` as a JSON schema, never "please return JSON".
- Batch size 10 for Job A; one session per call for Job B.
- Every stored output carries **model name and generation timestamp**.

Provenance is what makes a model swap safe. Without it you cannot tell which
verdicts came from the old model, so you cannot tell whether the new one is
better — you can only hope.

---

## 8. The eval set

**A first-class deliverable, not an afterthought.**

The scenarios cannot be written down in advance. *"Teams → GitLab → code →
commit is an MR review"* was discovered by looking at real captured data, and
there are more like it that nobody has thought of yet. That is precisely why a
model is needed instead of more rules — and it is also why the only way to know
whether the model is any good is to test it against reality.

```
docs/llm-evals/
  identities.json    # ~30 identities with expected categories
  sessions.json      # ~20 real sessions with expected kind + workstream
```

Drawn from actual capture, hand-labelled once. Then:

```
devlog llm-eval
```

reports per-job accuracy. Run it before and after any model change, prompt
change, or temperature change. **Without this, tuning is superstition.**

Job B's eval is the one worth investing in. Category classification is easy to
eyeball; whether a narration is *right* is not, and a plausible-sounding wrong
answer is the failure this whole design is arranged to catch.

---

## 9. CLI surface

```
devlog llm                    provider, model, reachability, which jobs are on
devlog classify-ai            drain pending identities            (Job A)
devlog narrate [--since 7d]   narrate sessions lacking one        (Job B)
devlog digest [--week]        generate the brag document          (Job C)
devlog ask "..."              natural-language query              (Job G)
devlog llm-eval               accuracy against the labelled set
```

Every one supports `--dry-run`, printing what would be written without writing
it. The first run against a new model should always be inspectable before it
touches the database.

`devlog llm` is the command you run when something seems wrong. It answers *is a
provider configured, can I reach it, which model is it, and which jobs are
enabled* — in one screen, without guessing.

---

## 10. Build order

1. **`ChatClassifier`** — the OpenAI-compatible client, with the unreachable path
   tested first against a stubbed handler. Connection-refused is the case most
   likely to be hit in practice, so it is the case to get right first.
2. **Job A** — smallest, and proves the whole pipeline end to end against a
   queue that already exists.
3. **Eval harness** — before Job B, so B is built against a measurement.
4. **Job B** — migration `004`, then narration. The real work.
5. **Job C** — depends on B.
6. **Job G** — depends on the read layer from Phase 4b.

Jobs A and B depend on nothing in the UI work, so they can proceed in parallel
with Phase 4b.
