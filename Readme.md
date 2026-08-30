# Zanarkand Workshop

<p align="center">
  <img src="ZanarkandWorkshop/Assets/ZanarkandWorkshop.png" alt="Zanarkand Workshop logo" width="560"/>
</p>

## Modern all-in-one toolkit for editing Final Fantasy X HD Remaster game data. 
Zanarkand Workshop is under active development, with new tools, improved workflows, and ongoing enhancements. Originally based on osdanova/FFXProjectEditor v1.2, it has grown into a significantly expanded editor.  
  
Modify auto abilities, redesign monster battle AI, customize commands, adjust encounter formations, and more.

The current release is **v0.4.2**, featuring named project management and Save As,
shared Undo/Redo history, verified Recovery, safer editing transitions, and
expanded Sphere Grid, Treasure Map, Battle Formation, and Monster tools.

## Features
* Monster Editor
* Monster Battle Script Editor
* Auto Ability Editor
* Item Editor
* Player & Aeon Command Editor
* Enemy Command Editor
* Battle Formation Editor
* Rikku Mix Editor
* Sphere Grid Editor
* Treasure Chest Editor
* Live Memory Utilities
* Inventory Tracker
* Arena Tracker
* Live Battle Tracker
  
  ### ⚠️ Compatibility Warning

It is strongly recommended that you **do not edit game files with other modding tools and then load those same files into Zanarkand Workshop**.

Different modding tools may modify or structure game data differently, which can cause compatibility issues, unexpected behavior, or errors when Zanarkand Workshop attempts to read or modify those files.

For the most reliable experience, use **clean, unmodified game files** as the starting point when working with Zanarkand Workshop.
## Known limitation

* `Ctrl+Shift+Z` may invoke Undo instead of Redo outside text fields. Use
  `Ctrl+Y` or the **Redo** button instead.


## v0.4.2 highlights

* Added named project management and **File > Save As**, which creates a complete
  copy in a new project folder.
* Added **Undo**, **Redo**, **Undo All**, and **Save** controls across
  the editors, with detailed history feedback and pending-change protection.
* Reworked the Sphere Grid link creator feature.
* Dropdown menus now use the active names for things like Auto Abilities, monster names, commands, etc instead of static ones.

## Future updates planned

* Add a weapon editor.
* Add a `ply_save` editor.

## Getting Started

Download the latest Windows package from the Releases section of this fork,
extract it, and run `ZanarkandWorkshop.exe`.

