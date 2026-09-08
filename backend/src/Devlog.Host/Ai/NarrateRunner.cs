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
    AiOptions options) : INarrateRunner
{
    public async Task<NarrateResult> RunAsync(
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
            return new NarrateResult(dryRun, AcceptedCount: 0, RejectedCount: 0, Outcomes: []);
        }

        var limit = limitOverride ?? 20;
        var toNarrate = toProcess
            .OrderByDescending(s => s.Session.DurationSeconds)
            .Take(limit)
            .ToList();

        var outcomes = new List<NarrateOutcome>();
        int accepted = 0;
        int rejected = 0;

        // One request per batch, not per session — a request costs the same
        // against the provider's daily quota regardless of size, so batching
        // is what makes a fixed quota cover more than `batchSize` sessions a
        // day. See AiOptions.NarrateBatchSize.
        var batchSize = Math.Max(1, options.NarrateBatchSize);
        foreach (var chunk in toNarrate.Chunk(batchSize))
        {
            var inputs = new List<SessionNarrationInput>(chunk.Length);
            foreach (var s in chunk)
            {
                var activities = await sessionReader.GetActivitiesAsync(s.Session.Id, ct).ConfigureAwait(false);
                var commits = await sessionReader.GetCommitsForSessionAsync(s.Session.Id, ct).ConfigureAwait(false);
                inputs.Add(new SessionNarrationInput(s, activities, commits));
            }

            var userContent = SessionNarratorPrompt.BuildBatchUserContent(inputs);
            var chatResult = await chatClient.CompleteAsync(
                SessionNarratorPrompt.BatchSystemPrompt,
                userContent,
                SessionNarratorPrompt.BatchSchemaName,
                SessionNarratorPrompt.BatchJsonSchema,
                reasoningEffort: "high",
                ct).ConfigureAwait(false);

            if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
            {
                var reason = chatResult.Error ?? "no response";
                foreach (var s in chunk)
                {
                    rejected++;
                    outcomes.Add(new NarrateOutcome(s.Session.Id, s.Session.StartUtc, s.Session.Project, s.Session.DurationSeconds, false, null, reason));
                }
                continue;
            }

            IReadOnlyList<SessionNarrativeResult> parseResults;
            try
            {
                parseResults = SessionNarratorPrompt.ValidateAndParseBatch(
                    chatResult.Content,
                    inputs,
                    options.MinConfidence,
                    options.Model,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            catch (Exception ex)
            {
                foreach (var s in chunk)
                {
                    rejected++;
                    outcomes.Add(new NarrateOutcome(s.Session.Id, s.Session.StartUtc, s.Session.Project, s.Session.DurationSeconds, false, null, ex.Message));
                }
                continue;
            }

            // parseResults is positional against inputs/chunk — ValidateAndParseBatch
            // guarantees one result per input, in the same order, regardless of how
            // the model ordered its own response.
            for (int i = 0; i < chunk.Length; i++)
            {
                var s = chunk[i];
                var parseResult = parseResults[i];

                if (!parseResult.IsAccepted || parseResult.Narrative is null)
                {
                    rejected++;
                    outcomes.Add(new NarrateOutcome(s.Session.Id, s.Session.StartUtc, s.Session.Project, s.Session.DurationSeconds, false, null, parseResult.RejectionReason));
                    continue;
                }

                var n = parseResult.Narrative;
                if (!dryRun)
                {
                    await narrativeStore.UpsertAsync(n, ct).ConfigureAwait(false);
                }

                accepted++;
                outcomes.Add(new NarrateOutcome(s.Session.Id, s.Session.StartUtc, s.Session.Project, s.Session.DurationSeconds, true, n, null));
            }
        }

        return new NarrateResult(dryRun, accepted, rejected, outcomes);
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
}
