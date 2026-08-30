namespace Devlog.Host.Commands;

/// <summary>
/// Minimal argument parsing. Deliberately not a CLI framework — these are
/// developer-facing diagnostics for a tray app, not a public interface, and a
/// dependency would cost more than it saves.
/// </summary>
public sealed class CommandLine(string[] args)
{
    private readonly string[] _args = args;

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
}