Extract the game files using an extractor such as [VBF Browser](https://www.nexusmods.com/finalfantasy12/mods/3).

The folder that the app uses (and needs to be loaded) is `ffx_ps2/ffx/master`.
Inside this folder are directories containing data for every region. Currently,
this tool only supports US game data; both `new_uspc` and `jppc` are needed.

I recommend using the [External File Loader](https://www.nexusmods.com/finalfantasyxx2hdremaster/mods/150).
This creates a `mods` folder inside your FFX folder where custom files can be
loaded by the game. Once installed, copy your `master` folder to
`mods/ffx_ps2/ffx/` and load that folder in the editor to immediately modify
the files used by the game.

**It is also recommended that you keep a clean version of the master files, as
they are needed for the recovery feature of this program.**

## How to use

Click **Open Project Folder...** and select your master folder. The app detects
the running game automatically; the **FFX Connected** indicator shows the
connection status.

<img src="ReadmeAssets/ZW_Home_Screen.png" alt="Zanarkand Workshop home screen" width="900"/>

Most editor footers provide **Undo**, **Redo**, **Undo All**, and **Save**.
`Ctrl+Z` and `Ctrl+Y` use the same editor history as those buttons. When leaving
an editor with pending changes, choose **Save**, **Discard**, or **Cancel**.
Use **File > Save As** to create a complete copy of the active project under a
new project name.

<img src="ReadmeAssets/ZW_Guide_1.png" alt="Zanarkand Workshop text editing and project requirements guide" width="720"/>

<img src="ReadmeAssets/ZW_Guide_2.png" alt="Zanarkand Workshop editor guide" width="720"/>

### Monster Editor

* Live testing: Monster changes are loaded when combat starts, so a saved
  monster file applies to the next encounter.

Edit monster stats, elemental affinities, status resistances, loot drops, and
other properties.  
**Recovery > Restore Original Status** is a new recovery feature that allows you to return an  
individual tab back to it's original game version. so if you've made a lot of changes in Battle Script  
But need to reset status or loot you can do so without reverting Battle Script.

<img src="ReadmeAssets/ZW_Monster_Editor1.png" alt="Zanarkand Workshop Monster Editor" width="900"/>

The Battle Script editor can change attacks, targets, conditions, jumps, shared
values, and other monster behavior. v0.2.0 adds safer structural copying,
insertion, replacement, and deletion; worker/function/jump navigation; protected
`RETURN (3C)` handling; transactional manual hex validation; and recoverable
rejected edits.

<img src="ReadmeAssets/ZW_Monster_Editor2.png" alt="Zanarkand Workshop Monster Battle Script Editor" width="900"/>

### Items

* Live testing: Loaded when the game starts. The **Load Ingame** button can be
  used to see changes without restarting.

Edit item names, descriptions, targeting, effects, formulas, elements, statuses,
and related properties.

<img src="ReadmeAssets/ZW_Item_Editor.png" alt="Zanarkand Workshop Item Editor" width="900"/>

### Rikku Mix Recipes

Edit the result command produced by any two usable items. Recipes are loaded
from `jppc/battle/kernel/prepare.bin` and command names come from
`new_uspc/battle/kernel/command.bin`.

Search or sort the recipe list, choose ingredients and results from dropdowns,
then save the updated Mix table.

<img src="ReadmeAssets/ZW_Rikku_Mix_Editor.png" alt="Zanarkand Workshop Rikku Mix Editor" width="900"/>

### Auto Abilities

Edit the Auto Abilities used by weapons and armor. The editor reads
`new_uspc/battle/kernel/a_ability.bin` and the customization recipes stored in
`jppc/battle/kernel/kaizou.bin`.

Either file can be edited independently. If one is missing, its corresponding
tab is disabled and saving updates only the file that was loaded. The editor
refuses to open only when both files are missing.

The **Properties & Effects** tab includes:

* The ability name and description.
* Basic display and grouping properties.
* Elemental effects.
* Permanent, temporary, and extra status effects.
* Direct stat increases and the percentage increase amount.
* Separate Strength, Magic, Defense, and Magic Defense calculation bonuses.
* Confirmed special effects such as Sensor, Counterattack, Auto-Potion,
  Break Damage Limit, No Encounters, and Capture.

Direct stat increases change the stored combat stat and remain subject to that
stat's normal cap. The four Bonus flags do not change the displayed combat stat;
they apply separately during the game's calculations.

The **Recipe** tab controls whether the ability is customized onto weapons or
armor, the required item, and the required quantity. Recipe quantities are
limited to the game's inventory maximum of 99. Some auto abilities do not have  
recipes and disable the Recipe tab.

Names and descriptions use the same supported-character, cyan-formatting, and
line-break rules described under **General text editing**. Saving rebuilds the
text offsets in `a_ability.bin` and writes the corresponding recipe changes to
`kaizou.bin`.

Use **Restore Original Ability** to recover only the selected ability's
properties, text, and matching recipe. Other Auto Abilities remain unchanged,
and Save rebuilds valid text offsets for the current file.

<img src="ReadmeAssets/ZW_Auto_Ability_Editor.png" alt="Zanarkand Workshop Auto Ability Editor" width="900"/>

### Player & Aeon Commands

Edit the commands used by the party and Aeons.

<img src="ReadmeAssets/ZW_Player_Aeon_Commands_Editor.png" alt="Zanarkand Workshop Player and Aeon Command Editor" width="900"/>

### Standard Monster Commands

Edit commands used by standard enemies.

<img src="ReadmeAssets/ZW_Standard_Monster_Commands_Editor.png" alt="Zanarkand Workshop Standard Monster Command Editor" width="900"/>

### Boss Commands

Edit commands used by bosses.

<img src="ReadmeAssets/ZW_Boss_Commands_Editor.png" alt="Zanarkand Workshop Boss Command Editor" width="900"/>

### Battle Formations

Edit which monsters appear in a battle and adjust their positions. When the
required clean Original Game Files are configured, the position layout displays
the battlefield surface for encounters whose map data can be rendered.

<img src="ReadmeAssets/ZW_Battle_Formation_Editor.png" alt="Zanarkand Workshop Battle Formation Editor" width="900"/>

### Sphere Grid Editor

Visually edit the Original, Standard, and Expert Sphere Grids. Select a node to
change its type, section color, or position. You can also click and drag nodes to 
change their location. Click on a connecting line to edit its link. Section colors 
are saved as Zanarkand Workshop project metadata.

The editor can create nodes and links and find/replace node types. Link creation
uses a guided Node A then Node B workflow and defaults to a straight link. A new
node or link can be removed with **Undo** as long as the grid has not been saved;
after it is saved, the editor cannot delete it.

For game stability, grids are limited to 860 nodes. Standard and Original grids
support up to 1,021 total links, Expert supports up to 934, and each node can
have up to five usable connections.

Use **Recovery** to restore the game's original grids. Start a new game when
testing a redesigned Sphere Grid so the game loads the new grid data cleanly.  

<img src="ReadmeAssets/ZW_Sphere_Grid_Editor.png" alt="Zanarkand Workshop Sphere Grid Editor" width="900"/>

### Treasure Chest Editor

View treasure chests on interactive maps reconstructed from your own game
files. Filter the field list, select a field, click a chest icon, or use
**Previous Chest** and **Next Chest** to inspect every detected chest—even one
without a visible map icon. Browsing fields, map states, chests, and conditional
chest contents does not create an edit. Change the reward type, reward, and
quantity for Gil, items, key items, weapons, and armor. Chest positions are
shown for reference and are not edited.

Required files: `jppc/battle/kernel/takara.bin`, `buki_get.bin`,
`jppc/map` with `mapout.vpa` files, and `jppc/event/obj` with `.ebp` files.
Only `takara.bin` is changed when you save.  

<img src="ReadmeAssets/ZW_Treasure_Map_Editor.png" alt="Zanarkand Workshop Treasure Map Editor" width="900"/>

## General text editing

These rules apply across editable game-text fields in Zanarkand Workshop,
including monster names, Sensor and Scan text, items, and command names and
descriptions, as well as Auto Ability names and descriptions.

### Cyan formatting

Wrap text in `[cyan]` and `[/cyan]` to use the game's cyan formatting:

```text
Weak against [cyan]Fire[/cyan].
```

The tags are editor notation. In the game, `Fire` is displayed in cyan and the
brackets are not shown. Tags are case-insensitive, although the lowercase form
is recommended. Keep opening and closing tags paired and correctly ordered.

### Line breaks

Press Enter in a multiline field to insert a game line break. Visual wrapping
inside an editor field does not insert a line break unless Enter was pressed.
The game controls the final text-box width and pagination, so review longer
text in game.

### Supported characters and validation

FFX uses its own limited character table rather than Unicode. If text contains
a character that cannot be encoded, Zanarkand Workshop reports an error instead
of writing damaged text. Replace unsupported smart quotes, symbols, or accented
characters with a supported equivalent and save again.

## Utilities
If you only want to use the Utilities, simply open the app when the game is open and use them, no need to set anything up.

* Available only while an active session of FFX is running.
* Zanarkand Workshop will automatically connect.
* Check **FFX Connected** in the top-right corner for confirmation.
* Utilities are only compatible with the Windows Steam version.
* Utilities are compatible with [Untitled Project X](https://steamcommunity.com/sharedfiles/filedetails/?id=683802394).
* To refresh Utility data, it has to be reopened (excluding the Battle Tracker).

Tools to play around with the game.

### Debug Menu

Resides inside the Utilities menu. A configuration menu with debug options.

### Battle Tracker

A menu to see and modify ally and enemy data. Autorefresh can be enabled, but
editing and loading data is disabled while it is autorefreshing.

### Inventory Tracker

See all of your inventory and edit it as needed. You can also sell equipment in
bulk.

### Arena Tracker

Keep tabs on your arena captures.

## Made with

* .NET
* Avalonia UI
* MemorySharp (compiled branch that supports x64 apps)

## Building from source

Requirements:

* Windows
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

From the repository root:

```powershell
dotnet restore .\ZanarkandWorkshop.sln
dotnet build .\ZanarkandWorkshop.sln -c Release --no-restore
```

The executable is written to
`ZanarkandWorkshop/bin/Release/net8.0/ZanarkandWorkshop.exe`.

To create the ready-to-distribute, self-contained Windows ZIP used for GitHub
Releases:

```powershell
.\scripts\build-release.ps1 -Version 0.4.2
```

The package is written to `artifacts/ZanarkandWorkshop-v0.4.2-win-x64.zip`.
Pushing a version tag such as `v0.4.2` runs the same packaging process on
GitHub and creates a draft release for review.

## Project status

Zanarkand Workshop is under active development. Back up modded game files before
editing them and review the release notes before upgrading. Version 1.0.0 is
reserved for a future stable milestone.

See [CHANGELOG.md](CHANGELOG.md) for release history and [NOTICE.md](NOTICE.md)
for project provenance and licensing information.

## Special Thanks

The knowledge on the files was shared by the FFX community folks. Check out the
[Fahrenheit project](https://github.com/peppy-enterprises/fahrenheit/tree/main)
to learn more.

Big thanks to [Karifean/FFXDataParser](https://github.com/Karifean/FFXDataParser)
and their knowledge of monster Battle Script structure.

Another big thanks to the
[Cid's Salvage Ship](https://discord.gg/yAQc3ngwDF) Discord community. They've
been an enormous help with FFX modding. Check them out if you're interested.  
## Contact  
You can find me in the Cid's Salvage Ship community discord. Questions and feedback about Zanarkand Workshop are welcome there.
