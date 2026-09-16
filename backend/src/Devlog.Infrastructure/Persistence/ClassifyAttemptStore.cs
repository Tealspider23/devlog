using Dapper;
using Devlog.Core.Abstractions;

namespace Devlog.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="IClassifyAttemptStore"/>. See its doc comment for why this is a separate table from <c>classification_rule</c>.</summary>
public sealed class ClassifyAttemptStore(ISqliteConnectionFactory factory) : IClassifyAttemptStore
{
    public async Task<HashSet<string>> GetRecentlyAttemptedAsync(int withinDays, CancellationToken ct = default)
    {
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, withinDays)).ToUnixTimeMilliseconds();

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        var sites = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT site FROM llm_classify_attempt WHERE attempted_utc >= @cutoffUtc;",
            new { cutoffUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return new HashSet<string>(sites, StringComparer.OrdinalIgnoreCase);
    }

    public async Task RecordAttemptAsync(string site, long nowUtc, string? reason, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO llm_classify_attempt (site, attempted_utc, attempts, last_reason)
            VALUES (@site, @nowUtc, 1, @reason)
            ON CONFLICT (site) DO UPDATE SET
              attempted_utc = excluded.attempted_utc,
              attempts      = llm_classify_attempt.attempts + 1,
              last_reason   = excluded.last_reason;
            """,
            new { site, nowUtc, reason },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
