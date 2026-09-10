# Cult Tweaker 2.0.0: World Shaper, Pre-release 6

Everything new since pre-release 5.

## Custom weapons

- A **player spine can now add weapons to the game**. They are declared in the spine's own
  `config.json` under `weapons`, and each one is built on a vanilla weapon, so it keeps that
  weapon's heavy attack, sounds, hit shapes and pickup card and swaps in your look, your combo and
  your numbers
- Every hit of a combo is yours to set: animation, damage, swing speed, range, hit box size,
  knockback, lunge speed and length, camera shake, attack type, whether the next hit can be queued,
  whether you can turn mid-swing, and when in the animation the hit lands and the swing can be
  broken out of
- **Any spine can wield any spine's weapon.** The weapon's art is transplanted into whichever
  player skin is worn, the same way fleece cycling works, and a swing with no animation of its own
  falls back to the base weapon's, so a weapon made for one character still plays on another
- **Chain weapons** are supported in full: sweep shapes, hook motion patterns, up to two chains
  swung at once with a delay between them, and your own hook art and size at the end of the chain
- Added `preloadWeapons` to load a spine's weapon art at startup rather than at the moment a weapon
  is picked up, so a pickup never stalls play
- Added a `Debug / WeaponHitboxes` config that draws the hit shape of every swing, and the chain
  hook's own collider as it flies, so a combo can be tuned by eye
- The **Podium tool** now has a dropdown to pin which weapon a podium hands out, instead of the
  podium rolling one at random
- Full field reference and a guide to hit timings in `CustomWeapons.md`

## Custom NPC quests

- **A custom NPC can now hand out quests.** They appear in the game's own objectives panel on the
  right of the screen, next to the game's, with the same headings, tick boxes, counters and
  countdown wheels
- Quests finish on the same kinds of criteria the game's own do: gathering items, killing enemies,
  building, growing the flock, clearing a dungeon (custom ones included), performing a ritual,
  speaking to another custom NPC, a flag another mod raises, or **any of the game's own story
  beats** - cooking a first meal, catching a fish, burying a body, declaring a doctrine and about
  two hundred more
- Dialogue drives them: a node or an answer can hand a quest out, take it in, or give it up, so
  "will you help me?" becomes a real yes and no
- An NPC greets you differently depending on where a quest stands - untaken, under way, ready to
  hand in, finished or failed - through a list of conditional ways into the conversation
- Quests can pay out items, be repeatable, run on a timer, and take the items they asked for when
  they are handed in
- Progress is kept in CultTweaker's own file, one per save slot, and the game's save file is left
  untouched, so removing the mod never leaves an unfinishable objective behind in your save
- Full guide in `CustomNpcQuests.md`

## Whiteboard

- Added a **Whiteboard** tool: draw freehand over the room, in the world, to plan with - a
  Show whiteboard toggle, Draw with a size slider and nine colours, Erase with its own size, and
  **Clear all**
- Strokes are never selectable and never part of the room; they show only while the editor is
  open, are saved with the map, and travel to the other player live as you draw
- One drag is one stroke and one undo; an erase drag is one undo too, and Clear all wipes the
  board in one undoable step
- The chosen colour shows a white border instead of being covered, and the brush lands exactly
  under the cursor

## Multiplayer

- Added editing together over COTL MP Steam: with a multiplayer build that carries editor sync
  on both machines, either player can open the map editor in the base and both see every change
  as it happens
- Added a shared pause: opening the editor stops the world for both players, and the other player
  gets a chat line inviting them to press F4 and edit too
- Added two selection colours: your own selection stays cyan, the other player's is amber, on the
  object, its outline, their cursor and in the layer list, so it is always clear who holds what
- Added locking: an object the other player has selected or is dragging cannot be taken until
  they let go
- Added live drags: moves, resizes and reshapes show on the other screen while the mouse is still
  down, not only on release; the other player's cursor no longer flickers, dragged objects glide
  instead of jittering, and lighting changes fade in
- Saving from either side saves on the host's machine; the guest receives the files
- Custom dungeon levels work in a session: the host starts a level and the guest follows into the
  same layout with the same rooms, and both can edit the rooms together
- Added a "Resync" button in the shortcut panel for when the two rooms ever look different
- Added a `MapEditor / NetVerbose` config that logs every change sent and received, and a
  "Sync self-test" button that runs the room through the sync on one machine, for tracking down
  a desync
- Every saved object now carries an id in its map file, so it can be told apart from its
  neighbours across a save, a load and a second machine; maps saved by pre-release 5 still open

## For mod authors

- Added a small **code contract** other mods can build on: one class, `CultTweakerApi`, which lists
  and finds every kind of custom content this install has - dungeons, NPCs, enemies, items, meals,
  tarots, structures, weapons, skins, rooms, levels, world maps, menus, cutscenes and quests - and
  can enter a custom dungeon, spawn a custom NPC, drive a quest and locate content on disk
- Shipping content through CultTweaker still needs no code at all; the contract is for mods that
  want to react to what is installed
- Documented in `ModdingApi.md`, including the load-order trap (content registers during our
  startup, and BepInEx does not order plugins) and why the numbers the game gives custom content
  must never be saved or sent to another machine

## Fixes

- Fixed door pads growing across the whole room when the room's centre is a hole (a pit, a tree):
  the check that decides whether a doorway reaches the floor now measures against the floor itself,
  so pads stay at their normal length and no longer lay a band of collision across the hole
- Fixed placed enemies waking up and attacking while the editor was open with multiplayer enabled,
  even when hosting alone
- Fixed the guest being stuck on the summoning screen when the host entered a custom dungeon level
- Fixed a move or resize received from the other player sometimes respawning the object instead of
  moving it, which left enemy bodies behind on the guest's screen

## Compatibility

- Built for Cult of the Lamb **1.5.26**; earlier versions of the game are not supported
