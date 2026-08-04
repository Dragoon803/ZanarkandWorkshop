# Treasure Map Editor — Phase 7 Testing Build

The first interactive Avalonia editor is available from **Editors → Treasure Map Editor**.

Implemented for testing:

- scans the active project rather than packaging map assets
- lists fields containing projected chest records
- renders decoded guide-map triangles
- switches between alternate guide-map states
- pans, wheel-zooms, button-zooms, and fits the map
- displays clickable gold chest markers and hover summaries
- shows world coordinates, confidence, and source provenance
- edits the four-byte `takara.bin` treasure record (kind, quantity, and type/ID)
- retains edits while switching between fields
- validates a staged catalog before replacement and automatically rolls back a failed save

Position editing remains deliberately read-only in this build. Moving a chest requires changing ATEL constants inside the corresponding event package, not merely changing the visual marker. That writer will be enabled only after its package reconstruction and round-trip validation are complete.

For an initial in-game check, use a disposable editing project, choose an exact chest, note its treasure ID and original values, make a distinctive content change, save, and open that chest in game. Recovery can restore `jppc/battle/kernel/takara.bin` from the configured original-game master.
