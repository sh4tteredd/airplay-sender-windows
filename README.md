# AirPlay Sender for Windows

A Windows tray application that streams system audio (or a selected endpoint) to classic AirPlay / RAOP receivers. The first target is a speaker running `shairport-sync`; AirPlay 2 / HomePod support is a later phase.

This project is GPLv3. The protocol core is a Git submodule of the [airplay2-sender-cpp fork](https://github.com/sh4tteredd/airplay2-sender-cpp) (Apache-2.0), based on [upstream](https://github.com/akustikrausch/airplay2-sender-cpp).

## Status

Working today:

- mDNS discovery of `_raop._tcp` and `_airplay._tcp` receivers.
- Manual receiver entry (`host:port`) when mDNS is unavailable.
- WASAPI loopback capture of the default Windows output.
- Capture of a selected render or capture endpoint.
- Classic AirPlay/RAOP session: `ANNOUNCE` → `SETUP` → `RECORD` → RTP.
- ALAC audio at 44.1 kHz / 16-bit stereo.
- Volume control, stop/teardown, status and diagnostics.
- Optional virtual-cable mode: switches the Windows default output to VB-CABLE/VAC and restores it on stop or after a crash. The code path is implemented; a VB-CABLE/VAC pair was not installed on the test machine, so this mode still needs hardware verification.
- Tray icon and single-instance behavior.

Verified against a local `shairport-sync` 5.6.1 + `nqptp` receiver. A long soak test is still pending. See [docs/verification.md](docs/verification.md) for the evidence and the remaining test matrix.

Not implemented yet:

- AirPlay 2 / HomePod / Apple TV 4K pairing and PTP.
- Multi-receiver fan-out and synchronized multi-room.
- Metadata / artwork.
- Native kernel-mode virtual audio driver.
- Code signing / installer certificate.

## Why ALAC instead of L16?

`airplay2-sender-cpp`'s AP1 path originally advertised L16 PCM. Some minimal `shairport-sync` builds use a reduced FFmpeg with ALAC/AAC decoders and no PCM decoder, so L16 causes:

```text
fatal error: connection N: could not initialise the codec context
```

TuneBlade also uses ALAC for AP1. The fork therefore carries a small integration patch on top of upstream:

- `RaopSender::setAp1Alac(true)`
- AP1 SDP advertises `AppleLossless`
- AP1 RTP payload uses the existing uncompressed-ALAC encoder

See [docs/upstream.md](docs/upstream.md).

## Build

Prerequisites:

- Windows 10 22H2 or Windows 11, x64.
- .NET 10 SDK.
- CMake 3.20+.
- Git (for the protocol-core submodule).
- Visual Studio 2026 (or 2022) with the Desktop development with C++ workload and Windows SDK.

Clone with the protocol-core submodule:

```powershell
git clone --recurse-submodules <repository-url>
cd airplay-windows
```

If the repository was cloned without submodules:

```powershell
git submodule update --init --recursive
```

From the repository root:

```powershell
cmake -S . -B build/native -G "Visual Studio 18 2026" -A x64
cmake --build build/native --config Release --target AirPlayCore --parallel
dotnet build AirPlaySender.slnx -c Release
```

For Visual Studio 2022, use `-G "Visual Studio 17 2022"`.

The native DLL is copied into the WPF output via the project file.

Or use the helper scripts:

```powershell
.\scripts\build.ps1
$env:AIRPLAY_TEST_HOST = "receiver.local:7000"   # optional; otherwise the first discovered receiver is used
.\scripts\test-smoke.ps1
.\scripts\publish.ps1
```

`publish.ps1` creates a self-contained `artifacts\AirPlaySender-win-x64.zip`. To install a per-user copy with a Start Menu shortcut:

```powershell
.\scripts\install.ps1
```

For AP1 receivers that send unsolicited UDP to the sender, add a per-program firewall rule from an elevated prompt:

```powershell
.\scripts\install-firewall.ps1
```

## Run

```powershell
.\src\AirPlaySender.App\bin\Release\net10.0-windows\AirPlaySender.exe
```

1. Wait for the receiver list, or type `host:port` in the manual box and press **Add**.
2. Select the receiver.
3. Choose the capture mode:
   - **Default Windows output** — capture the current default playback device.
   - **Specific audio endpoint** — capture the selected render or recording endpoint.
   - **Virtual cable** — select a VB-CABLE/VAC endpoint; the app makes it the default output and captures the other side.
4. Press **Start streaming**.

The app minimizes to the tray when the window is closed. Use the tray menu to show it again or exit.

## Virtual cable mode

This is the “shows up as a Windows output” option without writing a kernel driver.

1. Install [VB-CABLE](https://vb-audio.com/Cable/) or Virtual Audio Cable.
2. Select **Virtual cable (VB-CABLE / VAC)** as the capture mode.
3. Select either `CABLE Input` or `CABLE Output`.
4. Start streaming.

The app saves the previous default playback device and restores it on stop. If the app crashes, the saved state is used to restore it on the next launch.

Do not bundle VB-CABLE/VAC without checking their redistribution terms. This repository only detects and uses an installed virtual cable.

## Troubleshooting

- **Receiver not listed**: use manual `host:port`; mDNS may be blocked by the firewall or a multi-homed adapter.
- **`501 Not Implemented` from a setup request**: you are seeing an AirPlay 2/NTP attempt against an AP1 path. This app uses `ANNOUNCE` + AP1.
- **`could not initialise the codec context` on the receiver**: the receiver's FFmpeg lacks the PCM decoder. Use ALAC (this app does).
- **No audio**: check the receiver volume, the Windows capture endpoint, and whether the application is using exclusive mode. Use VB-CABLE for exclusive-mode/DAW audio.
- **Mute local speakers**: some sound devices stop providing loopback audio when muted. If that happens, uncheck the option and mute the speakers manually, or use virtual-cable mode.
- **Session drops**: check `shairport-sync` logs and whether the receiver process is still alive. Some minimal receiver builds have crashed on unsupported codec errors.

## License

GPLv3. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The protocol core is Apache-2.0; its notices and third-party notices are retained. If you distribute a binary build, publish the corresponding source tree as well. AirPlay is an Apple trademark; this project is not affiliated with or endorsed by Apple.
