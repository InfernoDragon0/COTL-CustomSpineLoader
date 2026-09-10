**Cult Tweaker 2.0.0: World Shaper Pre-release 6**

__Custom weapons__

- Added custom weapons: a player spine declares them in its config, each built on a vanilla weapon
- Set the animation, damage, speed, range, hitbox size, knockback and hit timing of every swing
- Any spine can wield any spine's weapon
- Chain weapons: sweep shapes, hook patterns, two chains per swing, and your own hook art
- Added a Debug / WeaponHitboxes setting that draws every hit shape, and a Podium tool dropdown to pick the weapon

__Custom NPC quests__

- Added quests to custom NPCs, shown in the game's own objectives panel
- Criteria: gather, kill, build, recruit, clear a dungeon, perform a ritual, or any vanilla story beat
- Dialogue hands quests out and takes them in, and the NPC greets you by quest state
- Added rewards, repeatable quests and expiry timers; progress never touches your save

__Whiteboard__

- Added a Whiteboard tool: draw and erase planning marks over the room, nine colours, sizes, undo per stroke, clear all
- Strokes are not selectable, hide when the editor closes, save with the map and sync live to the other player

__Multiplayer (experimental)__

- Added mod compatibility for COTL MP Steam (Worldshaper)
- Added a chat invite when the other player opens the editor
- Added two selection colors (you cyan, theirs amber) and locking of whatever the other player selects
- Added live drags, host-side saving with files sent to the guest, and a Resync button
- Custom dungeon levels: the guest follows the host into the same level and rooms
- Objects now carry ids in the map files

__For mod authors__

- Added a code contract so other mods can find and use what CultTweaker loaded

__Fixes__

- Fixed door pads growing across the room and into a central hole
- Fixed placed enemies attacking while the editor is open with multiplayer enabled
- Fixed the guest stuck on the summoning screen when the host enters a custom dungeon level
- Fixed enemy bodies left behind on the guest after a move from the other player
