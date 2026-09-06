namespace Devlog.Core.Ai;

/// <summary>
/// A candidate or labelled identity fixture for Job A accuracy evaluation.
/// </summary>
public sealed record IdentityEvalFixture(
    string Identity,
    string? Process,
    List<string> SampleTitles,
    string? Expected,
    string? Note
);

/// <summary>
/// A candidate or labelled session fixture for Job B accuracy evaluation.
/// Uses durable StartUtc for cross-derive stability.
/// </summary>
public sealed record SessionEvalFixture(
    long StartUtc,
    long? SessionId,
    string? ExpectedKind,
    string? ExpectedWorkstream,
    string? Note,
    string? Project = null,
    int? DurationSeconds = null
);

/// <summary>
/// The result of evaluating a single identity fixture against model prediction.
/// </summary>
public sealed record IdentityEvalItemResult(
    IdentityEvalFixture Fixture,
    string? PredictedCategory,
    double? Confidence,
    string? Reason,
    bool IsMatch,
    string? Error = null
);

/// <summary>
/// Aggregated report of Job A eval accuracy.
/// </summary>
public sealed record JobAEvalReport(
    int TotalLabelled,
    int Correct,
    int Mismatches,
    int DiscardedOrSkipped,
    double Accuracy,
    List<IdentityEvalItemResult> Items
)
{
    public static JobAEvalReport Evaluate(
        IReadOnlyList<IdentityEvalFixture> fixtures,
        IReadOnlyList<ValidatedVerdict> verdicts,
        IReadOnlyList<string> discards)
    {
        var verdictMap = new Dictionary<string, ValidatedVerdict>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in verdicts)
        {
            verdictMap[v.Identity] = v;
        }

        var discardSet = new HashSet<string>(discards, StringComparer.OrdinalIgnoreCase);

        var items = new List<IdentityEvalItemResult>(fixtures.Count);
        int correct = 0;
        int mismatches = 0;
        int discardedCount = 0;

        foreach (var f in fixtures)
        {
            if (string.IsNullOrWhiteSpace(f.Expected))
            {
                continue;
            }

            var expected = f.Expected.Trim();

            if (verdictMap.TryGetValue(f.Identity, out var verdict))
            {
                var predicted = verdict.Category.ToString();
                var isMatch = string.Equals(expected, predicted, StringComparison.OrdinalIgnoreCase);
                if (isMatch)
                {
                    correct++;
                }
                else
                {
                    mismatches++;
                }

                items.Add(new IdentityEvalItemResult(
                    Fixture: f,
                    PredictedCategory: predicted,
                    Confidence: verdict.Confidence,
                    Reason: verdict.Reason,
                    IsMatch: isMatch));
            }
            else
            {
                // Discarded (e.g. low confidence, Unknown category, or omitted)
                discardedCount++;
                var isUnknownExpected = string.Equals(expected, "Unknown", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(expected, "Other", StringComparison.OrdinalIgnoreCase);

                var isDiscardedExplicit = discardSet.Any(d => d.Contains(f.Identity, StringComparison.OrdinalIgnoreCase));
                var note = isDiscardedExplicit ? "Discarded by classifier" : "No verdict returned";

                if (isUnknownExpected)
                {
                    correct++;
                    items.Add(new IdentityEvalItemResult(
                        Fixture: f,
                        PredictedCategory: "Unknown",
                        Confidence: null,
                        Reason: note,
                        IsMatch: true));
                }
                else
                {
                    mismatches++;
                    items.Add(new IdentityEvalItemResult(
                        Fixture: f,
                        PredictedCategory: "(none)",
                        Confidence: null,
                        Reason: note,
                        IsMatch: false,
                        Error: note));
                }
            }
        }

        int totalLabelled = correct + mismatches;
        double accuracy = totalLabelled > 0 ? (double)correct / totalLabelled : 0.0;

        return new JobAEvalReport(
            TotalLabelled: totalLabelled,
            Correct: correct,
            Mismatches: mismatches,
            DiscardedOrSkipped: discardedCount,
            Accuracy: accuracy,
            Items: items);
    }
}
