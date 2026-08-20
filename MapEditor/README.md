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
  - [Clear](#clear) · [Load Map](#load-map) · [Level](#level) · [Dungeon Builder](#dungeon-builder)
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

Click an object to select it, then delete it individually. Ctrl-click-drag clones it. A selection
carries two drag nodes, the same pair the trigger tool uses: a yellow one at the centre that moves
it and a blue one on the top-right corner that resizes it (hold Shift to stretch a single axis).
Also offers send-back / bring-front (Z nudge) and horizontal flip.

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
- Resize is uniform unless Shift is held: room dressing distorts badly when its axes are scaled
  apart, so a stretch should be asked for rather than arrived at. Each drag scales from the size
  captured on mouse-down, never from the previous frame, so a slow drag and a fast one land in the
  same place.
- Resizing grows the object about its *visible* centre, not its transform pivot — pivots sit
  wherever the artist left them, often on a floor line, and scaling around one walks the object out
  from under the cursor. The correction is applied every frame by measuring the bounds again.
- Doors refuse to resize for the same reason they refuse to flip.
- Scale round-trips for everything the editor can place. Props and kept authored objects always
  carried it (`RoomSnapshot` captures `lossyScale`); structures, enemies, NPCs and podiums did not,
  because each is written by its own tool's `ContributeTo` and those wrote position only — so a
  resized NPC came back its original size. They now save `lossyScale` too and the loader applies it
  after each spawn, through the `LastPlacedInstance` each tool exposes (the spawn routines are
  coroutines and hand nothing back). A structure stores the *absolute* scale, because its mirror is
  stored separately as `FlipX` and re-applied on load — a negative X here would cancel it out.
  A null scale is a blueprint written before any of this existed, and means "leave the spawn as it
  came".
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

- There is no vanilla enemy factory or enum-to-prefab table — enemies are addressable prefabs, so
  the catalog is enumerated straight from the Addressables locators (minus Dead Bodies and Weapons,
  which are corpses and projectiles).
- **Three address spaces, not one.** `Assets/Prefabs/Enemies/**` is only part of the roster;
  `Assets/Resources_moved/Enemies/**` and a bare `Enemies/**` are left over from the pre-Addressables
  Resources folder and were never re-addressed. They hold 103 further prefabs — Leshy, Heket,
  Kallamar, the base Scamp/Archer/Swordsman/Brute roster and most of the Dungeon 1–4 enemies — with
  no filename overlap with the first space, so all three are scanned and merged by folder.
- Three bosses are aliased in the grid (`Leshy (Worm Boss)` and friends): the prefabs are named for
  what the boss is, not who it is, so searching the list for a bishop's name found nothing.
- **Shamura and Narinder have no enemy prefab at all.** Neither appears at any address; they are
  authored inside their boss rooms (`Assets/_Rooms/…`) and wired to a `MiniBossController` /
  `SpiderHeadManager` in that room. Reaching them means extracting from a room prefab the way the
  NPC tool does, not a catalog fix.
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
- **Everything picked is a dropdown**, the same widget the other tools use: open a level, select the
  room being edited (pre-selected on rebuild, which is what the old `<` marker did), set its
  modifier, add to its pool. The panel used to be a stack of buttons that grew by one for every
  level and every map ever saved.
- The pool follows the trigger tool's add-and-remove shape rather than a checkbox per blueprint: the
  dropdown offers only what is *not* in the pool, and each member gets an `X` row. So the panel
  scales with the pool, not with the save folder.
- The whole lower panel is destroyed and rebuilt after each pick, dropdowns included — picking from
  one that is about to be destroyed is safe, since the widget closes its overlay before invoking the
  handler and reads nothing afterwards.
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

### Dungeon Builder

Authors a **dungeon** as the Slay-the-Spire-style node graph the game shows between rooms: one node
is one level blueprint, and the graph of them is the whole run. Saved to
`CustomDungeonMaps/<name>.json`, and every saved file registers a custom dungeon at startup — so the
map *is* the dungeon, and there is no second file to keep in step with it. **Enter Dungeon** runs it.

Four buttons, because the others turned out to be the same job twice. **Save Dungeon** opens the
game's name dialog — the same one the map save and the lighting profiles use — prefilled with the
current name, so it is also the rename (confirming a different name writes a second dungeon and
leaves the first alone) and it carries its own overwrite warning, which is what the old
press-twice-to-confirm was for. A separate *Preview Map* button went too: the grid overlay already
draws the graph with the game's own node icons, and the only thing the real selector added was the
jitter it applies on top.

The side panel keeps the usual shape (new / open / rename / save / enter / grid size); the graph is
edited on its own **grid overlay** behind *Edit Nodes*. **Left click places or selects; right click
links.** Left-clicking an empty cell places a node, left-clicking a node selects it (and again
deselects), and right-clicking a second node links or unlinks it to the selected one. Placing a node
one layer from the selected one links them straight away, which is how a path gets laid out in one
click per step. The node type picker retypes the selected node, or sets what the next placed node
will be.

Left click deliberately never links: clicking a second node used to mean "join these" while a
selection was live and "select that one" otherwise, which is the same gesture doing two things.
Right click is polled from `Input.GetMouseButtonDown(1)` and hit-tested against the cell rects
rather than taken from the EventSystem — this game installs Rewired's pointer module, which the
editor already works around for left clicks, and a link gesture that silently never fired would be
worse than a hit test of our own. The overlay canvas is `ScreenSpaceOverlay`, so the null camera
passed to `RectangleContainsScreenPoint` is correct.

**Where the game's map lives.** `MapManager` (namespace `Map`, an embedded copy of the open-source
Slay-the-Spire map package: `MapConfig`, `MapGenerator`, `Map`, `Node`, `NodeBlueprint`, `NodeType`,
`Point`) holds `CurrentMap`, and `UIAdventureMapOverlayController` renders it. `EnterNode` is what
turns a node into rooms: it reads `node.blueprint.RoomPrefabs` and feeds `BiomeGenerator` — the same
seam level playback rides.

