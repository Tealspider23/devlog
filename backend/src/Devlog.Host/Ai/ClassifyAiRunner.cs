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
    IChatClient chatClient,
    AiOptions options) : IClassifyAiRunner
{
    public async Task<ClassifyAiResult> RunAsync(bool dryRun, int? limitOverride, CancellationToken ct = default)
    {
        var rules = await ruleStore.GetAllAsync(ct).ConfigureAwait(false);

        // Neither marker is awaiting a verdict: [seed] identities describe fixtures,
        // and [excluded] is the privacy rule working as designed.
        var allPending = rules
            .Where(r => r.IsPending && r.Scope == RuleScope.Site)
            .Where(r => !SyntheticData.IsSynthetic(r.Site) && !PrivacyMarker.IsExcluded(r.Site))
            .OrderByDescending(r => r.TotalSeconds)
            .ToList();

        if (allPending.Count == 0)
        {
            return new ClassifyAiResult(dryRun, true, 0, 0, 0, [], [], null);
        }

        var limit = limitOverride ?? options.ClassifyBatchSize;
        var batch = allPending.Take(limit).ToList();

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
            ct).ConfigureAwait(false);

        if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
        {
            var reason = $"classifier unreachable, {allPending.Count} identities still pending: {chatResult.Error ?? "no response"}";
            return new ClassifyAiResult(dryRun, false, 0, 0, allPending.Count, [], [], reason);
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
            var reason = $"Malformed JSON from classifier ({ex.Message}), {allPending.Count} identities still pending.";
            return new ClassifyAiResult(dryRun, true, 0, 0, allPending.Count, [], [], reason);
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

        var totalRemaining = allPending.Count - (dryRun ? 0 : verdicts.Count);
        return new ClassifyAiResult(dryRun, true, verdicts.Count, discards.Count, totalRemaining, outcomes, discards, null);
    }
}
