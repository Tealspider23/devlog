using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

/// <summary>
/// Job B: Session narrative prompt, input assembler, JSON schema, and evidence validator.
/// </summary>
public static class SessionNarratorPrompt
{
    public const string SchemaName = "session_narrative";

    public static readonly string[] AllowedKinds =
    [
        "feature-work",
        "bugfix",
        "mr-review",
        "research",
        "meeting-followup",
        "admin",
        "context-thrash",
        "unclear"
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "then", "after", "before", "session",
        "activity", "commit", "file", "code", "work", "time", "page", "window"
    };

    /// <summary>
    /// The system prompt — verbatim from docs/LLM.md section 5.4.
    /// </summary>
    public const string SystemPrompt = """
        You describe what a developer was doing during one work session.

        You are given one session: its project, duration, and the ordered list of
        activities inside it, plus any commits that landed during it. Times are in
        seconds from the start of the session.

        Produce:

        - narrative: one or two sentences, past tense, plain and specific. Describe what
          happened, in order, as a colleague would explain it. Do not editorialise about
          productivity, focus or effort.
        - kind: exactly one of
            feature-work        building something new
            bugfix              diagnosing or fixing a defect
            mr-review           reviewing someone else's change
            research            reading, learning, evaluating
            meeting-followup    acting on something from a call or chat
            admin               timesheets, tickets, non-code housekeeping
            context-thrash      genuinely scattered, no single thread
            unclear             you cannot tell
        - workstream: a ticket id, branch name or feature name if one appears in the
          input. null if none does. Never invent one.
        - evidence: 2 to 4 short strings, each quoting or naming something that ACTUALLY
          APPEARS in the input above and supports your reading.
        - confidence: 0.0 to 1.0.

        Rules:

        - Every claim in the narrative must be supported by something in the input. You
          may connect events in sequence - that is the point of this task - but you may
          not introduce facts that are not there.
        - Each evidence string must refer to content present in the input. If you cannot
          produce two pieces of real evidence, answer kind "unclear" with low confidence.
        - "context-thrash" and "unclear" are correct answers. A scattered session is a
          real and useful finding. Do not invent a coherent story for an incoherent
          session - the user would rather know.
        - Do not calculate or restate durations, totals or percentages. Numbers are
          computed elsewhere and yours would conflict with them.
        - Do not mention the person's name or judge them.

        Return only JSON matching the schema. No prose, no markdown, no code fences.
        """;

