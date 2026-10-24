## [1.1.4]

- Add Left / Center / Right selection to the Panorama main menu, applied immediately to all submenus for that player.
- Add `PanoramaMenuPosition` config default (`center`), with case-insensitive validation and safe fallback.
- Keep per-player overrides until disconnect/map change; clear them on reset to prevent slot reuse leaks.
- Reuse existing `utils-dialog` / `pos-left`, `pos-center`, `pos-right` CSS classes; supplied swiftly_menu_ui needs no resource recompilation.
- Preserve beta.26 compatibility, diagnostic logging, existing permissions, material projection and database schema.
- Extend EN/RU translations, alignment logic tests and static CSS contract checks.

## [1.1.3]

- Add opt-in bounded diagnostic logs: GAME/VPK material lookup, placement geometry, spawn/next-update entity state, database lifecycle, edits, preferences and per-player transmit decisions.
- Add admin-only read-only `mapdecaldebug [decal-id|paths]` snapshots with a two-second rate limit; server console supported.
- Rotate debug files at approximately 5 MiB with two archives; preserve console output on file errors; escape/truncate diagnostic fields.
- Keep existing Panorama layout, projection algorithm, permissions and database schema unchanged.
- Add Russian diagnostic instructions and pure logging/path/rotation tests (88 total logic/SQLite/diagnostic assertions).
- Verified Release compilation and Panorama static contract on beta.26 dependencies; live CS2/client rendering remains unverified.

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