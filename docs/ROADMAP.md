# Roadmap

Version numbers below describe development milestones rather than promised dates. Some capabilities may land earlier when they are needed to validate later stages.

## Current baseline — 0.6.1

Eikura now has:

- a WinUI 3 shell with Simplified Chinese, Japanese, and English;
- local and WebDAV media sources;
- Japanese-media recognition and the external Recognition runtime path;
- TMDB and Bangumi metadata/integration work;
- title, season, episode, cast/staff, and artwork presentation;
- LibVLC playback, resume state, queue, fullscreen, seek, speed control, and subtitle work;
- dual/external subtitle handling;
- cache/download management for remote media;
- signed GitHub MSIXBundle and one-click distribution;
- application visual assets and a public product-site foundation.

## 0.7 — Remote-source reliability

- harden WebDAV incremental scan and change detection;
- improve reconnect, timeout, retry, and remote-file diagnostics;
- refine range/seek behavior for large remote media;
- make scan/scrape/cache jobs observable without blocking the UI.

## 0.8 — Streaming gateway and cache policy

- provider-neutral stream descriptors;
- HTTP range handling and retry policy;
- chunk cache and eviction strategy;
- stronger background download/resume semantics;
- bandwidth and cache diagnostics.

## 0.9 — Additional cloud providers

- official OAuth flows where supported;
- provider abstractions beyond WebDAV;
- secure token persistence through Windows credential facilities;
- source-specific capability and failure reporting.

## 1.0 — Stable release

- regression and performance close-out;
- accessibility and keyboard navigation;
- packaging and Microsoft Store validation;
- privacy / third-party notices review;
- updater and rollback hardening;
- long-running playback and large-library acceptance.

## Ongoing priorities

- Japanese anime and drama remain first-class content.
- General movies and TV should remain fully usable.
- Recognition, metadata, playback, and app-shell boundaries should stay independently testable.
- Expensive indexing or network work must not block first-render UI.
