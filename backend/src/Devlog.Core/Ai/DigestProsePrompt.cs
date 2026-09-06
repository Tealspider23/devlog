using System.Text.Json;
using System.Text.RegularExpressions;
using Devlog.Core.Domain;
using Devlog.Core.Metrics;

namespace Devlog.Core.Ai;

/// <summary>
/// Job C: Digest prose domain models, prompt generator, and numeric hallucination validator.
/// </summary>
public static class DigestProsePrompt
{
    public const string SchemaName = "digest_prose";

    /// <summary>
    /// The system prompt — verbatim from docs/LLM.md section 6.3.
    /// </summary>
    public const string SystemPrompt = """
        You write the opening summary of a developer's work log for a period of time.

        You are given pre-computed figures and a list of session narratives. Write three
        to five sentences of plain prose that a person could paste into a performance
        review.

        Rules:

        - Every number, duration, percentage and count you write must appear EXACTLY as
          given in "figures". Copy the strings. Do not recompute, round, convert, add or
          compare numbers. If you want to say something a figure does not support, do not
          say it.
        - Describe what was built and what it was for, drawing on the narratives. Prefer
          the specific over the general: name projects and tickets that appear in the
          input.
        - Do not praise, motivate, or comment on productivity, discipline or effort.
          State what happened.
        - Do not mention anything absent from the input.
        - If the narratives are thin or mostly "unclear", write less. A short honest
          paragraph is the correct output for a scattered period.

        Return only JSON matching the schema. No prose outside the JSON, no markdown
        headings, no code fences.
        """;

