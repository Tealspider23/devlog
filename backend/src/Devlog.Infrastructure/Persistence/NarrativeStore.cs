using System.Text.Json;
using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of INarrativeStore.
/// Keyed on session_start_utc.
/// </summary>
public sealed class NarrativeStore(ISqliteConnectionFactory factory) : INarrativeStore
{
    public async Task<SessionNarrative?> GetByStartUtcAsync(long sessionStartUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<NarrativeRow>(new CommandDefinition(
            """
            SELECT session_start_utc, session_end_utc, activity_count, session_id,
                   narrative, kind, workstream, evidence, confidence, model, generated_utc
            FROM session_narrative
            WHERE session_start_utc = @sessionStartUtc;
            """,
            new { sessionStartUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    public async Task<List<SessionNarrative>> GetRangeAsync(long fromUtc, long toUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<NarrativeRow>(new CommandDefinition(
            """
            SELECT session_start_utc, session_end_utc, activity_count, session_id,
                   narrative, kind, workstream, evidence, confidence, model, generated_utc
            FROM session_narrative
            WHERE session_start_utc >= @fromUtc AND session_start_utc <= @toUtc
            ORDER BY session_start_utc ASC;
            """,
            new { fromUtc, toUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<List<SessionNarrative>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<NarrativeRow>(new CommandDefinition(
            """
            SELECT session_start_utc, session_end_utc, activity_count, session_id,
                   narrative, kind, workstream, evidence, confidence, model, generated_utc
            FROM session_narrative
            ORDER BY session_start_utc ASC;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task UpsertAsync(SessionNarrative narrative, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var evidenceJson = JsonSerializer.Serialize(narrative.Evidence);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO session_narrative
              (session_start_utc, session_end_utc, activity_count, session_id,
               narrative, kind, workstream, evidence, confidence, model, generated_utc)
            VALUES
              (@SessionStartUtc, @SessionEndUtc, @ActivityCount, @SessionId,
               @Narrative, @Kind, @Workstream, @EvidenceJson, @Confidence, @Model, @GeneratedUtc)
            ON CONFLICT (session_start_utc) DO UPDATE SET
              session_end_utc = excluded.session_end_utc,
              activity_count  = excluded.activity_count,
              session_id      = excluded.session_id,
              narrative       = excluded.narrative,
              kind            = excluded.kind,
              workstream      = excluded.workstream,
              evidence        = excluded.evidence,
              confidence      = excluded.confidence,
              model           = excluded.model,
              generated_utc   = excluded.generated_utc;
            """,
            new
            {
                narrative.SessionStartUtc,
                narrative.SessionEndUtc,
                narrative.ActivityCount,
                narrative.SessionId,
                narrative.Narrative,
                narrative.Kind,
                narrative.Workstream,
                EvidenceJson = evidenceJson,
                narrative.Confidence,
                narrative.Model,
                narrative.GeneratedUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RelinkSessionIdsAsync(IReadOnlyList<Session> sessions, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE session_narrative SET session_id = NULL;",
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        const string sql = "UPDATE session_narrative SET session_id = @Id WHERE session_start_utc = @StartUtc;";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            sessions,
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long sessionStartUtc, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM session_narrative WHERE session_start_utc = @sessionStartUtc;",
            new { sessionStartUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM session_narrative;",
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class NarrativeRow
    {
        public long session_start_utc { get; set; }
        public long session_end_utc { get; set; }
        public int activity_count { get; set; }
        public long? session_id { get; set; }
        public string narrative { get; set; } = string.Empty;
        public string kind { get; set; } = string.Empty;
        public string? workstream { get; set; }
        public string evidence { get; set; } = "[]";
        public double confidence { get; set; }
        public string model { get; set; } = string.Empty;
        public long generated_utc { get; set; }

        public SessionNarrative ToDomain()
        {
            List<string> parsedEvidence;
            try
            {
                parsedEvidence = JsonSerializer.Deserialize<List<string>>(evidence) ?? [];
            }
            catch
            {
                parsedEvidence = [];
            }

            return new SessionNarrative
            {
                SessionStartUtc = session_start_utc,
                SessionEndUtc = session_end_utc,
                ActivityCount = activity_count,
                SessionId = session_id,
                Narrative = narrative,
                Kind = kind,
                Workstream = workstream,
                Evidence = parsedEvidence,
                Confidence = confidence,
                Model = model,
                GeneratedUtc = generated_utc
            };
        }
    }
}
