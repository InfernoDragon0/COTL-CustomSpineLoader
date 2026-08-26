# CultTweaker 2.0 — Pre-Release 2 (Experimental)

> ⚠️ **Experimental build.** Bugs may corrupt or lose save data. **Back up your game saves**
> (`AppData/LocalLow/Massive Monster/Cult Of The Lamb/saves`) — ideally play on a spare slot —
> and back up your creations in `BepInEx/plugins/CultTweaker/`: `CustomNodeBlueprints`,
> `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`, `CustomShapeProfiles`,
> `LightingProfiles.json`.

## New in pre-release 2

The full list is in `CHANGES.md`.

- Improved the Dungeon Builder into a full screen with free node placement
- Added hand-built floor layouts ("custom walk") with auto connecting doors
- Added move, resize and depth handles to the room editor
- Added search by name to the Structure, Enemy and NPC tools
- Added a "Bosses (inside rooms)" list to the Enemy tool
- Added a player panel to the F7 menu with a live character preview
- Fixed an issue where doors did not lock with monsters in the room

Dungeon maps saved in pre-release 1 still open.

## The Worldshaper

Press **F4** in a dungeon room and it becomes your canvas — edited live, with the real game systems.
Tools: **Select**, **Shape** (terrain with real collision), **Structure**, **Enemy**, **NPC** (custom
dialogue), **Podium**, **Trigger** (action sequences: cutscenes, camera, text, music...), **Door**,
**Lighting**, **Music**, **Clear**, **Load Map**, **Level**, **Dungeon Builder**. Keys: **F6** hide
UI, **F5** reset room, **Ctrl+S** quicksave, **Ctrl+Z** undo.

## Hubs & World Maps

A **hub** is a safe town built in the emptied DLC town — **F7 panel**: New / Edit / Visit Hub.
Every hub needs a trigger with the **Hub spawn point** action (it won't save without one); add
*Return to base* or *Open world map* triggers as portals.

The **world editor** (F7 panel, World Maps) builds custom overworld maps: sprite/spine layers,
travel nodes with links, unlocks, keys and locks — each node leading to a dungeon, level or hub.
**F6** flips edit/play. Progress is tracked per save slot.

## For modders

Ship content through CultTweaker with no code: put a `CultTweaker` folder in your mod and use the
same folder names inside it — `plugins/YourMod/CultTweaker/CustomNpcs/YourNpc/`. Works for every
content type (rooms, world maps, NPCs, skins, structures...). Read-only: nothing is written into
your folder, and name clashes resolve in the player's favour.

## Feedback

This build is about **feel** as much as bugs — if anything is janky (a tool that fights you, a
clunky workflow, an arrival that looks wrong), say so. When reporting bugs, include **video or
screenshots**, **`BepInEx/LogOutput.log`** (copy it before relaunching — it's overwritten every
start), and what you were doing.
