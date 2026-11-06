using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AirPlaySender.App;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainWindow _window;

    public event EventHandler? ExitRequested;

    public TrayIconService(MainWindow window)
    {
        _window = window;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AirPlay Sender",
            Visible = false
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show AirPlay Sender", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(1500, "AirPlay Sender",
            "Running in the notification area. Right-click for options.", Forms.ToolTipIcon.Info);
    }

    public void ShowWindow()
    {
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
