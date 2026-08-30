using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

public sealed class EventStore(ISqliteConnectionFactory factory) : IEventStore
{
    public async Task AppendAsync(IReadOnlyList<RawEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // One transaction for the whole batch: the point of buffering is to pay
        // a single fsync for many rows rather than one per row.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO raw_event (ts_utc, kind, process_name, window_title, exe_path, idle_seconds)
            VALUES (@TsUtc, @Kind, @ProcessName, @WindowTitle, @ExePath, @IdleSeconds);
            """,
            events.Select(e => new
            {
                e.TsUtc,
                Kind = (int)e.Kind,
                e.ProcessName,
                e.WindowTitle,
                e.ExePath,
                e.IdleSeconds
            }),
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<RawEvent?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<EventRow>(new CommandDefinition(
            """
            SELECT id, ts_utc, kind, process_name, window_title, exe_path, idle_seconds
            FROM raw_event
            ORDER BY id DESC
            LIMIT 1;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM raw_event;",
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<List<RawEvent>> GetRangeAsync(
        long? fromUtc = null,
        long? toUtc = null,
        CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<EventRow>(new CommandDefinition(
            """
            SELECT id, ts_utc, kind, process_name, window_title, exe_path, idle_seconds
            FROM raw_event
            WHERE (@from IS NULL OR ts_utc >= @from)
              AND (@to   IS NULL OR ts_utc <  @to)
            ORDER BY ts_utc, id;
            """,
            new { from = fromUtc, to = toUtc },
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    /// <summary>Snake_case landing type; Dapper does not map to init-only records cleanly.</summary>
    private sealed class EventRow
    {
        public long id { get; set; }
        public long ts_utc { get; set; }
        public int kind { get; set; }
        public string? process_name { get; set; }
        public string? window_title { get; set; }
        public string? exe_path { get; set; }
        public int idle_seconds { get; set; }

        public RawEvent ToDomain() => new()
        {
            Id = id,
            TsUtc = ts_utc,
            Kind = (EventKind)kind,
            ProcessName = process_name,
            WindowTitle = window_title,
            ExePath = exe_path,
            IdleSeconds = idle_seconds
        };
    }
}
