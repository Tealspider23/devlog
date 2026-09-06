using Devlog.Core.Capture;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class CaptureDeciderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SuppressAfterIdle = TimeSpan.FromSeconds(300);

    private static ForegroundSnapshot Snap(string process, string title, int idle = 0) =>
        new(process, title, null, idle);

    [Fact]
    public void FirstObservation_IsAlwaysRecorded()
    {
        var action = CaptureDecider.Decide(
            lastRecorded: null,
            Snap("Code", "auth.cs - devlog - Visual Studio Code"),
            T0, T0, Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.RecordFocusChange, action);
    }

    [Fact]
    public void ContextChange_IsRecorded()
    {
        var action = CaptureDecider.Decide(
            Snap("Code", "auth.cs - devlog - Visual Studio Code"),
            Snap("chrome", "SQLite WAL mode - Google Chrome"),
            T0, T0.AddSeconds(30), Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.RecordFocusChange, action);
    }

    [Fact]
    public void IdleDriftAlone_IsNotAChange()
    {
        // This is the rule that keeps the table small. Idle seconds move on every
        // single sample; if that counted as a transition nothing would ever be
        // skipped and we would be back to ~28,000 rows a day.
        var action = CaptureDecider.Decide(
            Snap("Code", "auth.cs - devlog - Visual Studio Code", idle: 3),
            Snap("Code", "auth.cs - devlog - Visual Studio Code", idle: 47),
            T0, T0.AddSeconds(44), Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.Skip, action);
    }

    [Fact]
    public void CounterChurn_IsNotAChange()
    {
        var action = CaptureDecider.Decide(
            Snap("chrome", "Inbox (14) - Gmail - Google Chrome"),
            Snap("chrome", "Inbox (15) - Gmail - Google Chrome"),
            T0, T0.AddSeconds(5), Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.Skip, action);
    }

    [Fact]
    public void Heartbeat_FiresWhenIntervalElapsedAndEngaged()
    {
        var action = CaptureDecider.Decide(
            Snap("Code", "auth.cs", idle: 10),
            Snap("Code", "auth.cs", idle: 12),
            T0, T0.Add(Heartbeat), Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.RecordHeartbeat, action);
    }

    [Fact]
    public void Heartbeat_IsSuppressedOnceIdle()
    {
        // The rule that makes overnight and weekends nearly free: one row saying
        // "went idle" carries everything, 96 rows repeating it carry nothing.
        var action = CaptureDecider.Decide(
            Snap("Code", "auth.cs", idle: 400),
            Snap("Code", "auth.cs", idle: 460),
            T0, T0.Add(Heartbeat), Heartbeat, SuppressAfterIdle);

        Assert.Equal(CaptureAction.Skip, action);
    }

    [Fact]
    public void Heartbeat_CanBeDisabled()
    {
        var action = CaptureDecider.Decide(
            Snap("Code", "auth.cs"),
            Snap("Code", "auth.cs"),
            T0, T0.AddHours(3), TimeSpan.Zero, SuppressAfterIdle);

        Assert.Equal(CaptureAction.Skip, action);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1499, false)]
    [InlineData(1500, true)]
    [InlineData(5000, true)]
    public void Debounce_RequiresTheChangeToSurvive(int elapsedMs, bool expected)
    {
        var settled = CaptureDecider.HasSettled(
            T0, T0.AddMilliseconds(elapsedMs), TimeSpan.FromMilliseconds(1500));

        Assert.Equal(expected, settled);
    }

    [Fact]
    public void Debounce_OfZero_SettlesImmediately()
    {
        Assert.True(CaptureDecider.HasSettled(T0, T0, TimeSpan.Zero));
    }
}
