# CultTweaker Map Editor

An in-game room editor for Cult of the Lamb. It captures, edits and rebuilds a dungeon room as a
**node blueprint** (`CustomNodeBlueprints/<name>.json`), and chains those rooms into a **level
blueprint** (`CustomLevelBlueprints/<name>.json`) that plays through the mod's own dungeon.

| Key | Action |
| --- | --- |
| `F4` | Open / close the editor (Dungeon1 scenes only) |
| `F5` | Enter the test dungeon — or, with the editor open, reset the room |
| `F7` | CultTweaker panel (fleeces, player spines, mod info) — not part of the editor |
| `WASD` / arrows | Pan the camera |
| `Z` / `X` | Zoom in / out |
| Wheel | Switch tool (or scroll the list under the cursor) |
| `Ctrl+Z` | Undo the last placement |
| `Del` | Delete the selection (tool-dependent) |

While the editor is open the game is paused (`timeScale 0`), the HUD is hidden, and the camera is
handed to a dummy follow target so the view can leave the play area. Area culling is suspended for
the session — it deactivates whole areas whose precomputed bounds leave the viewport, and a roaming
camera would otherwise make the world appear to delete itself.

---

## Contents

- [Editor shell](#editor-shell)
- [Tools](#tools)
  - [Select](#select) · [Shape](#shape) · [Structure](#structure) · [Enemy](#enemy) · [NPC](#npc)
  - [Podium](#podium) · [Trigger](#trigger) · [Door](#door) · [Lighting](#lighting) · [Music](#music)
  - [Clear](#clear) · [Load Map](#load-map) · [Level](#level)
- [Trigger actions](#trigger-actions)
- [Custom NPCs and dialogue](#custom-npcs-and-dialogue)
- [Saving and loading](#saving-and-loading)
- [File map](#file-map)

---

## Editor shell

**Dock.** One flat row of square icon buttons across the bottom. It replaced a tall scrolling
column: a scrolled-away button sat outside every registered blocker rect, so clicking it placed an
object in the world instead. The plate sizes itself to its icons. Icons come from
`Assets/EditorIcons/<Tool.Name>.png` with `colorwheel.png` as the placeholder — dropping a correctly
named PNG in that folder is the whole hand-over, no code change. Save sits immediately after Load
Map; they are two halves of the same job.

**Tool options panel** (right). Shows the active tool's controls and sizes itself to them. Its
header collapses it.

**Shortcuts panel** (bottom left). Lists what the mouse and keys do for the active tool. Its title
bar collapses it; the bar sits at the *bottom* of the stack because the panel is pivoted to the
corner and grows upward — a header on top would drift with the hint count.

**Status bar.** The editor's only feedback channel. Severity colours the text and, for anything the
user must act on, darkens and pulses the bar.

**Undo.** One stack for the whole editor. Tools push an entry as they place; `Ctrl+Z` walks the
single history regardless of which tool is active. Entries return false when the thing they would
undo has already gone (cleared, loaded over, destroyed by another tool) and the stack moves on.

**Widgets.** Built from scratch rather than cloned from the settings menu — those rows are authored
56px tall for a full-width panel and only exist once the player has opened the settings menu. The
dropdown is likewise hand-built: the game's `MMDropdown` opens through `UIMenuBase.ActiveMenus`, a
menu stack the editor is deliberately outside of.

**Pointer handling.** `PointerOverUi` tests the editor's own registered rects rather than calling
`EventSystem.IsPointerOverGameObject` — this game installs Rewired's pointer module, under which
that call reported true almost everywhere and silently rejected every world click. The wheel is
routed to the editor's own scroll views by hand for the same reason.

**Text entry.** The inline prompt reads `Input.inputString` directly and suspends the EventSystem
for its duration: `MMButton` derives from Unity's `Button`, so it handles `ISubmitHandler`, and this
game's input module raises submit from the interact key — typing an "e" into a name re-pressed
whatever button was focused.

---

## Tools

### Select

Click an object to select it, then delete it individually. Ctrl-click-drag clones it. Also offers
send-back / bring-front (Z nudge) and horizontal flip.

**Notes**

- Picking is physics first, then the smallest visible renderer whose bounds contain the point —
  much of the room dressing is purely visual and carries no collider, and a physics hit is only
  trusted when the thing is actually drawn (the room is littered with invisible trigger and
  particle colliders). Enemy HP bars are skipped: they are spawned as *siblings* of their enemy, so
  no enemy check catches them.
- `PickWorldObject` is public because other tools must agree with Select about what an object is —
  the trigger tool picks its move target through it.
- Selection walks up to the outermost parent that is still map content, stopping before the room
  containers, because many props are several sprites under a shared parent.
- Flip is a mirror (negated X scale), not a rotation: the view is 2.5D and the art is flat, so
  rotating a prop either tips it over or swings it through its own sorting plane. Doors refuse to
  flip — their pad, barrier and lock visuals are built from the direction they face. Structures
  serialise their own mirror flag, so the tracked value follows the transform.
- Deleting needs no bookkeeping: a blueprint is a full snapshot, so a deleted object is simply
  absent from it.
- Moving anything out of its culling area keeps culling suspended for the session, or it would be
  deactivated when culling resumed.

### Shape

Spawns and edits `SpriteShapeController` terrain, and keeps its collision in sync. Add/move/delete
spline points, pick a profile, toggle per-shape collision, show the baked collision outline.

**Notes**

- New shapes are **cloned** from a sprite shape already in the room. Building one from scratch
  produced untextured geometry: a working shape needs a matching profile, fill material, sorting
  layer and renderer settings, and only the profile is reachable through
  `GenerateRoom.DecorationList`. Cloning inherits all of it, so authored terrain matches the biome.
- The tool keeps an inactive template clone so new shapes can still be created after Clear Terrain
  has removed every original.
- Collision is a per-shape property read off the object: many room shapes are decorative and carry
  no collider, and editing one must not silently make it solid.
- `SpriteShapeController` bakes into an `EdgeCollider2D` (open) or `PolygonCollider2D` (closed) but
  never *creates* one — without the component present `BakeCollider` silently does nothing.
- Baking must wait a frame after `RefreshSpriteShape`: mesh generation is deferred to end of frame,
  so baking immediately captures the *previous* outline.
- Colliders are joined to the room's `CompositeCollider2D`. A lone `PolygonCollider2D` is solid, so
  the player was blocked by the whole filled area; merged into the composite only the union's
  outline is solid — the same treatment `IslandPiece` colliders get.
- "Use island collision" merges the vanilla island pieces into the walkable union, so the area can
  never shrink below the original floor. Turning it off **disables** the island colliders (rather
  than clearing `usedByComposite`, which would turn each island into a solid standalone body).
- Custom profiles come from `CustomShapeProfiles/<Folder>/config.json` and are registered as
  `CultTweaker_<Name>`, so custom names can never collide with vanilla ones. Minimal config is
  fill-only: `{"Name":"Dirt","FillTexture":"dirt.png"}`.
- The blueprint saves every standalone sprite shape in the room, not only tool-created ones. Shapes
  under island pieces are excluded (their island root is captured as a prop and brings them back)
  and so are door pads (derived from door presence, rebuilt on load).

### Structure

Places structures and props. The browser lists cult structures (the build menu's own
`TypeAndPlacementObjects` entries plus anything mods registered through COTL_API) and every prop
prefab in the Addressables catalog, grouped by folder two levels deep.

**Notes**

- Placement instantiates the prefab directly under the room's content root rather than going
  through `StructureManager.BuildStructure`: dungeon locations have no cult placement grid, and a
  map builder wants a positioned prop, not a functioning cult building with a `StructureBrain` and
  a save entry.
- The game's build menu gains a **Map Assets** tab (`MapAssetsTab`) listing every structure type,
  including background props (TREE, BUSH, GRASS, ROCK, WEEDS, TILE_\*, DECORATION_\*) that vanilla
  tabs never show because they are not player-buildable. A full listing beats "vanilla tabs minus
  their contents" because the vanilla categories come from several `DataManager` sources with their
  own unlock gating. `ForceUnlockAll` flips the unlock checks for our population pass only.
- The cursor ghost is built from the structure's **own** prefab, not
  `TypeAndPlacementObject.PlacementObject`: that wrapper only draws once its `Start()` has
  asynchronously instantiated the real asset, and it publishes itself to the static
  `PlacementObject.Instance` while it does, sending `Interactor.Update` down a build-placement
  branch dungeons cannot satisfy.
- Icons are taken off the entry already in hand — going back through
  `TypeAndPlacementObjects.GetByType` per structure is what other mods hook to lazily build menu
  entries, and doing that hundreds of times was slow and error-prone.
- Custom structures are saved by `InternalName`: `ToString()` on a GuidManager-minted enum prints a
  bare integer that resolves to something else on the next launch. Vanilla `PrefabPath` is a bare
  relative name and must be expanded into an addressable key the way
  `LocationManager.InstantiateStructureAsync` does.
- Props are pooled spawns, so the room snapshot resolves them back to their path without this tool
  tracking them. Cursor ghosts are live pooled objects and are explicitly excluded from snapshots.

### Enemy

Spawns enemies, vanilla and custom, from a thumbnailed grid grouped by catalog folder.

**Notes**

- There is no vanilla enemy factory or enum-to-prefab table — enemies are addressable prefabs under
  `Assets/Prefabs/Enemies/**`, so the catalog is enumerated straight from the Addressables locators
  (minus Dead Bodies and Weapons, which are corpses and projectiles).
- Custom enemies come from COTL_API's `CustomEnemyManager`, keyed by `InternalName`, never the
  runtime-minted `Enemy` enum value. `CustomEnemyList` is internal to COTL_API, so it is read via
  Harmony `Traverse` rather than depending on a publicized build. The custom group is read live —
  mods register at their own pace.
- Placed enemies are **live**: frozen only while the editor holds `timeScale` at 0, acting the
  moment it closes. They join `Health.team2`, so room-lock doors may close until they are dealt
  with.
- Spawning skips the teleport-in VFX (`withVfx`): its coroutine is frozen under `timeScale 0` and
  would leave the enemy invisible until the editor closes.
- Cells go in a few per frame and thumbnails render a few frames apart — a group of 150 enemies is
  150 Spine instantiations, and doing it in one frame is a visible stall.
- A spawned enemy is kept on the authored floor: vanilla rooms contain enemies with room-lock
  barriers and unit-position correction that custom maps do not get, so anything more than a
  node-and-a-half off the walkable A\* graph is snapped back.

**Thumbnails.** The game has no 2D icons for enemies anywhere. It solves this two ways: live
`SkeletonGraphic` cards (fine when every card shows the same skeleton with a different skin) and
`FollowersNameManager`-style baking (one reusable off-screen camera and RenderTexture, one
`Camera.Render()` per item, blitted into a shared atlas). A grid of 150+ *different* skeletons is
the second case, so the icons live in a few shared atlas pages — a handful of draw calls, and they
clip inside a scroll view like any other sprite. The subject is built from the prefab's
`skeletonDataAsset` rather than by instantiating the enemy; cloning a whole enemy (AI, health,
colliders, particles, child rigs) just to photograph it was the most expensive thing here.
Thumbnails render through unlit shader copies, because `LightingManager` writes **global** shader
values that every material samples — otherwise the icons wore the room's current lighting.

### NPC

Spawns non-combat characters from three sources: standalone NPC prefabs, characters extracted from
room prefabs, and mod-registered custom NPCs.

**Notes**

- `Assets/Prefabs/NPC/**` holds only **four** standalone NPC prefabs (the ghost children and the
  lost lamb). Every named character — Ratau, Midas, Plimbo, Sozo, the Fisherman, the marketplace
  vendors, Klunko & Bop, the Witness — is authored *inside* a room prefab under `Assets/_Rooms/`
  and has no prefab of its own. Nothing in the game spawns one from code.
- So a source room is a group: picking one loads that room prefab (cached) and lists the character
  subtrees inside it; placing instantiates just that subtree. Entries are keyed
  `room:<room prefab key>|<child path>`.
- Rooms are pooled into three buckets (story characters, vendors & games, special rooms) because
  one group per room meant a hundred-entry dropdown for a catalog where most rooms hold a single
  character. First matching bucket wins.
- **Character extraction goes bottom-up.** Searching top-down for "a node with a skeleton under it"
  matched the room root's own containers, so placing an NPC sometimes dropped an entire room into
  the scene, taking the editor's room references with it. Instead every skeleton is a candidate and
  is widened *upwards* only while the enclosing node still describes one character — the walk stops
  at room structure (a door, an island piece, a sprite shape), at ~250 transforms, or at more than
  four skeletons. Stopping at the first parent holding a *second* skeleton was too strict: many
  characters carry a companion rig, a VFX spine or a shadow, and their interaction components sit
  on the parent above both.
- `IsSafeToSpawn` is applied to what a key actually resolves to, not to what the scan thought it
  picked — a blueprint saved before that check can still name a whole room.
- Which characters a room holds is cached to `EditorCache/npc-index.json` between sessions: a
  bucket scan opens dozens of room prefabs and the answer only changes when the game updates. A
  stale entry costs a refused placement, which is logged.
- Custom NPCs are read live from `CustomNpcManager` and spawned through `CustomNpcManager.Spawn`.

### Podium

Places weapon selection podiums (the pedestals guaranteed in a dungeon's first room), with a
per-room "clear all on equip" rule.

**Notes**

- The game never instantiates these from code except via `Interaction_Chest`, so the prefab is
  acquired from a loaded chest's serialized asset reference or by cloning a scene podium before
  anything destroys it. Both routes keep the instance under an **inactive** holder while fields are
  fixed up: `OnEnableInteraction` destroys any podium whose `RemoveIfNotFirstLayer` flag is still
  set once the run has left its first room.
- Vanilla treats a room's podiums as choose-one-of-N: equipping from one disables the others. With
  `ClearAllOnEquip` false only the used podium is consumed. Three layers enforce that, because the
  podium prefab we place carries an *empty* `otherWeaponOptions` array while the room's authored
  podiums carry a populated one — so the podium doing the disabling is often not one we spawned:
  1. an `OnInteract` patch blanks the used podium's disable-others list;
  2. an `IsPodiumInSameRoom` patch makes vanilla skip any podium marked keep-usable;
  3. a per-podium component restores its own podium if something turned it off anyway.
- The toggle is the *room's* rule, not a stamp on the next placement — it applies to podiums the
  room generated with too, and those are the ones whose `otherWeaponOptions` is actually populated.
- Curse podiums destroy themselves when spells are disabled, so they are downgraded rather than
  silently vanishing.
- The room's authored podiums are captured on save: the snapshot deliberately never records podiums
  as props, so without this they were lost on load.

### Trigger

Boxes the player can step into, each running an ordered list of actions.

**Placing and editing.** Click empty space to drop a volume, click one to select it, drag its
centre to move or its corner to resize. Width/height sliders, *Fire once*, *Lock control while
playing*, *Show volumes in play*, Re-arm All, Delete Selected, Clear All. Volumes are drawn while
the tool is open, and optionally during play.

**Clear All asks twice.** The first press arms the button — it renames itself to `Delete all N?
Click again` and warns in the status bar — and a second press within 4 seconds does the wipe. The
window lapsing, or leaving the tool, puts the label back. Two presses rather than a dialog because
the panel has no modal of its own, and the wipe is not undoable: it clears the undo stack along
with the triggers.

**Action list.** *Add action* offers the action types; a second dropdown asks that type's follow-up
question and hides again once answered. Rows are numbered, reorder with `^` / `v` and delete with
`X`. Clicking a row outlines its target in the world in green — the object or NPC it moves to, the
volume it sends the players to, or the players themselves for an animation.

**Notes**

- Detection runs two ways because either alone has a hole in it:
  - a `BoxCollider2D` marked `isTrigger` with an `OnTriggerEnter2D` that only accepts a collider
    carrying `PlayerFarming` (vanilla's own rule, `TriggerCallback`) — this never fires unless the
    two objects' layers are enabled against each other in the collision matrix and one has a
    `Rigidbody2D`, neither of which a runtime-created object can guarantee;
  - a point-in-rectangle poll against every live player, which needs nothing from physics and is
    what actually makes the volume work in an editor-built room.
- Both funnel into an edge-triggered `Fire()`, so a trigger never fires twice for one entry.
- Every player is tested — both halves of a coop pair **and** `PlayerFarming.Instance`, which is the
  only one populated when coop features are off. The body collider is used where there is one: a
  player's pivot sits at their feet, so brushing the edge counts as stepping in.
- Nothing fires while the editor is open; the player is usually parked inside the volume being
  drawn, and standing there at `F4` time must not count as an entry when play resumes.
- **One sequence runs at a time, globally.** A trigger that fires during another's sequence defers
  rather than being consumed: it stays armed and un-entered and retries next frame. Two sequences
  would each take and hand back control of the same players, and whichever finished first would
  unfreeze them mid-scene.
- The sequence coroutine is hosted on the editor object, not the trigger: deleting the volume (or
  clearing the room) mid-sequence would otherwise kill it with the players still frozen.
- The gizmo flashes green as it fires, so "did it fire?" is answerable without reading the log.
- Ids only need to be unique within the room; they are how one trigger addresses another.
- `MapTriggerData.Action` (a free-text name) predates sequences and is kept only for blueprint
  compatibility.

### Door

Repositions, adds and removes the room's four doors.

**Notes**

- Doors are safe to move: `Door.OnTriggerEnter2D` switches on the Door's own `ConnectionType` and a
  private `NextRoom` index and never reads world position. The `PlayerPosition` marker is a child
  transform, so the player still arrives in the right spot.
- Add/remove operates on the door **island** (the `IslandPiece` the Door lives in), which bundles
  the door, its lock controller and the floor patch the player walks through.
- Each present door owns a **pad**: a collidable floor rectangle built from the room's shape
  template and merged into the composite, so the doorway is walkable wherever the door sits — the
  vanilla walkway is part of the island's authored shape and cannot move with a dragged door. Pads
  are derived state: never serialized, rebuilt on load. The pad's collision is a **box** because a
  sprite shape bakes an `EdgeCollider2D`, which a composite cannot merge ("not capable of being
  composited") — left like that the pad stayed a standalone solid wall across the room.
- Pads stay short by default; only a doorway the loader finds cut off from the floor grows, one
  step at a time.
- In authored rooms the door can sit on the room's one big floor shape, which cannot be hidden
  without removing the entire floor — so a removed door there gets a solid plug at the doorway
  mouth instead. Plugs are deliberately *not* part of the composite: a solid body inside the
  walkable union blocks movement, which is what a bricked-up doorway should do.
- Doors carry `PlayerDistanceMovement`, which caches `StartPos` — a **world** position — in
  `Start()`, long before a door is repositioned. Its `Update` then lerped the door back toward that
  stale anchor: the door "randomly" drifting away, taking its walkable floor with it. Vanilla hits
  this too and only fixes it for the entrance door; every door we move needs the anchor re-cached.
- `Door.OnDisable` removes the door from `Door.Doors`, so a deactivated door vanishes from that
  list and can never be found again — which is why it looked permanently deleted. The tool keeps
  its own references. Lookups use `includeInactive`, or a toggled-off door cannot be toggled back
  on.
- Every blueprint carries all four doors, because a node is dropped into whatever slot the
  generated walk gives it. Doors the graph does not use are set to `ConnectionTypes.False` —
  vanilla's own inert setting, where `Door.OnTriggerEnter2D` returns immediately — rather than just
  barriered, because a barrier hides a dead end visually while the trigger still fires and sends
  the player to a room that does not exist. Entrance, Exit and NextLayer doors are left alone.
- Doors are dragged through their handle via EventSystem drag events, not by polling
  `Input.GetMouseButton`: the polled version moved the door on any held click, so pressing a
  toolbar button teleported the nearest door to the cursor.

### Lighting

Edits the room's lighting and fog, and saves the values on the blueprint. A finished look can also
be saved as a named **lighting profile** (Save As Profile → name dialog), stored outside any map in
`LightingProfiles.json`, and applied to any other map from the Profiles dropdown — or at play time
by a trigger's *Apply lighting* action.

**Notes**

- Applying a profile copies its values onto the map (and the blueprint saves them as its own), so a
  profile deleted later does not hollow out maps that used it. Only a trigger's *Apply lighting*
  action references a profile by name at run time — a blueprint shared to a machine without that
  profile logs a warning and skips the action.
- Saving a profile while "following the biome" captures what is currently on screen first, so the
  profile holds a real look rather than defaults.

- Lighting is not an object that can be placed: the game drives it from a `BiomeLightingSettings`
  asset applied by `LightingManager`, so the tool edits a settings instance of its own and pushes
  it through the game's own override channel (`overrideSettings` + `inOverride`) — the same one the
  NightFox interaction uses — with per-property flags so only what the blueprint sets is
  overridden.
- `LightingManager`'s transition advances its timer with `Time.deltaTime` unless the settings ask
  for unscaled time, and only ends once the timer passes the duration. The editor runs at
  `timeScale 0`, where `deltaTime` is zero: the loop spun forever with `lerpActive` stuck true and
  `UpdateLighting` swallowed every later call. One change landed at full strength and nothing moved
  again until the editor closed. The tool's settings therefore ask for unscaled time.
- `forceUpdate` matters: `TransitionLighting` bails out early when it decides current and target
  are equivalent, and our edits change shader globals without always changing the asset it compares
  — which is why *Reset To Biome* could look like it did nothing.
- Resetting restores the **captured** biome values rather than just clearing `inOverride`: clearing
  it transitions to `LightingManager`'s time-of-day target, which in a dungeon is not the biome's
  own lighting, and the room kept the custom mood.
- A blueprint that never captured anything starts from what the room actually looks like, so the
  first slider drag is a nudge rather than a jump to black.

### Music

Picks the blueprint's music from every music event in the loaded FMOD banks. Selecting a track
plays it immediately as the preview; the choice is replayed when the blueprint loads. The empty
selection ("Vanilla") keeps the biome music.

**Notes**

- The list is filled on tool entry, not when the panel is built — FMOD banks are not guaranteed to
  be loaded at that point. The game loads its banks at startup, so the set is stable afterwards.
- It is a dropdown rather than a grid because tracks have nothing to show as an icon, and "Vanilla"
  is the first entry rather than a separate Clear button: no music *is* a choice of music.

### Clear

Bulk-wipes room contents at two levels: everything the editor placed, or the procedurally generated
backdrop as well.

**Notes**

- Biome lighting, `BiomeVolume` and parallax are deliberately left alone — removing them makes the
  scene unreadable and they are not what "background objects" means here.
- Much of the backdrop hangs directly off the room root rather than under `SceneryTransform`, so it
  is swept separately. A node holding a door is descended into but never destroyed, so dressing
  that shares a parent with a door still goes.

### Load Map

Lists the saved blueprints under `CustomNodeBlueprints/` with their save-time screenshot, and loads
the chosen one. Loading clears the room, rebuilds it, closes the editor and walks the player in
through the entrance door; press `F4` afterwards to keep editing.

**Notes**

- Screenshots are megabytes of texture each, so they are read as cells appear and dropped when the
  panel closes.
- A manual load cancels any running level: a stale run advancing on the next door would teleport
  the player into an unrelated room chain.

### Level

Authors `CTLevelBlueprint`s — the room chain a custom level generates from. Create or open a level,
set how many rooms it has, and pick which node blueprints each room may generate from. **Play
Level** resolves the chain, re-enters the dungeon scene and loads the entrance room; doors then
advance through the chain.

**Notes**

- `Rooms[0]` is always the Entrance and `Rooms[^1]` always the Exit; added rooms go between them
  and neither end can be removed.
- A pool entry can be `<vanilla>`, meaning "leave this room as the game generated it", so a level
  can mix authored and vanilla rooms. An empty pool means "any saved node".
- Playback follows the F5 convention end to end: `EnterDungeon()` reloads the scene,
  `BiomeGenerator` lays out `NumRooms` with its normal walk, doors and room changes are fully
  vanilla, and each generated room is rebuilt from a node blueprint via `OnRoomGenerated`.
- The room hook fires *inside* vanilla generation while the transition still covers the screen, so
  the apply routine holds that cover (`MMTransition.CanResume`), swaps the blueprint in behind it,
  and only then resumes — the player never sees the vanilla room.
- Slot mapping: the first generated room takes Entrance, the room owning the exit (NextLayer) door
  takes Exit, and others consume middle slots in discovery order. Revisited rooms regenerate
  vanilla content, so their remembered slot re-applies.
- All playback state is static: scene reloads destroy the editor host, and each fresh host re-binds
  via `OnEditorReady`.

---

## Trigger actions

Six action types, each authored through the trigger tool's dropdowns:

| Action | Target | Behaviour |
| --- | --- | --- |
| Move players to trigger | another trigger's Id | walks the players to that volume's centre |
| Move players to object | a clicked object | walks them to that object (falls back to the authored position) |
| Talk to custom NPC | a registered `InternalName` | runs that NPC's dialogue tree and waits for it |
| Play animation on players | an animation on the player skeleton | plays it once, or loops it for 2 / 5 / 10s |
| Apply lighting | a saved lighting profile, or "Vanilla lighting" | cross-fades the room's lighting over 1 / 2 / 4s (or instant), then moves on; vanilla restores the biome's own values |
| Change music | an FMOD music event | starts the track and keeps it looping; does not wait for it |

**Apply lighting asks for a fade length** after the profile, the way the animation action asks for
a loop length, and stores it in the same `Duration` field. The fade goes through the manager's own
`transitionDurationMultiplier` (it scales a 5-second `transitionDuration` rather than taking
seconds, and resets the multiplier after every transition, so it is set per apply) — the game's own
cross-fade, not a hand-rolled one, so fog, exposure, LUTs and light rotation all move together. The
sequence does not wait for it: the new light comes up under whatever runs next. A fade starting
while another transition is still unwinding cancels it, waits for it to land and re-reads the live
values first, because the manager would otherwise lerp from a `currentSettings` that no longer
matches the screen and jump before fading. Actions saved before the picker existed have no duration
and take the 1.5s default; a negative duration is the picker's explicit *Instant*. The tool's own
sliders and the blueprint loader stay instant, where a fade would read as lag.

**Change music does not block the sequence.** Music plays under whatever happens next — an action
that waited for a track to finish would freeze the players for the length of the song. Looping goes
through the same watchdog blueprint music uses (`SetMusicLoop`), which restarts the event when FMOD
reports it stopped, because FMOD events only loop if they were authored to. Starting a second music
action replaces the first one's watchdog, so two tracks cannot fight over the channel.

**Everything drives the game's own player API** rather than moving transforms: `GoToAndStop`
pathfinds and animates the walk, `CustomAnimation` owns the spine track and the state it belongs
to, and `InActive` is the state the game itself parks the player in during a cutscene
(`PlayerFarming.Update` returns early on it, so no input is read).

**Coop is a first-class case.** Every routine works over the whole player list, never
`PlayerFarming.Instance` alone:

- one player lands on the target spot; several settle evenly around it on a ring, so a pair never
  ends up inside each other. The game's own group move offsets followers a flat 1 unit below the
  leader, which is why `groupAction` is left off;
- an animation plays on every player, offset by a small random delay — two lambs on the same frame
  of the same animation look like one puppet mirrored.

**Control lock** (per trigger). While the sequence runs the players are parked in `InActive`; the
lock is lifted around actions that need their input (a conversation, whose wheel needs a button
press) and retaken for the next one that does not, so "walk over, talk, walk away" works. Moves use
`forcePositionOnTimeout`, so a blocked path snaps and continues rather than stalling the scene.

**Waiting on a conversation** uses `NpcDialogueRunner.IsRunning`, *not* `MMConversation.isPlaying`:
a dialogue tree is a chain of conversations (one per node) and `isPlaying` drops between them, so
waiting on it alone let the sequence resume during the first gap. A ten-second silence while the
runner still believes it is mid-chain is treated as an interrupted conversation and the sequence
continues rather than leaving the players frozen.

**Objects are addressed by scene path** with the authored position as a fallback, since the room
rebuilds its hierarchy on load. Failing an exact path match, the leaf name is matched instead.

---

## Custom NPCs and dialogue

Custom NPCs are defined on disk under `CustomNpcs/<name>/config.json` (spine files auto-discovered
from the same folder) and registered with `CustomNpcManager`, mirroring COTL_API's
`CustomEnemyManager` minus the combat.

**Spawning.** The clone starts under an **inactive** holder and every mimic script is destroyed
before it wakes. This is not tidiness: mimic scripts key their behaviour to save state (`GhostNPC`
turns the whole object off when its rescue conditions are not met), and a deferred `Destroy` after
a live `Instantiate` loses that race — which is exactly why the editor preview showed while the
placed NPC vanished. Only `Spine`-namespace behaviours survive.

**Dialogue.** `Dialogue` in the config is a node graph: each node has lines and either a `Next` or
exactly two `Choices`.

- **One node = one `MMConversation`.** The choice wheel only appears after a conversation's last
  line, so a mid-tree branch must end its conversation and start the next from the response
  callback. Every `Play` passes `CallOnConversationEnd: false` and the letterbox/camera/input
  teardown runs exactly once, at the true end of the chain.
- `DialogueWheel` renders **exactly two** responses (a fixed serialized array); a node with any
  other count keeps its lines and loses its choices.
- `ConversationEntry.Callback` is a `UnityEvent` whose runtime listeners **never** fire
  (`MMConversation` only invokes it when its persistent count is non-zero), so everything routes
  through `ConversationObject.CallBack` and `Response.ActionCallBack`. `Response` must be fully
  qualified as `MMTools.Response` — a legacy top-level `Response` class also exists.
- **Raw strings never render.** `MMConversation.UpdateText` runs every line through
  `LocalizationManager`, so lines, choices and the character name are registered as real I2 terms.
  The game's source uses `MissingTranslationAction.ShowTerm` and resolves by the *current* language
  index, so every language slot is filled — filling slot 0 alone is why dialogue showed raw keys.
  Terms are re-registered lazily because the game rebuilds its language source during load.
- **Per-line animation** with a `Loop` flag. A looping line passes an empty `DefaultAnimation`:
  the game queues `DefaultAnimation` unconditionally, and Spine starts a queued animation after one
  cycle of a looping predecessor, so a looping line otherwise played exactly once.
- Text supports the game's Febucci tags (`<wave>`, `<wiggle>`, `<shake>`, `<bounce>`, `<rot>`,
  `<swing>`, `<rainb>`, `<speed=X>` reset with `<speed=1>`) and TMP markup. See the shipped
  `CustomNpcs/TestNpc/config.json`, which demonstrates each one.

**Interaction.** The "Talk" prompt is a plain `Interaction` (no collider needed — the base class
keeps a distance-scanned static list). `IgnoreTutorial = true` is required or the label is blanked
pre-tutorial, and `base.OnInteract(state)` must be called: it closes barks, sets the main player and
plays the SFX.

---

## Saving and loading

A node blueprint is a **full snapshot** of the room: loading always clears everything first, so a
deleted object is simply absent from the snapshot and vanilla scenery is captured as `Props`
entries.

**Snapshot resolution** is tiered:

1. `ObjectPool` bookkeeping — `spawnedObjects` maps a live instance back to its prefab, and the
   pool's path dictionaries map that prefab back to the string it was spawned from. Exact for all
   pooled decorations and critters.
2. Name matching against the Addressables catalog for anything instantiated directly (island
   pieces, encounters, secondary sprite shapes, ctrl-drag clones). Only names ending in `(Clone)`
   are considered — prefab-authored children never carry that suffix, which cleanly separates
   runtime additions from content a recorded prefab brings back itself.
3. Anything else is logged and skipped.

Structures and custom enemies are keyed by **name**, never by enum integer: vanilla ids shift
between game versions and COTL_API mints custom ones at runtime via `GuidManager`.

**Load order:** capture (templates and profiles that clearing would destroy) → clear → shapes →
props → structures → doors → enemies → NPCs → podiums → triggers → one batched collision and
pathfinding rebuild → close the editor and walk the player in through the entrance door, mirroring
the game's own first-arrival routine so they are never clipped into terrain. Triggers come last
because their actions address NPCs and objects by name. Everything up to the walk-in runs with the
editor open at `timeScale 0`; the walk-in needs real time and a fresh A\* graph, so the editor is
closed first. Every phase logs and continues per item — one bad entry never aborts the load.

**The save dialog** is the game's own naming modal (`UICultNameMenuController`, the one the cult is
named through), with its disclaimer line repurposed into a live overwrite warning. The editor
closes fully before it opens and reopens (restoring camera, zoom and tool) when it closes: the
modal's show/hide animations run on **scaled** time, so the editor's `timeScale 0` froze the dialog
half-open, visible and permanently unable to take a keystroke.

---

## File map

| File | Role |
| --- | --- |
| `RuntimeMapEditor.cs` | Editor host: canvas, dock, panels, camera, pause, save/load entry points |
| `IMapEditorTool.cs` | Tool interface (`OnEnter` / `OnExit` / `OnUpdate` / `BuildPanel`) |
| `MapEditorUI.cs`, `MapEditorWidgets.cs` | Widget layer: plates, buttons, sliders, toggles, dropdown, grid, scroll column |
| `MapEditorIcons.cs` | Every sprite the chrome draws (tool icons, structure icons, prop icons) |
| `MapEditorData.cs` | Blueprint schema and JSON read/write |
| `MapEditorHistory.cs` | The single undo stack |
| `RoomSnapshot.cs` | Captures the live room into a blueprint |
| `BlueprintLoader.cs` | Rebuilds a room from a blueprint |
| `SceneRefs.cs` | Null-guarded access to room / camera / content root |
| `MapNamePrompt.cs` | Save dialog built on the game's naming modal |
| `EnemyThumbnails.cs` | Baked Spine icons for the enemy and NPC grids |
| `CustomShapeProfiles.cs` | Disk-loaded SpriteShape profiles (`CultTweaker_*`) |
| `CTLevelBlueprint.cs`, `CTLevelDungeon.cs`, `LevelPlayback.cs` | Level tier: data, dungeon, run driver |
| `CustomRoomPatches.cs` | Marks rooms whose contents a blueprint replaced |
| `Tools/*.cs` | One file per tool, plus shared gizmos, ghosts and protection rules |
| `Tools/TriggerActions.cs` | Trigger action model and the sequence runner |
| `Npc/*.cs` | Custom NPC behaviour, dialogue schema and dialogue runner |

Anything protected by `MapEditorProtection` is never destroyed by the clear or delete tools — doors
in particular are `IslandPiece`s carrying the `RoomLockController`, so destroying one soft-locks the
room: it can never be completed or exited.
