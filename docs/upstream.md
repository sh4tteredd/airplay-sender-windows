# Protocol core and fork

The native AirPlay core is included as a Git submodule:

- **Fork:** https://github.com/sh4tteredd/airplay2-sender-cpp
- **Upstream:** https://github.com/akustikrausch/airplay2-sender-cpp
- **Base commit:** `fc913509039412790aa2427558d35178aa7842ea`
- **Integration commit:** `4ba97e55359138bb9f1f7da85498bcb710cf1715`
- **License:** Apache-2.0 (see the submodule's `LICENSE`, `NOTICE`, and
  `licenses/THIRD-PARTY-NOTICES.txt`)

## Integration patch

The fork adds one small capability on top of upstream:

- `RaopSender::setAp1Alac(bool enabled)`
- `sendAnnounce_()` advertises `a=rtpmap:96 AppleLossless` when enabled
- `sendAudioPacket_()` uses the existing uncompressed-ALAC encoder for AP1
  when enabled

The Windows wrapper calls `setAp1Alac(true)`.

## Why ALAC on AP1

Classic AirPlay supports ALAC. Some minimal `shairport-sync` builds use a
reduced FFmpeg containing ALAC and AAC decoders but not the PCM S16BE decoder.
Upstream's AP1 L16 path therefore fails on such receivers with:

```text
fatal error: connection N: could not initialise the codec context
```

ALAC avoids that failure and is also what TuneBlade advertises for classic
AirPlay.

## Working with the submodule

Clone the parent repository recursively:

```powershell
git clone --recurse-submodules <repository-url>
```

If the parent was cloned without submodules:

```powershell
git submodule update --init --recursive
```

To update the fork from upstream:

```powershell
cd third_party/airplay2-sender-cpp
git fetch upstream
git rebase upstream/main
# resolve the ALAC patch if needed
git push origin main
```

Then update the parent submodule pointer and rebuild:

```powershell
cd ../..
git add third_party/airplay2-sender-cpp
git commit -m "Update airplay2-sender-cpp"
```

Keep the Apache-2.0 notices and the upstream third-party notices intact.
