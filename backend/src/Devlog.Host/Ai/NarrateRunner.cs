using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Host.Ai;

/// <summary>
/// Job B: Session narrative runner.
/// Selects multi-activity sessions worth narrating, prompts LLM for a structured narrative,
/// validates evidence against hallucinations, and stores valid narratives.
/// </summary>
public sealed class NarrateRunner(
    ISessionReader sessionReader,
    INarrativeStore narrativeStore,
    IChatClient chatClient,
    AiOptions options)
{
    public async Task<int> RunAsync(
        string? sinceArg,
        int? limitOverride,
        bool dryRun,
        bool force,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var fromUtc = ParseSince(sinceArg, now);

        var summaries = await sessionReader.GetRangeAsync(fromUtc, now.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);

        // Filter sessions worth narrating:
        // Duration >= 300s (5 min), ActivityCount >= 2, non-synthetic.
        var eligible = summaries
            .Where(s => s.Session.DurationSeconds >= 300 && s.ActivityCount >= 2)
            .Where(s => !SyntheticData.IsSynthetic(s.Session.Project ?? "") && !SyntheticData.IsSynthetic(s.Session.ActivityKey))
            .ToList();

        var toProcess = new List<Core.Domain.SessionSummary>();
        foreach (var s in eligible)
        {
            if (force)
            {
                toProcess.Add(s);
                continue;
            }

            var existing = await narrativeStore.GetByStartUtcAsync(s.Session.StartUtc, ct).ConfigureAwait(false);
            if (existing is null || existing.Model != options.Model || existing.IsStale(s.Session, s.ActivityCount))
            {
                toProcess.Add(s);
            }
        }

        if (toProcess.Count == 0)
        {
            Console.WriteLine("\nNo sessions needing narration in the selected window.\n");
            return 0;
        }

        var limit = limitOverride ?? 20;
        var batch = toProcess
            .OrderByDescending(s => s.Session.DurationSeconds)
            .Take(limit)
            .ToList();

        var mode = dryRun ? "PROPOSED NARRATIVES (--dry-run, no database writes)" : "SESSION NARRATIVES";
        Console.WriteLine($"\n=== {mode} ({batch.Count} sessions) ===\n");

        int accepted = 0;
        int rejected = 0;

        foreach (var s in batch)
        {
            var activities = await sessionReader.GetActivitiesAsync(s.Session.Id, ct).ConfigureAwait(false);
            var commits = await sessionReader.GetCommitsForSessionAsync(s.Session.Id, ct).ConfigureAwait(false);

            var userContent = SessionNarratorPrompt.BuildUserContent(s, activities, commits);
            var chatResult = await chatClient.CompleteAsync(
                SessionNarratorPrompt.SystemPrompt,
                userContent,
                SessionNarratorPrompt.SchemaName,
                SessionNarratorPrompt.JsonSchema,
                reasoningEffort: "high",
                ct).ConfigureAwait(false);

            if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
            {
                Console.WriteLine($"  [unreachable] Session {s.Session.Id}: {chatResult.Error ?? "no response"}");
                rejected++;
                continue;
            }

            SessionNarrativeResult parseResult;
            try
            {
                parseResult = SessionNarratorPrompt.ValidateAndParse(
                    chatResult.Content,
                    s,
                    activities,
                    commits,
                    options.MinConfidence,
                    options.Model,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [parse-error] Session {s.Session.Id}: {ex.Message}");
                rejected++;
                continue;
            }

            if (!parseResult.IsAccepted || parseResult.Narrative is null)
            {
                Console.WriteLine($"  [rejected] Session {s.Session.Id}: {parseResult.RejectionReason}");
                rejected++;
                continue;
            }

            var n = parseResult.Narrative;
            if (!dryRun)
            {
                await narrativeStore.UpsertAsync(n, ct).ConfigureAwait(false);
            }

            accepted++;
            var durationStr = Humanise(s.Session.DurationSeconds);
            var projectStr = string.IsNullOrWhiteSpace(s.Session.Project) ? "" : $" ({s.Session.Project})";
            var wsStr = string.IsNullOrWhiteSpace(n.Workstream) ? "" : $" [{n.Workstream}]";
            Console.WriteLine($"  Session {s.Session.Id}{projectStr} {durationStr} -> [{n.Kind}]{wsStr} (confidence: {n.Confidence:F2})");
            Console.WriteLine($"    \"{n.Narrative}\"");
            Console.WriteLine($"    Evidence: {string.Join(" | ", n.Evidence)}");
            Console.WriteLine();
        }

        Console.WriteLine($"\n  Finished: {accepted} accepted, {rejected} rejected/skipped.\n");
        return 0;
    }

    private static long ParseSince(string? since, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(since))
        {
            return now.AddDays(-7).ToUnixTimeMilliseconds();
        }

        if (since.EndsWith("d", StringComparison.OrdinalIgnoreCase) && int.TryParse(since[..^1], out var days))
        {
            return now.AddDays(-days).ToUnixTimeMilliseconds();
        }

        if (since.EndsWith("h", StringComparison.OrdinalIgnoreCase) && int.TryParse(since[..^1], out var hours))
        {
            return now.AddHours(-hours).ToUnixTimeMilliseconds();
        }

        if (DateTimeOffset.TryParse(since, out var parsed))
        {
            return parsed.ToUnixTimeMilliseconds();
        }

        return now.AddDays(-7).ToUnixTimeMilliseconds();
    }

    private static string Humanise(int seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m{seconds % 60:00}s",
        _ => $"{seconds / 3600}h{seconds % 3600 / 60:00}m"
    };
}
