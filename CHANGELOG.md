# Changelog

All notable changes to Zanarkand Workshop will be documented here.

## [0.4.2] - 2026-08-23

- Made project switching prepare all fallible registry and metadata work before publishing the new active project.
- Made multi-file folder Recovery transactional with verified staging and automatic rollback.
- Re-verifies every Recovery input immediately before use and requires explicit approval for unverified files.

### Added

* Added named project registration, Known Projects management, and a project-level Save As workflow that copies the active project and its Workshop metadata.
* Added shared Save, Discard, and Cancel prompts when leaving editors or selections with pending changes.
* Added trusted Recovery-source verification, targeted diagnostics, and explicit warnings for unrecognized or extra recovery files.
* Added per-project Sphere Grid section-color metadata and controls for assigning or resetting node colors without changing game-owned route data.
* Added project-registry and Recovery-verification smoke coverage.

### Changed

* Made footer **Save As** available whenever its editor context is available, while keeping **Save** limited to pending changes.
* Improved Monster command, item, and dropped Auto Ability selectors with current project names and clearer unknown-entry labels.
* Improved project switching, recent-project handling, save routing, metadata migration, and active-editor ownership checks.
* Expanded Monster section Recovery and validation so unrelated Status, Loot, Battle Script, Worker, and unknown data are preserved.

### Fixed

* Fixed Monster serialization reserving too little header space and overwriting the first four Battle Script bytes during unrelated saves.
* Prevented opening a recovered Battle Script from mutating its source buffer.
* Made localized Monster saves and Status Recovery transactional across `mXXX.bin` and `monsterN.bin`, with verified staging and automatic rollback.
* Added transactional Sphere Grid saving so paired layout/content files roll back together after a failed write.
* Fixed pending editor changes being silently lost during project, editor, and internal selection transitions.
* Fixed numeric grid edits in Items and command editors not enabling Save or participating in dirty-state checks.
* Made scrolling fully manual in Items and command editors so focusing or editing a cell never repositions the table, and kept only the compact Index column frozen.

## [0.4.1] - 2026-08-05

### Changed

* Limited Sphere Grids to 860 nodes when creating or saving game-compatible grids. Larger grids can corrupt the game's Sphere Grid visual memory and crash when the grid closes.
* Limited Standard/Original grids to 1,021 total links and Expert grids to 934 total links, based on successful in-game testing.
* Limited each node to five links so all displayed connections remain functional for movement and activation.
* Added a caution before creating a node or link explaining that saved structures cannot currently be deleted in the editor.
* Added a **Don't show this warning again** preference for the creation caution.

### Fixed

* Prevented the Sphere Grid Editor from producing node counts known to be unsafe in game.

## [0.4.0] - 2026-08-04

### Added

* Added an Auto Ability Editor for properties, effects, names, descriptions, and customization recipes.
* Added a Rikku Mix Recipe Editor for searching, sorting, and changing ingredient pairs and result commands.
* Added a Battle Formation Editor for changing enemy parties and editing formation coordinates visually or numerically.
* Added a visual Sphere Grid Editor for nodes, links, types, positions, sections, find-and-replace, and undo.
* Added a Treasure Map Editor for viewing mapped chests and changing individual rewards.
* Added single-entry recovery for Auto Abilities and treasure rewards.
* Added expanded Monster Editor constraints and direct Enter/Escape numeric editing.
* Added smoke tests for battle formations, battlefields, command flags, Sphere Grids, Treasure Maps, command-slot expansion, and monster Battle Scripts.

### Changed

* Improved Monster Editor status, element, stat, basic-property, and loot layouts.
* Restricted monster drops, steals, and bribes to project item data with current item names.
* Added a dedicated Ronso Rage command selector using current Player and Aeon command names.
* Corrected unsigned monster fields and reward limits to better match Fahrenheit and the game structures.
* Expanded command editors to safely add command slots while preserving existing records and references.
* Improved the in-app guide and GitHub documentation for the new editors.
* Removed automatic `.bak` file creation to prevent backup files from accumulating beside edited game files.
* Promoted the Battle Formation Editor from experimental status after extensive in-game formation testing.

### Fixed

* Correctly mapped Rikku Mix result IDs to their corresponding `command.bin` command names.
* Improved fixed column sizing, sorting, filtering, and sticky headers in the Rikku Mix Recipe Editor.
* Improved handling of missing files and folders in the Rikku Mix and Battle Formation editors.
* Fixed Auto Ability text recovery so manually changed primary and alternate offsets are rebuilt safely.

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
