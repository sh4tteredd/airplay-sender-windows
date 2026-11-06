# Verification notes

These notes describe what was checked during the first working build. Hostnames, addresses, and device identifiers are intentionally omitted.

## Environment

- Windows 11 x64
- .NET SDK 10
- Visual Studio C++ tools (MSVC 19.51)
- CMake 4.x
- Receiver: a local `shairport-sync` 5.6.1 + `nqptp` installation
  - AP2-capable with classic AirPlay fallback
  - Reduced FFmpeg build with ALAC/AAC decoders but no PCM decoder

## Results

### Discovery

The smoke harness discovered the configured test receiver and an unrelated AirPlay receiver on the same LAN:

```text
FOUND: <receiver> <address> model=<model> pw=False
FOUND: <other-receiver> <address> model=<model> pw=False
```

The raw mDNS browser worked on a machine with multiple virtual network adapters. The earlier packaged mDNS browser did not. The raw implementation joins the group on every active IPv4 adapter and sets the outgoing multicast interface per query.

### AP1 + ALAC

```text
Cast: RAOP RTSP connected to <receiver>
Cast: RAOP remote ports, server=<port> control=<port> timing=<port>
Cast: RAOP receiver Audio-Latency=<frames> frames
Cast: RAOP streaming to '<sender>', RTP 352 frames/packet @ 44100 Hz
STATE: Streaming
```

30-second run:

```text
PUSHED: 1,323,882 frames, buffered=958, dropped=0
```

60-second run:

```text
PUSHED: 2,646,441 frames, buffered=1,513, dropped=0
```

The receiver remained alive after each session.

### L16 failure

The first AP1 attempt used upstream's L16 SDP. A receiver with a reduced FFmpeg build reported:

```text
warning: Shairport Sync can not handle NTP streams.
...
fatal error: connection N: could not initialise the codec context
```

A separate AirPlay 2/NTP setup attempt produced `501 Not Implemented`. Switching the AP1 path to ALAC fixed the codec failure. See `docs/upstream.md`.

### Default device switching

`DefaultDeviceService.SetDefaultRender(currentDefaultId)` returned `True` without changing the device. A full VB-CABLE/VAC pair test is pending a machine with a virtual cable installed.

### UI automation

The WPF window was found through UI Automation, a discovered receiver was selected, and **Start streaming** was invoked. The native core reached `Streaming`; **Stop** returned it to `Stopped`.

## Still to verify

- 2-hour continuous playback.
- Audible output on the target hardware.
- VB-CABLE default switch and restore.
- Packet loss / Wi-Fi jitter behavior.
- Reboot/restart of the receiver during playback.
- Windows 10 22H2 (only a Windows 11 build was tested).
