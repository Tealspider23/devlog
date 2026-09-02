using Dapper;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

public sealed record PurgeResult(int RawEvents, int PendingRules);

/// <summary>
/// Destructive maintenance, kept deliberately apart from <see cref="EventStore"/>.
/// <para>
/// <c>raw_event</c> is the append-only source of truth and
/// <see cref="Core.Abstractions.IEventStore"/> offers no delete for exactly that
/// reason. Removing generated fixtures is real and occasionally necessary, but it
/// is maintenance rather than normal operation — so it lives behind a separate,
/// obviously-named type rather than quietly widening the contract everything else
/// depends on.
/// </para>
/// </summary>
public sealed class MaintenanceStore(ISqliteConnectionFactory factory)
{
    public async Task<int> CountSyntheticAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM raw_event WHERE window_title LIKE @p;",
            new { p = SyntheticData.LikePattern },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes generated fixtures and the unanswered classification rows that
    /// only existed because of them.
    /// <para>
    /// Derived tables are deliberately left alone: every activity and session
    /// built from these rows is now invalid, and the caller re-derives rather
    /// than this attempting a surgical repair. Rebuilding from source is both
    /// simpler and the behaviour the whole derived/disposable split exists for.
    /// </para>
    /// </summary>
    public async Task<PurgeResult> PurgeSyntheticAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var events = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM raw_event WHERE window_title LIKE @p;",
            new { p = SyntheticData.LikePattern },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        // Unanswered rules naming a synthetic identity would otherwise linger as
        // unknowns pointing at data that no longer exists. Answered rules are
        // left untouched — a verdict is yours, even if it was reached about
        // fixture data, and re-deriving simply stops referencing it.
        var rules = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM classification_rule WHERE category IS NULL AND site LIKE @p;",
            new { p = SyntheticData.LikePattern },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);

        return new PurgeResult(events, rules);
    }
}
