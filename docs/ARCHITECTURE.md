# Architecture

Eikura is structured as a media-library application whose playback engine is replaceable, rather than as a player with a library bolted onto it.

> Eikura 1.3.2 retains the existing `Eizo.*` source/project/assembly identifiers where changing them would break upgrade or component compatibility. These names are technical compatibility identifiers, not the current product brand.

## Planned modules

    src/
    ├─ Eizo.App
    ├─ Eizo.Domain
    ├─ Eizo.Application
    ├─ Eizo.Infrastructure
    ├─ Eizo.Metadata
    ├─ Eizo.Storage
    ├─ Eizo.Streaming
    └─ Eizo.Playback

Business rules should not depend directly on a specific cloud provider, metadata provider, database implementation, or playback engine.

## Media sources

All storage backends converge on a common media-source abstraction. Planned providers include local files, WebDAV, OneDrive, and Google Drive. The playback layer must not need provider-specific OAuth or API knowledge.

## Media identity

A media work is not the same thing as a physical media file. One work may have several local or remote versions.

Japanese media recognition preserves the native Japanese title, Simplified Chinese title, English title, romaji title, aliases, season/episode identity, and TV/Movie/OVA/OAD/ONA/Special/Extra distinctions where available.

## Metadata

Metadata providers are replaceable and mergeable. The planned model allows TMDB, Bangumi, AniList, local NFO, and future providers to contribute without becoming the domain model.

## Playback

Playback is behind a playback-engine boundary. Anime-oriented acceptance tests explicitly cover ASS/SSA subtitles, multiple audio tracks, HEVC 10-bit, AV1, HDR, and common MKV workflows before the default backend is considered stable.

## Streaming

Remote providers expose seekable streams through a common streaming layer. A local streaming gateway may translate player HTTP range requests into provider-specific range reads and caching.

## Secrets

OAuth tokens, WebDAV passwords, signing material, and store publishing credentials are infrastructure concerns and must not be stored in ordinary application configuration or source control.
