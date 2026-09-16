-- Phase 15: a forensic trail for AI calls. Before this, a failed narrate run
-- left no record anywhere of what was sent, what came back, or what it
-- cost — the collector's own log file said nothing about AI at all. Also
-- backs the "requests used today" figure shown in Settings and `devlog llm`.
CREATE TABLE llm_request (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  requested_utc INTEGER NOT NULL,
  job           TEXT    NOT NULL,   -- narrate | classify | digest | ask | weekly-win | eval
  model         TEXT,
  status        INTEGER,            -- HTTP status; NULL = transport failure (never reached the provider)
  ok            INTEGER NOT NULL,
  error         TEXT
);

CREATE INDEX ix_llm_request_day ON llm_request (requested_utc);
