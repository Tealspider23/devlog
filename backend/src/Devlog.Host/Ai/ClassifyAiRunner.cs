using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Host.Ai;

/// <summary>
/// Job A: Identity classification runner.
/// Selects top pending identities by duration, pulls representative sample titles,
/// requests categorization from the LLM, and writes accepted verdicts to the rule store.
/// Does no I/O of its own beyond the rule store and the model — see
/// <c>DiagnosticCommands.ClassifyAi</c> for the console renderer, and
/// <c>POST /v1/classify-ai</c> for the JSON one. Same "one result, two
/// renderers" split as <see cref="NarrateRunner"/>.
/// </summary>
public sealed class ClassifyAiRunner(
    IClassificationRuleStore ruleStore,
    IClassifyAttemptStore attemptStore,
    IChatClient chatClient,
    AiOptions options) : IClassifyAiRunner
{
    public async Task<ClassifyAiResult> RunAsync(bool dryRun, int? limitOverride, bool force = false, CancellationToken ct = default)
    {
        var rules = await ruleStore.GetAllAsync(ct).ConfigureAwait(false);

        // Neither marker is awaiting a verdict: [seed] identities describe fixtures,
        // and [excluded] is the privacy rule working as designed.
        var allPending = rules
            .Where(r => r.IsPending && r.Scope == RuleScope.Site)
            .Where(r => !SyntheticData.IsSynthetic(r.Site) && !PrivacyMarker.IsExcluded(r.Site))
            .OrderByDescending(r => r.TotalSeconds)
            .ToList();

        var trulyPendingCount = allPending.Count;
        if (trulyPendingCount == 0)
        {
            return new ClassifyAiResult(dryRun, true, 0, 0, 0, [], [], null);
        }

        // classify-ai now runs on every dashboard Refresh (Phase 13.6). Without
        // this, an identity the model already declined to answer confidently
        // gets re-sent on every single click — this is where a 20/day request
        // budget disappeared on 2026-09-15 without a single narrative written.
        // trulyPendingCount is kept separate from the filtered list below so
        // reporting still says how many identities genuinely await a verdict,
        // not just how many this run is willing to ask about again.
        var eligible = allPending;
        if (!force)
        {
            var recentlyAttempted = await attemptStore.GetRecentlyAttemptedAsync(options.ClassifyRetryAfterDays, ct).ConfigureAwait(false);
            if (recentlyAttempted.Count > 0)
            {
                eligible = allPending.Where(r => !recentlyAttempted.Contains(r.Site)).ToList();
            }
        }

        if (eligible.Count == 0)
        {
            return new ClassifyAiResult(dryRun, true, 0, 0, trulyPendingCount, [], [], null);
        }

        var limit = limitOverride ?? options.ClassifyBatchSize;
        var batch = eligible.Take(limit).ToList();

        var inputs = new List<IdentityInput>(batch.Count);
        foreach (var r in batch)
        {
            var titles = await ruleStore.GetSampleTitlesAsync(r.Site, 3, ct).ConfigureAwait(false);
            inputs.Add(new IdentityInput(r.Site, null, r.TotalSeconds, r.Hits, titles));
        }

        var userContent = IdentityClassifierPrompt.BuildUserContent(inputs);
        var chatResult = await chatClient.CompleteAsync(
            IdentityClassifierPrompt.SystemPrompt,
            userContent,
            IdentityClassifierPrompt.SchemaName,
            IdentityClassifierPrompt.JsonSchema,
            reasoningEffort: "low",
            ct,
            job: "classify").ConfigureAwait(false);

        if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
        {
            var reason = $"classifier unreachable, {trulyPendingCount} identities still pending: {chatResult.Error ?? "no response"}";
            return new ClassifyAiResult(dryRun, false, 0, 0, trulyPendingCount, [], [], reason);
        }

        List<ValidatedVerdict> verdicts;
        List<string> discards;
        try
        {
            verdicts = IdentityClassifierPrompt.ParseVerdicts(
                chatResult.Content,
                inputs,
                options.MinConfidence,
                out discards);
        }
        catch (Exception ex)
        {
            var reason = $"Malformed JSON from classifier ({ex.Message}), {trulyPendingCount} identities still pending.";
            return new ClassifyAiResult(dryRun, true, 0, 0, trulyPendingCount, [], [], reason);
        }

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var outcomes = new List<ClassifyAiVerdictOutcome>(verdicts.Count);
        foreach (var v in verdicts)
        {
            if (!dryRun)
            {
                await ruleStore.ClassifyAsync(
                    v.Identity,
                    v.Category,
                    keyword: null,
                    source: ClassificationSource.Llm,
                    nowUtc: nowUtc,
                    ct).ConfigureAwait(false);
            }

            outcomes.Add(new ClassifyAiVerdictOutcome(v.Identity, v.Category, v.Confidence, v.Reason));
        }

        // Only real discards get an attempt recorded — computed as "sent but
        // not accepted" rather than by parsing the free-form discard reason
        // strings. An unreachable/malformed run above never reaches here at
        // all, so there is nothing to remember as "declined" for those.
        // Skipped in dry runs: a preview shouldn't change what the next real
        // run considers eligible.
        if (!dryRun)
        {
            var acceptedIdentities = new HashSet<string>(verdicts.Select(v => v.Identity), StringComparer.OrdinalIgnoreCase);
            foreach (var input in inputs)
            {
                if (!acceptedIdentities.Contains(input.Identity))
                {
                    await attemptStore.RecordAttemptAsync(input.Identity, nowUtc, reason: null, ct).ConfigureAwait(false);
                }
            }
        }

        var totalRemaining = trulyPendingCount - (dryRun ? 0 : verdicts.Count);
        return new ClassifyAiResult(dryRun, true, verdicts.Count, discards.Count, totalRemaining, outcomes, discards, null);
    }
}
