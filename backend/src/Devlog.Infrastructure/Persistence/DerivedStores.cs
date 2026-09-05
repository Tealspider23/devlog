using Dapper;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

/// <summary>
/// Writes for the derived half of the schema.
/// <para>
/// Both stores replace their table wholesale inside a single transaction. That
/// is what makes "derived is disposable" true in practice: a failed derivation
/// rolls back completely rather than leaving a half-rebuilt timeline that looks
/// like real data.
/// </para>
/// </summary>
public sealed class ActivityStore(ISqliteConnectionFactory factory)
{
    public async Task ReplaceAllAsync(IReadOnlyList<Activity> activities, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM activity;", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (activities.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO activity
                  (start_utc, end_utc, process_name, activity_key, context, project, site_identity,
                   category, engagement, title_changes, sample_title, session_id)
                VALUES
                  (@StartUtc, @EndUtc, @ProcessName, @ActivityKey, @Context, @Project, @SiteIdentity,
                   @Category, @Engagement, @TitleChanges, @SampleTitle, @SessionId);
                """,
                activities.Select(a => new
                {
                    a.StartUtc,
                    a.EndUtc,
                    a.ProcessName,
                    a.ActivityKey,
                    a.Context,
                    a.Project,
                    a.SiteIdentity,
                    Category = a.Category.ToString(),
                    Engagement = (int)a.Engagement,
                    a.TitleChanges,
                    a.SampleTitle,
                    a.SessionId
                }),
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM activity;", cancellationToken: ct)).ConfigureAwait(false);
    }
}

public sealed class SessionStore(ISqliteConnectionFactory factory)
{
    public async Task ReplaceAllAsync(IReadOnlyList<Session> sessions, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM session;", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (sessions.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO session
                  (id, start_utc, end_utc, activity_key, project, category,
                   interruptions, deep_seconds, label)
                VALUES
                  (@Id, @StartUtc, @EndUtc, @ActivityKey, @Project, @Category,
                   @Interruptions, @DeepSeconds, @Label);
                """,
                sessions.Select(s => new
                {
                    s.Id,
                    s.StartUtc,
                    s.EndUtc,
                    s.ActivityKey,
                    s.Project,
                    Category = s.Category.ToString(),
                    s.Interruptions,
                    s.DeepSeconds,
                    s.Label
                }),
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM session;", cancellationToken: ct)).ConfigureAwait(false);
    }
}

/// <summary>
/// Manual corrections. SOURCE OF TRUTH — never touched by a rebuild, which is
/// the whole reason overrides are keyed by identity rather than by session id.
/// </summary>
public sealed class OverrideStore(ISqliteConnectionFactory factory)
{
    public async Task<List<SessionOverride>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<OverrideRow>(new CommandDefinition(
            "SELECT session_start_utc, activity_key, category, label FROM session_override;",
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task UpsertAsync(SessionOverride o, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO session_override (session_start_utc, activity_key, category, label)
            VALUES (@Start, @Key, @Category, @Label)
            ON CONFLICT (session_start_utc, activity_key) DO UPDATE SET
              category = excluded.category,
              label    = excluded.label;
            """,
            new
            {
                Start = o.SessionStartUtc,
                Key = o.ActivityKey,
                Category = o.Category?.ToString(),
                o.Label
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class OverrideRow
    {
        public long session_start_utc { get; set; }
        public string activity_key { get; set; } = string.Empty;
        public string? category { get; set; }
        public string? label { get; set; }

        public SessionOverride ToDomain() => new()
        {
            SessionStartUtc = session_start_utc,
            ActivityKey = activity_key,
            Category = ActivityCategoryExtensions.TryParse(category, out var c) ? c : null,
            Label = label
        };
    }
}
