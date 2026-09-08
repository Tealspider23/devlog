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
    /// <para>
    /// <c>range=week|month</c> resolves through the same <see cref="CalendarRange"/>
    /// the CLI's <c>--week</c>/<c>--month</c> use, so the Week/Month pages and
    /// <c>devlog digest --week</c> can never mean a different span for the same
    /// word. Explicit <c>from</c>/<c>to</c> still override, for prev/next navigation.
    /// </para>
    /// <para>
    /// <c>prose=true</c> mirrors <c>devlog digest --prose</c>, splicing Job C's
    /// summary in with the exact same <see cref="DigestProseSplice"/> the CLI
    /// uses. Without it the response is byte-identical to before this existed.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetDigest(
        string? from, string? to, string? range, bool? prose,
        ISessionReader reader, IDigestProseRunner proseRunner, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var (defaultFrom, defaultTo) = range?.ToLowerInvariant() switch
        {
            "month" => CalendarRange.For(DigestRangeKind.Month, today),
            "week" => CalendarRange.For(DigestRangeKind.Week, today),
            _ => (today.AddDays(-6), today)
        };

        var fromDate = DateOnly.TryParse(from, out var f) ? f : defaultFrom;
        var toDate = DateOnly.TryParse(to, out var t) ? t : defaultTo;

        if (fromDate > toDate)
        {
            return Results.BadRequest(new { error = "from must not be after to" });
        }

        var (metrics, markdown) = await DigestBuilder.BuildAsync(reader, fromDate, toDate, ct);

        if (prose == true)
        {
            var fromUtc = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();
            var toUtc = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

            var (proseMarkdown, note) = await proseRunner.GenerateProseAsync(metrics, fromUtc, toUtc, ct);
            markdown = DigestProseSplice.Apply(markdown, metrics, proseMarkdown, note);
        }

        return Results.Ok(DigestDto.FromMetrics(metrics, markdown));
    }
}
