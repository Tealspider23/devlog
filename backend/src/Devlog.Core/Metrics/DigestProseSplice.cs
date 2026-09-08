namespace Devlog.Core.Metrics;

/// <summary>
/// Splices Job C's prose into the deterministic digest markdown, right after
/// the header line. Pulled out of <c>devlog digest --prose</c> so
/// <c>GET /v1/digest?prose=true</c> can do exactly the same splice — the
/// CLI/API byte-identical property applies to the prose variant too, not just
/// the plain one.
/// </summary>
public static class DigestProseSplice
{
    public static string Apply(string markdown, DigestMetrics metrics, string? proseMarkdown, string? note)
    {
        if (proseMarkdown is not null)
        {
            var lines = markdown.Split('\n');
            var headerLine = lines.FirstOrDefault(l => l.StartsWith("# devlog", StringComparison.OrdinalIgnoreCase))
                ?? $"# devlog — {metrics.From:MMM d} to {metrics.To:MMM d, yyyy}";
            var restOfMarkdown = string.Join('\n', lines.Skip(1));

            return $"{headerLine}\n\n{proseMarkdown.TrimEnd()}\n\n{restOfMarkdown.TrimStart()}";
        }

        if (note is not null)
        {
            return markdown + $"\n\n*Note: Prose summary was skipped ({note})*\n";
        }

        return markdown;
    }
}
