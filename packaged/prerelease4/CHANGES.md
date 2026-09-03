# Cult Tweaker 2.0.0: World Shaper, Pre-release 4

Everything new since pre-release 3.

## Follower skin editor

- Added a follower skin editor on F8, editing the same `FollowerSkins/` folders the mod loads
- Added a left panel: skin and variant picking, new skins and variants, base skin, animation
  preview, colour sets, and the list of overridden slots
- Added a right panel for the selected layer: its slot, the image covering it, hide, placement
  sliders and its colour
- Added `skin/variant` naming, so one prompt saves either a new variant or a whole new skin
- Added live reload: a saved skin can be worn by followers without restarting the game

## Structures

- Added an "Affected by wind" checkbox: structures and props sway with the biome's own wind,
  either armed for the next placement or set on anything already placed
- Added wind to structures built from several sprites, including parts whose own art was never
  made to move
- Added shadows to custom structures, which previously cast none
- Improved see-through so it can be worn together with wind
- Added `"HideFromBuildMenu": true` to a custom structure's `config.json`: it stops appearing in the
  build menu, so players cannot build it, while you can still place it yourself
- Added a **Custom** group to the structure tool, holding every custom structure, hidden ones
  included; custom structures used to sit at the end of the Build Menu Structures list

## Triggers

- Added a "Blocking volume" checkbox: the trigger becomes an invisible wall to players and enemies,
  drawn in red
- Added enemy pathfinding around blocking volumes, updated as they are moved and resized
- Added a "Play animation on object" action: pick anything in the room that has a spine, choose one
  of its animations, and play it once or loop it for a while
- Added a choice of what happens when that animation ends, so an enemy or NPC can go back to the
  idle it was playing instead of freezing on the last frame

## Loading

- Custom follower skins are now baked once and kept, instead of being rebuilt on every launch:
  roughly 10 seconds off the startup and about 2 GB less memory while it loads
- A skin is rebuilt only when you change it, and the kept copy can be shipped with a skin so
  other players never pay for the first build either

## Compatibility

- Built for Cult of the Lamb **1.5.26**; earlier versions of the game are no longer supported

## Fixes

- Fixed an issue where F5 started the test dungeon from the title screen and during scene changes
- Fixed an issue where a structure's own sprites, added as it is placed, missed the look it was
  given
- Fixed an issue where naming a skin could confirm a name the editor would not accept
- Fixed the skin editor claiming to get a skin ready to edit while doing nothing; viewing and
  recolouring a skin no longer costs a rebuild at all
