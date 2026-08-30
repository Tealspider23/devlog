using System.Text;
using Dapper;
using Devlog.Infrastructure.Persistence;

namespace Devlog.Host.Diagnostics;

/// <summary>
/// Prints what is actually in the database.
/// <para>
/// Exists because there is no sqlite3 on this machine, and because the questions
/// worth asking after a day of capture are always the same ones: is the row
/// volume sane, is WAL really on, and — the one that drives Phase 2 — what do my
/// real window titles actually look like?
/// </para>
/// </summary>
public sealed class StatsReporter(ISqliteConnectionFactory factory)
{
    /// <summary>
    /// Prefix that synthetic rows carry in their window title, so every report
    /// can separate real capture from generated history. Declared here rather
    /// than on the generator, so the diagnostics do not depend on it existing.
    /// </summary>
    private const string SeedMarker = "[seed]";


    public string Report()
    {
        using var db = factory.Open();
        var sb = new StringBuilder();

        void Section(string title)
        {
            sb.AppendLine().AppendLine($"=== {title} ===");
        }

        sb.AppendLine($"database   : {factory.DatabasePath}");
        sb.AppendLine($"size       : {FormatBytes(new FileInfo(factory.DatabasePath).Length)}");
        sb.AppendLine($"journal    : {db.ExecuteScalar<string>("PRAGMA journal_mode;")}   (must be 'wal')");
        sb.AppendLine($"schema ver : {db.ExecuteScalar<int>("SELECT version FROM schema_version;")}");

        var total = db.ExecuteScalar<long>("SELECT COUNT(*) FROM raw_event;");
        var seeded = db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM raw_event WHERE window_title LIKE @m;",
            new { m = SeedMarker + "%" });

        sb.AppendLine($"raw_event  : {total} rows  ({seeded} seeded, {total - seeded} real)");

        Section("ROWS PER DAY (local)");
        foreach (var r in db.Query(
            """
            SELECT DATE(ts_utc / 1000, 'unixepoch', 'localtime') AS d,
                   COUNT(*) AS n,
                   SUM(CASE WHEN window_title LIKE '[seed]%' THEN 0 ELSE 1 END) AS real_n
            FROM raw_event GROUP BY d ORDER BY d;
            """))
        {
            sb.AppendLine($"  {r.d}   {r.n,5} rows   ({r.real_n} real)");
        }

        Section("BY KIND");
        foreach (var r in db.Query(
            """
            SELECT kind, COUNT(*) n FROM raw_event GROUP BY kind ORDER BY kind;
            """))
        {
            sb.AppendLine($"  {KindName((int)r.kind),-16} {r.n,5}");
        }

        Section("TOP PROCESSES (real capture only)");
        foreach (var r in db.Query(
            """
            SELECT process_name, COUNT(*) n FROM raw_event
            WHERE process_name IS NOT NULL AND window_title NOT LIKE '[seed]%'
            GROUP BY 1 ORDER BY n DESC LIMIT 15;
            """))
        {
            sb.AppendLine($"  {r.process_name,-28} {r.n,5}");
        }

        // The query that drives Phase 2: normalization rules get written against
        // these strings, not against assumptions about their shape.
        Section("TOP REAL TITLES  <- write Phase 2 extraction rules against these");
        foreach (var r in db.Query(
            """
            SELECT window_title, COUNT(*) n FROM raw_event
            WHERE window_title IS NOT NULL AND window_title NOT LIKE '[seed]%'
            GROUP BY 1 ORDER BY n DESC LIMIT 20;
            """))
        {
            var t = (string)r.window_title;
            sb.AppendLine($"  {r.n,4}x  {(t.Length > 90 ? t[..90] + "..." : t)}");
        }

        // If the WinEvent hook is delivering, real switches land at arbitrary
        // moments and these buckets are spread out. If they cluster on a couple
        // of values, the hook is dead and we are silently running on the idle
        // timer's grid instead — which is both coarser and slower than the
        // polling it was meant to replace.
        Section("HOOK HEALTH — focus timestamps mod IdlePollSeconds (current run only)");

        // Scoped to the latest collector_start. Mixing runs is misleading: a
        // fix applied mid-history looks like a partial success when it is really
        // two different behaviours averaged together.
        var runStart = db.ExecuteScalar<long?>(
            "SELECT MAX(ts_utc) FROM raw_event WHERE kind = 6;") ?? 0;

        var buckets = db.Query(
            """
            SELECT (ts_utc / 1000) % 20 AS bucket, COUNT(*) n
            FROM raw_event
            WHERE kind = 0 AND ts_utc >= @start
              AND (window_title IS NULL OR window_title NOT LIKE '[seed]%')
            GROUP BY 1 ORDER BY n DESC;
            """, new { start = runStart }).ToList();

        var totalFocus = buckets.Sum(b => (long)b.n);
        if (totalFocus < 6)
        {
            sb.AppendLine($"  only {totalFocus} focus events this run — not enough to judge.");
            sb.AppendLine("  Switch between several windows, then re-run --stats.");
        }
        else
        {
            foreach (var b in buckets.Take(10))
            {
                sb.AppendLine($"  +{b.bucket,2}s   {new string('#', (int)Math.Min(40, (long)b.n))} {b.n}");
            }

            // A dead hook leaves every event pinned to the timer tick plus the
            // debounce, so nearly all of them share one or two offsets.
            var distinct = buckets.Count;
            var topShare = (double)(long)buckets[0].n / totalFocus;

            sb.AppendLine();
            sb.AppendLine(distinct <= 3 || topShare > 0.6
                ? $"  VERDICT: {distinct} offsets, top={topShare:P0} of {totalFocus} — TIMER-DRIVEN, hook is NOT firing."
                : $"  VERDICT: {distinct} offsets, top={topShare:P0} of {totalFocus} — hook is firing.");
        }

