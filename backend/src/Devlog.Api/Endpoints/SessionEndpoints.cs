using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/sessions/{id:long}", GetSession);
        return group;
    }

    private static async Task<IResult> GetSession(long id, ISessionReader reader, CancellationToken ct)
    {
        var summary = await reader.GetByIdAsync(id, ct);

        if (summary is null)
        {
            return Results.NotFound();
        }

        var activities = await reader.GetActivitiesAsync(id, ct);
        var commits = await reader.GetCommitsForSessionAsync(id, ct);

        return Results.Ok(new SessionDetailDto(
            SessionDto.From(summary),
            [.. activities.Select(ActivityDto.From)],
            [.. commits.Select(CommitDto.From)]));
    }
}
