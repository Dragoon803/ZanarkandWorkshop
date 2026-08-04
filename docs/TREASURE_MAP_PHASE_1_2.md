# Treasure Map Editor — Phase 1 and 2 Findings

## Milestone outcome

The game contains enough information to build the editor without distributing its copyrighted map assets. The application can scan the user's extracted FFX `master` directory at runtime and build a read-only index from:

- `jppc/map/<area>/<field>/bin/mapout.vpa` — field map packages (`MAP1`)
- `jppc/event/obj/<group>/<event>/<event>.ebp` — event scripts (`EV01`, with an `ATEL` chunk)
- `jppc/battle/kernel/takara.bin` — the 498-entry treasure table

No game asset is copied into the application or changed during indexing.

## Implemented readers

- Strict `takara.bin` parser with the 0x14-byte header and 498 four-byte records.
- Strict `EV01` package/chunk reader and ATEL extraction.
- `MAP1` signature/header validation and field asset discovery.
- Event bytecode recognition for `setPosition` (`0x0013`), `obtainTreasure` (`0x015B`), and `obtainTreasureSilently` (`0x01A7`).
- Integer and floating-point constant-table resolution needed by those calls.
- A reusable, read-only `TreasureMapIndexBuilder` and a standalone smoke-test project.

## Results against the clean master

The first complete scan found:

- 498 treasure records
- 433 `MAP1` field packages
- 283 map fields with matching event packages
- 305 event packages parsed, with zero failures
- 326 event workers referencing treasure records
- 414 distinct treasure IDs referenced by those workers
- 25 conservative direct mappings where one worker has exactly one treasure ID and one explicit XYZ position

The workbook's first tab was useful as a validation source: its offsets agree with `0x14 + treasureId * 4`. It is not required by the released editor.

## Important limitation

An event worker that grants treasure is not always a physical chest. It may be an NPC, scripted reward, shared handler, or a worker whose position is set indirectly. Therefore the scanner deliberately reports evidence and only labels an entry “directly mappable” when the relationship is unambiguous. Treating all 414 referenced IDs as chest icons would create false locations.

The next research step is to correlate ATEL workers with event model-group (`.mgrp`) entries and map/world transforms. That should distinguish chest actors from non-chest rewards and resolve workers with indirect or multiple positions. Pixel placement on a 2D map also requires determining the `MAP1` projection/coordinate transform; raw XYZ values alone are not screen coordinates.

## Recommended continuation

1. Identify chest model IDs and link ATEL worker indices to `.mgrp` actors.
2. Decode or reproduce the field minimap projection from `MAP1` data.
3. Build a reviewed chest-location manifest from game-derived evidence, retaining confidence and provenance per entry.
4. Add the Avalonia editor UI only after the model and projection are stable: zoom/pan map, icon hover/select, drag position, edit contents, validate, and transactional save/backup.

This keeps the same architectural standards as the Sphere Grid and Battle Formation editors: FfxLib owns parsing/writing, the module uses a data model, loading is non-destructive, writes are validated and transactional, and uncertain data is surfaced rather than guessed.
