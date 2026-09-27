# Eizo v1.3.1

Release date: 2026-09-27

## Startup loading visual refinement

- Replaced the native `ProgressRing` below the in-app startup logo with a thin indeterminate `ProgressBar` sized 72 × 2.
- The indicator now uses the WinUI 3 theme resource `TextFillColorSecondaryBrush`, automatically following light and dark themes instead of using a fixed Windows accent blue.
- Made the progress track transparent so the startup logo remains the primary visual focus.
- Preserved the existing startup initialization and deferred-loading sequence; this release changes only the startup loading presentation.
- Bumped product / MSIX versions to 1.3.1 / 1.3.1.0.
