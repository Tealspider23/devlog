using System.Text;

namespace Devlog.Host.Commands;

/// <summary>
/// What you see when you type <c>devlog</c> with nothing after it.
/// <para>
/// Rendered from <see cref="CommandCatalog"/> rather than written out by hand,
/// so a command can never exist without appearing here — the usual way a CLI
/// grows features nobody knows about.
/// </para>
/// </summary>
public static class HelpScreen
{
    /// <param name="status">
    /// A one-line summary of the current database, or null when it could not be
    /// read. Help must still render if the database is missing or locked: the
    /// moment you most need the help screen is when something is broken.
    /// </param>
    public static string Render(string? status = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("  devlog — what you attended to, against what you shipped");

        if (!string.IsNullOrWhiteSpace(status))
        {
            sb.AppendLine();
            sb.AppendLine($"  {status}");
        }

        sb.AppendLine();
        sb.AppendLine("  USAGE");
        sb.AppendLine("    devlog <command> [arguments]");

        var width = CommandCatalog.All.Max(c => c.Usage.Length);

        foreach (var group in CommandCatalog.Groups)
        {
            sb.AppendLine();
            sb.AppendLine($"  {group}");

            foreach (var c in CommandCatalog.All.Where(c => c.Group == group))
            {
                sb.AppendLine($"    {c.Usage.PadRight(width)}   {c.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("""
              NOTES
                Flags still work — `devlog --sessions 20` is the same as
                `devlog sessions 20`, so nothing already written down breaks.

                The collector is a separate program (Devlog.Host.exe, in the tray).
                This command only reads and rebuilds; it never captures anything.
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Shown instead of running anything when the command is not recognised.
    /// <para>
    /// The exit code is 2, not 0, so a typo in a script fails loudly. It must
    /// never fall through to launching the collector, which is what the old
    /// "unrecognised argument" path did.
    /// </para>
    /// </summary>
    public static string Unknown(string typed)
    {
        var suggestion = CommandCatalog.ClosestTo(typed);

        return suggestion is null
            ? $"\n  Unknown command: {typed}\n  Run `devlog` on its own to see what there is.\n"
            : $"\n  Unknown command: {typed}\n  Did you mean `devlog {suggestion}`?\n";
    }
}
