-- Job C, run once per calendar week within a month: a synthesized "win"
-- summary for the Month view. DERIVED and re-runnable, same discipline as
-- session_narrative (005) — but keyed on (week_from, week_to), not a session
-- id, since a week's boundaries are never reassigned by `devlog derive` the
-- way session ids are. NOT cleared by DerivationRunner's rebuild pass —
-- invalidated only by its own staleness check (narrative_count /
-- narratives_max_generated_utc / model changing), which WeeklyWinRunner
-- checks before ever calling the model again for a given week.
CREATE TABLE weekly_win (
  week_from       TEXT    NOT NULL,
  week_to         TEXT    NOT NULL,
  summary         TEXT    NOT NULL,
  highlights      TEXT    NOT NULL,  -- JSON array of strings
  narrative_count INTEGER NOT NULL,
  narratives_max_generated_utc INTEGER NOT NULL,
  model           TEXT    NOT NULL,
  generated_utc   INTEGER NOT NULL,
  PRIMARY KEY (week_from, week_to)
);
