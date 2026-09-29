# Changelog

## 1.3.2 — 2026-09-27

- Renamed the user-facing product from the previous Eizo / 映藏 / 映蔵 branding to **Eikura** across the WinUI shell, localization resources, About surface, documentation, website, installer copy, and release-facing assets.
- Bumped product / MSIX versions to 1.3.2 / 1.3.2.0.
- Prepared GitHub Pages and repository links for the Eikura repository name.
- Renamed release asset branding to `Eikura_*.msixbundle` and `Eikura-v*-x64-one-click.zip`.
- Preserved legacy MSIX identity and local-data compatibility identifiers so existing installations can upgrade in place.
- Added staged Bangumi OAuth migration with `eikura://` primary activation, `eizo://` legacy compatibility, and automatic relay fallback.

See: [简体中文](docs/RELEASE-NOTES-v1.3.2.md) · [日本語](docs/RELEASE-NOTES-v1.3.2.ja.md) · [English](docs/RELEASE-NOTES-v1.3.2.en.md)

## 1.3.1 — 2026-09-27

- Replaced the startup `ProgressRing` with a thin native indeterminate `ProgressBar`.
- The startup indicator now uses `TextFillColorSecondaryBrush`, so it follows WinUI light/dark theme foreground colors instead of the fixed Windows accent blue.
- Made the progress track transparent so the loading motion stays visually lightweight beneath the startup logo.
- Bumped product / MSIX versions to 1.3.1 / 1.3.1.0.

See: [简体中文](docs/RELEASE-NOTES-v1.3.1.md) · [日本語](docs/RELEASE-NOTES-v1.3.1.ja.md) · [English](docs/RELEASE-NOTES-v1.3.1.en.md)

## 1.3.0 — 2026-09-25

- Replaced the standalone Bangumi Anime Blogs page with a native WinUI 3 Anime Search/index workspace.
- Added a native search box, rank/popularity/score/relevance sorting, and format/source/genre/region/audience/year filters.
- Extended Bangumi subject search with meta-tag, tag, year, anime-type, and non-NSFW constraints.
- Reused the unified MediaPosterCard component and preserved navigation into existing Bangumi subject details.
- Removed the legacy Anime Blogs navigation and workspace wiring; no WebView/WebView2 is used.
- Added Eizo 1.3 anime-index regression and advanced-search test coverage.
- Moved Ranking & Discover directly below Anime Search and replaced its duplicate search glyph with a dedicated discovery/star icon.
- Reworked Anime Search filters from a right-side panel into full-width horizontal filter rows above the results.
- Removed the manual Load More button; reaching the end of Anime Search now automatically fetches the next 50 entries.
- Made the Seasonal Anime season strip open a native WinUI 3 year/season picker for direct year and quarter jumps.
- Made Anime Search a single vertically scrollable page, added collapsible filter rows, and right-aligned the Search / Reset / Sort action group.
- Home Continue Watching now reconstructs the title subject before playback so the queue contains the same episode list as normal title playback.
- Rounded the playback-queue selected/highlight surface to match the sidebar card geometry.
- Added fullscreen Up/Down volume control in 5% steps; keyboard and mouse-wheel volume changes now use a transient top-center volume banner and never summon the bottom controls.
- Reserved the primary subtitle selector for embedded subtitles when present; external subtitles move to the secondary selector.
- Fullscreen long-press left/right speed control now uses a compact translucent top-center speed banner without summoning the bottom controls.
- Clicking the video surface now toggles play/pause in both windowed and fullscreen modes.
- Bumped product / MSIX versions to 1.3.0 / 1.3.0.0.

See: [简体中文](docs/RELEASE-NOTES-v1.3.0.md) · [日本語](docs/RELEASE-NOTES-v1.3.0.ja.md) · [English](docs/RELEASE-NOTES-v1.3.0.en.md)

## 1.2.1 — 2026-09-24

- Changed only explicit/manual WebDAV video caching to persist one complete media file instead of many 4 MB `.blk` files.
- Isolated manual downloads under dedicated `manual-video:` cache groups while leaving automatic playback block caching unchanged.
- Preserved range-download pause/resume, progress, speed tracking, cache accounting, pinned protection, deletion, and open-folder behavior.
- Added direct local-file playback for completed manual caches while retaining compatibility with legacy block-based offline caches.
- Preserved the source media extension and added single-file cache regression coverage.
- Existing legacy `.blk` manual caches remain compatible and can be deleted/re-cached to convert them to the new representation.

