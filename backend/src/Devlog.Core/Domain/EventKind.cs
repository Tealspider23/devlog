namespace Devlog.Core.Domain;

/// <summary>
/// What happened at the moment an event was recorded.
/// Stored as an integer in <c>raw_event.kind</c> — values are part of the
/// on-disk contract and must never be renumbered.
/// </summary>
public enum EventKind
{
    /// <summary>The foreground window or its title changed.</summary>
    FocusChange = 0,

    /// <summary>
    /// Periodic keepalive while nothing changed. Bounds the damage from an
    /// unclean shutdown — without it a crash leaves an unbounded open span.
    /// </summary>
    Heartbeat = 1,

    /// <summary>Session locked (Win+L, screensaver, switch user).</summary>
    Lock = 2,

    /// <summary>Session unlocked.</summary>
    Unlock = 3,

    /// <summary>Machine entering sleep/hibernate.</summary>
    Suspend = 4,

    /// <summary>Machine resumed from sleep/hibernate.</summary>
    Resume = 5,

    /// <summary>Collector started. Marks the beginning of a capture window.</summary>
    CollectorStart = 6,

    /// <summary>Collector stopped cleanly. Its absence implies a crash.</summary>
    CollectorStop = 7
}
