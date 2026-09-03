using Devlog.Api.Contracts;
using Devlog.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class DeriveEndpoints
{
    public static RouteGroupBuilder MapDeriveEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/derive", PostDerive);
        return group;
    }

    private static async Task<IResult> PostDerive(IDerivationRunner runner, CancellationToken ct)
    {
        var r = await runner.RunAsync(ct);

        return Results.Ok(new DeriveResultDto(
            r.RawEvents,
            r.AfterNoise,
            r.Activities,
            r.Sessions,
            r.PendingIdentities,
            r.UnclassifiedSeconds,
            r.CommitsLinked,
            r.CommitsUnattached,
            r.Elapsed.TotalMilliseconds));
    }
}
