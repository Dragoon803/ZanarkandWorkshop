# Treasure Map Editor — Phases 4 and 5

## Phase 4: MAP1 and guide-map geometry

Executable tracing confirms that `mapout.vpa` is a `MAP1` archive with a 16-entry relative-offset table beginning at file offset `0x10`. Section 11 contains the dedicated guide-map/minimap model data used by `Yn_GuideMapSetData`.

A `YNGM` model contains indexed triangles, signed 16-bit XYZ vertices, bounds, a short-to-float conversion scale, and local matrices. The editor can therefore reconstruct its background from the user's installed game rather than package copyrighted map images.

Clean-master validation:

- 433 field packages scanned
- 259 fields with dedicated guide-map geometry
- 292 guide-map models, including alternate states
- zero guide-map parse failures
- empty 64-byte `MAP1` placeholders handled
- five retail models with the shorter trailing-matrix layout handled explicitly

## Phase 5: exact coordinate transform

Runtime analysis closes the missing link between ATEL actor coordinates and guide-map geometry:

- the default guide scene factor is `10.0`
- the retail section-11 archives contain `YNDT`, `YNGM`, and `YNED`, with no per-field scene override
- consequently `guideX = worldX / 10` and `guideZ = worldZ / 10`
- the minimap angle rotates the live camera presentation; the static editor uses a north-up map, so no camera rotation is applied
- guide coordinates are fitted to the canvas with preserved aspect ratio and centered padding

The projection is implemented by `GuideMapProjection.ProjectWorld`. The SVG test renderer overlays recovered chest positions and emits a title tooltip for each marker. The generated `kami00` validation map contains five chest markers aligned to model state 0.

Phase 5 is complete: field-world positions can now be converted to deterministic map pixels without an approximation or bundled image asset.
