using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;
using Devlog.Infrastructure.Persistence;

namespace Devlog.Host.Derivation;

/// <summary>
/// Rebuilds the entire derived half of the database from <c>raw_event</c>.
/// <para>
/// Deliberately whole-log rather than incremental. Reprocessing everything is
/// what makes re-derivation genuinely idempotent — and at a few hundred rows a
/// day, an incremental path would buy nothing but state to get wrong.
/// </para>
/// <para>
/// Shared by the <c>--derive</c> command now and by <c>POST /v1/derive</c> in
/// Phase 4; the CLI is not throwaway scaffolding.
/// </para>
/// </summary>
public sealed class DerivationRunner(
    IEventStore events,
    ActivityStore activityStore,
    SessionStore sessionStore,
    OverrideStore overrideStore,
    ClassificationRuleStore ruleStore,
    ICommitStore commitStore,
    DerivationOptions options,
    GitOptions gitOptions,
    ILogger<DerivationRunner> logger) : IDerivationRunner
{
    public async Task<DerivationResult> RunAsync(CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;

        var raw = await events.GetRangeAsync(ct: ct).ConfigureAwait(false);
        var rules = await ruleStore.GetAllAsync(ct).ConfigureAwait(false);
        var overrides = await overrideStore.GetAllAsync(ct).ConfigureAwait(false);

        var noise = new NoiseFilter(options.NoiseProcesses, options.NoiseTitles);
        var classifier = new Classifier(rules, options.ResolveCategoryOverrides());

        var afterNoise = noise.Apply(raw).Count;

        // The same resolver the git scanner uses, so a repo is named identically
        // whether it arrived as attention (a window title) or as output (a
        // commit). Without it the two axes can disagree about what a project is
        // called, and the digest would put the same work under two names.
        var projects = new ProjectResolver(gitOptions.Repos);

        var built = new ActivityBuilder(options, classifier, noise, projects).Build(raw);

        // Sessions come back with ids assigned and the activities stamped with
        // them, so the two tables are linked without a database round-trip.
        var (sessions, activities) = new SessionBuilder(options).Build(built.Activities, overrides);

        // Sessions first: activity.session_id has a foreign key to session.id, so
        // writing activities first would reference rows that do not exist yet.
        await sessionStore.ReplaceAllAsync(sessions, ct).ConfigureAwait(false);
        await activityStore.ReplaceAllAsync(activities, ct).ConfigureAwait(false);

        // Only genuinely unanswered identities are recorded. Anything a builtin
        // rule already settled — Code, WindowsTerminal, ms-teams — must never
        // appear on the unanswered list, or the list is noise and gets ignored.
        await ruleStore.RecordSightingsAsync(
            built.PendingIdentities.Select(kv => (kv.Key, kv.Value.Hits, kv.Value.Seconds)),
            started.ToUnixTimeMilliseconds(),
            ct).ConfigureAwait(false);

        // Re-linking is the cheap half of enrichment: session ids just changed
        // under every commit (ReplaceAllAsync wiped and reassigned them), but
        // this never re-reads a repo or recomputes a diff. commit_record.session_id
        // has ON DELETE SET NULL, so the wipe above already nulled every link —
        // this rebuilds them against the fresh sessions.
        var commits = await commitStore.GetAllAsync(ct).ConfigureAwait(false);
        var linked = new CommitLinker(gitOptions).Link(commits, sessions);
        await commitStore.RelinkAsync(linked, ct).ConfigureAwait(false);

        var result = new DerivationResult(
            raw.Count,
            afterNoise,
            activities.Count,
            sessions.Count,
            built.PendingIdentities.Count,
            built.PendingIdentities.Values.Sum(v => v.Seconds),
            linked.Values.Count(v => v is not null),
            linked.Values.Count(v => v is null),
            DateTimeOffset.UtcNow - started);

        logger.LogInformation(
            "Derived {Activities} activities and {Sessions} sessions from {Raw} events in {Ms}ms",
            result.Activities, result.Sessions, result.RawEvents, (int)result.Elapsed.TotalMilliseconds);

        return result;
    }
}