    /// <summary>
    /// Response schema — verbatim from docs/LLM.md section 5.5.
    /// </summary>
    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["sessionId", "narrative", "kind", "workstream", "evidence", "confidence"],
          "properties": {
            "sessionId":  { "type": "integer" },
            "narrative":  { "type": "string" },
            "kind":       { "type": "string",
                            "enum": ["feature-work","bugfix","mr-review","research",
                                     "meeting-followup","admin","context-thrash","unclear"] },
            "workstream": { "type": ["string","null"] },
            "evidence":   { "type": "array", "minItems": 2, "maxItems": 4,
                            "items": { "type": "string" } },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
          }
        }
        """;

    /// <summary>
    /// Assembles user input JSON for a single session per docs/LLM.md section 5.3.
    /// </summary>
    public static string BuildUserContent(
        SessionSummary summary,
        IReadOnlyList<Activity> activities,
        IReadOnlyList<CommitRecord> commits)
    {
        var session = summary.Session;
        var startIso = session.Start.ToString("yyyy-MM-ddTHH:mm:sszzz");

        var actList = new List<object>(activities.Count);
        foreach (var act in activities)
        {
            var atSec = Math.Max(0, (int)((act.StartUtc - session.StartUtc) / 1000));
            actList.Add(new
            {
                atSeconds = atSec,
                durationSeconds = act.DurationSeconds,
                process = act.ProcessName,
                category = act.Category.ToString(),
                project = act.Project,
                identity = act.SiteIdentity ?? act.ProcessName,
                title = act.SampleTitle ?? act.Context ?? string.Empty
            });
        }

        var commitList = new List<object>(commits.Count);
        foreach (var c in commits)
        {
            commitList.Add(new
            {
                sha = c.Sha.Length > 7 ? c.Sha[..7] : c.Sha,
                message = c.Message ?? string.Empty,
                branch = c.Branch,
                files = c.FilesChanged,
                insertions = c.Insertions,
                deletions = c.Deletions
            });
        }

        var payload = new
        {
            sessionId = session.Id,
            start = startIso,
            durationSeconds = session.DurationSeconds,
            project = session.Project,
            category = session.Category.ToString(),
            deepSeconds = session.DeepSeconds,
            interruptions = session.Interruptions,
            activities = actList,
            commits = commitList
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Parses and validates a session narrative response against schema and evidence requirements.
    /// </summary>
    public static SessionNarrativeResult ValidateAndParse(
        string responseJson,
        SessionSummary summary,
        IReadOnlyList<Activity> activities,
        IReadOnlyList<CommitRecord> commits,
        double minConfidence,
        string model,
        long generatedUtc)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("sessionId", out var sidProp) || sidProp.GetInt64() != summary.Session.Id)
        {
            return SessionNarrativeResult.Rejected($"SessionId mismatch (expected {summary.Session.Id})");
        }

        var narrative = root.TryGetProperty("narrative", out var narrProp) ? narrProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(narrative))
        {
            return SessionNarrativeResult.Rejected("Empty narrative");
        }

        var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(kind) || !AllowedKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            return SessionNarrativeResult.Rejected($"Invalid kind '{kind}'");
        }

        var workstream = root.TryGetProperty("workstream", out var wsProp) && wsProp.ValueKind == JsonValueKind.String
            ? wsProp.GetString()
            : null;

        var confidence = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.0;
        if (confidence < minConfidence)
        {
            return SessionNarrativeResult.Rejected($"Confidence {confidence:F2} is below threshold {minConfidence:F2}");
        }

        var evidence = new List<string>();
        if (root.TryGetProperty("evidence", out var evProp) && evProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in evProp.EnumerateArray())
            {
                if (item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                {
                    evidence.Add(s);
                }
            }
        }

        if (evidence.Count < 2)
        {
            return SessionNarrativeResult.Rejected($"Fewer than 2 evidence items returned ({evidence.Count})");
        }

        // Validate evidence hallucination check
        if (!ValidateEvidence(evidence, summary.Session, activities, commits, out var supportedCount))
        {
            return SessionNarrativeResult.Rejected($"Hallucination check failed: only {supportedCount}/{evidence.Count} evidence items supported by input");
        }

        var validNarrative = new SessionNarrative
        {
            SessionStartUtc = summary.Session.StartUtc,
            SessionEndUtc = summary.Session.EndUtc,
            ActivityCount = summary.ActivityCount,
            SessionId = summary.Session.Id,
            Narrative = narrative,
            Kind = kind,
            Workstream = workstream,
            Evidence = evidence,
            Confidence = confidence,
            Model = model,
            GeneratedUtc = generatedUtc
        };

        return SessionNarrativeResult.Accepted(validNarrative);
    }

    /// <summary>
    /// Hallucination detector per docs/LLM.md section 5.6.
    /// Checks that at least 2 evidence strings are supported by the input haystack.
    /// </summary>
    public static bool ValidateEvidence(
        IReadOnlyList<string> evidence,
        Session session,
        IReadOnlyList<Activity> activities,
        IReadOnlyList<CommitRecord> commits,
        out int supportedCount)
    {
        var haystackParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.Project))
        {
            haystackParts.Add(session.Project);
        }

        foreach (var a in activities)
        {
            if (!string.IsNullOrWhiteSpace(a.SampleTitle)) haystackParts.Add(a.SampleTitle);
            if (!string.IsNullOrWhiteSpace(a.Context)) haystackParts.Add(a.Context);
            if (!string.IsNullOrWhiteSpace(a.ProcessName)) haystackParts.Add(a.ProcessName);
            if (!string.IsNullOrWhiteSpace(a.SiteIdentity)) haystackParts.Add(a.SiteIdentity);
            if (!string.IsNullOrWhiteSpace(a.Project)) haystackParts.Add(a.Project);
        }

        foreach (var c in commits)
        {
            if (!string.IsNullOrWhiteSpace(c.Message)) haystackParts.Add(c.Message);
            if (!string.IsNullOrWhiteSpace(c.Branch)) haystackParts.Add(c.Branch);
            if (!string.IsNullOrWhiteSpace(c.Project)) haystackParts.Add(c.Project);
        }

        var haystack = string.Join(" ", haystackParts).ToLowerInvariant();

        supportedCount = 0;
        foreach (var e in evidence)
        {
            var clean = Regex.Replace(e.ToLowerInvariant(), @"[^\w\s-]", " ");
            var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 4 && !StopWords.Contains(w))
                .Distinct()
                .ToList();

            if (words.Count == 0)
            {
                // Fallback: if evidence had no long non-stopword tokens, check if evidence substring is in haystack
                if (haystack.Contains(clean.Trim()))
                {
                    supportedCount++;
                }
                continue;
            }

            int matchingWords = words.Count(w => haystack.Contains(w));
            if (matchingWords >= (words.Count + 1) / 2)
            {
                supportedCount++;
            }
        }

        return supportedCount >= 2;
    }
}

public sealed record SessionNarrativeResult(
    bool IsAccepted,
    SessionNarrative? Narrative,
    string? RejectionReason)
{
    public static SessionNarrativeResult Accepted(SessionNarrative narrative) =>
        new(true, narrative, null);

    public static SessionNarrativeResult Rejected(string reason) =>
        new(false, null, reason);
}