**Nodes play levels.** Each node is bound to a `CTLevelBlueprint` from the Level tool through the
overlay's *Plays level* dropdown; bound nodes carry a green corner badge. Entering one generates
that level's room chain instead of what the node's type would have produced. A node left on
*Vanilla floor* behaves exactly as the game intended, so a run can mix both.

**The bottom node is the first floor**, and there can only be one of it — the game does not let the
player choose where to start: its renderer marks `GetFirstNode()` visited and offers that node's
links, so a second bottom node would be drawn and never reachable. The exit door then decides:

| Where the run is | What the exit door does |
| --- | --- |
| below the top layer | opens the map selector to pick the next floor |
| on the top layer | shows the completion screen — the run is over |
| map not playable / missing | shows the completion screen |

**Notes on binding**

- The binding hangs off `MapManager.EnterNode` as a **postfix**. The vanilla body sets the floor up;
  the postfix adjusts it, and it is still early enough because `Regenerate` defers the entire
  generation into an `MMTransition.Play` callback — nothing has read `OverrideRandomWalk` or
  `NumberOfRooms` by the time the postfix returns.
- A bound node forces `OverrideRandomWalk = false`, because only the floor types (`FirstFloor`,
  `DungeonFloor`, `MiniBossFloor`, `Boss`, `FinalBoss`) generate a multi-room floor — every other
  type is a single fixed room, which would show just the level's entrance. So binding a level to a
  Treasure node turns it into a floor.
- `BiomeGenerator.NumberOfRooms` is set to the level's room count and the biome's own value is put
  back when a node without a level is entered. It is a field on the scene's `BiomeGenerator`, so a
  level's length would otherwise stick to the rest of the run.
- **The room hooks had to learn about vanilla dungeons.** Both `Door.OnTriggerEnter2D` and the
  `GenerateRoom.Generate` postfix used to return early unless the current dungeon was a registered
  custom one. A bound node plays inside the *vanilla* dungeon it was entered from, so both now also
  run while `LevelPlayback.Active` — without the door half, `GenCheck` stays latched from the last
  door and the next room's blueprint is never applied. The `NextLayer` exit-door branch stays
  custom-only: on a real map that door is how the next node gets picked.
- Entry from the map is not a door, so nothing resets the hand-off the room hook reads;
  `DungeonPatches.ResetRoomHandoff()` does it when the level binds.
