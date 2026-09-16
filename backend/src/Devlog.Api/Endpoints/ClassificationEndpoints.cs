using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Devlog.Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class ClassificationEndpoints
{
    public static RouteGroupBuilder MapClassificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/unknowns", GetUnknowns);
        group.MapPost("/classify", PostClassify);
        group.MapPost("/classify-ai", PostClassifyAi);
        return group;
    }

    /// <summary>Same filter as <c>devlog unknowns</c> — neither the CLI nor the API is the one that decides what counts as pending.</summary>
    private static async Task<IResult> GetUnknowns(IClassificationRuleStore rules, CancellationToken ct)
    {
        var all = await rules.GetAllAsync(ct);

        var pending = all
            .Where(r => r.IsPending && r.Scope == RuleScope.Site)
            .Where(r => !SyntheticData.IsSynthetic(r.Site) && !PrivacyMarker.IsExcluded(r.Site))
            .OrderByDescending(r => r.TotalSeconds)
            .Select(r => new PendingIdentityDto(r.Site, r.Hits, r.TotalSeconds));

        return Results.Ok(pending);
    }

    private static async Task<IResult> PostClassify(
        ClassifyRequest request, IClassificationRuleStore rules, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Identity))
        {
            return Results.BadRequest(new { error = "identity is required" });
        }

        if (!ActivityCategoryExtensions.TryParse(request.Category, out var category))
        {
            return Results.BadRequest(new
            {
                error = $"unknown category '{request.Category}'",
                valid = Enum.GetNames<ActivityCategory>()
            });
        }

        // source is always "manual" here — this is the human override path. An
        // llm verdict is written by the classifier job directly, never through
        // this endpoint, so precedence can never be spoofed by whoever calls it.
        var promoted = await rules.ClassifyAsync(
            request.Identity,
            category,
            request.Keyword,
            source: "manual",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ct);

        return Results.Ok(new ClassifyResponse(request.Identity, category.ToString(), promoted));
    }

    /// <summary>
    /// The LLM-verdict path — called from Today's Refresh chain, folded in
    /// rather than a standalone button (see WeeklyWinRunner/NarrateButton for
    /// the contrasting case: a standalone, unbounded action needs its own
    /// preflight; this one rides an already-deliberate Refresh click).
    /// <c>DryRun</c> defaults to <c>false</c> — Refresh wants live writes, and
    /// a dry-run default would make every click silently do nothing.
    /// </summary>
    private static async Task<IResult> PostClassifyAi(ClassifyAiRequestDto request, IClassifyAiRunner runner, CancellationToken ct)
    {
        var result = await runner.RunAsync(request.DryRun ?? false, request.Limit, request.Force ?? false, ct);
        return Results.Ok(ClassifyAiResultDto.From(result));
    }
}
