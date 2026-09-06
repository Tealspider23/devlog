using System.Text.Json;
using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;

namespace Devlog.Host.Ai;

/// <summary>
/// Measures accuracy of AI models against hand-labelled eval fixtures in docs/llm-evals.
/// </summary>
public sealed class LlmEvalRunner(
    ISessionReader sessionReader,
    IChatClient chatClient,
    AiOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<int> RunAsync(string? dir, CancellationToken ct = default)
    {
        var targetDir = Path.GetFullPath(dir ?? "docs/llm-evals");
        var identitiesPath = Path.Combine(targetDir, "identities.json");
        var sessionsPath = Path.Combine(targetDir, "sessions.json");

        bool foundAny = false;

        if (File.Exists(identitiesPath))
        {
            foundAny = true;
            await RunJobAEvalAsync(identitiesPath, ct).ConfigureAwait(false);
        }

        if (File.Exists(sessionsPath))
        {
            foundAny = true;
            await RunJobBEvalAsync(sessionsPath, ct).ConfigureAwait(false);
        }

        if (!foundAny)
        {
            Console.WriteLine($"""

                No eval fixtures found in:
                  {targetDir}

                Generate candidate fixtures for hand-labelling with:
                  devlog llm-fixtures --out {dir ?? "docs/llm-evals"}

                Then fill in the blank 'expected' fields and run `devlog llm-eval`.
                """);
        }

        return 0;
    }

    private async Task RunJobAEvalAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        List<IdentityEvalFixture>? fixtures;
        try
        {
            fixtures = JsonSerializer.Deserialize<List<IdentityEvalFixture>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Job A] Failed to parse {path}: {ex.Message}\n");
            return;
        }

        if (fixtures is null || fixtures.Count == 0)
        {
            Console.WriteLine($"\n[Job A] {path} is empty.\n");
            return;
        }

        var labelled = fixtures.Where(f => !string.IsNullOrWhiteSpace(f.Expected)).ToList();
        if (labelled.Count == 0)
        {
            Console.WriteLine($"\n[Job A] {fixtures.Count} candidates in {Path.GetFileName(path)}, but 0 have 'expected' filled in.\n");
            return;
        }

        Console.WriteLine($"\n=== LLM EVAL: JOB A (Identity Classification) ===");
        Console.WriteLine($"  Evaluating {labelled.Count} labelled identities against model {options.Model}...\n");

        var allVerdicts = new List<ValidatedVerdict>();
        var allDiscards = new List<string>();

        var batchSize = options.ClassifyBatchSize > 0 ? options.ClassifyBatchSize : 10;
        for (int i = 0; i < labelled.Count; i += batchSize)
        {
            var chunk = labelled.Skip(i).Take(batchSize).ToList();
            var inputs = chunk.Select(f => new IdentityInput(
                Identity: f.Identity,
                Process: f.Process,
                TotalSeconds: 0,
                Hits: 0,
                SampleTitles: f.SampleTitles ?? []
            )).ToList();

            var userContent = IdentityClassifierPrompt.BuildUserContent(inputs);
            var result = await chatClient.CompleteAsync(
                IdentityClassifierPrompt.SystemPrompt,
                userContent,
                IdentityClassifierPrompt.SchemaName,
                IdentityClassifierPrompt.JsonSchema,
                reasoningEffort: "low",
                ct).ConfigureAwait(false);

            if (!result.Reachable || string.IsNullOrWhiteSpace(result.Content))
            {
                Console.WriteLine($"  [Batch {i / batchSize + 1}] Provider unreachable or returned empty: {result.Error}");
                foreach (var item in chunk)
                {
                    allDiscards.Add($"Unreachable: {item.Identity}");
                }
                continue;
            }

            try
            {
                var verdicts = IdentityClassifierPrompt.ParseVerdicts(
                    result.Content,
                    inputs,
                    options.MinConfidence,
                    out var discards);

                allVerdicts.AddRange(verdicts);
                allDiscards.AddRange(discards);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [Batch {i / batchSize + 1}] Failed to parse response: {ex.Message}");
                foreach (var item in chunk)
                {
                    allDiscards.Add($"Parse error for {item.Identity}: {ex.Message}");
                }
            }
        }

        var report = JobAEvalReport.Evaluate(labelled, allVerdicts, allDiscards);

        Console.WriteLine($"""
            === JOB A EVAL RESULTS ===
              Total Labelled : {report.TotalLabelled}
              Correct        : {report.Correct}
              Mismatches     : {report.Mismatches}
              Discarded/Skip : {report.DiscardedOrSkipped}
              Accuracy       : {report.Accuracy * 100:F1}%
            """);

        if (report.Mismatches > 0)
        {
            Console.WriteLine("\n  Mismatches / Failures:");
            foreach (var item in report.Items.Where(it => !it.IsMatch))
            {
                var conf = item.Confidence.HasValue ? $" (conf: {item.Confidence.Value:F2})" : "";
                var note = !string.IsNullOrWhiteSpace(item.Fixture.Note) ? $" [{item.Fixture.Note}]" : "";
                Console.WriteLine($"    [FAIL] {item.Fixture.Identity,-25} Expected: {item.Fixture.Expected,-12} Got: {item.PredictedCategory,-12}{conf}{note}");
            }
        }

        Console.WriteLine();
    }

    private async Task RunJobBEvalAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        List<SessionEvalFixture>? fixtures;
        try
        {
            fixtures = JsonSerializer.Deserialize<List<SessionEvalFixture>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Job B] Failed to parse {path}: {ex.Message}\n");
            return;
        }

        if (fixtures is null || fixtures.Count == 0)
        {
            Console.WriteLine($"\n[Job B] {path} is empty.\n");
            return;
        }

        var labelled = fixtures.Where(f => !string.IsNullOrWhiteSpace(f.ExpectedKind)).ToList();
        Console.WriteLine($"=== LLM EVAL: JOB B (Session Narrative) ===");
        if (labelled.Count == 0)
        {
            Console.WriteLine($"  {fixtures.Count} candidates found in {Path.GetFileName(path)}, 0 labelled (fill in 'expectedKind').\n");
            return;
        }

        Console.WriteLine($"  Evaluating {labelled.Count} labelled sessions against model {options.Model}...\n");

        int correctKind = 0;
        int correctWs = 0;
        int evaluated = 0;
        int skipped = 0;

        foreach (var f in labelled)
        {
            // Find matching session by startUtc (or sessionId)
            var range = await sessionReader.GetRangeAsync(f.StartUtc - 1000, f.StartUtc + 1000, ct).ConfigureAwait(false);
            var summary = range.FirstOrDefault(s => Math.Abs(s.Session.StartUtc - f.StartUtc) <= 1000)
                ?? (f.SessionId.HasValue ? await sessionReader.GetByIdAsync(f.SessionId.Value, ct).ConfigureAwait(false) : null);

            if (summary is null)
            {
                Console.WriteLine($"  [skip] Session start {f.StartUtc} not found in database.");
                skipped++;
                continue;
            }

            var activities = await sessionReader.GetActivitiesAsync(summary.Session.Id, ct).ConfigureAwait(false);
            var commits = await sessionReader.GetCommitsForSessionAsync(summary.Session.Id, ct).ConfigureAwait(false);

            var userContent = SessionNarratorPrompt.BuildUserContent(summary, activities, commits);
            var chatResult = await chatClient.CompleteAsync(
                SessionNarratorPrompt.SystemPrompt,
                userContent,
                SessionNarratorPrompt.SchemaName,
                SessionNarratorPrompt.JsonSchema,
                reasoningEffort: "high",
                ct).ConfigureAwait(false);

            if (!chatResult.Reachable || string.IsNullOrWhiteSpace(chatResult.Content))
            {
                Console.WriteLine($"  [unreachable] Session {summary.Session.Id}: {chatResult.Error}");
                skipped++;
                continue;
            }

            var parseResult = SessionNarratorPrompt.ValidateAndParse(
                chatResult.Content,
                summary,
                activities,
                commits,
                options.MinConfidence,
                options.Model,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (!parseResult.IsAccepted || parseResult.Narrative is null)
            {
                Console.WriteLine($"  [rejected] Session {summary.Session.Id}: {parseResult.RejectionReason}");
                evaluated++;
                continue;
            }

            evaluated++;
            var n = parseResult.Narrative;
            var kindMatch = string.Equals(f.ExpectedKind, n.Kind, StringComparison.OrdinalIgnoreCase);
            if (kindMatch) correctKind++;

            var wsMatch = string.Equals(f.ExpectedWorkstream?.Trim(), n.Workstream?.Trim(), StringComparison.OrdinalIgnoreCase);
            if (wsMatch) correctWs++;

            var status = kindMatch ? "PASS" : "FAIL";
            Console.WriteLine($"  [{status}] Session {summary.Session.Id} -> Expected kind: {f.ExpectedKind,-15} Got: {n.Kind,-15} (conf: {n.Confidence:F2})");
            if (!kindMatch && !string.IsNullOrWhiteSpace(f.Note))
            {
                Console.WriteLine($"         Note: {f.Note}");
            }
        }

        var kindAcc = evaluated > 0 ? (double)correctKind / evaluated * 100 : 0.0;
        Console.WriteLine($"""

            === JOB B EVAL RESULTS ===
              Total Evaluated : {evaluated} (Skipped: {skipped})
              Kind Accuracy   : {correctKind}/{evaluated} ({kindAcc:F1}%)
              Workstream Match: {correctWs}/{evaluated}
            """);
        Console.WriteLine();
    }
}