See: [简体中文](docs/RELEASE-NOTES-v1.2.1.md) · [日本語](docs/RELEASE-NOTES-v1.2.1.ja.md) · [English](docs/RELEASE-NOTES-v1.2.1.en.md)

## 1.2.0 — 2026-09-24

- Added a shared card design system with reusable poster-card sizing, hover, pressed, focus, border, typography, and cache-row styles.
- Replaced duplicated Media Library and Bangumi My Following poster markup with the reusable `MediaPosterCard` component.
- Fixed Bangumi My Following pointer highlights so the interaction surface exactly follows the card bounds and corner radius.
- Added whole-card cache context menus with “Open file location” and immediate “Delete cache” actions; delete remains a direct action with no confirmation dialog.
- Added cache-folder resolution for completed media groups, with a safe fallback to the Eizo cache root for active tasks.
- Added Eizo 1.2 card UI regression and acceptance contracts.

See: [简体中文](docs/RELEASE-NOTES-v1.2.0.md) · [日本語](docs/RELEASE-NOTES-v1.2.0.ja.md) · [English](docs/RELEASE-NOTES-v1.2.0.en.md)

## 1.1.0 — 2026-09-18

- Added native Windows picture-in-picture using Windows App SDK CompactOverlay.
- Preserved the active PlaybackView and LibVLC session while entering and leaving PiP.
- Added compact native transport controls as a transparent bottom overlay over full-surface video, with automatic hiding, keyboard escape, mouse-wheel volume control, responsive subtitle sizing, non-overlapping bilingual subtitle placement, and top-of-window secondary subtitles when an embedded subtitle track is active.
- Restored the previous window geometry/maximized state and sidebar state when leaving PiP.
- Added Simplified Chinese, Japanese, and English PiP accessibility/tooltip strings.
- Added five-state Bangumi collection controls (Wish / Done / Doing / On Hold / Dropped) to subject details with authenticated state loading and updates.
- Hardened Bangumi collection writes with exact `application/json` media type handling and surfaced HTTP/server diagnostics instead of generic submission failures.
- Added a dedicated v1.1.0 acceptance contract and signed x64 acceptance package pipeline.

See: [简体中文](docs/RELEASE-NOTES-v1.1.0.md) · [日本語](docs/RELEASE-NOTES-v1.1.0.ja.md) · [English](docs/RELEASE-NOTES-v1.1.0.en.md)

All notable user-facing changes are summarized here. Detailed multilingual notes remain under `docs/RELEASE-NOTES-v*.md`.

## 0.6.1 — 2026-09-15

- Refreshed the Windows app icon, AppList/MSIX assets, splash assets, and Microsoft Store artwork.
- Added the in-app Mica startup surface with the native blue loading ring.
- Deferred media-library, playback-history, and cache startup work until after the startup surface has rendered.
- Kept component runtime bootstrap on the early path so external Playback / Recognition activation remains intact.
- Improved the detail episode list: unwatched episodes now show only the unwatched state instead of an empty progress bar.
- Preserved signed x64 MSIXBundle and one-click GitHub distribution validation.

See: [简体中文](docs/RELEASE-NOTES-v0.6.1.md) · [日本語](docs/RELEASE-NOTES-v0.6.1.ja.md) · [English](docs/RELEASE-NOTES-v0.6.1.en.md)

## 0.6.0 — 2026-09-15

- Refined the player and title-detail experience.
- Improved hero/detail layout, metadata actions, episode artwork and playback state.
- Added configurable fullscreen control persistence and keyboard seek behavior.

See: [简体中文](docs/RELEASE-NOTES-v0.6.0.md) · [日本語](docs/RELEASE-NOTES-v0.6.0.ja.md) · [English](docs/RELEASE-NOTES-v0.6.0.en.md)

## 0.5.x

The 0.5 series established the real-content media model, TMDB production integration, provider routing, field-level metadata merge, identity binding, scan/scrape persistence, and the real-content library UI.

See the individual 0.5.x notes under [docs](docs/).

## 0.4.x

The 0.4 series expanded playback, subtitles, Bangumi integration, cache/download behavior, settings, and library aggregation.

## 0.3.x

The 0.3 series introduced WebDAV source management, remote playback, metadata/runtime activation, and library aggregation work.

## 0.2.x and 0.1.x

Early releases established recognition, localization, updater/runtime infrastructure, and the WinUI 3 application shell.
