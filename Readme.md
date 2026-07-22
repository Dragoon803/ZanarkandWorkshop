# Zanarkand Workshop

<p align="center">
  <img src="ZanarkandWorkshop/Assets/ZanarkandWorkshop.png" alt="Zanarkand Workshop logo" width="560"/>
</p>

> **Forked with permission from original author:** This project is based on
> [osdanova/FFXProjectEditor](https://github.com/osdanova/FFXProjectEditor),
> originally created by osdanova. This fork is independently maintained and is
> not affiliated with or endorsed by Square Enix.

An unofficial Final Fantasy X modding toolkit for Windows. Zanarkand Workshop
uses its own version numbering beginning at v0.1.0 and was derived from FFX
Project Editor v1.2.

If you only want to use the Utilities, simply open the app when the game is open and use them, no need to set anything up.

* Utilities are only compatible with the Windows Steam version.
* Utilities are compatible with [Untitled Project X](https://steamcommunity.com/sharedfiles/filedetails/?id=683802394)
* To refresh Utility data it has to be reopened (Excluding the Battle Tracker)

## How to set it up

Download the latest Windows package from the Releases section of this fork,
extract it, and run `ZanarkandWorkshop.exe`.

Extract the game files using an extractor such as [VBF Browser](https://www.nexusmods.com/finalfantasy12/mods/3).

The folder that the app uses (Needs to be loaded) is ffx_ps2/ffx/master. Inside this folder there are folders containing data for every region. Currently this tool only supports US (Both new_uspc and jppc are needed)

I recommend using the [External File Loader](https://www.nexusmods.com/finalfantasyxx2hdremaster/mods/150). This will create a folder called mods inside your FFX folder where your custom files will be located and loaded by the game. Once installed, copy your master folder to mods/ffx_ps2/ffx/ and load that folder in the editor to immediately modify the files your game loads.

**It is also recommended that you keep a clean version of the master files as they are needed for the recovery feature of this program.**

## How to use

Click **Open Project Folder...** and select your master folder. The app detects
the running game automatically; the **FFX Connected** indicator shows the
connection status.

<img src="ReadmeAssets/WelcomeScreen.png" alt="Zanarkand Workshop welcome screen" width="900"/>

## File Editors

* Available: When a master file is selected and confirmed.

### Monster Editor

* Live testing: Loaded when combat starts so once a monster is saved the new data will apply to the next encounter.

Edit stats, elemental affinities, status resistences, loot drops, etc.  
Battle Script editor capable of changing attack used, the target of those attacks, and much more.

<img src="ReadmeAssets/BattleScriptEditor.png" alt="Zanarkand Workshop Battle Script Editor" width="900"/>
<img src="ReadmeAssets/MonEditorStatus.png" alt="Zanarkand Workshop Monster Status Editor" width="900"/>
<img src="ReadmeAssets/MonEditorLoot.png" alt="Zanarkand Workshop Monster Loot Editor" width="900"/>

### Items / Player & Aeon Commands / Standard Monster Commands/ Boss Commands

* Live testing: Loaded when the game starts. The "Load Ingame" button can be used to see changes without reloading.

All of these files share their structure.

* Items: Game items
* Player & Aeon Commands: The commands used by the party
* Standard Monster Commands: Commands used by standard enemies
* Boss Commands: Commands used by bosses

## Utilities

* Available: only while an active session of FFX is running.
* Zanarkand Workshop will automatically connect.
* Check the FFX Connected in the top right corner for confirmation.

Tools to play around with the game.

### Debug Menu

Resides inside the Utilities menu.
A configuration menu with debug options.

### Battle Tracker

A Menu to see and modify ally and enemy data. Note that the autorefresh can be enabled but editing and loading data is disabled while it is autorefreshing.

### Inventory Tracker

See all of your inventory and edit it as you need. You can also sell equipment in bulk!

### Arena Tracker

Keep tabs on your arena captures.

## Made with

* .Net
* Avalonia UI
* MemorySharp (Compiled branch that supports x64 apps)

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
.\scripts\build-release.ps1 -Version 0.1.0
```

The package is written to `artifacts/ZanarkandWorkshop-v0.1.0-win-x64.zip`.
Pushing a version tag such as `v0.1.0` runs the same packaging process on
GitHub and creates a draft release for review.

## Project status

Zanarkand Workshop is under active development. Back up modded game files before
editing them and review the release notes before upgrading.

See [CHANGELOG.md](CHANGELOG.md) for release history and [NOTICE.md](NOTICE.md)
for project provenance and licensing information.

## Special Thanks

The knowledge on the files was shared by the FFX community folks. Check out the [Fahrenheit project](https://github.com/peppy-enterprises/fahrenheit/tree/main) to learn more!
