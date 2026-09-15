# Cult Tweaker 2.0.0: World Shaper, Pre-release 7

Everything new since pre-release 6.

## Follower hats and clothes

- Added **custom hats and clothes for followers**. A wardrobe pack is a Spine export of the
  follower rig in `FollowerSpines/YourPack` with a `config.json` naming which of its skins are
  hats and which are clothes
- Clothes can be as many pieces as the garment needs: one skin carries the body, sleeves and
  shawl together, and every piece comes across at once
- Meshes, weights and cross-atlas art all work, the same way custom fleeces do; the follower's own
  hat, necklace and everything else the game dresses it in stays
- Pick them per follower: talk to a follower, **Customize Follower**, turn on Enable Customization
  and Follower Costume Override, then scroll **Custom Hat** and **Custom Clothes**. The follower
  changes as you scroll and keeps the look when you close the menu
- The follower's portrait in the game's menus shows the custom look as well
- A pack is only loaded the first time a follower wears something from it
- Added a **stripped follower rig** for Spine, `FollowerRig-stripped.zip`: the full rig takes
  minutes to open in Spine, this one opens in a moment, with the Cat, the basic robe, seven
  animations and every image beside the JSON. Its README explains how clothes attach and what not
  to touch
- Added a sample pack, `FollowerSpines/TestWardrobe`, with one hat and one set of clothes
- Full guide in `README.md` under "Follower hats and clothes"

## Brooms

- Added **broom transmog** to the F7 panel, beside the fleece transmog: a toggle and a list of
  every broom the player spine has, applied the moment one is picked and kept across rooms and
  restarts
- Added **custom brooms**: a player spine lists broom skins under `brooms` in its `config.json`,
  and they can be worn by any spine, not just the one that carries them
- The F7 portrait sweeps once when a broom is picked or the transmog turned on, so a broom can be
  seen; it waits for a broom that is still loading

## Structure tool

- Added **favourites**: right-click a structure or prop in the browser to pin it; a pinned cell
  wears a gold star, and a **Favourites** group in the browser lists everything pinned. Right-click
  again to unpin. Pins are kept between launches
- Added a **quick pick hotbar**: nine slots above the status bar holding pinned things first and
  the last things placed after them. Click a slot or press **1-9** to arm it; the armed slot wears
  a ring, and a slot under the pointer lights up and names what it holds. While the bar is showing,
  the **mouse wheel** steps through it instead of cycling tools. It is on by default and can be
  turned off from the Structure tool's panel
- Added a **Boss & special room pieces** group: the chains, dressing and plots that only exist
  inside the boss and special rooms can now be placed on their own
- **Randomised sets** now place as their separate pieces, each one selectable, movable and saved
  on its own, so a room looks the same every time it opens. A toggle keeps the old one-object
  behaviour
- Maps with many custom structures open noticeably faster
- A custom structure's row in the browser shows its name instead of its registry key

## Trigger tool

- Added a **Trigger method** for every volume: **On player step** as before, **Blocking volume**
  (an invisible wall, which replaces the old checkbox) and **Enemy health threshold**, which watches
  one enemy and fires when its health drops to 75, 50, 25 or 0 percent
- A blocking volume shows red, a health-watching one pink, so the room reads at a glance
- Added **Pause enemy AI** and **Resume enemy AI** actions, for every enemy in the room or one
  clicked in the world
- Actions that target an object can now point at an exact one, so two enemies with the same name
  no longer get mixed up

## For mod authors

- The code contract gained **world map progress** (contract version 3): list a map's nodes, read a
  node's state, complete and un-complete nodes, open and close locks, and read, set or reset keys,
  all by name. Documented in `ModdingApi.md`
- Added `FollowerHats` and `FollowerClothes` to the content kinds it lists

## Fixes

- Fixed an error when cleaning poop that was placed by the Structure tool
- Drag handles are now hollow rings, so they no longer cover the thing being dragged
- Fixed the follower skin editor's prebake doing nothing, so the pause landed on the first edit

## Compatibility

- Built for Cult of the Lamb **1.5.26**; earlier versions of the game are not supported
- Maps, levels, worlds, hubs, base edits and menu presets saved by pre-release 6 still open, and
  maps saved by this build still open in pre-release 6
