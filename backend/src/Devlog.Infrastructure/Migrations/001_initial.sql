-- devlog initial schema
--
-- Two classes of table:
--   SOURCE OF TRUTH  never rewritten; everything else is rebuilt from these
--   DERIVED          dropped and rebuilt wholesale by POST /v1/derive
--
-- The split is what makes it safe to guess at thresholds today: a wrong guess
-- costs a re-derivation, never a re-collection.

------------------------------------------------------------------ SOURCE OF TRUTH

-- A layer-1 event: a moment at which something changed. Append-only.
--
-- Transitions, not polls. Writing a row per 3s sample would be ~28,000
-- near-duplicate rows/day; writing only on change gives a few hundred, and each
-- row's duration is implied by the row that follows it.
CREATE TABLE raw_event (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  ts_utc        INTEGER NOT NULL,   -- unix milliseconds, UTC. Never local time.
  kind          INTEGER NOT NULL,   -- 0=focus 1=heartbeat 2=lock 3=unlock
                                    -- 4=suspend 5=resume 6=start 7=stop
  process_name  TEXT,
  window_title  TEXT,               -- RAW. Normalization happens at derivation.
  exe_path      TEXT,               -- best-effort; null for elevated processes
  idle_seconds  INTEGER NOT NULL    -- MEASUREMENT, not a flag. Reading docs and
                                    -- being away look identical here on purpose;
                                    -- separating them is a derivation decision.
);

CREATE INDEX ix_raw_event_ts ON raw_event (ts_utc);
CREATE INDEX ix_raw_event_kind_ts ON raw_event (kind, ts_utc);

-- Manually captured achievements. Enriched at read time with the covering
-- session and nearby commits.
CREATE TABLE win (
  id      INTEGER PRIMARY KEY AUTOINCREMENT,
  ts_utc  INTEGER NOT NULL,
  note    TEXT NOT NULL
);

CREATE INDEX ix_win_ts ON win (ts_utc);

-- User corrections to derived sessions. Keyed by identity rather than by
-- session id, because ids do not survive a rebuild but the corrections must.
CREATE TABLE session_override (
  session_start_utc INTEGER NOT NULL,
  activity_key      TEXT    NOT NULL,
  category          TEXT,
  label             TEXT,
  PRIMARY KEY (session_start_utc, activity_key)
);

------------------------------------------------------------------------ DERIVED

-- A maximal continuous interval during which the activity key stayed constant.
CREATE TABLE activity (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  start_utc     INTEGER NOT NULL,
  end_utc       INTEGER NOT NULL,
  process_name  TEXT,
  activity_key  TEXT    NOT NULL,   -- process + extracted context
  context       TEXT,               -- e.g. project "devlog", cwd, channel
  engagement    INTEGER NOT NULL,   -- 0=producing 1=consuming 2=idle
  title_changes INTEGER NOT NULL,   -- volatile detail collapsed into this span
  sample_title  TEXT,
  session_id    INTEGER REFERENCES session (id) ON DELETE SET NULL
);

CREATE INDEX ix_activity_start ON activity (start_utc);
CREATE INDEX ix_activity_session ON activity (session_id);

-- A meaningful unit of work: several activities sharing intent.
CREATE TABLE session (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  start_utc     INTEGER NOT NULL,
  end_utc       INTEGER NOT NULL,
  activity_key  TEXT    NOT NULL,
  project       TEXT,
  category      TEXT    NOT NULL,
  interruptions INTEGER NOT NULL,
  deep_seconds  INTEGER NOT NULL    -- uninterrupted producing time
);

CREATE INDEX ix_session_start ON session (start_utc);

-- Artifacts. NOT activities: the collector never sees a commit happen. These are
-- discovered independently by the git scanner and joined by timestamp overlap.
-- Keeping the two axes separate is what allows "4 hours spent, 200 lines shipped".
CREATE TABLE commit_record (
  sha           TEXT PRIMARY KEY,
  repo          TEXT    NOT NULL,
  ts_utc        INTEGER NOT NULL,
  message       TEXT,
  branch        TEXT,
  files_changed INTEGER,
  insertions    INTEGER,
  deletions     INTEGER,
  languages     TEXT,               -- comma-separated
  session_id    INTEGER REFERENCES session (id) ON DELETE SET NULL
);

CREATE INDEX ix_commit_ts ON commit_record (ts_utc);
CREATE INDEX ix_commit_session ON commit_record (session_id);
