using System.Text.Json;
using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

public sealed record IdentityInput(
    string Identity,
    string? Process,
    int TotalSeconds,
    int Hits,
    IReadOnlyList<string> SampleTitles);

public sealed record ValidatedVerdict(
    string Identity,
    ActivityCategory Category,
    double Confidence,
    string Reason);

public static class IdentityClassifierPrompt
{
    public const string SchemaName = "identity_verdicts";

    public const string SystemPrompt = """
        You classify what kind of work a piece of computer activity represents.

        You are given a batch of identities. An identity is a website name, an
        application name, or a process name, together with sample window titles seen for
        it and how much time it accounts for.

        Answer with exactly one category per identity, from this list and no other:

          Coding          writing, reviewing or debugging code; IDEs, terminals, pull
                          requests, merge requests, database clients used for development
          Learning        documentation, tutorials, articles, reading a repository
          Communication   chat and email - Slack, Teams messages, Outlook
          Meeting         calls and video meetings, which are not interruptible
          FileManagement  file explorers, moving and organising files
          Distraction     social media, entertainment, games, videos for fun
          Personal        shopping, banking, travel, property, admin unrelated to work
          Other           genuinely none of the above, and you are confident of that
          Unknown         you cannot tell from the evidence given

        Rules:

        - "Unknown" is a correct and expected answer. Use it whenever the sample titles
          do not give you enough to be sure. An identity you leave as Unknown will be
          asked again later, which costs nothing. A confident wrong answer is stored
          permanently and silently corrupts the user's time reports.
        - Judge only from the identity and the sample titles you are given. Do not use
          outside knowledge about what a website usually is if the titles contradict it.
        - A site can serve more than one purpose. If the sample titles disagree with each
          other, answer Unknown rather than picking the most common one.
        - "Other" means you are confident it fits no category. It is not a synonym for
          Unknown.
        - confidence is your own estimate from 0.0 to 1.0 that your category is correct.
        - reason is one short sentence citing what in the sample titles led you there.

        Return only JSON matching the schema. No prose, no markdown, no code fences.
        """;

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["verdicts"],
          "properties": {
            "verdicts": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["identity", "category", "confidence", "reason"],
                "properties": {
                  "identity":   { "type": "string" },
                  "category":   { "type": "string",
                                  "enum": ["Coding","Learning","Communication","Meeting",
                                           "FileManagement","Distraction","Personal",
                                           "Other","Unknown"] },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                  "reason":     { "type": "string" }
                }
              }
            }
          }
        }
        """;

    public static string BuildUserContent(IReadOnlyList<IdentityInput> inputs)
    {
        var payload = new
        {
            identities = inputs.Select(i => new
            {
                identity = i.Identity,
                process = i.Process,
                totalSeconds = i.TotalSeconds,
                hits = i.Hits,
                sampleTitles = i.SampleTitles
            }).ToArray()
        };

        return JsonSerializer.Serialize(payload);
    }

    public static List<ValidatedVerdict> ParseVerdicts(
        string jsonContent,
        IReadOnlyList<IdentityInput> sentInputs,
        double minConfidence,
        out List<string> discards)
    {
        discards = [];
        var valid = new List<ValidatedVerdict>();
        var validIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sentInputs)
        {
            validIdentities[item.Identity] = item.Identity;
        }

        using var doc = JsonDocument.Parse(jsonContent);
        if (!doc.RootElement.TryGetProperty("verdicts", out var verdictsProp) ||
            verdictsProp.ValueKind != JsonValueKind.Array)
        {
            return valid;
        }

        foreach (var v in verdictsProp.EnumerateArray())
        {
            if (!v.TryGetProperty("identity", out var idProp) || idProp.GetString() is not { } rawIdentity)
            {
                continue;
            }

            if (!validIdentities.TryGetValue(rawIdentity, out var canonicalIdentity))
            {
                discards.Add($"Identity '{rawIdentity}' was not present in the sent batch (invented row discarded)");
                continue;
            }

            if (!v.TryGetProperty("category", out var catProp) || catProp.GetString() is not { } categoryStr)
            {
                discards.Add($"Missing category for '{canonicalIdentity}'");
                continue;
            }

            if (string.Equals(categoryStr, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                discards.Add($"'{canonicalIdentity}' -> Unknown (remains pending)");
                continue;
            }

            if (!v.TryGetProperty("confidence", out var confProp) || !confProp.TryGetDouble(out var confidence))
            {
                discards.Add($"Invalid confidence for '{canonicalIdentity}'");
                continue;
            }

            if (confidence < minConfidence)
            {
                discards.Add($"'{canonicalIdentity}' confidence {confidence:F2} below MinConfidence {minConfidence:F2} (remains pending)");
                continue;
            }

            if (!ActivityCategoryExtensions.TryParse(categoryStr, out var category))
            {
                discards.Add($"'{canonicalIdentity}' category '{categoryStr}' is not a valid ActivityCategory");
                continue;
            }

            var reason = v.TryGetProperty("reason", out var reasonProp) && reasonProp.GetString() is { } r ? r : "";

            valid.Add(new ValidatedVerdict(canonicalIdentity, category, confidence, reason));
        }

        return valid;
    }
}
