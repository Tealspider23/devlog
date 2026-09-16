-- Phase 15: stops classify-ai (which runs on every dashboard Refresh) from
-- re-sending the same identities forever once the model has already
-- declined to answer them confidently.
--
-- NOT touched by derivation, deliberately — unlike classification_rule,
-- whose pending rows are deleted and rebuilt on every derive
-- (ClassificationRuleStore.RecordSightingsAsync). An attempt record must
-- outlive that, or a 20/day request budget can disappear on identities that
-- were never going to get an accepted verdict, without a single narrative
-- ever being written — exactly what happened on 2026-09-15.
CREATE TABLE llm_classify_attempt (
  site          TEXT    NOT NULL PRIMARY KEY,
  attempted_utc INTEGER NOT NULL,
  attempts      INTEGER NOT NULL DEFAULT 1,
  last_reason   TEXT
);
