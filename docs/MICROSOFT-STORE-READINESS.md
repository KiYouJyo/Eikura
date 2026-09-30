# Eikura Microsoft Store readiness

This document tracks repository-side readiness for the first Microsoft Store submission of Eikura.

## Current product baseline

- Product version: `1.0.0`
- Package version: `1.0.0.0`
- Target: Windows 10 2004 / Windows 11, x64
- Packaging: MSIX / MSIXBundle
- App model: packaged desktop WinUI 3 with `runFullTrust`
- First-run setup: Welcome → Media Sources → TMDB → Bangumi → Playback → Finish
- GitHub stable publication gate remains disabled while Store readiness is in progress.

## Repository-ready items

- [x] 1.0 product and package version contract
- [x] Multi-size Windows/MSIX app assets
- [x] Store artwork baseline under `release/MicrosoftStore/0.6.1/`
- [x] Public product site
- [x] Public privacy page
- [x] Public support page
- [x] Tri-lingual product UI and release notes
- [x] First-run guide with resumable state
- [x] Local and WebDAV source setup in onboarding
- [x] TMDB configuration and connection verification in onboarding
- [x] Bangumi browser OAuth in onboarding
- [x] Playback preference setup in onboarding
- [x] Settings action to reopen the guide
- [x] Dedicated 1.0 validation and signed sideload acceptance flow

## Partner Center identity

Partner Center has assigned the production Store identity:

- Package Identity Name: `JoKiy.Eizo`
- Publisher: `CN=C4E4B33A-7B77-4121-897C-7D720A5471F8`
- Publisher display name: `Jo Kiyō`
- Package Family Name: `JoKiy.Eizo_4wdwgytaw3v2m`

These values are pinned in `release/MicrosoftStore/store-identity.json`.

The repository source manifest intentionally keeps the GitHub sideload identity:

- `Identity Name="Eizo"`
- `Publisher="CN=AppPublisher"`

The StoreUpload build injects the Partner Center identity only into the Store build workspace, then restores the source manifest. This keeps the existing GitHub signing certificate compatible while producing Partner Center-compatible Store packages.

The StoreUpload pipeline deep-verifies the main x64 package plus every generated scale resource package for Identity Name, Publisher, and PublisherDisplayName before uploading the artifact.

The final Store package is produced with Store-upload build mode and package signing disabled; Microsoft signs accepted Store packages.

## Store listing inputs

Use the public Eikura Pages site as the website, privacy-policy, and support destinations. Before certification, confirm all three production URLs resolve without authentication.

Prepare the final listing in Simplified Chinese, Japanese, and English:

- App name
- Short description
- Full description
- Feature list
- Category
- Age-rating questionnaire
- Search terms
- At least one current screenshot per listing language
- Store logo / artwork
- Certification notes where external login and media-source behavior need explanation

## Final package gate

Before generating the Store upload package:

1. Partner Center product name reserved. ✅
2. Store identity synchronized from Partner Center. ✅
3. Repository validation PASS on the final 1.0 commit.
4. Stage 7 playback integration PASS.
5. Signed sideload acceptance package installs and launches successfully.
6. First-run guide verified on a clean install.
7. TMDB configuration verified.
8. Bangumi OAuth callback verified on clean and warm activation.
9. Local-folder and WebDAV onboarding verified without automatic scan before Finish.
10. Finish-step scan verified.
11. Privacy and support pages verified publicly.
12. Final screenshots captured from the accepted 1.0 UI.
13. StoreUpload package generated unsigned for Partner Center.
