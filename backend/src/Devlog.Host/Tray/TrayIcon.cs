using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Devlog.Core.Configuration;
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
    private readonly string _dashboardUrl;
    private readonly Action _requestExit;

    public TrayIconContext(
        PauseController pause,
        ISqliteConnectionFactory factory,
        ApiOptions api,
        Action requestExit)
    {
        _pause = pause;
        _databasePath = factory.DatabasePath;
        _requestExit = requestExit;

        // Built from the same options Kestrel binds, so a changed port cannot
        // leave the menu pointing somewhere nothing is listening.
        _dashboardUrl = $"http://127.0.0.1:{api.Port}";

        _pauseItem = new ToolStripMenuItem("Pause recording", null, (_, _) => TogglePause());

        var openDashboard = new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenDashboard());

        var menu = new ContextMenuStrip();
        menu.Items.Add(openDashboard);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
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

        // The default gesture for a tray app, and the reason the menu item is
        // first: opening the dashboard is what you want from this icon, and
        // pausing is what you rarely want.
        _icon.DoubleClick += (_, _) => OpenDashboard();

        menu.Items[0].Font = new Font(menu.Font, FontStyle.Bold);

        _pause.Changed += OnPauseChanged;
    }

    /// <summary>
    /// Opens the dashboard in the default browser.
    /// <para>
    /// Deliberately a separate process, not an embedded window: closing a
    /// browser cannot stop the collector, which is the property an in-process
    /// window would have to be careful to preserve. Capture stopping because a
    /// window was closed is what lost a day of data on 2026-09-01.
    /// </para>
    /// </summary>
    private void OpenDashboard() =>
        Process.Start(new ProcessStartInfo(_dashboardUrl) { UseShellExecute = true });

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
