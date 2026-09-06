using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class ActivityBuilderTests
{
    private static readonly long Base = DateTimeOffset
        .Parse("2026-08-30T09:00:00Z").ToUnixTimeMilliseconds();

    private static long At(int seconds) => Base + (seconds * 1000L);

    private static RawEvent Focus(int atSeconds, string process, string title, int idle = 0) => new()
    {
        TsUtc = At(atSeconds),
        Kind = EventKind.FocusChange,
        ProcessName = process,
        WindowTitle = title,
        IdleSeconds = idle
    };

    private static RawEvent Marker(int atSeconds, EventKind kind) => new()
    {
        TsUtc = At(atSeconds),
        Kind = kind,
        IdleSeconds = 0
    };

    private static ActivityBuilder Build(DerivationOptions? options = null)
    {
        var opts = options ?? new DerivationOptions();
        return new ActivityBuilder(opts, new Classifier([]), new NoiseFilter());
    }

    // ---------------------------------------------------------------- terminators

    /// <summary>
    /// The regression this class exists to prevent. Real data once produced a
    /// 9h44m "activity" on a browser tab because the span ran straight through a
    /// reboot — a single duration like that corrupts every KPI downstream.
    /// </summary>
    [Fact]
    public void SpanNeverCrossesCollectorStop()
    {
        var events = new[]
        {
            Focus(0, "chrome", "GitHub - Google Chrome"),
            Marker(60, EventKind.CollectorStop),

            // Eight hours of machine-off time.
            Focus(28_800, "Code", "auth.cs - devlog - Visual Studio Code"),
            Focus(28_860, "Code", "other.cs - devlog - Visual Studio Code")
        };

        var activities = Build().Build(events).Activities;

        Assert.All(activities, a =>
            Assert.True(
                a.DurationSeconds < 3600,
                $"activity ran {a.DurationSeconds}s — a span crossed the shutdown"));
    }

    [Fact]
    public void SpanNeverCrossesLock()
    {
        var events = new[]
        {
            Focus(0, "Code", "auth.cs - devlog - Visual Studio Code"),
            Marker(60, EventKind.Lock),
            Marker(5_400, EventKind.Unlock),
            Focus(5_401, "Code", "auth.cs - devlog - Visual Studio Code"),
            Focus(5_500, "chrome", "Docs - Google Chrome")
        };

        var activities = Build().Build(events).Activities;

        // The 90 minutes locked must not be attributed to anything.
        Assert.All(activities, a => Assert.True(a.DurationSeconds < 600));
        Assert.DoesNotContain(activities, a => a.StartUtc < At(60) && a.EndUtc > At(5_400));
    }

    /// <summary>
    /// Regression for phantom work across a crash and restart. The collector was
    /// force-killed with no clean stop event and restarted 3.5 hours later; the
    /// span before the kill stretched to meet the restart and claimed 3h27m of
    /// "deep work" that never happened.
    /// </summary>
    [Fact]
    public void SpanNeverStretchesAcrossACrashAndRestart()
    {
        var events = new[]
        {
            Focus(0, "Code", "auth.cs - devlog - Visual Studio Code"),

            // Collector force-killed here — note there is NO CollectorStop.
            // Three and a half hours of nothing, then it comes back up.
            Marker(12_600, EventKind.CollectorStart),
            Focus(12_602, "Code", "auth.cs - devlog - Visual Studio Code"),
            Focus(12_700, "chrome", "Docs - Google Chrome")
        };

        var activities = Build().Build(events).Activities;

        Assert.All(activities, a =>
            Assert.True(
                a.DurationSeconds <= 600,
                $"activity claimed {a.DurationSeconds}s — a span stretched across the dead period"));
    }

    [Fact]
    public void SpanIsCappedWhenEvidenceRunsOut()
    {
        // No terminator at all, just a long silence. Whether the collector died
        // or the user walked away, there is no evidence either way — so the span
        // is capped rather than credited with the whole gap.
        var events = new[]
        {
            Focus(0, "Code", "auth.cs - devlog - Visual Studio Code"),
            Focus(7200, "Code", "auth.cs - devlog - Visual Studio Code"),
            Focus(7300, "chrome", "Docs - Google Chrome")
        };

        var first = Build().Build(events).Activities[0];

        Assert.True(first.DurationSeconds <= 600);
    }

    [Fact]
    public void NormalHeartbeatPacedWork_IsNotTruncated()
    {
        // The cap must not bite during genuine engaged work, where heartbeats
        // keep arriving well inside the limit.
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code"),
            Marker(300, EventKind.Heartbeat),
            Focus(300, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(600, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(900, "chrome", "Docs - Google Chrome")
        };

        var devlog = Build().Build(events).Activities.First(a => a.Context == "devlog");

        // Three chained 5-minute spans merge into one continuous 15 minutes.
        Assert.Equal(900, devlog.DurationSeconds);
    }

    [Fact]
    public void SpanNeverCrossesSuspend()
    {
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code"),
            Marker(30, EventKind.Suspend),
            Marker(20_000, EventKind.Resume),
            Focus(20_001, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(20_100, "chrome", "x - Google Chrome")
        };

        Assert.All(Build().Build(events).Activities, a => Assert.True(a.DurationSeconds < 600));
    }

    // -------------------------------------------------------------------- merging

    [Fact]
    public void ConsecutiveSameProjectRows_MergeIntoOneActivity()
    {
        // Twelve file switches inside one refactor. The project never changed, so
        // this is one activity - not twelve.
        var events = new List<RawEvent>();
        for (var i = 0; i < 12; i++)
        {
            events.Add(Focus(i * 60, "Code", $"file{i}.cs - devlog - Visual Studio Code"));
        }

        events.Add(Focus(12 * 60, "chrome", "Docs - Google Chrome"));
        events.Add(Focus(13 * 60, "chrome", "Docs - Google Chrome"));

        var activities = Build().Build(events).Activities;
        var devlog = activities.Where(a => a.Context == "devlog").ToList();

        Assert.Single(devlog);
        Assert.Equal(11, devlog[0].TitleChanges);
        Assert.Equal(720, devlog[0].DurationSeconds);
    }

    [Fact]
    public void SubThresholdBlip_IsAbsorbedByNeighbour()
    {
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(300, "explorer", "Downloads - File Explorer"),   // 2s flicker
            Focus(302, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(600, "chrome", "x - Google Chrome")
        };

        var activities = Build().Build(events).Activities;

        Assert.DoesNotContain(activities, a => a.ProcessName == "explorer");

        // And having absorbed it, the two devlog halves become one span.
        Assert.Single(activities, a => a.Context == "devlog");
    }

    /// <summary>
    /// Absorbing one blip can make two same-key neighbours adjacent, which must
    /// then merge, which can expose another blip. A single pass leaves the
    /// timeline half-collapsed.
    /// </summary>
    [Fact]
    public void CascadingBlips_CollapseCompletely()
    {
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(100, "explorer", "x - File Explorer"),
            Focus(102, "Code", "b.cs - devlog - Visual Studio Code"),
            Focus(104, "explorer", "y - File Explorer"),
            Focus(106, "Code", "c.cs - devlog - Visual Studio Code"),
            Focus(400, "chrome", "z - Google Chrome")
        };

        var activities = Build().Build(events).Activities;

        Assert.Single(activities, a => a.Context == "devlog");
        Assert.DoesNotContain(activities, a => a.ProcessName == "explorer");
    }

    // ----------------------------------------------------------------- engagement

    [Fact]
    public void ReadingDocumentation_IsConsuming_NotIdle()
    {
        // The finding that reshaped this design: scrolling is input, so a real
        // reading session reports idle=0 and looks identical to typing. Category
        // is what separates them - never idle time.
        var events = new[]
        {
            Focus(0, "chrome", "Understanding MCP servers - Model Context Protocol - Google Chrome"),
            Focus(600, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(700, "Code", "b.cs - devlog - Visual Studio Code")
        };

        var rules = new[]
        {
            new ClassificationRule
            {
                Scope = RuleScope.Site,
                Site = "Model Context Protocol",
                Category = ActivityCategory.Learning,
                SourceName = "manual"
            }
        };

        var builder = new ActivityBuilder(
            new DerivationOptions(), new Classifier(rules), new NoiseFilter());

        var reading = builder.Build(events).Activities.First(a => a.ProcessName == "chrome");

        Assert.Equal(ActivityCategory.Learning, reading.Category);
        Assert.Equal(Engagement.Consuming, reading.Engagement);
    }

    [Fact]
    public void IdleBeyondThreshold_IsIdle()
    {
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code", idle: 900),
            Focus(600, "Code", "b.cs - devlog - Visual Studio Code")
        };

        Assert.Equal(Engagement.Idle, Build().Build(events).Activities[0].Engagement);
    }

    // ---------------------------------------------------------------------- noise

    [Fact]
    public void NoiseRows_AreDroppedAndNeighboursMerge()
    {
        var events = new[]
        {
            Focus(0, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(300, "ShellExperienceHost", "New notification"),
            Focus(305, "Code", "a.cs - devlog - Visual Studio Code"),
            Focus(900, "chrome", "x - Google Chrome")
        };

        var activities = Build().Build(events).Activities;

        Assert.DoesNotContain(activities, a => a.ProcessName == "ShellExperienceHost");
        Assert.Single(activities, a => a.Context == "devlog");
    }

    [Fact]
    public void FinalEvent_IsDroppedRatherThanLeftOpen()
    {
        // The last row has no successor, so its extent is unknown. Guessing one is
        // exactly how an unbounded span gets created.
        var events = new[] { Focus(0, "Code", "a.cs - devlog - Visual Studio Code") };

        Assert.Empty(Build().Build(events).Activities);
    }
}
