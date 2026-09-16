using Dapper;
using Devlog.Core.Abstractions;

namespace Devlog.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="ILlmRequestLog"/>. Written from <c>ChatClassifier</c>, the one chokepoint every AI job's requests funnel through.</summary>
public sealed class LlmRequestLog(ISqliteConnectionFactory factory) : ILlmRequestLog
{
    public async Task RecordAsync(string job, string? model, int? httpStatus, bool ok, string? error, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO llm_request (requested_utc, job, model, status, ok, error)
            VALUES (@requestedUtc, @job, @model, @httpStatus, @ok, @error);
            """,
            new
            {
                requestedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                job,
                model,
                httpStatus,
                ok = ok ? 1 : 0,
                error
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> CountTodayAsync(CancellationToken ct = default)
    {
        var localMidnightUtc = new DateTimeOffset(DateTime.Now.Date, DateTimeOffset.Now.Offset).ToUnixTimeMilliseconds();

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM llm_request WHERE requested_utc >= @localMidnightUtc;",
            new { localMidnightUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
