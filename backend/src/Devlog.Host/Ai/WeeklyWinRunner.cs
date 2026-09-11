using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Core.Metrics;

namespace Devlog.Host.Ai;

/// <summary>
/// Job C, run once per calendar week within a month instead of once for the
/// whole range — the Month view's "story of the month" as one short summary
/// plus concrete wins per week. Reuses <see cref="DigestProsePrompt"/> exactly
/// as <see cref="DigestProseRunner"/> does (same system prompt, same schema,
/// same numeric-hallucination guard) rather than duplicating it — a week's
/// "win" is a digest-prose call, just scoped smaller.
/// </summary>
public sealed class WeeklyWinRunner(
    ISessionReader sessionReader,
    INarrativeStore narrativeStore,
    IWeeklyWinStore winStore,
    IChatClient chatClient,
    AiOptions options) : IWeeklyWinRunner
{
    public async Task<WeeklyWinResult> RunAsync(DateOnly monthFrom, DateOnly monthTo, bool force, CancellationToken ct = default)
    {
        var weeks = CalendarRange.WeeksWithin(monthFrom, monthTo);
        var outcomes = new List<WeekOutcome>(weeks.Count);

        if (!options.Enabled || !options.Jobs.Digest)
        {
            foreach (var (weekFrom, weekTo) in weeks)
            {
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null,
                    "AI features or digest job are disabled in configuration."));
            }

            return new WeeklyWinResult(outcomes);
        }

        var reachable = await chatClient.IsReachableAsync(ct).ConfigureAwait(false);
        if (!reachable)
        {
            foreach (var (weekFrom, weekTo) in weeks)
            {
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null, "AI provider is unreachable."));
            }

            return new WeeklyWinResult(outcomes);
        }

        foreach (var (weekFrom, weekTo) in weeks)
        {
            var weekFromUtc = new DateTimeOffset(weekFrom.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();
            var weekToUtc = new DateTimeOffset(weekTo.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

            var narratives = await narrativeStore.GetRangeAsync(weekFromUtc, weekToUtc, ct).ConfigureAwait(false);

            if (narratives.Count == 0)
            {
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null, "No narratives in this week yet."));
                continue;
            }

            var narrativeCount = narratives.Count;
            var maxGeneratedUtc = narratives.Max(n => n.GeneratedUtc);

            if (!force)
            {
                var existing = await winStore.GetAsync(weekFrom, weekTo, ct).ConfigureAwait(false);
                if (existing is not null && !existing.IsStale(narrativeCount, maxGeneratedUtc, options.Model))
                {
                    outcomes.Add(new WeekOutcome(weekFrom, weekTo, true, true, existing, null));
                    continue;
                }
            }

            var (metrics, _) = await DigestBuilder.BuildAsync(sessionReader, weekFrom, weekTo, ct).ConfigureAwait(false);
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
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null,
                    chatResult.Error ?? "AI provider returned no content."));
                continue;
            }

            DigestProseResult parseResult;
            try
            {
                parseResult = DigestProsePrompt.ValidateAndParse(chatResult.Content, figures);
            }
            catch (Exception ex)
            {
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null, $"Malformed response from model: {ex.Message}"));
                continue;
            }

            if (!parseResult.IsAccepted || parseResult.Prose is null)
            {
                outcomes.Add(new WeekOutcome(weekFrom, weekTo, false, false, null, parseResult.RejectionReason));
                continue;
            }

            var win = new WeeklyWin
            {
                WeekFrom = weekFrom,
                WeekTo = weekTo,
                Summary = parseResult.Prose.Summary,
                Highlights = parseResult.Prose.Highlights,
                NarrativeCount = narrativeCount,
                NarrativesMaxGeneratedUtc = maxGeneratedUtc,
                Model = options.Model,
                GeneratedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await winStore.UpsertAsync(win, ct).ConfigureAwait(false);
            outcomes.Add(new WeekOutcome(weekFrom, weekTo, true, false, win, null));
        }

        return new WeeklyWinResult(outcomes);
    }
}
