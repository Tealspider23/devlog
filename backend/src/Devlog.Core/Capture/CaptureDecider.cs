using Devlog.Core.Domain;

namespace Devlog.Core.Capture;

public enum CaptureAction
{
    /// <summary>Nothing changed and no heartbeat is due. Write nothing.</summary>
    Skip = 0,

    /// <summary>The attended context changed. Write a <see cref="EventKind.FocusChange"/>.</summary>
    RecordFocusChange = 1,

    /// <summary>Context unchanged but the heartbeat interval elapsed while engaged.</summary>
    RecordHeartbeat = 2
}

/// <summary>
/// The rules that keep the database small enough to stay useful.
/// <para>
/// Pure and dependency-free — no clock, no OS, no database — because these are
/// the numbers most likely to be retuned once real data exists.
/// </para>
/// </summary>
public static class CaptureDecider
{
    /// <summary>
    /// Should this snapshot become a row?
    /// </summary>
    /// <param name="lastRecorded">
    /// The last snapshot actually <em>written</em> — not the last one sampled. Null on first run.
    /// </param>
    /// <param name="current">The snapshot under consideration.</param>
    /// <param name="lastRecordedAtUtc">When <paramref name="lastRecorded"/> was written.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="heartbeatInterval">
    /// How long an unchanging foreground may go unrecorded. Bounds the span an
    /// unclean shutdown can leave open. Zero or negative disables heartbeats.
    /// </param>
    /// <param name="suppressHeartbeatAfterIdle">
    /// Once idle exceeds this, stop emitting heartbeats entirely. One row saying
    /// "went idle" carries everything; 96 rows repeating it overnight carry nothing.
    /// This is what makes weekends and overnight nearly free.
    /// </param>
    public static CaptureAction Decide(
        ForegroundSnapshot? lastRecorded,
        ForegroundSnapshot current,
        DateTimeOffset lastRecordedAtUtc,
        DateTimeOffset nowUtc,
        TimeSpan heartbeatInterval,
        TimeSpan suppressHeartbeatAfterIdle)
    {
        // First observation of this run — establish a baseline.
        if (lastRecorded is null)
        {
            return CaptureAction.RecordFocusChange;
        }

        if (!current.IsSameContextAs(lastRecorded))
        {
            return CaptureAction.RecordFocusChange;
        }

        // Same context from here on. Idle seconds will have drifted, but drift
        // alone is not a transition or every sample would qualify.
        if (heartbeatInterval <= TimeSpan.Zero)
        {
            return CaptureAction.Skip;
        }

        if (suppressHeartbeatAfterIdle > TimeSpan.Zero
            && current.IdleSeconds >= suppressHeartbeatAfterIdle.TotalSeconds)
        {
            return CaptureAction.Skip;
        }

        return nowUtc - lastRecordedAtUtc >= heartbeatInterval
            ? CaptureAction.RecordHeartbeat
            : CaptureAction.Skip;
    }

    /// <summary>
    /// Has a pending focus change survived the debounce window?
    /// <para>
    /// Focus that is superseded within ~1.5s was a flicker — an alt-tab passing
    /// through, or a notification stealing focus and handing it back. Nothing
    /// meaningful happens in under a second, and discarding these costs no signal
    /// while removing a steady trickle of worthless rows.
    /// </para>
    /// <para>
    /// Note this is capture-time noise removal. The coarser 8s blip-merging at
    /// derivation time is a separate, retunable stage.
    /// </para>
    /// </summary>
    public static bool HasSettled(
        DateTimeOffset pendingSinceUtc,
        DateTimeOffset nowUtc,
        TimeSpan debounce) =>
        debounce <= TimeSpan.Zero || nowUtc - pendingSinceUtc >= debounce;
}
