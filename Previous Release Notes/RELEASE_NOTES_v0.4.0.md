# Zanarkand Workshop v0.4.0

Zanarkand Workshop v0.4.0 adds several major editors and expands existing tools with safer, clearer controls.

## Highlights

* Edit Auto Ability names, descriptions, properties, effects, and customization recipes.
* Edit Rikku Mix recipes by selecting two ingredients and their resulting command.
* Edit battle enemy parties and adjust formation positions using a visual graph or exact coordinates.
* Redesign Sphere Grids by editing nodes, links, types, positions, and sections.
* View treasure chests on reconstructed maps and change individual rewards.
* Add and edit command slots while preserving existing command records and references.
* Use current project item and command names throughout Monster Editor selectors.
* Edit monster numeric fields with clearer limits and Enter/Escape keyboard controls.
* Work with reorganized status, element, stat, basic-property, and loot layouts.
* Use the expanded in-app guide for editor requirements and game-text formatting.
* Restore one Auto Ability or treasure reward without replacing unrelated edits.

## Reliability

The release includes round-trip tests for formations, commands, Sphere Grids, battlefields, and treasure data, plus command-slot and Monster Battle Script testing. Recovery reads from a clean original master folder and applies only the requested data.

The Battle Formation Editor has also completed extensive in-game testing with normal enemies, large monsters, bosses, mixed parties, and advanced coordinate changes.

## Compatibility

Zanarkand Workshop supports the Windows Steam version of Final Fantasy X/X-2 HD Remaster. Keep a clean extracted `master` folder available for recovery, and test edited game data before distributing a mod.
