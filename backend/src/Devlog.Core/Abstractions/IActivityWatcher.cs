using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Produces foreground observations. The abstraction exists so the collector
/// does not care whether snapshots arrive because the OS pushed an event or
/// because something polled — swapping mechanisms stays contained to one class.
/// </summary>
public interface IActivityWatcher : IAsyncDisposable
{
    /// <summary>
    /// Begins observing. Snapshots arrive via <see cref="ReadAllAsync"/>.
    /// </summary>
    void Start();

    /// <summary>
    /// Streams observations until cancelled. Includes both OS-pushed focus
    /// changes and periodic idle-timer samples, since the consumer needs a
    /// regular tick to notice idle transitions and due heartbeats.
    /// </summary>
    IAsyncEnumerable<ForegroundSnapshot> ReadAllAsync(CancellationToken ct = default);

    /// <summary>Takes an observation right now, e.g. to stamp a lock or shutdown event.</summary>
    ForegroundSnapshot Sample();
}
