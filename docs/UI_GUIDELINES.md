# UI implementation guidelines

Eikura treats the Figma file as an upper-level reference for information hierarchy, proportions, and design language. It is not a pixel contract.

## Native-first rules

1. Prefer WinUI 3 controls, visual states, system typography, Mica, theme resources, native flyouts/dialogs, and built-in transitions.
2. Never reproduce a Figma artifact with absolute positioning when a responsive Grid, NavigationView, ListView, ItemsRepeater, ContentDialog, ToggleSwitch, ComboBox, or another native control expresses the same intent.
3. When the design conflicts with native Windows behavior, accessibility, DPI scaling, localization, or responsive layout, fix the implementation rather than preserving the flaw.
4. Product content shown in the design is sample data. UI implementation must not couple layout logic to those samples.
5. Reuse proven project-family solutions before inventing new chrome.

## Reused product-family patterns

- SpatialViewer: Mica window, custom title bar, detached 32-DIP title tabs, NavigationView, native theme transitions.
- UrbanPlanToolbox: native card surfaces, NavigationView lifecycle, settings-card hierarchy, localization conventions.
- Eikura: Japanese-media-first information hierarchy and playback-focused immersive tabs.

## 0.1 scope

0.1 is a UI foundation milestone. Playback, metadata, storage and cloud-provider logic remain behind future boundaries while navigation and interaction surfaces stabilize.
