using Devlog.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Devlog.Infrastructure.Windows;

/// <summary>
/// Lock, unlock, sleep and resume.
/// <para>
/// These matter more than they look. Without them, locking the machine for a
/// two-hour meeting leaves the last focus event open and that time is silently
/// counted as work. The lock row is what stops devlog from flattering you.
/// </para>
/// <para>
/// Uses managed <see cref="SystemEvents"/> rather than P/Invoke — but note it has
/// the same requirement as the WinEvent hooks: a running message loop, which the
/// tray icon provides.
/// </para>
/// </summary>
public sealed class SessionSwitchMonitor(ILogger<SessionSwitchMonitor> logger) : IDisposable
{
    private bool _started;

    /// <summary>Raised with <see cref="EventKind.Lock"/>, <c>Unlock</c>, <c>Suspend</c> or <c>Resume</c>.</summary>
    public event Action<EventKind>? StateChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        logger.LogInformation("Session switch monitor started");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            // SessionLock covers Win+L; ConsoleDisconnect covers switch-user and
            // RDP taking the console away. Both mean "not at this desk".
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                StateChanged?.Invoke(EventKind.Lock);
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                StateChanged?.Invoke(EventKind.Unlock);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                StateChanged?.Invoke(EventKind.Suspend);
                break;

            case PowerModes.Resume:
                StateChanged?.Invoke(EventKind.Resume);
                break;
        }
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _started = false;
    }
}
