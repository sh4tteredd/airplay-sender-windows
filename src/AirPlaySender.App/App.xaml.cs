using System.Threading;
using System.Windows;
using AirPlaySender.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace AirPlaySender.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private TrayIconService? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, "AirPlaySender.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("AirPlay Sender is already running. Look for it in the notification area.",
                "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logger.Instance.Error("Unhandled exception", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown error"));
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Instance.Error("Dispatcher exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _window = new MainWindow();
        MainWindow = _window;
        _window.Show();

        _tray = new TrayIconService(_window);
        _tray.ExitRequested += (_, _) =>
        {
            _window.AllowClose = true;
            _window.Close();
            Shutdown();
        };
        _tray.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _window?.ShutdownServices();
        _instanceMutex?.Dispose();
        Logger.Instance.Dispose();
        base.OnExit(e);
    }
}
