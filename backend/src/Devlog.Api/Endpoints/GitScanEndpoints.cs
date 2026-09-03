using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class GitScanEndpoints
{
    public static RouteGroupBuilder MapGitScanEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/scan-git", PostScanGit);
        return group;
    }

    /// <summary>
    /// Walks every configured repo on disk — the slow half of git enrichment,
    /// unlike /derive. Meant to be triggered explicitly (the dashboard's
    /// Refresh button), never on every page load.
    /// </summary>
    private static async Task<IResult> PostScanGit(IGitScanRunner runner, CancellationToken ct)
    {
        var r = await runner.RunAsync(ct);
        return Results.Ok(new GitScanResultDto(r.Scanned, r.Skipped, r.ReposFailed));
    }
}
