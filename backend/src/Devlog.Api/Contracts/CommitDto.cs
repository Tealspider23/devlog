using Devlog.Core.Domain;

namespace Devlog.Api.Contracts;

public sealed record CommitDto(
    string Sha,
    string Repo,
    string Project,
    string TimestampIso,
    string? Message,
    string? Branch,
    int FilesChanged,
    int Insertions,
    int Deletions,
    string? Languages,
    bool IsMerge,
    long? SessionId)
{
    public static CommitDto From(CommitRecord c) => new(
        c.Sha,
        c.Repo,
        c.Project,
        c.Timestamp.ToLocalTime().ToString("O"),
        c.Message,
        c.Branch,
        c.FilesChanged,
        c.Insertions,
        c.Deletions,
        c.Languages,
        c.IsMerge,
        c.SessionId);
}
