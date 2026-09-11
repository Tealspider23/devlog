using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

/// <summary>One session's evidence, bundled for a batched narrate call.</summary>
public sealed record SessionNarrationInput(
    SessionSummary Summary,
    IReadOnlyList<Activity> Activities,
    IReadOnlyList<CommitRecord> Commits);

/// <summary>
/// Job B: Session narrative prompt, input assembler, JSON schema, and evidence validator.
/// </summary>
public static partial class SessionNarratorPrompt
{
    public const string SchemaName = "session_narrative";
    public const string BatchSchemaName = "session_narratives_batch";

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

        - narrative: exactly two sentences, past tense, plain and specific. The first
          sentence sets up what was worked on, the second what happened or resulted. Do
          not editorialise about productivity, focus or effort.
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
        - If you cannot support a genuine second sentence with evidence, do not pad -
          write one honest sentence instead. "unclear" and "context-thrash" answers are
          exempt from the two-sentence requirement for exactly this reason.
        - Each evidence string must refer to content present in the input. If you cannot
          produce two pieces of real evidence, answer kind "unclear" with low confidence.
        - "context-thrash" and "unclear" are correct answers. A scattered session is a
          real and useful finding. Do not invent a coherent story for an incoherent
          session - the user would rather know. A low-confidence "unclear" costs
          nothing: the session is simply asked about again later. A confident but
          invented "feature-work" is stored and treated as fact.
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
    /// The batch system prompt — verbatim from docs/LLM.md section 5.3a.
    /// Same task and rules as <see cref="SystemPrompt"/>, restated per-session
    /// so the model treats each session's evidence as its own closed world and
    /// never borrows a fact from one session to support another in the batch.
    /// </summary>
    public const string BatchSystemPrompt = """
        You describe what a developer was doing during a batch of work sessions.

        You are given several sessions. For each one: its project, duration, and the
        ordered list of activities inside it, plus any commits that landed during it.
        Times are in seconds from the start of that session — each session's clock
        starts over at zero.

        Answer once per session, in the same order they are given, each keyed by its
        sessionId. Produce for each:

        - narrative: exactly two sentences, past tense, plain and specific. The first
          sentence sets up what was worked on, the second what happened or resulted. Do
          not editorialise about productivity, focus or effort.
        - kind: exactly one of
            feature-work        building something new
            bugfix              diagnosing or fixing a defect
            mr-review           reviewing someone else's change
            research            reading, learning, evaluating
            meeting-followup    acting on something from a call or chat
            admin               timesheets, tickets, non-code housekeeping
            context-thrash      genuinely scattered, no single thread
            unclear             you cannot tell
        - workstream: a ticket id, branch name or feature name if one appears in that
          session's own input. null if none does. Never invent one.
        - evidence: 2 to 4 short strings, each quoting or naming something that ACTUALLY
          APPEARS in that same session's input and supports your reading.

        Rules:

        - Every claim in a session's narrative must be supported by that session's own
          input. You may connect events in sequence within a session - that is the point
          of this task - but you may not introduce facts that are not there, and you may
          not use evidence or facts from a different session in this batch to support
          this one. Each session is its own closed world.
        - If you cannot support a genuine second sentence with evidence, do not pad -
          write one honest sentence instead. "unclear" and "context-thrash" answers are
          exempt from the two-sentence requirement for exactly this reason.
        - Each evidence string must refer to content present in that same session's
          input. If you cannot produce two pieces of real evidence for a session, answer
          that session's kind "unclear" with low confidence.
        - "context-thrash" and "unclear" are correct answers. A scattered session is a
          real and useful finding. Do not invent a coherent story for an incoherent
          session - the user would rather know. A low-confidence "unclear" costs
          nothing: the session is simply asked about again later. A confident but
          invented "feature-work" is stored and treated as fact.
        - Do not calculate or restate durations, totals or percentages. Numbers are
          computed elsewhere and yours would conflict with them.
        - Do not mention the person's name or judge them.
        - Return exactly one entry per session you were given, in the same order, each
          with its matching sessionId.

        Return only JSON matching the schema. No prose, no markdown, no code fences.
        """;

