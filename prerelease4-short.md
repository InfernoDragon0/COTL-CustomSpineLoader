**Cult Tweaker 2.0.0: World Shaper Pre-release 4**

__Follower skin editor__

- Added a follower skin editor with F8 (in game, not on the title screen)
- Added skin and variant picking, base skin, animation preview and colour sets
- Added per-layer editing: slot, image, hide, placement and colour
- Added skin/variant naming so one prompt saves a new variant or a whole new skin
- Added live reload, so a saved skin is wearable without a restart

__Structures__

- Added an "Affected by wind" checkbox, for placement and for anything already placed
- Added wind support for structures made of several sprites
- Added shadows for custom structures
- Improved see-through so it can be used together with wind

__Triggers__

- Added a "Blocking volume" checkbox, making the trigger an invisible wall for players and enemies
- Added a red outline for blocking volumes
- Added enemy pathfinding updates when a blocking volume is moved or resized

__Fixes__

- Fixed an issue where F5 started the test dungeon from the title screen and during scene changes
- Fixed an issue where sprites a structure adds as it is placed missed the look it was given
- Fixed an issue where a skin name could be confirmed when the editor would not accept it
