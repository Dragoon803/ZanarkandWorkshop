# Treasure Map Editor — Phase 10 release hardening

Phase 10 prepares chest-content editing for broader in-game testing. ATEL event packages remain read-only; the editor writes only `jppc\battle\kernel\takara.bin`.

## Automated safeguards

- The writer preserves the original 0x14-byte catalog header and record count.
- A staged file with a changed length or header is rejected before replacement.
- The staged file is parsed and verified byte-for-byte before installation.
- Smoke coverage round-trips Gil, normal item, key item, and equipment records together and confirms that unrelated records remain unchanged.
- Save failure guidance now identifies `takara.bin`, rather than the selected field map, as the recovery target.

## In-game test checklist

Use a disposable mod project and retain an unmodified master for Recovery.

1. Choose one known, reachable chest and record its original reward.
2. Save and test a Gil reward; verify the displayed hundreds-of-Gil value matches the amount received.
3. Repeat with a normal item and a quantity greater than one.
4. Repeat with a key item that is safe to obtain in the current save.
5. Repeat with one weapon or armor entry and verify owner, equipment type, slots, and abilities.
6. Save a second edit to the same `takara.bin` and confirm the later reward is received.
7. Edit an unpositioned chest through Previous Chest / Next Chest and confirm content editing does not depend on an icon.
8. Confirm the deployed mod contains `jppc\battle\kernel\takara.bin`; map and event files are source inputs for the editor and are not modified.

Perform progression-sensitive key-item tests on a disposable save. Some key items can affect story or inventory flags independently of the editor.

## Remaining manual validation

- Test on a lower-memory laptop while switching repeatedly between large fields.
- Record untranslated retail reward IDs or unclear location names for lookup-table cleanup.
- Exercise Recovery after deliberately restoring a known original `takara.bin`.
- Confirm error messages for incomplete source folders are understandable to a new user.
