using AirPlaySender.Core.Models;
using NAudio.CoreAudioApi;

namespace AirPlaySender.Core.Services;

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDevice> GetRenderDevices()
        => _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDevice
            {
                Id = d.ID,
                FriendlyName = d.DeviceFriendlyName,
                Kind = AudioDeviceKind.Render,
                Device = d
            })
            .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public IReadOnlyList<AudioDevice> GetCaptureDevices()
        => _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDevice
            {
                Id = d.ID,
                FriendlyName = d.DeviceFriendlyName,
                Kind = AudioDeviceKind.Capture,
                Device = d
            })
            .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public AudioDevice? GetDefaultRenderDevice()
    {
        try
        {
            var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioDevice
            {
                Id = d.ID,
                FriendlyName = d.DeviceFriendlyName,
                Kind = AudioDeviceKind.Render,
                Device = d
            };
        }
        catch
        {
            return null;
        }
    }

    public AudioDevice? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetRenderDevices().Concat(GetCaptureDevices())
            .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeVirtualCable(string name)
        => name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
           || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Virtual Audio", StringComparison.OrdinalIgnoreCase)
           || name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Line 1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the render/capture pair for a virtual cable. VB-CABLE exposes
    /// "CABLE Input" (render) and "CABLE Output" (capture); VAC uses a similar
    /// naming convention.
    /// </summary>
    public (AudioDevice Render, AudioDevice Capture)? FindVirtualCablePair(AudioDevice selected)
    {
        var renders = GetRenderDevices();
        var captures = GetCaptureDevices();

        if (selected.Kind == AudioDeviceKind.Render)
        {
            var expected = selected.FriendlyName
                .Replace("Input", "Output", StringComparison.OrdinalIgnoreCase);
            var capture = captures.FirstOrDefault(c => NamesMatch(c.FriendlyName, expected))
                          ?? captures.FirstOrDefault(c => LooksLikeVirtualCable(c.FriendlyName));
            return capture is null ? null : (selected, capture);
        }
        else
        {
            var expected = selected.FriendlyName
                .Replace("Output", "Input", StringComparison.OrdinalIgnoreCase);
            var render = renders.FirstOrDefault(r => NamesMatch(r.FriendlyName, expected))
                         ?? renders.FirstOrDefault(r => LooksLikeVirtualCable(r.FriendlyName));
            return render is null ? null : (render, selected);
        }
    }

    private static bool NamesMatch(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
           || a.Contains(b, StringComparison.OrdinalIgnoreCase)
           || b.Contains(a, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _enumerator.Dispose();
}
