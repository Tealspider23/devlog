namespace Devlog.Core.Ai;

/// <summary>
/// The result of Job G — a natural-language question answered over the user's
/// own data. Moved here from <c>Devlog.Host.Ai</c> because it is
/// <see cref="Abstractions.IAskRunner"/>'s return type, and <c>Devlog.Api</c>
/// cannot reference <c>Devlog.Host</c>.
/// </summary>
public sealed record AskResult(
    bool Success,
    string? Answer,
    string? Model,
    int ToolRounds,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> UnverifiedNumbers,
    string? Error);
