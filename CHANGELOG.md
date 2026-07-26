# Changelog

All notable changes to Zanarkand Workshop will be documented here.

## [0.2.0] - 2026-07-25

### Added

* Added safer Battle Script copying, insertion, replacement, and deletion workflows.
* Added worker function and jump navigation, including jump-destination editing.
* Added recoverable, transactional validation for manual Battle Script hex edits.
* Added modal, user-friendly details for Battle Script information, success, warning, and error messages.
* Added session-aware Battle Script undo and redo support.
* Added `[cyan]...[/cyan]` formatting, explicit line breaks, and unsupported-character validation for editable game text.
* Added verified backup and original-file restoration workflows to command and item editors.

### Changed

* Battle Logic groups containing `RETURN (3C)` now preserve the protected return while allowing adjacent editable instructions to be deleted or copied.
* Deleting jump-reference logic retains its jump-table destination and rebuilds later offsets.
* Rejected manual hex edits now roll back automatically and can be recovered with **Restore Rejected Edit**.
* Battle Script messages now use concise severity-specific banners with expanded explanations.
* Revert, Undo, and Redo are shown only while the Battle Script tab is active.
* Reorganized command, item, and monster-command properties into clearer editing categories.

### Fixed

* Fixed message-banner styling being cleared by duplicate Battle Logic selection events.
* Fixed failed manual edits remaining in the primary hex view until Undo was pressed.
* Fixed grouped `RETURN` protection unnecessarily preventing deletion of neighboring instructions.
* Fixed command saves clearing unexposed `UsageFlags` bits used by battle cameras and command presentation.
* Fixed the Player & Aeon Commands view failing to open because stray text was interpreted as a grid column.

## [0.1.0] - 2026-07-22

### Added

* Expanded monster Battle Script inspection and editing tools.
* Original-game-file verification and recovery support.
* Editor help and recovery dialogs.
* Window-size preferences and recent-project support.
* Zanarkand Workshop branding and independent versioning.

### Changed

* Renamed the application from FFX Project Editor to Zanarkand Workshop.
* Set the fork's initial version to 0.1.0.
