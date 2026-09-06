-- Job B: what a session was actually about.
--
-- DERIVED and re-runnable, like activity and session: re-narrating after a model
-- change is expected, and `model` plus `generated_utc` are what make the old and
-- new answers comparable.
--
-- Keyed on session_start_utc (durable across SQLite re-derivation) rather than
-- an ephemeral auto-increment session_id. session_end_utc and activity_count
-- are stored alongside to detect if underlying activity changed.
CREATE TABLE session_narrative (
  session_start_utc INTEGER PRIMARY KEY,
  session_end_utc   INTEGER NOT NULL,
  activity_count    INTEGER NOT NULL,
  session_id        INTEGER,
  narrative         TEXT    NOT NULL,
  kind              TEXT    NOT NULL,
  workstream        TEXT,
  evidence          TEXT    NOT NULL,  -- JSON array of strings
  confidence        REAL    NOT NULL,
  model             TEXT    NOT NULL,
  generated_utc     INTEGER NOT NULL
);

CREATE INDEX ix_session_narrative_generated ON session_narrative (generated_utc);
