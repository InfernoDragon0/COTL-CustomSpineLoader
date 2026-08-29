# CultTweaker 2.0 — Pre-Release 3 (Experimental)

> ⚠️ **Experimental build.** Bugs may corrupt or lose save data. **Back up your game saves**
> (`AppData/LocalLow/Massive Monster/Cult Of The Lamb/saves`) — ideally play on a spare slot —
> and back up your creations in `BepInEx/plugins/CultTweaker/`: `CustomNodeBlueprints`,
> `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`, `CustomBaseMaps`,
> `CustomMainMenus`, `CustomShapeProfiles`, `LightingProfiles.json`.

## New in pre-release 3

The full list is in `CHANGES.md`.

- Added editing of your own base town, without ever touching the game's save
- Added a build totem for hubs — the game's own build menu, building the game's own structures
- Added a **main menu editor**: a "Customize Menu" button on the title screen for the palette and
  background colour, the lamb (or a custom spine), the logo and the edition line, kept as presets
- Improved every editor with the game's own artwork: panels, toggles, sliders, headers and the
  pause menu's button ribbons
- Added per-room **weather** to the Lighting tool, at every strength the game has art for
- Added a Camera shake trigger action

Maps, levels, worlds and hubs saved in pre-release 2 still open.

## The Worldshaper

Press **F4** in a dungeon room and it becomes your canvas — edited live, with the real game systems.
Tools: **Select**, **Shape** (terrain with real collision), **Structure**, **Enemy**, **NPC** (custom
dialogue), **Podium**, **Trigger** (action sequences: cutscenes, camera, text, music...), **Door**,
**Lighting** (now with weather), **Music**, **Clear**, **Load Map**, **Level**, **Dungeon Builder**.
Keys: **F6** hide UI, **F5** reset room, **Ctrl+S** quicksave, **Ctrl+Z** undo.

## Base, Hubs & World Maps

The **base editor** (F7 panel) edits your real town — structures, terrain, paths — and keeps every
change in the mod's own files, never in your save. Bought buildings can be moved but not deleted.

A **hub** is a safe town built in the emptied DLC town — **F7 panel**: New / Edit / Visit Hub.
Every hub needs a trigger with the **Hub spawn point** action; add *Return to base* or *Open world
map* triggers as portals. The **build totem** lets a hub's structures be built in play, through the
game's own build menu, at costs you set.

The **world editor** (F7 panel, World Maps) builds custom overworld maps: sprite/spine layers,
travel nodes with links, unlocks, keys and locks — each node leading to a dungeon, level or hub.
**F6** flips edit/play. Progress is tracked per save slot.

## The menu editor

**Customize Menu** on the title screen. Looks are saved as named presets under `CustomMainMenus/`
(a folder each: `config.json` beside its art); the chosen preset dresses the menu on every launch.
Drop a png in a preset's folder to use it as the title; drop a spine folder (`.json` + `.atlas` +
pages) to stand it where the lamb is.

## For modders

Ship content through CultTweaker with no code: put a `CultTweaker` folder in your mod and use the
same folder names inside it — `plugins/YourMod/CultTweaker/CustomNpcs/YourNpc/`. Works for every
content type (rooms, world maps, menu presets, NPCs, skins, structures...). Read-only: nothing is
written into your folder, and name clashes resolve in the player's favour.

## Feedback

This build is about **feel** as much as bugs — if anything is janky (a tool that fights you, a
clunky workflow, an arrival that looks wrong), say so. When reporting bugs, include **video or
screenshots**, **`BepInEx/LogOutput.log`** (copy it before relaunching — it's overwritten every
start), and what you were doing.
