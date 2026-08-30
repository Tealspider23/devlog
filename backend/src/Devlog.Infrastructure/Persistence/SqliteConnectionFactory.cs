using Devlog.Core.Configuration;
using Microsoft.Data.Sqlite;

namespace Devlog.Infrastructure.Persistence;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }

    SqliteConnection Open();

    Task<SqliteConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(DevlogOptions options)
    {
        DatabasePath = options.ResolveDatabasePath();

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // Shared cache is deliberately off. The UI process is a second
            // connection to this file and WAL handles that correctly; shared
            // cache would introduce table-level locking we do not want.
            Cache = SqliteCacheMode.Default,

            // Wait rather than failing instantly if the other process holds a
            // write lock during a flush.
            DefaultTimeout = 15
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Configure(connection);
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        Configure(connection);
        return connection;
    }

    /// <summary>
    /// Per-connection pragmas. <c>journal_mode</c> is not set here — it is a
    /// persistent property of the database file and is applied once at
    /// initialization, before any writes.
    /// </summary>
    private static void Configure(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 15000;
            PRAGMA synchronous = NORMAL;
            """;
        cmd.ExecuteNonQuery();
    }
}
