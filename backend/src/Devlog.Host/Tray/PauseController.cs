namespace Devlog.Host.Tray;

/// <summary>
/// The off switch. Shared between the tray menu (which sets it) and the
/// collector (which reads it on every evaluation).
/// <para>
/// While paused nothing is captured at all — as opposed to captured and
/// discarded later. A privacy control that only promises not to look at data it
/// already stored is not a privacy control.
/// </para>
/// </summary>
public sealed class PauseController
{
    private volatile bool _paused;

    public bool IsPaused => _paused;

    public event Action<bool>? Changed;

    public void Pause() => Set(true);

    public void Resume() => Set(false);

    public bool Toggle()
    {
        Set(!_paused);
        return _paused;
    }

    private void Set(bool value)
    {
        if (_paused == value)
        {
            return;
        }

        _paused = value;
        Changed?.Invoke(value);
    }
}
