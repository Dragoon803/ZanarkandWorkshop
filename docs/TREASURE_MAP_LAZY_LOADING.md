# Treasure Map Editor — Lazy Field Loading

The editor no longer scans every event file before opening.

Initial load now reads only:

- the prerequisite check
- `takara.bin`
- the field/map directory
- event filenames grouped by field

The map area intentionally remains blank until the user selects a field. Selecting a field asynchronously loads only that field's `mapout.vpa`, associated `.ebp` event files, chest candidates, and projections. The field row is then updated with its discovered chest count.

Only the currently selected field remains loaded. Selecting another field releases the previous geometry, event scan, and chest projections before loading the new field. A selection-generation guard prevents a slower previous selection from replacing a newer selection if the user clicks fields quickly.

The blank map distinguishes four states: no selection, loading, no confirmed chests, and field-load failure.
