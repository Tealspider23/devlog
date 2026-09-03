using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class TimelineEndpoints
{
    public static RouteGroupBuilder MapTimelineEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/timeline", GetTimeline);
        return group;
    }

    /// <summary>
    /// One day, by local calendar date. <c>date</c> is interpreted in the
    /// server's local timezone and converted to the UTC window the reader
    /// expects — the server and the browser are the same machine, so there is no
    /// timezone to reconcile between them.
    /// </summary>
    private static async Task<IResult> GetTimeline(
        string? date, ISessionReader reader, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var day))
        {
            return Results.BadRequest(new { error = "date must be YYYY-MM-DD" });
        }

        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));
        var (fromUtc, toUtc) = (start.ToUnixTimeMilliseconds(), start.AddDays(1).ToUnixTimeMilliseconds());

        var sessions = await reader.GetRangeAsync(fromUtc, toUtc, ct);
        var commits = await reader.GetCommitsAsync(fromUtc, toUtc, ct);
        var unclassified = await reader.GetUnclassifiedSecondsAsync(fromUtc, toUtc, ct);

        return Results.Ok(new TimelineDto(
            day.ToString("O"),
            [.. sessions.Select(SessionDto.From)],
            [.. commits.Select(CommitDto.From)],
            unclassified));
    }
}
