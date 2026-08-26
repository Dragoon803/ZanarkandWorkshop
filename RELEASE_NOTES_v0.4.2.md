# Zanarkand Workshop v0.4.2

Zanarkand Workshop v0.4.2 focuses on safer project workflows, verified Recovery, and stronger protection for edited game data.

Project switching now keeps the previous project active if preparation fails. Folder Recovery commits all replacements together or restores the original project files, and every Recovery source is re-verified immediately before it is used.

## Project workflow

* Register named Workshop projects and manage them through **Known Projects**.
* Use **Save As** to copy the active project and its Workshop metadata into a new project.
* Save, Discard, or Cancel pending changes before switching editors, monsters, formations, projects, or closing the application.
* Use footer **Save As** from a clean or modified editor; ordinary **Save** remains available only when changes are pending.

## Bug fixes  
* Corrected the sphere grid color change not saving.
* Corrected the recovery issue where m008 was causing users to be unable to use the recovery feature.

## Recovery and data safety

* Verify configured original game files against the packaged trusted manifest and review targeted, privacy-safe diagnostics.
* Continue with explicit warnings when a structurally usable Recovery source is unrecognized or contains files outside the trusted manifest.
* Recover individual Monster Status, Loot, and Battle Script sections while protecting unrelated sections.
* Localized Monster saves now stage and verify both `mXXX.bin` and `monsterN.bin`, restoring both originals if either installation fails.
* Sphere Grid layout and content files now save as a paired transaction with automatic rollback.

## Editor improvements

* Monster command, item, and dropped Auto Ability selectors use names from the active project and identify unknown entries clearly.
* Sphere Grid nodes can use editor-only section colors stored in project metadata without changing game-owned route data.
* Fixed a Monster serializer header-size error that could overwrite the first four Battle Script bytes during an unrelated Status save.
* Prevented Battle Script parsing and Recovery from dirtying source buffers merely by opening them.
* Numeric changes in Items, Player & Aeon Commands, Standard Monster Commands, and Boss Commands now enable Save immediately and participate in pending-change protection.
* Wide command tables use fully manual scrolling: focusing or editing an edge cell no longer repositions the table, and only the Index column remains frozen.

## Compatibility

This release supports the Windows Steam version of Final Fantasy X/X-2 HD Remaster with US `new_uspc` and shared `jppc` game data. Keep a clean extracted `master` folder available for Recovery and test edited game data before distributing a mod.
