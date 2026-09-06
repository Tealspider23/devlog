using System.Text.Json;
using System.Text.Json.Serialization;
using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Host.Ai;

/// <summary>
/// Exports candidate identity and session fixtures for hand-labelling.
/// Real sessions and identities are exported with blank expected fields.
/// </summary>
public sealed class LlmFixturesRunner(
    IClassificationRuleStore ruleStore,
    ISessionReader sessionReader)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<int> RunAsync(string? outDir, CancellationToken ct = default)
    {
        var targetDir = Path.GetFullPath(outDir ?? "docs/llm-evals");
        Directory.CreateDirectory(targetDir);

        var identitiesPath = Path.Combine(targetDir, "identities.json");
        var sessionsPath = Path.Combine(targetDir, "sessions.json");

        // 1. Export identity candidates
        var rules = await ruleStore.GetAllAsync(ct).ConfigureAwait(false);
        var eligibleRules = rules
            .Where(r => r.Scope == RuleScope.Site)
            .Where(r => !SyntheticData.IsSynthetic(r.Site) && !PrivacyMarker.IsExcluded(r.Site))
            .OrderByDescending(r => r.TotalSeconds)
            .Take(30)
            .ToList();

        var identityFixtures = new List<IdentityEvalFixture>(eligibleRules.Count);
        foreach (var r in eligibleRules)
        {
            var titles = await ruleStore.GetSampleTitlesAsync(r.Site, 3, ct).ConfigureAwait(false);
            identityFixtures.Add(new IdentityEvalFixture(
                Identity: r.Site,
                Process: null,
                SampleTitles: titles,
                Expected: "",
                Note: ""
            ));
        }

        var identitiesJson = JsonSerializer.Serialize(identityFixtures, JsonOptions);
        await File.WriteAllTextAsync(identitiesPath, identitiesJson, ct).ConfigureAwait(false);

        // 2. Export session candidates
        var recentSummaries = await sessionReader.GetRecentAsync(50, ct).ConfigureAwait(false);
        var eligibleSessions = recentSummaries
            .Where(s => !SyntheticData.IsSynthetic(s.Session.Project ?? "") && !SyntheticData.IsSynthetic(s.Session.ActivityKey))
            .Take(20)
            .ToList();

        var sessionFixtures = new List<SessionEvalFixture>(eligibleSessions.Count);
        foreach (var s in eligibleSessions)
        {
            sessionFixtures.Add(new SessionEvalFixture(
                StartUtc: s.Session.StartUtc,
                SessionId: s.Session.Id,
                ExpectedKind: "",
                ExpectedWorkstream: null,
                Note: "",
                Project: s.Session.Project,
                DurationSeconds: s.Session.DurationSeconds
            ));
        }

        var sessionsJson = JsonSerializer.Serialize(sessionFixtures, JsonOptions);
        await File.WriteAllTextAsync(sessionsPath, sessionsJson, ct).ConfigureAwait(false);

        Console.WriteLine($"""

            === LLM EVAL FIXTURES EXPORTED ===
              identities : {identitiesPath} ({identityFixtures.Count} candidates)
              sessions   : {sessionsPath} ({sessionFixtures.Count} candidates)

            These files contain real window titles and are gitignored (*.json in docs/llm-evals/).
            Fill in the blank 'expected' (Job A) and 'expectedKind' (Job B) fields by hand,
            then run:
              devlog llm-eval
            """);

        return 0;
    }
}
