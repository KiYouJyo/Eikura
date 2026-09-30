# Eikura v1.3.2

Eikura 1.3.2 completes the user-facing brand migration to **Eikura**.

## Changes
- Unified the app title, About surface, three localization sets, and user-facing messages under Eikura.
- Updated the project website, privacy/support pages, README files, and branding rules for Eikura.
- The GitHub one-click package is named `Eikura-v1.3.2-x64-one-click.zip`. To let the Eizo 1.3.1 built-in updater discover the renamed release without interruption, the single MSIXBundle technical asset for 1.3.2 temporarily retains the compatibility name `Eizo_1.3.2.0_x64.msixbundle`. The installed product name remains Eikura.
- Preserved legacy MSIX identity and local-data compatibility identifiers, and verified the 1.3.1 → 1.3.2 path with real signed packages while retaining settings, library data, and playback history.
- Added `eikura://` as the primary Bangumi OAuth callback while retaining `eizo://` compatibility; the app probes the new Eikura Worker and automatically falls back to the legacy relay until rollout is enabled.

## Compatibility
`Eizo_1.3.2.0_x64.msixbundle`, the MSIX identity, legacy data paths, and a small set of runtime identifiers remain only as a 1.3.2 migration bridge. The current product brand is Eikura, and subsequent new release assets return to Eikura naming.
