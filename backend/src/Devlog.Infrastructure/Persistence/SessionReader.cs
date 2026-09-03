using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

/// <summary>
/// The read half of the derived tables. See <see cref="ISessionReader"/> for why
/// it exists as its own thing rather than as more methods on the writers.
/// <para>
/// The summary query is lifted verbatim from what used to sit inside
/// <c>StatsReporter.Sessions</c>. It is repeated in two places here only because
/// the ordering and the filter genuinely differ; the correlated subqueries — the
/// part that would drift — are shared.
/// </para>
/// </summary>
public sealed class SessionReader(ISqliteConnectionFactory factory) : ISessionReader
{
    /// <summary>
    /// The four counts that turn a session row into a <see cref="SessionSummary"/>.
    /// <para>
    /// Correlated subqueries rather than joins with GROUP BY: a session with no
    /// commits must yield zero rather than vanishing, and at ~100 sessions the
    /// cost is irrelevant next to getting the outer join semantics wrong.
    /// </para>
    /// </summary>
    private const string SummaryColumns =
        """
        s.id, s.start_utc, s.end_utc, s.activity_key, s.project, s.category,
        s.interruptions, s.deep_seconds, s.label,
        (SELECT COUNT(*) FROM activity a WHERE a.session_id = s.id) AS activity_count,
        (SELECT COUNT(*) FROM commit_record c WHERE c.session_id = s.id) AS commit_count,
        (SELECT COALESCE(SUM(c.insertions), 0) FROM commit_record c WHERE c.session_id = s.id) AS ins,
        (SELECT COALESCE(SUM(c.deletions), 0) FROM commit_record c WHERE c.session_id = s.id) AS del
        """;

    public async Task<List<SessionSummary>> GetRangeAsync(
        long fromUtc, long toUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        // Overlap, not containment. A session that began before the window and
        // ran into it belongs on the day's picture — otherwise the block you
        // were already inside at midnight silently disappears.
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            $"""
             SELECT {SummaryColumns}
             FROM session s
             WHERE s.start_utc < @toUtc AND s.end_utc > @fromUtc
             ORDER BY s.start_utc;
             """,
            new { fromUtc, toUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<List<SessionSummary>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        // Newest first to apply the limit, then reversed so the caller reads
        // them in the order they happened.
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            $"""
             SELECT {SummaryColumns}
             FROM session s
             ORDER BY s.start_utc DESC
             LIMIT @n;
             """,
            new { n = count },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Reverse().Select(r => r.ToDomain())];
    }

    public async Task<SessionSummary?> GetByIdAsync(long sessionId, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<SummaryRow>(new CommandDefinition(
            $"""
             SELECT {SummaryColumns}
             FROM session s
             WHERE s.id = @sessionId;
             """,
            new { sessionId },
            cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    public async Task<List<Activity>> GetActivitiesAsync(long sessionId, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ActivityRow>(new CommandDefinition(
            """
            SELECT id, start_utc, end_utc, process_name, activity_key, context,
                   site_identity, category, engagement, title_changes, sample_title, session_id
            FROM activity
            WHERE session_id = @sessionId
            ORDER BY start_utc;
            """,
            new { sessionId },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<List<CommitRecord>> GetCommitsAsync(
        long fromUtc, long toUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<CommitRow>(new CommandDefinition(
            """
            SELECT sha, repo, project, ts_utc, message, branch, author_email,
                   files_changed, insertions, deletions, languages, is_merge, session_id
            FROM commit_record
            WHERE ts_utc >= @fromUtc AND ts_utc < @toUtc
            ORDER BY ts_utc;
            """,
            new { fromUtc, toUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<List<CommitRecord>> GetCommitsForSessionAsync(long sessionId, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<CommitRow>(new CommandDefinition(
            """
            SELECT sha, repo, project, ts_utc, message, branch, author_email,
                   files_changed, insertions, deletions, languages, is_merge, session_id
            FROM commit_record
            WHERE session_id = @sessionId
            ORDER BY ts_utc;
            """,
            new { sessionId },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<long> GetUnclassifiedSecondsAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(end_utc - start_utc) / 1000, 0)
            FROM activity WHERE category = 'Other';
            """,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<long> GetUnclassifiedSecondsAsync(long fromUtc, long toUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(MIN(end_utc, @toUtc) - MAX(start_utc, @fromUtc)) / 1000, 0)
            FROM activity
            WHERE category = 'Other' AND start_utc < @toUtc AND end_utc > @fromUtc;
            """,
            new { fromUtc, toUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class SummaryRow
    {
        public long id { get; set; }
        public long start_utc { get; set; }
        public long end_utc { get; set; }
        public string activity_key { get; set; } = string.Empty;
        public string? project { get; set; }
        public string? category { get; set; }
        public int interruptions { get; set; }
        public int deep_seconds { get; set; }
        public string? label { get; set; }
        public int activity_count { get; set; }
        public int commit_count { get; set; }
        public int ins { get; set; }
        public int del { get; set; }

        public SessionSummary ToDomain() => new()
        {
            Session = new Session
            {
                Id = id,
                StartUtc = start_utc,
                EndUtc = end_utc,
                ActivityKey = activity_key,
                Project = project,
                Category = ActivityCategoryExtensions.TryParse(category, out var c) ? c : ActivityCategory.Other,
                Interruptions = interruptions,
                DeepSeconds = deep_seconds,
                Label = label
            },
            ActivityCount = activity_count,
            CommitCount = commit_count,
            Insertions = ins,
            Deletions = del
        };
    }

    private sealed class ActivityRow
    {
        public long id { get; set; }
        public long start_utc { get; set; }
        public long end_utc { get; set; }
        public string? process_name { get; set; }
        public string activity_key { get; set; } = string.Empty;
        public string? context { get; set; }
        public string? site_identity { get; set; }
        public string? category { get; set; }
        public int engagement { get; set; }
        public int title_changes { get; set; }
        public string? sample_title { get; set; }
        public long? session_id { get; set; }

        public Activity ToDomain() => new()
        {
            Id = id,
            StartUtc = start_utc,
            EndUtc = end_utc,
            ProcessName = process_name,
            ActivityKey = activity_key,
            Context = context,
            SiteIdentity = site_identity,
            Category = ActivityCategoryExtensions.TryParse(category, out var c) ? c : ActivityCategory.Other,
            Engagement = (Engagement)engagement,
            TitleChanges = title_changes,
            SampleTitle = sample_title,
            SessionId = session_id
        };
    }

    private sealed class CommitRow
    {
        public string sha { get; set; } = string.Empty;
        public string repo { get; set; } = string.Empty;
        public string? project { get; set; }
        public long ts_utc { get; set; }
        public string? message { get; set; }
        public string? branch { get; set; }
        public string? author_email { get; set; }
        public int files_changed { get; set; }
        public int insertions { get; set; }
        public int deletions { get; set; }
        public string? languages { get; set; }
        public long is_merge { get; set; }
        public long? session_id { get; set; }

        public CommitRecord ToDomain() => new()
        {
            Sha = sha,
            Repo = repo,
            Project = project ?? string.Empty,
            TsUtc = ts_utc,
            Message = message,
            Branch = branch,
            AuthorEmail = author_email ?? string.Empty,
            FilesChanged = files_changed,
            Insertions = insertions,
            Deletions = deletions,
            Languages = languages,
            IsMerge = is_merge != 0,
            SessionId = session_id
        };
    }
}