- **The door prefix repeats vanilla's own opening conditions before it acts** (`IsPlayerUsingDoor`):
  a `PlayerFarming` collider, not already `Used`, no transition playing, not `GoToAndStopping`, and
  not a `False`/`LeaderBoss` door. It used to act on any collider touching any door trigger, which
  produced two separate mysteries. Followers, thrown items and knocked-back enemies were setting
  the room hand-off at arbitrary moments, so blueprints re-applied to rooms that were already built
  and their doors jumped to another room's authored positions. And the player's own scripted
  walk-in — `GoToAndStopping`, the thing vanilla checks precisely so an arrival cannot use a door —
  reached the far door of a small single-room node (a Wood or Food room) and fired the exit, so the
  dungeon map reopened the instant a node was entered. The `NextLayer` branch also marks the door
  `Used` now: it never reaches vanilla, so nothing else would.
- `LevelPlayback.StartForMapNode` deliberately skips the `EnterDungeon` that F5 playback does —
  `EnterNode` has already queued a regenerate of the floor in place, and re-entering would throw
  that run away. Both paths share one `Resolve` for picking a node blueprint per level room.
- The map holds names, not blueprints: a level renamed or deleted after binding logs a warning and
  the node falls back to a vanilla floor. Save and Enter both name it, since it is nearly always a
  rename.

**Notes on the dungeon**

- The map is remembered **on entry** (`DungeonMapPlayback.UseMap`), not looked up on exit: by the
  time the exit door asks, the thing that knew which map this dungeon uses is out of reach.
- **A custom dungeon has to be told which encounter layer it is on.** `CustomDungeon.EnterDungeon`
  calls vanilla's `Interaction_BaseDungeonDoor.GetFloor`, which reads the layer out of save data
  keyed by location — and `DataManager.GetDungeonLayer` returns 0 for anything it does not
  recognise, which every minted location is. `IslandPiece.AvailableOnLayer` has no case for layer
  0, so *every* island encounter reported itself unavailable and a node without a level generated
  rooms containing nothing: no enemies, no resources, and the generator logging that it had run
  out of encounters. `CustomDungeon.DungeonLayer` (1–4, default 1) is clamped into
  `GameManager.CurrentDungeonLayer` straight after that call.
- Boss and MiniBoss nodes with no level bound still generate an *ordinary* floor: vanilla decides
  whether a floor is a boss fight from `DataManager.DungeonBossFight`, which `GetFloor` computes
  from the same save data a custom location has none of. Saving warns rather than refusing —
  the map plays, the icon just promises more than the floor delivers. Bind a level to author what
  happens there.
- **The start node's level binds in `OnBiomeReady`, not in `EnterDungeon`.** A level run is static
  state, and everything between the button press and the new scene can end it: the editor closing,
  the old scene tearing down, the entry guard, a node-entry patch firing on somebody else's map.
  Binding in `EnterDungeon` survived about three log lines. `CustomDungeon.OnBiomeReady` is called
  from `BiomeGenerator.OnEnable`, in the dungeon's own scene, once per entry and before any room
  generates — the first moment at which nothing left over from the old scene can undo the binding,
  and still early enough that the entrance room's hook sees it.
- `DungeonMapPlayback.OnNodeEntered` acts only on nodes from the graph *it* installed
  (`ReferenceEquals(MapManager.CurrentMap, _built)`). The `EnterNode` patch fires for whatever map
  the game is showing, and a node from the player's ordinary adventure map reads as "a node with no
  level" — which ended the run this dungeon had just bound.
- `LevelPlayback.Stop` logs its caller. A level run ending early is otherwise invisible: the
  symptom appears rooms later as a floor that generated vanilla content, with nothing in the log
  tying it to whoever ended the run.
- A dungeon that binds a level before its scene loads must say so with
  `CustomDungeon.DrivesLevelPlayback`. `BiomeGenerator.OnEnable` ends any level run whose dungeon
  did not bring its own — otherwise a run's statics leak into an unrelated scene — and that guard
  used to name `CTLevelDungeon` as the one exception.
- Arriving in the dungeon plays the bottom node's level without showing the map. That lines up with
  the game: the first time the selector opens it marks `GetFirstNode()` visited, so layer 0 is
  already behind the player.
- A scene load builds a fresh `MapManager` with no map of ours in it, so the graph is rebuilt and
  re-installed on the first exit after entering. Node entry does *not* reload the scene
  (`Regenerate` passes `MMTransition.NO_SCENE`), so progress along `Map.path` survives between
  floors — which is what the top-layer check reads.
