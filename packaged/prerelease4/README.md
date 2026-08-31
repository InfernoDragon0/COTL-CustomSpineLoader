# CultTweaker 2.0 — Pre-Release 4 (Experimental)

> ⚠️ **Experimental build.** Bugs may corrupt or lose save data. **Back up your game saves**
> (`AppData/LocalLow/Massive Monster/Cult Of The Lamb/saves`) — ideally play on a spare slot —
> and back up your creations in `BepInEx/plugins/CultTweaker/`: `CustomNodeBlueprints`,
> `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`, `CustomBaseMaps`,
> `CustomMainMenus`, `CustomShapeProfiles`, `LightingProfiles.json`.

## New in pre-release 4

The full list is in `CHANGES.md`.

- Added a **follower skin editor** (**F8** in your cult): build a custom follower skin from a base
  skin, override parts with your own pngs, set colour sets, and watch it animate before saving
- Added **Affected by wind** to the Structure and Select tools: a structure or prop sways with the
  biome's own wind, whole rather than in pieces, and can be see-through at the same time
- Added **shadows** to custom structures, which used to cast none
- Added **Blocking volume** to the Trigger tool: an invisible wall to players and enemies, drawn in
  red, with enemy pathfinding kept in step as you move and resize it

Maps, levels, worlds, hubs, base edits and menu presets saved in pre-release 3 still open.

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

## The follower skin editor

Press **F8** anywhere in the game (not on the title screen). Skins live under `FollowerSkins/`,
one folder per skin with a folder per variant (`base`, `variant1`...), each holding a
`config.json` and its pngs — the same files the mod already loads.

The **left panel** picks which skin and variant you are editing (and starts new ones), the base
skin every layer sits on (Cat, Dog...), the animation the preview plays, the colour sets, and the
list of layers: pick a slot to override it, then click a layer to work on it. The **right panel**
is that layer — its slot, the image covering it, whether it is hidden, its placement, and its
colour for the set you are editing.

Names are written as `skin/variant`, so one prompt makes either a new variant of a skin you have
or a whole new skin. **Ctrl+S** saves, and saving makes the skin wearable straight away — it
rebuilds the skin for the game, which pauses for a moment. Drop pngs into the variant's folder and
they show up in the image picker.

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
