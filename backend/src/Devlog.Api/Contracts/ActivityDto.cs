using Devlog.Core.Domain;

namespace Devlog.Api.Contracts;

public sealed record ActivityDto(
    long Id,
    string StartIso,
    string EndIso,
    int DurationSeconds,
    string? ProcessName,
    string? Context,
    string? SiteIdentity,
    string Category,
    string Engagement,
    int TitleChanges,
    string? SampleTitle)
{
    public static ActivityDto From(Activity a) => new(
        a.Id,
        a.Start.ToLocalTime().ToString("O"),
        a.End.ToLocalTime().ToString("O"),
        a.DurationSeconds,
        a.ProcessName,
        a.Context,
        a.SiteIdentity,
        a.Category.ToString(),
        a.Engagement.ToString(),
        a.TitleChanges,
        a.SampleTitle);
}
