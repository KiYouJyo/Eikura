# Release process

Eikura uses `release/release.json` as the single GitHub publication gate.

## Package contents

Release and acceptance downloads must not include Windows App Runtime installers.
Keep runtime downloads used for CI checks outside the uploaded artifact directory.
User installers should use the existing runtime, downloading the required Microsoft
runtime only when dependency resolution reports that it is missing or outdated.
Acceptance archives contain only the signed application, public certificate,
installation script, readme, and checksums.

## Stable GitHub release

1. Finish the target version on a release-ready branch.
2. Keep these versions aligned:
   - `src/Eizo.App/Eizo.App.csproj` `Version`
   - `InformationalVersion`
   - `src/Eizo.App/Package.appxmanifest` package version
   - `release/release.json`
3. Provide all three release-note files:
   - `docs/RELEASE-NOTES-vX.Y.Z.md`
   - `docs/RELEASE-NOTES-vX.Y.Z.ja.md`
   - `docs/RELEASE-NOTES-vX.Y.Z.en.md`
4. Run repository validation and the relevant acceptance workflow.
5. Merge to `main`.
6. A change to `release/release.json` on `main` triggers `.github/workflows/publish-github-release.yml`.

## Publication workflow

The GitHub release workflow:

- restores the pinned Eizo.Playback and Eizo.Metadata sources;
- builds the x64 Release app;
- produces an unsigned MSIXBundle and signs the final bundle with the configured publisher certificate;
- validates the updater signature verifier;
- installs and launches the signed package on CI;
- builds and validates the one-click installer;
- produces SHA-256 checksums;
- creates or reconciles the `vX.Y.Z` tag and GitHub Release;
- publishes exactly three release assets:
  - `Eikura_X.Y.Z.0_x64.msixbundle`
  - `Eikura-vX.Y.Z-x64-one-click.zip`
  - `SHA256SUMS.txt`

## GitHub Pages

The public product site lives under `docs/` and is intended to be served from the repository's GitHub Pages site:

- home: `docs/index.html`
- support: `docs/support/index.html`
- privacy: `docs/privacy/index.html`
- status data: `docs/project-status.json`

When the stable version changes, update `docs/project-status.json` together with the release documentation.

## Security

Signing keys, private certificates, OAuth secrets, and Store credentials must never be committed. See [SECURITY.md](../SECURITY.md).