    /// <summary>
    /// Batch response schema — verbatim from docs/LLM.md section 5.5a. Each
    /// item is shaped exactly like the single-session <see cref="JsonSchema"/>.
    /// </summary>
    public const string BatchJsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["narratives"],
          "properties": {
            "narratives": {
              "type": "array",
              "items": {
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
            }
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
        return JsonSerializer.Serialize(
            BuildSessionPayload(summary, activities, commits),
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Assembles user input JSON for a batch of sessions in one call — see
    /// docs/LLM.md section 5.3a. One request costs the same against the
    /// provider's daily quota whether it carries one session or several, so
    /// batching is the lever for making a fixed quota cover more sessions.
    /// </summary>
    public static string BuildBatchUserContent(IReadOnlyList<SessionNarrationInput> inputs)
    {
        var payload = new
        {
            sessions = inputs.Select(i => BuildSessionPayload(i.Summary, i.Activities, i.Commits)).ToArray()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object BuildSessionPayload(
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

        return new
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
        return ValidateOne(doc.RootElement, summary, activities, commits, minConfidence, model, generatedUtc);
    }

    /// <summary>
    /// Parses and validates a batch response, one result per <paramref name="inputs"/>
    /// entry in the same order — not response order, so a caller can zip results
    /// back onto the sessions it asked about regardless of how the model ordered
    /// its answer. A session missing from the response (the model dropped it, or
    /// returned an unrecognised sessionId) is rejected individually rather than
    /// failing the whole batch — the sibling results in the same call are still
    /// worth keeping.
    /// </summary>
    public static IReadOnlyList<SessionNarrativeResult> ValidateAndParseBatch(
        string responseJson,
        IReadOnlyList<SessionNarrationInput> inputs,
        double minConfidence,
        string model,
        long generatedUtc)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var byId = new Dictionary<long, JsonElement>();

        if (doc.RootElement.TryGetProperty("narratives", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("sessionId", out var sidProp) && sidProp.ValueKind == JsonValueKind.Number)
                {
                    byId[sidProp.GetInt64()] = item;
                }
            }
        }

        var results = new List<SessionNarrativeResult>(inputs.Count);
        foreach (var input in inputs)
        {
            var sessionId = input.Summary.Session.Id;
            results.Add(byId.TryGetValue(sessionId, out var item)
                ? ValidateOne(item, input.Summary, input.Activities, input.Commits, minConfidence, model, generatedUtc)
                : SessionNarrativeResult.Rejected($"No narrative returned for session {sessionId} in the batch response"));
        }

        return results;
    }

    private static SessionNarrativeResult ValidateOne(
        JsonElement root,
        SessionSummary summary,
        IReadOnlyList<Activity> activities,
        IReadOnlyList<CommitRecord> commits,
        double minConfidence,
        string model,
        long generatedUtc)
    {
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

        // "unclear"/"context-thrash" are self-reported uncertainty, exempt from both
        // the confidence floor below and the two-sentence check above it: forcing a
        // genuinely scattered or unreadable session to pad to two sentences (or to a
        // confidence it does not have) is exactly what the prompt tells the model not
        // to do. Without this exemption the honest low-confidence/short answer could
        // never reach the database - only a confident, padded one could survive - and
        // the session would be silently re-asked from scratch on every later run.
        // Evidence and evidence-count still apply below regardless - this is not a
        // bypass for fabrication, only for the length and confidence floors.
        var isSelfReportedUncertain = string.Equals(kind, "unclear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "context-thrash", StringComparison.OrdinalIgnoreCase);

        if (!isSelfReportedUncertain && CountSentences(narrative) < 2)
        {
            return SessionNarrativeResult.Rejected("Narrative has fewer than two sentences");
        }

        var workstream = root.TryGetProperty("workstream", out var wsProp) && wsProp.ValueKind == JsonValueKind.String
            ? wsProp.GetString()
            : null;

        var confidence = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.0;

        if (confidence < minConfidence && !isSelfReportedUncertain)
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
    /// Counts sentence-ending punctuation in a trimmed narrative, as a proxy for
    /// "two sentences" per docs/LLM.md section 5.6a. A terminator only counts when
    /// followed by whitespace or the end of the string, so a decimal like "3.5"
    /// does not inflate the count. This is a cheap heuristic, not a parser: an
    /// abbreviation like "e.g." followed by a space still counts as a boundary,
    /// same as a real sentence would - accepted as a false positive rather than
    /// building real sentence detection for a floor this loose.
    /// </summary>
    private static int CountSentences(string narrative) =>
        SentenceTerminatorRegex().Matches(narrative.Trim()).Count;

    [GeneratedRegex(@"[.!?]+(?=\s|$)")]
    private static partial Regex SentenceTerminatorRegex();

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