        Section("LAST 15 EVENTS");
        foreach (var r in db.Query(
            """
            SELECT ts_utc, kind, process_name, window_title, idle_seconds
            FROM raw_event ORDER BY id DESC LIMIT 15;
            """))
        {
            var when = DateTimeOffset.FromUnixTimeMilliseconds((long)r.ts_utc).ToLocalTime();
            var title = (string?)r.window_title ?? "-";
            sb.AppendLine(
                $"  {when:HH:mm:ss}  {KindName((int)r.kind),-14} idle={r.idle_seconds,4}s  "
                + $"{r.process_name,-18} {(title.Length > 60 ? title[..60] + "..." : title)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Recent real events with the gap to the next one — i.e. how long that
    /// context actually held the foreground.
    /// <para>
    /// Not derivation, just arithmetic on adjacent rows. Useful for sanity
    /// checking capture against your own memory of the last hour, which is the
    /// only ground truth available before Phase 2 exists.
    /// </para>
    /// </summary>
    public string Events(int count, string? processFilter = null)
    {
        using var db = factory.Open();
        var sb = new StringBuilder();

        var rows = db.Query(
            """
            SELECT ts_utc, kind, process_name, window_title, idle_seconds
            FROM raw_event
            WHERE (window_title IS NULL OR window_title NOT LIKE '[seed]%')
              AND (@proc IS NULL OR process_name = @proc)
            ORDER BY ts_utc DESC LIMIT @n;
            """, new { n = count, proc = processFilter }).Reverse().ToList();

        if (rows.Count == 0)
        {
            return "no matching events\n";
        }

        sb.AppendLine($"=== LAST {rows.Count} REAL EVENTS"
            + (processFilter is null ? "" : $" (process = {processFilter})") + " ===");
        sb.AppendLine();

        var totalHeld = 0L;

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)r.ts_utc).ToLocalTime();

            var held = i < rows.Count - 1
                ? ((long)rows[i + 1].ts_utc - (long)r.ts_utc) / 1000
                : 0;
            totalHeld += held;

            var title = (string?)r.window_title ?? "-";
            sb.AppendLine(
                $"  {ts:HH:mm:ss}  {FormatHeld(held),8}  idle={r.idle_seconds,4}s  "
                + $"{KindName((int)r.kind),-9} {r.process_name,-18} "
                + $"{(title.Length > 62 ? title[..62] + "…" : title)}");
        }

        sb.AppendLine();
        sb.AppendLine($"  span: {FormatHeld(totalHeld)} across {rows.Count} events");
        return sb.ToString();
    }

    /// <summary>
    /// Derived sessions, newest last, with the activities that formed them.
    /// <para>
    /// The duration column is the one to scrutinise. A session running for many
    /// hours means a span crossed a lock or a shutdown, which would corrupt every
    /// downstream KPI — the failure this pipeline is built to prevent.
    /// </para>
    /// </summary>
    public string Sessions(int count)
    {
        using var db = factory.Open();
        var sb = new StringBuilder();

        var rows = db.Query(
            """
            SELECT s.id, s.start_utc, s.end_utc, s.project, s.category,
                   s.interruptions, s.deep_seconds, s.label,
                   (SELECT COUNT(*) FROM activity a WHERE a.session_id = s.id) AS activity_count
            FROM session s
            ORDER BY s.start_utc DESC
            LIMIT @n;
            """, new { n = count }).Reverse().ToList();

        if (rows.Count == 0)
        {
            return "no sessions — run --derive first\n";
        }

        sb.AppendLine($"=== LAST {rows.Count} SESSIONS ===");
        sb.AppendLine();

        long longest = 0;

        foreach (var r in rows)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds((long)r.start_utc).ToLocalTime();
            var end = DateTimeOffset.FromUnixTimeMilliseconds((long)r.end_utc).ToLocalTime();
            var seconds = ((long)r.end_utc - (long)r.start_utc) / 1000;
            longest = Math.Max(longest, seconds);

            var label = (string?)r.label ?? (string?)r.project ?? (string)r.category;
            var interruptions = (long)r.interruptions;

            sb.AppendLine(
                $"  {start:MM-dd HH:mm}–{end:HH:mm}  {FormatHeld(seconds),8}  "
                + $"{(string)r.category,-14} {label,-22} "
                + $"deep={FormatHeld((long)r.deep_seconds),7}  "
                + $"{interruptions} int  {r.activity_count} act");
        }

        sb.AppendLine();
        sb.AppendLine($"  longest session: {FormatHeld(longest)}");
        sb.AppendLine(longest > 4 * 3600
            ? "  WARNING: a session over 4h almost certainly spans a lock or shutdown."
            : "  OK: no session long enough to suggest a span crossed a boundary.");

        var unclassified = db.ExecuteScalar<long>(
            """
            SELECT COALESCE(SUM(end_utc - start_utc) / 1000, 0)
            FROM activity WHERE category = 'Other';
            """);

        sb.AppendLine($"  unclassified activity time: {FormatHeld(unclassified)}  (see --unknowns)");

        return sb.ToString();
    }

    private static string FormatHeld(long seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m{seconds % 60:00}s",
        _ => $"{seconds / 3600}h{seconds % 3600 / 60:00}m"
    };

    private static string KindName(int kind) => kind switch
    {
        0 => "focus",
        1 => "heartbeat",
        2 => "lock",
        3 => "unlock",
        4 => "suspend",
        5 => "resume",
        6 => "collector_start",
        7 => "collector_stop",
        _ => $"unknown({kind})"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
