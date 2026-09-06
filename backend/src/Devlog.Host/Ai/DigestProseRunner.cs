using System.Text;
using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Metrics;

namespace Devlog.Host.Ai;

/// <summary>
/// Job C: Generates opening summary prose for a date range digest.
/// Enforces that the model never computes numbers and validates all numbers against DigestMetrics.
/// </summary>
public sealed class DigestProseRunner(
    INarrativeStore narrativeStore,
    IChatClient chatClient,
    AiOptions options)
{
    public async Task<(string? ProseMarkdown, string? Note)> GenerateProseAsync(
        DigestMetrics metrics,
        long fromUtc,
        long toUtc,
        CancellationToken ct = default)
    {
        if (!options.Enabled || !options.Jobs.Digest)
        {
            return (null, "AI features or digest job are disabled in configuration.");
        }

        var reachable = await chatClient.IsReachableAsync(ct).ConfigureAwait(false);
        if (!reachable)
        {
            return (null, "AI provider is unreachable.");
        }

        var narratives = await narrativeStore.GetRangeAsync(fromUtc, toUtc, ct).ConfigureAwait(false);

        var (userContent, figures) = DigestProsePrompt.BuildUserContent(metrics, narratives);

        var chatResult = await chatClient.CompleteAsync(
            DigestProsePrompt.SystemPrompt,
            userContent,
            DigestProsePrompt.SchemaName,
            DigestProsePrompt.JsonSchema,
            reasoningEffort: "high",
            ct).ConfigureAwait(false);

        if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
        {
            return (null, $"AI provider returned no content: {chatResult.Error ?? "unknown error"}");
        }

        DigestProseResult parseResult;
        try
        {
            parseResult = DigestProsePrompt.ValidateAndParse(chatResult.Content, figures);
        }
        catch (Exception ex)
        {
            return (null, $"Malformed response from model: {ex.Message}");
        }

        if (!parseResult.IsAccepted || parseResult.Prose is null)
        {
            return (null, $"Prose was rejected: {parseResult.RejectionReason}");
        }

        var prose = parseResult.Prose;
        var sb = new StringBuilder();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(prose.Summary);
        sb.AppendLine();

        if (prose.Highlights.Count > 0)
        {
            sb.AppendLine("### Highlights");
            foreach (var h in prose.Highlights)
            {
                sb.AppendLine($"- {h}");
            }
            sb.AppendLine();
        }

        return (sb.ToString(), null);
    }
}
