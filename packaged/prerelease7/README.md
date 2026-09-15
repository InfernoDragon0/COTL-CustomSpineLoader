# CultTweaker 2.0 — Pre-Release 7 (Experimental)

> ⚠️ **Experimental build.** Bugs may corrupt or lose save data. **Back up your game saves**
> (`AppData/LocalLow/Massive Monster/Cult Of The Lamb/saves`) — ideally play on a spare slot —
> and back up your creations in `BepInEx/plugins/CultTweaker/`: `CustomNodeBlueprints`,
> `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`, `CustomBaseMaps`,
> `CustomMainMenus`, `CustomShapeProfiles`, `LightingProfiles.json`, `QuestProgress`,
> `EditorFavourites.json`, `CustomColors<slot>.json`.

## New in pre-release 7

The full list is in `CHANGES.md`.

- Added **custom hats and clothes for followers**: a wardrobe pack is a Spine export of the
  follower rig with a config naming its hats and clothes, and a follower wears them from the
  Customize Follower menu. A **stripped follower rig** (`FollowerRig-stripped.zip`) opens in Spine in
  a moment instead of minutes, and a sample pack is included
- Added **broom transmog** to the F7 panel, and **custom brooms** any spine can wear
- The **Structure tool** gained favourites, a nine-slot hotbar the mouse wheel steps through, a
  group of boss and special room pieces, and randomised sets that place as separate pieces
- The **Trigger tool** gained trigger methods - player step, blocking volume, enemy health
  threshold - and pause / resume enemy AI actions
- The code contract gained **world map progress** (`ModdingApi.md`)
- Fixed an error cleaning poop placed by the Structure tool; drag handles no longer hide what
  you drag

Maps, levels, worlds, hubs, base edits and menu presets saved in pre-release 6 still open, and maps
saved by this build still open in pre-release 6.

Built for Cult of the Lamb **1.5.26**. Older versions of the game are not supported.

## The Worldshaper

Press **F4** in a dungeon room and it becomes your canvas — edited live, with the real game systems.
Tools: **Select**, **Shape** (terrain with real collision), **Structure**, **Enemy**, **NPC** (custom
dialogue and quests), **Podium** (pick the weapon it hands out), **Trigger** (action sequences: cutscenes, camera, text, music...), **Door**,
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

## Custom weapons

A player spine can add weapons. Declare them in the spine's `config.json` under `weapons`: each one
names a vanilla weapon to build on - Sword, Axe, Hammer, Dagger, Gauntlet, Chain - and keeps that
weapon's heavy attack, sounds and pickup card while taking your art, your combo and your numbers.
Every hit sets its own animation, damage, speed, range, hit box size, knockback, lunge and timings.

Weapons travel between characters: whichever spine the player is wearing gets the weapon's art
transplanted in, and a swing with no animation of its own falls back to the base weapon's. Set
`preloadWeapons` on a spine to load its weapon art at startup so a pickup never stalls play, turn on
`Debug / WeaponHitboxes` in the config to see every hit shape drawn as it swings, and use the
**Podium tool** dropdown to pin which weapon a podium hands out while you are testing.

Chain weapons get their own controls: sweep shapes, hook motion, two chains at once with a delay
between them, and your own hook art at the end of the chain. See `CustomWeapons.md`.

## Custom NPC quests

A custom NPC can hand out quests, and they appear in the game's own objectives panel on the right
of the screen - same headings, tick boxes, counters and countdown wheels as the game's own.

A quest finishes on the criteria you would expect: gather items, kill enemies, build something,
grow the flock, clear a dungeon (custom ones too), perform a ritual, speak to another custom NPC,
or reach one of the game's own story beats. Dialogue does the rest: an answer can take the quest on,
a later conversation hands it in, and the NPC greets you differently depending on whether the quest
is untaken, under way, ready to hand in or done. Quests can pay out items, repeat, run on a timer,
and take the items they asked for.

The included **Test Npc** carries two quests to try. Progress is kept in CultTweaker's own file, one
per save slot, so your game save is never touched. See `CustomNpcQuests.md`.

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

## Follower hats and clothes

Followers can wear hats and clothes that other mods draw. A **wardrobe pack** is a Spine export of
the follower rig in `FollowerSpines/YourPack` - the `.json`, `.atlas` and page beside a
`config.json` that lists which of its skins are hats and which are clothes:

```
{ "hats": [ "Hats/Crown" ], "clothes": [ "Clothes/Armour" ] }
```

To wear one, talk to a follower, pick **Customize Follower**, turn on Enable Customization and
Follower Costume Override, and scroll **Custom Hat** and **Custom Clothes**. A hat is a skin with
art on the two head slots; clothes are one skin with as many pieces as the garment needs. Start
from `FollowerRig-stripped.zip`: it opens in Spine in a moment, and its README says where clothes
attach. Export the template whole, without deleting skins or touching bones. The included
`FollowerSpines/TestWardrobe` is a working pack to compare against.

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

If your mod needs to *react* to what is installed, there is now a small code contract as well —
one class that lists and finds every kind of custom content, enters a custom dungeon, spawns a
custom NPC and drives a quest. It is in `ModdingApi.md`, along with the two things that catch
people out: plugin load order, and why the numbers the game gives custom content must never be
saved or sent to another machine.

## Feedback

This build is about **feel** as much as bugs — if anything is janky (a tool that fights you, a
clunky workflow, an arrival that looks wrong), say so. When reporting bugs, include **video or
screenshots**, **`BepInEx/LogOutput.log`** (copy it before relaunching — it's overwritten every
start), and what you were doing.
