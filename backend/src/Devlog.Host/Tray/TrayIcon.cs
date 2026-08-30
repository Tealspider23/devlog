using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Devlog.Infrastructure.Persistence;

namespace Devlog.Host.Tray;

/// <summary>
/// The tray icon, and — less obviously — the reason the rest of this works.
/// <para>
/// <c>SetWinEventHook</c> and <see cref="Microsoft.Win32.SystemEvents"/> both
/// deliver through the Windows message queue, so something on this thread has to
/// pump messages. <see cref="Application.Run(ApplicationContext)"/> is that pump.
/// Without a message loop the hooks install successfully and then never fire.
/// </para>
/// </summary>
public sealed class TrayIconContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly PauseController _pause;
    private readonly string _databasePath;
    private readonly Action _requestExit;

    public TrayIconContext(
        PauseController pause,
        ISqliteConnectionFactory factory,
        Action requestExit)
    {
        _pause = pause;
        _databasePath = factory.DatabasePath;
        _requestExit = requestExit;

        _pauseItem = new ToolStripMenuItem("Pause recording", null, (_, _) => TogglePause());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open data folder", null, (_, _) => OpenDataFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "devlog — recording",
            Visible = true,
            ContextMenuStrip = menu
        };

        _pause.Changed += OnPauseChanged;
    }

    private void TogglePause() => _pause.Toggle();

    private void OnPauseChanged(bool paused)
    {
        // Fired from whichever thread flipped the flag; marshal to the UI thread.
        if (_icon.ContextMenuStrip?.InvokeRequired == true)
        {
            _icon.ContextMenuStrip.BeginInvoke(() => OnPauseChanged(paused));
            return;
        }

        _pauseItem.Text = paused ? "Resume recording" : "Pause recording";
        _icon.Text = paused ? "devlog — paused" : "devlog — recording";
    }

    private void OpenDataFolder()
    {
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
    }

    private void Exit()
    {
        _icon.Visible = false;
        _requestExit();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pause.Changed -= OnPauseChanged;
            _icon.Visible = false;
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
