using AirPlaySender.Core.Models;
using AirPlaySender.Core.Services;

var seconds = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 8;
var targetHost = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("AIRPLAY_TEST_HOST");
Console.WriteLine("AirPlay Sender smoke test");
Console.WriteLine($"Log: {Logger.Instance.LogPath}");

using var discovery = new DiscoveryService();
discovery.DeviceFound += (_, d) => Console.WriteLine($"FOUND: {d.Name} {d.Endpoint} model={d.Model} pw={d.RequiresPassword}");
discovery.DiscoveryError += (_, e) => Console.WriteLine($"DISCOVERY ERROR: {e}");
discovery.Start();

var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
ReceiverDevice? receiver = null;
while (DateTime.UtcNow < deadline)
{
    await Task.Delay(250);
    receiver = string.IsNullOrWhiteSpace(targetHost)
        ? discovery.Devices.FirstOrDefault()
        : discovery.Devices.FirstOrDefault(d =>
            string.Equals(d.Host, targetHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Endpoint, targetHost, StringComparison.OrdinalIgnoreCase));
    if (receiver is not null) break;
}

if (receiver is null)
{
    Console.WriteLine("No matching AirPlay receiver found via mDNS.");
    Console.WriteLine("Set AIRPLAY_TEST_HOST or pass host as the second argument, e.g. -- <seconds> <host>.");
    return 1;
}

Console.WriteLine($"SELECTED: {receiver.Name} {receiver.Endpoint} model={receiver.Model} pw={receiver.RequiresPassword}");

using var audioDevices = new AudioDeviceService();
Console.WriteLine("RENDER ENDPOINTS:");
foreach (var d in audioDevices.GetRenderDevices()) Console.WriteLine($"  {d.FriendlyName} | {d.Id}");
Console.WriteLine("CAPTURE ENDPOINTS:");
foreach (var d in audioDevices.GetCaptureDevices()) Console.WriteLine($"  {d.FriendlyName} | {d.Id}");

var captureDevice = audioDevices.GetDefaultRenderDevice();
if (captureDevice is null)
{
    Console.WriteLine("No default render endpoint.");
    return 1;
}
Console.WriteLine($"CAPTURE: {captureDevice.FriendlyName}");

var defaultDevices = new DefaultDeviceService();
var currentDefault = defaultDevices.GetDefaultRenderId();
Console.WriteLine($"DEFAULT ID: {currentDefault}");
if (currentDefault is not null)
    Console.WriteLine($"SET DEFAULT (same): {defaultDevices.SetDefaultRender(currentDefault)}");

using var session = new AirPlaySession("AirPlay Smoke", "02:41:50:4C:41:59");
session.StateChanged += (_, e) => Console.WriteLine($"STATE: {e.State} {e.Detail}");
session.LogReceived += (_, line) => Console.WriteLine($"CORE: {line}");

try
{
    session.Start(receiver, null);
    session.SetVolume(30);
}
catch (Exception ex)
{
    Console.WriteLine($"START FAILED: {ex.Message}");
    return 2;
}

using var capture = new AudioCaptureService();
var pushed = 0L;
capture.PcmAvailable += (_, e) =>
{
    if (session.PushPcm(e.Samples, e.Frames)) pushed += e.Frames;
};
capture.CaptureError += (_, e) => Console.WriteLine($"CAPTURE ERROR: {e}");
capture.Start(captureDevice);
Console.WriteLine($"CAPTURE FORMAT: {capture.CurrentFormat}");

await Task.Delay(TimeSpan.FromSeconds(seconds));
Console.WriteLine($"PUSHED: {pushed:N0} frames, buffered={session.BufferedFrames:N0}, dropped={session.DroppedFrames:N0}");

capture.Stop();
session.Stop();
Console.WriteLine("DONE");
return 0;
