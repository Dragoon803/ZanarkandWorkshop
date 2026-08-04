# Battlefield viewer research — phases 1–3

## Phase 1: asset discovery

Battlefield packages are stored under `jppc\btlmap`, separately from normal field maps. The game assigns each package a stable ID (`0x401`–`0x439`) and a code such as `bsil03_a` or `bika03_c`. `BattlefieldAssetCatalog` now discovers installed packages and correlates unambiguous formation names such as `bsil03_00` with `bsil03_a`. Stages with multiple variants remain explicit candidates until the battle-list association is parsed; the editor must not silently choose one.

## Phase 2: lightweight wireframe decoding

The battlefield `mapout.vpa` is a MAP1 archive. Section 1 contains the full textured VIF model, but section 2 normally contains the much smaller gameplay height/collision mesh. `BattlefieldHeightMap` decodes its packed vertices, triangle indices, and attributes without loading textures or full visual geometry. This is the appropriate low-memory source for an editor wireframe. A few special-purpose packages omit section 2; callers can detect those with `TryRead` and keep the current empty grid rather than failing.

The structure and coordinate conversion were cross-checked against noclip.website's open FFX parser. Fahrenheit's battle-map ID list was used to verify canonical package IDs and variant names.

## Phase 3: coordinate alignment

Packed height-map coordinates use the stored scale divided by ten. World coordinates are `X = packed X / scale`, `Y = -packed Y / scale`, and `Z = -packed Z / scale`. Formation positions already use game-world floats, so the surface and formation points can share one canvas transform. The smoke test verifies every discovered retail battlefield mesh and the unambiguous Besaid formation/package match.

## Phase 4: exact formation association

`jppc\battle\kernel\btl.bin` is the authoritative formation pool table. `BattlefieldFormationIndex` resolves each listed formation to its numeric map ID, including cases where filenames are misleading. The encounter byte is printed as a decimal filename suffix (`0x0A` becomes `_10`). For example, `bika03_00` uses `bika01_a`, `bika03_10` uses `bika03_b`, and `bika03_20` uses `bika03_c`. IDs in the dedicated battlefield range (`0x401` and above) resolve through `BattlefieldAssetCatalog`. Lower IDs identify field/event maps used by scripted encounters and intentionally return no dedicated battlefield package.

The retail pool table directly covers 826 of the 863 physical formation files. The other 37 are direct/scripted formations outside the pool table. They must remain explicitly unresolved unless another authoritative association is found; the editor must not guess between `_a`, `_b`, and `_c` variants.

## Phase 5: editor integration

The Battle Formation canvas now loads only the selected formation's decoded collision surface and draws it as a read-only wireframe behind the existing position markers. Fitting and centering include both the surface and formation positions. Changing formations replaces the previous surface reference rather than caching maps. If `btl.bin` has no proven assignment, the selected package omits collision geometry, or the project does not contain `jppc\btlmap`, the editor keeps the normal grid and explains why above the viewer.

Testing requires the existing `jppc\battle\btl` formation files, `jppc\battle\kernel\btl.bin`, and the `jppc\btlmap` folder. No battlefield files are edited when saving a formation.
