[简体中文](README.md) | [日本語](README.ja-JP.md) | English

# Eikura

A native personal media library for Windows, optimized first for Japanese anime and drama while remaining useful for movies, TV series, and other personal media. Eikura brings recognition, metadata, playback, WebDAV, caching, and watch-state management into one WinUI 3 app.

<a href="https://get.microsoft.com/installer/download/9N1PTDCCND8S?referrer=appbadge"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="240" alt="Get Eikura from Microsoft Store"></a>

[![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/Eikura?display_name=tag&sort=semver&color=2F81F7&label=Release)](https://github.com/KiYouJyo/Eikura/releases/latest)
[![CI](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml)
[![Windows](https://img.shields.io/badge/Windows-WinUI%203-0078D4?logo=windows&logoColor=white)](https://github.com/KiYouJyo/Eikura)
[![Architecture](https://img.shields.io/badge/Architecture-x64-005A9E)](#system-requirements)
[![Languages](https://img.shields.io/badge/Languages-%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20English-6F42C1)](#languages)
[![Local First](https://img.shields.io/badge/Design-Local--first-2EA043)](#privacy-and-network-access)

## Get Eikura

- [Microsoft Store (recommended)](https://apps.microsoft.com/detail/9N1PTDCCND8S): Store-managed installation and updates without manual publisher-certificate setup.
- [GitHub sideload edition](https://github.com/KiYouJyo/Eikura/releases/latest): one-click installer, signed MSIXBundle, and checksum manifest.
- [Project website](https://kiyoujyo.github.io/Eikura/): current version, feature overview, downloads, and support.
- Advanced users can download the signed `.msixbundle` and `SHA256SUMS.txt` directly from Release Assets.

## Core features

- **Native media library** for local and WebDAV sources, scanning, classification, and title/season/episode aggregation.
- **Japanese-media-first recognition** for seasons, episodes, OVA/OAD/ONA/SP, release groups, and technical tags while preserving native titles.
- **Real metadata** from providers such as TMDB and Bangumi for artwork, summaries, seasons, episodes, cast, and staff.
- **Playback** powered by LibVLC with resume state, seeking, speed controls, fullscreen, queue support, and common video formats.
- **Subtitles** including external ASS/SSA/SRT, normalized embedded subtitle rendering, dual subtitles, and preferred languages.
- **Cache and downloads** for remote video, including progress, pause/resume, and offline playback.
- **Bangumi integration** for broadcast calendar, discovery, collections, and account-backed features.
- **Native Windows experience** with WinUI 3, Mica, light/dark themes, three languages, and high-DPI visual assets.

## Install and update

### Microsoft Store installation (recommended)

Install Eikura from its [Microsoft Store listing](https://apps.microsoft.com/detail/9N1PTDCCND8S). The Store and GitHub editions use separate update channels. The 1.3.4 Store update has been submitted for review; check the Store for the available version.

### GitHub sideload installation

Download the one-click package from the [latest Release](https://github.com/KiYouJyo/Eikura/releases/latest), extract it, and follow the bundled instructions. It carries the installation bootstrap and publisher-certificate setup needed by the GitHub distribution.

### Updates

The Microsoft Store edition updates through the Store; starting with 1.3.4, it can also check and install Store updates from the About page. The GitHub sideload edition downloads application updates from GitHub Releases and verifies integrity and publisher signature before installation. Both editions update the application as a whole; Metadata ships with the app.

### Manual verification

Each release also publishes the signed MSIXBundle and `SHA256SUMS.txt` for manual deployment, archiving, or verification.

## Privacy and network access

Eikura is local-first. Library indexes, settings, and playback history are stored locally by default, and local media files are not uploaded to an Eikura-operated server. Features such as metadata retrieval, Bangumi, update checks, and user-configured WebDAV access communicate with the corresponding third-party service or media source.

See [Privacy](PRIVACY.md).

## System requirements

- Windows 10 version 2004 or later / Windows 11
- x64
- Windows App SDK Runtime

## Languages

Simplified Chinese, Japanese, and English are supported with a shared resource-key contract.

## Documentation

- [Project website](https://kiyoujyo.github.io/Eikura/)
- [Roadmap](docs/ROADMAP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Playback integration](docs/PLAYBACK_INTEGRATION.md)
- [Localization](docs/LOCALIZATION.md)
- [UI guidelines](docs/UI_GUIDELINES.md)
- [Release process](docs/RELEASE.md)
- [Changelog](CHANGELOG.md)
- [Privacy](PRIVACY.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

## Development

```powershell
dotnet restore Eikura.slnx
dotnet test Eikura.slnx -c Debug
```

## Feedback

Use [GitHub Issues](https://github.com/KiYouJyo/Eikura/issues) or the [support page](https://kiyoujyo.github.io/Eikura/support/).

---

Eikura
