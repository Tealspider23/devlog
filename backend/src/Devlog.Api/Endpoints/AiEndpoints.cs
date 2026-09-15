using Devlog.Api.Contracts;
using Devlog.Api.Security;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Devlog.Api.Endpoints;

public static class AiEndpoints
{
    public static RouteGroupBuilder MapAiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/ai/status", GetStatus);
        group.MapPost("/ai/key", PostSetApiKey);
        group.MapGet("/ai/models", GetModels);
        group.MapPost("/ask", PostAsk);
        group.MapGet("/narratives", GetNarratives);
        group.MapPost("/narrate", PostNarrate);
        group.MapGet("/weekly-wins", GetWeeklyWins);
        group.MapPost("/weekly-wins", PostWeeklyWins);
        return group;
    }

    /// <summary>
    /// The <c>devlog llm</c> screen as data. Same rule as the CLI: the API key
    /// is reported present/absent, never its value.
    /// </summary>
    private static async Task<IResult> GetStatus(AiOptions ai, IChatClient chatClient, CancellationToken ct)
    {
        var apiKeyPresent = !string.IsNullOrWhiteSpace(ai.ApiKey);

        if (!ai.Enabled)
        {
            return Results.Ok(new AiStatusDto(
                Enabled: false,
                ConfiguredModel: ai.Model,
                ApiKeyPresent: apiKeyPresent,
                JobClassifyEnabled: ai.Jobs.Classify,
                JobNarrateEnabled: ai.Jobs.Narrate,
                JobDigestEnabled: ai.Jobs.Digest,
                JobAskEnabled: ai.Jobs.Ask,
                Reachable: false,
                Endpoint: null,
                OffMachine: null,
                Host: null));
        }

        var endpoint = await chatClient.ResolveEndpointAsync(ct);

        // Same "where does my data go" reasoning as `devlog llm`: a host that
        // is not this machine is named, never left implicit.
        bool? offMachine = null;
        string? host = null;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            offMachine = !uri.IsLoopback;
            host = uri.Host;
        }

        return Results.Ok(new AiStatusDto(
            Enabled: true,
            ConfiguredModel: ai.Model,
            ApiKeyPresent: apiKeyPresent,
            JobClassifyEnabled: ai.Jobs.Classify,
            JobNarrateEnabled: ai.Jobs.Narrate,
            JobDigestEnabled: ai.Jobs.Digest,
            JobAskEnabled: ai.Jobs.Ask,
            Reachable: endpoint is not null,
            Endpoint: endpoint,
            OffMachine: offMachine,
            Host: host));
    }

    /// <summary>
    /// Overrides <c>appsettings.local.json</c>'s key, live, no restart — see
    /// <see cref="AiKeyStore"/>. A blank key clears the stored one.
    /// </summary>
    private static IResult PostSetApiKey(SetAiApiKeyRequestDto request, AiKeyStore store)
    {
        store.Save(request.ApiKey);
        return Results.Ok(new AiApiKeyResultDto(!string.IsNullOrWhiteSpace(request.ApiKey)));
    }

    private static async Task<IResult> GetModels(IChatClient chatClient, CancellationToken ct)
    {
        var models = await chatClient.ListModelsAsync(ct);
        return Results.Ok(new AiModelsDto(models));
    }

    private static async Task<IResult> PostAsk(AskRequestDto request, IAskRunner runner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Results.BadRequest(new { error = "question is required" });
        }

        var result = await runner.AskAsync(request.Question, request.Model, quiet: true, ct);
        return Results.Ok(AskResponseDto.From(result));
    }

    /// <summary>The first read path <c>session_narrative</c> has ever had — <c>narrate</c> could only ever be watched scroll past before this.</summary>
    private static async Task<IResult> GetNarratives(string? from, string? to, INarrativeStore store, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromDate = DateOnly.TryParse(from, out var f) ? f : today.AddDays(-6);
        var toDate = DateOnly.TryParse(to, out var t) ? t : today;

        var fromUtc = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();
        var toUtc = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

        var narratives = await store.GetRangeAsync(fromUtc, toUtc, ct);
        return Results.Ok(narratives.Select(NarrativeDto.From));
    }

    /// <summary>Explicitly triggered, like <c>POST /v1/scan-git</c> — narration ships session titles to a third party, and that must never happen implicitly.</summary>
    private static async Task<IResult> PostNarrate(NarrateRequestDto request, INarrateRunner runner, CancellationToken ct)
    {
        var result = await runner.RunAsync(request.Since, request.Limit, request.DryRun ?? false, request.Force ?? false, ct);
        return Results.Ok(NarrateResultDto.From(result));
    }

    /// <summary>A plain read, no model call — same shape as <see cref="GetNarratives"/>. Only returns weeks that already have a stored win; a week with none simply doesn't appear.</summary>
    private static async Task<IResult> GetWeeklyWins(string? from, string? to, IWeeklyWinStore store, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromDate = DateOnly.TryParse(from, out var f) ? f : new DateOnly(today.Year, today.Month, 1);
        var toDate = DateOnly.TryParse(to, out var t) ? t : today;

        var wins = await store.GetRangeAsync(fromDate, toDate, ct);
        return Results.Ok(wins.Select(WeeklyWinDto.From));
    }

    /// <summary>
    /// Explicitly triggered, same reasoning as <see cref="PostNarrate"/> — this
    /// is Job C, run once per week, and ships narrative text to a third party.
    /// Has real cost even on a cache hit: the staleness check for every week
    /// still runs, it just skips the model call when nothing changed.
    /// </summary>
    private static async Task<IResult> PostWeeklyWins(WeeklyWinsRequestDto request, IWeeklyWinRunner runner, CancellationToken ct)
    {
        if (!DateOnly.TryParse(request.From, out var from) || !DateOnly.TryParse(request.To, out var to) || from > to)
        {
            return Results.BadRequest(new { error = "valid from/to required, from <= to" });
        }

        var result = await runner.RunAsync(from, to, request.Force ?? false, ct);
        return Results.Ok(WeeklyWinsResultDto.From(result));
    }
}
