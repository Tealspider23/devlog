-- Phase 2: classification + the columns derivation needs.

-- Category is stored as TEXT, not an integer, so the database stays readable
-- when debugging through --stats and --sessions. At a few hundred rows a day the
-- storage difference is irrelevant and legibility is worth far more.
ALTER TABLE activity ADD COLUMN category TEXT NOT NULL DEFAULT 'Other';

-- What was being looked at, for classification purposes: a site name for
-- browsers, the process name otherwise. This is the thing a verdict attaches to.
ALTER TABLE activity ADD COLUMN site_identity TEXT;

-- Set from a session_override once you have corrected a session.
ALTER TABLE session ADD COLUMN label TEXT;

-- SOURCE OF TRUTH, never rebuilt.
--
-- A cache of verdicts on "what kind of time is this?", keyed by the *thing*
-- rather than the occurrence. Three pages of MCP documentation are one row here,
-- not three, and never a question again.
--
-- The source column matters: this table does not care whether a verdict came
-- from a builtin rule, a local model, or you. That is what lets an LLM fill the
-- gaps later with no redesign, while a manual answer stays available to override
-- whatever it decided.
CREATE TABLE classification_rule (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  scope         TEXT    NOT NULL,              -- 'Site' | 'Page'
  site          TEXT    NOT NULL,              -- site identity, or process name
  keyword       TEXT,                          -- NULL for site-scope rules
  category      TEXT,                          -- NULL = pending an answer
  source        TEXT    NOT NULL DEFAULT 'manual',   -- builtin | llm | manual
  is_mixed      INTEGER NOT NULL DEFAULT 0,    -- site answered two ways => ask per page
  hits          INTEGER NOT NULL DEFAULT 0,
  total_seconds INTEGER NOT NULL DEFAULT 0,    -- drives --unknowns ordering
  last_seen_utc INTEGER,
  created_utc   INTEGER NOT NULL
);

-- Keyword is nullable and SQLite treats NULLs as distinct in a UNIQUE index, so
-- uniqueness is enforced with two partial indexes instead.
CREATE UNIQUE INDEX ux_rule_site ON classification_rule (scope, site)
  WHERE keyword IS NULL;

CREATE UNIQUE INDEX ux_rule_page ON classification_rule (scope, site, keyword)
  WHERE keyword IS NOT NULL;

CREATE INDEX ix_rule_pending ON classification_rule (category, total_seconds DESC);
