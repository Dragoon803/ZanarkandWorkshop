# Treasure Map Editor — Phase 8

Phase 8 makes the editor safer and easier to navigate during testing.

## Project validation

Before scanning, the editor now verifies all required source groups:

- `jppc\battle\kernel\takara.bin`
- `jppc\battle\kernel\buki_get.bin`
- `jppc\map` containing `mapout.vpa` files
- `jppc\event\obj` containing `.ebp` files

A partial project receives one consolidated message listing every missing path and explaining that only `takara.bin` is needed for chest-content runtime deployment.

## Loading feedback

The application loading overlay now reports scan stages, including treasure/field indexing, event-field progress, projection, and field-list preparation. The heavy scan remains off the UI thread.

## Navigation

- Field filtering matches area ID, field ID, and chest count display.
- Chest filtering matches treasure ID, friendly contents, and event ID.
- Filtering reuses the same edit rows, so pending changes are not discarded.
- Fields display both their area and field identifiers.

## Unsaved changes

Opening another project editor or loading another project while treasure edits are pending now asks for explicit confirmation before discarding them.

## Verification

- Complete clean master: accepted and fully scanned.
- Empty/partial master: all four missing prerequisite groups reported.
- Treasure smoke build: zero errors.
- Full scan: zero MAP1 or event parse failures.
