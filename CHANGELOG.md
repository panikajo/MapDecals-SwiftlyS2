## [1.1.2]

- Change the default PanoramaLayout to panorama/layout/custom_game/swiftly_menu_ui.xml as requested.
- Preserve SwiftlyS2 1.5.0-beta.26 compatibility and all utils-* panel IDs.
- Existing config values are preserved: update PanoramaLayout manually if it still points to mapdecals_menu_ui.xml.
- The separately named mapdecals_menu_ui assets/build kit remain optional; they require an explicit config override.

## [1.1.1]

- Build against SwiftlyS2.CS2 1.5.0-beta.26 to match the reported server installation.
- Set MinimumAPIVersion to 1.5.0-beta.26; the prior stable minimum rejected this prerelease server.
- Refresh the dependency lock and installation notes; retain all Panorama functionality.
- Verified: Release compilation, publish, 61 logic/SQLite assertions. Live server loading still needs confirmation.

## [1.1.0]

- Target SwiftlyS2 1.5.0 stable; pin direct dependencies and add lock files.
- Add opt-in Custom HUD Panorama editor using the supplied menu assets under isolated paths.
- Add owner/permission validation, bounded chat input, pagination and confirmed deletion.
- Add English/Russian UI strings, per-decal Panorama write serialization and save-failure handling.
- Restore player transmit rules, await preference loading and guard stale map-load callbacks.
- Clean up HUD/decal state on unload; avoid duplicate round spawns and stale-handle deletion.
- Fix classic editor's Z-value initialization and invariant coordinate formatting.
- Add portable ZIP packaging, CI and 61 logic/SQLite assertions.
- Compiled Panorama resources and live CS2 validation remain required before deployment.

# Changelog

All notable changes to this project will be documented in this file.

# 1.0.0
Initial Release