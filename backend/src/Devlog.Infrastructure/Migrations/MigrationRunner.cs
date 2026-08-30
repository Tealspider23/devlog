using System.Reflection;
using Devlog.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Devlog.Infrastructure.Migrations;

/// <summary>
/// Applies embedded <c>.sql</c> files in filename order, tracked by
/// <c>schema_version</c>.
/// <para>
/// Deliberately hand-rolled rather than EF Core: this is a tray app that should
/// start instantly, the schema is small, and the migration files double as the
/// clearest documentation of the storage contract.
/// </para>
/// </summary>
public sealed class MigrationRunner(ISqliteConnectionFactory factory, ILogger<MigrationRunner> logger)
{
    private const string ResourcePrefix = "Devlog.Infrastructure.Migrations.";

    public void Run()
    {
        using var connection = factory.Open();

        EnableWalMode(connection);
        EnsureVersionTable(connection);

        var current = GetCurrentVersion(connection);
        var migrations = LoadMigrations();
        var applied = 0;

        foreach (var (version, name, sql) in migrations)
        {
            if (version <= current)
            {
                continue;
            }

            using var tx = connection.BeginTransaction();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = tx;
                stamp.CommandText = "UPDATE schema_version SET version = $v;";
                stamp.Parameters.AddWithValue("$v", version);
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
            applied++;
            logger.LogInformation("Applied migration {Version} ({Name})", version, name);
        }

        if (applied == 0)
        {
            logger.LogInformation("Schema up to date at version {Version}", current);
        }
    }

    /// <summary>
    /// WAL is a persistent property of the file, set once before any writes.
    /// It is what allows the UI process to read while the collector writes —
    /// retrofitting it later means debugging lock contention instead.
    /// </summary>
    private void EnableWalMode(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        var mode = cmd.ExecuteScalar() as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Expected WAL journal mode but database reports '{Mode}'", mode);
        }
    }

    private static void EnsureVersionTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
            INSERT INTO schema_version (version)
              SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
            """;
        cmd.ExecuteNonQuery();
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static List<(int Version, string Name, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var results = new List<(int, string, string)>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = resource[ResourcePrefix.Length..];
            var underscore = name.IndexOf('_');
            if (underscore <= 0 || !int.TryParse(name[..underscore], out var version))
            {
                throw new InvalidOperationException(
                    $"Migration '{name}' must be named like '001_description.sql'.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Could not read embedded migration '{resource}'.");
            using var reader = new StreamReader(stream);

            results.Add((version, name, reader.ReadToEnd()));
        }

        return [.. results.OrderBy(r => r.Item1)];
    }
}
