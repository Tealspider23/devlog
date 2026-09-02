using System.Runtime.InteropServices;

namespace Devlog.Host.Commands;

/// <summary>
/// Minimal argument parsing. Deliberately not a CLI framework — these are
/// developer-facing diagnostics for a tray app, not a public interface, and a
/// dependency would cost more than it saves.
/// </summary>
public sealed class CommandLine(string[] args)
{
    private readonly string[] _args = Normalise(args);

    /// <summary>
    /// Turns a leading subcommand into the flag the dispatcher already
    /// understands: <c>sessions 20</c> becomes <c>--sessions 20</c>.
    /// <para>
    /// Doing it here rather than in a parallel dispatch table is what lets both
    /// spellings coexist for free and forever. Everything written down so far —
    /// the README, the docs, a week of shell history — used flags, and breaking
    /// that to gain a nicer surface would be a poor trade.
    /// </para>
    /// </summary>
    public static string[] Normalise(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            return args;
        }

        var normalised = (string[])args.Clone();
        normalised[0] = "--" + args[0];
        return normalised;
    }

    /// <summary>
    /// The subcommand as typed, with no leading dashes — null when nothing was
    /// asked for. Used to decide between help, a real command, and a typo.
    /// </summary>
    public static string? CommandName(string[] args) =>
        args.Length == 0 ? null : args[0].TrimStart('-');

    public static bool WantsHelp(string[] args) =>
        args.Length == 0
        || args.Any(a => a is "--help" or "-h" or "-?" or "/?" or "help");

    public bool Has(string flag) =>
        _args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value following <paramref name="flag"/>, if any.</summary>
    public string? Value(string flag)
    {
        var i = Array.FindIndex(_args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        if (i < 0 || i + 1 >= _args.Length)
        {
            return null;
        }

        var next = _args[i + 1];
        return next.StartsWith("--", StringComparison.Ordinal) ? null : next;
    }

    public int ValueOrDefault(string flag, int fallback) =>
        int.TryParse(Value(flag), out var n) && n > 0 ? n : fallback;

    /// <summary>Positional values after a flag, up to the next flag.</summary>
    public string[] ValuesAfter(string flag)
    {
        var i = Array.FindIndex(_args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        if (i < 0)
        {
            return [];
        }

        return [.. _args
            .Skip(i + 1)
            .TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))];
    }

    public static void TrySetUtf8Console()
    {
        try
        {
            // Window titles are full Unicode — em-dashes, the VS Code modified
            // dot, CJK. Without this the console mangles them and it looks like a
            // storage bug when it is only a display one.
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached (launched from Explorer). Nothing to encode.
        }
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    /// <summary>
    /// Binds this process's stdout/stdin to the console window that launched it.
    /// <para>
    /// The exe is a GUI-subsystem app (<c>WinExe</c>, needed for the tray icon).
    /// Windows does not attach a GUI app's output to the parent console
    /// automatically — run it with any argument from an interactive PowerShell
    /// window and every <c>Console.WriteLine</c> silently goes nowhere, with no
    /// error. It looks identical to the command doing nothing.
    /// </para>
    /// <para>
    /// MUST be called before .NET's <see cref="Console"/> class is touched for
    /// the first time — the OS handles are bound lazily on first access, so this
    /// is the literal first line of <c>Main</c>. Only attempted when arguments
    /// are present: a bare double-click launch (zero args, tray mode) must stay
    /// silent and undisturbed.
    /// </para>
    /// </summary>
    public static void AttachToParentConsoleIfInvokedWithArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch (Exception)
        {
            // No parent console to attach to (e.g. launched by a scheduler).
            // Diagnostics degrade to invisible, exactly as before this fix.
        }
    }
}