- `CustomDungeonManager.Add` mints a `FollowerLocation` from `GuidManager` keyed by a name, and every
  map dungeon shares the same `Location` seed — so `Add` now keys on `InternalName` when there is
  one. Without that the second dungeon minted the first one's value and threw on insert.
  Registration is idempotent: a map already registered keeps its minted location and only its graph
  is refreshed, which is what lets *Save Dungeon* make it enterable without a restart.
- `SceneName` is a json field with no control in the tool. The editor only knows `Dungeon1` is real,
  and offering scene names that may not exist is worse than editing the file.
- `CTLevelDungeon` (the Level tool's *Play Level*) clears the installed map on entry, so a map left
  behind by a *Preview Map* press cannot turn a single level's exit into a node picker.

--- | --- |
| empty | shows the completion screen — a one-floor dungeon |
| set, current node below the top layer | opens the map selector to pick the next floor |
| set, current node on the top layer | shows the completion screen — the run is over |

**Notes**

- The map is remembered **on entry** (`DungeonMapPlayback.UseMap`), not looked up on exit: by the
  time the exit door asks, the blueprint that named it is out of reach.
- The first floor is the map's bottom layer. `ShowMap` marks `GetFirstNode()` visited and offers
  its outgoing links, so arriving in the dungeon and then meeting the map lines up with layer 0
  already being behind the player.
- A scene load builds a fresh `MapManager` with no map of ours in it, so the graph is rebuilt and
  re-installed on the first exit after entering. Node entry does *not* reload the scene
  (`Regenerate` passes `MMTransition.NO_SCENE`), so progress along `Map.path` survives between
  floors.
- `CustomDungeonManager.Add` mints a `FollowerLocation` from `GuidManager` keyed by a name, and
  every json dungeon shares the same `Location` seed — so `Add` now keys on `InternalName` when
  there is one. Without that the second dungeon minted the first one's value and threw on insert.
  Registration is idempotent: a dungeon already registered keeps its minted location and only its
  blueprint is refreshed, which is what lets *Save Dungeon* make a dungeon enterable without a
  restart.
- `CTLevelDungeon` (the Level tool's *Play Level*) clears the installed map on entry, so a map left
  behind by a *Test Map* press cannot turn a single level's exit into a node picker.

**Notes**

- **It is a grid because the renderer is.** `MakeMapNode` positions every node at
  `new Vector2(point.x, point.y) * 300f + Random.insideUnitCircle * 50f` — the integer cell is the
  position, `Node.position` is not read at all, and the jitter is re-rolled every time the map
  opens. Authoring finer than a cell would be discarded on the first open.
- Editing happens on the editor's own canvas rather than inside the vanilla overlay, which is a
  `UIMenuBase` built in one pass in `OnShowStarted`, wired into the game's menu stack and Rewired
  navigation, and which pauses the simulation and pulls the camera's far plane to 0.02 to hide the
  world. The editor is deliberately outside that stack.
- Layer 0 is the bottom row on screen and the start of the run, matching `point.y`. `outgoing`
  points up the map (toward the end), `incoming` back down; the builder fills both from the single
  authored `Outgoing` list, because two stored directions of one fact drift apart.
- Links only join neighbouring layers. Traversal itself does not check — `GetNextAdventureMapNodes`
  just walks `outgoing` — but the player moves one layer per step, so a longer link draws a line
  nothing can use.
- Save always writes, even when the map is not playable yet; the status bar says what is missing.
  **Test** refuses, because each rule it checks is a crash or a blank screen in the game's own code:
  a dangling link is an unchecked `NodeFromPoint` in `MakeLineConnection`; no node on layer 0 makes
  `GetFirstNode()` (a `.First()`) throw; a node with no links at all is silently skipped by the
  renderer; and every node has to be reachable from layer 0 or it is drawn but unenterable.
- The node type list is only the types the loaded `MapConfig` has a blueprint for — a type without
  one has no icon and no `RoomPrefabs`, so it would place a node that cannot be entered. Cell icons
  are the blueprint's own sprite via `GetSprite`.
- `Node`'s constructor hides one node in ten at random; authored nodes are built with
  `Hidden = false, CanBeHidden = false` so the map shows what was drawn.
- A hand-built map leaves `MapGenerator`'s static layer list empty, so the three tarot cards that
  rewrite the map at runtime (shuffle, randomise-next, teleporter) no-op on custom maps. The other
  readers of that state (`WorldManipulatorManager`, `DungeonLeaderMechanics`) already test for
  `Nodes.Count == 0`.
- Test closes the editor first (`ExitForPlayback`), as Play Level does: the selector is a real menu
  and needs `timeScale`, the HUD and the camera handed back.

---

## Trigger actions

Actions are authored through the trigger tool's dropdowns, grouped: *Add action* offers a category,
the same dropdown then offers that category's actions, and after that whatever the chosen action
still needs. A category holding one action skips its own submenu.

**Player actions**

| Action | Target | Behaviour |
| --- | --- | --- |
| Move players to trigger | another trigger's Id | walks the players to that volume's centre |
| Move players to object | a clicked object | walks them to that object (falls back to the authored position) |
| Talk to custom NPC | a registered `InternalName` | runs that NPC's dialogue tree and waits for it |
| Play animation on players | an animation on the player skeleton | plays it once, or loops it for 2 / 5 / 10s |

**Camera actions**

| Action | Target | Behaviour |
| --- | --- | --- |
| Look at object | a clicked object | frames it for 0.5–8s, then hands the camera back |
| Look at trigger | another trigger's Id | same, aimed at that volume's centre |
| Set camera offset | framed in the editor | shifts the camera relative to whatever it follows |
| Reset camera offset | — | back to centred on the players |
| Set camera zoom | 1–10 | follow distance; smaller is closer, 10 is the rig's own resting value |
| Reset camera zoom | — | back to whatever the rig was on before a trigger touched it |
| Play camera effect | chromatic aberration, vignette, desaturate, shake, letterbox in/out | runs the effect and waits for it |
| Play cutscene | a video in `CustomCutscenes`, or a vanilla one | plays fullscreen and waits for it to end |

**Screen text**

| Action | Target | Behaviour |
| --- | --- | --- |
| Caption (bottom left) | typed title + subtext | the pair, bottom left |
| Title (top of screen) | typed title + subtext | the same pair, top centre |
| Fullscreen text (centre) | typed title + subtext | the pair centred over a 75% dimmed screen |

**Ambient actions**

| Action | Target | Behaviour |
| --- | --- | --- |
| Apply lighting | a saved lighting profile, or "Vanilla lighting" | cross-fades the room's lighting over 1 / 2 / 4s (or instant), then moves on; vanilla restores the biome's own values |
| Change music | an FMOD music event | starts the track and keeps it looping; does not wait for it |

**Wait for seconds** stands on its own: 0.5–8 seconds of nothing, for pacing between two other
actions. It waits in realtime, because a sequence is often running while the game is paused around
a conversation and a scaled wait would never end there.

### Camera

Everything here drives `CameraFollowTarget`, the rig the game's own cutscenes push around, rather
than moving the camera transform — which the rig would overwrite on the next frame.

- **Offset** is `TargetOffset`, which the rig lerps towards, so the shift is a glide. It is stored
  relative to the follow target rather than as a world position, because at run time the players
  are somewhere else entirely. Authoring it is by eye: choosing the action snaps the editor view
  back onto the players, you pan with `WASD` to the framing you want, and `V` takes the difference.
- **Zoom** is `targetDistance`. The reset remembers what the rig was on before a trigger first
  touched it, captured lazily — the rig's own value is only settled once the room has generated.
- Both are global state on a rig that outlives the room, exactly like the lighting override, so
  `TriggerCameraActions.ResetAll()` is called from the same place lighting is cleared
  (`BiomeGenerator.OnEnable`). Without that, a room that zoomed in would hand the zoom to the next.
- **Look at** swaps the rig's follow targets rather than teleporting: the move is the camera's
  ordinary smoothed follow, and the players keep playing underneath. The anchor is parented to the
  object so a moving target stays framed, target weights are restored exactly (co-op frames two
  players by weight), and if everything it was following is gone by the end — a room reload during
  the shot — the player's camera bone is put back, because the rig only updates while it has a
  target.
- **Effects** are `BiomeConstants`' own post-processing tweens (the ones the bosses and the winter
  events use), `CameraManager.ShakeCameraForDuration`, and the cinematic `LetterBox`. The pulses run
  out and back over the action's duration, so a sequence cannot leave the screen stuck in an effect
  it forgot to undo; each tween is told where to start as well as where to end, so the return leg is
  exact. The letterbox is the exception — bars are a state, which is why they are two actions.

### Screen text

Three actions, all drawing the same pair of lines: a **title** and an optional **subtext** under
it, at two clearly different sizes. Each is typed into its own dialog when the action is added
(title, then subtext, then how long it stays up) — the second can be left empty, since a title on
its own is a perfectly good caption.

| Action | Where |
| --- | --- |
| Caption | bottom left, left aligned |
| Title | top centre |
| Fullscreen text | centred, over a screen dimmed to 75% |

**The canvas is ours, not the game's.** This started on `HUD_DisplayName` — the dungeon-name text —
and that was wrong three ways: it forces `<uppercase>` on whatever it is given, it has exactly two
positions (`BottomRight` and `Centre`) and neither is where a caption belongs, and it is a single
line, so a title and its subtext could not be two sizes. Owning the canvas makes all three layout
rather than obstacles, and authored text now appears exactly as typed.

- The font is the game's own **FiraSans SDF**, the face the intro's "a game by Massive Monster" is
  set in (`Intro Room 1/Canvas/Game by MM`). It is found among the font assets already in memory
  rather than loaded or shipped — the HUD uses it, so it is always there — via
  `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`, which reaches assets no live object happens to
  reference. If it cannot be found, whatever the game's own UI is set in stands in.
- The canvas is `ScreenSpaceOverlay` at sorting order 4000: above the HUD, below the editor's own
  panels, which are only up while the game is paused for editing anyway. It has no
  `GraphicRaycaster` and every element has `raycastTarget` off — this is scenery, and a raycaster
  would eat clicks meant for the game.
- A `CanvasScaler` at a fixed 1920×1080 reference means an authored font size means the same thing
  on every display.
- Fullscreen dims to 75%, not black, so the room stays readable behind the text. The dim fades
  with the text rather than snapping.
- Fades are **unscaled** throughout: a sequence often runs while the game is paused around a
  conversation, and a scaled fade would sit at zero alpha until it resumed.
- One overlay, reused. A second caption while the first is still up replaces it rather than
  stacking. A scene load takes it with it, which is right — text from the last room has no
  business in this one — so a missing overlay is rebuilt on the next call.
- Text does not block the sequence, the same way music does not. Use *Wait* for pacing.
- The true fullscreen quote screen (the Woolhaven intro) is a scene of its own,
  `QuoteScreenController` plus a transition into the `QuoteScreen` scene, with text from
  `QUOTE/<type>` localisation keys — using it mid-room would mean leaving the dungeon and coming
  back. This draws the same idea in place instead.

### Cutscenes

`MMTools.MMVideoPlayer` is how the game plays its own — the prefab, the fullscreen surface, the
skip prompt and the menu blocking are all vanilla, and *Play cutscene* uses them.

Vanilla cutscenes are `VideoClip`s compiled into `Resources` (`Intro`, `DLC_Intro`, `Trailer`,
`Update_Video`), and a `VideoClip` cannot be built at run time, so custom videos take Unity's other
route: `VideoSource.Url` pointed at the file. Drop an `.mp4` (or `.webm`, `.mov`, `.m4v`) into
`BepInEx/plugins/CultTweaker/CustomCutscenes` and it is a cutscene named after the file — there is
no config.json and no registration step, and the folder is re-read every time the picker opens.

`MMVideoPlayer.Play` starts the video the moment it is called, so a custom video cannot be set up
by calling it and correcting the source afterwards: the start on a source that does not exist
raises an error that ends the cutscene before the real one loads. `PlayFromFile` therefore repeats
Play's setup with the url in place from the start, including the statics the vanilla component's
own `Update` reads — without those the skip button does nothing and the end of the video is never
noticed. The sequence waits for the cutscene, with a 15-minute ceiling so a video that never
reports finishing cannot strand the run.

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
| `Tools/TriggerCameraActions.cs` | Camera offset/zoom/look-at, post-processing effects, cutscenes |
| `Tools/TriggerScreenText.cs` | The caption / title / fullscreen text overlay |
| `Npc/*.cs` | Custom NPC behaviour, dialogue schema and dialogue runner |

Anything protected by `MapEditorProtection` is never destroyed by the clear or delete tools — doors
in particular are `IslandPiece`s carrying the `RoomLockController`, so destroying one soft-locks the
room: it can never be completed or exited.