    /// <summary>
    /// Response schema — verbatim from docs/LLM.md section 6.3.
    /// </summary>
    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "highlights"],
          "properties": {
            "summary": { "type": "string" },
            "highlights": {
              "type": "array",
              "minItems": 0,
              "maxItems": 3,
              "items": { "type": "string" }
            }
          }
        }
        """;

    /// <summary>
    /// Assembles user input JSON for digest prose per docs/LLM.md section 6.2.
    /// </summary>
    public static (string Json, DigestFigures Figures) BuildUserContent(
        DigestMetrics metrics,
        IReadOnlyList<SessionNarrative> narratives)
    {
        var period = $"{metrics.From:MMM d} to {metrics.To:MMM d, yyyy}";

        var figures = new DigestFigures
        {
            Period = period,
            DeepWork = Hours(metrics.DeepSeconds),
            Tracked = Hours(metrics.TrackedSeconds),
            FocusRatio = $"{metrics.FocusRatio:P0}",
            Sessions = metrics.SessionCount.ToString(),
            ActiveDays = metrics.ActiveDays.ToString(),
            Commits = metrics.CommitCount.ToString(),
            LinesAdded = metrics.Insertions.ToString(),
            LinesRemoved = metrics.Deletions.ToString(),
            LongestBlock = metrics.LongestBlock is { } lb
                ? $"{Hms(lb.DeepSeconds)} on {lb.Project ?? "unclassified"}"
                : null,
            BestDay = metrics.BestDay is { } bd
                ? $"{bd.Date:dddd, MMM d}"
                : null,
            Projects = metrics.TimeByProject.Select(p => $"{p.Project}: {Hours(p.Seconds)}").ToList(),
            Languages = metrics.Languages.ToList(),
            FirstTimeLanguages = metrics.FirstTimeLanguages.ToList(),
            Tickets = metrics.TicketIds.ToList()
        };

        var narrativeList = narratives.Select(n => new
        {
            kind = n.Kind,
            workstream = n.Workstream,
            project = (string?)null,
            narrative = n.Narrative
        }).ToList();

        var payload = new
        {
            period,
            figures = new Dictionary<string, object?>
            {
                ["deepWork"] = figures.DeepWork,
                ["tracked"] = figures.Tracked,
                ["focusRatio"] = figures.FocusRatio,
                ["sessions"] = figures.Sessions,
                ["activeDays"] = figures.ActiveDays,
                ["commits"] = figures.Commits,
                ["linesAdded"] = figures.LinesAdded,
                ["linesRemoved"] = figures.LinesRemoved,
                ["longestBlock"] = figures.LongestBlock,
                ["bestDay"] = figures.BestDay,
                ["projects"] = figures.Projects,
                ["languages"] = figures.Languages,
                ["firstTimeLanguages"] = figures.FirstTimeLanguages,
                ["tickets"] = figures.Tickets
            },
            narratives = narrativeList
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return (json, figures);
    }

    /// <summary>
    /// Parses response JSON and runs the number validation check per section 6.4.
    /// </summary>
    public static DigestProseResult ValidateAndParse(string responseJson, DigestFigures figures)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("summary", out var sumProp) || sumProp.GetString() is not { } summary || string.IsNullOrWhiteSpace(summary))
        {
            return DigestProseResult.Rejected("Missing or empty summary");
        }

        var highlights = new List<string>();
        if (root.TryGetProperty("highlights", out var hlProp) && hlProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hlProp.EnumerateArray())
            {
                if (h.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                {
                    highlights.Add(s);
                }
            }
        }

        var prose = new DigestProse(summary, highlights);

        if (!ValidateNumbers(prose, figures, out var offendingNumber))
        {
            return DigestProseResult.Rejected($"Numeric hallucination detected: '{offendingNumber}' is not in allowed figures");
        }

        return DigestProseResult.Accepted(prose);
    }

    /// <summary>
    /// The number check per docs/LLM.md section 6.4:
    /// Allowed numbers = every numeric substring inside figures values.
    /// Rejects if any numeric token in prose is absent from the allowed set.
    /// </summary>
    public static bool ValidateNumbers(DigestProse prose, DigestFigures figures, out string? offendingToken)
    {
        var allowedStrings = new List<string>();
        void AddString(string? s)
        {
            if (!string.IsNullOrWhiteSpace(s)) allowedStrings.Add(s);
        }

        AddString(figures.Period);
        AddString(figures.DeepWork);
        AddString(figures.Tracked);
        AddString(figures.FocusRatio);
        AddString(figures.Sessions);
        AddString(figures.ActiveDays);
        AddString(figures.Commits);
        AddString(figures.LinesAdded);
        AddString(figures.LinesRemoved);
        AddString(figures.LongestBlock);
        AddString(figures.BestDay);

        foreach (var p in figures.Projects) AddString(p);
        foreach (var t in figures.Tickets) AddString(t);

        var allowedNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var str in allowedStrings)
        {
            var matches = Regex.Matches(str, @"\d+(?:\.\d+)?");
            foreach (Match m in matches)
            {
                allowedNumbers.Add(m.Value);
                // Also add integer part if decimal
                if (m.Value.Contains('.'))
                {
                    var parts = m.Value.Split('.');
                    allowedNumbers.Add(parts[0]);
                    allowedNumbers.Add(parts[1]);
                }
            }
        }

        // Validate all numbers in summary and highlights
        var textToValidate = prose.Summary + " " + string.Join(" ", prose.Highlights);
        var proseMatches = Regex.Matches(textToValidate, @"\d+(?:\.\d+)?");

        foreach (Match m in proseMatches)
        {
            var token = m.Value;
            if (!allowedNumbers.Contains(token))
            {
                offendingToken = token;
                return false;
            }
        }

        offendingToken = null;
        return true;
    }

    private static string Hours(int seconds) =>
        $"{seconds / 3600.0:0.#}h";

    private static string Hms(int seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        return h > 0 ? $"{h}h{m:00}m" : $"{m}m";
    }
}

public sealed record DigestFigures
{
    public required string Period { get; init; }
    public required string DeepWork { get; init; }
    public required string Tracked { get; init; }
    public required string FocusRatio { get; init; }
    public required string Sessions { get; init; }
    public required string ActiveDays { get; init; }
    public required string Commits { get; init; }
    public required string LinesAdded { get; init; }
    public required string LinesRemoved { get; init; }
    public string? LongestBlock { get; init; }
    public string? BestDay { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }
    public required IReadOnlyList<string> FirstTimeLanguages { get; init; }
    public required IReadOnlyList<string> Tickets { get; init; }
}

public sealed record DigestProse(string Summary, IReadOnlyList<string> Highlights);

public sealed record DigestProseResult(
    bool IsAccepted,
    DigestProse? Prose,
    string? RejectionReason)
{
    public static DigestProseResult Accepted(DigestProse prose) =>
        new(true, prose, null);

    public static DigestProseResult Rejected(string reason) =>
        new(false, null, reason);
}
