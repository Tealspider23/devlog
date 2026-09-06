# LLM eval fixtures

The full spec is `docs/LLM.md` section 9. This file exists so the fixture
**format** is committed and versioned, while the fixtures themselves never are.

## Why `*.json` here is gitignored

These files hold real captured sessions and identities, hand-labelled for
accuracy testing — not synthetic test data. Window titles are genuinely
sensitive: server names, colleague names, ticket contents. `backend/tests/`
fixtures are sanitised and safe to publish; these are the opposite, by design —
an eval is only meaningful against what you actually captured.

`.gitignore` excludes `docs/llm-evals/*.json`. Generate them with
`devlog llm-fixtures --out docs/llm-evals`, hand-label the blank `expected`
fields, and keep the result local — or carry it between machines outside git.

**Label before you see the model's answers.** Once you have seen them you
cannot unsee them, and the fixtures stop being an independent measurement.

## `identities.json`

One entry per identity fed to Job A. `expected` is the category a human decided
on, looking only at the identity and its sample titles — exactly what the model
sees.

```json
[
  {
    "identity": "Example Docs",
    "process": "chrome",
    "sampleTitles": [
      "Getting started - Example Docs",
      "API reference - Example Docs"
    ],
    "expected": "Learning",
    "note": "optional: why this is the right answer, or why it's a hard case"
  },
  {
    "identity": "Example Search",
    "process": "chrome",
    "sampleTitles": ["cats - Example Search", "flight prices - Example Search"],
    "expected": "Unknown",
    "note": "genuinely mixed-use - a single category would be wrong half the time"
  }
]
```

`expected` must be one of the nine values `ActivityCategory` plus `Unknown`
uses — see `docs/LLM.md` section 4.3. Roughly 30 identities, weighted toward
the ones that were actually hard to call.

## `sessions.json`

One entry per session fed to Job B. `expectedKind` and `expectedWorkstream`
are what a human reading the same session (activities, commits, branch names)
would conclude.

```json
[
  {
    "sessionId": 1234,
    "expectedKind": "mr-review",
    "expectedWorkstream": "PROJ-42",
    "note": "reviewed a merge request in the browser, then made a small fix and committed it"
  },
  {
    "sessionId": 1240,
    "expectedKind": "context-thrash",
    "expectedWorkstream": null,
    "note": "chat, three unrelated tabs, no commit - genuinely scattered, not a coherent story"
  }
]
```

`expectedKind` must be one of the eight values in `docs/LLM.md` section 5.4.
Roughly 20 sessions. Include a few `context-thrash` / `unclear` cases
deliberately — a model that always finds a coherent story is the failure this
eval exists to catch.

## Running the eval

```
devlog llm-eval
```

Reports per-job accuracy against whatever is present in this directory. Run it
before and after any model, prompt or temperature change — see `docs/LLM.md`
section 9 for why this is not optional.
