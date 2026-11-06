using System.Text.Json;

namespace AirPlaySender.Core.Services;

public enum CaptureMode
{
    DefaultOutput,
    SpecificEndpoint,
    VirtualCable
}

public sealed class AppSettings
{
    public string? LastReceiverHost { get; set; }
    public int LastReceiverPort { get; set; } = 7000;
    public string? LastReceiverName { get; set; }
    public string? Password { get; set; }
    public string? CaptureDeviceId { get; set; }
    public string? VirtualCableCaptureDeviceId { get; set; }
    public string? VirtualCableRenderDeviceId { get; set; }
    public string? VirtualCablePreviousDefaultId { get; set; }
    public bool VirtualCableActive { get; set; }
    public CaptureMode CaptureMode { get; set; } = CaptureMode.DefaultOutput;
    public double Volume { get; set; } = 50;
    public bool MuteLocalOutput { get; set; }
    public bool LocalMuteActive { get; set; }
    public bool LocalMutePreviousState { get; set; }
    public string? LocalMuteDeviceId { get; set; }
    public bool AutoConnect { get; set; }
    public bool StartWithWindows { get; set; }
    public string SenderDeviceId { get; set; } = string.Empty;
    public string SenderName { get; set; } = "Windows AirPlay";

    public void EnsureDeviceId()
    {
        if (!string.IsNullOrWhiteSpace(SenderDeviceId)) return;
        var bytes = Guid.NewGuid().ToByteArray();
        SenderDeviceId = "02:" + string.Join(":", bytes.Take(5).Select(b => b.ToString("X2")));
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AirPlaySender");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.EnsureDeviceId();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Could not load settings", ex);
        }

        var created = new AppSettings();
        created.EnsureDeviceId();
        return created;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            settings.EnsureDeviceId();
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Could not save settings", ex);
        }
    }
}
