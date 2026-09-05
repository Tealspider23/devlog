-- Phase 6.5: the repository an activity belongs to, kept separate from its
-- context.
--
-- `context` is the stable part of a window title - what makes two moments "the
-- same thing", and what sessions are keyed by. It is a repository for VS Code
-- and something else entirely everywhere else: a site name for a browser, and
-- the whole raw title for an app with no extraction rule.
--
-- Conflating the two put nonsense in the one document this project exists to
-- produce. A digest listed "GitLab", "Windows PowerShell" and four raw SQL
-- Server Management Studio window titles - server, database and the product
-- name repeated twice - among its projects, because each was Coding-categorised
-- and its context was promoted to a project unconditionally. The hours were
-- right; the labels were not.
--
-- So `project` is populated ONLY when an extraction rule genuinely resolved a
-- repository, and is NULL otherwise. Coding time with no resolvable repository
-- is reported as exactly that, rather than inventing a name for it.
--
-- Nullable and no default: activity is DERIVED and rebuilt wholesale on every
-- derive, so this needs no backfill - the next `devlog derive` populates it from
-- raw events plus config, which is the standing requirement for any derived
-- column.
ALTER TABLE activity ADD COLUMN project TEXT;

CREATE INDEX ix_activity_project ON activity (project);
