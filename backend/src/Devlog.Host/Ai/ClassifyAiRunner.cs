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
/// </summary>
public sealed class ClassifyAiRunner(
    IClassificationRuleStore ruleStore,
    IChatClient chatClient,
    AiOptions options)
{
    public async Task<int> RunAsync(bool dryRun, int? limitOverride, CancellationToken ct = default)
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
            Console.WriteLine("\nNothing pending — every identity seen so far has a verdict.\n");
            return 0;
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
            Console.WriteLine($"\nclassifier unreachable, {allPending.Count} identities still pending: {chatResult.Error ?? "no response"}\n");
            return 0;
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
            Console.WriteLine($"\nMalformed JSON from classifier ({ex.Message}), {allPending.Count} identities still pending.\n");
            return 0;
        }

        var mode = dryRun ? "PROPOSED VERDICTS (--dry-run, no database writes)" : "CLASSIFIED VERDICTS";
        Console.WriteLine($"\n=== {mode} ===\n");

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

            Console.WriteLine($"  {v.Identity,-30} => {v.Category,-15} (confidence: {v.Confidence:F2}) — {v.Reason}");
        }

        if (discards.Count > 0)
        {
            Console.WriteLine();
            foreach (var d in discards)
            {
                Console.WriteLine($"  [skipped] {d}");
            }
        }

        var totalRemaining = allPending.Count - (dryRun ? 0 : verdicts.Count);
        Console.WriteLine($"""

              {verdicts.Count} processed, {discards.Count} skipped/pending, {totalRemaining} total pending remaining.
            """);

        if (!dryRun && verdicts.Count > 0)
        {
            Console.WriteLine("  Run `devlog derive` to apply newly classified identities to existing sessions.\n");
        }
        else
        {
            Console.WriteLine();
        }

        return 0;
    }
}
