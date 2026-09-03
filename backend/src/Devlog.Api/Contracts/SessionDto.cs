using Devlog.Core.Domain;

namespace Devlog.Api.Contracts;

/// <summary>
/// A session as the frontend consumes it.
/// <para>
/// Not the domain record: <see cref="Session"/> stores unix-millisecond UTC,
/// which is right for a database and wrong for a browser. Timestamps here are
/// local-time ISO 8601 with offset, matching what the terminal already shows
/// via <c>Session.Start.ToLocalTime()</c> — the UI and the CLI describe the same
/// moment the same way.
/// </para>
/// </summary>
public sealed record SessionDto(
    long Id,
    string StartIso,
    string EndIso,
    int DurationSeconds,
    string? Project,
    string Category,
    int Interruptions,
    int DeepSeconds,
    string? Label,
    int ActivityCount,
    int CommitCount,
    int Insertions,
    int Deletions,
    bool IsZeroOutput)
{
    public static SessionDto From(SessionSummary summary)
    {
        var s = summary.Session;

        return new SessionDto(
            s.Id,
            s.Start.ToLocalTime().ToString("O"),
            s.End.ToLocalTime().ToString("O"),
            s.DurationSeconds,
            s.Project,
            s.Category.ToString(),
            s.Interruptions,
            s.DeepSeconds,
            s.Label,
            summary.ActivityCount,
            summary.CommitCount,
            summary.Insertions,
            summary.Deletions,
            summary.IsZeroOutput);
    }
}
