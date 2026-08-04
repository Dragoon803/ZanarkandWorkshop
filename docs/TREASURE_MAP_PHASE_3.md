# Treasure Map Editor — Phase 3 Research

## Chest actor identification

Physical chest actors can be identified directly in event ATEL bytecode. Their initialization code calls `loadModel` (`0x0001`) or `loadModel2` (`0x0134`). The game model dictionary identifies:

- `0x5002` — `TAKARABOX002`, the standard field treasure chest
- `0x50AA` — the blue field treasure chest

The scanner now records each worker's loaded model IDs and only calls a worker a confirmed chest when it loads one of these models. On the clean master, 232 of the 326 treasure-granting workers are confirmed chest actors. This removes NPC gifts, scripted rewards, and other non-physical treasure grants from the prospective map layer. The first confidence pass classifies 23 as exact, 132 as conditional, and 77 as unresolved.

Locations carry explicit confidence:

- `Exact` — confirmed chest model, one treasure record, and one initialization XYZ
- `Conditional` — confirmed chest model and one treasure record, but multiple initialization positions selected by script state
- `Unresolved` — confirmed chest model, but its treasure or initialization position is not uniquely recoverable yet
- `NotAConfirmedChest` — treasure-granting worker without a recognized chest model

Position evidence also records the owning ATEL function. Function zero is the actor initializer and is preferred over later movement or interaction code.

## What the in-game minimap actually is

The field minimap is not a pre-rendered bitmap stored alongside each field. Read-only decompilation of the PC executable shows that the engine maintains a separate minimap camera and world matrix:

- `graphicMinimapCameraSetWorldMatrix` inverts and converts a supplied 4x4 world matrix.
- It passes that matrix to `PhyreScene::setMinimapCameraWorldMatrix`.
- `PhyreScene::setMinimapCameraWorldMatrix` installs the matrix on a dedicated camera and updates its view matrices.
- `graphicMinimapSetEnable` and `graphicSetMiniMapType` control rendering state.

This strongly indicates that the cyan in-game minimap is a camera rendering of field geometry, not an image asset that can simply be extracted. `mapout.vpa` contains the field geometry/material payload needed by that renderer.

## Editor implication

The copyright-friendly design is still valid, but “load the user's minimap image” becomes “derive a 2D map from the user's field geometry.” There are two practical paths:

1. Decode enough `MAP1`/Phyre geometry to reproduce the minimap camera offline. This is the faithful long-term solution.
2. Generate a simplified top-down map from recovered walk/navigation geometry. This may be much faster and clearer for an editor, while still using only the user's game data.

The next work item is to locate the geometry section consumed by the minimap render pass and recover the camera framing parameters. Until that transform is known, XYZ positions are reliable game coordinates but not yet reliable editor pixels.
