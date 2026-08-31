using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

public sealed class CommitStore(ISqliteConnectionFactory factory) : ICommitStore
{
    public async Task<HashSet<string>> GetKnownShasAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var shas = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT sha FROM commit_record;", cancellationToken: ct)).ConfigureAwait(false);

        return new HashSet<string>(shas, StringComparer.OrdinalIgnoreCase);
    }

    public async Task UpsertAsync(IReadOnlyList<CommitRecord> commits, CancellationToken ct = default)
    {
        if (commits.Count == 0)
        {
            return;
        }

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Diff stats are only ever computed once for a given sha - the scanner
        // skips known shas before it gets here - so an upsert only needs to
        // touch branch/session on conflict, never the expensive columns.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO commit_record
              (sha, repo, project, ts_utc, message, branch, author_email,
               files_changed, insertions, deletions, languages, is_merge, session_id)
            VALUES
              (@Sha, @Repo, @Project, @TsUtc, @Message, @Branch, @AuthorEmail,
               @FilesChanged, @Insertions, @Deletions, @Languages, @IsMerge, @SessionId)
            ON CONFLICT (sha) DO UPDATE SET
              branch     = excluded.branch,
              session_id = excluded.session_id;
            """,
            commits.Select(c => new
            {
                c.Sha,
                c.Repo,
                c.Project,
                c.TsUtc,
                c.Message,
                c.Branch,
                c.AuthorEmail,
                c.FilesChanged,
                c.Insertions,
                c.Deletions,
                c.Languages,
                IsMerge = c.IsMerge ? 1 : 0,
                c.SessionId
            }),
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<CommitRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<CommitRow>(new CommandDefinition(
            """
            SELECT sha, repo, project, ts_utc, message, branch, author_email,
                   files_changed, insertions, deletions, languages, is_merge, session_id
            FROM commit_record
            ORDER BY ts_utc;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task RelinkAsync(IReadOnlyDictionary<string, long?> shaToSessionId, CancellationToken ct = default)
    {
        if (shaToSessionId.Count == 0)
        {
            return;
        }

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // The cheap half of a re-derive: session ids change on every rebuild,
        // but this never re-reads a repo or recomputes a diff.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE commit_record SET session_id = @SessionId WHERE sha = @Sha;",
            shaToSessionId.Select(kv => new { Sha = kv.Key, SessionId = kv.Value }),
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
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
            Project = project ?? "?",
            TsUtc = ts_utc,
            Message = message,
            Branch = branch,
            AuthorEmail = author_email ?? "?",
            FilesChanged = files_changed,
            Insertions = insertions,
            Deletions = deletions,
            Languages = languages,
            IsMerge = is_merge != 0,
            SessionId = session_id
        };
    }
}
