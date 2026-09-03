using Devlog.Api.Endpoints;
using Devlog.Api.Security;
using Devlog.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Devlog.Api;

/// <summary>
/// Wires devlog's own HTTP surface into the host's DI container and endpoint
/// routing. Not a general-purpose web API pattern — this exists so
/// <c>Devlog.Host</c> has exactly two calls to make (<see cref="AddDevlogApi"/>
/// at builder time, <see cref="MapDevlogApi"/> after <c>Build()</c>) and never
/// has to know what routes exist.
/// </summary>
public static class ApiExtensions
{
    public const string DevCorsPolicy = "devlog-dev";

    public static IServiceCollection AddDevlogApi(this IServiceCollection services, ApiOptions api)
    {
        services.AddSingleton<ApiTokenStore>();

        if (!string.IsNullOrWhiteSpace(api.DevCorsOrigin))
        {
            services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
                .WithOrigins(api.DevCorsOrigin)
                .AllowAnyHeader()
                .AllowAnyMethod()));
        }

        return services;
    }

    /// <summary>
    /// Maps every route. Callers decide separately whether to invoke this at
    /// all — see the note on <see cref="ApiOptions.Enabled"/> in
    /// <c>Devlog.Host/Program.cs</c> for why "off" is handled by the caller
    /// rather than in here.
    /// </summary>
    public static WebApplication MapDevlogApi(this WebApplication app, ApiOptions api)
    {
        if (!string.IsNullOrWhiteSpace(api.DevCorsOrigin))
        {
            app.UseCors(DevCorsPolicy);
        }

        // Unauthenticated and outside the /v1 group on purpose: the desktop
        // shell and a monitoring probe both need to tell "collector not
        // running" apart from "wrong token" without holding a secret.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        var v1 = app.MapGroup("/v1").AddEndpointFilter<TokenAuthFilter>();

        v1.MapTimelineEndpoints();
        v1.MapSessionEndpoints();
        v1.MapClassificationEndpoints();
        v1.MapDeriveEndpoints();
        v1.MapGitScanEndpoints();

        return app;
    }
}
