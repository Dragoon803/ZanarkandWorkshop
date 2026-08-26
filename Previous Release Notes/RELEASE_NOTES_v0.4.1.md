# Zanarkand Workshop v0.4.1

Zanarkand Workshop v0.4.1 is a safety hotfix for the Sphere Grid Editor.

## Sphere Grid safety

* Creating and saving game-compatible grids is now limited to 860 nodes.
* Link totals are limited to the tested safe capacities: 1,021 for Standard/Original and 934 for Expert.
* Each node is limited to five links so every displayed connection remains usable for movement and activation in game.
* The editor explains when the safe node limit has been reached.
* Creating a node or link now displays a yellow caution explaining that a saved structure cannot currently be deleted in the editor.
* The caution can be hidden with **Don't show this warning again**.

Testing confirmed that grids above 860 nodes may open successfully but can corrupt Sphere Grid visual memory and crash Final Fantasy X when the grid closes. Existing oversized files can still be inspected so they can be diagnosed or recovered, but they cannot be saved as game-compatible grids until their node count is safe.

## Compatibility

This hotfix supports the Windows Steam version of Final Fantasy X/X-2 HD Remaster. Keep a clean extracted `master` folder available for Recovery and test edited game data before distributing a mod.
