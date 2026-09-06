using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Devlog.Host.Tests;

/// <summary>
/// Regression coverage for the precedence guarantee: a manual verdict may never
/// be replaced by anything but another manual one. Against a real SQLite file
/// rather than a stub — the bug this guards was in the SQL itself, and in the
/// order two separate statements ran in, neither of which a fake would exercise.
/// </summary>
public sealed class ClassificationRuleStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ClassificationRuleStore _store;

    public ClassificationRuleStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devlog-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(new DevlogOptions { DatabasePath = _dbPath });

        new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance).Run();

        _store = new ClassificationRuleStore(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    private async Task<ClassificationRule> RuleFor(string site) =>
        (await _store.GetAllAsync()).Single(r => r.Site == site && r.Keyword is null);

    // ------------------------------------------------------- the bug itself

    /// <summary>
    /// The actual defect. An earlier version guarded only the final upsert, so
    /// this sequence left the stored category untouched but still ran the
    /// mixed-use promotion path — setting IsMixed and demoting the manual
    /// verdict to a page rule keyed on a keyword no real title contains.
    /// Classifier.Classify skips a mixed site's own site-level rule, so the
    /// manual verdict silently stopped being applied even though this
    /// assertion on the raw row would have kept passing.
    /// </summary>
    [Fact]
    public async Task LlmVerdict_DoesNotPromoteASiteToMixedUse_WhenTheExistingAnswerIsManual()
    {
        await _store.ClassifyAsync("Dashboard", ActivityCategory.Other, null,
            ClassificationSource.Manual, nowUtc: 1000);

        await _store.ClassifyAsync("Dashboard", ActivityCategory.Distraction, null,
            ClassificationSource.Llm, nowUtc: 2000);

        var rule = await RuleFor("Dashboard");

        Assert.False(rule.IsMixed);
        Assert.Equal(ActivityCategory.Other, rule.Category);
        Assert.Equal(ClassificationSource.Manual, rule.SourceName);
    }

    /// <summary>
    /// The behavioural check, not just the stored row: an identity the classifier
    /// still resolves correctly is the actual guarantee, since a bug in the
    /// promotion path affects resolution (IsMixed) without ever touching the
    /// category column the previous test already asserts on its own.
    /// </summary>
    [Fact]
    public async Task ManualVerdict_StillResolvesCorrectly_AfterAConflictingLlmAttempt()
    {
        // Deliberately not ActivityCategory.Other for the manual verdict: a
        // pending fall-through also defaults to Other, so that choice would let
        // this assertion pass by coincidence even if the manual verdict had
        // stopped being applied — exactly the false negative this test exists
        // to avoid. Learning cannot collide with a fallback default.
        await _store.ClassifyAsync("Dashboard", ActivityCategory.Learning, null,
            ClassificationSource.Manual, nowUtc: 1000);

        await _store.ClassifyAsync("Dashboard", ActivityCategory.Distraction, null,
            ClassificationSource.Llm, nowUtc: 2000);

        var rules = await _store.GetAllAsync();
        var classifier = new Classifier(rules);

        var result = classifier.Classify(
            processName: "chrome", windowTitle: "Dashboard", siteIdentity: "Dashboard", defaultFromContext: null);

        Assert.Equal(ActivityCategory.Learning, result.Category);
        Assert.False(result.IsPending);
    }

    [Fact]
    public async Task LlmVerdict_DoesNotOverwriteTheStoredCategoryOrSource()
    {
        await _store.ClassifyAsync("Dashboard", ActivityCategory.Other, null,
            ClassificationSource.Manual, nowUtc: 1000);

        var promoted = await _store.ClassifyAsync("Dashboard", ActivityCategory.Distraction, null,
            ClassificationSource.Llm, nowUtc: 2000);

        Assert.False(promoted);

        var rule = await RuleFor("Dashboard");
        Assert.Equal(ActivityCategory.Other, rule.Category);
        Assert.Equal(ClassificationSource.Manual, rule.SourceName);
    }

    /// <summary>Page-scope rules have no promotion concept at all — only the upsert — but need the same guard.</summary>
    [Fact]
    public async Task LlmPageRule_DoesNotOverwriteAManualPageRule()
    {
        await _store.ClassifyAsync("YouTube", ActivityCategory.Learning, "tutorial",
            ClassificationSource.Manual, nowUtc: 1000);

        await _store.ClassifyAsync("YouTube", ActivityCategory.Distraction, "tutorial",
            ClassificationSource.Llm, nowUtc: 2000);

        var rule = (await _store.GetAllAsync()).Single(r => r.Site == "YouTube" && r.Keyword == "tutorial");
        Assert.Equal(ActivityCategory.Learning, rule.Category);
        Assert.Equal(ClassificationSource.Manual, rule.SourceName);
    }

    // ---------------------------------------------------- what must still work

    [Fact]
    public async Task ManualVerdict_CanStillOverwriteAnEarlierManualVerdict()
    {
        await _store.ClassifyAsync("Dashboard", ActivityCategory.Other, null,
            ClassificationSource.Manual, nowUtc: 1000);

        await _store.ClassifyAsync("Dashboard", ActivityCategory.Distraction, null,
            ClassificationSource.Manual, nowUtc: 2000);

        var rule = await RuleFor("Dashboard");
        Assert.Equal(ActivityCategory.Distraction, rule.Category);
    }

    /// <summary>Mixed-use promotion is for a human disagreeing with their own earlier answer — that must still work.</summary>
    [Fact]
    public async Task ManualVerdict_StillPromotesToMixed_WhenItConflictsWithAnEarlierManualVerdict()
    {
        await _store.ClassifyAsync("YouTube", ActivityCategory.Learning, null,
            ClassificationSource.Manual, nowUtc: 1000);

        var promoted = await _store.ClassifyAsync("YouTube", ActivityCategory.Distraction, null,
            ClassificationSource.Manual, nowUtc: 2000);

        Assert.True(promoted);

        var rule = await RuleFor("YouTube");
        Assert.True(rule.IsMixed);
        Assert.Equal(ActivityCategory.Distraction, rule.Category);
    }

    [Fact]
    public async Task LlmVerdict_WritesNormally_OverAPendingIdentity()
    {
        var promoted = await _store.ClassifyAsync("Dashboard", ActivityCategory.Other, null,
            ClassificationSource.Llm, nowUtc: 1000);

        Assert.False(promoted);

        var rule = await RuleFor("Dashboard");
        Assert.Equal(ActivityCategory.Other, rule.Category);
        Assert.Equal(ClassificationSource.Llm, rule.SourceName);
    }

    [Fact]
    public async Task LlmVerdict_CanOverwriteAnEarlierBuiltinVerdict()
    {
        await _store.ClassifyAsync("Dashboard", ActivityCategory.Other, null,
            ClassificationSource.Builtin, nowUtc: 1000);

        await _store.ClassifyAsync("Dashboard", ActivityCategory.Distraction, null,
            ClassificationSource.Llm, nowUtc: 2000);

        var rule = await RuleFor("Dashboard");
        Assert.Equal(ActivityCategory.Distraction, rule.Category);
        Assert.Equal(ClassificationSource.Llm, rule.SourceName);
    }
}
