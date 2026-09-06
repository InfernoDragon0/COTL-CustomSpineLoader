# CultTweaker 2.0 — Pre-Release 5 (Experimental)

> ⚠️ **Experimental build.** Bugs may corrupt or lose save data. **Back up your game saves**
> (`AppData/LocalLow/Massive Monster/Cult Of The Lamb/saves`) — ideally play on a spare slot —
> and back up your creations in `BepInEx/plugins/CultTweaker/`: `CustomNodeBlueprints`,
> `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`, `CustomBaseMaps`,
> `CustomMainMenus`, `CustomShapeProfiles`, `LightingProfiles.json`.

## New in pre-release 5

The full list is in `CHANGES.md`.

- Added a **Layers** panel to the map editor (the + beside the shortcut hints): everything in the
  room listed by kind, click a row to select it, shift-click for a range, and it follows your
  selection
- Added **multi-selection** to the Select tool (Shift-click) and **groups** with **Ctrl+G**: move,
  undo and delete whole sets at once; groups are saved with the map and shown as folders
- Added a **Whiteboard** tool: draw planning marks over the room in nine colours, erase them, undo
  a stroke at a time; they are never selectable, hide when the editor closes, and sync live
- Added the **DLC dungeon dressing** (Ewefall and Rot, pieces and whole plots) plus the **Art** and
  **Tile Decorations** groups to the structure browser
- Added **editing together over COTL MP Steam**: with a multiplayer build that carries editor sync
  on both machines, either player opens the editor in the base or in a custom level and both see every change live,
  with a shared pause, a second selection colour for the other player, locking, live drags and host-side
  saving

Maps, levels, worlds, hubs, base edits and menu presets saved in pre-release 4 still open; maps
saved by this build carry an id on every object, which older builds ignore.

Built for Cult of the Lamb **1.5.26**. Older versions of the game are not supported.

## The Worldshaper

Press **F4** in a dungeon room and it becomes your canvas — edited live, with the real game systems.
Tools: **Select**, **Shape** (terrain with real collision), **Structure**, **Enemy**, **NPC** (custom
dialogue), **Podium**, **Trigger** (action sequences: cutscenes, camera, text, music...), **Door**,
**Whiteboard**, **Lighting** (now with weather), **Music**, **Clear**, **Load Map**, **Level**, **Dungeon Builder**.
Keys: **F6** hide UI, **F5** reset room, **Ctrl+S** quicksave, **Ctrl+Z** undo, **Shift-click**
multi-select, **Ctrl+G** group. The **Layers** panel at the left edge lists the room by kind.

## Editing together

With **COTL MP Steam** on both machines (a build that includes editor sync), press **F4** in the
base while in a session: the world pauses for both players, the other player is invited in chat
and can press **F4** to edit with you. Your selection is cyan, theirs is amber, and whatever they
hold is locked until they let go. Saving from either side saves on the host's machine; the guest
receives the files. If the two rooms ever disagree, the **Resync** button in the shortcut panel
puts them back in step.

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

The **left panel** picks which skin and variant you are editing (and starts new ones), the color
set, the base skin every layer sits on (Cat, Dog...), an outfit for the preview to wear (just for
looking - it is not part of the skin), the animation the preview plays, and the list of layers:
pick a slot to override it, then click a layer to work on it. The **right panel** is that layer —
its slot, the image covering it, whether it is hidden, its placement, and its color for the set
you are editing.

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
