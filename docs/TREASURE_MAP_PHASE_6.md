# Treasure Map Editor — Phase 6

## Projected chest-location index

Phase 6 introduces `ChestLocationIndexBuilder`, the editor-facing bridge between the treasure/event scan and the decoded guide-map models. It creates a projected record containing:

- field and event identifiers
- ATEL worker index and source event path
- treasure record identifier or identifiers
- recovered world X/Y/Z position
- normalized guide X/Z position
- matching guide-map state
- canvas pixel X/Y position
- exact, conditional, or unresolved confidence
- provenance describing the ATEL script offset and MAP1 model state

Alternate initialization positions and alternate guide-map states remain separate records. This is intentional: the editor can expose the relevant condition or state without discarding evidence or inventing a single location. Summary APIs also count unique event workers so alternate records do not inflate the apparent chest count.

## Confidence policy

- **Exact:** one treasure record, one recovered initialization position, and one matching guide-map state.
- **Conditional:** one treasure record but multiple initialization positions and/or matching map states.
- **Unresolved:** multiple possible treasure records, no recovered constant position, no guide model, or no compatible map state.

No unresolved value is silently guessed. Those entries remain in the index for later control-flow analysis and manual review.

## Clean-master verification

The end-to-end smoke scan currently reports:

- 498 treasure records
- 433 MAP1 fields
- 259 guide-map fields and 292 models
- 305 event files parsed with zero failures
- 232 workers confirmed to use chest models
- 23 exact pre-projection worker mappings
- 18 exact projected workers
- 132 conditional projected workers
- 82 unresolved projected workers
- 476 projected records when alternate positions and map states are retained (18 exact, 361 conditional, 97 unresolved)

The smoke test also renders a `kami00` SVG with five projected markers. Phase 6's data/index milestone is complete; the next phase can consume this index in the interactive Avalonia editor and add controlled write-back transactions.
