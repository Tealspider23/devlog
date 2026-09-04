using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Devlog.Core.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class DigestEndpoints
{
    public static RouteGroupBuilder MapDigestEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/digest", GetDigest);
        return group;
    }

    /// <summary>
    /// Calls the exact same <see cref="DigestBuilder"/> as <c>devlog digest</c>
    /// — see the comment there. Dates are local calendar dates, same convention
    /// as <c>GET /v1/timeline?date=</c>.
    /// </summary>
    private static async Task<IResult> GetDigest(
        string? from, string? to, ISessionReader reader, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var fromDate = DateOnly.TryParse(from, out var f) ? f : today.AddDays(-6);
        var toDate = DateOnly.TryParse(to, out var t) ? t : today;

        if (fromDate > toDate)
        {
            return Results.BadRequest(new { error = "from must not be after to" });
        }

        var (metrics, markdown) = await DigestBuilder.BuildAsync(reader, fromDate, toDate, ct);

        return Results.Ok(DigestDto.FromMetrics(metrics, markdown));
    }
}
