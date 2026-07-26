# Zanarkand Workshop v0.2.0

Zanarkand Workshop v0.2.0 expands the monster Battle Script editor with safer structural editing, clearer feedback, and recoverable manual-edit workflows.

## Highlights

* Copy, insert, replace, and delete Battle Logic with rebuilt script offsets.
* Remove jump-reference logic without deleting its jump-table destination.
* Edit groups such as `00 00 3C` while preserving the protected `RETURN (3C)`.
* Navigate workers, functions, and jump destinations more directly.
* Recover rejected manual hex edits after the editor automatically restores the last valid script.
* Undo and redo successfully applied Battle Script changes.
* Open concise message details in a modal window that remains in front of the editor.
* Use `[cyan]...[/cyan]` formatting and explicit line breaks in supported game-text fields.
* Edit command, item, and monster-command properties in reorganized categories with verified save and restoration workflows.
* Preserve hidden command presentation and battle-camera flags when editing and saving command data.

## Safety

The standard editor continues to protect function-ending `RETURN (3C)` instructions and other structural invariants. Failed manual edits are rejected transactionally and do not become Undo entries. A future Full Control mode is planned for advanced restructuring that intentionally overrides selected protections.

Command saves also retain raw flag bits that are not currently exposed in the interface, preventing unrelated edits from changing command camera behavior.

## Compatibility

Zanarkand Workshop supports the Windows Steam version of Final Fantasy X/X-2 HD Remaster. Back up modded files before editing and test Battle Script changes in a controlled encounter before distributing them.
