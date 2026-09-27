# Eikura playback integration

Stage 7 integrates the standalone `KiYouJyo/Eikura.Playback` repository into the Eikura WinUI application without merging the two source trees.

## Dependency model

Eikura pins one exact legacy `Eizo.Playback` package commit in:

```text
eng/Eizo.Playback.json
```

The bootstrap script:

```powershell
./scripts/Restore-EizoPlayback.ps1
```

clones that exact commit into the ignored `.deps/Eizo.Playback` directory, builds it, packs the four NuGet packages, and writes them to the ignored local feed:

```text
.packages/Eizo.Playback
```

`NuGet.config` exposes that local feed alongside nuget.org.

The Eikura application references the Eizo playback integration package plus the platform-native LibVLC runtime:

```xml
<PackageReference Include="Eizo.Playback.LibVLC.WinUI" Version="0.1.0" />
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.23.1" />
```

The native package is a deployment responsibility of the final Windows host. Eikura UI code still does not reference LibVLCSharp or native LibVLC APIs.

The remaining Eizo.Playback packages are transitive dependencies.

## Why this is not a submodule

The Stage 7 development boundary deliberately remains package-based:

```text
Eikura repository
    |
    | PackageReference
    v
local NuGet feed
    ^
    | dotnet pack
    |
pinned Eizo.Playback repository commit
```

This catches package metadata and dependency problems during integration instead of letting source-level project references hide them.

When Eizo.Playback receives a formal package publishing channel, the local feed can be replaced without changing PlayerView's API usage.

## Player surface

`PlayerView` hosts:

```text
Eizo.Playback.WinUI.PlaybackView
```

and controls the current engine exclusively through:

```text
IPlaybackEngine
├─ Tracks
├─ Navigation
└─ Diagnostics
```

The Eizo application does not reference LibVLCSharp or native LibVLC types. Its direct `VideoLAN.LibVLC.Windows` PackageReference exists only so the final Windows package contains `libvlc.dll`, `libvlccore.dll`, and the plugin tree.

## Current Stage 7 UI wiring

The current PlayerView supports:

- local media selection;
- play/pause;
- seek and ±10-second movement;
- previous/next chapter;
- playback rate switching;
- audio-track selection;
- subtitle-track selection and subtitle disable;
- timeline position/duration;
- video/source diagnostic summary.

The library is still demo-backed, so selecting an episode does not yet resolve a real media-source record automatically. The local-file picker is the acceptance entry point for this stage.

## Surface recreation

The WinUI video surface is scoped to a live D3D swap chain. Switching away from a player tab may unload the surface and dispose the current engine.

PlayerView therefore preserves:

- the selected `PlaybackSource`;
- last observed position;
- play/pause intent.

When a replacement surface engine becomes available, PlayerView reopens the source, waits for seek capability, restores the position, and resumes or pauses according to the stored intent.

## Updating the playback pin

1. Complete and merge the desired Eizo.Playback change.
2. Update `eng/Eizo.Playback.json` with the new commit and package version.
3. Run:

```powershell
./scripts/Restore-EizoPlayback.ps1 -Force
```

4. Build Eikura.
5. Let Repository validation and Stage 7 playback integration CI pass before merging.

Do not update the package version without updating the pinned source commit to the commit that actually produces that version.

## CI acceptance

Stage 7 CI verifies:

- the pinned Eizo.Playback commit can be cloned and packed from scratch;
- Eikura compiles against those packages;
- all four managed Eizo.Playback assemblies enter the application output;
- `libvlc.dll`, `libvlccore.dll`, and LibVLC plugins enter the application output;
- an unsigned x64 MSIX can be produced;
- the MSIX itself contains the LibVLC native runtime and plugins.

Manual acceptance should still use representative anime/drama media to verify actual image rendering, subtitles, audio switching, seeking and surface recreation.
