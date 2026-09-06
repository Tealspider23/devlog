using Dapper;
using Devlog.Core.Abstractions;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Infrastructure.Persistence;

/// <summary>
/// The verdict cache. SOURCE OF TRUTH — never rebuilt by derivation.
/// </summary>
public sealed class ClassificationRuleStore(ISqliteConnectionFactory factory) : IClassificationRuleStore
{
    public async Task<List<ClassificationRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<RuleRow>(new CommandDefinition(
            """
            SELECT id, scope, site, keyword, category, source, is_mixed,
                   hits, total_seconds, last_seen_utc, created_utc
            FROM classification_rule;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToDomain())];
    }

    /// <summary>
    /// Records that an identity was seen, without answering it. Pending rows are
    /// what <c>--unknowns</c> lists and what the LLM phase will consume.
    /// </summary>
    public async Task RecordSightingsAsync(
        IEnumerable<(string Site, int Hits, int Seconds)> sightings,
        long nowUtc,
        CancellationToken ct = default)
    {
        var grouped = sightings
            .Where(s => !string.IsNullOrWhiteSpace(s.Site))
            .GroupBy(s => s.Site, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Site = g.Key,
                Hits = g.Sum(x => x.Hits),
                Seconds = g.Sum(x => x.Seconds),
                Now = nowUtc
            })
            .ToList();

        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Unanswered rows are rebuilt from scratch on every derivation, exactly
        // like the derived tables. Without this, an identity that a new builtin
        // rule has since settled would sit on the unanswered list forever, and a
        // list full of already-answered things is a list nobody reads.
        //
        // Answered rows have a non-null category and are never touched.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM classification_rule WHERE category IS NULL;",
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        if (grouped.Count == 0)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        // Counters are recomputed rather than accumulated, because derivation
        // reprocesses the whole log every time. Adding would inflate them on
        // every re-derive.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO classification_rule
              (scope, site, keyword, category, source, is_mixed, hits, total_seconds, last_seen_utc, created_utc)
            VALUES
              ('Site', @Site, NULL, NULL, 'pending', 0, @Hits, @Seconds, @Now, @Now)
            ON CONFLICT (scope, site) WHERE keyword IS NULL DO UPDATE SET
              hits          = @Hits,
              total_seconds = @Seconds,
              last_seen_utc = @Now;
            """,
            grouped,
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers an identity.
    /// <para>
    /// If the site already has a <em>different</em> site-level answer, it is
    /// mixed-use: the old answer is demoted to a page-scope keyword rule, the
    /// site is flagged, and future verdicts for it are made per page. YouTube
    /// promotes itself the first time you disagree with your earlier self.
    /// </para>
    /// <para>
    /// A <c>manual</c> verdict may never be replaced by anything but another
    /// <c>manual</c> one, in either scope. This has to be checked before the
    /// promotion block runs, not only guarded on the final upsert: an earlier
    /// version guarded only the upsert, so an <c>llm</c> verdict disagreeing
    /// with a <c>manual</c> one left the stored category untouched but still
    /// ran the promotion path — setting <see cref="ClassificationRule.IsMixed"/>
    /// and demoting the manual answer to a page rule keyed on a keyword no real
    /// title will ever contain. <see cref="Classifier.Classify"/> skips a mixed
    /// site's own site-level rule, so the manual verdict silently stopped being
    /// applied even though the row itself still said the right thing. Guarding
    /// the read here — before any write — is what makes the whole call a no-op
    /// against a manual verdict, rather than a quieter form of overwriting it.
    /// </para>
    /// </summary>
    public async Task<bool> ClassifyAsync(
        string site,
        ActivityCategory category,
        string? keyword,
        string source,
        long nowUtc,
        CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var isSiteScope = string.IsNullOrWhiteSpace(keyword);

        var existing = await connection.QuerySingleOrDefaultAsync<ExistingVerdict>(new CommandDefinition(
            isSiteScope
                ? "SELECT category, source FROM classification_rule WHERE scope='Site' AND site=@site AND keyword IS NULL;"
                : "SELECT category, source FROM classification_rule WHERE scope='Page' AND site=@site AND keyword=@keyword;",
            new { site, keyword },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        var blockedByManual = existing is not null
            && string.Equals(existing.source, ClassificationSource.Manual, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source, ClassificationSource.Manual, StringComparison.OrdinalIgnoreCase);

        if (blockedByManual)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return false;
        }

        var promoted = false;

        if (isSiteScope)
        {
            var conflicts = !string.IsNullOrEmpty(existing?.category)
                && !string.Equals(existing.category, category.ToString(), StringComparison.OrdinalIgnoreCase);

            if (conflicts)
            {
                // Preserve the previous verdict as a page rule so it is not simply
                // lost - it was right about *something*, just not everything.
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO classification_rule
                      (scope, site, keyword, category, source, is_mixed, created_utc)
                    VALUES ('Page', @site, @keyword, @category, @source, 0, @now)
                    ON CONFLICT (scope, site, keyword) WHERE keyword IS NOT NULL
                    DO UPDATE SET category = excluded.category;
                    """,
                    new { site, keyword = $"__previous__{existing!.category}", category = existing.category, source, now = nowUtc },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE classification_rule SET is_mixed = 1 WHERE scope='Site' AND site=@site AND keyword IS NULL;",
                    new { site },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                promoted = true;
            }
        }

        var scope = isSiteScope ? "Site" : "Page";

        // The WHERE clause on each upsert states the same invariant already
        // enforced above, at the storage layer rather than only in this method
        // - worth having on a source-of-truth table a future caller might reach
        // by a path that does not go through the read-before-write check above.
        await connection.ExecuteAsync(new CommandDefinition(
            isSiteScope
                ? """
                  INSERT INTO classification_rule
                    (scope, site, keyword, category, source, is_mixed, created_utc)
                  VALUES ('Site', @site, NULL, @category, @source, 0, @now)
                  ON CONFLICT (scope, site) WHERE keyword IS NULL DO UPDATE SET
                    category = excluded.category,
                    source   = excluded.source
                  WHERE classification_rule.source <> 'manual' OR excluded.source = 'manual';
                  """
                : """
                  INSERT INTO classification_rule
                    (scope, site, keyword, category, source, is_mixed, created_utc)
                  VALUES ('Page', @site, @keyword, @category, @source, 0, @now)
                  ON CONFLICT (scope, site, keyword) WHERE keyword IS NOT NULL DO UPDATE SET
                    category = excluded.category,
                    source   = excluded.source
                  WHERE classification_rule.source <> 'manual' OR excluded.source = 'manual';
                  """,
            new { site, keyword, category = category.ToString(), source, now = nowUtc, scope },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return promoted;
    }

    /// <summary>What already answers this exact site or page, and who gave that answer.</summary>
    private sealed class ExistingVerdict
    {
        public string? category { get; set; }
        public string? source { get; set; }
    }

    private sealed class RuleRow
    {
        public long id { get; set; }
        public string scope { get; set; } = "Site";
        public string site { get; set; } = string.Empty;
        public string? keyword { get; set; }
        public string? category { get; set; }
        public string source { get; set; } = "manual";
        public long is_mixed { get; set; }
        public int hits { get; set; }
        public int total_seconds { get; set; }
        public long? last_seen_utc { get; set; }
        public long created_utc { get; set; }

        public ClassificationRule ToDomain() => new()
        {
            Id = id,
            Scope = Enum.TryParse<RuleScope>(scope, ignoreCase: true, out var s) ? s : RuleScope.Site,
            Site = site,
            Keyword = keyword,
            Category = ActivityCategoryExtensions.TryParse(category, out var c) ? c : null,
            SourceName = source,
            IsMixed = is_mixed != 0,
            Hits = hits,
            TotalSeconds = total_seconds,
            LastSeenUtc = last_seen_utc,
            CreatedUtc = created_utc
        };
    }
}
