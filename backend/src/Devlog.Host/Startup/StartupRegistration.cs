using Microsoft.Win32;

namespace Devlog.Host.Startup;

/// <summary>
/// Registers the collector to launch at logon.
/// <para>
/// Deferred from Phase 1 as a packaging concern, which turned out to cost real
/// data: capture stopped at 10:26 on 2026-09-01 and nothing brought it back,
/// losing roughly a day of activity. A tracker that silently stops tracking is
/// worse than one that was never installed, because the gap is invisible until
/// you go looking.
/// </para>
/// <para>
/// Uses the per-user <c>Run</c> key rather than a scheduled task or a service:
/// the collector <em>must</em> run inside the interactive user session to see
/// the foreground window at all — the same constraint that rules out a Windows
/// Service, where session 0 isolation makes <c>GetForegroundWindow</c> useless.
/// </para>
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "devlog";

    /// <summary>The exe path as it would be registered, quoted for paths with spaces.</summary>
    private static string CommandLine =>
        $"\"{Path.Combine(AppContext.BaseDirectory, "Devlog.Host.exe")}\"";

    public static string? CurrentRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static bool IsRegistered =>
        string.Equals(CurrentRegistration(), CommandLine, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes (or refreshes) the registration. Rewriting on every enable is
    /// deliberate — a rebuild into a different output folder leaves a stale path
    /// pointing at an exe that no longer exists, and that failure is silent.
    /// </summary>
    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException($@"Could not open HKCU\{RunKey}");

        key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static string DescribeTarget() => CommandLine;
}
