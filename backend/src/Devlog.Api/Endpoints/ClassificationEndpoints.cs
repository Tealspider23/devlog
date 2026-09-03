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
}
