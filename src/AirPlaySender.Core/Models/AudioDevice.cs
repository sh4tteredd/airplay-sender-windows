using NAudio.CoreAudioApi;

namespace AirPlaySender.Core.Models;

public enum AudioDeviceKind
{
    Render,
    Capture
}

public sealed class AudioDevice
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required AudioDeviceKind Kind { get; init; }
    public required MMDevice Device { get; init; }

    public string DisplayName => $"{FriendlyName} ({(Kind == AudioDeviceKind.Render ? "output" : "input")})";

    public override string ToString() => DisplayName;
}
