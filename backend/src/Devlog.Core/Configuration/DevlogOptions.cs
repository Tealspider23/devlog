namespace Devlog.Core.Configuration;

/// <summary>
/// Everything tunable, in one place. Bound from <c>appsettings.json</c>.
/// <para>
/// Note what is <b>absent</b>: there is no idle threshold here. Whether 200
/// seconds without input means "reading documentation" or "went to lunch" is a
/// derivation-time decision, and <c>raw_event.idle_seconds</c> stores the raw
/// measurement so that call can be changed later without re-collecting.
/// </para>
/// </summary>
public sealed class DevlogOptions
{
    public const string SectionName = "Devlog";

    /// <summary>
    /// How long a focus change must survive before it is recorded. Discards
    /// alt-tab flicker and notification focus steals; a genuine file or app
    /// switch outlives it comfortably.
    /// </summary>
    public int DebounceMilliseconds { get; set; } = 1500;

    /// <summary>
    /// How often to sample idle state. Focus changes are pushed by the OS, so
    /// this timer exists only to notice idle transitions and due heartbeats —
    /// which is why it can be slow.
    /// </summary>
    public int IdlePollSeconds { get; set; } = 20;

    /// <summary>
    /// Maximum gap between rows while engaged. Bounds the span an unclean
    /// shutdown can leave open. Zero disables heartbeats.
    /// </summary>
    public int HeartbeatMinutes { get; set; } = 5;

    /// <summary>
    /// Stop emitting heartbeats once idle passes this. One row saying "went
    /// idle" carries everything; repeating it every five minutes all night
    /// carries nothing. This is what makes overnight and weekends nearly free.
    /// </summary>
    public int SuppressHeartbeatAfterIdleSeconds { get; set; } = 300;

    /// <summary>How often the in-memory buffer is flushed to SQLite.</summary>
    public int FlushIntervalSeconds { get; set; } = 10;

    /// <summary>Flush early if the buffer reaches this many rows.</summary>
    public int FlushMaxBatch { get; set; } = 100;

    /// <summary>
    /// Defaults to <c>%LOCALAPPDATA%\devlog\devlog.db</c> when left empty.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Never recorded — not recorded-then-filtered. With or without <c>.exe</c>.
    /// </summary>
    public string[] ExcludedProcesses { get; set; } = [];

    /// <summary>
    /// Regex, matched case-insensitively against the raw window title.
    /// An invalid pattern stops startup rather than being silently ignored.
    /// </summary>
    public string[] ExcludedTitlePatterns { get; set; } = [];

    public TimeSpan Debounce => TimeSpan.FromMilliseconds(Math.Max(0, DebounceMilliseconds));

    public TimeSpan IdlePollInterval => TimeSpan.FromSeconds(Math.Max(1, IdlePollSeconds));

    public TimeSpan HeartbeatInterval => TimeSpan.FromMinutes(Math.Max(0, HeartbeatMinutes));

    public TimeSpan SuppressHeartbeatAfterIdle =>
        TimeSpan.FromSeconds(Math.Max(0, SuppressHeartbeatAfterIdleSeconds));

    public TimeSpan FlushInterval => TimeSpan.FromSeconds(Math.Max(1, FlushIntervalSeconds));

    /// <summary>Resolves <see cref="DatabasePath"/>, applying the default and creating the folder.</summary>
    public string ResolveDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(DatabasePath))
        {
            var custom = Environment.ExpandEnvironmentVariables(DatabasePath);
            EnsureDirectory(custom);
            return custom;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "devlog");
        var path = Path.Combine(dir, "devlog.db");
        EnsureDirectory(path);
        return path;
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
