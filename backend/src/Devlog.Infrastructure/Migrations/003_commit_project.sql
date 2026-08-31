-- Phase 3: the columns commit scanning needs.
--
-- commit_record is DERIVED but re-scannable: --scan-git rebuilds rows from the
-- repos on disk, and --derive re-links them to freshly rebuilt sessions with no
-- disk access. The git history itself is the real source of truth, not this
-- table - which is what makes it safe to delete rows and let them be rescanned.

-- The logical project a commit belongs to, per the configured repo->project
-- mapping. Two clones of the same service map to the same project, so their
-- commits combine under one name even though `repo` (the path scanned from)
-- still differs.
ALTER TABLE commit_record ADD COLUMN project TEXT;

-- Who actually made the commit. Needed because different repos on this machine
-- commit under different identities - the personal GitHub noreply address for
-- devlog, the work email for everything else - so "mine" cannot be a single
-- fixed filter.
ALTER TABLE commit_record ADD COLUMN author_email TEXT;

-- Merge commits are excluded from scanning entirely (their diffs are enormous
-- and attribute other people's work to whoever merged), but the column is kept
-- rather than filtering at the query layer, so a future "merges I made" view
-- stays possible without a schema change.
ALTER TABLE commit_record ADD COLUMN is_merge INTEGER NOT NULL DEFAULT 0;

CREATE INDEX ix_commit_project ON commit_record (project);
