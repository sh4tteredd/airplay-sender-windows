using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AirPlaySender.Core.Models;
using AirPlaySender.Core.Services;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace AirPlaySender.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AudioDeviceService _audioDevices = new();
    private readonly DefaultDeviceService _defaultDevices = new();
    private readonly DiscoveryService _discovery;
    private readonly ObservableCollection<ReceiverDevice> _receivers = new();
    private readonly ObservableCollection<AudioDevice> _captureDevices = new();
    private readonly DispatcherTimer _statusTimer;

    private AirPlaySession? _session;
    private AudioCaptureService? _capture;
    private NAudio.CoreAudioApi.MMDevice? _mutedDevice;
    private bool _previousMute;
    private bool _stopping;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        RestoreVirtualCableAfterCrash();
        RestoreLocalMuteAfterCrash();
        _discovery = new DiscoveryService();
        _discovery.DeviceFound += Discovery_DeviceFound;
        _discovery.DiscoveryError += (_, error) => AppendLog($"Discovery error: {error}");

        ReceiversList.ItemsSource = _receivers;
        CaptureDeviceCombo.ItemsSource = _captureDevices;
        CaptureModeCombo.ItemsSource = new[]
        {
            "Default Windows output",
            "Specific audio endpoint",
            "Virtual cable (VB-CABLE / VAC)"
        };
        CaptureModeCombo.SelectedIndex = Math.Clamp((int)_settings.CaptureMode, 0, 2);
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 100);
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
        MuteLocalCheckBox.IsChecked = _settings.MuteLocalOutput;
        PasswordInput.Password = _settings.Password ?? string.Empty;

        Logger.Instance.LineWritten += Logger_LineWritten;

        LoadAudioDevices();
        UpdateCaptureModeUi();
        _discovery.Start();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statusTimer.Tick += (_, _) => UpdateStats();
        _statusTimer.Start();

        SetStatus(SessionState.Idle, "Select a receiver and press Start.");
        AppendLog($"Ready. Log file: {Logger.Instance.LogPath}");
    }

    public bool AllowClose
    {
        get => _allowClose;
        set => _allowClose = value;
    }

    private void LoadAudioDevices()
    {
        var previousId = (CaptureDeviceCombo.SelectedItem as AudioDevice)?.Id ?? _settings.CaptureDeviceId;
        _captureDevices.Clear();
        foreach (var device in _audioDevices.GetRenderDevices().Concat(_audioDevices.GetCaptureDevices()))
            _captureDevices.Add(device);

        var selected = _captureDevices.FirstOrDefault(d =>
            string.Equals(d.Id, previousId, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) CaptureDeviceCombo.SelectedItem = selected;
    }

    private void UpdateCaptureModeUi()
    {
        var mode = (CaptureMode)Math.Clamp(CaptureModeCombo.SelectedIndex, 0, 2);
        CaptureDeviceCombo.IsEnabled = mode is CaptureMode.SpecificEndpoint or CaptureMode.VirtualCable && _session is null;
    }

    private void Discovery_DeviceFound(object? sender, ReceiverDevice device)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var existing = _receivers.FirstOrDefault(r =>
                string.Equals(r.Endpoint, device.Endpoint, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _receivers.Add(device);
            }
            else
            {
                var index = _receivers.IndexOf(existing);
                _receivers[index] = device;
            }

            if (ReceiversList.SelectedItem is null
                && string.Equals(device.Host, _settings.LastReceiverHost, StringComparison.OrdinalIgnoreCase))
            {
                ReceiversList.SelectedItem = device;
            }

            ReceiverCountText.Text = $"{_receivers.Count} found";
        });
    }

    private void ReceiversList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReceiversList.SelectedItem is ReceiverDevice receiver)
        {
            SelectedReceiverText.Text = receiver.ToString();
            PasswordInput.IsEnabled = receiver.RequiresPassword;
        }
        else
        {
            SelectedReceiverText.Text = "None selected";
            PasswordInput.IsEnabled = true;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAudioDevices();
        _discovery.Refresh();
        AppendLog("Refreshing receiver and audio device lists...");
    }

    private void AddManualButton_Click(object sender, RoutedEventArgs e)
    {
        var text = ManualHostInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var host = text;
        var port = 7000;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var parsedPort))
        {
            host = text[..colon];
            port = parsedPort;
        }

        var device = new ReceiverDevice
        {
            Name = host,
            Host = host,
            Port = port
        };
        var existing = _receivers.FirstOrDefault(r =>
            string.Equals(r.Endpoint, device.Endpoint, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _receivers.Add(device);
            ReceiverCountText.Text = $"{_receivers.Count} found";
        }
        ReceiversList.SelectedItem = existing ?? device;
        AppendLog($"Added manual receiver {device.Endpoint}.");
    }

    private void CaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCaptureModeUi();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null) return;

        if (ReceiversList.SelectedItem is not ReceiverDevice receiver)
        {
            MessageBox.Show("Select an AirPlay receiver first.", "AirPlay Sender",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = (CaptureMode)Math.Clamp(CaptureModeCombo.SelectedIndex, 0, 2);
        AudioDevice? captureDevice;
        if (mode == CaptureMode.DefaultOutput)
        {
            captureDevice = _audioDevices.GetDefaultRenderDevice();
        }
        else
        {
            var selected = CaptureDeviceCombo.SelectedItem as AudioDevice;
            if (selected is null)
            {
                MessageBox.Show("Select an audio endpoint first.", "AirPlay Sender",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (mode == CaptureMode.VirtualCable)
            {
                var pair = _audioDevices.FindVirtualCablePair(selected);
                if (pair is null)
                {
                    MessageBox.Show(
                        "Could not find the matching playback side of this virtual cable. " +
                        "For VB-CABLE, select either 'CABLE Input' or 'CABLE Output'.",
                        "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var previousDefault = _defaultDevices.GetDefaultRenderId();
                if (string.IsNullOrWhiteSpace(previousDefault))
                {
                    MessageBox.Show("Could not read the current Windows default playback device.",
                        "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_defaultDevices.SetDefaultRender(pair.Value.Render.Id))
                {
                    MessageBox.Show("Could not switch Windows audio to the virtual cable.",
                        "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _settings.VirtualCableActive = true;
                _settings.VirtualCablePreviousDefaultId = previousDefault;
                _settings.VirtualCableRenderDeviceId = pair.Value.Render.Id;
                _settings.VirtualCableCaptureDeviceId = pair.Value.Capture.Id;
                SettingsStore.Save(_settings);
                AppendLog($"Virtual cable active: Windows default -> {pair.Value.Render.FriendlyName}");
                captureDevice = pair.Value.Capture;
            }
            else
            {
                captureDevice = selected;
            }
        }

        if (captureDevice is null)
        {
            MessageBox.Show("No audio endpoint is available for the selected capture mode.",
                "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _settings.Password = PasswordInput.Password;
            _settings.Volume = VolumeSlider.Value;
            _settings.MuteLocalOutput = MuteLocalCheckBox.IsChecked == true;
            _settings.CaptureMode = mode;
            _settings.CaptureDeviceId = captureDevice.Id;
            _settings.LastReceiverHost = receiver.Host;
            _settings.LastReceiverPort = receiver.Port;
            _settings.LastReceiverName = receiver.Name;
            SettingsStore.Save(_settings);

            _session = new AirPlaySession(_settings.SenderName, _settings.SenderDeviceId);
            _session.StateChanged += Session_StateChanged;
            _session.LogReceived += (_, line) => Logger.Instance.Info($"core: {line}");
            _session.Start(receiver, PasswordInput.Password);
            _session.SetVolume(VolumeSlider.Value);

            if (MuteLocalCheckBox.IsChecked == true && captureDevice.Kind == AudioDeviceKind.Render)
            {
                try
                {
                    _mutedDevice = captureDevice.Device;
                    _previousMute = _mutedDevice.AudioEndpointVolume.Mute;
                    _mutedDevice.AudioEndpointVolume.Mute = true;
                    _settings.LocalMuteActive = true;
                    _settings.LocalMutePreviousState = _previousMute;
                    _settings.LocalMuteDeviceId = captureDevice.Id;
                    SettingsStore.Save(_settings);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Warn($"Could not mute the local endpoint: {ex.Message}");
                    _mutedDevice = null;
                }
            }

            _capture = new AudioCaptureService();
            _capture.PcmAvailable += Capture_PcmAvailable;
            _capture.CaptureError += (_, error) => Dispatcher.BeginInvoke(() =>
            {
                AppendLog($"Capture error: {error}");
                SetStatus(SessionState.Error, error);
                StopStreaming();
            });
            _capture.Start(captureDevice);

            SetStreamingUi(true);
            CaptureFormatText.Text = _capture.CurrentFormat ?? "-";
            SetStatus(SessionState.Connecting, $"Connecting to {receiver.Endpoint}...");
            AppendLog($"Streaming {captureDevice.DisplayName} to {receiver.Endpoint} ({receiver.Name}).");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Could not start streaming", ex);
            AppendLog($"Could not start: {ex.Message}");
            SetStatus(SessionState.Error, ex.Message);
            StopStreaming();
            MessageBox.Show(ex.Message, "AirPlay Sender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopStreaming();

    private void Capture_PcmAvailable(object? sender, PcmChunkEventArgs e)
    {
        _session?.PushPcm(e.Samples, e.Frames);
    }

    private void Session_StateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SetStatus(e.State, e.Detail);
            if (e.State is SessionState.Error or SessionState.Stopped)
            {
                if (!_stopping) StopStreaming();
            }
        });
    }

    private void StopStreaming()
    {
        if (_stopping) return;
        _stopping = true;
        try
        {
            _capture?.Stop();
            _capture?.Dispose();
            _capture = null;

            if (_session is not null)
            {
                _session.StateChanged -= Session_StateChanged;
                _session.Stop();
                _session.Dispose();
                _session = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Error while stopping", ex);
        }
        finally
        {
            RestoreLocalMute();
            RestoreVirtualCable();
            SetStreamingUi(false);
            SetStatus(SessionState.Idle, "Stopped.");
            _stopping = false;
        }
    }

    private void RestoreLocalMute()
    {
        if (_mutedDevice is null) return;
        try
        {
            _mutedDevice.AudioEndpointVolume.Mute = _previousMute;
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Could not restore the local endpoint mute state: {ex.Message}");
        }
        finally
        {
            _mutedDevice = null;
            _settings.LocalMuteActive = false;
            _settings.LocalMuteDeviceId = null;
            SettingsStore.Save(_settings);
        }
    }

    private void RestoreLocalMuteAfterCrash()
    {
        if (!_settings.LocalMuteActive) return;
        var device = _audioDevices.FindById(_settings.LocalMuteDeviceId);
        if (device is not null)
        {
            try
            {
                device.Device.AudioEndpointVolume.Mute = _settings.LocalMutePreviousState;
                Logger.Instance.Info("Restored the local endpoint mute state after an unclean shutdown.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"Could not restore the local endpoint mute state: {ex.Message}");
            }
        }
        _settings.LocalMuteActive = false;
        _settings.LocalMuteDeviceId = null;
        SettingsStore.Save(_settings);
    }

    private void RestoreVirtualCable()
    {
        if (!_settings.VirtualCableActive) return;
        var previous = _settings.VirtualCablePreviousDefaultId;
        if (!string.IsNullOrWhiteSpace(previous) && _defaultDevices.SetDefaultRender(previous))
        {
            AppendLog("Restored the previous Windows default playback device.");
        }
        else
        {
            Logger.Instance.Warn("Could not restore the previous Windows default playback device.");
        }

        _settings.VirtualCableActive = false;
        _settings.VirtualCablePreviousDefaultId = null;
        SettingsStore.Save(_settings);
    }

    private void RestoreVirtualCableAfterCrash()
    {
        if (!_settings.VirtualCableActive) return;
        var previous = _settings.VirtualCablePreviousDefaultId;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            if (_defaultDevices.SetDefaultRender(previous))
                Logger.Instance.Info("Restored the Windows default playback device after an unclean shutdown.");
            else
                Logger.Instance.Warn("Could not restore the Windows default playback device after an unclean shutdown.");
        }
        _settings.VirtualCableActive = false;
        _settings.VirtualCablePreviousDefaultId = null;
        SettingsStore.Save(_settings);
    }

    private void SetStreamingUi(bool streaming)
    {
        StartButton.IsEnabled = !streaming;
        StopButton.IsEnabled = streaming;
        ReceiversList.IsEnabled = !streaming;
        CaptureModeCombo.IsEnabled = !streaming;
        PasswordInput.IsEnabled = !streaming;
        RefreshButton.IsEnabled = !streaming;
        MuteLocalCheckBox.IsEnabled = !streaming;
        UpdateCaptureModeUi();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeText.Text = $"{e.NewValue:0}%";
        _session?.SetVolume(e.NewValue);
    }

    private void SetStatus(SessionState state, string detail)
    {
        HeaderStatusText.Text = state.ToString();
        HeaderDetailText.Text = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail;
        StateText.Text = state.ToString();
        StatusDot.Fill = state switch
        {
            SessionState.Streaming => new SolidColorBrush(Color.FromRgb(0x1A, 0x9E, 0x54)),
            SessionState.Connecting or SessionState.Authenticating or SessionState.Reconnecting =>
                new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)),
            SessionState.Error => new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
            _ => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
        };
    }

    private void UpdateStats()
    {
        if (_session is null)
        {
            BufferText.Text = "0 frames / 0 dropped";
            return;
        }

        BufferText.Text = $"{_session.BufferedFrames:N0} frames / {_session.DroppedFrames:N0} dropped";
        CaptureFormatText.Text = _capture?.CurrentFormat ?? "-";
    }

    private void Logger_LineWritten(object? sender, LogLineEventArgs e)
    {
        Dispatcher.BeginInvoke(() => AppendLog(e.Line));
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogTextBox.AppendText(line + Environment.NewLine);
        if (LogTextBox.Text.Length > 200_000)
            LogTextBox.Text = LogTextBox.Text[^100_000..];
        LogTextBox.ScrollToEnd();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            ShutdownServices();
            return;
        }

        e.Cancel = true;
        Hide();
        Logger.Instance.Info("Window hidden to tray.");
    }

    public void ShutdownServices()
    {
        _statusTimer.Stop();
        Logger.Instance.LineWritten -= Logger_LineWritten;
        StopStreaming();
        _discovery.DeviceFound -= Discovery_DeviceFound;
        _discovery.Dispose();
        _audioDevices.Dispose();
    }
}
