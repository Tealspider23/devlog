using System.Diagnostics;
using System.Threading.Channels;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Devlog.Infrastructure.Windows;

/// <summary>
/// Event-driven foreground observation.
/// <para>
/// The OS pushes focus changes to us via <c>SetWinEventHook</c> rather than us
/// asking 28,800 times a day. That is both cheaper — near-zero CPU at rest — and
/// <em>more accurate</em>: a two-second visit to an app falls between polls but
/// cannot slip past a hook.
/// </para>
/// <para>
/// A slow timer still runs alongside, but only so the consumer gets a regular
/// tick to notice idle transitions and due heartbeats. It is not how focus
/// changes are detected.
/// </para>
/// </summary>
public sealed class WinEventForegroundWatcher : IActivityWatcher
{
    private readonly DevlogOptions _options;
    private readonly ILogger<WinEventForegroundWatcher> _logger;
    private readonly Channel<ForegroundSnapshot> _channel;

    // CRITICAL: this field is the only thing keeping the delegate alive.
    // SetWinEventHook stores a raw function pointer; if the delegate is collected
    // the hook stops firing silently — no error, just a collector that quietly
    // records nothing. Do not inline this into the SetWinEventHook call.
    private NativeMethods.WinEventProc? _callback;

    private nint _foregroundHook;
    private nint _nameChangeHook;
    private Timer? _idleTimer;
    private bool _started;

    public WinEventForegroundWatcher(DevlogOptions options, ILogger<WinEventForegroundWatcher> logger)
    {
        _options = options;
        _logger = logger;

        // Bounded and drop-oldest: if the consumer ever stalls we would rather
        // lose a stale sample than grow unboundedly. Focus changes are frequent
        // enough that the next one repairs the picture.
        _channel = Channel.CreateBounded<ForegroundSnapshot>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _callback = OnWinEvent;

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        // Catches title changes without a focus change — switching files inside
        // VS Code, or navigating to a new page in an already-focused browser.
        _nameChangeHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_NAMECHANGE,
            NativeMethods.EVENT_OBJECT_NAMECHANGE,
            nint.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_foregroundHook == nint.Zero)
        {
            _logger.LogError(
                "SetWinEventHook failed for EVENT_SYSTEM_FOREGROUND. No message loop on this thread?");
        }

        _idleTimer = new Timer(
            _ => Publish(Sample()),
            state: null,
            dueTime: TimeSpan.Zero,
            period: _options.IdlePollInterval);

        _logger.LogInformation(
            "Foreground watcher started (event-driven; idle tick every {Interval}s)",
            _options.IdlePollInterval.TotalSeconds);
    }

    /// <summary>
    /// Runs on the OS's callback thread. Does the minimum possible and returns —
    /// blocking here would stall the desktop's event delivery.
    /// </summary>
    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        // NAMECHANGE fires for controls and menu items too. Only whole windows,
        // and only the one currently in the foreground, are interesting.
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        if (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE
            && hWnd != NativeMethods.GetForegroundWindow())
        {
            return;
        }

        Publish(Sample());
    }

    private void Publish(ForegroundSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    public ForegroundSnapshot Sample()
    {
        var idleSeconds = NativeMethods.GetIdleSeconds();
        var hWnd = NativeMethods.GetForegroundWindow();

        if (hWnd == nint.Zero)
        {
            // Happens during lock, on the secure desktop, or briefly between
            // windows closing and the next gaining focus.
            return new ForegroundSnapshot(null, null, null, idleSeconds);
        }

        var title = NativeMethods.GetWindowTitle(hWnd);
        string? processName = null;
        string? exePath = null;

        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);

        if (pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;

                try
                {
                    // Throws Access Denied for elevated processes when we are not
                    // elevated. Best-effort by design — ProcessName still resolves,
                    // and the exe path is a nice-to-have.
                    exePath = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    exePath = null;
                }
            }
            catch (ArgumentException)
            {
                // Process exited between the PID lookup and this call.
            }
            catch (InvalidOperationException)
            {
                // Same race, different surface.
            }
        }

        return new ForegroundSnapshot(processName, title, exePath, idleSeconds);
    }

    public IAsyncEnumerable<ForegroundSnapshot> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);

    public ValueTask DisposeAsync()
    {
        if (_idleTimer is not null)
        {
            _idleTimer.Dispose();
            _idleTimer = null;
        }

        if (_foregroundHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = nint.Zero;
        }

        if (_nameChangeHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_nameChangeHook);
            _nameChangeHook = nint.Zero;
        }

        // Only safe to release once the hooks are gone.
        _callback = null;
        _channel.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }
}
