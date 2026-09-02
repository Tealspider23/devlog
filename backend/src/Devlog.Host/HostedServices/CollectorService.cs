using Devlog.Core.Abstractions;
using Devlog.Core.Capture;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Host.Tray;
using Devlog.Infrastructure.Windows;

namespace Devlog.Host.HostedServices;

/// <summary>
/// Turns pushed foreground observations into the smallest set of rows that
/// still describes the day faithfully.
/// <para>
/// Two loops. One drains the watcher's channel into a single pending slot; the
/// other wakes on a short tick to decide whether that pending observation has
/// settled, whether a heartbeat is due, and whether the buffer should flush.
/// The evaluation tick does no Win32 work at all — it only inspects memory.
/// </para>
/// </summary>
public sealed class CollectorService(
    IActivityWatcher watcher,
    IEventStore store,
    SessionSwitchMonitor sessionMonitor,
    PauseController pause,
    DevlogOptions options,
    ILogger<CollectorService> logger) : BackgroundService
{
    /// <summary>
    /// Substituted for the process name when the foreground is an excluded app.
    /// <para>
    /// Recording a placeholder rather than nothing at all is deliberate. If we
    /// simply skipped, a 30-minute detour into an excluded application would be
    /// invisible and that time would be silently attributed to whatever you were
    /// doing beforehand — inflating your own numbers. The marker keeps the gap
    /// honest while revealing nothing about what was on screen.
    /// </para>
    /// </summary>
    private const string ExcludedMarker = PrivacyMarker.Excluded;

    private readonly ExclusionRules _exclusions = new(
        options.ExcludedProcesses,
        options.ExcludedTitlePatterns);

    private readonly List<RawEvent> _buffer = [];
    private readonly Lock _gate = new();

    private ForegroundSnapshot? _pending;
    private DateTimeOffset _pendingSince;

    private ForegroundSnapshot? _lastRecorded;
    private DateTimeOffset _lastRecordedAt;
    private DateTimeOffset _lastFlush;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribed BEFORE the first await, so it is guaranteed to be in place
        // by the time host.Start() returns and Program installs the hooks.
        // Everything after the await runs on a thread-pool thread.
        sessionMonitor.StateChanged += OnSessionStateChanged;

        await ReportPreviousRunAsync(stoppingToken).ConfigureAwait(false);

        // NOTE: watcher.Start() and sessionMonitor.Start() are deliberately NOT
        // called here. SetWinEventHook delivers callbacks to the thread that
        // installed the hook, and that thread must pump messages. This method is
        // running on the thread pool by now, so installing here produces a hook
        // that is created successfully and then never fires — leaving the
        // collector silently running on the idle timer alone.
        // Program.RunTray installs them on the UI thread instead.

        _lastFlush = DateTimeOffset.UtcNow;
        Enqueue(RawEvent.From(EventKind.CollectorStart, DateTimeOffset.UtcNow, watcher.Sample()));

        var drain = DrainAsync(stoppingToken);
        var evaluate = EvaluateLoopAsync(stoppingToken);

        try
        {
            await Task.WhenAll(drain, evaluate).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            sessionMonitor.StateChanged -= OnSessionStateChanged;

            // CollectorStop is what lets the next run tell a clean exit from a
            // crash. Written with CancellationToken.None because stoppingToken
            // is already cancelled by the time we get here.
            Enqueue(RawEvent.From(EventKind.CollectorStop, DateTimeOffset.UtcNow));
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Consumes pushed observations. Cheap — it only updates a slot.</summary>
    private async Task DrainAsync(CancellationToken ct)
    {
        await foreach (var snapshot in watcher.ReadAllAsync(ct).ConfigureAwait(false))
        {
            lock (_gate)
            {
                // Reset the debounce clock only when the *context* changed.
                // Idle seconds drift on every sample and must not count as
                // movement, or nothing would ever settle.
                if (_pending is null || !snapshot.IsSameContextAs(_pending))
                {
                    _pendingSince = DateTimeOffset.UtcNow;
                }

                _pending = snapshot;
            }
        }
    }

    private async Task EvaluateLoopAsync(CancellationToken ct)
    {
        // Fast enough to honour a 1.5s debounce, slow enough to be free. This
        // tick performs no interop; it just looks at fields.
        var period = TimeSpan.FromMilliseconds(Math.Clamp(options.DebounceMilliseconds / 3.0, 250, 1000));
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            Evaluate(DateTimeOffset.UtcNow);

            if (DateTimeOffset.UtcNow - _lastFlush >= options.FlushInterval)
            {
                await FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private void Evaluate(DateTimeOffset now)
    {
        ForegroundSnapshot? candidate;
        DateTimeOffset since;

        lock (_gate)
        {
            candidate = _pending;
            since = _pendingSince;
        }

        if (candidate is null || pause.IsPaused)
        {
            return;
        }

        // Excluded context is replaced before it reaches any decision or buffer,
        // so the raw title never exists outside this method.
        if (_exclusions.IsExcluded(candidate))
        {
            candidate = candidate with
            {
                ProcessName = ExcludedMarker,
                WindowTitle = null,
                ExePath = null
            };
        }

        if (!CaptureDecider.HasSettled(since, now, options.Debounce))
        {
            return;
        }

        var action = CaptureDecider.Decide(
            _lastRecorded,
            candidate,
            _lastRecordedAt,
            now,
            options.HeartbeatInterval,
            options.SuppressHeartbeatAfterIdle);

        if (action == CaptureAction.Skip)
        {
            return;
        }

        var kind = action == CaptureAction.RecordHeartbeat ? EventKind.Heartbeat : EventKind.FocusChange;
        Enqueue(RawEvent.From(kind, now, candidate));

        _lastRecorded = candidate;
        _lastRecordedAt = now;
    }

    /// <summary>
    /// Lock, unlock, suspend and resume bypass the debounce entirely — they are
    /// discrete facts, not something that needs to settle.
    /// </summary>
    private void OnSessionStateChanged(EventKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        Enqueue(RawEvent.From(kind, now));

        if (kind is EventKind.Lock or EventKind.Suspend)
        {
            // Drop the baseline so the first observation after returning is
            // always recorded, even if you come back to the same window.
            lock (_gate)
            {
                _pending = null;
            }

            _lastRecorded = null;
        }

        _lastRecordedAt = now;
    }

    private void Enqueue(RawEvent e)
    {
        lock (_gate)
        {
            _buffer.Add(e);
        }
    }

    /// <summary>Writes the buffer in one transaction — one fsync for many rows.</summary>
    private async Task FlushAsync(CancellationToken ct)
    {
        RawEvent[] batch;

        lock (_gate)
        {
            if (_buffer.Count == 0)
            {
                _lastFlush = DateTimeOffset.UtcNow;
                return;
            }

            batch = [.. _buffer];
            _buffer.Clear();
        }

        try
        {
            await store.AppendAsync(batch, ct).ConfigureAwait(false);
            _lastFlush = DateTimeOffset.UtcNow;
            logger.LogDebug("Flushed {Count} events", batch.Length);
        }
        catch (Exception ex)
        {
            // Put them back rather than losing the day's tail to a transient
            // lock. Ordering is preserved because we prepend.
            lock (_gate)
            {
                _buffer.InsertRange(0, batch);
            }

            logger.LogError(ex, "Flush failed; {Count} events returned to buffer", batch.Length);
        }
    }

    private async Task ReportPreviousRunAsync(CancellationToken ct)
    {
        var latest = await store.GetLatestAsync(ct).ConfigureAwait(false);

        if (latest is null)
        {
            logger.LogInformation("No prior events — this is a fresh database");
            return;
        }

        if (latest.Kind != EventKind.CollectorStop)
        {
            logger.LogWarning(
                "Previous run did not stop cleanly (last event was {Kind} at {At:u}). "
                + "The span after it is unbounded and should be treated as unknown, not as work.",
                latest.Kind,
                latest.Timestamp);
        }
    }
}
