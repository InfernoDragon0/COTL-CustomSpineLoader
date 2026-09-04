**Cult Tweaker 2.0.0: World Shaper Pre-release 4**

__Follower skin editor__

- Added a follower skin editor with F8 (in game live editor)
- Added skin and variant picking, color sets, base skin, animation preview and an outfit preview
- Added per-layer editing: slot, image, hide, placement and colour
- Added skin/variant naming
- Added live hot reload, newly applied skins to followers will use new skins

__Player skins__

- Custom player spines now show in the inventory player tab, Knucklebones and Flockade

__Structures__

- Added an "Affected by wind" checkbox, for placement and for anything already placed
- Added wind support for multi sprite structures
- Added shadows for custom structures
- Improved see-through shader so that it can be used with the wind shader
- Added "hideFromBuildMenu" for custom structures, hidden ones still show in the structure tool's new Custom group

__Triggers__

- Added a "Blocking volume" checkbox, allows creating invisible wall for players and enemies
- Added a red outline for blocking volumes
- Added auto pathfinding update after blocking volume placement

__Loading__

- Custom follower skins are baked once and cached, about 10 seconds faster startup and 2GB less memory
- Built for Cult of the Lamb 1.5.26

__Fixes__

- Fixed an issue where F5 started the test dungeon from the title screen and during scene changes
- Fixed an issue where incorrect sprite was previewed for some structures
