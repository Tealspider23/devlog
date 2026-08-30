using Devlog.Core.Configuration;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <summary>
/// Events → activities.
/// <para>
/// Pure and dependency-free: no clock, no database, no OS. This is the code most
/// likely to be retuned once a real workday exists, so it has to be testable
/// without any of them.
/// </para>
/// </summary>
public sealed class ActivityBuilder(DerivationOptions options, Classifier classifier, NoiseFilter noise)
{
    /// <summary>
    /// Events that end a span and start nothing. A duration must never run
    /// through one of these — observed hazard: a naive gap-to-next calculation
    /// attributed 9h44m to a browser tab across a reboot, because nothing stopped
    /// the span at the shutdown.
    /// </summary>
    private static bool IsTerminator(EventKind kind) =>
        kind is EventKind.Lock or EventKind.Suspend or EventKind.CollectorStop;

    /// <summary>Events that carry an observation worth turning into a span.</summary>
    private static bool IsObservation(EventKind kind) =>
        kind is EventKind.FocusChange or EventKind.Heartbeat or EventKind.CollectorStart;

    /// <param name="Activities">The derived timeline.</param>
    /// <param name="PendingIdentities">
    /// Identities nothing could give a verdict on, with the time they account for.
    /// <para>
    /// Tracked here rather than inferred afterwards from
    /// <see cref="ActivityCategory.Other"/>, because <c>Other</c> is also a
    /// legitimate answer — Notepad genuinely is Other. Confusing "we decided
    /// Other" with "we could not decide" would put settled processes back on the
    /// unanswered list forever.
    /// </para>
    /// </param>
    public readonly record struct BuildResult(
        List<Activity> Activities,
        Dictionary<string, (int Hits, int Seconds)> PendingIdentities);

    public BuildResult Build(IEnumerable<RawEvent> events)
    {
        var ordered = noise.Apply(events.OrderBy(e => e.TsUtc).ThenBy(e => e.Id));

        var pending = new Dictionary<string, (int Hits, int Seconds)>(StringComparer.OrdinalIgnoreCase);
        var spans = BuildSpans(ordered, pending);
        var merged = MergeSameKey(spans);

        return new BuildResult(MergeBlipsUntilStable(merged), pending);
    }

    /// <summary>
    /// Each observation runs until the next event of any kind. Terminators close
    /// the open span and leave a hole, so nothing spans a lock or a shutdown.
    /// </summary>
    private List<Activity> BuildSpans(
        List<RawEvent> ordered,
        Dictionary<string, (int Hits, int Seconds)> pending)
    {
        var result = new List<Activity>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];

            if (!IsObservation(current.Kind))
            {
                continue;
            }

            // A CollectorStart with no window observed is just a marker.
            if (current.Kind == EventKind.CollectorStart && string.IsNullOrEmpty(current.ProcessName))
            {
                continue;
            }

            var next = i + 1 < ordered.Count ? ordered[i + 1] : null;
            if (next is null)
            {
                // The final row has no successor, so its extent is unknown. It is
                // dropped rather than guessed — an open-ended span is exactly the
                // bug this class exists to prevent.
                continue;
            }

            var endUtc = next.TsUtc;
            if (endUtc <= current.TsUtc)
            {
                continue;
            }

            var extracted = ContextExtractor.Extract(current.ProcessName, current.WindowTitle);
            var identity = SiteIdentity.For(current.ProcessName, current.WindowTitle);

            var classification = classifier.Classify(
                current.ProcessName,
                current.WindowTitle,
                identity,
                extracted.DefaultCategory);

            if (classification.IsPending && identity is not null)
            {
                var seconds = (int)((endUtc - current.TsUtc) / 1000);
                var prior = pending.GetValueOrDefault(identity);
                pending[identity] = (prior.Hits + 1, prior.Seconds + seconds);
            }

            result.Add(new Activity
            {
                StartUtc = current.TsUtc,
                EndUtc = endUtc,
                ProcessName = current.ProcessName,
                ActivityKey = BuildKey(current.ProcessName, extracted.Context),
                Context = extracted.Context,
                SiteIdentity = identity,
                Category = classification.Category,
                Engagement = ClassifyEngagement(current, classification.Category),
                TitleChanges = 0,
                SampleTitle = current.WindowTitle
            });
        }

        return result;
    }

    /// <summary>
    /// Engagement never separates producing from consuming using idle time —
    /// scrolling through documentation and typing code both report zero idle.
    /// Idle is used only for what it can actually detect: absence.
    /// </summary>
    private Engagement ClassifyEngagement(RawEvent e, ActivityCategory category)
    {
        if (e.IdleSeconds >= options.AwayIdleSeconds)
        {
            return Engagement.Idle;
        }

        return category.IsProductive() ? Engagement.Producing : Engagement.Consuming;
    }

    private static string BuildKey(string? process, string? context) =>
        $"{process ?? "?"}{context ?? "?"}";

    /// <summary>Collapses consecutive spans sharing a key, counting the title churn they hid.</summary>
    private static List<Activity> MergeSameKey(List<Activity> spans)
    {
        var result = new List<Activity>();

        foreach (var span in spans)
        {
            var last = result.Count > 0 ? result[^1] : null;

            // Adjacency matters: a gap means a terminator sat between them.
            if (last is not null
                && last.ActivityKey == span.ActivityKey
                && last.EndUtc == span.StartUtc)
            {
                result[^1] = last with
                {
                    EndUtc = span.EndUtc,
                    TitleChanges = last.TitleChanges
                        + (string.Equals(last.SampleTitle, span.SampleTitle, StringComparison.Ordinal) ? 0 : 1),

                    // Keep the least-idle engagement: a heartbeat mid-session
                    // should not downgrade an otherwise active stretch.
                    Engagement = (Engagement)Math.Min((int)last.Engagement, (int)span.Engagement)
                };

                continue;
            }

            result.Add(span);
        }

        return result;
    }

    /// <summary>
    /// Drops sub-threshold activities into their longer neighbour, repeatedly.
    /// <para>
    /// The loop is not optional: collapsing a blip can make two same-key
    /// neighbours adjacent, which must then merge, which can expose another blip.
    /// A single pass leaves the timeline half-collapsed.
    /// </para>
    /// </summary>
    private List<Activity> MergeBlipsUntilStable(List<Activity> activities)
    {
        var current = activities;

        for (var pass = 0; pass < 10; pass++)
        {
            var kept = new List<Activity>(current.Count);
            var removed = false;

            for (var i = 0; i < current.Count; i++)
            {
                var a = current[i];

                if (a.DurationSeconds >= options.MinActivitySeconds)
                {
                    kept.Add(a);
                    continue;
                }

                var prev = kept.Count > 0 ? kept[^1] : null;
                var next = i + 1 < current.Count ? current[i + 1] : null;

                var canExtendPrev = prev is not null && prev.EndUtc == a.StartUtc;
                var canGiveToNext = next is not null && next.StartUtc == a.EndUtc;

                if (canExtendPrev)
                {
                    kept[^1] = prev! with { EndUtc = a.EndUtc };
                    removed = true;
                }
                else if (canGiveToNext)
                {
                    current[i + 1] = next! with { StartUtc = a.StartUtc };
                    removed = true;
                }
                else
                {
                    // Isolated between terminators — nothing to merge into.
                    kept.Add(a);
                }
            }

            current = MergeSameKey(kept);

            if (!removed)
            {
                break;
            }
        }

        return current;
    }
}
