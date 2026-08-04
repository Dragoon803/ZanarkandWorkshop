# FFX Sphere Grid ImHex patterns

These patterns expose the two compact files that form one Sphere Grid.

| Grid | Layout and links | Authoritative node types |
|---|---|---|
| Original | `dat01.dat` | `dat09.dat` |
| Standard | `dat02.dat` | `dat10.dat` |
| Expert | `dat03.dat` | `dat11.dat` |

## Use

1. Open a file in ImHex.
2. Open the Pattern Editor and load the matching `.hexpat` file.
3. Run the pattern.
4. Expand `sphere_grid` or `sphere_grid_node_content` in Pattern Data.

For a node numbered **N**, inspect `nodes[N]` in the layout and
`node_types[N]` in the paired content file. ImHex array indices are zero-based,
matching Zanarkand Workshop's displayed node numbers.

The layout's `redundant_node_type` is not always reliable, especially in the
Expert grid. The byte in `dat09/10/11.dat` is the authoritative type used by
the game. A link whose `anchor_node_index` is `0xFFFF` is straight; any other
value identifies the node used as the center when constructing its circular arc.

Editing Pattern Data changes the open ImHex provider. Save a backup first and
keep both files' `node_count` values synchronized when appending records.
