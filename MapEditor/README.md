# CultTweaker Map Editor

Claude Fable's book of fun things to read about. (this is for claude to learn about the code base, for players or mod users please read the thunderstore or nexusmods description instead :>)



An in-game room editor for Cult of the Lamb. It captures, edits and rebuilds a dungeon room as a
**node blueprint** (`CustomNodeBlueprints/<name>.json`), and chains those rooms into a **level
blueprint** (`CustomLevelBlueprints/<name>.json`) that plays through the mod's own dungeon.

| Key               | Action                                                                                                                       |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `F4`            | Open / close the editor (Dungeon1 scenes; in the base, only once a hub or the base editor has been opened from the F7 panel) |
| `F5`            | Enter the test dungeon — or, with the editor open, reset the room                                                           |
| `F6`            | Hide / show the editor's UI while it stays paused (screenshots); on an open world map, flip between play view and editing    |
| `F7`            | CultTweaker panel (fleeces, player spines, mod info) — not part of the editor                                               |
| `WASD` / arrows | Pan the camera                                                                                                               |
| `Z` / `X`     | Zoom in / out                                                                                                                |
| Wheel             | Switch tool (or scroll the list under the cursor)                                                                            |
| `Ctrl+Z`        | Undo the last placement                                                                                                      |
| `Ctrl+S`        | Quicksave under the current name (both editors) — see below                                                                 |
| `Del`           | Delete the selection (tool-dependent)                                                                                        |

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
  - [Clear](#clear) · [Load Map](#load-map) · [Level](#level) · [Hubs](#hubs) ([build totem](#the-build-totem)) · [Base editor](#base-editor) · [Dungeon Builder](#dungeon-builder)
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

**Hiding the UI.** `F6` takes the chrome away without leaving the editor: the room stays frozen at
`timeScale 0` — enemies included — so a shot can be framed and taken with nothing of the editor in
it. The camera still pans while hidden, but no tool acts on a click, and the world gizmos (trigger
volumes, selection outlines) go with the panels, since they are objects in the scene rather than
canvas chrome. All that is left is a *Preview mode - F6 to show UI* line in the bottom left, on a
canvas of its own because the editor's own is switched off wholesale. Closing the editor restores
everything.

**Saving.** The save button opens the game's name dialog on the current name, so confirming it
saves and changing it saves under the new name — the room editor's Save Map and the world editor's
Save Map both work this way, and both are therefore also the rename. `Ctrl+S` is the same save with
no dialog, under the name already on the title bar. It refuses to quietly clobber: the first press
that would land on a file *this session did not write* only arms, turning the status bar orange with
"already exists - Ctrl+S again to overwrite"; the second press within five seconds writes. Once a
name has been written this session, `Ctrl+S` under it is a plain quicksave.

**Undo.** One stack for the whole editor. Tools push an entry as they place; `Ctrl+Z` walks the
single history regardless of which tool is active. Entries return false when the thing they would
undo has already gone (cleared, loaded over, destroyed by another tool) and the stack moves on.

**Closing asks about unsaved work.** `F4` on an edited room raises a red strip along the bottom —
*Save & close* / *Discard* / *Cancel* — the same question the world and dungeon editors ask, and the
same shape of answer. A strip rather than a dialog because the question is asked *about* the room: a
full-screen modal would hide the thing being decided. `F4` again dismisses the strip rather than
answering it. *Save & close* goes straight to the write, past the quicksave's overwrite arming —
saying "save under this name" *is* the confirmation arming asks for — and the editor only closes once
the write has landed, so a blocked or cancelled save leaves the work where it is.

The third button is **"Close anyway", not "Discard"**, and the distinction is the whole point. The
room *is* the live scene. Closing the editor only puts the panels away: every edit is still standing,
`F4` brings it all back, and playing the room plays the edited one. What goes unsaved is the file.
A button labelled "Discard" would promise an undo the editor cannot perform — there is no earlier
room to go back to, only the one on screen — so the status line says what actually happened instead.

Whether there *is* unsaved work is counted, not compared against the file. The world and dungeon
editors diff their JSON against what was last written, but a room's save is a multi-frame collection
that rewrites the blueprint from the live scene — running it to answer a question would be a save in
all but name. So `MapEditorHistory` raises `Changed` on every push and every undo that undid
something, which covers every placement and removal in the editor, and the tools that change the room
*without* an undo entry — transforms, doors, lighting, music, shapes, clears — call
`RuntimeMapEditor.MarkEdited()` themselves.

The baseline moves **only on a save and on a load** — the two moments the room genuinely matches a
file (a load leaves it matching exactly, however much it changed on the way there). Notably *not* on
open: the count starts clean, so opening the editor to look at a room and closing it again asks
nothing on its own, while re-baselining per open would mean the editor forgot — close an edited room
with "Close anyway", open it again, close it again, and it would go quietly even though the room
still holds work no file has.

**Widgets.** Built from scratch rather than cloned from the settings menu — those rows are authored
56px tall for a full-width panel and only exist once the player has opened the settings menu. The
dropdown is likewise hand-built: the game's `MMDropdown` opens through `UIMenuBase.ActiveMenus`, a
menu stack the editor is deliberately outside of.

**Pointer handling.** `PointerOverUi` tests the editor's own registered rects rather than calling
`EventSystem.IsPointerOverGameObject` — this game installs Rewired's pointer module, under which
that call reported true almost everywhere and silently rejected every world click. The wheel is
routed to the editor's own scroll views by hand for the same reason.

**Text entry.** The inline prompt reads `Input.inputString` directly. `MMButton` derives from Unity's
`Button`, so it handles `ISubmitHandler`, and this game's input module raises submit from the
interact key — typing an "e" into a name re-pressed whatever button was focused. What stops that is
locking `UINavigatorNew` and clearing the EventSystem's *selection* each frame, **not** disabling the
EventSystem: see the Structure tool's chapter for why that was the wrong lever twice over.

---

## Tools

### Select

Click an object to select it. Ctrl-drag clones it. A selection carries three drag nodes: a **yellow**
one at the centre that moves it, a **blue** one on the top-right corner that resizes it (hold Shift
to stretch a single axis), and a **purple** one on the top-left corner that shifts its depth.

**Depth is a drag, not a pair of buttons.** The purple node reads the pointer in *screen* space —
depth is the one axis a pointer cannot be projected onto, so the gesture is "how far up the screen
have you dragged" and nothing else. Up is away, the direction the old `Send Back (Z+)` button named,
at a tenth of a unit every ten pixels — the same step that button took, so a short drag is still a
nudge. It works from the Z captured on mouse-down rather than from the previous frame, so a drag that
wanders back over its own start lands exactly where it began.

**Picking happens in screen space**, and it has to, because "what is under this world point" stops
being the same question as "what is the pointer over" the moment anything leaves the ground. The
room's ground *is* the world XY plane and the camera is pitched 45° over it, so Z here is height and
not only sort order: raise an object and it rises up the screen. Two things stayed behind when it
did. A 2D collider has no Z at all, so it sits on the floor while its art climbs — the game's own
convention for a raised platform — and the renderer sweep compared a `z=0` world point against world
XY bounds. A lifted object could only be selected by clicking the empty floor where it used to be,
while its outline drew correctly around the art.

The outline was the honest one, so the hit test moved rather than the gizmo. Putting the box on the
ground instead would have dragged the grip and both corner nodes down to the object's feet, trading
one mismatch for four. The collider is still consulted first, for its precision, but only when the
object it found is *also* drawn under the cursor — for anything at ground level that is every time,
so ordinary picking is unchanged; for a lifted object it rejects the collider left behind on the
floor and the screen-space sweep answers instead. Bounds are projected corner by corner: a pitched
camera turns a world-aligned box into a skewed shape, and only the eight corners bound it.

**Ctrl works on the handles too**, and not doing so is what made Ctrl-drag look broken. The handles
sit *on* the object they belong to — the grip right in its middle — and they are UI blockers, so the
polled Ctrl path never saw a click that landed on one. On anything small there was no bare pixel left
to grab, and whatever sat under the grip could not be cloned at all. Now any of the three handles
starts a clone when Ctrl is down, and hands the gesture straight to the tool's own polled drag.

**The panel says what is selected**, because the gizmos move things by numbers that were nowhere on
screen. A portrait box at the top, then the name, position (with z), scale, and how many scripts
are on the object, how many more in its children, and how many renderers - enough to tell a prop
from a structure from a whole nested rig, and to notice when the click walked further up the
hierarchy than expected. Protected objects say so. There is no rotation row: the view is 2.5D and
the art is flat, so nothing here is ever turned - the tool refuses to rotate for the same reason,
and a row that always reads 0 is noise. The numbers follow a drag at 10Hz; they are one label, but a
label is a text mesh rebuild and the gizmos move every frame. With nothing selected the box says so
and the readout and *Deselect* go away rather than standing there empty.

**The readout names the thing, not the GameObject.** An `Internal` row sits under the Unity name,
carrying whatever the files call it: a map author reading `Building Bed(Clone)` cannot tell which
structure that is in a blueprint or in a `BuildingOverrides/` folder, and the internal name is the
one both of those are keyed by. Three tiers, first answer wins - `StructureTool.TryGetPlacedName`
(ours, and the only one that knows a custom structure's COTL_API name), then a `Structure`
component's `Brain.Data.Type` (every vanilla structure, including anything the player built), then
`RoomSnapshot.TryResolveKey` for the addressable key a save would write for a piece of scenery.
Nothing resolvable, no row. It is resolved **on selection, not in the refresh**: the refresh runs at
10Hz through a drag and the last tier walks the addressables catalog.

The tool also **refuses to pick a trigger**, on both picking paths: the trigger tool draws them,
lists them and edits their actions, and selecting an invisible volume here meant it could be
dragged, resized or deleted out from under that tool.

**The portrait is one camera render per change of selection** (`SelectionPreview`), never per
frame. It is deliberately *not* the thumbnail rig: `EnemyThumbnails` stages a **prefab** on a spare
layer and reads the pixels back into an atlas, which is right for a catalogue of hundreds and wrong
for one live object that must not be moved. This points a disabled orthographic camera at the
object where it stands, frames it on its own renderer bounds, and hands the RenderTexture straight
to a `RawImage` - no readback, no atlas. It re-renders when the mouse comes up after a drag, so a
gesture costs one render rather than sixty.

**The plate is a centred square, not a full-width strip.** The render texture is square and the
`RawImage` inside it always was, but a plate stretched the whole width of the panel around a square
picture reads as a stretched picture whatever the picture is doing. The row is still full width -
that is the only width a layout group gives a child it controls - and the plate sits centred inside
it at the portrait's own shape.

**The selection tint comes off for the shot.** The highlight is a cyan tint on the object *itself*,
not an overlay, so a portrait taken while it is on is a picture of a cyan object. Rather than reorder
the calls - the tint is also on during a drag and a flip, both of which re-render - it is lifted for
the length of one render and put straight back. Nothing observes the gap: no frame is drawn inside it.

**Ctrl-drag copies the selection, not whatever the pointer re-picks.** Picking takes the smallest
sprite under the cursor, and a room is full of grass, decals and splashes lying over the thing you
want — so a clone gesture that began on the selected object could quietly copy something else. From a
handle the answer needs no working out at all, since the gizmo belongs to the selection; from bare
space the selection still wins whenever the pointer is over it, and only otherwise does the tool pick
afresh. Ctrl-drag on a selection is not an invitation to re-choose.

**Clones are numbered.** Unity names a copy `<name>(Clone)`, and a copy of a copy
`<name>(Clone)(Clone)` — a suffix that grows without bound, gets written into the saved blueprint,
and says nothing except how many times somebody pressed Ctrl. The tool strips it, strips any trailing
number too (so a clone of `Torch 4` is `Torch 5`, not `Torch 4 2`, and an old `(Clone)(Clone)` name
lands back on its real stem), and counts up from the copy's own siblings — names only have to tell
things apart where they sit together, and a room-wide sweep per Ctrl-drag would cost more than the
answer is worth.

**The tint comes off for a clone, too, and for the same reason as the portrait.** `Instantiate` copies renderer colours
like any other property, so a clone taken from a selected object was born wearing the highlight -
and `Select` then recorded those tinted colours as the clone's *originals* and tinted on top. Cloning
a clone compounded it: each generation came out darker than the last, and none of it washed out on
deselect, because by then the tint *was* the object's colour. A tint that lives on the object rather
than over it has to be lifted every time the object is read, and cloning is a read.

**The portrait borrows the room camera's rotation**, and that is what was actually squashing it -
the square plate and the square `RawImage` were both fixes to the wrong thing. The game's camera is
**pitched**: `CameraFollowTarget` settles it on `Euler(-45, 0, 0)` and the room's art stands up to
meet it (`BillboardFacingCamera` and friends), so a sprite that looks upright on screen is a quad
tilted 45 degrees in the world. A camera looking straight down `-Z` at that quad sees it edge-on and
draws it at `cos(45)` - about 71% - of its height. Nothing in the UI was ever stretched; the picture
was taken from the wrong angle. The portrait is not a different projection of the room, it is the
same view from closer, so it takes the room camera's rotation and measures the subject's extent in
*that* camera's space - with a pitched camera, world height and screen height are not the same
quantity.

Two of the thumbnail rig's tricks are borrowed, and both are *undone in the same call* because this
subject is alive rather than a staged throwaway. The object's layers are swapped to a spare one, so
the neighbours stay out of the portrait and the background comes out transparent. And its renderers
are swapped onto unlit copies of their own materials (`EnemyThumbnails.MakeUnlit`, which now takes
an optional restore list for exactly this) - a dungeon is dark, and a portrait lit by the room it
stands in is a black square. Nothing else ever observes either change: no physics step, no cull, no
frame.

**Lights are left out of the portrait**, and this is the price of the unlit swap: a light draws a
mask of *brightness* rather than a picture - a soft white blob that lightens what it covers - and
`Sprites/Default` is alpha-blended, so swapping it turned every glow into a solid disc over the
subject. They come out *before* the framing is measured, too: a halo is usually far bigger than the
thing it lights, and leaving it in shrank the subject to fit its own glow.

Finding them by **blend factor does not work**, which cost a round trip worth recording. The game
blends through the third-party **BlendModes** package, and its extensions (`DappleLighting`,
`Godrays`, `DecalSprite`) declare `_SrcBlend = One, _DstBlend = Zero` - which reads as *opaque* -
then do the real blending themselves. So the factors say "solid" for precisely the things that are
not, which is also why they came out as solid discs. The reliable signal is the package's own
components (any type in the `BlendModes` namespace, or a `*_BlendModeExtension`), with the shader
families those classes name as a second net, and particle renderers excluded outright - a particle
system frozen on whatever frame the room left it on is not a portrait of anything. The game's own
stencil lighting is a third: an `IStencilLighting` component owns a *child* mesh renderer holding
the light's decal quad, which is another brightness mask - and one drawn against a stencil buffer
that nothing fills in a single-camera render.

**And the smear that survived all of that was not a light at all - it was the subject twice.** Every
lit sprite in the game grows a hidden twin: `StencilLighting_ExcludeSprite.Start` builds a child
called `ExclusionRenderer` carrying a copy of the same sprite on the `Lighting_NoRender` layer, and
the only thing keeping it out of the frame is the main camera's culling mask. `Restage` swept *every*
child onto the portrait layer, which promoted that twin into view wearing the exclusion material,
drawn over the real one. So the portrait now takes the room camera's `cullingMask` as its rule:
a renderer on a layer the player's camera does not draw is not photographed either.

**Right-click deselects**, from anywhere in the room rather than from one spot in the panel — so the
*Deselect* button is gone. It also had to appear and disappear with the selection, which moved
everything under it every time something was picked. Polled rather than taken from the EventSystem,
like every other right-click in the editor: this game installs Rewired's pointer module, which drops
the right button. It is skipped over the editor's own panels, since a right-click there is aimed at
the panel and dropping the selection out from under the controls being used on it would be a
surprise.

**Flip is a checkbox, not a button.** Flipped is a state of the thing, and a button gave no way to
see whether what you were looking at was flipped already. It *sets* the sign rather than negating
it, so the box and the object cannot drift apart. There is no delete button - `Del` does it, and
the button sat one slip away from the controls above it.

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

Spawns and edits `SpriteShapeController` terrain, and keeps its collision in sync. Ctrl-click adds a
spline point, drag to move one, right-click to delete it; pick a profile, toggle per-shape collision,
show the baked collision outline.

**Z does not layer sprite shapes**, which is why the depth buttons went and no purple node replaced
them. The game stacks shapes with the renderer's **sorting layer and order in layer**, and leaves Z
at a rounding nudge: `GenerateRoom.CreateSpriteShape` builds every room shape at `z = 0.0001` and
then sets `sortingLayerName = "Ground"` and `sortingOrder = -1`, and where the game needs to know
which shape is on top at a point it compares `spriteShapeRenderer.sortingLayerID`. Unity draws in
that order too — sorting layer, then order in layer, and only then camera distance — so a Z nudge
reaches the weakest mechanism available, and every shape the tool makes is a clone of one template
sharing one layer and one order. Hence "moving it in Z barely did anything".

**The shape list is a list, not a dropdown**, and it does the work of three controls: click a row to
edit that shape, `-`/`+` to move it behind or in front of its neighbours, `X` to delete it. A
dropdown could only answer the first, and the buttons beside it acted on "whatever is selected" — so
you had to read the dropdown to learn what they were about to do. `-`/`+` move **one shape one step**
rather than renumbering the list: the room's own generated shapes are in there on orders the biome
chose, and tidying the numbering would restack terrain the author never touched. Order is saved as a
**nullable** `SortingOrder`, so a map written before this says nothing rather than saying zero and
keeps the order it inherits from the template — which is what those maps have always looked like.

The list and its *New Shape* button sit at the **bottom** of the panel, boxed in a scroll view.
Everything above them acts on the shape the list picked, so reading top-down as "settings, then the
things they apply to" was backwards — and a list that grows is the one thing on the panel that should
not be shoving the fixed controls around.

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
- **The catalog is not all under `Assets/Prefabs/`.** The DLC dungeons' dressing lives in
  `Assets/Resources_moved/Dungeon/Decoration Plots/Mountain` (Ewefall) and `.../Prison` (the Rot
  dungeon); each has whole plots straight under the biome folder (`Prison_2x2_Cages`) and the
  pieces one folder down (`Decoration Prefabs/`, with Ewefall's split further into deadForest,
  dearForest, godCemetry, mistyForest). `GroupOfKey` lists them as `DLC Dungeon / Ewefall
  (Mountain)` and `... Plots`, and likewise for Rot. `Assets/Art/` carries the cave biome pieces,
  the base weeds and some boss/shop room dressing (listed as `Art / <folder> / <folder>`), and
  `Assets/Tile Decorations/` the destructible tiles. Deliberately not listed: `Assets/src/...
  /Island Pieces` (1643 whole encounter layouts, the room generator's raw material, not props),
  `Assets/_Rooms` (whole rooms; the NPC tool reads those) and the rest of `Resources_moved` (FX,
  UI, units). Spawning and icons need nothing special: `ObjectPool.Spawn` treats any key as
  addressable, and the snapshot resolves a pooled object through `loadedAddressables` whatever
  the key's prefix. The pre-existing `Placement Objects / DLC` and `VFX / DLC` folders are shown
  as `... / Misc` so "DLC" in the picker means the dungeons and nothing else.

**The cells scroll in a box of their own** (`CreateIconGrid(scrollHeight:)`), so the search field
and the group picker above them never leave the screen. A catalogue of hundreds used to grow the
tool's own column past the panel, and reaching the search meant scrolling back up through
everything you had just scrolled down through. The hovered-name caption stays pinned under the box.
Short lists (Load Map) keep the old grow-to-fit behaviour. Wheel handling needed nothing: the
editor's `ScrollUiUnderPointer` walks scroll rects back to front, so the inner box wins over the
column it sits in.

The `scrollHeight` is a **ceiling, not a size**: the box is as tall as its rows need and no taller,
so a group of three icons does not sit in a pane of empty black. The height is worked out from the
cell count rather than measured, because cells fill in over several frames and a rect that has not
had a layout pass yet measures zero. `OptionsMaxHeight` went up to 940 to clear a full box plus the
controls around it - at 820 the column those sit in started scrolling, which is the one thing boxing
the cells was meant to prevent.

**Setting the `LayoutElement` is not enough to make the box shrink**, and this one took three goes.
The editor answers `RequestOptionsResize` with `ForceRebuildLayoutImmediate` on the tool's option
column - and that walk never reaches inside the box. `LayoutRebuilder` descends into a child only if
the rect it is standing on carries a layout controller of its own, and a `ScrollRect`'s **Viewport
carries nothing but a `RectMask2D`**. The walk stops dead there, so the grid of cells underneath,
with its own `ContentSizeFitter`, is never re-measured from above; it gets there on its own dirty
flags eventually, but by then the box had already been sized against the stale measurement and
nothing came back to correct it. A box that had been tall for a big group stayed tall for the small
group after it. So `MapEditorGrid` rebuilds the inner column **by name**, two frames after the target
height changes - late enough that the previous group's cells are actually gone (`Destroy` defers to
end of frame) and the staggered fill has stopped adding to this one. It also puts the box back to
the top on `Clear`, or the handful of icons a short list does have sit above the viewport and have to
be scrolled back up to.

**The scrollbars are a hairline red rail** rather than the old fourteen-pixel grey slab, which was
eating into the last column of icons: two pixels wide, the editor's accent for the handle, and a
plain rectangle - rounding a rail that thin only makes it look chewed. The viewport reserves exactly
the rail's width plus two, so the cells get the rest, and `ScrollbarVisibility.AutoHide` takes the
rail away entirely when the content fits. `AutoHide` rather than `AutoHideAndExpandViewport`,
because the viewport's inset is set by hand and that mode would fight it for the two pixels.

Shrinking the box needed more than setting its `LayoutElement`: a box that had been tall for a big
group stayed tall for the small one after it, because the fitters above it had already settled at
the larger size and nothing on the way down told them to look again. The rect is now resized
directly and its parent layout marked dirty by hand, alongside the panel resize request.

**Search by name** (`MapEditorSearchRow`, shared with the Enemy and NPC tools). The catalog runs to
thousands of prefabs across dozens of folders, and what you are after is nearly always known by
name, so the top of the panel is a search field. It matches **every** group at once - the catalog is
filed by Addressables folder and the same word turns up in several of them - and shows the first 60
hits with the total in the status bar. `Enter` leaves
typing with the filter applied, `Escape` clears it and puts the group view back, and **clicking a
result ends the search too** (`RuntimeMapEditor.ConfirmPrompt`, the same path the key takes) - so
what you searched for can be placed straight away instead of being confirmed and then clicked.

**Hovering a cell blows its icon up beside the panel.** A cell is sixty-odd pixels of a prop that
may be a hundred times that on the ground, so half the catalog reads as the same brown smudge. The
big preview is drawn from the sprite the cell already holds - so it costs a texture draw and nothing
else - and it needs no click, unlike the ghost that appears in the room once something is selected.
It lives on `MapEditorUI`, so every icon grid gets it: structures, props, enemies, NPCs and the
saved-map screenshots.

Text entry in this game is the hard part, and three things had to be true before it worked:

- **The E key was firing buttons.** `MMButton.OnPointerEnter` hands the hovered button to
  `UINavigatorNew` as its current selectable, and that navigator polls Rewired's accept binding
  from its own `Update` and confirms the selectable directly - it never touches the EventSystem, so
  suspending the EventSystem did nothing about it. Worse, `OnPointerExit` never clears the
  selectable, so the last button the cursor passed over stayed armed indefinitely and `E` pressed
  anywhere fired it. Every editor button is now built with `PreventMouseSelection = true`, so the
  navigator is never handed one: clicks are unaffected (`OnPointerClick` does not consult it) and
  the hover state it skips is Unity's, which the editor does not use.
- **The navigator is locked while typing.** `PreventMouseSelection` only covers *our* buttons; a
  game button that was already the navigator's selectable would still answer `E`, and the cancel
  binding would still fire. `UINavigatorNew.LockInput` is the switch the game itself uses to gate
  that whole block. It is only ever cleared by the editor if the editor set it - a stranded lock
  takes every menu in the game down until a restart - and it is cleared defensively on teardown
  alongside the modal-state reset.
- **The EventSystem stays on.** Suspending it was the original guess at the E key, it never fixed
  that, and it cost twice over. It never came back: `EventSystem.current` is the first *enabled*
  EventSystem in the scene, so switching off the only one makes `current` null - and the restore
  path asked for `current` again, found nothing, and switched nothing back on. The panel stayed
  dead until the scene changed, which is what "the search never gives control back" was. And a
  search field wants its grid **live** underneath it: results are there to be hovered for a big
  preview and clicked to pick one, and neither reached the panel while the EventSystem was off.
  What the submit key actually needs is a *target*, so the prompt clears
  `EventSystem.currentSelectedGameObject` every frame instead - a key press has nothing to land
  on, and mouse clicks, which never needed a selection, keep working.

With those three, `RuntimeMapEditor.PromptText` is a usable field: it reads `Input.inputString`
(characters and backspace, so no caret, selection, paste or IME), and `Update` returns immediately
after reading the keystroke, so no editor shortcut, camera key or wheel tool-switch sees it. It
takes an `onChanged` callback for filtering as you type and can draw somewhere other than the title
bar.

Repopulating is **debounced through a coroutine** rather than the tool's `OnUpdate`, which does not
run while a prompt is open. Without the delay every letter would start and cancel a screenful of
async icon loads.

### Enemy

Spawns enemies, vanilla and custom, from a thumbnailed grid grouped by catalog folder, with the
shared **search field** (`MapEditorSearchRow`) above it. Search covers every group at once plus
whatever mods have registered - the catalog is filed by biome, and the enemy you want is rarely in
the biome you happen to be standing in. Hovering a cell blows its thumbnail up beside the panel.

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
room prefabs, and mod-registered custom NPCs. The shared **search field** sits above the grid, with
one honest limit: NPCs are found by opening room prefabs, which is slow enough to be indexed on
disk, so a search covers **what has been scanned so far** plus the custom list rather than
pretending to have looked everywhere. Open a group once and its characters are searchable from then
on, in this session and the next - the index is saved. An empty result says which it was.

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
centre to move or its blue corner node to resize. *Fire once*, *Lock control while playing*, *Show
volumes in play*, Re-arm All. Volumes are drawn while the tool is open, and optionally during play.

**Ctrl-drag copies a trigger whole** — size, *Fire once*, *Lock control* and the entire action
sequence. Rebuilding a ten-step sequence by hand to put the same thing in two doorways was the
slowest job in the tool. The actions are round-tripped through their *saved* form rather than copied
field by field: that is the shape they are already written and read in, so a copy cannot quietly
share a reference with its source, and an action type added later cannot be forgotten here. An action
that pointed at its own trigger is retargeted to the copy's; one naming a *different* trigger is left
alone, because that is a reference to elsewhere in the room and the copy means it just as much. Ctrl
on a handle clones too — the handles cover the volume they belong to — and the selection wins as the
source whenever the pointer is inside it, so a copy started on the trigger you are working on cannot
grab the one stacked underneath.

**No Delete Selected, and no Clear All.** `Del` already deletes, and it is the shortcut the panel's
own list advertises. Wiping every trigger in the room is a *clearing* job, so it moved to the Clear
tool to sit with the others rather than at the bottom of the panel used to author one trigger at a
time — arming and all.

There are **no width/height sliders, and no readout put in their place**. The corner node sizes the
volume on the volume itself, which is the thing being sized; two sliders that had to be found in the
panel, dragged, and checked against a shape somewhere else on screen were a slower way of making the
same edit. The selected trigger's line already carries its size.

**A volume is coloured by what it does.** Cyan is fire-once — the default and the common case — and
**violet** is a trigger that re-arms on every entry, so a room full of boxes says which of them will
go off again without clicking through them one at a time. The distinction shows only while a volume
is idle: selected (yellow), firing (green) and spent (grey) are worth seeing whatever kind of trigger
they belong to, and a repeating trigger is never spent anyway. Toggling *Fire once* re-tints the
volume immediately — the colour is a reading of the toggle, so it cannot lag behind it.

*Clear All Triggers* — now in the **Clear** tool — **asks twice.** The first press arms the button:
it renames itself to `Delete all N? Click again` and warns in the status bar, and a second press
within 4 seconds does the wipe. The window lapsing, or leaving the tool, puts the label back. Two
presses rather than a dialog because the panel has no modal of its own, and the wipe cannot be taken
back. It is the only armed button on that panel, and that is worth being honest about: the sweeps
beside it are just as destructive and have never asked. Arming them too is a bigger decision than
moving a button, so it was left alone.

**The action list scrolls in a box of its own** (`MapEditorUI.CreateScrollBox`), so a long sequence
cannot push the controls above it off the panel. The box is as tall as its rows need up to a ceiling,
and its height is worked out from the row *count* rather than measured — a rebuild destroys its old
rows with `Object.Destroy`, which defers to end of frame, so anything measured in the same breath
measures the rows on their way out as well as the ones replacing them. It also returns to the top on
every rebuild, or a shorter list leaves its rows above the viewport. The Shape tool's list uses the
same widget.

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

**There is no Capture Biome Lighting button, because opening the tool already does it.** `OnEnter`
captures the live biome into an untouched blueprint, so the sliders open showing what the room is
actually doing, and the first slider moved flips the map from following the biome to overriding it.
The button's only unique effect was that flip *without* a value change — "pin this exact biome look" —
which a nudge of any slider gives. The state note went with it: it said "Following the biome" or
"Overriding the biome" for a fact the sliders and the room in front of you already show.

**Reset To Biome puts the sliders back too**, which it used to skip. Left where the override had
them, the next slider touched would snap the room straight back to the look just discarded, and the
panel would meanwhile be describing a room that no longer looked like that. The values come from the
snapshot taken *before* anything overrode the lighting, not from the manager: `ClearOverride` fades
back over several seconds, so reading the manager at that moment would catch a frame from the middle
of the fade and pin the knobs to a colour the room is only passing through.

**Notes**

- Applying a profile copies its values onto the map (and the blueprint saves them as its own), so a
  profile deleted later does not hollow out maps that used it. Only a trigger's *Apply lighting*
  action references a profile by name at run time — a blueprint shared to a machine without that
  profile logs a warning and skips the action.
- Saving a profile while still following the biome captures what is currently on screen first, so
  the profile holds a real look rather than defaults.
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
- The override is global state on `LightingManager`, and a room does not carry it — so walking
  through a door neither removes nor restores anything on its own. Each room's lighting is
  therefore remembered against the room itself (`BiomeRoom`, the same identity level playback keys
  its slots off, and stable across revisits) and asserted again whenever that room is generated.
  Without this, the room you left went on lighting the room you walked into, and the room you came
  back to had lost its own. The table is dropped when a new biome starts and when a level run ends.
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

**It is a screen, not a panel**, and that follows from what a snapshot is. A room snapshot is a
picture of a whole room, and the reason to show one at all is that a room is *recognised* faster
than its name is read — but two of them at 168px in a side panel gave that up: at that size every
dungeon room is the same brown smudge, so the name did the work and the picture was decoration. Full
screen the pictures are large enough to pick from, each card carrying the room's name and when it
was last written underneath. Picking the tool opens the browser directly (a panel holding one button
is a door, and a door needs no handle); `Esc`, the corner button and `F4` all close it, `F4` via the
same `IMapEditorScreenTool.ScreenStepBack` the dungeon map uses, so it closes the browser before it
closes the editor.

**Sorted newest first**, from the blueprint file's own write time — the blueprint records no date and
the file system already knows. Working on a room usually means working on the one last saved, so the
list needs no reading most of the time. Dates within a day read as a clock time, so the ones you are
actually iterating on are legible at a glance rather than being four identical timestamps.

**Nothing is parsed to list it**, and that was the second stutter on opening. The browser needs a
name and a date; a blueprint carries every shape, prop, structure and enemy in its room. Reading the
folder used to mean deserialising all of that — every saved room, on the main thread, before the
first card appeared. But `Save` writes to `PathFor(MapName)` after sanitising it, so **the file name
is the map name**, and the write time is on the file: the listing is now a directory read, and the
one blueprint that gets parsed is the one that gets clicked (`MapEditorSerialization.SavedNames`
offers the same listing to anything else that only wants names — the Level tool's *Add To Pool*
picker was doing the identical thing on every selection change).

What is left of the scan — the directory walk, the write times, and the level blueprints the hub
filter has to read — runs on a worker thread through `Task.Run`, the way `SaveAsync` already writes.
The coroutine holds a "Reading saved rooms..." note until it lands. Nothing on that thread touches a
Unity API; `ModContentPaths` is `System.IO` and a static path, and its bridge list is warmed on the
main thread first so the lazy fill is not a race.

**Cards are built six to a frame** with a count in the status bar, on the model of the NPC tool's
room scan. Each card is a plate, two labels and a picture frame, so a folder of forty in one frame
was a visible lurch even once the parsing was gone.

**Hubs are filtered by sanitised name.** `HubSession` stores the blueprint name as the author typed
it and `LoadByName` sanitises before going to disk, so comparing sanitised names compares the two
things the loader itself would — where the old comparison against the blueprint's own `MapName`
field would have missed a name that needed sanitising.

**Notes**

- Screenshots are written at **1280 wide** (height follows the screen's aspect; a narrower screen
  is written as it is, since the downscale never enlarges). 512 was enough for a grid cell and not
  enough for the hover preview, which was already upscaling it on a high-resolution screen. The
  cost is roughly six times the pixels: about 3.7MB of texture per preview once decoded, against
  600KB before. They are megabytes of texture each, so they are read as cells appear and dropped
  when the panel closes.
- **A snapshot on disk keeps the width it was taken at.** Saving a map again is what re-takes it, so
  blueprints written before this keep their old screenshot until they are next saved.
- **They are decoded off the main thread.** A room snapshot is a full-screen png and
  `Texture2D.LoadImage` is a blocking decode of several milliseconds; a few in a row was the stutter
  when the panel opened. `UnityWebRequestTexture` against a `file://` url decodes on a worker thread
  and hands back a finished texture, so the frame only pays for the upload - the same shape as the
  async icon loads the structure and enemy browsers use. They are requested `nonReadable`, since
  nothing reads the pixels back and a readable copy doubles the memory of the heaviest thing in the
  editor. A token is bumped whenever the list is rebuilt or the tool exits, so a snapshot still
  decoding for a grid that no longer exists throws its texture away instead of filling a dead
  cell.
- **Three at a time, not all at once.** The decode is off the main thread but the *upload* is not,
  and forty of those landing within a few frames is a stutter of its own. Three worker coroutines
  pull from one cursor over the list, so the pipe stays busy without uploads piling up — and since
  the list is newest-first, the pictures worth waiting for arrive first. They start only once every
  card is standing, so the pictures fill in against a list that has stopped moving.
- A manual load cancels any running level: a stale run advancing on the next door would teleport
  the player into an unrelated room chain.

### Level

Authors `CTLevelBlueprint`s — the rooms a custom level generates from. Create or open a level, set how
many rooms it has, and pick which node blueprints each room may generate from. **Play Level** resolves
them, re-enters the dungeon scene and loads the entrance room; doors then advance through the level.

A level can either leave the floor's shape to the game (the original behaviour, described here) or
state it outright — see **Authored layouts** below.

**Notes**

- `Rooms[0]` is always the Entrance and `Rooms[^1]` always the Exit; added rooms go between them
  and neither end can be removed.
- **Everything picked is a dropdown**, the same widget the other tools use: select the room being
  edited, set its modifier, add to its pool. The panel used to be a stack of buttons that grew by one
  for every level and every map ever saved.
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
- **`NumberOfRooms` is not a room count.** `CreateRandomWalk` seeds a room *before* its loop and then
  adds `NumberOfRooms` more, and `PlaceEntranceAndExit` appends the end-of-floor room on top — so a
  floor arrives at `NumberOfRooms + 2`, and handing it the level's room count straight through made a
  two-room level generate four. `CTLevelDungeon.NumRooms` subtracts the two, which are the entrance
  and end-of-floor rooms the level now carries at its ends. It cannot go below one:
  `PlaceEntranceAndExit` picks the entrance and exit from rooms with exactly one connection, and a
  lone room has none, so it dereferences null. Authored layouts are unaffected; the walk never runs
  for them.
- The room hook fires *inside* vanilla generation while the transition still covers the screen, so
  the apply routine holds that cover (`MMTransition.CanResume`), swaps the blueprint in behind it,
  and only then resumes — the player never sees the vanilla room.
- Slot mapping *without* a layout: the first generated room takes Entrance, the room owning the exit
  (NextLayer) door takes Exit, and others consume middle slots in discovery order. Revisited rooms
  regenerate vanilla content, so their remembered slot re-applies.
- All playback state is static: scene reloads destroy the editor host, and each fresh host re-binds
  via `OnEditorReady`.

#### Authored layouts

A level can also say the shape of the floor itself — a cell per room and a door per side — instead of
being dealt onto whatever the random walk produced. It is a toggle on the layout screen, **Random
walk**: cleared, the floor is exactly the grid; ticked, the level is a sequence again and the doors
are ignored. New levels start laid out; blueprints written before layouts existed load with it ticked
and play exactly as they always did.

Under a random walk nothing about the grid has to be wired — there is no shape to wire. The rooms
still stand on cells, because that is how they are seen and picked, but they play in the order they
are numbered. Ctrl+click adds a room *before* the last one.

**The first and last rooms are the game's, and the level owns them.** A random walk always builds
two rooms of its own: `PlaceEntranceAndExit` makes the room the player arrives in the
`EntranceRoomPath` prefab — the weapon podiums — and appends the `EndOfFloorRoomPath` room with the
way out. They were always on the floor; the level just did not know about them, so its first
blueprint was being loaded over the podiums and its last over the exit platform. Now they are the
first and last entries in the level: drawn in their own colours, carrying no pool, and refusing to be
deleted, because the floor has them whether the author wants them or not.

That is also what makes the count on screen the floor the player walks — `CTLevelDungeon.NumRooms`
subtracts exactly those two before handing the rest to the walk. A level always keeps at least one
room of its own between the ends, since the walk cannot be asked for fewer than two rooms
(`PlaceEntranceAndExit` reads rooms with exactly one connection, and a lone room has none).

Migrating a blueprint written before this **inserts** the two ends rather than taking over the rooms
already at those positions — those carry pools the author chose, and repurposing them would throw two
of them away. So an old two-room level becomes four: entrance, its two rooms, exit.

**`StartWithBossRoomDoor` is switched off for every custom dungeon.** It is vanilla's "see the boss
you are walking towards" room, and it does far more than decorate: `PlaceEntranceAndExit` appends a
whole extra room for it at `(-999, -999)` and then points `StartX`/`StartY` at *that* instead of the
entrance. The floor comes out a room longer than anything asked for, and the room the player arrives
in is no longer the entrance — so the weapon-podium room fell to second place, and second place gets
dealt a blueprint. It fires on `GameManager.CurrentDungeonFloor == 1`, which is why it only showed up
sometimes. Nothing in a custom dungeon sets up the boss progression it announces, so it is turned off
rather than accounted for; an authored layout never reached it anyway.

A one-line check at the first room logs the floor's real size against the level's, so a mismatch
names itself instead of having to be counted out of the per-room lines.

**What vanilla gives us.** `BiomeGenerator.CreateRandomWalk` drops a room at the origin and then, for
`NumberOfRooms` iterations, picks a random room already placed and a random one of four directions,
adding a neighbour wherever the cell is free. `PlaceEntranceAndExit` then flood-fills to choose the
two furthest-apart dead ends. Neither is consulted when `OverrideRandomWalk` is set — and that flag is
not a back door: `MapManager.EnterNode` sets it for every dungeon-map node that is not a random floor,
and the DLC intro dungeon runs on it. It is checked as an early return in `PlaceEntranceAndExit`,
`GetCriticalPath`, `PlaceLockAndKey`, `PlaceStoryRooms`, `PlaceDynamicCustomRooms` and
`PlaceFixedCustomRooms`, so raising it stands the whole procedural layer down.

**What we do with it** (`LevelLayout`): a Harmony prefix on `CreateRandomWalk` builds the `BiomeRoom`
graph from the authored grid, raises the flag, and skips the original. Generation then carries on
completely unchanged on top of it — room shape and doors follow from the connection types alone, since
`GenerateRoom.Generate(seed, N, E, S, W)` walks `CreatePaths` and `PlaceDoors` off them.

**Notes**

- `CreateRandomWalk` is the hook rather than `CustomDungeon.OnBiomeReady`, because a floor entered
  from a dungeon map is regenerated in place (`MapManager.EnterNode` → `Regenerate`), which re-runs
  `GenerateRoutine` without the biome ever being enabled a second time. `CreateRandomWalk` is the
  first thing `GenerateRoutine` does on both paths.
- Vanilla's own `OverrideRooms` list is deliberately **not** used. That path marks every room custom
  and loads a prefab for it by Addressable path — right for the fixed prefab rooms it was built for,
  wrong here, where an authored level wants ordinary generated rooms with islands and doors that a
  blueprint is then pasted onto. Those are what `InstantiatePrefabs` makes for anything left
  `IsCustom == false`.
- `RoomEntrance`, `RoomExit`, `lastRoom`, `StartX` and `StartY` are set by hand, because
  `PlaceEntranceAndExit` is what normally sets them and `Door`/`Interaction_BiomeDoor` dereference
  `RoomEntrance` without checking it.
- The minimap hides itself whenever `OverrideRandomWalk` is up — right for what vanilla uses the flag
  for, a node whose whole floor is one room. A prefix/postfix pair lowers the flag for the length of
  `MiniMap.OnBiomeGenerated` and raises it again straight after.
- **Doors follow from the arrangement.** Placing or dragging a room opens a door to everything it now
  touches (`LevelLayout.WireRoom`) and walls off what it has parted from; the side dropdowns are there
  to close one again, for a spiral or a room you walk past rather than through. Only the room being
  moved is rewired, so a door deliberately closed elsewhere survives dragging a third room about.
- **The two end doors are derived, not placed.** The way in goes on the first room in the level and
  the way out on the last, on a side facing open grid — south and north for preference, which is
  vanilla's own. **Start Here** / **Way Out Here** move the selected room to the front or back of the
  level, which is how you choose which rooms those are. Leaving them to be set by hand meant a level
  could be dragged into a shape with two ways in, or none, and only find out at the badge.
- A side facing another room is a wall or a door; a side facing open grid is a wall or one of the two
  end doors. `LevelLayout.Normalize` re-applies all of that after every edit.
- **A door is shared, so it is written to both rooms at once** (`LevelLayout.SetShared`). `Normalize`
  settles disagreements with *"if either side says door, both do"*, which is what lets a room dragged
  next to another open the door from one side only — and the reason closing one from a single side
  silently never took: the neighbour was still saying door, so the same pass put it back. Every
  deliberate door change goes through `SetShared` so the two rooms agree before `Normalize` is asked
  anything.
- Sides are **Up / Down / Left / Right** on screen. The compass is the biome's own vocabulary and
  stays in the data — north is +Y, east is +X — but on a grid drawn face-on it only confused the
  issue.
- Room binding stops guessing: `LevelPlayback` looks the slot up by cell, so the blueprint in room 4
  plays in room 4 rather than in the fourth room the player happened to walk into.
- The layout is validated before the scene loads (`LevelPlayback.Resolve`), not at build time — inside
  the generation coroutine the only way to report a problem is a log line and a floor that quietly is
  not the one that was authored. If the build fails anyway it falls back to the vanilla walk, which is
  a worse level than the one authored and an enormously better one than a black room.
- Blueprints without a layout keep the old behaviour exactly: `AuthoredLayout` deserialises false, and
  every path above is gated on it.

#### The layout screen

The dungeon builder's arrangement one level down — full screen, options floating right, dock along the
bottom, status bar above it. The dock panel is the list of levels and nothing else; picking one goes
straight to the screen, so there is no second set of controls and no half-open state to be in.

| Gesture            | Does                                                     |
| ------------------ | -------------------------------------------------------- |
| Ctrl + left click  | Add a room in that cell, doored to everything it touches |
| Left click         | Select a room; drag to move it                           |
| Right click a room | Open or close the door between it and the selection      |
| Del                | Delete the selected room                                 |
| Ctrl+S             | Quicksave · Ctrl+Z undo · Esc close                    |

- Green edge = the way in, gold = the way out, pale = a door between rooms.
- **Room kind** picks between a generated room and one of the game's own finished ones: the
  **vanilla entrance**, which is the room with the weapon podiums a run starts in, and the **vanilla
  exit**, the end-of-floor room. Both are drawn in their own colour on the grid.
  - These are `BiomeGenerator.EntranceRoomPath` and `EndOfFloorRoomPath` — *per-biome* fields, so
    "the podium room" resolves to the right room for whichever dungeon the level is played in rather
    than to a path baked into the blueprint. `GameManager.Layer2` swaps in the `_P2` variant, the
    same way vanilla does.
  - They are built the way `PlaceEntranceAndExit` builds them: `IsCustom` with a `GameObjectPath`,
    and `Generated` left false so the prefab's own `GenerateRoom` still lays the island and doors
    around what the prefab brought with it.
  - A prefab room's pool is ignored and nothing is loaded over it — `LevelPlayback.Resolve` returns
    `<vanilla>` for it, because pasting a blueprint on would clear exactly what it was picked for.
  - Where they stand is only an advisory, not a rule: the podiums gate on
    `GameManager.CurrentDungeonFloor`, not on the room being the entrance, so a podium room works
    anywhere. It does mean **the podiums only appear on the first floor of a run**.
- The badge reads **Playable**, or the first thing wrong with the grid; rooms a blocking issue names
  get a red ring. Unreachable rooms are the failure the grid exists to make visible — the level runs,
  and part of it is simply never seen.
- Setting a side to *way in* clears any other, since the player can only arrive through one door.
- Undo snapshots the whole grid rather than inverting each edit: every edit renormalises the rooms
  around it, so an inverse would have to carry the neighbours' sides too — a snapshot with extra
  steps.

### Hubs

A **hub** is a safe town room built in the game's own DLC town — Woolhaven, emptied.

**Woolhaven is not a scene.** It is a `GenerateRoom` called `DLC_ShrineRoom` sitting inside
`Base Biome 1`, switched on by `BiomeBaseManager.ActivateDLCShrineRoom()`, which switches the base
room and the church off at the same time. Enabling a `GenerateRoom` makes it `GenerateRoom.Instance`
— and `GenerateRoom.Instance` is what every editor tool, the blueprint loader and the clear sweeps
work on. That one fact is the whole feature: once the town room is up, the F4 editor edits it with
nothing changed.

Hubs live in the **F7 panel**, not the Level tool, because the editor can only be reached once the
room it edits is standing:

| Panel control       | What happens                                                                           |
| ------------------- | -------------------------------------------------------------------------------------- |
| **New Hub**   | Travels to the base, raises the town room, empties it, opens the editor on a blank hub |
| **Edit Hub**  | The same trip, then rebuilds the saved hub in the room for editing                     |
| **Visit Hub** | The same trip, then rebuilds it and leaves the player in it                            |

`HubSession` does all three: get to `Base Biome 1` (`GameManager.ToShip`, picked up again on scene
load), raise the room, quiet `LambTownController` so it stops driving shops and boards that are
about to stop existing, clear placed objects / scenery / terrain, then hand over to the editor or
the blueprint loader.

**The players step out of the room for the sweep and back in after it.** In the base the player
hangs off a unit layer several levels inside the room, and `MapEditorProtection` only looked
*upwards* for a `PlayerFarming` — so a container holding the player counted as scenery and the sweep
destroyed both. That is what left a hub with no character in it, and it also explains the F7 panel
refusing to open there: the panel bails when there is no player. Protection now looks downwards too,
as a backstop — but a protected container is a piece of town left standing in a finished hub, so the
session parks the players at the scene root first and nothing has to be spared.

**The trip in happens behind a curtain.** `HubCurtain` is a black full-screen `Image` on its own
`DontDestroyOnLoad` canvas (sorting order 5100), raised the moment a hub is asked for and dropped
only once the player is standing in it. Its bottom-right corner is the game's own loading corner:
the spinning crown is `MMTransition`'s `LoadingIcon` cloned whole (`ForceFifty`, the half-fill the
vanilla screen shows when it has no real progress), and beside it a label reading *"Entering
\<hubname\>..."* — built fresh rather than cloned, since a cloned label can carry a localizer that
would overwrite the message, but wearing the vanilla label's font and material. Everything in between is something nobody should have to
watch: the base's own arrival playing out in a town that is about to be emptied, the sweep taking it
apart, and the player being moved onto the hub's floor. That last one is what the *"and then it
teleports me a few seconds later"* complaint was.

The curtain is **pure UI — it pauses nothing**. Holding the game's own transition over the same
stretch was tried and reverted: `MMTransition`'s cover is released by `ResumePlay`, and the arrival
*sets the player up* (state, camera, animation) as part of running to that point, so a hub built
behind a held cover inherited a half-finished one — an invisible, immovable player. Here the arrival
runs to its end exactly as it always did; it simply is not on screen. A failsafe uncovers the screen
after 45 seconds whatever happens, because a black screen with no way out is worse than an ugly
arrival.

**The arrival itself is the town's own, aimed at the hub's spawn point.**
`DLCShrineRoomLocationManager.PositionPlayer` is three lines — place the players at the room's door,
then walk the lamb eight units in with `GoToAndStop(..., IdleOnEnd: true)`. `ArriveAtSpawn` does the
same thing with the destination the author chose: the players are put down 3.5 units short of the
mark *while the screen is still black*, the curtain fades, and then the walk plays. The cut happens
where nobody can see it and what the player watches is a lamb walking into a hub.

The walk plays **only when the navigation graph covers both ends of it** (`GroundUnder`: A*'s
nearest walkable node within 2.5 units, mid-scan exceptions counting as "no"). `GoToAndStop` paths
through A*, and a graph that has not caught up with the hub's floor hands it a path starting
somewhere else entirely — the walk heads the wrong way, vanilla's own bail-outs fire (`maxDuration`,
or one second of no progress), and `forcePositionOnTimeout` snaps the player across the room. That
snap is what "it teleported mid-walk" was. No ground, no walk: the player is placed on the mark
directly. When the walk does play it always ends — `maxDuration: 3`, `forcePositionOnTimeout: true`,
and a final belt-and-braces snap if it still ended more than two units from the mark.

**Triggers are muted while the walk runs** (`CTMapTrigger.MuteFiring`, walk time plus two seconds).
The walk crosses — and ends on — authored volumes, and an entry mid-walk is the session carrying the
player, not the player walking in: a control-locking sequence fired then calls `SetInactive` on a
player who is `GoToAndStopping`, which wedges `InActive` onto the walk's own end. The mute leaves
`_inside` false, so a sequence on the spawn trigger itself fires the moment the mute lifts — a
welcome caption plays right after the player lands, not during the landing.

Nothing forces the player's state: that was an earlier attempt and it lost, because the arrival's
own coroutines set `CustomAnimation` again on the next frame and a state shoved on top of a running
animation left the player frozen mid-pose (the black silhouette). What remains is the tail —
colliders, the conversation lock, the camera, the input maps — plus a watchdog for an arrival that
still manages to leave the player wedged. The watchdog **stands down for good at its first sight of
a free, controllable player**: from that moment anything that parks the player — an NPC
conversation, the F7 panel, a trigger's cutscene — is doing it on purpose, and a watchdog that kept
running was yanking the player out of dialogue mid-sentence.

**Where the player lands is authored, and compulsory.** A trigger carrying the **Hub spawn point**
action marks it; `HubSession.SpawnPoint()` finds it by walking `CTMapTrigger.All`. The action does
nothing when the volume is entered — the position is read long before the player is put down — so a
trigger may carry it alone or alongside a welcome caption. **Save Map and Ctrl+S both refuse a hub
that has none** (`RuntimeMapEditor.HubSaveBlock`), the way a dungeon room is refused without its four
doors. Without one the arrival falls back to `EntranceFromBase` — the town's own door, measured
*before* the sweep so it survives — which is right for a brand-new hub being authored (the author is
dropped where the town's door was) and wrong for a finished one.

**Nothing starts until the game has finished arriving.** `BaseLocationManager.PlacePlayer`
instantiates and positions the player, and a trip in from a dungeon plays a spawn on top of that, so
the session waits for a real, awake, not-mid-spawn player with no transition still covering the
screen before it touches anything. Starting early is what left a hub entered from a dungeon with no
character in it.

The shape tool's template is cloned from a live sprite shape, so it is captured **before** the
sweep — after it there is nothing left to clone, since `FindObjectOfType` cannot see the base room's
own shapes once that room is switched off. It also prefers a shape that can actually draw a fill,
and **ground over water** — a water profile's fill is the water surface, which over an emptied room
draws as nothing. Failing that, `SpawnShape` imposes one: a profile carrying a `fillTexture` (those
are assets, so the reach extends to rooms that are switched off) and a fill material in the
renderer's first slot, borrowed from any shape in the scene that already draws one.

A new shape is also **wound the same way round as the shape it was copied from** (shoelace sum over
the template's spline). A sprite shape fills the side its spline turns towards, so a square wound
against the template's direction comes out inside-out — edges facing in, fill spread over everything
outside the square. That is what a hollow-looking hub shape actually was. Three more things the town room does not arrive with, all handled after the
sweep:

- **A content root.** Every tool builds into `GenerateRoom.CustomTransform` (`SceneRefs.ContentRoot`).
  A generated dungeon room is handed one; Woolhaven is hand-authored and has none, so the session
  gives it one — created *after* the clear, or the clear would take it too.
- **A collision composite.** A room's collision is one merged outline (`GenerateRoom.RoomTransform`,
  a `CompositeCollider2D` in `Outlines` mode): a collider that joins it becomes an *edge* to walk
  along, while a collider left standing on its own is a solid body that pushes whatever touches it
  away. Woolhaven has no such composite — its buildings each carry a baked collider — so every shape
  drawn in a hub kept its own `PolygonCollider2D` and shoved the player straight back out of it.
  `SceneRefs.EnsureRoomComposite()` builds one (static `Rigidbody2D`, `Outlines`, layer `Island`) and
  assigns it to `RoomTransform`, after which every tool, `JoinRoomComposite` and
  `SetColliderAndUpdatePathfinding` behave exactly as they do in a dungeon. It also explains the
  blank `NullReferenceException` the collision rebuild used to log: vanilla's own
  `SetCollider` dereferences `RoomTransform` unconditionally.
- **Player control.** The trip in leaves the players inactive, because the arrival that would wake
  them is a dungeon door's walk-in and a town room has no doors. The session runs the same hand-back
  `BlueprintLoader` does after an entry: state, colliders, `OnConversationEnd`, the camera, then
  `ResetMainPlayer` / `RefreshCoopPlayerRewired` — without that last pair only movement survives the
  trip — and clears any transition still holding the clock.

The dock drops what a town has no use for: **Enemy**, **Podium**, **Door**, **Level** and **Dungeon
Builder**. The tools are still built, since the loader and the clear sweeps ask for them by type;
they just have no button and the wheel skips them. **Load Map** lists only other hubs' blueprints —
a dungeon room loaded into the town would arrive with doors and enemies. **Before it clears anything it checks that `SceneRefs.Room` really is
`DLC_ShrineRoom`** and bails out if it is not: in this scene the other candidate is the player's own
base, and there is no undo for emptying that.

Nothing is written to the scene, so **leaving a hub is a reload of `Base Biome 1`** and the town
comes back untouched. Every scene load ends a *running* session, that reload included — but a
*pending* trip survives until the base itself arrives, because the way there is not always a single
load and a transition or loading scene can land first. (Ending the request on the first scene that
was not the base is what made travelling to a hub from a dungeon appear to do nothing until the
button was pressed again from the base, where no trip is needed at all.) A trip that never lands
times out after 90 seconds, so a death warp cannot build a hub around some later arrival.

**Saving is what makes it a hub.** Save Map in a hub session writes the room blueprint and, beside
it, a `CTLevelBlueprint` with `IsHub` naming that blueprint — the record the world map's Hub picker
lists and playback rebuilds. The four-door requirement is waived for that save: a town room has no
doors to a next room, and Woolhaven has none to find. In its place stands the spawn-point
requirement above — a hub has to say where the player arrives. Saving under a different name makes that name
the hub.

**The portal is authored, not built in.** Dress whatever object reads as a portal, then put a
trigger volume on it with either **Return to base** (sends the players home) or **Open world map**
(opens a named world map, so a hub can be the place you travel from). Both are ordinary trigger
actions, so a hub can also greet the player with a caption, a conversation, or a camera move first.

A world map node reaches a hub with `TargetKind: "Hub"`; hubs are listed there rather than under
Level, and the node goes through `HubSession` rather than the level runner — a hub is a town room,
not a dungeon. Entering one never completes the node: a hub has no success path to record.

#### The build totem

A hub can have the base's **build totem** standing in it: the player walks up, the real build menu
opens, and they place real buildings on the hub's ground for real resources. It is placed by hand
like anything else — first entry in the Structure tool's list, hub context only — and saved in the
blueprint as `BuildTotem { Position }`. No totem, no build menu; that is the whole switch.

**It is a copy of the one in the base, not a rebuild.** A hub runs in `Base Biome 1`, so the
original is in the same scene, switched off with the base room —
`Resources.FindObjectsOfTypeAll` reaches it there. Copying it brings its art, its
`Interaction_PlacementRegion`, its `PlacementRegion` and the whole build menu behind it. The vanilla
totem is a scene object rather than a saved structure, which is the only reason this is possible:
nothing in the save refers to it, so nothing in the save has to be told a second one exists.

Three details of the copy are load-bearing:

- **It wakes inside a switched-off holder**, the same trick the placement ghosts use. A region claims
  `PlacementRegion.Instance` in its own `Awake`, and that field is how the rest of the game finds the
  *town's* region — so `SetAsInstance` has to be off before the copy is ever allowed to wake.
- **`OnDestroy` clears `Instance` whether or not that region was holding it.** Harmless in a game
  with one region and a scene reload behind every teardown; not harmless when a hub's copy is torn
  down while the town's own sits switched off. A prefix/postfix pair puts it back — for our regions
  only, vanilla's behaviour untouched.
- **The region needs a `Structure` with a brain before it can hold a single tile**, because
  `PlacementRegion.Grid` *is* `StructureInfo.Grid`; without one it hands back a throwaway list and
  the fill looks like it worked. `Structure.CreateStructure(..., save: false)` gives it one without
  the save hearing about it. Reading the `structureBrain` property once is also required: the build
  path uses the cached field directly, past the lazy getter that would have filled it.

**The grid is ours** (`HubBuildRegion`). Vanilla's fill is the right idea — walk the integer lattice
of the region's local space, keep every point inside a `PolygonCollider2D` — but two details do not
survive the trip. It is seeded once at local `(0,0)`, so ground the seed cannot walk to is not
buildable, and a hub is often several islands with the totem on one of them; and with the major DLC
installed it rebuilds the polygon from the *base's* cached outline every time it runs, which would
throw the hub's shape away a frame after we set it. So a prefix on `CreateFloodFill` sends our region
to a bounded scan instead: same lattice, same `ClosestPoint(p) == p` inside test, no seed, and the
polygon cut from the room's own collision composite — the buildable area *is* the ground, with
nothing to author twice and nothing to keep in step with the Shape tool. Disjoint islands included.

The grid is re-cut every time the menu opens, because a hub's ground is not fixed the way the town's
is — its author can redraw it between visits. A fresh grid is an empty grid, so everything standing
is re-stamped onto it immediately afterwards (vanilla hits the same problem when the player buys land
and solves it the same way); skip that and the next building lands on top of the last.

**Nothing reaches the player's save.** The temptation is real — a hub runs in the Woolhaven room and
Woolhaven has a real save list behind it — and it must be resisted. That list is shared by every hub
*and* by the actual Woolhaven, and `DLCShrineRoomLocationManager.PlaceStructures` prunes it on load:
an entry whose grid cell is already taken is deleted outright, which is precisely what two hubs
sharing one list would do to each other. So the vanilla flow runs end to end — grid, cost, build —
and the moment the game files the new structure, `StructureManager.AddStructure`'s postfix takes it
back out and writes it to `CustomHubStructures/slot<N>/<hub>.json` instead. The brain is made either
way, and the brain is the building; the list decides only whether the game will save it.

The interception is scoped as tightly as it can be — **not** "anything while a hub is open", because
the game restores the player's actual Woolhaven buildings through the same call and deleting those
would cost them their town. It is only what passes through a build driven by *our* region: a
prefix/finalizer pair around `PlacementRegion.Build` (which also catches the branch that hard-codes
`FollowerLocation.Base`, the one that would have put a fence built in a hub into the player's town),
and another around `StructureManager.BuildStructure` for the hub location (the far end of a build
site, long after the placement loop is gone). `HistoryOfStructures` is a save field too, and it is
put back for a type first seen in a hub.

Two consequences worth knowing. **Build sites finish instantly**: what completes one is a follower
walking over with an armful of wood, and nobody lives in a hub — so the site is built one frame after
it appears, which is when the region has finished stamping its cell and bounds. And **the brains are
retired on the way out**: the game clears them only on quit/death/menu, never on a scene change, so
left alone they would still be listed against the town room the next time the player walks into the
real one.

`PlacementRegion.PlayRoutine` also reaches for a handful of base-scene singletons without checking
any of them — `TownCentre`, the HUD, the weather, the path tiles, the lighting, the player, the
camera, and `DLCLandController` under the major DLC. All should be present, since a hub is in the
base's own scene; but a null inside a coroutine dies halfway through and leaves the player unable to
move, so the interaction is refused with a log line instead if one is missing. The mouse clamp
(`X_Constraints`/`Y_Constraints`, static get-only properties) is widened to the hub's own bounds
while a totem is standing — the vanilla numbers are the base's extents, which would pin the cursor to
a corner of a hub or off it entirely.

### Base editor

The same tools, run on the player's own base. Opened from the **F7 panel → Edit Base**, which is
offered only while the player is actually standing in it: the base editor edits what is under their
feet and never travels. Once open, `F4` closes and reopens it for the rest of the visit.

**Everything here is a difference, not a picture.** A hub is authored from nothing — the town room is
emptied first, so a save can simply write down everything in it. The base already exists and most of
what is standing in it is the player's save. So the file holds three lists: what this mod added, what
was taken away, and what was moved. It is `CustomBaseMaps/base_slot<N>.json`, one per save slot, and
it is re-applied on every arrival in the base whether or not anybody opens the editor. A slot with
nothing saved costs one settle-and-check on arrival and creates no editor at all.

**The safeguard: the game's save file is never written to on the editor's behalf.** Not one structure
added to it, not one removed, not one position changed in it. Uninstall the mod and the base is
exactly the base — everything this editor added simply is not there any more.

**What can be touched.** Rearranging the base is the whole point, so what is off-limits is
deliberately tiny — and the line that matters is *deletion*, not movement:

| Tier      | What                                                                                                                 | Can be                  |
| --------- | -------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| Editable  | What this mod placed, and inert scenery — trees, rocks, grass                                                       | moved, resized, deleted |
| Move only | Everything else of the player's: shrines, temples, beds, farm plots, dungeon doors, the town centre, the build totem | moved                   |
| Protected | Followers and their pets (they walk off on their own), and the`PlacementRegion` object                             | nothing                 |

The placement region is the one structural exception: its buildable grid is a lattice in that
object's own local space, so moving it slides every cell in the base out from under every building
standing on one. The totem the player walks up to is a separate object and moves freely.

Deletion is never offered for anything the player owns — a building was paid for and may be holding a
follower's job, and there is no undo. `MapEditorProtection.CanDelete` is the single answer, and every
path that destroys something asks it rather than each tool deciding for itself.

**A structure placed here is a real building.** Instantiating the prefab gets the art and nothing
else — the components on it read `Structure.Brain`, so without one a bed cannot be slept in and a
plot cannot be farmed. The brain is what makes it a building, and the game will make one without
filing it: `AddStructure`'s `save` flag is the list write alone. So the building works exactly as the
game intends and the save never hears about it. Ours are remembered in our own file, rebuilt on each
arrival, and their brains retired on the way out — the game clears brains only on quit, death or the
menu, so one left behind is a building the base thinks it still has. Objective progress raised by
those placements is dropped, since the apply pass re-places every one of them on every arrival.
Custom structures have no vanilla building data and stay decorative. Two things are still touched:
`DataManager.StructureID` advances (a counter, not content — reusing ids would be worse), and bounds
are assumed 1×1, so the build totem may allow something to overlap a placed structure.

**The base's own terrain can be reshaped too.** Dragging a node on a sprite shape the base came with
is recorded as an edit against that shape — found by hierarchy path, falling back to name and
original position, like every other journal entry — and its spline is written back on arrival. New
shapes drawn with the tool are separate, and round-trip through the delta's own `Shapes` list.

The base's shapes ship with collision **off**: the town's walkable area is `Room.Pieces[0].Collider`,
not the art. Turning *Shape Has Collision* on for one adds a collider, and that collider has to be
folded into the room's composite outline (`MergeCollisionIntoRoom`) or it is a solid body standing on
its own — indistinguishable from floor until something walks into it. The editor's own commit path
always did this; the load path did not, which is why reshaped terrain came back from every load
pushing the player around, and why toggling collision off and on again fixed it until the next load.

**Moving one of the player's buildings** is the one place where the safeguard needs machinery rather
than restraint. Followers navigate to `StructuresData.Position` and the buildable grid is stamped
from `GridTilePosition`, so a transform-only move leaves everyone walking to where the building used
to be. Both have to say the new place for the game to behave — and the file has to say the old one.
`SaveMask` makes both true at different moments: a prefix on `SaveAndLoad.Saving` (the single
statement that hands the live save object to the serializing thread, on the main thread, before it
starts) puts every moved building back where the player left it, and the write's own completion
callback puts the moves back. The window is a few frames, it only exists while a save is in flight,
and if anything goes wrong the state left behind is the vanilla layout — the moves re-apply from our
file on the next arrival. A watchdog lifts the mask after twenty seconds if the callback never comes.
`Forget()` restores originals rather than lifting the mask: lifting it writes the moved values back
into the live save data and then discards the entries that would hide them again, which is how an
early version of this cost a player a temple.

**Why it has to be airtight**, and the second half that does not depend on the first: what the game's
loader does with a building's entry it dislikes is not correct it but *delete* it. `PlaceStructures`
removes any entry whose grid cell another entry has already claimed, and any entry standing outside
the base's ground polygon — silently, and unrecoverably. So `BaseDelta.RepairSavedPositions` runs
from a prefix on `PlaceStructures` and puts every moved building back to its recorded original before
a single entry has been looked at. The journal knows what it moved and where each one stood, so this
is certainty rather than a bet; the move is re-applied by the apply pass a moment later. Between the
mask on the way out and the repair on the way in, the loader can never act on a position this mod
wrote — which is why a move is allowed to go anywhere, including places vanilla would have deleted
the building for standing in.

**New ground.** The Shape tool works as it does anywhere else, but in the base a shape has to reach
three separate things or it is ground in name only (`BaseGround`):

1. the room's collision composite and the navigation graph — what the player can walk on;
2. `BiomeBaseManager`'s ground validation collider — what "inside the base" means to
   `Follower.EnsureWithinBounds` (which teleports anyone outside it to the town centre) and to
   `LocationManager.PlaceStructures` (which **deletes save entries** outside it on load);
3. the build totem's placement region, whose buildable grid is a lattice cut from its own polygon.

The sequence is the game's own, the one it runs when the player buys land: merge the outlines into
`Room.Pieces[0].Collider`, re-derive the validation collider, `SetColliderAndUpdatePathfinding`,
clear `Follower.Points`, then throw the buildable lattice away and cut it again. Path 0 is never
touched — that path is the base as the game shipped it and every bounds check falls back to it; ours
are only ever appended after it. Shape edits debounce into one apply, because each one rebuilds the
navigation graph for the whole base.

The buildable grid is extended rather than replaced, which is the opposite of what a hub does. A
hub's region is ours and its ground is all authored, so its fill is ours too. The base's region is
the player's, its fill knows about bought DLC land and the bridge, and none of that is ours to
reimplement — so vanilla runs untouched and a second pass adds the lattice points inside the added
outlines. The added paths come *off* the polygon before vanilla's fill runs and go back on after:
that fill is recursive and walks from a single seed until it runs out of polygon or of tile budget,
and budget spent wandering onto new ground is budget the base's own tiles do not get.

**Buildings the player puts up on added ground** are the one case where their own build has to be
intercepted, and only because the game would otherwise destroy it: `PlaceStructures` culls save
entries outside the validation polygon at load, and our ground is not in the polygon the *save* was
written against. So such a build is lifted out of `BaseStructures` in the same call that files it —
the brain is made either way, so the building works exactly as the game intends — and remembered in
our file instead. `HistoryOfStructures` is put back for a type first built there, since the game hands
out unlocks off that list. Everything built on the base's own ground is left completely alone.

**Where the player lands.** A base with a trigger carrying the *Hub spawn point* action puts the
player on it — standing there from the start, not walking to it afterwards. The position is read from
the saved file rather than the live trigger, because the player is placed during the base's arrival,
long before the delta is applied and that trigger exists. So the base has to be saved once before it
takes effect; without such a trigger the base's own arrival spots are used, as always.

It hooks `Interaction_BaseTeleporter`'s warp-in and nothing else, because that is what the game runs
for a real arrival. The obvious hook — `LocationManager.PositionPlayer`, which every arrival goes
through — is too many arrivals: stepping out of the temple is one, and so is returning from the door
room or the shrine room. Those are doorways *inside* the base with a spot apiece, and landing on the
spawn point out of the temple door is not "the player arrives here", it is being teleported away from
where they just were. (`GetStartPosition` is worse still: the same method answers for arriving
*followers*, every one of whom would be dropped on the player's doorstep.)

Three things the warp-in ties to the portal's own transform: it places the player on it, snaps the
camera to it, and frames it for the length of the animation. That last one is why re-placing the
player alone looked broken — `OnConversationNext` hands the camera a GameObject to follow and holds it
there however often anything else asks it to look elsewhere, so the lamb appeared at the spawn point
while the camera watched an empty portal. But it is *handed* that object, so a prefix hands it a
stand-in parked on the spawn point instead, scoped to the teleporter's own call during an arrival so
no other conversation in the game is touched. The player is then put on the mark over the following
frames. The portal keeps its place in the base; what cannot follow is the warp effect itself, since
that is an animation on the portal's own skeleton — it plays off-screen, and the player simply
arrives.

**Picking things out of an authored scene.** The Select tool climbs from whatever the cursor hit up to
the thing the author means to move, which in a dungeon is the whole prop. The base is hand-authored
and has real regions in it, so there the climb stops below anything larger than `RegionSize` (9 world
units) across. Without that, clicking the teleport bridge handed back the whole teleport area, trees
and DLC statue included; counting children instead was the first attempt and far too eager, since a
single bush is several sprites and came apart into leaves. Size is the honest signal — things that
belong together are together *because* they are in the same place. Buildings are still picked up
whole: a `Structure` ancestor wins over the size rule, however its art is nested.

**What the base editor changed outside the base.** Almost all of it is gated on
`RuntimeMapEditor.Context`, which is `Dungeon` unless a hub or base session is running — the
region-size selection rule, shape collection, the vanilla-floor toggle, base protection and every
journal hook (`NoteRemoved`, `NoteMoved`, `NoteShapeTouched`, `BaseGround.RequestRefresh`) all return
immediately elsewhere. Three changes are **not** gated and apply to dungeon and hub rooms too:

| Change                                                                                                | Effect elsewhere                                                                                                                                                                                                      |
| ----------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EnsureCollider` delegates a new collider to the room composite at creation rather than at the bake | Same end state, earlier. Closes a one-frame window where a fresh collider is a solid body — harmless in a dungeon, where the clock is stopped, and the cause of the player being shoved off the spawn in a live base |
| `SetActiveShapeCollision` / `ApplyColliderSettings` call `MarkEdited()`                         | Toggling collision or dragging collider detail now counts as unsaved work, so the close guard offers to save where it used to go quiet                                                                                |
| Hubs filtered out of the Level tool's room pool and the Dungeon Builder's level picker                | Deliberate: a hub dealt into a floor arrives with no doors and the run stops in it                                                                                                                                    |

Three more are shared code that was rewritten but is *equivalent* rather than changed, worth knowing
if one of them ever looks like a regression: `SelectionRoot` gained `SceneRefs.ContentRoot` as a stop
(in a dungeon that resolves to `room.CustomTransform`, already in the list); every sweep moved from
`IsProtected` to `CanDelete`, which outside the base is defined as exactly `!IsProtected`; and
`SceneRefs.Room` / `ContentRoot` gained overrides that are null unless a base session sets them, and
are cleared on scene load and whenever a hub starts. The patches in `BaseEditPatches` are all guarded
on `Location == Base`, `!HubSession.Busy`, or state only a base session fills — except
`GameManager.OnConversationNext`, which is patched globally but returns on a bool check unless a base
arrival is in flight.

Hidden in base context: **Clear** and **Load Map** (both would replace a room that is not ours),
plus the dungeon-only tools. The Shape tool's *Vanilla Floor Collision* switch is hidden too —
switching the base's own floor off would drop everyone standing on it. Save has no name dialog: the
slot names the file.

### Dungeon Builder

Authors a **dungeon** as the Slay-the-Spire-style node graph the game shows between rooms: one node
is one level blueprint, and the graph of them is the whole run. Saved to
`CustomDungeonMaps/<name>.json`, and every saved file registers a custom dungeon at startup — so the
map *is* the dungeon, and there is no second file to keep in step with it. **Enter Dungeon** runs it.

**Save Dungeon** opens the game's name dialog — the same one the map save and the lighting profiles
use — prefilled with the current name, so it is also the rename (confirming a different name writes a
second dungeon and leaves the first alone) and it carries its own overwrite warning, which is what
the old press-twice-to-confirm was for.

**Preview Map** opens the real thing: `UIAdventureMapOverlayController`, the screen the player is
shown at an exit door, instantiated from its own prefab and handed the `Map.Map` that
`DungeonMapBuilder.Build` makes of the authored one. `disableInput` is set, because it is a look and
not a run, and `MapManager.CanShuffle` is switched off first — the shuffle prompt is one keypress
away on that screen and it would regenerate the run's map over ours. `Esc` returns.

It is offered **only when the map would play**, and that is a vanilla constraint rather than a
choice: `OnShowStarted` indexes `_adventureMapNodes[0]`, takes `GetFirstNode()` as a `.First()`,
dereferences `GetNextNode(point).point`, and skips every node with no links — so a map still being
built is a map that screen cannot draw, and a node just placed would be invisible on it. That is why
the editing view below exists at all, and why it is the one that holds every state before the map is
finished. `Hide` only switches a menu off, so the instance is destroyed on the way out; without that
every preview would leave another dead map screen in the scene.

#### The map screen

The dock panel is one question — which dungeon — and picking one goes straight to the map screen
(`DungeonMapCanvas`); closing the screen closes the dungeon and returns to that list, so there is no
half-open state and no *Edit Map* / *Close Dungeon* pair to keep in step. Closing with edits that
were never written says so in the status bar. The screen is the world map's screen with dungeon
nodes on it:

- **The room editor stands down while it is up.** This is the change that made it stop reading as a
  panel. `DungeonBuilderTool` implements `IMapEditorScreenTool`, and while `OwnsScreen` is true the
  host hides its own title, dock, options panel, status bar and shortcut list
  (`RuntimeMapEditor.SetOwnChromeVisible`), stops the camera keys panning the room behind it, stops
  the wheel switching tools, and sends `Ctrl+S` to the dungeon instead of to the room. `F6` is a
  no-op there: it works by switching the whole canvas off, and this screen hangs from that canvas.
  Before this, the room editor's furniture sat behind a translucent backdrop and every screenshot of
  the dungeon builder had another editor showing through it.
  The chrome is collected rather than named one at a time — everything parented to the canvas by the
  time `CreateUi` finishes is the host's own; anything added later belongs to a tool.
- **The map is the screen and the chrome floats over it.** Content stretches the whole canvas; the
  map name sits top-left and an `X` top-right, the options panel is a floating plate in the top-right
  corner at the world editor's own metrics (360 × 940, `(-14, -70)`) with a collapse control in its
  title bar, the shortcut list is bottom-left in the same key-cap style, the dock is a row of icons
  along the bottom (*Preview*, *Save*, *Enter* — there is no *Done*, because `Esc` and the `X`
  already close), and the status bar floats above it between the two. Every number here is
  `WorldMapEditor.BuildUi`'s, so the two map screens are one screen with different content on it.
- **One way out.** `Esc`, the corner `X` and `F4` all go through `RequestClose`: it dismisses the
  confirm strip if one is up, otherwise asks about unsaved work, otherwise closes. The prompt is the
  world map's own strip - *Save & close* / *Discard* / *Cancel*, red-bordered, sitting clear of the
  dock and status bar - and while it is up the map ignores every click and key but `Esc`. `F4` is
  routed through it too (`IMapEditorScreenTool.ScreenStepBack`), so closing the editor cannot take
  an unsaved dungeon with it; a second `F4` then closes the editor as usual. *Save & close* only
  closes if the save actually happened, since Save refuses an unplayable map and closing anyway
  would be a discard nobody asked for. A map with no nodes counts as nothing to save.
- **The status bar is two left-aligned lines**, the verdict above and the running commentary below.
  Side by side they fought for the same middle of the bar, and a long message under a long problem
  simply drew over it.
- Column labels are given the height their wrapped text actually needs (`FitHeight`, measured with
  `GetPreferredValues` since the column has had no layout pass yet). `MapEditorUI.CreateLabel` pins
  every row to one line, so the three-line summary and the longer validation messages used to draw
  straight over the widget below them.
- **The screen is the game's own too.** `DungeonMapSkin.CloneChrome` clones the whole overlay prefab
  and takes out everything belonging to a run in progress — the node and connection containers (the
  editor draws its own into its own layers), the crown and eye that mark where the player is
  standing, the shuffle prompt, the goop fade — then strips the scripts and forces the canvas groups
  to full alpha, since `UIMenuBase` fades them in on a show that is no longer there to happen. What
  is left is the map screen with nothing on it, hung behind the grid. Any nested `Canvas` goes as
  well: it would sort against ours rather than inside it, and its raycaster would eat the clicks the
  editor polls for itself. The DLC map got away with cloning nodes alone because its identity is in
  its nodes; this screen's identity is its chrome.
- **The art is the game's own**, cloned from the prefabs the in-run map screen is built from — the
  same trade the world editor makes with the DLC map. `DungeonMapSkin` mirrors `CustomMapSkin`:
  `UIManager.AdventureMapNodeTemplate` is harvested (loading `LoadDungeonAssets()` first if some
  other route got here before the game did), cloned under an inactive holder, and stripped of every
  vanilla script — they expect a live run, a `MapManager` and a node the player is standing in.
  `DungeonNodeVisual` drives what is left, the twin of `CustomNodeVisual`: it reads `_icon`,
  `_imageOutline`, the outline materials and the selection sprites off `AdventureMapNode` by
  reflection before that component is destroyed, then draws **every node as reachable** — the run
  has not been played, so a grey node would only be saying "you are not here yet". The furniture the
  stripped `Start` would have hidden (the start pin, the quest alert, the modifier icon, the flair)
  is switched off by hand; the start pin goes back on the bottom node, which is the one thing about
  a node the map itself says.
  Node art is drawn at half the size the prefab is authored for (it is built for a map at 300 units
  a step), and a boss is 1.5× as `AdventureMapNode.Configure` makes it. **Nodes are not captioned
  with names** — the game's own map does not name a node, so a name here would label something the
  player never sees. Every node is captioned with what it *is*, which layer it came out on, and
  what it *plays* instead: "Wood (L1) - vanilla floor" in grey, or the bound level in the game's
  completed-green. The layer marker earns its place because the layer is **derived** from where the
  node was dropped — it is the one thing about a node that the node itself cannot show, and it is
  what decides whether two nodes are on the same step of the run. Ids still exist, but only so a
  link has something to name; nothing on screen shows one, and the status bar, node picker and
  validator all name a node by its type and layer.
- Links are the game's `MMUILineRenderer` wearing the dotted material the map draws an unwalked
  connection with (`_connectionTexture` and `_idleDottedMaterial`, read off the overlay prefab), at
  the same width-to-spacing ratio — 7.5 at a pitch of 300. A link touching the selection takes the
  editor's accent. Each line is **kept and re-aimed rather than rebuilt**, so it stays attached to a
  node through the whole drag instead of snapping to it on release. The renderer keeps its points on
  a branch object that a component added at runtime may never have been handed, and the `Points`
  setter goes straight at it — so a line is aimed once inside the build guard, where a throw can
  still fall back to a plain one, rather than mid-redraw where it would abort the whole pass.
- **Both have a fallback**, because a view that draws nothing is worse than one that draws plainly:
  no vanilla node prefab means the blueprint's sprite on a plate (and failing that, the type's name),
  and no dotted material means a tiled dash. Neither should happen where this tool opens — F4 implies
  a dungeon scene, and the dungeon scenes are exactly where the game loads these Addressables.

#### Free placement, derived grid

**Nodes go where they are put.** `Ctrl+click` places one, left-click selects and drags, right-click
links the selection to a node or cuts a line under the cursor, `Del` deletes, `Esc` closes. Those are
the world node tool's gestures, key for key, because it is the same job. Placing with something
selected links the two immediately — a path in one gesture per step, and one undo entry, not two.

That is possible because **the grid is no longer what is authored**. A node stores `PosX`/`PosY` and
a list of child ids; `DungeonMapBuilder.Layout` resolves those onto the integer `Map.Point` grid the
game addresses nodes by, at the moment the map is built or played:

- Nodes are gathered into **rows by height** — within `LayerBand` (70 units) of each other vertically
  is the same row — and each row is numbered from the bottom.
- Inside a row, nodes keep their **left-to-right order** and are numbered from the left.
- So every node gets a point of its own. That matters more than the arrangement does: a point is a
  node's identity in `GetNode`, `NodeFromPoint`, `outgoing`/`incoming` and the level bindings, and
  two nodes sharing one would silently become the same node.

The preview therefore shows the *rows* that were drawn, not the exact pixels: vanilla lays a node at
`new Vector2(point.x, point.y) * 300f` and `Point` is a pair of ints, so nothing finer survives into
the game whatever is authored. Free placement is for the person drawing the run; the resolution is
what the renderer will accept.

**Nothing constrains a link any more.** The old rule that links may only join neighbouring layers
came from the grid, and checking the decompiled source it was ours rather than the game's — the only
place `point.y + 1` appears is the *Adventure Map Freedom* tarot card. `GetNextAdventureMapNodes`
just walks `outgoing`, so any node may lead to any other.

**Ids, not cells.** Links name a node id, so a node can be dragged anywhere without a single link
being rewritten — the move is one field and one undo entry. A map saved by the old build is migrated
on load (`CTDungeonMap.Migrate`): ids are minted, cells become positions at the pitch the old grid
drew at, and `(x,y)` links become id links. The legacy fields are read and never written again.

Right click is polled from `Input.GetMouseButtonDown(1)` rather than taken from the EventSystem —
this game installs Rewired's pointer module, which the editor already works around for left clicks,
and a link gesture that silently never fired would be worse than a hit test of our own. The canvas is
`ScreenSpaceOverlay`, so the null camera passed to `ScreenPointToLocalPointInRectangle` is correct.
Picking is geometric, past the buttons, as it is on the world map.

**A polled surface must not close an open dropdown itself.** While `MapEditorUI.TransientUiOpen` is
true the map ignores every click and key (bar `Esc`, which closes the list) and lets the list's own
full-screen catcher do the closing. Closing it from the polled handler instead *broke the widget*:
a list item's button fires on mouse **up**, the polled handler fires on mouse **down**, so the list
was already destroyed by the time the click had anywhere to land and picking an option did nothing
at all.

**Everything is undoable** through the room editor's own `Ctrl+Z` — place, move, link, cut, clear,
delete, rename, retype and rebind each push a closure onto the shared `MapEditorHistory`. Each one
begins by checking the map it captured is still the one open, and `MapEditorHistory.Undo` treats a
`false` as "skip this entry", so entries belonging to a dungeon that has since been closed dissolve
instead of corrupting the one that replaced it.

**The verdict is live.** `DungeonMapBuilder.Issues` returns every rule the map breaks and, where the
rule is about a node, which node — so the offending ones wear a red halo (orange for advisories) and
the bar's right-hand badge reads either the first blocking message or a green **Playable**. `Validate`
and `Advisory` are the first blocking and first advisory entry of that list.

**The status bar says one thing at a time.** Hovering a node reads it out; everything the editor says
of its own accord (placed, linked, saved) sits underneath and comes back when the cursor leaves. A
message also re-reads what is under the cursor as it is written, because placing or deleting changes
that without the cursor moving — otherwise the hover would talk over the sentence just printed.

Two accessors exist for this view and are worth knowing about if another full-screen surface is ever
built: `RuntimeMapEditor.WorldClicksBlocked` (because `PointerOverUi` returns true *everywhere*
inside a surface whose backdrop is a registered blocker, and so cannot tell a widget press from a
click on the surface) and `MapEditorUI.TransientUiOpen` (an open dropdown list stands outside every
blocker rect, so a polled click has to close it rather than act on what is underneath it).

**Where the game's map lives.** `MapManager` (namespace `Map`, an embedded copy of the open-source
Slay-the-Spire map package: `MapConfig`, `MapGenerator`, `Map`, `Node`, `NodeBlueprint`, `NodeType`,
`Point`) holds `CurrentMap`, and `UIAdventureMapOverlayController` renders it. `EnterNode` is what
turns a node into rooms: it reads `node.blueprint.RoomPrefabs` and feeds `BiomeGenerator` — the same
seam level playback rides.

**Nodes play levels.** Each node is bound to a `CTLevelBlueprint` from the Level tool through the
map screen's *Plays level* dropdown; a bound node writes the level's name under itself in green.
Entering one generates that level's room chain instead of what the node's type would have produced.
A node left on *Vanilla floor* behaves exactly as the game intended, so a run can mix both.

**The lowest node is the first floor**, and there can only be one of it — the game does not let the
player choose where to start: its renderer marks `GetFirstNode()` visited and offers that node's
links, so a second node on the bottom row would be drawn and never reachable. `GetFirstNode()` is a
`.First()` over the node list, so the builder emits nodes in layout order and the start is the
leftmost node of the lowest row; it wears the vanilla start pin in the editor, and the validator says
so when something else is down there with it. The exit door then decides:

| Where the run is           | What the exit door does                        |
| -------------------------- | ---------------------------------------------- |
| below the top layer        | opens the map selector to pick the next floor  |
| on the top layer           | shows the completion screen — the run is over |
| map not playable / missing | shows the completion screen                    |

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

**Notes on the layout**

- **The renderer is a grid, so something has to be.** `MakeMapNode` positions every node at
  `new Vector2(point.x, point.y) * 300f + Random.insideUnitCircle * 50f` — the integer point is the
  position, `Node.position` is not read at all, and the jitter is re-rolled every time the map
  opens. What changed is *where* that grid comes from: the editor authors positions and derives the
  grid, instead of making you author the grid.
- Editing happens on the editor's own canvas rather than inside the vanilla overlay, which is a
  `UIMenuBase` built in one pass in `OnShowStarted`, wired into the game's menu stack and Rewired
  navigation, and which pauses the simulation and pulls the camera's far plane to 0.02 to hide the
  world. The editor is deliberately outside that stack — *Preview* instantiates one for a look.
- Row 0 is the bottom row on screen and the start of the run, matching `point.y`. `outgoing` points
  up the map (toward the end), `incoming` back down; the builder fills both from the single authored
  `Children` list, because two stored directions of one fact drift apart.
- `DungeonMapPlayback` keys its level bindings on the **derived** point, re-deriving the layout with
  the same function the builder uses rather than storing it. That is safe because `Layout` is
  deterministic — the sort falls back to the node id, so equal positions still order the same way
  every time.
- **Neither Save nor Enter will write or run an unplayable map.** Save used to write anyway and
  say what was missing, but every saved file registers a dungeon at startup - so a broken one became
  a dungeon that crashes the map screen the moment somebody picks it. The badge has been saying what
  is wrong the whole time it was being drawn. Each rule is a crash or a blank screen in the game's
  own code:
  a dangling link is an unchecked `NodeFromPoint` in `MakeLineConnection`; a node with no links at
  all is silently skipped by the renderer; every node has to be reachable from the bottom row or it
  is drawn but unenterable; and a map that is all one row ends the run at its first exit, since the
  exit door reads "top row reached" as "the run is over".
- **The node type list is this scene's config, and so is Preview.** A type without a blueprint has
  no icon and no `RoomPrefabs`, so the picker offers only what `MapConfig` has — but the default a
  new node starts on (`MinorEnemy`) is a hardcoded string, and a config without that blueprint would
  let a whole map be built out of a type it cannot draw. That showed up as *Preview* refusing with
  "no blueprint for MinorEnemy" while **entering the dungeon worked**, because the dungeon that is
  entered loads its own config. The pending type is now resolved against what this scene actually
  has when the screen opens, and any node whose type is missing here raises an **advisory** rather
  than a blocking issue — the map may well be fine where it is played. `MinorEnemy` is gone from
  the no-config fallback list for the same reason: the dungeon configs this editor runs in have no
  blueprint for it, so it drew nothing.
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

| Action                                | Target                              | Behaviour                                                                                                                                                               |
| ------------------------------------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Move players to trigger               | another trigger's Id                | walks the players to that volume's centre                                                                                                                               |
| Move players to object                | a clicked object                    | walks them to that object (falls back to the authored position)                                                                                                         |
| Talk to custom NPC                    | a registered`InternalName`        | runs that NPC's dialogue tree and waits for it                                                                                                                          |
| Play animation on players             | an animation on the player skeleton | plays it once, or loops it for 2 / 5 / 10s                                                                                                                              |
| Hub spawn point (player arrives here) | —                                  | a mark, not a step: the trigger's position is where a hub's arrival walks the player to. Does nothing when the volume is entered, and a hub cannot be saved without one |

**Camera actions**

| Action              | Target                                                              | Behaviour                                                             |
| ------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Look at object      | a clicked object                                                    | frames it for 0.5–8s, then hands the camera back                     |
| Look at trigger     | another trigger's Id                                                | same, aimed at that volume's centre                                   |
| Set camera offset   | framed in the editor                                                | shifts the camera relative to whatever it follows                     |
| Reset camera offset | —                                                                  | back to centred on the players                                        |
| Set camera zoom     | 1–10                                                               | follow distance; smaller is closer, 10 is the rig's own resting value |
| Reset camera zoom   | —                                                                  | back to whatever the rig was on before a trigger touched it           |
| Play camera effect  | chromatic aberration, vignette, desaturate, shake, letterbox in/out | runs the effect and waits for it                                      |
| Play cutscene       | a video in`CustomCutscenes`, or a vanilla one                     | plays fullscreen and waits for it to end                              |

**Screen text**

| Action                   | Target                | Behaviour                                 |
| ------------------------ | --------------------- | ----------------------------------------- |
| Caption (bottom left)    | typed title + subtext | the pair, bottom left                     |
| Title (top of screen)    | typed title + subtext | the same pair, top centre                 |
| Fullscreen text (centre) | typed title + subtext | the pair centred over a 75% dimmed screen |

**Ambient actions**

| Action         | Target                                          | Behaviour                                                                                                            |
| -------------- | ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Apply lighting | a saved lighting profile, or "Vanilla lighting" | cross-fades the room's lighting over 1 / 2 / 4s (or instant), then moves on; vanilla restores the biome's own values |
| Change music   | an FMOD music event                             | starts the track and keeps it looping; does not wait for it                                                          |
| Open world map | a saved world map                               | opens it and waits until it closes — a hub's travel portal                                                          |
| Return to base | —                                              | ends any run and sends the players home, as the dungeon portal does; nothing after it runs                           |

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

| Action          | Where                                |
| --------------- | ------------------------------------ |
| Caption         | bottom left, left aligned            |
| Title           | top centre                           |
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

**Sound comes from a companion file.** Unity's video player produces nothing audible in this
build — it reports *"Direct audio output mode not yet supported for this VideoPlayer backend"*, and
routing it through an `AudioSource` instead is equally silent, because the engine's own audio is
not what this game runs on. The game's own cutscenes work around exactly this: the intro is a
**silent video** with `event:/music/intro/intro_video` fired alongside it. So a sound file next to
the video with the same name — `test2.mp4` and `test2.ogg` — is played through FMOD when the video
starts and stopped when it ends. `.ogg`, `.mp3`, `.wav`, `.flac` and `.aiff` work; the mp4's own
AAC track does not, since FMOD only decodes AAC on Apple platforms. The name is matched for vanilla
cutscenes too, so `Intro.ogg` gives the game's own intro a soundtrack.

**Extracting that file is automated, with nothing to install.** On startup every video without a
matching sound file is decoded through **Media Foundation** — the Windows codec stack behind every
video thumbnail in Explorer, reached via NAudio (`NAudio.Core` + `NAudio.Wasapi`, about 360KB
shipped beside the plugin). It reads the mp4's AAC track, which FMOD cannot, and writes a 16-bit
PCM `.wav` next to the video, which FMOD reads without thinking about it. It runs on a background
thread, writes under a temporary name and moves the file into place when finished, so a decode
that dies halfway leaves nothing to be found. It happens once per video and never again.

The decode is COM interop and Mono's support for it is not guaranteed; a runtime that cannot do it
fails on the first attempt, is not asked again that session, and the ffmpeg path below takes over.

**ffmpeg is the fallback.** When Media Foundation is unusable, a video without a matching sound
file is handed to ffmpeg — `-vn -c:a libvorbis -q:a 4` — in the background, and the `.ogg` lands next to
the `.mp4` where the player already looks for it. ffmpeg is looked for in `CultTweaker/Tools/ffmpeg.exe`, then beside the plugin, then on PATH; when
neither route is available the log names the cutscene and says what to drop in the folder.
Conversions either way are fire-and-forget: a long video transcodes while the game runs and is
picked up the next time that cutscene plays.

**The line down the middle** of every cutscene was the video camera drawing the room. The video is
rendered `CameraNearPlane` on a camera the prefab brings with it, and that camera has a culling
mask — so along with the video it drew whatever world geometry sat in front of it, which from where
it sits projects to a hairline. Not part of the video, and not a UI element either, which is why a
scan of every canvas graphic on screen found nothing. Its mask is emptied for the duration and put
back afterwards; the near-plane blit is a command buffer and is not affected by culling.

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

**Revisiting an authored room** goes through none of that: `BiomeRoom.Activate` only switches the
previous room's object off and this one's on, so `Generate` never runs again and the blueprint is
never re-applied. What *does* run is vanilla's re-entry housekeeping on a room it still believes it
generated — `GenerateRoom.OnEnable` → `RegenerateDecorationsWithPool` — and three of its steps are
wrong for an authored room. All three are suppressed by the `CustomRoomMarker` the loader leaves on
it (`CustomRoomPatches`), which is a *lasting* answer where `LevelPlayback`'s `SuppressVanillaContent`
is only open during the generation window:

- **`SpawnDecorations`** re-rolls the biome's trees, rocks and critters into the room, and spawns
  them *inside* sprite shapes — all over the author's own floor. This is what put random trees in a
  custom room on every revisit. The patch still sets `GeneratedDecorations`, because
  `BiomeGenerator` blocks the arrival until it turns true.
- **`DisableDecorationsNearDoor`** switches off any `SceneryTransform` child within three units of a
  door, on the assumption that everything under there is scattered dressing. In a custom room it is
  a prop the author put there deliberately.
- **`OnDisable`** recycles every `SceneryTransform` child on the way out — and `ObjectPool.Recycle`
  **destroys** anything the pool did not spawn, so authored props were thrown away when the room was
  left and never came back. `customDecorations` is vanilla's own switch for "this room's scenery is
  not mine to recycle"; `Mark` sets it (by reflection — it is private) and a real regeneration clears
  it again along with the marker.

**The save dialog** is the game's own naming modal (`UICultNameMenuController`, the one the cult is
named through), with its disclaimer line repurposed into a live overwrite warning. The editor
closes fully before it opens and reopens (restoring camera, zoom and tool) when it closes: the
modal's show/hide animations run on **scaled** time, so the editor's `timeScale 0` froze the dialog
half-open, visible and permanently unable to take a keystroke.

**Room locking in custom dungeons** (`APIHelper/RoomLockNet`) is decided after the arrival, not during
generation. Whether a room should be locked has one honest answer — is anything alive in it? — and
that answer does not exist while the room is still being built: the encounter system is still
spawning and the player has not arrived.

`CustomDungeon.SpawnEnemies` used to call `RoomLockController.RoomCompleted()` from the generation
hook whenever a dungeon had no custom enemy list of its own, and that is what stopped
vanilla-populated rooms locking. `RoomCompleted` does not merely open doors: it sets
`CurrentRoom.Completed` on the `BiomeRoom`, and `BiomeGenerator.PlacePlayer` wraps its **entire**
arrival block — the enemy count, the walk-in and the `CloseAll` with it — in `if (!CurrentRoom.Completed)`. Calling it during generation told the game the room was finished before the
player had arrived, so the arrival never looked at what was standing there. The vanilla monsters were
present the whole time; nothing ever asked.

So the net waits for the walk-in to end (`PlacePlayer` parks the players `InActive` and vanilla's own
`CloseAll` hangs off the end of that walk, so acting sooner would race the code it backs up), then
reconciles the doors with what is actually in the room: **enemies with the doors open → `CloseAll`**,
**nothing at all with the doors shut → `RoomCompleted`**. The second half is why the old early call
existed — a room with nothing in it is locked by vanilla anyway, since `doorsWillClose` defaults true,
and with nothing to kill there is no way out. Both directions are safe to run on a room the game
already got right. Level runs bring their own net (`LevelPlayback.LockIfContested`), which also knows
what the blueprint put in the room, so the two never arm together.

---

## File map

| File                                                                 | Role                                                                                                     |
| -------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `RuntimeMapEditor.cs`                                              | Editor host: canvas, dock, panels, camera, pause, save/load entry points                                 |
| `IMapEditorTool.cs`                                                | Tool interface (`OnEnter` / `OnExit` / `OnUpdate` / `BuildPanel`)                                |
| `MapEditorUI.cs`, `MapEditorWidgets.cs`                          | Widget layer: plates, buttons, sliders, toggles, dropdown, grid, scroll column                           |
| `MapEditorIcons.cs`                                                | Every sprite the chrome draws (tool icons, structure icons, prop icons)                                  |
| `MapEditorData.cs`                                                 | Blueprint schema and JSON read/write                                                                     |
| `MapEditorHistory.cs`                                              | The single undo stack                                                                                    |
| `RoomSnapshot.cs`                                                  | Captures the live room into a blueprint                                                                  |
| `BlueprintLoader.cs`                                               | Rebuilds a room from a blueprint                                                                         |
| `SceneRefs.cs`                                                     | Null-guarded access to room / camera / content root                                                      |
| `MapNamePrompt.cs`                                                 | Save dialog built on the game's naming modal                                                             |
| `EnemyThumbnails.cs`                                               | Baked Spine icons for the enemy and NPC grids                                                            |
| `CustomShapeProfiles.cs`                                           | Disk-loaded SpriteShape profiles (`CultTweaker_*`)                                                     |
| `CTLevelBlueprint.cs`, `CTLevelDungeon.cs`, `LevelPlayback.cs` | Level tier: data, dungeon, run driver                                                                    |
| `CustomRoomPatches.cs`                                             | Marks rooms whose contents a blueprint replaced                                                          |
| `HubBuildTotem.cs`                                                 | Copies the base's build totem into a hub (holder-wake, singleton guard, region data)                     |
| `HubBuildRegion.cs`                                                | The hub's buildable grid: ground outline in, lattice tiles out                                           |
| `HubStructureStore.cs`                                             | What the player built in a hub, per save slot, in our own file                                           |
| `../Patches/HubBuildPatches.cs`                                    | Keeps hub builds out of the game's save; grid, clamps, singleton                                         |
| `BaseSession.cs`                                                   | The base editor's lifetime and the guards that decide whether it may open                                |
| `BaseDelta.cs`                                                     | The base as a difference: added content, the removed/moved journal, apply on arrival                     |
| `BaseGround.cs`                                                    | Ground the base did not come with: collision, validation polygon, buildable grid                         |
| `SaveMask.cs`                                                      | Hides a moved building from every write of the game's save file                                          |
| `../Patches/BaseEditPatches.cs`                                    | Grid extension, builds on added ground, the save mask's trigger                                          |
| `Tools/DungeonMapCanvas.cs`                                        | The dungeon map screen: chrome, free-placed node graph, geometric picking                                |
| `Tools/DungeonMapSkin.cs`, `Tools/DungeonNodeVisual.cs`          | The game's own adventure-map art, cloned and stripped (twins of`CustomMapSkin` / `CustomNodeVisual`) |
| `../APIHelper/ModContentPaths.cs`                                  | Finds the same content folders inside other mods'`CultTweaker` folders                                 |
| `Tools/*.cs`                                                       | One file per tool, plus shared gizmos, ghosts and protection rules                                       |

Three folders are **per save slot** rather than per map, because what they hold belongs to the
player's game rather than to the content: `CustomWorldMaps/progress_slot<N>.json` (which nodes are
beaten), `CustomHubStructures/slot<N>/<hub>.json` (what the player built in each hub) and
`CustomBaseMaps/base_slot<N>.json` (what the author changed about that slot's base). `<N>` is
`SaveAndLoad.SAVE_SLOT % 10` — saving a DLC game shifts that field by ten for the length of one
write, and a file named from the shifted value is a file the next session cannot find. The slot is
re-read on every access; there is no reliable "another save was loaded" hook.
| `Tools/TriggerActions.cs` | Trigger action model and the sequence runner |
| `Tools/TriggerCameraActions.cs` | Camera offset/zoom/look-at, post-processing effects, cutscenes |
| `Tools/TriggerScreenText.cs` | The caption / title / fullscreen text overlay |
| `Npc/*.cs` | Custom NPC behaviour, dialogue schema and dialogue runner |

Anything protected by `MapEditorProtection` is never destroyed by the clear or delete tools — doors
in particular are `IslandPiece`s carrying the `RoomLockController`, so destroying one soft-locks the
room: it can never be completed or exited.

### Content shipped by other mods

Every content folder is read from **two** places: our own plugin folder, and the same folder name
inside any other mod's `CultTweaker` folder. A mod ships content for these loaders without shipping
any code for them:

```
BepInEx/plugins/SomeOtherMod/CultTweaker/CustomNodeBlueprints/TheirRoom.json
BepInEx/plugins/SomeOtherMod/CultTweaker/CustomWorldMaps/TheirMap/config.json
BepInEx/plugins/SomeOtherMod/CultTweaker/CustomNpcs/TheirNpc/config.json
```

`APIHelper/ModContentPaths.cs` finds those folders (a bounded walk of `BepInEx/plugins`, three levels
deep, so nested installs are found without sweeping a large plugins tree) and merges them. It covers
`CustomNodeBlueprints`, `CustomLevelBlueprints`, `CustomDungeonMaps`, `CustomWorldMaps`,
`CustomShapeProfiles`, `CustomCutscenes`, `CustomNpcs`, `CustomEnemies`, `CustomStructures`,
`CustomInventoryItems`, `CustomMeals`, `CustomTarotCards`, `PlayerSkins`, `FollowerSpines`,
`FollowerSkins` and `BuildingOverrides`.

Three rules make this safe:

- **Reading is shared; writing is not.** Everything the editors save still goes to our own folder, so
  a foreign mod's files are never edited in place and a mod update cannot clobber the player's work.
  Saving another mod's world map under a new name copies its art across (`CopyArt` reads through
  `FolderForRead`), which is how one is adopted for editing.
- **Ours wins.** Where two mods use the same name for the same kind of thing the local copy is used
  and the other is skipped with a warning; between two foreign mods, the first found wins. A player's
  own creation is never shadowed by an installed mod.
- **`Exists` and `Available` are different questions.** `Exists` is local-only and answers *"would
  saving overwrite something of mine"* — the overwrite warnings and the name prompts use it, because
  saving over a foreign name writes our own copy rather than touching theirs. `Available` answers
  *"is there one of these to load"* and sees every mod's; the trigger action's world-map check, the
  dungeon map's level validation and the `FreeName` series use that one.

Paths are resolved once and kept absolute, so a foreign entry's art loads from wherever it lives —
`LoaderResult.FolderPath`, `PlayerSkins`' `SpineEntry.Folder`, `BuildingOverrides`' remembered
folder, and `CTWorldMapSerialization.FolderForRead` for a world map's images and spine subfolders.
One thing deliberately stays local: an extracted cutscene soundtrack is cached in *our*
`CustomCutscenes` folder even when the video came from another mod, since that mod's folder is not
ours to write into.

## World maps

The world editor builds a custom overworld in the shape of the DLC's Ewefall map: a full-screen
travel menu over whatever scene the player is standing in, built of the map's own art, with
travel nodes that unlock outward from what the player has completed. It is entirely the mod's
own system - the vanilla DLC map (whose node IDs are save-data array indices) is never touched,
and completion lives in the mod's own per-save-slot file.

### Folder layout

```
CustomWorldMaps/
    <MapName>/
        config.json      the map (layers + nodes)
        *.png            sprite layers and node icons, referenced by file name
        <SpineFolder>/   one spine per subfolder: skeleton .json + .atlas + pages
    progress_slot<N>.json    per-save-slot completion (managed by the mod)
```

One folder per map: the config and its art travel together, so a finished map ships as a zip.
The folder name is the map's identity.

### Opening and editing

- F7 panel, "World Maps" section: Open / Edit / New. **New World Map** takes the next free name in
  the `untitledworld`, `untitledworld2`, ... series (`CTWorldMapSerialization.FreeName`), so it
  always opens an empty canvas rather than reopening the last scratch map; rename it from the
  editor's File tool.
- **F6** flips an open map between play view and the editor.
- A trigger with the **Open world map** action (under Ambient actions) opens a named map from
  inside any custom room - this is also how a hub room's exit portal will work.
- Esc and the X button are one door, in this order: dismiss the prompt on screen, else leave edit
  mode, else close. The world is paused underneath, exactly like the vanilla map menu.
- Closing while the map differs from what is on disk asks first: **Save & close**, **Discard**, or
  **Cancel**. The check is a comparison against the last saved copy rather than a flag raised by
  each widget, so no slider can quietly slip through it. The prompt strip stands clear of the dock
  and status bar and wears a red border — it shares the canvas with the editor's chrome, and one
  drawn behind it reads as nothing having happened.
- **Rename / Save As** writes the map under the new name *and copies its art across*, since a map
  is its folder — the new name would otherwise be a config.json with no images beside it. Files
  already at the destination are left as they are, and the original map is left alone.

The editor has its own dock across the bottom, in the room editor's shape and metrics (separate
from the F4 room editor's; the two can never be open at once), with the same accent border around
the selected tool. Its options panel carries the room editor's title bar and `-`/`+` collapse, so
the quarter of the map it covers can be worked on without leaving the tool. There is no Play
button: **F6** leaves edit mode.

The dock runs **World Nodes**, **World Layers**, **World File**, and opens on the first of them: a
map is made of its nodes, layers dress it and the file tool is housekeeping, so the editor should
land on the thing you came to do rather than on a save button.

**World Nodes** is a node picker, drag, name, icon type, visibility, destination, keys, count gates
and links. **World Layers** selects a layer by clicking it on the map or its row — the selection
wears a frame — then drag to move, ctrl+drag to clone, scale, rotate, parallax, flip, labelled spine
animation and skin pickers, a two-step **Add layer** (kind, then which file — the trigger tool's
action-then-target shape; the second list survives panel rebuilds, so the next sprite is two
clicks), *Refresh art list* for files dropped in mid-session, `Del` for the selected layer, and a
layer list with `+`/`-` order and `X` delete for any other. **World File** is save/load/new,
background colour, parallax strength and wipe progress. Ctrl+Z undoes placements, moves, clones,
deletions, rotations, resizes and links.

**Scale and rotation are gizmos, not sliders** — the room editor's grammar, in canvas terms. The
selection wears two corner nodes: the blue one on the right resizes (the scale follows how far the
pointer moves from the centre, relative to where the grab started), the green one on the left
rotates (the layer follows the pointer's angle around its own centre). Nodes carry the blue one
only. Both hang from the gizmo root, so nothing drawn over the selection can cover them, and both
are **clamped to the screen**: a background layer's true corner is somewhere off in the dark, so the
handle rides the corner until it leaves the view and then holds at the edge, still on the same side
of the centre so the drag reads the same either way.

Picking a layer on the map: the **selected** layer wins wherever it sits in the stack, so one behind
others stays draggable; otherwise the front-most under the cursor wins. **Right click** drops that
priority and takes the front-most regardless — the way out when the selection is a backdrop the
cursor is inside everywhere, and left click can no longer reach anything in front of it. However far a layer is
scaled down, its grab box and its selection frame stop shrinking at the same floor — 68 screen
pixels for a sprite, 110 for a spine, whose skeleton draws well outside the rect it reports
(`WorldMapSelectionFrame.MinFor`). What is outlined is what can be grabbed, which is what makes a
spine layer at 0.005 usable at all.

The frame is drawn in the room editor's selection colour (`MapEditorGizmos.BoxColour`) and hangs
from the content rect's **last** child, a gizmo root, rather than from the layer it marks: parented
to the layer it would draw at that layer's depth, and any layer in front would cover it. It copies
the layer's position, rotation and scale every frame instead, and destroys itself when its target
goes.

The node tool has no *Add node* button — ctrl+click the map places one, and the dropdown in its
place selects a node by name, for nodes sitting under others. **Icon type** is one list: the seven
node types first, then the map folder's pngs. Picking a png overrides only the drawn icon, so a
node keeps meaning what its type means (Base closes the map, Key banks keys, Lock spends them)
whatever face it wears. **Visibility** is the initial state and **Destination** is what selecting
the node enters (`No destination` completes it on the spot). All three carry a caption above them,
since a dropdown shows its current value once one is picked and then says nothing about what it
sets.

The selected node's **unlock gate is drawn on the map**: every node in its `RequiredNodes` list
wears a green outline while it is selected, the same outline the selection wears in red, so picking
the gate is done by looking at the map rather than by reading a list of ids. Where a node is both,
red wins — it is the one being worked on.

Nodes are worked with the mouse rather than through modes:

| Gesture                     | What it does                                                 |
| --------------------------- | ------------------------------------------------------------ |
| Left click a node           | selects it; its outline turns red                            |
| Left drag a node            | moves it                                                     |
| Left click empty map        | clears the selection                                         |
| **Ctrl** + left click | places a new node there (the only way to add one)            |
| Drag the blue corner        | resizes the selection                                        |
| **Del**               | deletes the selected node (there is no button for it)        |
| Right click a node          | links the selection to it, or removes that link if it exists |
| Right click a link          | removes it                                                   |

A node's **Name** is one field: it is what the map shows, and the id everything refers to (links,
gates, saved progress) is a slug of it, kept unique on its own. Renaming rewrites every reference
inside the map, but progress already recorded under the old id does not carry over.

A **shortcuts panel** sits in the bottom left, the same one the room editor has and built from the
same `IMapEditorShortcuts` list: the active tool's keys first, then Ctrl+Z and F6, with its
title bar collapsing it. Esc has no row of its own — it does what F6 does and then closes. A **status bar** runs along the bottom beside it, saying what the cursor
is over — the dock tool, a widget, a layer row, or the node under the pointer with its type,
unlock state, destination and links — and carrying the editor's own messages when nothing is
hovered. Tool icons come from
`Assets/EditorIcons/<Tool name>.png` like the room editor's — here `World File.png`,
`World Layers.png` and `World Nodes.png`, matching `IMapEditorTool.Name` exactly, spaces and all.
A tool with no icon file borrows one that already means the right thing (World File takes Load
Map's, World Nodes takes the vanilla map's dungeon disc) or shows its initials. Icons are read once
per session and the miss is cached too, so a file dropped in while the game runs needs a restart.

In play view the map carries a permanent *Play mode - F6 to show UI* line in the bottom left, the
room editor's badge in the room editor's corner. It appears only once the editor has been used on
that map this session: a player who opened the map to travel has no business being told about F6.

### Nodes and unlocking

Positions are in the canvas's 1920x1080 reference space. A node's `Id` is a string it keeps
forever - progress is keyed on it, so renaming a node orphans progress earned under the old id.

States follow the DLC cascade: a **completed** node's children become **selectable**, its
grandchildren become a grey **preview** ("???"), everything further stays **hidden**. States
only ever upgrade when branches meet. On top of that:

- `InitialState` (`Hidden` / `Preview` / `Selectable`) is what a node is before any completion
  reaches it. The starting node is authored `Selectable`.
- A **Base** node is home: selecting it closes the map, it never completes, and its children
  unlock from the start.
- A **Key** node banks `KeysGranted` keys when completed (once, however often it is replayed).
- A **Lock** node surfaces where a selectable node would be, priced at `KeysCost` keys, and
  blocks its branch until opened. An opened lock passes completion straight through.
- `RequiredCompletedCount` + `RequiredNodes` holds a node at preview until N of the listed
  nodes are completed - the "beat the minibosses first" gate.

A node's destination is `TargetKind` + `Target`: a saved **dungeon map** (`CustomDungeonMaps`),
a saved **level** (`CustomLevelBlueprints`), or **None**, which completes on the spot when
selected (a reward cache, a key lying on the map). Completion is recorded only when a run
entered from the map ends in the success path - dying records nothing.

Selecting a node acts immediately; the vanilla DLC map asks nothing either
(`UIDLCMapMenuController.OnLocationSelected` travels or refuses, and never prompts). The one
exception is a **Lock**, which asks before spending keys - that is a cost, and it is not a gesture
the vanilla map has.

### Vanilla map art

Nodes and links are **clones of the game's own DLC map**, so a custom world reads as part of the
game: the same node discs, outline glows, selection rings, lock fills and the same four-state link
renderers (dim / open / walked / highlighted), plus the map's open, close, hover and enter sounds.

The prefab is an Addressable the game only loads when the DLC map is about to open, so the first
map opened in a session waits a moment for it ("Preparing the map..."). If it cannot be loaded the
screen falls back to the mod's own discs and lines and says so once in the log — everything else
works the same.

Only the art is taken. Every vanilla script is stripped off the clone before it wakes, because
they expect the DLC menu, its save data and its authored node graph; what is left is driven by
`DlcNodeVisual` and `DlcLineVisual`, which mirror the game's own state visuals. A node's authored
`Icon` png replaces the vanilla icon inside the vanilla frame, so custom art still gets the glow
and the ring. `NodeType` chooses which vanilla style is borrowed:

| NodeType                | Vanilla style                                    |
| ----------------------- | ------------------------------------------------ |
| `Base`                | the home node                                    |
| `Dungeon`             | a standard Ewefall dungeon                       |
| `MiniBoss` / `Boss` | the miniboss / boss nodes                        |
| `Key` / `Lock`      | the key and lock nodes, with their extra outline |
| `Reward`              | the reward cache                                 |

### Layers

Sprite layers are the png at its authored pixel size, tinted and scaled as configured. Spine
layers render on the canvas with `SkeletonGraphic` and animate even while the world is paused.
A canvas skeleton draws in raw spine units, which this game authors far larger than a 1920x1080
canvas wants, so spine layers carry the game's own factor internally: **scale 1 means the size the
game draws that skeleton at**, matching what 1 means for a sprite. Their grab area also never
shrinks below a clickable box, since a skeleton's rect says nothing about what it actually draws.
**This game's Spine runtime draws a canvas skeleton with a single texture, so world map spines
should use a single-page atlas** - a multi-page skeleton renders its first page correctly and
misdraws the rest. `ParallaxDistance` (0 pinned, 1 moves most) drifts a layer toward the mouse
in play view; the editor keeps everything pinned so placement is exact.

### Files

| File                             | What it is                                                       |
| -------------------------------- | ---------------------------------------------------------------- |
| `CTWorldMap.cs`                | World map schema and JSON read/write                             |
| `WorldMapProgress.cs`          | Per-save-slot completion, key/lock bookkeeping, run tracking     |
| `WorldMap/WorldMapScreen.cs`   | The full-screen map: canvas, pause, layers, states, travel       |
| `WorldMap/WorldMapNodeView.cs` | One node's icon, label and state rendering                       |
| `WorldMap/WorldMapLine.cs`     | The link lines                                                   |
| `WorldMap/WorldMapAssets.cs`   | The map's sprite/spine cache (and the Keep() discipline)         |
| `WorldMap/CustomMapSkin.cs`    | Loads the game's DLC map prefab and cuts stripped clones from it |
| `WorldMap/CustomNodeVisual.cs` | Drives a cloned vanilla node's state visuals and hover           |
| `WorldMap/CustomLineVisual.cs` | Drives a cloned vanilla link's four line renderers               |
| `WorldMap/WorldMapEditor.cs`   | The editor host: dock, options panel, undo, tool plumbing        |
| `WorldMap/Tools/*.cs`          | The four world tools                                             |

## Code notes (consolidated from source comments, 2026-08-29)

The explanatory comments that used to live in the source files were swept into this section.
Organized by area; each entry is the why/mechanism/dead-end knowledge, not a restatement of the code.

### Custom cutscenes (APIHelper/CustomCutsceneLoader.cs, CutsceneAudioExtractor.cs)

- Vanilla cutscenes are VideoClips compiled into Resources — cannot be added to at runtime. Custom ones play via `VideoSource.Url` pointed at a file path, through the same MMVideoPlayer. No config.json: a file in CustomCutscenes IS a cutscene, named after itself. Extensions limited to what Unity's VideoPlayer opens on Windows without extra codecs (.mp4/.webm/.mov/.m4v). Vanilla names (Intro, DLC_Intro, Trailer, Update...) are offered alongside because "play the intro" is a reasonable thing for an authored room to want.
- **Audio must be a separate companion file** (test2.mp4 + test2.ogg): Unity's audio engine is compiled out of this build (sample rate 0, zero voices) so nothing routed through an AudioSource is ever heard; game sound is FMOD; FMOD does not decode AAC on Windows, and AAC is what sits inside an .mp4. The game does the same for its own intro (silent video + FMOD event "event:/music/intro/intro_video"). Companion names are matched against vanilla cutscenes too, so Intro.ogg gives the vanilla intro a soundtrack.
- Auto-extraction of a video's soundtrack: once per video, in the background. Windows Media Foundation first (COM, MTA apartment; produces .wav at 44.1k 16-bit PCM; written to a `.partial` name then moved, so a decode that dies halfway never leaves a half-written file the player will try to play), ffmpeg fallback (`-vn -q:a 4` Vorbis; the mod's own copy first, then PATH; `-version` probe with a 4s deadline). Output always lands in OUR folder even for a video another mod shipped — theirs is not ours to write into — and AudioPathFor looks there first. ffmpeg's stderr must be drained as it arrives: one that writes more than the pipe buffer blocks BEFORE exiting, Exited never fires, deadlock. Media Foundation interop failures (TypeLoad/EntryPointNotFound/DllNotFound — i.e. macOS/Linux) are permanent for the session and not retried. Names() reads from disk each call so a video dropped in mid-session shows in the picker without a restart.

### Custom dungeons and room locking (APIHelper/CustomDungeon.cs, CustomDungeonManager.cs, RoomLockNet.cs)

- `DrivesLevelPlayback` true = the dungeon binds a level blueprint before the scene loads; entering any other dungeon ends a run in progress so its state cannot leak into an unrelated scene (see BiomeGenerator_OnEnable). `DungeonLayer` matters more than it looks: DataManager.GetDungeonLayer returns 0 for a minted location it doesn't recognise, NextDungeonLayer stores that, and IslandPiece.AvailableOnLayer has no case for 0 — every encounter reports "not available on this layer" and rooms generate with nothing in them. Hence the layer fix in EnterDungeon when !DungeonUseAllLayers.
- Captions (`CaptionTitle`): shown only after the transition ends and the player has control back, plus a beat — at biome-ready the room isn't built and the fade is still up; a caption landing on a black screen is shown to nobody. Realtime deadline because the transition runs at timeScale 0. Dungeons the player was *sent* to override captions to empty — the game already announces the destination on arrival.
- `OnBiomeReady` fires from BiomeGenerator.OnEnable in the dungeon's own scene, once per entry, before any room generates: the last point run state can be set up and the first at which leftover teardown from the departed scene can't kill it. `OnRoomGenerated` fires once per newly generated room for every connection type (SpawnEnemies only for True rooms).
- **AdoptIntoRoom is what makes rooms lock.** BiomeGenerator decides whether to close doors by asking the room itself — `CurrentRoom.generateRoom.GetComponentsInChildren<UnitObject>()` looking for Team2 — and COTL_API spawns at the scene root, invisible to that question: the room fills with monsters and the game believes it is empty. Parent spawns under the room's CustomTransform (worldPositionStays).
- **Never declare RoomCompleted during generation** (the old bug): it sets `CurrentRoom.Completed`, and BiomeGenerator.PlacePlayer wraps its ENTIRE arrival block — enemy count, walk-in, CloseAll — in `if (!CurrentRoom.Completed)`; the vanilla monsters the encounter system spawned were standing there the whole time, nothing ever asked. The honest answer to "should this room lock" (is anything alive in it?) only exists after the room is built and arrived in. RoomLockNet: wait for the walk-in to finish (PlacePlayer parks players InActive and walks them in; vanilla hangs its own CloseAll off the end of that walk, so acting earlier races the very code being backed up), wait ~0.5s realtime for spawn-in, then reconcile doors in both directions — CloseAll and RoomCompleted are idempotent, so re-running them on a room the game got right is safe. Read the door controllers, not the static DoorsOpen flag — that flag is only written by DoorUp/DoorDown and carries whatever the last room said. A token counter disarms a pending net when a new room or run supersedes it. The alive==0 && shut branch exists because an empty room is locked by vanilla anyway (doorsWillClose defaults true) with nothing to kill and no way out.
- COTL_API Spawn returns null while an enemy prefab is still async-loading (this mod's own build path) — tolerate a missing enemy rather than throw inside the room's generation coroutine.
- `WorldMapProgress.NotifyRunSucceeded()` in the final-room exit is the one success-only choke point every run type funnels through; death never reaches it — that is how a world-map node earns completion. `AbortTracking()` on dungeon entry: a new entry supersedes whatever run the map was watching; only the map itself re-arms tracking.
- CustomDungeonManager keys minted Location values by InternalName — every JSON dungeon shares a Location seed, so without distinct InternalNames the second dungeon mints the first's value and the Add throws.

### Custom enemies (APIHelper/CustomEnemyLoader.cs, CustomEnemyDressing.cs)

- The whole design: a JSON enemy names a vanilla prefab to **mimic** and keeps its brain wholesale — states, attacks, animations, death — then changes only what is changeable from outside (skeleton, health, size, any public number on the controller by name). The hard part of an enemy is behaviour and the game already wrote 248 of them. `EnemyController => null` is deliberate twice over: our own controller would replace the mimic's brain, and COTL_API's custom-controller spawn branch casts the mimic to EnemySwordsmanWolf, pinning every custom enemy to that one prefab. Same reason dressing happens AFTER spawn: COTL_API only applies spine overrides inside that branch.
- `Registered` is keyed by the minted Enemy enum (what a spawn is asked for); it is also the only registry of ours readable without reflection — COTL_API's own list is internal. CustomEnemyManager.Add throws on duplicate names; skip the enemy rather than take the whole load down.
- COTL_API instantiates at the scene root and custom rooms skip the scenery recycle sweep — so spawns and corpses followed the player through doors. TrackedSpawns is swept at the top of ChangeRoomRoutine while the departing room is still current. Corpses: SpawnDeadBodyOnDeath drops a separate DeadBodySliding prefab that inherits the enemy's parent — scene root for ours; vanilla corpses live under their room and are left alone. A per-room stash-and-restore (vanilla-style corpse persistence on revisit) was tried and never restored reliably — removed in favour of the simple sweep.
- RepairControllerReferences: when COTL_API swaps in a custom controller it copies every UnitObject field onto the new one then DESTROYS the original, but mimic helper components hold serialized refs to the corpse. Cower is the one that bites: its death-knockback coroutine's first statement is `AIScriptToDisable.enabled = false` on the destroyed controller, AFTER DestroyOnDeath is already off — the corpse never finishes dying and stands frozen. Repair only known offenders by type: a generic "re-point every UnitObject field" sweep would rewrite legitimate cross-unit references (BarrierEnemy.barrierPartner points at a DIFFERENT unit). Destroy is deferred so the stale value doesn't read as null this frame — re-point unconditionally anything not already the live controller (no-op for unswapped enemies).
- ApplySpine is unconditional — an enemy keeping its mimic's brain still wants its own skin. Skin with no skeleton override re-dresses the mimic's own skeleton (a follower-skinned enemy with no shipped art). A skin the skeleton lacks is a warning, not the exception SetSkin throws halfway through Initialize. FindSkeleton prefers the controller's own Spine field — a prefab can hold several skeletons (mount, weapon) and the field is the one the AI animates; SkeletonField is shared with the enemy tool (ghosts/thumbnails) via a memoised lookup.
- Tuning table: reflection by member name against the controller (which IS the UnitObject, so `maxSpeed` and `AttackWithinRange` are on the same object). Memoised per (type, member) including misses — a room of eight enemies with a six-entry table used to do ~100 uncached reflection walks; a typo asks once. Numbers only (JSON); a bool member takes 0/1. Unknown names are logged, never swallowed — a typo'd config is otherwise an enemy that quietly ignores half its file.
- Boss bar drives the game's own UIBossHUD (the bishops' bar) for an ordinary enemy — and must be taken down by hand: UIBossHUD.Update dereferences its boss per frame with no destroyed-guard, so a dead enemy leaving the bar up throws MissingReferenceException every frame for the rest of the run. Hide only if `boss == _health` still — with two alive, the first death would otherwise take the survivor's bar down. No HUD/Canvas in scene is non-fatal.

### Custom NPCs (APIHelper/CustomNpc.cs, CustomNpcLoader.cs, CustomNpcManager.cs)

- Modelled on COTL_API's CustomEnemy but non-combat, with its known traps fixed rather than copied: registries are PUBLIC and string-keyed (map blueprints store InternalName, so no unstable minted integer anywhere; nothing vanilla ever addresses an NPC); Add tolerates duplicates (last registration wins — a plugin reload should log, not throw halfway through Awake); the loaded mimic ASSET is never mutated (mutating it corrupts the vanilla prefab for the session — every per-instance change lives in Spawn); the spine override is applied unconditionally, not gated behind a controller type (for an NPC with no controller that would mean never).
- Default mimic is the lost-lamb ghost: the simplest standalone Spine NPC in the shipped catalog — one skeleton, no combat components, no room dependencies. DisplayName is registered as a localization term at load (config carries English text, not a term key).
- **Clone under an INACTIVE holder and strip before waking.** Mimic scripts key behaviour to save state — GhostNPC.Start turns the whole object off when its rescue conditions aren't met — and a deferred Destroy after a live Instantiate loses the race with Awake/Start. This is exactly why the editor's ghost previews (same holder pattern) always showed while the placed NPC vanished.
- StripMimicBrains: only Spine-namespace behaviours survive (the same allowlist as editor ghost previews). UnitObject goes first — it holds the Health reference, and a combat-component mimic would put the NPC in Health.team2 where room locks wait for it to die. Several DestroyImmediate passes: it refuses to remove a component that another component [RequireComponent]s, so dependents go first. Colliders/physics are stripped — no business on scenery that talks. All safe as DestroyImmediate because no Awake has run under the inactive holder.
- WakeBody: mimics can be authored hidden or ghost-faded — toggled-off children come back on, renderers regain full strength. The skeleton's own tint survives a data swap, so it is reset separately after the spine override. Idle animation looked up guarded — a missing name is a static pose, not an exception. MainSkeleton = first in hierarchy (no UnitObject left to ask; NPC prefabs never had the enemy Spine field).

### Structures, items, meals, tarots (APIHelper/CustomStructureLoader.cs, CustomItemLoader.cs, CustomMealLoader.cs, CustomTarotLoader.cs)

- Spine structures: the sprite is only the build-menu icon once a skeleton stands in the world — a missing one is a warning and a placeholder, not a dropped structure. The icon lives in the same folder as the atlas pages and must be excluded from texture auto-discovery, or it is loaded as an atlas page and the skeleton renders blank. StructureSpineConfig: every path optional (skeleton = the folder's one non-config .json, atlas = its one .atlas, pages = its .pngs minus the icon); SkeletonScale 0.005 matches the game's own skeleton import scale; Rotation absent = face the camera like the sprite would (fallback: the world's -60 tilt), set it to take that decision over (e.g. a prop lying flat: 0,0,0); HideSprite=false keeps a painted base under an animated skeleton; empty Animation holds the setup pose (what a static prop wants).
- Sprite/recipe getters on items, meals, structures and tarots are all cached: the build menu / cooking UI / tarot draw re-read them per redraw, and each call used to decode the PNG from disk into a fresh texture nothing ever destroyed.
- Loader<T></t> scans each subfolder for a single config.json — in our folder and in any other mod's CultTweaker bridge folder, so a mod can ship content for these loaders without shipping code.

### Mod content bridge (APIHelper/ModContentPaths.cs)

- Another mod hands content to our loaders by putting a `CultTweaker/<KnownFolderName>/...` tree beside its own files (e.g. BepInEx/plugins/SomeOtherMod/CultTweaker/CustomNpcs/TheirNpc/config.json). Nothing is registered or copied — the loaders simply read those folders too. Only reading is shared: everything this mod writes goes to its own folder, so a foreign mod's files are never edited in place and its updates can't be clobbered.
- Name collisions: OURS WINS with a warning (the player's own creations are never shadowed by an installed mod); between two foreign mods, first found wins. Bridge search depth is 3 under BepInEx/plugins (Thunderstore installs one folder per mod but some managers nest Author-Mod/plugins/...; an unbounded sweep of a large plugins folder is a cost paid every scan). Folder existence is re-tested per call rather than cached — a mod may create its folder after we first looked. Our own tree is excluded from the bridge scan (we'd find our own folders twice). An unreadable folder is one place with no content, not a failed scan.

### Custom color command (Commands/CustomColorCommand.cs)

- Rebuilds the follower summary menu in place: hides the traits/thoughts sections, retitles the header, and adds RGB/alpha/scale sliders plus costume selectors cloned from the settings menu's audio-slider template. Recolours the skeleton per body slot: ARM_LEFT_SKIN, LEG_LEFT_SKIN, LEG_RIGHT_SKIN, ARM_RIGHT_SKIN, HEAD_SKIN_BTM, HEAD_SKIN_BTM_BACK. Scale maps 0.1..5.0 across a 0..100 slider. Palworld special types are masked as "Unused_enumid" pending a later release; necklace type is a TODO.

### Base editor — the delta model (MapEditor/BaseSession.cs, BaseDelta.cs, BaseGround.cs)

- **The design:** a hub is authored from nothing (town room emptied, author's content is the whole of it); the base is the opposite — it already exists, the player lives in it, most of what stands there is their save. So nothing is cleared and nothing rebuilt: the editor opens on the base as it stands and every gesture is recorded as a *difference* (added / removed / moved), in a file of our own, one per save slot, re-applied on every arrival. **The rule the feature is built around: the game's save file is never written to on the editor's behalf** — where a change must exist at runtime (a moved building followers walk to), it is masked back out for the length of every save write (SaveMask) and repaired on the way back in (RepairSavedPositions).
- Slot = `SaveAndLoad.SAVE_SLOT % 10`: saving a DLC game shifts SAVE_SLOT by ten for one write, and a file named from that could never be found next session.
- Enter() deliberately does not travel — the base editor edits what is under the player's feet. WhyNot() reasons: a hub Busy in the same scene (a hub is the same scene wearing a different room — editing "the base" then would journal the hub's contents into the base's file and clear them on the way back); base room not standing (never trust GenerateRoom.Instance — that static is whichever of the scene's four rooms enabled last; see SceneRefs.RoomOverride / ClaimRoom). Sessions never survive a scene load; RetireOwnedBrains on the way out (the game clears brains only on quit/death/menu — a leftover brain is a building the base thinks it still has, pointing at dead objects); SaveMask.Forget likewise.
- **Identity of vanilla scenery:** a scene object has no id, its name is shared with every copy, and its hierarchy index changes with decoration rolls. Name + original position pins it (two objects of the same kind are never in the same place); MatchRadius 0.5 only absorbs JSON round-trip epsilon. That was still not enough: the base carries several "Sprite Shape" objects all at the origin, so a `Path` of names+sibling indices from the root was added — path first, name+position fallback for pre-path entries (which learn their path at next save).
- **EnsureContentRoot:** the base editor gets a container of its own, never the room's CustomTransform — the base ships that container already populated with the player's things, and "is it under the content root" is how the editor tells its work from theirs. Borrowing it told the editor everything was ours: nothing protected from deletion, nothing the player owned journaled.
- **Applying on arrival (BaseDelta.OnArrived):** runs whether or not the editor will open. Waits for the base to finish arriving (45s realtime deadline); bails if HubSession.Busy (a hub trip lands in the base then switches rooms — rebuilding into a room about to be emptied puts the author's work in the hub and destroys it). ClaimRoom before anything reads SceneRefs.Room. Journal applies in two passes with a 1.5s realtime gap — the base doesn't finish arriving at once (decorations pool in from a coroutine, furniture switches on later). A SceneIndex of every transform in the whole scene by name is built once per pass — the base's furniture is spread across scene roots (indoctrination ring, teleporter collision, land tiles) and a sweep from GenerateRoom finds none of it; a per-entry scene walk turns arrival into a stutter. Find() never guesses: a miss leaves scenery alone rather than moving the wrong thing.
- **Reshaped vanilla terrain needs its collider folded in on load:** the base's own sprite shapes ship with collision off; the editor's toggle path enables it and folds it into the room's outline, but the load path didn't — so reshaped ground came back as a solid body shoving the player around (toggling collision off/on in the editor "fixed" it because that path did the work). Bake one frame later: sprite-shape meshes generate at end of frame, and a collider baked before that captures the pre-edit outline.
- **Moves of the player's buildings** replay everything the game's MoveBuilding does minus the save write: grid unstamped/restamped, follower navigation data updated, registered with SaveMask. UpdateGraphBounds is called directly. It used to be called by name: it exists in the shipping game but not in the 1.5.15 reference assembly the project used to compile against, and the reflection carried a full-rescan fallback for games that lacked it. Only the DATA is updated on a drag (the transform is already where the author left it); snapping the transform to data first made buildings visibly jump one step behind every drag.
- **Scene-anchored buildings move as scenery:** the game binds those to their scene object by exact position equality on load and deletes the save entry outright when nothing stands at the recorded spot — their position is not a fact about them, it is the key they are found by, and this mod must not write it. Cost: followers still walk to where the game thinks it is. Old entries that wrote such positions (DontLoadMe) are dropped, not acted on.
- **Why RepairSavedPositions exists:** the loader's response to a position it dislikes is deletion, not correction — an entry whose grid cell another already claimed is removed (the loader walks the save backwards collecting cells), and an entry outside the ground polygon is removed; neither is recoverable or announced. SaveMask keeps moved positions out of the file on the way out; the repair pass assumes that failed and puts originals back before the loader looks, then the apply pass re-applies the move.
- **Structure lookup traps:** match on building id, not brain instance — the loader rebinds data to scene objects per arrival, so a brain fetched from data can be a different instance than the standing building holds (that mismatch left a shrine unmoved while everything claimed success). Collect ALL candidates: a shrine has a twin — two Structure components bound to the same save entry, one an artless stub; taking the first match moved the stub. Prefer the one with renderers (the one the player can see), then the one nearest the journaled position. FindStructure for an edited object searches itself/children first, parents only after — a drag takes children and leaves parents, so a Structure found upwards did not move. ReportWhatStayedBehind is diagnosis, not repair: a "moved" building that looks unmoved is either a pile (shrine on SHRINE_BASE, temple on TEMPLE_BASE — moving the top leaves the base) or art hanging off a different object.
- **Buildings placed by the editor (GiveBrain):** instantiating the prefab gets art and nothing else — components read Structure.Brain, so without one a bed can't be slept in, a plot can't be farmed. AddStructure's `save` flag only controls the list write; the brain is created either way — so the building works and the save never hears of it. Offset zeroed (CreateStructure jitters placements so rows don't look stamped; the editor placed exactly where asked). AddStructure ticks the objective system — right for a paid building, wrong for editor placements re-applied every arrival (quests would tick repeatedly), so `_placingOurOwn` (a depth, for future nesting) scopes a patch that drops those ticks. Bounds guessed at 1×1: bounds only decide grid coverage — guessing small risks overlap placement, not a broken building. Cells off the buildable grid are fine (nothing can be built there anyway). Own buildings dragged later: same data update the game makes, no journal entry (FollowOwnBuilding) — but it must happen, or followers walk to the old spot.
- **Builds by the player on mod-added ground (ModGroundStructures):** the game would delete these on load (outside the polygon it validates), so they are lifted out of its save as they are built and kept in our file. **A build site IS the building:** in the base a follower walks over with wood — between paying and that arrival the site is all there is; recording only finished buildings lost people their purchases. Record the ToBuildType; restore puts the site back for a follower to finish; one already standing when the player left is finished on the spot (they built it once). The site→building transition is two calls about one thing — the second replaces the first (matched by position, never by cell: off-grid buildings carry a sentinel cell, and matching by cell made them all look like the same building). Removal is matched on the data object, not cell — a site retiring as it becomes its building fires removal for a cell just re-recorded. FinishSiteAt runs a frame after placement (the region stamps cell/bounds after the creating call) and finds the site by position (the placement's return value is not the site; it arrives via AddStructure a moment later).
- **Journal bookkeeping:** NoteMoved skips gestures that ended where they began (resize, undo landing home) — an entry with equal positions is a line the apply pass looks up to do nothing. A twice-moved object must stay findable from where the game itself puts it (match on original OR current-session start). NoteRemoved on our own things just retires the brain. Reshaped vanilla ground is tracked as live controller references, not records — a drag commits per frame and writing forty spline points at 60Hz describes one gesture nobody asked for; the final shape is read once at save. IsMine (content-root/tool-tracked check) caches tool lookups per editor host — it's asked for every renderer in the base on every pick, and a LINQ scan thousands of times is not fine.
- **BaseGround — three readers must agree or new ground is ground in name only:** (1) collision + navigation graph; (2) BiomeBaseManager's GroundValidationCollider — what "in the base" means to followers (EnsureWithinBounds teleports outsiders to the town centre) and to the loader (PlaceStructures deletes entries outside it); (3) the build totem's placement region grid. The apply sequence is the game's own land-purchase sequence with our outlines appended: merge paths, re-derive the validation collider (read directly now; it used to be reached by field name because it postdates the 1.5.15 reference assembly, and the four lines are still copied rather than the method called, a field being a smaller dependency than a signature), rebuild collision/pathfinding, clear Follower.Points (the cached path every follower bounds-check reads — stale, it teleports anyone on new ground home), re-cut the grid. **Path 0 is never touched** — it is the base as shipped and every bounds check falls back to it; ours are only appended. Vanilla outline read once per visit AFTER the DLC land merge (bought land counts as vanilla). Reshaped vanilla pieces are collected beside mod shapes — an enlarged piece of own ground is new ground nothing else notices. Open-ended shapes are skipped (edge collider = a line, no inside). RequestRefresh is debounced 0.6s unscaled — each apply rebuilds the whole base's navigation graph. Paths are rewritten from scratch per apply, not appended (a moved/deleted shape must lose its old outline).
- **Grid over added ground:** the game's recursive fill walks from one seed; with the major DLC it first rebuilds the polygon from the base's cached outline (our appended ground is gone by the time it looks), and unreachable ground would be missed anyway. So TrimAddedPaths removes our outlines before the fill (budget spent wandering into added ground is budget vanilla tiles don't get — without DLC the polygon is the same object we appended to, and re-appending would grow it per build-menu open), then after the fill the outlines go back and every unclaimed lattice point inside them becomes a tile (ceiling 3000). Grid must be StructureInfo.Grid — a region without a brain hands back a throwaway list that would look like it worked. CreateDictionaryLookup is called explicitly: the region rebuilds it lazily on next lookup, but the placement loop asks before that. LatticeBounds converts all four corners (a world rect is a diamond in region space).
- IsOnBaseGround with nothing remembered yet returns true — refusing a question we couldn't ask would block every move. IsModGround decides ownership of every player build: on their ground = the game's business, untouched; on ours = the game would delete it, so ours to remember.
- SaveQuietly writes on the spot, not at session end — a build the player pays for is theirs from the moment they pay, and there is no reliable "end" to a base visit.

### Blueprint loading (MapEditor/BlueprintLoader.cs)

- Load phases: capture what clearing destroys → clear (then yield a frame — Destroy defers to end of frame, and rebuilding beside doomed objects corrupts the collision bake) → shapes (yield again: sprite-shape meshes generate deferred; bake against real outlines a frame later) → props → structures → doors (pads last — pad placement depends on final door position; culling stays suspended: a door moved out of its culling area is deactivated the moment culling resumes) → enemies → NPCs → podiums → collision rebuild → build totem (after collision, because the totem's grid is cut from the room's finished outline) → CustomRoomPatches.Mark (stops vanilla re-entry re-rolling decorations) → backdrop (derived state: never saved, cleared, recreated exactly once) → strip editor overlays (no editor visual may survive into play) → lighting/weather (values, not objects — a blueprint that never set them leaves the biome alone) → LevelPlayback.OnContentReady (cover comes off before the walk-in — doors settling is meant to be watched) → MarkSaved (the room now IS the file).
- Undo history is cleared on load — everything it referred to was destroyed. Clear preserves CustomTransform placements the same way the clear tool does; a load must not duplicate them. Parked (KeepHolder) objects may need reactivating. Strip Instantiate's "(Clone)" suffix or the next save treats the object as a runtime spawn. Old terrain visuals die with the terrain (parking them hoards geometry). HP bars are siblings of their unit and survive it — destroy them; ShowHPBar re-instantiates on demand. Island pieces re-register with the generator or composite maintenance misses them. Each disconnected floor region is its own composite path (the player cannot cross between them). ObjectPool positions relative to the parent; blueprints store world coordinates — set position after reparent.
- Walk-in: vanilla distance is doorway + 7.3 units in, snapped to walkable so a short authored floor never wedges the player. Door.OnTriggerEnter2D leaves player colliders off — restore them as vanilla does. An Entrance-typed door turns solid after entry (soft-lock guard); re-armed only once the player demonstrably steps away. Input maps are rebound after entry or only movement survives. Vanilla's DelayActivateRoom (0.5s) gates "room entered". Level playback prefers entering through the door opposite the one just used; some rooms have no entrance-typed door at all.
- Hub totem rebuild: the totem is part of the map (author placed it); what the player built through it belongs to their save slot, restored from HubStructureStore only when the hub is being *played* (the editor sweeps the room anyway). Begin() the store BEFORE standing the totem up — opening the store retires the last hub's buildings and would sweep this totem too. Wait a frame after giving the region its brain (tiles are read a frame later); RestorePaths after the buildings (floor tiles ask the grid which cell a point falls in, and the grid is only finished once built on).

### Dungeon maps (MapEditor/CTDungeonMap.cs, DungeonMapBuilder.cs, DungeonMapPlayback.cs, CTMapDungeon.cs)

- Authored form is free positions + a graph (like the world map); the game addresses nodes by integer Map.Point and draws at point*300, so the grid is *derived* on the way in (Layout): nodes gather into rows by height (LayerBand 70 ≈ half a node, measured from the lowest node in the row so a row can't creep upward), rows numbered bottom-up, columns left-right — deterministic, so playback re-derives instead of storing. Node identity is a stable string Id (links name it; a moved node keeps it); NodeType stored by enum NAME (a stored int silently shifts if the game renumbers). Legacy grid maps (Layers/X/Y/cell links) are migrated once in memory (idempotent; logged once per map per session — every folder scan re-migrates until a save rewrites the file).
- Validation rules (the renderer and run impose them): a dangling link is a crash (renderer never null-checks a link's far end); GetFirstNode() is a .First() — leftmost node of the bottom row starts the run, a second node down there is unreachable; a one-row map is over at once (run ends on reaching the top row); a node with no connections is silently skipped by the renderer; unreachable branches are drawn but unenterable (player only moves along outgoing links). Advisories warn, don't refuse: a boss node with no level bound generates an ordinary floor (the boss-fight flag comes from save data a minted location lacks); a node type without a blueprint in *this* scene's config may exist in the target dungeon's config — Preview builds against whatever is loaded here.
- Build: nodes added in layout order (bottom row first, left→right — GetFirstNode's .First() depends on it); `Hidden = false` explicitly (the Node constructor hides one node in ten at random); both link directions written from the authored list (renderer walks outgoing, traversal walks incoming, they must agree). InstallMap: CurrentMap has a private setter; MapGenerated=true stops ShowMap generating a fresh map over ours.
- Playback: level bindings are keyed by the derived grid point (the game's Node objects carry nothing of ours). `_built` reference-compare tells "still mine" from "a scene reload rebuilt MapManager" — rebuild the graph on first exit after entry. The biome's own NumberOfRooms is saved and restored when a node without a level is entered (it's a field on the scene's BiomeGenerator — a level's length would stick); forgotten, not restored, on scene exit. OnNodeEntered runs after vanilla's node-entry setup but before the queued generation (Regenerate defers into an MMTransition callback); acts on OUR map only — a foreign node reads as "no level" and would end the run. OverrideRandomWalk=false for level nodes (non-floor node types are a single fixed room — would show only the level's entrance). ResetRoomHandoff: the map is not a door, so nothing else resets the hand-off the room hook reads. Top-layer node = run over; empty path = the first exit of a one-layer map — finish rather than open a map with nowhere to go. UseMap remembers the map name on entry because by the time the exit door asks, the thing that knew is gone.
- CTMapDungeon: every saved map registers one; DrivesLevelPlayback (start node's level binds before scene load; the entry guard must not undo it); bind the start node's level in OnBiomeReady, NOT EnterDungeon — a level run is static state and anything between button press and new scene can end it. SpawnEnemies is empty: node blueprints carry their own enemies at exact positions. A minted FollowerLocation cannot be handed back — re-registering keeps the slot and refreshes the graph, which is what lets Save make a map enterable without a restart.

### Levels (MapEditor/CTLevelBlueprint.cs, CTLevelDungeon.cs)

- Room sides stored as ConnectionTypes NAMES ("False"=wall etc.) so blueprints stay readable and survive enum growth. VanillaRoom empty = an ordinary generated room a node blueprint is pasted onto; entrance/end-of-floor rooms are named by ROLE (BiomeGenerator.EntranceRoomPath / EndOfFloorRoomPath are per-biome, so the same level works in any dungeon). AuthoredLayout=true replaces vanilla's random walk with the authored grid (legacy blueprints deserialize false and keep old behaviour); sides facing open grid are Wall / WayIn / WayOut, kept consistent by LevelLayout.Normalize.
- **NumRooms trap:** CreateRandomWalk seeds a room BEFORE its loop then adds NumberOfRooms more, and PlaceEntranceAndExit appends the end-of-floor room — a floor arrives at NumberOfRooms+2. Passing the level's count straight through made a two-room level generate four. The two extras aren't padding — they are the arrival room and the way out, which a random-walk level carries as its first/last entries (LevelTool.EnsureEndRooms) — so subtract exactly those. Never below one: PlaceEntranceAndExit picks entrance/exit from rooms with exactly one connection; a lone room has none → null deref. Authored layouts report their real length (the walk never runs). Seed 0 = fresh per run; else deterministic pool picks.
- CTLevelDungeon: SpawnEnemies empty (node blueprints carry their own; random spawns would fight them); DungeonMapPlayback.Clear() on entry — a map left installed by Test Map would turn this level's exit into a node picker.

### Menu presets and world map files (MapEditor/CTMenuPreset.cs, CTWorldMap.cs)

- **Everything in a menu preset is an offset or multiplier over what the game draws, never an absolute:** a fresh preset must mean "exactly vanilla", or a player changing one thing inherits whatever the numbers were on the authoring machine — and the menu moves between game versions. Blend is the game's own LerpPalette (menu already runs 0 with a second palette loaded — costs nothing); Background is the menu's dark-mode overlay, whose blend ignores image hue (strength only, no colour); OverrideClear is the camera clear colour (what the palette maps into the gold field), defaulting to the shipped cream; edition fields have no master switch — each field's neutral value means "the game's own". Stylizer loose grain fields belong to an older grain path this screen never draws — not offered.
- Folder conventions shared by presets, world maps, dungeon maps, levels: **the folder name is the identity**, not the name in the file. FolderFor = write side (always ours); FolderForRead = read side (ours, else another mod's — everything reading a map/preset's files goes through it). `Exists` = ours (the overwrite question); `Available` = ours or any mod's (the load question). FreeName uses Available, not Exists — a "new" button must not hand back a name that shadows another mod's. CopyArt: a preset/map IS its folder, so save-as brings the art (read side as source — adopting another mod's map for editing); config.json is skipped (Save writes it; copying would overwrite the map being saved); existing destination files are left alone. ToJson = "what Save would write" compared against the last write — beats a dirty flag a widget could forget to raise. Unknown node types etc. from a newer file are dropped with a warning, not a crash. EnsureRootFolder at startup so there's somewhere to drop art before any save. WorldNodeState declaration order is cascade priority — a state never downgrades.

### Custom room re-entry protection (MapEditor/CustomRoomPatches.cs)

- CustomDecorations (private field, reflection) is vanilla's own switch for "this room's scenery is not mine": OnDisable otherwise recycles every SceneryTransform child on exit — and ObjectPool.Recycle DESTROYS anything the pool didn't spawn. Authored props were thrown away on leaving, and revisits never re-run Generate so the blueprint never reapplied.
- Re-entering is not regeneration: the room object is switched off/on, OnEnable runs RegenerateDecorationsWithPool over a room it believes it generated — re-rolling trees/rocks/critters INSIDE the author's sprite shapes (the random-trees-on-revisit bug). The generation-window suppression in LevelPlayback can't catch it (the window is long closed by revisit) — the marker on the room answers instead. GeneratedDecorations is still set (BiomeGenerator blocks arrival until it's true). DisableDecorationsNearDoor is the same pass's tail: switches off ANY SceneryTransform child within 3 units of a door — in a custom room that's the author's prop. CreateBackgroundSpriteShape: vanilla never removes old backdrops — a custom room gained one per visit. A real Generate un-marks the room (vanilla again; the pool wants its scenery back).
- OnEnable prefix prunes destroyed entries from room.Pieces — a later room swap can destroy loader-registered islands, and vanilla's decoration coroutine dies on the first destroyed entry.

### Shape profiles (MapEditor/CustomShapeProfiles.cs)

- Runtime-built SpriteShape assets need `HideFlags.DontUnloadUnusedAsset` — the game runs Resources.UnloadUnusedAssets on every room change and a collected asset blanks every shape using it. The game's SpriteShape parameterless constructors do NOT initialize the sprite lists — `range.sprites ??= []` or Add throws NullReferenceException. 9-slice borders in pixels so edge strips stretch middles, not caps; Corner takes UnityEngine.U2D.CornerType names.

### Enemy thumbnails (MapEditor/EnemyThumbnails.cs)

- 256px source cells drawn at 88px: hover blows a thumbnail up ~300 units and a 96px bake stretched 3×. 4 columns per page (1024² pages, not 1280) — small pages keep each progressive Texture2D.Apply cheap. One render every 2 frames: invisible as a hitch, ~150 finish while the user is still looking. Layer isolation via OffscreenLayer (the game's own bake-camera trick); staging position (5000,5000) is only the fallback when no layer is spare. Camera aspect must be set to 1 explicitly AFTER the target texture — a fresh camera starts on the screen's aspect and squeezes skeletons horizontally. Camera disabled, rendered by hand.
- MakeUnlit swaps every renderer onto an unlit copy of its own material (room lighting has no say): copy material first THEN swap shader, so shared properties (_MainTex) carry over by name; skeletons get Spine/Skeleton, sprites get Sprites/Default (both known in this build). sharedMaterials returns a fresh array each call — a safe record. `restore` list exists for callers working on a live object (selection preview); the thumbnail rig photographs a throwaway.
- Subjects instantiate INACTIVE with skeleton pre-assigned so Awake runs once with data rather than once empty; prefab transform scale is part of how the enemy looks (wildly different authoring scales); at timeScale 0 nothing ticks — pose and mesh pushed by hand (Initialize/Update(0)). Mimic prefabs carry extra skeleton renderers for ghost/afterimage effects — they double-expose; strip them. Ghost fallback (non-Spine enemies) restores full renderer strength (ghosts are faded for cursor use). Undrawn atlas slots must be cleared to transparent, not allocation garbage. CancelPending on grid rebuild — pending cells no longer exist. Orthographic size = extents*1.02 (subject fills its tile).

### Hub sessions (MapEditor/HubSession.cs, HubCurtain.cs)

- Woolhaven is not a scene: it is a GenerateRoom ("DLC_ShrineRoom") inside Base Biome 1, switched on by BiomeBaseManager.ActivateDLCShrineRoom() (which switches the base room and church off). Enabling it makes it GenerateRoom.Instance — which is what the whole map editor works on, so once the room is up every tool, the blueprint loader and the clear sweeps apply unchanged. Authoring and playing are the same three steps: get to the room, strip it, then hand it to the editor or rebuild the saved blueprint. Nothing is written back to the scene — leaving is a reload of Base Biome 1 and the town returns untouched.
- `Busy` = active, preparing, or a trip pending — a trip to a hub lands in the base first, and for a second or two the base looks exactly like a base nobody is leaving. A PENDING trip survives scene loads until the base itself arrives (a transition/loading scene can land first — ending the request on the first non-base scene made "travel to hub from a dungeon" do nothing until pressed twice); it times out after 90s (a death warp or menu trip must not build a hub around some later arrival).
- **The base's own arrival is waited out, never held:** holding its transition was tried and reverted — the arrival sets the player up (state, camera, animation) as part of running, and a hub built behind a held cover inherited a half-finished, invisible, immovable player. HubCurtain instead covers the screen with pure UI (sorting 5100, above the editor's 5000–5001; blocksRaycasts=false — it covers, it does not take clicks; 45s max — a black screen with no way out is worse than an ugly arrival; unscaled fades). It wears the game's own loading corner — MMTransition's spinning crown cloned whole, the text built fresh (a cloned label can carry a localizer that overwrites the message) wearing the vanilla font/material; ForceFifty because OnEnable clears the fill each time.
- Prepare order and why: ShapeTool.PrepareForLoad BEFORE the sweep (it clones a live sprite shape as its terrain template, and the only live ones belong to the room about to be emptied); CaptureArrivalStart before the sweep takes the transform it is measured from; ParkPlayersOutsideRoom (players step to the scene root — anything holding them would be protected from the sweep, i.e. a piece of town left standing; moving the player INTO the room before it is emptied hands them to the sweep, which destroyed player + F7 panel); EnsureContentRoot AFTER the sweep (the town room is hand-authored and has no CustomTransform — why every tool had nothing to build into; created earlier it would be swept). **The check that must never be skipped:** verify SceneRefs.Room really is DLC_ShrineRoom before clearing — everything clears GenerateRoom.Instance, and until the handover that is the player's base; an emptied base is not something undo can fix. Yield after ActivateDLCShrineRoom (OnEnable runs nav rescan/decoration coroutines; clearing on top destroys objects mid-rebuild); yield after the sweep (Destroy defers to end of frame; building on doomed objects bakes against them).
- AdoptPlayers: the base's units hang off the base room, and raising the town room switches that room — and the player — off (an object under an inactive parent is not active however often it's told to be); the character vanishes mid-animation and stops taking input. Re-host players under the Woolhaven unit layer inside the room that stays on. Snap only a NUDGE onto nearby ground — the navigation graph still holds the base's walkable area, and the nearest node to the town room was one of those: the snap was carrying the player across the map.
- ResetTownCollision: the town bakes buildings into composite colliders (sceneryCollider + room), and a composite keeps the geometry it last generated even after the sweep destroys what it was baked from — the emptied town stayed solid where houses used to be, shoving the player out of the hub's floor. Regenerate with nothing left. The town has no room composite of its own (buildings carry baked colliders) — one is built (GeometryType.Outlines, as dungeon rooms use) so editor shapes become floor-with-walls, not solid blocks. SuppressTown: Woolhaven's controller keeps driving shops/boards/animals at objects about to stop existing.
- Walk-in (ArriveAtSpawn): the player is put down short of the mark while the screen is still black (the cut happens where nobody sees it), screen returns, lamb walks in — the town's own arrival (GoToAndStop, IdleOnEnd), 3.5 units (vanilla town walks 8; a hub floor is only as big as drawn). GoToAndStop always ends (maxDuration, or a second of standing still, forcePositionOnTimeout leaves the player on the mark — the first walk-in attempt lacked that and left an immovable player). **The walk only plays when the graph covers both ends** (GroundUnder): A* on a graph that hasn't caught up hands back a path starting elsewhere — the walk heads the wrong way, bail-outs fire, and forcePositionOnTimeout snaps the player across the room (the "teleported mid-walk" report). Mid-scan the graph answers with exceptions — that is "no" with extra steps. Any walk the base started is called off first (it would drag the player back out). Trigger firing is muted for the walk + 2s — an entry mid-walk is the session carrying the player, and a control-locking sequence fired then wedges into the walk's own end; a sequence on the spawn trigger fires the moment the mute lifts. Wherever the walk ended, the arrival ends on the mark (belt with braces).
- HoldControl watchdog: started LAST (during preparation the player is legitimately mid-arrival). Runs only until the first confirmed sight of a free, controllable player (1.5s grace, 1.25s settle) — from that moment anything parking the player (NPC conversation, F7 panel, trigger cutscene) is doing it on purpose; a watchdog still running yanked the player out of dialogue mid-sentence. Not while the editor holds the room (timeScale 0 on purpose — a stalled animation is not a stuck one). A walk-in that never reached its mark holds the state and swallows input — call it off before a state means anything. Reported once per episode; acted on every tick until it takes.
- FinishArrival: colliders on, conversation lock released, camera re-aimed and snapped (it is still watching the switched-off room the base arrival left it on — looks exactly like "no player"), input maps rebound (without that only movement survives). It deliberately does NOT set the player's state — the walk-in ends itself, and forcing a state on a running animation is a fight the animation wins one frame at a time. "The player is gone" has meant a live object with a switched-off skeleton at least as often as a destroyed one.
- The hub record: a hub is a CTLevelBlueprint with IsHub set — a name plus one room naming the blueprint that dresses it; the level tier already carries saving/listing/world-map picking, so a hub stays one of those rather than a fourth file kind. BlueprintNames() compares SANITIZED names (the record stores what the author typed; LoadByName sanitizes before disk — compare what the loader would compare); it exists so room pickers don't offer a town where a dungeon room belongs. The editor host is normally dungeon-scoped; a hub session is one of the two times it belongs in the base (Ensure), and it goes away with the session.

### Hub build totem (MapEditor/HubBuildTotem.cs, HubBuildRegion.cs, HubStructureStore.cs)

- The totem is a copy of the base's own — possible because the vanilla one is a scene object, not a saved structure (nothing in the save refers to it). **Only the totem is copied; the region is built from nothing:** "Placement Region" in this scene is the object the base's whole build area hangs off — ground sprite shape, bushes, repair interactions — and copying it copied a piece of town into the hub (pale slab, per-frame nulls from repair scripts). Copying halves separately is no better: Unity only re-points references when both ends are inside the same copy. A region is really a polygon, a grid and a few prefab refs — all settable by hand. CopyFields copies NAMED fields only — a blanket copy would share the live grid-lookup dictionary with the town's region, the two writing over each other's tiles.
- FindSource must skip our own copies — and the GHOST is the dangerous one: it is a whole totem with every component switched off standing at the cursor, and a copy of it looks perfect and is deaf (a disabled interaction never runs). HubTotemCopy marks everything we make, the marker added BEFORE anything else; a ghost's region must be disarmed before it wakes or it claims PlacementRegion.Instance. Skip prefabs/assets (scene.IsValid), skip disabled components (the ghost's tell — read the component flag, not the object's activeSelf: the base totem's OBJECT is off the whole time a hub is open, which is why the search must include inactive objects); the SetAsInstance flag marks the base's own.
- Region-from-nothing details: the grid's lattice lives in the region transform's local space, so its rotation IS the grid's rotation — the game's tiles sit on a 45° diagonal, and an upright region puts every tile at an angle to the art. "ObstructionColldiers" (sic) is a list with no initialiser, walked by the pathfinding update without a null check, so it must be a list before the region is built. Set directly now; it used to be set by name because it is absent from the 1.5.15 reference assembly. PlaceWeeds/rubble off (those dress the town's grid on first creation). MaxTileCount int.MaxValue (vanilla's 50 is a brake on its recursive fill, not a real limit — our fill is a bounded scan). GiveRegionData: a region reads its grid off the Structure it sits on, so CreateStructure with save:false (the totem lives in our hub file, the game's save is never told); assigning the Brain is what the region listens for; `_ = region.structureBrain;` must be read once — the build path uses the cached reference directly without the lazy getter, so an unasked totem throws on first use. Purchased=true (the author placed it; nothing to buy). Adopt into HubStructureStore — the brain is in the game's RUNTIME list of what stands in the town room, and that list outlives the scene.
- The fill (HubBuildRegion): vanilla's flood fill is seeded once (ground the seed can't walk to is unbuildable — a hub is often several islands) and, with the major DLC, rebuilds the polygon from the base's cached outline every run (throwing the hub's shape away a frame after we set it). Ours: same lattice, same inside test (vanilla's kept to the letter — a lattice point counts when the polygon's ClosestPoint to it is itself; Vector2 == does the approximate compare), but seeded nowhere and bounded by the polygon — every point inside becomes a tile, islands included. A prefix on CreateFloodFill routes the region here. Ceiling 4000 (a hub the size of the base is a few hundred tiles). The polygon collider must be ENABLED before asking ClosestPoint — a disabled collider answers with its own transform position, making every tile look valid. LatticeBounds measures from the outline just laid down, not Collider2D.bounds — the physics world hasn't been told about new paths on the frame they're set, and answers an empty box for a switched-off object; a world rect is a diamond in region space, so convert all four corners.
- **RefreshOccupancy vs RebuildGrid:** opening the build menu needs "which cells are taken" — for a long time it called RebuildGrid, which re-cut the lattice from the ground outline every time; that outline shifts subtly as colliders join/leave the composite, so cells could come back different from the ones the player built on — their buildings stood on cells that no longer existed, and the demolish tool (which picks by grid stamp, not by what's under the cursor) found nothing. The lattice is cut once when the totem goes up; a hub is stood up fresh per visit, the right cadence for ground that changes only between visits. A fresh grid is an empty grid — re-stamp standing structures before anything is placed (vanilla's own land-purchase recipe) or the next build lands on top of the last; a structure whose recorded cell isn't in this grid marks nothing (built against a different region or before the ground moved).
- AimPlacementAtTotem: the totem carries a hard-coded open-at spot (-7.6, -5.61) — a place in the town's buildable ground; in a hub the cursor (and the chasing camera) went off into the void, and since the cursor snaps only within ~1.5 units of a tile, it could never place anything. Aim at the totem; never exactly zero (the totem reads zero as "unset" and falls back to the town's spot).
- RegisterWithInteractor: an interaction files itself with the interactor's spatial index from a coroutine that opens with `yield return new WaitForSeconds(0.1f)` — SCALED time, and the editor runs at timeScale 0, so a totem placed in the editor was wired correctly and completely unknown to the proximity system (loading the map worked because the clock was running). File directly and STOP the waiting coroutine — its call appends without looking, and a double registration leaves a dead index entry when the totem is destroyed (hubs are entered more than once).
- HideVolumetrics: the pale slab under a copied totem — four pieces of 3D geometry (volumetric light cone, cylinder, two decal meshes) that only look like anything inside the base's stencil-lighting rig; elsewhere they render as plain white geometry (same cause as the white squares on F7 portraits). Switched off, not made to work. Spine draws through MeshRenderer too — never touch skeletons. IsolateReferences: the totem switches referenced objects on/off the moment it wakes (lit/unlit states, tutorial sign, "new building" badge), and refs pointing outside the copy still point at the ORIGINAL — a hub totem reaching across the world to toggle the town's totem; each gets a stub. newBuildingAnimator is played on waking and a null one throws before the rest of the method runs. MissingDependency(): PlacementRegion.PlayRoutine reaches for base-scene singletons without checking — a null inside a coroutine dies quietly with the player locked out of input, so refuse the interaction and say why. ReportReadiness waits 1.5 REAL seconds (a scaled wait is exactly the trap that stopped registration).
- HubStructureStore: **Woolhaven's real save list must never be used** — it is shared by every hub AND the actual Woolhaven, the game prunes it on load (entries whose grid cell is taken are deleted outright — what two hubs sharing a list would do to each other), and it is the player's save (a bug costs their town, not their hub). The vanilla flow runs in full — grid, cost, build — and the moment the game hands the building to its save list, the patch takes it back out and records it here (per save slot, slot = SAVE_SLOT % 10 for the DLC shift). Build sites are placeholders — only what they become is recorded (Adopt skips them; Removed fires for the site retiring into its building, hence tolerating unrecorded data). Restore is free of charge (paid when first placed), through the region's own placement call so the grid is stamped exactly as a fresh build; records whose cell no longer exists (ground moved) drop their building rather than persist unplaceable. World position is preferred over cell in records — off-grid buildings carry the sentinel cell (-2147483647²), which no grid lookup can resolve; cell is the fallback for old records. Paths/floor tiles are mirrored wholesale from the region's own data list (short, changes only on lay/lift); the drawing half is a private method on PathTileManager called by name — the same one the game uses to restore the town's paths. Saved on every change: a hub is left by scene reload and there is no reliable "end". TearDown retires brains (the game never clears them on scene change — they'd still be listed against Woolhaven next visit, pointing at destroyed buildings) and restores the town's build region (leaving mid-placement left it unfindable).
- AttachPathTiles: floor decorations (paths, planks, tiled floors) are not structures — PathTileManager draws them into tilemaps, and it binds itself in Awake to GetComponentInParent<PlacementRegion></placementregion>(). It lives under the TOWN's region, so it asked the town's region to turn a hub world position into one of its cells — always "no such cell", tile silently dropped. A hub gets its own manager parented under its own region (same localPosition as the original under the town's, so tile grid lines up with build grid), carrying its own tilemaps cleared of the town's paths, built inside a switched-off holder so Awake claims the singleton with the right region already in place.

### Host interface (MapEditor/IMapEditorTool.cs)

- IMapEditorHost is what modal dialogs need (input block, status line, coroutine runner) plus what the shared WIDGETS need. MapEditorUI used to name the two hosts outright (MapEditorHover reached for RuntimeMapEditor.Active or WorldMapEditor.Instance; AttachButton the same) — so a third host got plates that lit up and said nothing, and dropdowns whose blocker rects went nowhere. Routing through the attached host means the next editor works by being built, not by being added to a list.
- IMapEditorEscapeHandler: Escape closes the editor, so the host asks the active tool first — a key meaning "back out" must back out of the innermost thing first. IMapEditorScreenTool: a tool owning the whole screen (dungeon map view); host chrome stands down, camera keys stop panning, wheel stops switching tools, Ctrl+S saves what's on screen; ScreenHoverStatus exists because the host's status bar is off behind the screen; ScreenStepBack lets F4 close the tool's screen (and its prompts) before the editor, so unsaved work isn't thrown away on the way past.

### Authored level layouts (MapEditor/LevelLayout.cs)

- Vanilla lays floors by random walk (CreateRandomWalk) and the author controls nothing about the shape. The game's own switch for authored shapes is `OverrideRandomWalk` — not a back door (MapManager.EnterNode sets it for non-random nodes; the DLC intro dungeon runs on it); raising it makes every procedural stage stand down (PlaceEntranceAndExit, GetCriticalPath, PlaceLockAndKey, PlaceStoryRooms, PlaceDynamic/FixedCustomRooms all check it and return). We build the room graph ourselves and raise the flag; room shape and doors follow from connection types alone (Generate(seed,N,E,S,W)). **Not** vanilla's `OverrideRooms` list: that marks every room custom and loads prefabs by path — right for fixed prefab rooms, wrong here (an authored level wants ordinary generated rooms, islands and doors, that blueprints are pasted onto — the rooms InstantiatePrefabs makes for IsCustom==false).
- Door rules re-applied after every edit (Normalize), not defended per call site: a side facing a neighbour is Door or Wall (a way in/out there would open onto the next room's floor); a side facing open grid is Wall/WayIn/WayOut (a plain Door there is a doorway onto open water — and GenerateRoom would build one); a door is agreed by both rooms ("if either says door, both do"). Consequence: closing a door from one side can never take (the neighbour still says door) — every deliberate change goes through SetShared, which sets both sides before Normalize is asked. WireRoom (on place/move) opens doors to everything the room touches — only ITS sides, so a door deliberately closed elsewhere isn't re-opened by dragging a third room.
- WayIn/WayOut are DERIVED (first room / last room, on a side facing open grid, south/north preferred as vanilla does): placed by hand, a level could be dragged into a shape with two ways in or none and only find out at the badge. A one-room level is both ends at once.
- Validation: two rooms on one cell is the one thing the generator cannot survive (BiomeRoom.GetRoom returns the first; the second is built, never reachable, never freed). Missing entrance = a room walled in by its own neighbours. Unreachable rooms are the failure this grid exists to make visible. Prefab rooms out of their usual place, or unbound pools, are advisories, not refusals (a level with nothing bound plays as vanilla rooms — legitimate).
- Build: Rooms list must be cleared first — vanilla's own first act, and not optional (BiomeRoom registers itself in a static list on construction; leftovers from the previous floor still answer GetRoom). Per-room seeds derive from the biome's (same seed rebuilds the same rooms; decoration/encounter passes read them). Vanilla prefab rooms: IsCustom set so InstantiatePrefabs loads by path, Generated stays false so the prefab's GenerateRoom still builds island and doors around what it brought (how vanilla places both). `_P2` suffix is vanilla's second-layer swap. RoomEntrance/StartX/StartY must be set (what PlaceEntranceAndExit would have set — Door and Interaction_BiomeDoor dereference RoomEntrance without checking). Build failure logs and falls back to the vanilla walk — a throw would take the generation coroutine down and leave a black room.
- The hook is CreateRandomWalk's prefix, deliberately NOT the dungeon's OnBiomeReady: a floor entered from a dungeon map is regenerated in place (EnterNode → Regenerate) — GenerateRoutine runs again without the biome being re-enabled, and CreateRandomWalk is its first act on both paths. MiniMap fix: OverrideRandomWalk hides the minimap (right for vanilla's one-room nodes, wrong for an authored floor) — lower the flag for the length of OnBiomeGenerated only; everything else still needs it raised.

### Level playback (MapEditor/LevelPlayback.cs)

- An apply token invalidates stale room-apply routines (rapid door use outruns a slow apply); Stop() and newer applies release the transition hold. Stop logs its caller via StackTrace (an early end is otherwise invisible until rooms later; two frames tell a deliberate stop from a patch). ClearContentSuppression drops the flag WITHOUT ending the run — a level run legitimately spans scene loads; "not in Dungeon1" is never by itself a reason to Stop. The lighting override is global — cleared on Stop, and re-based per room (fall back to what the room itself asks for).
- Rebuilds must stay strictly after ChangeRoomRoutine's fire-and-forget UnloadUnusedAssets (overlap crashes) — `_roomChangeTick` sequences that; the boot-time entrance has no ChangeRoomRoutine and must not wait for a signal that never comes (AwaitRoomChange). Boot-time entrance also has no CurrentRoom yet — record its slot so a revisit reuses it.
- Resolve() picks one node blueprint per room up front (a run never dead-ends on an empty pool) and validates BEFORE the scene loads — an authored grid is built inside the generation coroutine, where the only way to report a problem is a log line and a floor that quietly isn't the authored one. Rooms with VanillaRoom set resolve as vanilla and the apply leaves them alone (loading a blueprint over a prefab room clears exactly what it's there for); under a random walk the level carrying entrance/exit rooms at its ends is what stops the first blueprint being dealt onto the weapon podiums. StartForMapNode does NOT EnterDungeon — EnterNode already queued a Regenerate in place; re-entering throws that run away. Slot assignment: authored levels know exactly which room is in which cell (SlotFor); random walks use discovery order — first room is Entrance, the room owning the exit connection is Exit wherever the walk put it, middle slots cycle (a floor longer than the level is always possible — vanilla's arithmetic — and cycling deals blueprints round again instead of stacking extras on one).
- Fade handling: hold the fade black until the blueprint is in, or the vanilla room shows first. The world keeps RUNNING behind the black (the game's generation won't finish while its clock is stopped) — fine for the few hundred ms of building, not after: the walk-in was playing out under the cover too, and the first thing a room did was hit the player with something they could hear but not see. Vanilla lifts the fade ONTO the walk-in; OnContentReady (called by the loader — only it knows when the room stopped changing) does the same. ReleaseHold: StopCurrentTransition leaves IsPlaying stuck true for an orphaned coroutine; if the fade-out that restores time never ran, timeScale is put back by hand. Orphaned holds/suppression from a previous scene are cleared in OnEditorReady (the entrance room's generation hook can fire before the host exists — deferred apply picked up there).
- **ResetRoomLocks:** RoomLockController.Completed is set true in RoomCompleted and never set false anywhere in the game. Vanilla gets away with it because controllers die with their room; ours don't always — the editor deactivates/reactivates doors, and the game pools room objects, so a controller can carry a completed flag into an unplayed room. The flag is not decorative: arrival reads RoomLockControllers[0], clears doorsWillClose when completed, and the whole arrival block sits in `if (!CurrentRoom.Completed)` — a room that starts life believing it's finished is never asked what's in it. We latch it early (authored no-enemy rooms unlock on arrival), so we unlatch it. After RoomCompleted, reseal dead-end barriers (it also opens those).
- LockIfContested: a check on the RESULT, not a fix to one cause — the arrival has several ways to decide a room is peaceful (UnitObject count, completed flag, a schedule this mod interrupts by holding the fade), and a custom dungeon can miss the lock through any of them; enemies in the room with doors open is unambiguously wrong, and CloseAll is idempotent. Waits 1.5s unscaled — locking during the walk-in is the game's job and it does it better (CloseAll moves anyone in a doorway back inside first). Read door controllers, not the static DoorsOpen flag (only DoorUp/DoorDown write it; it carries the last room's answer). Suppressed room content: SpawnDecorations skipped under suppression — hundreds of pooled spawns the rebuild would destroy anyway.

### Editor UI shared pieces (MapEditor/MapEditorConfirm.cs, MapEditorHistory.cs, MapEditorIcons.cs, MapEditorSearchRow.cs, MapEditorData.cs, LightingProfiles.cs)

- MapEditorConfirm is a strip along the bottom, not a modal: the question is asked ABOUT the room, and a full-screen modal would hide the thing being asked about (and stop the editor underneath). It was once outlined red ("answering wrongly costs something") — but the buttons inside wear the red ribbon, and a red frame around a red button reads as one shape; black now. The middle button appears only for three-way questions (discard/skip).
- History: bounded at 256 entries (a runaway loop must not grow it without limit). `Changed` fires on push and on an undo that actually undid something — the one hook covering every tool that places/removes/restores, used to know whether closing loses work. A load or full clear invalidates everything the stack refers to.
- Icons: null is a real cached answer ("no art on disk" must not re-hit the filesystem). Addressable prop icons: fake-null check (an unloaded addressable sprite must become a reload, not reach Image.sprite); handles are never released (releasing unloads the sprite the icon still draws); one dispatch per frame (visible fill rather than burst-then-stall); a session counter makes stale completions no-ops (and stops _inFlight going negative when the drain coroutine dies with its host); scene-sourced icons die with the scene, disk icons survive (flagged DontUnloadUnusedAsset). Other mods' structures aren't in the scene's placement list — COTL_API keeps their icon.
- SearchRow: text entry rides RuntimeMapEditor.PromptText (Input.inputString with the EventSystem suspended and the navigator locked) because a TMP_InputField on the editor's canvas loses the fight with Rewired's input module and the game's navigator. Debounced through a coroutine, NOT the tool's OnUpdate — OnUpdate doesn't run while a prompt is open (the editor returns from Update as soon as it reads the keystroke); without the delay every letter starts and cancels a screenful of async icon loads. Clicking a result confirms AND ends the search (picking shouldn't mean confirm-then-click). MaxResults 60 — cells are real objects with async icons; a thousand is a stall.
- Data model conventions: nullable Scale/SortingOrder on blueprint entries — a map saved before a field existed says nothing rather than zero, and readers treat null as "leave it" (so old maps keep looking the way they always did). RotationY flips a prop in this game's fixed view; Z tips it over — both stored so vanilla scenery round-trips. Weather kept by NAME (unknown = no weather, not a broken file); weather is dungeons and hubs only — the base keeps the game's seasons; Transition snaps by default (a room you walk into should already be raining). Trigger action Type names: unknown values dropped on load with a warning. Move actions fall back to the authored Position when the target can't be resolved. SavedNames() reads file names only — the file name IS the map name (Save sanitizes first), and a picker has no business deserializing whole rooms for a string already on the file. `Exists` = ours only (the overwrite question); `Available` = ours or another mod's (the load question) — saving under a shipped name writes our copy, never touches theirs. Podiums: ClearAllOnEquip true = vanilla choose-one-of-N; false = only the equipped podium is consumed.
- LightingProfiles: one flat JSON shared across maps; Save stores a CLONE (map edits must not rewrite the profile) with Enabled forced on (a profile saved while following the biome would otherwise apply as a no-op).

### Shared widget kit (MapEditor/MapEditorUI.cs)

- `_host` invariant is "most recently OPENED", not "most recently built": the room editor builds its UI once per scene at Awake and only opens later, so it re-asserts itself via Rehost each open. A host left behind after closing is harmless (only one is open at a time; closed hosts' hover/click-block methods no-op) — but a host that OPENS while another is remembered must take over or its widgets talk to the wrong one. CurrentHost answers "destroyed" in Unity's terms.
- RoundedPlate: 9-sliced rounded rectangle generated at runtime — the mod ships no art. Ribbon plates: the pause menu's button art where available, the rounded plate until then; ribbon idle colour is softer than the pause menu's full cult red — a dozen buttons in a panel at that red reads as a warning, not buttons; list rows get the grey cut (a row should read as part of the list, not a thing asking to be pressed). Sliced whenever the sprite has a border, whatever the source's type — the menu highlight draws its ribbon Simple+preserveAspect, and stretched Simple art smears.
- Fonts: settings-row face for panel text (matches the borrowed widgets), the pause menu's heavier face for buttons — the two are different faces in the game, and reading them as one flattens the row/button distinction. Headers built before the async heading font arrives are re-fonted on arrival (and the pending list is cleared when no font is coming, or it accumulates destroyed TMP_Texts). MMTextScaler captures font size in OnEnable — add it only after the size is final. Min font size 17 (the game's menus never go below).
- **FitLabelHeight and the childControlHeight=false trap:** rows are pinned to one line by ApplyRowLayout, so a wrapped label draws over the next widget. TMP measurement answers short if the font hasn't loaded or the mesh never generated. The fix that matters is the RECT: a VerticalLayoutGroup with childControlHeight=false reads each child's `sizeDelta.y` and never consults LayoutElement — a five-line label still counted as one row, and marking the parent dirty just re-ran a layout reading the wrong number. Set sizeDelta AND the LayoutElement (for parents that do control height).
- Buttons: `PreventMouseSelection = true` on every editor button — MMButton's OnPointerEnter hands the hovered button to UINavigatorNew as its current selectable, and the navigator polls Rewired's accept binding (E) from its own Update, outside the EventSystem entirely. So E anywhere fired whichever editor button the cursor last passed over — including while typing into a prompt — and suspending the EventSystem did nothing. Worse than "while hovered": OnPointerExit never clears the navigator's selectable, so the last button stayed armed indefinitely. Clicks are unaffected (OnPointerClick doesn't consult it); the skipped hover state is Unity's, which we don't use. Every button press calls CurrentHost?.BlockWorldClicks() — the editors poll the mouse themselves, and a widget click must not also read as a world click.
- Hover preview: a cell is ~60px of a prop that may be a hundred times that on the ground — half the catalog reads as the same brown smudge; hovering blows the icon up to 300px beside the options panel.

### Widget kit continued (MapEditorUI.cs tail, MapEditorWidgets.cs)

- Icon hover preview: drawn from the sprite the cell already holds (a texture draw, nothing else; no click needed unlike the ghost preview); placed and raised to front on EVERY show, not once at build — sibling order is draw order, and any full-screen tool opened after the preview was built is a later sibling drawing over it; the right-edge offset belongs to whichever screen is open (default clears the room editor's 420px panel; wider panels raise IconPreviewRightOffset while up). raycastTarget=false — the cursor is on the grid behind it.
- Toggles: the row needs a near-invisible Image because a Graphic is required to receive the click. MapEditorToggle keeps state separate from the button so tools can push a value without re-entering their own change handler; the vanilla settings toggle's setter animates on change, stays quiet on match, never calls back. Scrollbars: AutoHide (a rail against an unscrollable list is a control that does nothing; AutoHideAndExpandViewport would fight the hand-set viewport inset); 2px rail (the wide bar ate the last icon column), plain rectangle (rounding a six-pixel rail looks chewed). Dropdowns: the game's own caret sprite where available (the old typed arrow never drew — the game's font has no glyph at that code point); one dropdown open at a time (a stranded overlay keeps swallowing world clicks); `TransientUiOpen` exists because an open list stands OUTSIDE every registered blocker rect — surfaces polling their own clicks must close it rather than act on the click underneath; the floating list's catcher Image dims, absorbs the closing click, and blocks tools; opens downward, upward when there's no room. Emphasis: Action = what the panel is for; Quiet = undo/clear/back-out — a panel where every button is accented has no accent; MapEditorQuietArea marks list containers (twenty rows must not read as twenty invitations). MapEditorHover: a panel hidden under the cursor never gets its exit event — Apply(false) on disable; HoverTextProvider for reused grid cells; worst case a closed host's ShowHoverStatus no-ops (says nothing rather than the wrong thing).
- ScrollBox height is computed from row COUNT, never measured: a rebuild destroys rows with Object.Destroy (deferred to end of frame), so measuring in the same breath measures outgoing rows too; cells also fill in over frames and unmeasured rects answer zero. **SettleBox — why the LayoutElement is not enough:** ForceRebuildLayoutImmediate descends into a child only if the rect it stands on carries a layout controller, and a ScrollRect's Viewport carries only a RectMask2D — the walk stops dead there, the grid's own ContentSizeFitter is never re-measured from above, and the box sized against the stale number stays wrong (a box tall for a big group stayed tall for the small one after). Rebuild the inner column by name, a couple of frames later (destroyed cells actually gone, staggered fill stopped). **Scroll-to-top must happen AFTER the rebuild:** Clear() resets scroll while the box is empty (necessary), but the box then grows through the staggered fill, and a scroll view whose content gets taller does not keep its top — the first row drifts above the viewport (the clipped-first-row bug that "came good on the next selection"). Reflow() exists because a layout rebuilds when its contents change, not when it is MOVED: a reparented grid (the level tool lifts its grid out of the column so rebuilds can't destroy it) keeps the sizes from its old home. Reflow routes through the same settle as every other re-measure — two rebuilds racing is how the previous attempt became timing-dependent.
- Multi-select is held BY THE GRID (_multi), not re-applied by the caller: cells arrive over several frames, so a set applied once only reaches cells that existed at the time; AddCell consults it and a cell is born lit. Clear() leaves it alone — it is the tool's intent, not the view's state. ShowNames off by default (a catalog of hundreds is browsed by picture; the caption serves every cell) — on for short lists of things the author named (there the name IS the identity). Cell names are a strip OVER the picture (a square cell, one line — a slice costs less than shrinking every image). Hover preview reads the sprite at hover time, not capture — icons arrive async and the cell may have been empty at build.

### Name prompt (MapEditor/MapNamePrompt.cs)

- NameLimit 40 (map names are paths, not cult names — vanilla's 16 is too tight). `_openCount` is a count, not a flag: the trigger tool's screen text opens its second prompt from the first's confirm callback, so the second opens while the first is alive — the first's close then cleared the editor's modal flag under it, leaving the dialog on screen with WASD and tool shortcuts going through underneath ("controls go through while typing"). ResetModalState from the host's OnDestroy — a scene change kills TrackLifetime before it can close, and a latched static count bricks the next session's input. The modal destroys itself on hide and cancel reports nothing — close is detected by watching the object die, not a callback. MuteBackdrop: the cult-naming screen dresses in ritual red (full-screen backdrop + red confirm highlight) which glares over the editor's muted panels — every near-full-screen image pressed to translucent black (criterion is SIZE, not colour — black tint turns red art into silhouette), highlight swapped to the black sprite the controller ships; styling only, one frame after Show (stretch-anchored rects need a layout to measure), and the dialog works fine red if it misses. One extra frame before selecting the field so the navigator's own selection lands first and ours replaces it. showDisclaimer keeps the disclaimer object alive so its text can become the overwrite warning.

### Custom NPC behaviour and dialogue (MapEditor/Npc/*)

- CustomNpcInteraction: Interactor scans the static Interaction list by distance — no collider needed; base class handles prompt, bark closing, player capture. `IgnoreTutorial = true` is required or the Label is blanked pre-tutorial and the prompt never appears in a dungeon. base.OnInteract is required (closes barks, records the interacting player, plays confirm SFX). No dialogue = no prompt (scenery with an idle animation).
- NpcDialogue: localization registered once per NPC (one UpdateDictionary for all terms, not one per term); language arrays are sized when a term is created — a source whose language list grew since would index out of range. Line Animation empty falls back to TalkAnimation; Loop=false plays once then the game queues the default idle behind the one-shot. The JSON converter's CanWrite=false — reading is the whole job, and the default writer running back through the converter would recurse.
- NpcDialogueRunner: never start a conversation while MMConversation.isPlaying (a second underneath corrupts both). Response must be fully qualified — a legacy top-level Response class exists in the game assembly and it is the wrong one. Every Play passes CallOnConversationEnd:false, so EndConversation is the one true teardown — letterbox, camera and input stay in conversation mode until it runs. A lines-less choice hub is ended (Validate should have removed it).

### Off-screen rigs and prefab addressing (MapEditor/OffscreenLayer.cs, RoomChildPrefabs.cs, RoomSnapshot.cs)

- OffscreenLayer: one shared answer for the three rigs (player portraits, selection portrait, enemy thumbnails) that each used to search separately and disagree. Searched downward from 31 **all the way to 1, not to 8**: Unity's builtin block is 0–7 but index 3 is blank in every project (an old "Ignore Collision" slot the engine never reused) — when a game names every layer from 8 up, 3 is often the only one left. COTL 1.5.26 named its last high layer, and a search stopping at 8 concluded there was nothing. Never negative: worst case the default layer works because the rigs are kept out of shot by DISTANCE — isolation is the guarantee, the layer is only the mechanism. IsPrivate matters to the selection portrait, which photographs its subject where it stands.
- RoomChildPrefabs: NPCs and built-in-arena bosses (Narinder, the Guardians under "Death Cat Controller" in Boss Room Dungeon 1_6) have no addressable of their own — but a stable position inside a prefab that IS addressable, so the key is room key + child path. The important property: a child of a prefab ASSET instantiates on its own — Unity hands back that subtree without the room around it. Lifted out of the NPC tool so the enemy tool shares the same grammar and the two can't drift on what a key means.
- RoomSnapshot key resolution, three tiers shared by save and Select-tool readout (so the readout and the blueprint agree): (1) ObjectPool knows which prefab an instance came from — but the pool lookup is not free, so TryResolveKey is called on click, never per frame; (2) room's own lists; (3) catalog by filename — a name under two keys caches null ("ambiguous, unresolvable by name"). Island prefab resolution also tier 3: Resources.FindObjectsOfTypeAll excluding scene instances (a live instance is a copy of the room being cleared); cached per session (every room of a level wants the same prefab). Sweep exclusions: protected objects (doors, player, camera, room-lock logic, editor's own); nested sweeps take only "(Clone)" runtime spawns (prefab-authored children come back with their parent); standalone sprite shapes round-trip as spline data via the shape tool (shapes under island pieces stay eligible — runtime spawns recorded as props); lone UnitObject roots are EnemyTool's or the encounter's business (containers with enemy children stay eligible); trigger volumes round-trip through bp.Triggers (checked without the editor — the loader spawns them too); weapon podiums are the podium tool's.

### Room editor host (MapEditor/RuntimeMapEditor.cs)

- Context (Dungeon/Hub/Base) decides tool visibility: a hub is a safe town (nothing spawns, no podiums, no doors to a next room, not part of a graph); the base is all that plus not ours to replace — Clear and Load are the two gestures that would, so neither is offered (a base session only adds). Tools are still BUILT in every context (the loader and clear sweeps ask for them by type) — they just have no dock/wheel presence.
- Unsaved work is a COUNTER (_edits vs _savedEdits), not a file comparison: a room save is a multi-frame collection rewriting the blueprint from the live scene — running it to answer "dirty?" would be a save in all but name (the dungeon/world editors compare JSON because their maps are small data objects). The undo stack raises it for every push; tools that change the room without one (transforms, doors, lighting, shapes, clears) call MarkEdited themselves. **No baseline is taken at open, deliberately:** the count starts clean, so open-look-close asks nothing; re-baselining per open would make the editor FORGET — close an edited room with "Close anyway", reopen, close again, and it would go quietly though the room holds work no file has. Only save and load move the baseline.
- "Close anyway", never "Discard": nothing is reverted and nothing can be — the room is the live scene; closing only puts panels away, every edit still stands, playing the room plays the edited one. What goes unsaved is the FILE. "Discard" would promise an undo the editor cannot perform. `_closeAfterSave` closes only once the write actually lands — a blocked or cancelled save must leave the editor open with the work in it; "Save & close" under a name already on the title bar skips the quicksave's overwrite arming (the visible name IS the confirmation).
- Open path calls MapEditorUI.Rehost(this) — this editor attaches at Awake, not open, so if the world-map or menu editor ran in between, the shared widgets' static host still points at them. ShowHoverStatus guards `if (!_editing) return` — closed means closed; shared widgets may route here while another editor owns the screen.
- F4 in the base scene while Context==Dungeon is refused: the host outlives its session there (it applies base deltas on arrival, remains after a hub was played) and F4 must not become a side door into the editor — the base is edited from the F7 panel where the safety guards live. F4 with the confirm strip up dismisses the question rather than answering it. Escape steps back innermost-first: open dropdown → tool mode → tool's own screen (ScreenStepBack) → the editor with its unsaved-work guard. Handled BEFORE tool updates — a tool polling Escape itself would never see the key; tools that need it implement IMapEditorEscapeHandler.
- Ensure(): the plugin creates the host for dungeon scenes; hub/base sessions stand one up beside an already-standing room. OnEditorReady re-binds a level run (static state) to the new host. Teardown must clean: destroy the canvas GameObject (leaks one per dungeon entry otherwise), ClearSceneScopedCache (a stale cache hands out destroyed sprites), ResetModalState, unlock the navigator (a lock stranded by an interrupted prompt takes every menu in the game down until restart).
- Pause: game menus restore timeScale on close, un-pausing under the open editor — Update re-freezes; tools that open a game menu call ReassertPause after. Restore time BEFORE closing chrome (the HUD's show animation needs a running clock). ExitForPlayback closes the editor because the walk-in runs on scaled time and an open editor would re-freeze it. EnsureEventSystem — without one every click is silently swallowed.
- Chrome: F6 hides panels while the room stays frozen (framing screenshots); a preview badge on its OWN canvas (the editor's canvas is switched off wholesale) is the one thing saying the room is frozen and which key returns. With chrome hidden the camera still pans but no tool acts on clicks. A screen tool stands the chrome down but the canvas stays ON — the tool's view hangs from it (F6 is refused while a screen tool is up for the same reason). Ctrl+S belongs to the screen tool while one is up. Camera keys and the tool wheel are suspended behind a full-screen tool.
- Input details: leaving a tool (or closing the editor) confirms an open prompt — panels stay live during prompts (that's what lets a search result be clicked), so the dock is reachable mid-word, and walking away used to leave the field wearing a caret. Wheel-over-list check walks ScrollRects back to front (an open dropdown is parented last and must win) and consumes the wheel so it doesn't also switch tools. A missing wheel axis in this build's input manager is remembered (stop asking). Update exceptions are throttled (a broken tool throws every frame). Zoom drives CamFollowTarget.targetDistance (game default 12, not an orthographic size), initialized from wherever the game's zoom is so opening never jumps. MoveCameraTo moves the follow ANCHOR — the rig overwrites the camera transform next frame. ScreenToWorld projects onto z=0 (correct for ortho and perspective). KeepCullingSuspended for tools that move objects out of their culling area. WorldClicksBlocked exists for full-screen surfaces: PointerOverUi is true everywhere inside one (the backdrop is a blocker), so it can't tell widget presses from surface clicks — BlockWorldClicks per widget press is the half that still can. RectangleContainsScreenPoint gets a null camera (screen-space overlay). Status bar: hover text restores the last real message when cleared; only the plain plate darkens for urgency (on the game's art the accent outline already says it).

### Room editor host, continued (RuntimeMapEditor.cs tail)

- Switching tools closes transient UI (an open dropdown would outlive the previous panel and keep absorbing clicks) and confirms the prompt (keystrokes were being eaten by a search box belonging to the tool just left). CanvasScaler is required (unreadably small at 4K). Attach the UI before building anything (overlays parent to the canvas root; icon fills need a coroutine host). Own-chrome list is collected at build time — everything on the canvas then is editor furniture; anything parented later belongs to a tool and must survive the chrome standing down. The confirm strip is built AFTER that collection, deliberately: chrome restore does SetActive(true) on everything collected, which would raise a hidden strip. ConfirmBottom clears the dock and status bar — a question behind them reads as nothing happening.
- Dock: padding is wider than the icons need because the plank art's edges are torn — a generous margin against a flat rectangle reads as icons running off the plank's end; ends are wider still (the short edges are the most torn). RefreshDockForContext exists because the dock is built at construction and the context can change under it (the base-scene host is stood up before anyone opens the base editor — its dock was laid out for a dungeon). **Detach stale icons before Destroy:** Destroy defers to end of frame, so old icons are still parented and counted when the layout rebuilds — the dock recovered on the next fitter pass but the width read NOW went to the status bar, leaving it near twice its size. ContentSizeFitter horizontal only (a vertical fit collapses the plate for a frame before icons report sizes); ForceRebuildLayoutImmediate so the status bar built next sizes off the resolved width. OptionsMaxHeight 940 — at 820 the column holding a boxed icon grid started scrolling, the very thing boxing was meant to stop. Options rebuild defers three frames (Destroy end-of-frame + staggered fills still adding cells). Status urgency uses the accent outline, not a washed plate, on the game's art. The shortcut panel is an invisible container but still a click blocker.
- **ClearUiSelection / typing — the history:** typing used to disable the EventSystem outright, wrong twice over. (1) It never came back: EventSystem.current is the first ENABLED one, so disabling the only one makes current null, and the restore path asked current again, found nothing, switched nothing on — panel dead until scene change (the "search never gives control back" bug). (2) A search field wants its grid live underneath (hover to preview, click to pick). What actually needed shutting out: UINavigatorNew (polls Rewired's accept from its own Update and confirms whatever selectable it holds — LockNavigator uses LockInput, the game's own gate) and the input module's submit (fires at whatever is selected — so the selection is cleared EVERY frame while a prompt is open, not just at open: a click on a live panel selects what it hit, and the next keystroke would be delivered as submit). Mouse clicks never needed a selection. Only unlock the navigator if WE locked it — blindly clearing would undo a lock the game took for its own reasons, and a stranded lock kills every menu until restart.
- CancelPrompt exists in both spirits: as Enter (picking from the filtered list says "this one") and as Escape (a caller taking the prompt's UI away) — the locks used to be lifted only by literal Enter/Escape, both of which need the prompt on screen; closing a screen under a live search left the editor typing into a nonexistent field with input locked.
- Quicksave: first Ctrl+S that would land on a file this session didn't write only warns (bar orange, press armed) — never silently clobber somebody else's map. The base skips arming (one file, slot-named, can't clobber anyone). SaveBlock is shared by dialog save, quicksave and close guard so they can't drift on what they refuse. Door checks are dungeon-only (Woolhaven and the base have none of the four doors); a hub instead cannot save without a spawn point — without one the arrival falls back to the town's own door, a transform the sweep took away. The base save is a difference, never a picture: a full sweep would write the player's whole town and the apply pass would rebuild a second copy on top of the real one. Failed writes clear _closeAfterSave both places (editor stays open with the work). Hub saves write the hub record under the name the save used — saving under a new name makes THAT the hub.
- Snapshots: WaitForEndOfFrame (the screen must render a frame without the UI before readback); Downscale may return its input unchanged (guard the double-destroy); the PNG write is pure System.IO on a Task. Width 1920 as a CEILING (never enlarges): higher was tried and reverted — disk and memory for every author for sharpness only big-monitor users see; 512 then 1280 were each sized for the largest use at the time and outgrown; this one is sized for the common screen. Old snapshots keep their width until a re-save.

### Save mask (MapEditor/SaveMask.cs)

- The enforcement half of "never touch the player's save": a moved building's grid stamp and follower-navigation position live on the save's own StructuresData — the live data must say the new place, the file the old one. The save serializes from the live object ON A BACKGROUND THREAD, so nothing can mask "around Save()" without racing the serializer — hook the writer: the call spawning the thread is on the main thread, its prefix puts every moved building back; the completion callback (marshalled to the main thread by the game, both success and error paths — a private field on the read-writer, subscribed once on first registration) re-applies the moves. Window is a few frames; failure state is the vanilla layout, safe by construction (moves re-apply next arrival from our file). 20s timeout lifts a stuck mask (a mask left engaged = a moved building silently back at its start).
- **Forget() must put everything back, never lift-then-drop:** it used to lift the mask first — writing moved positions back into live data and then discarding the entries that would have hidden them — so the next save wrote a moved position into the player's file. For a scene-anchored building (temple, shrine) that is fatal: the game binds by exact position equality on load, finds nothing where the save points, deletes the entry. Cost a player a temple. The original is what the file holds and the scene stands at — the only state safe to walk away from. DontLoadMe entries are refused at Register too (backstop — BaseDelta already refuses); the Original stays the original however many times a building moves after.

### Scene references (MapEditor/SceneRefs.cs)

- RoomOverride: GenerateRoom.Instance is whichever room enabled last — fine in a dungeon (one) and a hub (raising the town room makes it last), wrong in the base scene, which holds FOUR (base, door room, town, church) with the winner decided by enable order. A base session says which it means; dropped the moment anything else claims it. ContentRootOverride: same story as EnsureContentRoot (the base's CustomTransform ships full of the player's things; the tools build into our own root instead of reassigning the room's field under the game).
- EnsureRoomComposite: a room's collision is one merged outline — colliders in the composite become edges to walk along; a collider standing alone is a solid body that shoves. Dungeon rooms ship the composite; Woolhaven doesn't (buildings carry baked colliders) — a shape drawn in a hub was a solid block until one is built (static Rigidbody2D so the room doesn't fall out of the world). Unity-null covers a destroyed composite (the clear sweep can take it with the geometry it was built from). Collision-rebuild warnings log the exception TYPE — the town room throws one with an empty message.
- SeeThrough (crystal-tree tricks on anything): they are TWO tricks — the player showing through is the material's shader (taken from a crystal tree, never conjured from flags) plus the game's StencilLighting_ExcludeSprite mirroring the sprite into the lighting pass; fog passing through is `_FadeIntoWoods` on the material, which reads like the first and is not (alone it turns a structure the colour of the weather and lets nothing through). Applied to a COPY (`renderer.material` after assigning the asset is what makes the copy) — the shared material stands behind every crystal tree in the game. ExclusionRenderer child has no OnDestroy — remove it by hand with the component. Restore originals first when toggling one effect off, so the other's material isn't left behind. Walk renderer arrays backwards (destroying a parent mid-walk leaves dead entries). Donor material read once off the prefab (works whether or not the map contains a tree); blocking load, but on a click in the editor, never in play.

### Selection portrait (MapEditor/SelectionPreview.cs)

- Deliberately not the thumbnail rig: that stages a PREFAB on a spare layer with readback into an atlas — right for a catalog, wrong for one live object that must not be moved. This points a disabled camera at the object in place, renders once to a RenderTexture straight into a RawImage — no readback, no atlas, nothing per frame; both of the rig's tricks (layer isolation, unlit swap — a dungeon-lit portrait is a black square) are undone in the same call, and nothing else ever observes the change.
- **The pitched-camera squash:** CameraFollowTarget settles the game camera at Euler(-45,0,0) and the room's art stands up to meet it (BillboardFacingCamera) — a sprite upright on screen is a quad tilted 45° in the world, and a straight-down-Z portrait camera sees it edge-on by 45°, drawing it at cos(45)≈71% height. Nothing about the UI was stretched; the picture was taken from the wrong angle. The portrait borrows the room camera's ROTATION and measures the subject in that camera's space (with a pitched camera, world height ≠ screen height). Camera sits far enough back to clear the subject's own depth, which a pitched view spreads out.
- Glows come out FIRST, before framing — they are usually far bigger than what they light, and leaving them in shrinks the subject to fit a halo. **Finding glows by blend factor does not work:** the game blends through the BlendModes package, whose extensions (DappleLighting, Godrays, DecalSprite) declare `_SrcBlend=One, _DstBlend=Zero` — reads as OPAQUE — and do the real blending themselves; the factors say "solid" for exactly the things that aren't. Reliable signals: the package's own components, the named shader families, particle systems (a frozen godray is not a portrait of anything), IStencilLighting (which owns a CHILD mesh renderer holding a decal quad drawn against a stencil buffer nothing fills in a one-camera render — check the parent, the component sits on the holder). A genuinely additive material (outside the package) is still checked by factor.
- **VisibleLayers is the rule that matters most:** every lit sprite grows a hidden twin — StencilLighting_ExcludeSprite.Start builds an "ExclusionRenderer" child carrying a copy of the same sprite on "Lighting_NoRender", kept out of frame by the main camera's culling mask alone. Restaging every child onto the portrait layer promoted the twin into view — a second copy of the subject wearing the exclusion material over the real one. That was the smear that survived hiding the glows: not a light on the subject, the subject again. Only restage what the room's camera draws. HideAndDontSave already carries the camera across scene loads (DontDestroyOnLoad on top only earns a warning); aspect set to 1 explicitly after the target texture; camera disabled, rendered by hand.

### Clear and door tools (MapEditor/Tools/ClearTool.cs, DoorTool.cs)

- Clear: every button asks twice (arm window 4s), not just the one that arrived asking — they all destroy what the room cannot get back (the sweeps leave no undo entry) and sit close enough that a slip reaches the wrong one. Two presses rather than a dialog because the panel has no modal: first press rewrites the button to say what it will do; a lapse or tool change disarms. One armed at a time. Counts of -1 where counting means walking the whole room. Trigger sweep does NOT clear history (entries whose trigger is gone report false and are stepped over — the session's other undo survives); the placed-objects sweep does (everything the stack referred to is gone). Protected nodes are descended into, not destroyed (shared-parent dressing still goes); terrain shapes only on the deeper clear; much of the backdrop hangs off the room ROOT, not SceneryTransform.
- Doors: _removedByTool set — the revive safety net (the room pipeline still deactivates repositioned doors) must not resurrect deliberate removals. Pad length 9, width inside the door's barrier collider or the player slips around it. **A pad is floor, not scenery:** its BoxCollider2D is what the composite merges and the A* grid builds from; the sprite shape is only looks — and it looked wrong (cloned from a room ground shape, it arrived wearing the biome's floor sprites pasted in a rectangle nobody drew), so the renderer is switched off on EVERY refresh (pads also arrive from load paths that skip creation). Pad splines are world-axis on an unrotated transform, so the collider box is the spline's extents. FinalizeAllPads guarantees every pad's box is merged before a load rebuilds the union. `ConnectionType` set directly, never Door.Init — Init dereferences a graph-neighbour entry that doesn't exist for a door the graph never planned. Before the biome knows the current room, "no neighbour" would seal every door; a door the walk connects must stay usable even if a previous room sealed it. DoorIsland must search includeInactive (a hidden door's hierarchy is inactive). IslandPiece.HideSprites after spawn (vanilla hides placeholder sprites during generation — otherwise flat green editor fill). Dragging: KeepCullingSuspended (a door out of its culling area is deactivated when culling resumes); one undo entry per drag, and undo re-runs the pad and anchors as the drag does (or the island stays where the door no longer is); PlayerDistanceMovement caches StartPos in Start() — re-cache after a move or the door drifts back. A click without a drag selects (rotation buttons need a target). Grab radius is generous (doors are large, pivots off-centre).

### Dungeon builder screen (MapEditor/Tools/DungeonBuilderTool.cs)

- Gestures are the world editor's because it is the same job (ctrl+click place, left drag, right-click link/cut). The dock panel is only "which dungeon" — everything else lives on the map screen. Saving refuses an unplayable map: every saved file registers a dungeon at startup, so a broken one becomes a dungeon that crashes the map screen when picked; the badge has been saying what's wrong the whole time. RegisterAll after save — registration is what makes a dungeon enterable; already-registered maps just take the new graph. Unsaved-edits = JSON comparison (a forgotten dirty flag loses edits silently); an empty map counts as nothing to save. RequestClose is the ONE way out (Esc, X, F4 all share it — the corner button must never be a second, different door): dismiss prompt, else ask about unsaved work, else close; "close anyway" after a refused save would be the discard the author didn't ask for, so the bar says why nothing happened.
- Node type defaults are resolved against the SCENE's config (a config without a blueprint for "MinorEnemy" would let a whole map be built of a type this dungeon can't draw — Preview refuses while entering works, because the two read different configs). The node picker list exists because nodes overlap and one under another is unreachable by mouse; nodes are listed by position and type (a dungeon node has no name). BlockWorldClicks when picking from the list (the same frame's click would reach the map underneath).
- Input: a drag whose release was never seen (the name dialog stops updates for frames) is FINISHED, not dropped — abandoning it left the node where the cursor put it with links drawn to the old spot and no undo entry. An open dropdown owns every click/key and closes ITSELF via its catcher — closing it from the tool broke the widget (list items fire on mouse UP, the tool ran on mouse DOWN, the list was destroyed before the click landed). Right-click is polled, not EventSystem — Rewired drops right clicks. Drag moves the node's rect by reference (a per-frame redraw would destroy the rect being dragged) and re-aims lines rather than rebuilding (they stay attached during the drag). Hubs are excluded from the level list (same file format, not a floor).

### Dungeon builder screen, continued (DungeonBuilderTool.cs tail)

- Hover reads run every frame — only rebuild the hint text on a CHANGE (a text mesh rebuild per frame otherwise); off a node the bar returns to the last message, not blank; after placing/deleting, re-read the hover target now so the hover doesn't talk over the sentence just written. Placing with a selection links the two immediately (a path in one gesture per step). Node ids exist only so links have something to name — nothing on screen shows one; the status line says position and layer (the derived layer is the one fact not visible in the node itself). One right-click gesture both makes and breaks a link. Undo entries capture incoming links WITH their indices (what putting a deleted node back exactly needs) and redraw whatever is showing when they land (Ctrl+Z fires whether or not the map is up).
- Preview uses the game's real adventure-map overlay: offered only when the map would play (its OnShowStarted indexes the first node, GetFirstNode is a .First(), and it skips anything unlinked — a half-built map crashes it); shuffling would regenerate the run's map over ours, so the current MapManager map is guarded; Show(disableInput:true) — a look, not a run; Hide(immediate:true) on teardown (its fade-out would run on an object being destroyed under it); EndPreview destroys the instantiated screen (Hide only switches a menu off — every preview would otherwise leave a dead map screen in the scene). FallbackTypes deliberately omit MinorEnemy — the configs this editor runs against have no blueprint for it, so it drew nothing.

### Enemy tool (MapEditor/Tools/EnemyTool.cs)

- Three vanilla address spaces (the last two are pre-Addressables leftovers holding 103 prefabs); boss prefabs are named for what the boss is, not who — only the bishops need aliasing. The custom group is never cached (mods register at their own pace). The boss group is OPENED, not scanned, and goes last saying so: picking it reads a couple dozen room prefabs off disk. Rooms open one at a time with the grid filling behind (two dozen room prefabs in a frame is a freeze, and there's nothing to look at until the first lands); icons come last (the thumbnail rig stages and photographs each subject — doing that while rooms still load is two heavy loads competing). Search covers every group at once (the catalog is filed by biome and the enemy you want is rarely in the biome you're standing in) — but boss entries only exist in search once the group has been opened (searching cannot open two dozen room prefabs on a keystroke). BossGroupKey is "\0bosses", compared by reference — can never collide with a real biome folder name. Boss room list restricted to the top level of _Rooms (BossRoomAssets beside it is scenery).
- What counts as an enemy inside a room is deliberately NOT the NPC tool's test (conversation/shop components — a boss has none): UnitObject + Health is what makes a thing fight, the same pair the enemy prefabs carry. Take the UnitObject's OWN node, not a parent — a boss controller holds several (Narinder, two Guardians, the eyes), and climbing above one takes them all as a lump. Something must draw (a SkeletonAnimation) or the entry is an invisible marker.
- **A boss lifted from its arena arrives without its ritual:** Instantiate on the prefab CHILD (Unity hands back the subtree alone, no room around it) — a placed boss stands where put rather than playing its intro, which is what an editor wants and worth knowing before wondering why it isn't attacking. COTL_API's custom-enemy dictionary is internal, read once via Traverse (COTL_API mutates it in place, so one read serves the session). Ghost preview sets skeleton alpha before display so what is dragged looks like what will be placed. Placement cooldown uses SCALED time — frozen while the editor is open, which is correct (nothing moves then).

### Level tool (MapEditor/Tools/LevelTool.cs)

- The dock panel is only the list of levels (picking one goes straight to the screen — a half-open state with a second set of controls is a page nobody is on). Hubs are excluded (authored in the town room from the F7 panel; only half-editable here). The starter level is the smallest thing that is already whole: in at the south of one room, through a door, out the top of the next.
- **EnsureEndRooms:** a random walk always builds two rooms of its own whatever the level says — PlaceEntranceAndExit makes the arrival room the EntranceRoomPath prefab (the weapon podiums) and appends the EndOfFloorRoomPath room with the way out. They were always on the floor; the level just didn't know, which is why its first blueprint loaded over the podiums and its last over the exit platform. So the level OWNS them: first and last entries, no pool, undeletable. Inserted, never repurposed (a pre-existing blueprint has real rooms at both ends with author-chosen pools). Hubs exempt (one room is both ends). Delete refused below 3 rooms under a random walk (the walk can't lay out a floor with nothing between the ends — EnsureEndRooms would put a room straight back and the delete would look like it moved a room onto the podiums). After inserting, LineUpInOrder redraws the column to match the list (rooms the author never placed must not sit wherever there was a gap).
- Ends are tickboxes, not buttons — being the entrance is a STATE (a button can only say what it would do, never what is true); unticking is refused (a level has a first and last room whether anyone chose them — you can only give the role to a different room). Doors are NOT listed in the column: every neighbour side is toggled by right-clicking the pair — quicker and impossible to get backwards; a column of dropdowns saying the same thing was a second place to look. Podium note ("only appear on the first floor of a run") — the game's rule; an empty room reads as broken unless said. The modifier dropdown goes LAST (above the room list it pushed the list off screen).
- Pool grid: was a dropdown of names + X rows — asked you to know a room's look from its name and split one question across two controls that disagreed. Now every saved room as a snapshot, lit cells ARE the pool; click adds, click removes. "<vanilla></vanilla>" first (the answer for "anything, I don't mind"). Names from the FILE SYSTEM, not parsed blueprints (parsing every room to read names was a stall per selection); hubs excluded via the hub record (the folder listing can't tell a town from a dungeon room — dealt into a floor, a town arrives doorless and the run stops). Cells carry names (author-named rooms — the name IS half the identity; near-identical snapshots aren't). Built once per session and kept; DetachPoolGrid lifts it off the column before rebuilds (BuildOptionsColumn wipes children) — parented to nothing it is a scene root, so TeardownCanvas must destroy it by hand; Reflow after re-parenting or the box keeps its old height and clips the first row. TogglePool repaints marks and grid, NEVER the column — rebuilding would destroy the grid under the pointer and throw the scroll away per click, fatal for a control whose purpose is picking several in a row. Membership survives filters (SetSelectedMany re-applied — the pool is what it was; the grid just shows less). Room snapshots load via UnityWebRequestTexture (decodes off the main thread — forty full-screen PNGs inline was the stall); "no snapshot" is cached too.
- SetAuthored(true) on a level that never had a layout lays it out as a line (the shape a flat room list always described); rooms placed before wiring touch nothing (WireRoom reads the level). Fresh rooms are placed on the first empty cell straight up the column — CTLevelRoom defaults to (0,0), almost always occupied, and a room drawn on top of another can be neither seen nor clicked; called before the room joins the list so the search can't find the room being placed. CommitMove re-derives doors from the new position (doors to arrivals, walls at partings, end doors re-placed on free sides). EnsureCells: pre-layout blueprints have every room at (0,0) — strung out in a line in their own order, because the grid is how rooms are seen and picked in BOTH modes.
- Undo is a whole-grid JSON snapshot, not per-edit inverses — every edit renormalises the neighbours' sides too, so an inverse would carry those as well, at which point it is a snapshot with extra steps. AfterMutation deliberately does NOT call _editor.MarkEdited — that is the ROOM editor's dirty flag, and a level edit is not an unsaved room; the screen keeps its own against what was last written. Drag handling mirrors the dungeon builder (finish orphaned drags; move rects by reference; right-click polled because Rewired drops right clicks; off-window or occupied drops snap the rect back).

### Lighting tool (MapEditor/Tools/LightingTool.cs)

- No "Capture Biome Lighting" button: OnEnter already captures the live biome into an untouched blueprint, so the sliders open showing what the room is doing and the first moved slider flips to overriding; the button's only unique effect (pin-without-change) is a nudge of any slider. No state note either (the sliders and the room already show it). Reset-to-biome must also move the KNOBS back (AdoptBiomeValues) — left where the override put them, the next touched slider snaps the room back to the look just thrown away. AdoptBiomeValues reads the SNAPSHOT, not the manager — ClearOverride fades back over seconds, and the manager mid-fade reads a colour the room is only passing through.
- Weather rows are HIDDEN, not skipped — panels are built once, before a base session has said so. The base is not offered weather (it keeps the game's seasons and shrine weather; a map file setting it would fight them, and the writes that show weather there land in the player's save). Strength is not a free choice: the game keeps one weather set per (type, strength) pair and an absent pair produces NOTHING — picking a type re-offers only the strengths it has. HDR colour sliders run 0–3 (HDR routinely exceeds 1). Saving while following the biome captures what's on screen first (an uncaptured blueprint starts from the live look, not defaults). SyncSliders on enter — map loads and the trigger tool change values while the tool is closed. Profiles apply as CLONES (slider edits must not rewrite the profile).
- Per-room lighting is keyed by GRID COORDS, not the BiomeRoom object — BiomeRoom identity changes on revisit, which left re-entered rooms plain. The biome snapshot is captured once before any override; restoring it is NOT the same as clearing inOverride (that transitions to the time-of-day target, not the biome's own look). OnBiomeChangeRoom is the one signal firing on EVERY door change (revisits skip the generation hooks); wrapped so a slip can't break other subscribers; `_assertedRoom` dedupes the same arrival announced twice (multiple hooks). The hook stays free in ordinary play (early-out when nothing was ever overridden). The stored per-room object is the LIVE one (sliders write into it while editing). ApplyInternal (without Remember) exists so a biome restore is never recorded as the room's own look. ForgetCurrentRoom is explicit-give-up only (Reset button, blueprint without lighting) — NOT part of ClearOverride, which runs on every unlit-room arrival and would erase lit rooms' memory.
- Fade machinery: one settings object reused with HideAndDontSave — a fresh ScriptableObject per apply gets swept by UnloadUnusedAssets (room changes trigger it) while the manager still references it. Only our properties override; the rest follow the biome's time-of-day asset. UnscaledTime — the editor runs at timeScale 0 and a scaled transition never finishes. transitionDurationMultiplier is reset to 1 by the manager after every transition, so set it per apply (0 lands this frame). Starting a fade mid-transition lerps from stale currentSettings (visible jump) — cancel the running one, let it land (≤10 frames), read live values back. UpdateLighting REVERSES a running lerp (flips deltaTimeMult) instead of starting a new one — stand the old one down first. Fades are hosted on the manager so they die with the scene.

### Load browser (MapEditor/Tools/LoadTool.cs)

- The tool IS the browser (no panel — a submenu would be a page nobody is on, flashing up for a frame both ways). OnExit frees everything — snapshots are megabytes each, alive only while the browser is on screen. Esc takes the lightbox down first (a layer above the browser), then the screen.
- Scan lists the folder OFF the main thread and parses nothing: a blueprint carries every shape/prop/structure/enemy, and the browser needs a name and a date — both on the file itself (file name IS the map name; date from the file's write time, read once per file, never inside the sort comparison). The one blueprint parsed is the one clicked. Newest first (working on a room usually means the last saved). Hub/dungeon rooms are filtered both ways (a dungeon room in a town arrives with four doors and enemies; a town in a dungeon arrives doorless and the run stops). The bridge-roots static list is warmed on the main thread before the worker (not a race worth having). Cards build 6 per frame (a folder of forty in one frame is a visible lurch); snapshots fill in only after every card stands (pictures against a list that stopped moving). Loading a map stops any level run (a stale run advancing on the next door would teleport the player). Unreadable paths sort last rather than taking the browser down; file size is a nicety not worth a throw. Facts pane: one line per kind behind the icon of the tool that makes it (an index into the toolbar, not a wall of numbers); zeros omitted.
- Preview textures: `_previewToken` discards decodes for cards that no longer exist; three workers — the decode is off-thread but the GPU upload is not, and forty uploads in a handful of frames is its own stutter (newest-first means the pictures worth waiting for arrive first). The detail pane borrows the card's texture immediately while full-res loads (fills at once rather than sitting empty).
- **The mipmap-limit trap (LoadFullRes):** UnityWebRequestTexture builds textures WITH mipmaps, and QualitySettings' mipmap limit (shipped at 2 in this game) throws away the largest levels on upload — a 1920-wide snapshot was drawn from a 480-wide mip: fine in a grid cell, obviously wrong enlarged. Nothing shows it: Texture2D.width still reports 1920 (the CPU-side descriptor; the limit applies to the GPU copy) — which made it look, twice, like it wasn't happening. The game's own photo loader (MMImageDataReadWriter.Read) is the answer: mipChain:false — no mipmaps, nothing to discard. Cost: LoadImage decodes on the main thread, so it's done for ONE picture (the one being looked at), never for every card.

### Load browser textures, map assets tab, ghosts, gizmos, protection (LoadTool tail, MapAssetsTab.cs, MapEditorGhost.cs, MapEditorGizmos.cs, MapEditorProtection.cs, MusicTool.cs)

- Snapshot previews load nonReadable (nothing reads the pixels back; a readable copy doubles the memory of the heaviest thing in the editor). A card picked before its snapshot arrives takes the picture when it lands. The full-res file read is Task.Run (pure IO off-thread); only the decode happens on the main thread.
- MapAssetsTab: runtime-minted enum values don't appear in Enum.GetValues (hence the union). The cloned tab is built in an INACTIVE holder — MMTab.Awake would bind the clone to the original menu before rewiring; tab registration replicates what MMTabNavigatorBase.Start does for startup tabs. The gate patches decide listing/clickability; the vanilla checks only greyed entries out.
- MapEditorGhost: interactions are DestroyImmediate'd so OnDisable/OnDestroy deregistration happens before Interactor's next Update (deferred destroy is end-of-frame). A podium ghost's static-list entry must be removed — the podium registered itself on wake, OnDestroy does not clean the list, and a dead entry breaks the doors-open check real podiums run.
- Gizmos: per-frame bounds asks are memoised per (object, frame) — box/grip/corner ask two or three times a frame and each walked the child renderers. ONE line material for every gizmo (Sprites/Default renders vertex colour, so lines still tint via startColor/endColor) — a material per line was a monotonic leak (explicitly-assigned materials are not destroyed with their renderer). Boxes are drawn at the object's own Z (coplanar with what they mark). Gizmos are world objects, not canvas chrome — hiding the panels must reach them too or screenshots keep every outline. Grip = centre of footprint; resize node = top-right corner; depth node = the OPPOSITE top corner (on the outline, unmistakable for either).
- Protection: player is checked downwards as well as up — sweeps walk a room's direct children, and where the player hangs off a unit layer inside one, testing only upwards destroys the container and the player with it (thorough sweeps move the player out first rather than relying on this). **In the base IsProtected is deliberately tiny** — rearranging is the whole point; only two things refuse touch entirely, neither about the save: the people (followers/pets walk under their own steam — placing one doesn't stay done) and the placement region (its grid is a lattice in its own local space — moving the object slides every cell out from under every building; the totem itself moves freely). **CanDelete is a separate question**: a player building is movable but never deletable, whichever tool asks — paid for, possibly holding a follower's job, recorded in a save this mod doesn't write; no undo for taking one away (the build totem demolishes properly, with the refund). IsBuilding checks both the component AND the registry — the component catches unregistered ones, the registry catches ones whose hierarchy hides the component from both walks. Inert scenery stays deletable (a tree is a decoration).
- MusicTool: options filled on entry (FMOD banks may not be loaded at panel build); events enumerated once (banks load at startup, the set is stable); shared with the trigger tool (labels and list).

### NPC and podium tools (MapEditor/Tools/NpcTool.cs, PodiumTool.cs)

- NPC catalog: both "NPC/" and "NPCs/" prefixes accepted (nothing guarantees an update keeps the spelling). Characters live inside room prefabs — slow enough that scans are indexed ON DISK: search covers what has been scanned plus the custom list and says so, rather than pretending to have looked everywhere; open a group once and it is searchable this session and the next. Scan tokens invalidate fills for grids that are going away. Bucket order follows declaration, not alphabet (the named characters are what anyone opening the tool wants). The custom group reads live on every open. MaxSkeletonsPerCharacter guard: a few rigs is a character with an effect or companion; a dozen is a crowd, and taking the node above a crowd is how the tool starts swallowing the room again. Cursor previews go through the same IsSafeToSpawn guard as placement (a preview of a whole room is as bad as placing one) and the same key resolver as thumbnails and placement (all three agree what a key means); overlapping preview loads: only the one still matching the current key installs its ghost. Custom NPC ghosts get the skin override applied (the ghost is the mimic; the skin is what makes it look like itself). Spawn waits up to 10s for the prefab list (filled by a coroutine at plugin load; an early blueprint can outrun it).
- Podiums: vanilla's disable-others block is inverted exactly by Restore; the patch is a FINALIZER, not a postfix — postfixes are skipped when the original throws, which would leave the podium permanently stripped of its sibling options. A used podium (WeaponTaken/activated) stays consumed. Type dropdown always armed (a podium type is never "nothing" — four buttons plus a Clear that only turned placement off said less). ClearAllOnEquip is re-asserted on tool entry over unmarked podiums (a blueprint load brings in podiums that never saw the toggle). Ghost previews run SCRIPTS (a dead script leaves a pink error mesh from uninitialized runtime materials) with the self-destroy flag cleared before wake. `RemoveIfNotFirstLayer = false` defeats the first-room-only self-destroy without Harmony. Curse podiums downgrade rather than silently vanish when spells are disabled. Template acquisition must run in the loader's CAPTURE phase (scene still intact): a loaded chest asset carries the podium prefab as a serialized addressable reference (blocking-load; a raw prefab has never run Awake — ideal); fallback clones a live podium into the inactive holder. Saves record the originally chosen type (Random round-trips as Random) but read the LIVE ClearAllOnEquip marker (the toggle rewrites every podium in the room, including pre-toggle placements).

### Select tool (MapEditor/Tools/SelectTool.cs)

- Panel design decisions: no Deselect button (right-click, from anywhere; the button also appeared/disappeared with the selection, shoving everything under it); no depth buttons (the purple node drags Z directly and shows the result live); flipped is a CHECKBOX (a state — a button couldn't say whether the thing was already flipped); no delete button (Del; the button sat one slip from the controls above); no rotation line (2.5D, flat art, nothing is ever turned — a row always reading 0 is noise); see-through rows only shown for things that can carry them (disappear, not grey out). The portrait plate is a centred square inside the full-width row (a full-width plate around a square picture reads as a stretched picture); RawImage, not Image (a RenderTexture has no sprite); the empty box says "Nothing selected" (an empty plate reads as failed-to-load), and "selected but nothing to photograph" (trigger volume, empty parent) is said out loud. One portrait render per selection change / per gesture end, never per frame; the readout follows a drag throttled (text mesh rebuild), and resize=false skips layout rebuilds during drags (the readout keeps its shape).
- Internal name resolution (what the files call it, vs Unity's "Building Bed(Clone)"): placed-structure tier first (the only tier knowing a custom structure's COTL_API name), then Structure brain type (the save's own key; component field as fallback for un-brained ones), then the addressable key. Resolved at SELECTION, not in RefreshDetails — that runs ten times a second during drags and the last tier walks the catalog.
- **Clone drag:** Ctrl over a selection's own handles must clone — the handles sit ON the object (grip in its middle) and are UI blockers, so the polled Ctrl path never sees the click; requiring a bare pixel made Ctrl-drag unusable on anything small. From a handle there is nothing to work out (the gizmo belongs to the selection). The selection wins whenever the pointer is over it — re-picking took the smallest sprite under the cursor, and a room is full of grass/decals/splashes lying over the object you want. Podiums refuse cloning (post-Awake state, self-destroy on enable — use the podium tool). **The tint must come off for the copy:** the highlight is a colour written onto the source's renderers, Instantiate copies it, and Select then records the clone's tinted colours as its ORIGINALS and tints on top — each generation darker, never washing out (the tint had become the object's real colour); pre-fix clones keep their baked tint (re-clone from an original). Clone names: "(Clone)(Clone)..." grows without bound and lands in the blueprint — replaced with a sibling-scoped counter ("Torch 4" clones to "Torch 5", not "Torch 4 2"); sibling-scoped because names only distinguish where things sit together, and a room-wide sweep per Ctrl-drag costs more than the answer.
- **PickWorldObject — screen-space picking:** the room's ground is world-XY and the camera is pitched 45°, so Z is HEIGHT, not just sort order — a raised object's art climbs the screen while its 2D collider (no Z) stays on the ground, and a z=0 world-point sweep compared ground positions. Both meant a lifted object could only be selected by clicking the empty floor where it used to be. Fix: the collider hit counts only when the found thing is also under the cursor ON SCREEN; the renderer sweep projects all eight bounds corners (a pitched camera skews a world box; only the corners bound it; points behind the camera mirror through the origin and would cover half the screen). Smallest on-screen rect wins (a small prop in front of a big backdrop stays reachable). **Cheap questions first:** the ignore rules walk ancestors and subtrees — asked of every renderer they made base clicks stutter (thousands of renderers there); now only renderers actually under the cursor and smaller than the best so far are asked. HP bars are pick-ignored (siblings of their enemy — no enemy check catches them); triggers too (their own tool draws and edits them — picking one here selected an invisible volume that could be dragged or deleted out from under it).
- **The climb and IsRegion:** the cursor lands on a sprite inside the thing the author means, so the walk climbs to the container. In the base (hand-authored, has regions) blind climbing handed back the whole teleport area — trees, statue, bridge as one object. Counting children was far too eager (a single bush is several sprites — it came apart into leaves). SIZE is the honest signal: things that belong together are together because they're in the same place, so a group stays compact (RegionSize 9); a region is spread across the map by definition. In the base, buildings are picked up WHOLE however their art nests, and the climb stops below the content root (without that it climbs past our container and hands back everything the editor ever placed as one object).
- Gestures: one undo entry per GESTURE, captured at drag start, pushed at end only if something changed (a click grazing a handle must not fill the stack with no-ops). Undoing a move is itself a move to the base's journal (grid + navigation follow back). Resize scales from the START scale each frame (never the previous frame); grows about the visible CENTRE, not the pivot (pivots sit on corners/floor lines); a grab with no reach along an axis leaves it alone (the ratio is noise); a refused resize latches off for the whole drag (warn once, not per frame); centre grabs refuse (no direction, divide-by-zero). Depth: 0.01 Z per screen pixel (the old buttons' step — short drag nudges, long drag reorders); captured on mouse-down; up = away ("Send Back"). Drags preserve Z. KeepCullingSuspended when moving. Highlight is applied per-renderer, never via renderer.material.color (that clones the material and the clone survives deselection); lifted for the length of one render for portraits (no frame draws inside the gap). Delete respects CanDelete (a paid building must never cost the player — no undo can give it back); BaseDelta.NoteRemoved runs BEFORE destruction, while there is something to describe.

### Shape tool (MapEditor/Tools/ShapeTool.cs, first half)

- No click-to-add mode: Ctrl-click adds a point (a mode you must turn on and remember to turn off asks "did you mean that click?" one step too early — and left on, every stray click grew the shape). The shape list goes LAST with its add button (everything above acts on the picked shape — "settings, then the things they apply to" read backwards, and a growing list is the one thing that shouldn't shove fixed controls). A list, not a dropdown — it does three jobs (click to edit, -/+ to reorder, X to delete); a dropdown answers only the first and its side buttons acted on "whatever is selected". MinPointSpacing 0.25 (Spline.InsertPointAt throws when a point lands on an existing one).
- PrepareForLoad BEFORE the room clear (template and profiles come from scene objects). ResetTracking also turns Show Collision off (a stale toggle would redraw over the loaded map). The vanilla-floor switch is hidden in the base (there the room's own floor is the player's town — switching it off drops everyone standing on it). Template capture: inactive clone (so shapes can be created after Clear Terrain removed every original); prefer a FILLED shape — a template from an edging/rope profile draws a hollow outline (Woolhaven's own terrain is exactly that kind of unfilled decoration); ground before water (a water profile's fill is the water surface, which over an emptied room draws as nothing — the hollow shapes hubs used to get); FindObjectsOfTypeAll because in the base every shape outside the current room is switched off with its room (scene objects only — the sweep also reaches assets). EnsureFill: a fill needs a profile fill texture AND a fill material in renderer slot 0 (slot 1 is edges) — a clone of an edge-only shape has neither and comes out as an outline around nothing. WindsClockwise = shoelace sum. New shapes parent to the COMPOSITE, not the content root — anywhere else keeps a solid collider that pushes the player off. New shapes appear at screen centre, not the cursor (the cursor is over the button just clicked).

### Shape tool, continued (ShapeTool.cs tail)

- Strip the template's baked colliders (the clone must bake its own). New shapes wind the SAME way as the template — a sprite shape fills the side its spline turns towards; wound against the template's direction it comes out inside-out (edges inside, fill spread over everything outside). EnsureCollider on creation — CommitShape only maintains collision on shapes that already carry one.
- **Z does not layer sprite shapes.** The game stacks them with sortingLayer + sortingOrder and leaves Z at a rounding nudge (CreateSpriteShape builds every room shape at z=0.0001, sets "Ground"/-1; the game itself compares sortingLayerID to know which shape is on top). Unity draws sorting layer, then order, then camera distance — so a Z nudge reaches the weakest mechanism, and every tool shape is a clone of one template sharing one layer and order. That's why moving a shape in Z barely did anything; the list's -/+ move ORDER. Ground is forced onto the "Ground" layer whatever it was cloned from: a town clone can land on the same layer+order as the structures standing on it, and when both tie the decision falls to that 0.0001 depth — two surfaces that close flicker as the camera moves (the whole terrain-versus-structure fight). NudgeOrder steps the pressed shape ALONE — renumbering the whole list would restack the biome's own orders on terrain the author never touched.
- Deletes are DestroyImmediate (a deferred destroy leaves the shape in the merged outline until the next change) with BaseDelta.NoteRemoved first (while there is something to describe). SyncCollisionToggle uses notify:false (selecting a shape must not add/strip a collider as a side effect). Points insert after the NEAREST point, not at the end (the outline stays sensible); a spline needs at least 3 points to generate geometry; geometry-only refresh during drags (cheap, per-frame safe), collision and navigation rebuilt once on release.
- **JoinRoomComposite in the same breath a collider is born:** a collider is a solid body until delegated to the composite, and everything that delegates runs a frame later (once the mesh exists). In the editor the gap costs nothing (clock stopped); on the way into a base it is a real frame of real physics — anything standing where the collider appears is shoved off (what pushed the player from the spawn point when a reshaped base loaded). The geometry catches up at the bake; what matters is the collider never acts alone. Collision overlay: red = the active shape's contribution, green = the merged room outline (what the player actually collides with).
- CommitShape is the one funnel (points/drag/depth/collision all land there) — MarkEdited and, in the base, NoteShapeTouched (only the FACT of touching and the pre-touch state — a drag commits per frame, and the final shape is read at save). Visual-only shapes never get a collider (editing decorative geometry must not turn it solid). MergeCollisionIntoRoom is split out because a shape rebuilt ON LOAD needs the same treatment a hand-edited one gets — not getting it is what left reshaped base terrain pushing the player around after every load; sorting is deliberately left alone there (the base's own terrain came with the game's draw order). In the base, BaseGround.RequestRefresh after merge — collision is only half of what makes a floor (the validation polygon, follower bounds and buildable grid read a separate outline; ground solid but not in it keeps teleporting people off).
- Contribution to the save: in the base, only what this tool made — the dungeon sweep exists so a room's own authored terrain round-trips when the blueprint IS the room; a base file is a difference, and sweeping would write the player's whole town then lay a second copy over it. ApplyShapeData is split from RebuildShape because the base finds the player's shape where it stands and tells it what the author did (returns false when the data is unusable — the caller throws away what it made). Tangent mode is set FIRST (setting it recomputes tangents, clobbering saved values); a coincident point throws — skip the point, never lose the shape; absent SortingOrder keeps the template's (pre-editable-order maps). FinalizeLoadedShape a frame after RebuildShape (mesh gen deferred; earlier bakes capture the stale outline). CTEditorShape marks tool terrain — it matters only in the base, where the player's own ground stands beside anything drawn on it and nothing about a sprite shape says which is which. Handles are UI blockers (clicking one must not also drop a point). ShapePointHandle: drag moves, right-click deletes; end-of-drag rebuilds collision once.

### Structure tool (MapEditor/Tools/StructureTool.cs)

- Place-see-through/fog arm the NEXT placement (like scatter); placed things change via the Select tool. Scatter mode: gather several picks, each click chooses among them (filling a treeline one structure at a time produces rows that read as rows); the single selection and the gathered set are two answers to one question — only one is ever live; NO cursor ghost in scatter (a ghost would show one pick and be wrong most of the time — the status line states the count instead). CanSetSeeThrough answers only for structures this tool placed — the flags must be written down as well as applied or they're gone next load; the saved value is READ BACK from the object (a sprite an effect couldn't attach to leaves the boxes honest). TryFlip keeps the serialized mirror flag in step with the Select tool's flip; TryAdoptClone adopts ctrl-drag clones (a copy is a copy of the whole thing, looks included — Unity copied components and materials).
- The hub totem is an entry in this tool (first, hub-only): placed by hand like anything else; at play time it is the game's own build menu. AdoptTotem for loader rebuilds. The totem is IsTracked — unclaimed, the snapshot took it for authored scenery (its name doesn't end "(Clone)") and wrote it into KeptAuthored: a second way to bring back an object that already has one. Saved as a position on the blueprint; presence checked by the object still existing (destroyed via Select rather than un-placed — no flag to forget).
- **Custom structures:** their "path" is not a real addressable key — COTL_API swaps it inside InstantiateAsync only, so LoadAssetAsync throws InvalidKeyException; load the base prefab (what COTL_API instantiates before swapping the sprite) and dress it here (sprite + our skeleton, bottom-centre re-pivot as COTL_API does — a structure stands on its tile). Ghost fading: sprite tint misses the skeleton mesh (Spine carries its own colour). Overridden vanilla buildings (BuildingOverrides folders) get their look from a postfix on Structure.Start — the ghost never wakes that, so without dressing it shows stock art while aiming and the override after the click (the one moment you choose by eye is the one moment it lies). Addressable waits must not depend on scaled time (timeScale 0). Editor placements attach the structure spine themselves (the patch hangs off LocationManager.PlaceStructure, which the editor bypasses). SeeThrough applies AFTER flip/scale/custom art (both looks read the sprite that's there). In the base, BaseDelta.GiveBrain (a structure is meant to be a building — the bed gets slept in).
- Serialization: InternalNameOf — a GuidManager-minted enum prints a bare integer that resolves DIFFERENTLY next launch, so custom structures answer with their mod's name; old saves wrote the raw integer (honoured if it still exists). Scale saved ABSOLUTE (FlipX stored separately and re-applied — a negative X would cancel it). Prop groups exclude enemy folders (their own tool); search covers every group (the catalog is filed by Addressables folder and the same word turns up in several). CancelPendingPropIcons on group switch. Pooled prop previews: un-fade before recycling (the ghost's fade must come off before the instance goes back). Group dropdown deferred (TypeAndPlacementObjects doesn't exist when panels build in Awake).

### Structure looks: see-through, fog, wind, shadows (MapEditor/SeeThrough.cs, APIHelper/StructureShadows.cs)

- **The grass sway is a shader, not a script or a Spine animation.** The UberShader family animates vertices behind `_AnimateVertex` / `_ANIMATEVERTEX_ON`; strength and direction are GLOBAL — `_WindSpeed`, `_WindDensity` and `_WindDiection` (the game's own misspelling), pushed by `GameManager.SetGlobalShaders` and re-driven per biome through `BiomeVolume`. So the checkbox is on/off: the `_WindIntensity`/`_WindDensity` floats sitting on grass materials are dead serialized leftovers the shader does not declare, and a per-prop strength would need a shader we do not have. `LongGrass.cs` is a separate thing entirely — DOTween rotations for walking through grass, not wind.
- **Four things must all be true before a prop actually moves**, found in this order and each one looking like the last one's bug:
  1. `_AnimateVertex` alone produces a permanent LEAN, not motion — a constant offset reads exactly like frozen time, which sent the first search after `_Time` and the editor's `timeScale = 0` pause. (That pause is real and does freeze every shader while editing — preview by leaving the editor — but it was not this.)
  2. `_Vertex_Offset_X`/`_Vertex_Offset_Y` are TOGGLE properties: the displacement branches on `_VERTEX_OFFSET_X_ON` / `_VERTEX_OFFSET_Y_ON`, not on the float. Copying the numbers moved the passes that ignore the keyword and nothing else — **the tell was a still structure with a swaying silhouette**.
  3. `_IGNORESPRITEFACING` cancels the whole thing: facing rebuilds the quad toward the camera after the wind displaced it, so the main pass snaps back while the non-facing passes keep the motion — the same still-body/moving-silhouette symptom, one layer down. Every material in the game that visibly sways lacks that keyword; every still one has it. It is disabled (and `_IgnoreSpriteFacing` zeroed) wherever wind is applied. Watch for orientation changes on sprites that relied on it.
  4. A prop's own material has the property but no noise texture — `_WindTexture` (PerlinNoise) and the other inputs are copied from a donor.
- **The donor must be a material that is provably swaying** (`_AnimateVertex >= 0.5` on something in the room), not the best-looking candidate. Preferring a non-occlusion material picked `UberShader-NoDepth-WindMap`, whose displacement comes from a wind map lent sprites do not sample — everything given it went still. Fallback when a room has no grass is the `Assets/Prefabs/Grass/Dungeon1_Shrub_Grass.prefab` material.
- **Lending a shader is how a multi-sprite structure sways as one object.** A structure mixes materials — some carry `_AnimateVertex`, some are plain sprites — and the plain ones are handed the donor's shader (own values kept; reverted if the swap does not produce the property). Occlusion variants (`*_RadialOcclusion`) fade their object out when the player is behind them, so lending one makes a structure read as see-through by accident: the plain sibling is preferred (`Shader.Find`, then a sweep of shaders in use, since Find only sees what the build kept), and lending an occlusion shader anyway is a logged warning.
- **See-through and wind coexist**, contrary to the first assumption here. The donor material swap happens first, then fog and wind are set on whatever material the renderer ended up wearing. The one rule: a see-through sprite is NEVER lent a shader, because the material IS the see-through.
- **The renderer list goes stale.** A structure's brain brings sprites of its own and `GiveBrain` runs after the look was applied, so `Set` re-scans for untracked renderers on every call and the structure tool re-applies one frame after the brain lands.
- **Shadows were missing two halves, not one.** A SpriteRenderer defaults to `ShadowCastingMode.Off`, AND the stock sprite shader has no ShadowCaster pass, so flipping the flag alone still renders nothing — vanilla structure prefabs are authored with both, and `Structure.OptimizeShadows` thinks in exactly these terms (it demotes small `Sprites-Default` sprites to a no-shadow material). StructureShadows sets the mode and `receiveShadows`, lends a shadow-casting shader with the same plain-sibling preference where the material cannot cast, and sets `_SHADOWCAST_ON`. It runs at both entry points — the `LocationManager.PlaceStructure` patch for structures built normally, and the editor's PlaceAt — because the editor bypasses that patch. Spine structures are untouched: their renderer is a MeshRenderer and the game drives those through `SimpleSpineAnimator`.

### Trigger actions and camera (MapEditor/Tools/TriggerActions.cs, TriggerCameraActions.cs, TriggerScreenText.cs)

- Action notes: CameraShake is its own action because the effect-list shake had one fixed strength ("a distant rumble and a hit that throws the screen are the same call with different numbers — the number was the missing half"); the old effect name is kept so pre-existing maps still play. Loop doubles as "skippable" on cutscenes (nothing to loop). HubSpawnPoint is a MARK, not a step — the arrival reads the trigger's position before the player is put down, so by walk-in time there is nothing to do; a base's spawn is read from the FILE (BaseDelta.SpawnPoint) because the player lands before the delta is applied. ReturnToBase stops the level run FIRST (the scene lands in the base, where a run still believing it plays would apply blueprints to the cult's rooms) and nothing after it runs (the scene is going). OpenWorldMap waits for the screen to close. Lighting fade reuses Duration (negative = explicit instant; 0 = pre-picker save, gets the 1.5s default); empty profile target = back to the biome. The action list is COPIED before running (an action can outlive its trigger; the list must not change under the loop). Waits are unscaled (the game may be paused around a conversation — a scaled wait never ends). Move: the ring starts at the top so two players land above/below (reading as either side); a player destroyed mid-walk stops being counted. Only clear player states this system set (a player who died mid-sequence keeps theirs). Animation lengths come from the skeleton (1s fallback for names it lacks — the game plays nothing then, so the wait is a beat); the animation picker lists the player skeleton's own names (typing names got them wrong, and a wrong name plays nothing). Conversations: Play is async by a frame or two and refuses if another owns the screen — guard the wait; the runner's teardown lands a frame after the last node. Objects are addressed by scene path (survives save/load while the room is the same; fallback is the authored position).
- Camera: offset/zoom are CameraFollowTarget's own fields (the same ones the game's cutscenes push); look-at swaps the rig's follow targets so the move is the ordinary smoothed follow (players keep playing underneath) — restore the previous targets WITH their weights (co-op frames two players by weight; 1-each would reframe); if everything the rig followed is gone (room reload mid-shot) re-add the player (the rig only updates while it has a target — it would freeze looking at nothing). Resting zoom is captured LAZILY (the rig's value only settles once the room generates). Offset/zoom are global state on a rig that outlives the room — ResetAll is called where the lighting override is cleared. Effects are BiomeConstants' post tweens; pulses run out AND back over the duration (a sequence never leaves the screen stuck in an effect it forgot to undo — the letterbox is the exception: bars are a state, hence two separate actions); each tween is told its start as well as its end (the return leg is exact, not a guess at drift).
- Cutscenes via the game's MMVideoPlayer (prefab, fullscreen surface, skip prompt, menu blocking all vanilla; only the source differs — a file by URL). The HUD is hidden by US (vanilla's callers hide it themselves — a trigger cutscene came up with health/XP bars on top). Room music/ambience PAUSED, not stopped (puts the track back exactly where it was; a paused instance still reports PLAYING, which keeps the blueprint music watchdog from restarting the track over the cutscene). The player is configured BEFORE Play (Play starts the video in the same call — no moment afterwards that isn't too late); prepared first, then played (a URL source started before it's open shows black frames); PlayFromFile does Play's setup itself (calling Play then correcting raises an error on the missing source that ends the cutscene). The MMVideoPlayer statics must be set (its own Update reads them — without them skip does nothing and the end is never noticed); `-=` before `+=` on loopPointReached (the persistent player is reused — after N cutscenes the end handler fired N times). Teardown lives in a FINALLY — a scene change kills the coroutine mid-video and finally is the only block Unity still runs; without it the FMOD stream stayed open, the music bus stayed locked (global engine state), and the video camera stayed parked off the room for the session. ClearSurface (the prefab's render texture holds the last thing drawn; a video that doesn't cover it exactly leaves that showing round the edges). RestoreVideoCamera on the throw path too (_movedCamera is static; left set it blocks every later cutscene's isolate AND leaves the world invisible).
- **The line down the middle of every cutscene:** the video draws on the camera's near plane (renderMode CameraNearPlane) and that camera is REAL — it also renders whatever world geometry falls in its frustum; from where it sits a room outline or gizmo projects to a hairline (not part of the video, not UI — why the canvas scan found nothing). Emptying the culling mask takes the video with it (the near-plane quad is drawn as part of ordinary rendering — a camera culled to nothing draws nothing). MOVE the camera instead: it renders as before with empty space in front, and the video rides the near plane with it. The prefab camera also carries a Stylizer that draws a seam here (vanilla's cutscene scenes never show it) — switched off by NAME (Stylizer lives in an unreferenced assembly; only the Behaviour switch is needed) and put back.
- Companion audio: Unity's audio engine is compiled out (sample rate 0), so sound is an FMOD stream (streamed, not loaded — minutes long; 2D — it comes from nowhere) started with the video (a long video and a short track simply end apart). Routed into the game's music BUS when possible (music slider, pause duck apply for free) — a Studio bus only has a core channel group once something locks it, and flushCommands waits for Studio's own schedule; fallback is master×music applied per-frame while playing (so sliders work mid-play). SilenceVideoTrack: the video's own audio track is switched OFF (nothing routed through Unity audio is heard; leaving it enabled invites backend warnings).
- Screen text is the mod's own canvas: HUD_DisplayName was wrong three ways (forces uppercase, two positions neither where a caption belongs, one line — title and subtext couldn't differ in size). Font is the game's FiraSans SDF (the intro's "a game by Massive Monster"; loaded all session because the HUD uses it — found among loaded font assets, not shipped). One overlay reused (a second caption replaces, never stacks); rebuilt when a scene load takes it (correct — last room's text has no business here). Sorting 4000 (above HUD, below the editor's 5000+). Dim is 0.75, not black (read the text OVER the room). No GraphicRaycaster (scenery — one would eat clicks meant for the game). Fixed reference resolution (a font size means the same on every display). FontWeight explicitly Regular (weight variants aren't shipped — thinning is done where it works); soft shadow keeps text readable on any room colour.

### Screen text font and trigger tool (TriggerScreenText tail, TriggerTool.cs)

- Thin() must read `fontMaterial`, not `fontSharedMaterial` — shared would edit the game's own FiraSans material and thin every piece of text set in it, HUD included; fontMaterial hands back a per-object instance. Fades are unscaled (sequences run while the game is paused around conversations). The font-search failure latch only holds while the failed answer is current — a font found and later unloaded reads as fake-null and deserves a fresh search, not a lifetime of fallback captions.
- Trigger firing mute (`_mutedUntil`): the hub arrival carries the player across (and onto) authored volumes — an entry then is the session's doing; a control-locking sequence tripped mid-stride queues SetInactive onto its own end (a frozen player). `_inside` stays false through the mute, so a sequence on the spawn trigger itself fires the moment the mute lifts. On editor close, `_inside = AnyPlayerInside()` — standing in a volume at F4 time must not count as an entry when play resumes.
- Volume colours mean things: cyan = fire-once (the default and common one), violet = re-arms per entry; each keeps its own hue after firing, dimmed — a spent once-trigger is FINISHED, a fired repeating one is merely resting, and painting both grey said the first thing about both. RefreshTint exists for changes that alter what a volume IS (Fire once toggled) without touching visibility. The blank sprite is one world unit square so the fill scales straight from the trigger's size.
- **Blocking volume = an invisible wall, and it is built by NOT joining anything.** A child "Blocker" carries a solid BoxCollider2D on the Obstacles layer, sized with the volume. Standing alone — no composite, no rigidbody — a collider is a static solid that pushes whatever touches it (the trick DoorTool's plugs use to seal a doorway); merging it into the room composite would do the exact opposite, since that geometry is Outlines and anything joined to it becomes walkable floor. Obstacles is the layer the player, the enemies and the grid graph all read as terrain, so one box stops both parties — navigation is rescanned when the checkbox flips and when a blocking volume finishes a move/resize, never per drag frame. Red outline, and blocking beats the spent/fired greys (a wall that happens to have fired is still a wall) while selection yellow and the firing flash still win. The collider is live while editing too: the clock is stopped, but a wall drawn over where the players stand will shove them when play resumes.
- One sequence owner globally; ResetSequenceState on room teardown (a coroutine that died with the scene must not leave the global owner set, or players InActive). Fire() returns whether the entry was consumed (false = deferred, retried next frame). `_syncingWidgets` guards panel-from-selection writes (a widget's change callback must not write back into the trigger it is displaying). No width/height sliders (the blue corner node resizes the volume ON the volume; the size is on the readout line). No Delete Selected / Clear All buttons (Del deletes; the room-wide wipe lives in the Clear tool). LiveCount is public for the Clear tool's arming label.
- Action authoring: a two-stage flow (pick action → the shared target dropdown asks the next question; Seconds is shared by everything ending "for how long"). Target keys are separate from display labels (an NPC's display name is not its registry key; a music label is the short name, the action needs the full FMOD path; vanilla lighting's key is the empty string — a blank pending target is valid). Custom cutscene folder is listed first (a custom video shadows a vanilla one of the same name). Move-target pickers exclude the trigger's own volume (walking players back in would loop) and our own volumes (gizmos, not scenery to walk to). Look-at needs a duration after the object; move-to is complete at the object. The picked object's position is kept as the fallback for later loads. Chained text prompts open from CLOSE, not confirm — two name menus alive at once broke the modal state (shortcuts fired under the dialog being typed into); cancel is a valid answer and the duration question is asked either way. Target highlight rebuilds bounds every frame (targets move) but the scene-wide Resolve sweep is throttled to twice a second; a trigger target uses the volume's own rectangle (no renderer); a missing object marks the authored fallback position. Escape backs out of offset-capture and object-pick modes before the editor (states the author must be able to leave without leaving the editor). No placement while framing an offset (the world is the viewfinder). Offset is stored relative to the follow target (at run time the players are elsewhere).
- Ctrl-drag clones a trigger WHOLE (size, flags, the whole action sequence — rebuilding a ten-step sequence to put the same thing in two doorways was the slowest job in the tool). The clone round-trips through the saved form rather than field-by-field copying (it cannot quietly share a reference with its source, and a later-added action field cannot be forgotten). Self-referencing action targets are re-pointed at the copy; targets naming OTHER triggers are left alone (references to elsewhere in the room — the copy means them as much as the original). PickAt returns the smallest volume containing the point (a trigger drawn inside another stays pickable). RebuildActionList never runs from the drag path (it would rebuild rows every frame of a resize). One undo entry per gesture. Handles are screen-space grips, constant size at any zoom, registered blockers; Ctrl on a handle is a clone gesture (they cover the volume they belong to).

### Vanilla chrome and widgets (MapEditor/VanillaChrome.cs, VanillaWidgets.cs)

- VanillaChrome: photo mode's nine-sliced plate is the panel backing — not in the scene (photo mode loads its UI on demand), so fetched from the same addressable the game uses, handle kept for the session and never released (releasing unloads the sprite the panels draw). Nothing is required: panels keep the generated plate until the load lands, and already-built panels are re-dressed in place (the dressed list drops destroyed entries per pass). The plate is found by PATH from the prefab root ("Controls/Background" — "Background" alone is also what both sliders call theirs, and shape can't tell a control's backing from the panel's); the fallback for a moved plate is shape (nine-sliced borders = a stretchable panel background; decoration isn't) excluding full-screen frames and layout-filled rects; everything the prefab draws is logged once so a wrong pick can be NAMED. Trim(): "Rough_WhiteSquare_bottomaligned" sits at the bottom of its rect with empty space above — a plate stretched to a panel draws short of the edges; textureRect (where the art is) vs rect (the authored size) records that padding, so re-cut the sprite to the art. Art shipped at alpha 0 (faded in by a tween) is corrected; a translucent plate is a design and left alone. DontUnloadUnusedAsset on the sprite (the atlas is held by our handle; the sprite must survive resource sweeps between sessions). The status bar reads Tint rather than blackening itself.
- VanillaWidgets: rows come from UIManager's settings menu PREFAB (a plain serialized reference — always loaded, no addressable wait). COTL_API's equivalents are internal AND capture from a live settings menu (null until Settings is opened once, dangling after it closes). Row indices are positional — each is verified for its component (a game update reordering them would hand back the wrong widget silently; neighbours are checked too). Look() retries while empty (a session opened before UIManager was up shouldn't settle for plain widgets forever); only the logging is once. The pause menu's ButtonBackground is ONE plate behind the whole column that moves to the selected button — only its art is wanted. MenuHighlightRibbon is the title-screen fallback (no PauseMenuController until a save is entered; the main menu draws the same ribbon behind its selected button); scene-local, which the cache survives (Look retries when the cached ribbon is destroyed). QuietRibbon: the pause ribbon is 'Button Red' — red PAINT, not white art with a tint, so a list row cannot be quietened by tinting (that makes darker red); the game draws the same shape in grey. Art() finds sprites by name among loaded atlases (not addressable, on no held prefab — but the menus brought their atlases in); once per name, found or not; DontUnloadUnusedAsset.
- Borrowed-row mechanics: template toggles arrive wired to a setting (clear OnValueChanged); Snap() redraws without animating (called by name — private on the game's side) because the control only redraws on a CHANGE, so one starting on the prefab's shipped value wears the wrong face until first clicked. PushToEdge: a settings row splits width equally between label and control (right for a full-screen list, adrift in a narrow panel) — give the label the slack; only the toggle (the slider's bar is meant to stretch). The split node is the FORK (lowest node holding both label and control — they are not always the row's direct children, which is why aiming at the root did nothing); widths are MEASURED after ForceRebuildLayoutImmediate (rects hold nothing meaningful before the row's runtime layout; a row that can't answer keeps the game's split rather than collapsing). WholeRowClicks: every graphic in the borrowed row stops raycasting and one plate takes all clicks — the name, pill and wrapper all raycast in their own right and a child graphic is picked before its parent's; routing the pill through the plate also leaves the toggle's own button nothing to receive (keeps it from taking the keyboard). Unfocusable: Selectables holding focus answer the keyboard (arrows walk a slider, submit flips a toggle) — focus is handed straight back; the game's OWN navigator is the other half — MMSelectable_* scripts answer it whether or not Unity thinks anything is selected, so they are switched OFF (not removed — something may hold a reference).
- Slider maths: MMSlider rounds every value to a whole number of increments and the increment is an int — a 0-to-1 slider driven through it can only be 0 or 1. The row gets a whole-numbered range (200 steps) and the real value scales in and out (MapEditorSlider.Scale); the readout is told the caller's units. SetValueWithoutNotify, not the value property (that one rounds — and this is a value the caller holds, not one the player dragged to).
- Scaled rows: a localScale alone is not enough — the column controls children's width, so a scaled row is laid out at full width and drawn at half of it; the container takes the layout and the child is sized to container/scale (MapEditorScaledRow, refit on OnRectTransformDimensionsChange). The row's INTERNAL layout is left exactly as the game drew it (overriding widths from clone-time rects collapsed the slider to nothing). Readable(): the row shrinks, its writing must not — each label is given back the size it loses, in row units, landing at 18px on screen (a point above the editor's 17: the settings face is lighter and reads smaller at the same size); auto-sizing off (it would undo this immediately); word wrap off (a settings row wraps and grows, ours is fixed height — a second line draws over the row below); name gets Ellipsis, a slider's readout gets Overflow (a clipped name still reads, a clipped number does not); fontStyle Normal (the slant is a style the row's TextStyler would put back on next enable — TextStyler exists to strip styling for accessibility, the very thing being done). Rescaler(): MMTextScaler re-applies the size the text had when it FIRST woke, on every enable — and option columns are built, hidden, and shown per tool pick, so setting size alone held only until first open; the remembered size is moved to ours (not torn out — the player's text scale still multiplies). Localize components are removed (a key the game doesn't know draws as the key). Row hover lights the writing (the editor's rows have always lit — a borrowed row that stays one shade reads as disabled); the handler sits on the CONTAINER (pointer-enter on a child reports to ancestors — one handler covers the row however deep the cursor lands); repainted on enable (the accessibility pass can repaint when a panel is shown again).

### World map visuals (MapEditor/WorldMap/CustomMapSkin.cs, CustomNodeVisual.cs, CustomLineVisual.cs)

- CustomMapSkin harvests the game's "DLC Map Menu" prefab so custom maps read as part of the game — visuals only; every vanilla script is stripped (they expect the DLC menu, its save data, its authored graph). Clones are cut and stripped under an inactive holder (Awake runs the moment they're active); StripLogic uses DestroyImmediate (a deferred destroy still runs Awake when the clone is parented the same frame). `Lost` detects the harvest source being unloaded (closing the vanilla map unloads the prefab, taking references with it — the next map re-loads and re-harvests rather than drawing from dead sources). The prefab is an addressable the game only loads when the DLC map opens — the first custom map of a session waits for it. One node clone per style (nodes of a type differ only in position and wiring). Cloned art has raycastTarget=false (the node view owns one click target; art must not shadow it). A missing FMOD event is not worth a broken map screen.
- CustomNodeVisual mirrors DungeonMapIconContent's state visuals exactly (the art is built around them: the outline material carries the glow; the icon tint separates beaten from fresh). Key/Lock/home nodes keep their authored icon colour (tinting a lock green reads as a bug, not progress). HideMenuFurniture: the "you are here" pin and quest alert are hidden by the vanilla node's own Start — one of the stripped scripts — so hide them by hand or they show on every node. Editor marks ride the node's outline (red selection, green gate member); **the vanilla "selected" outline material carries a warm tint that multiplies with the mark — green through it came out dark red**, so only the selection wears it and other marks go on the plain outline. An affordable lock pulses (worth looking at); unaffordable is scenery. ApplyEditView draws every node as reachable (placement is what is judged). Pulses run unscaled (the map holds the world at timeScale 0).
- CustomLineVisual: four authored line renderers (one per state), exactly one shown; the vanilla component reads its endpoints off DungeonWorldMapIcon components we don't keep, hence dropped. Endpoints are in the connection's local space, kept aligned with the node container so anchored positions hand over as-is. Re-placed on every OnEnable (the prefab rebuilds its mesh from the points and a canvas resize can drop it).

### World map file/layer tools (WorldFileTool.cs, WorldLayerTool.cs first half)

- One Save button that is also the rename (dialog opens on the current name; confirming saves, changing saves a copy; Ctrl+S is the same save with no dialog). CopyArt after a rename (a map is its folder — the new name is an empty folder until the art follows). Every new map starts with its home node (nothing selectable means nothing testable).
- Layer tool: corner-node resize (scale follows pointer distance from the layer's centre relative to the grab), the other corner rotates. Adding art is two steps (kind, then file — the second list stays filled across panel rebuilds, so adding two sprites is two clicks the second time). Asset lists are re-read on every build (files can be dropped mid-session); WorldMapAssets failures are remembered (a missing file is not re-read per rebuild) and cleared on refresh (a dropped-in or fixed file is seen).

### World map editor and screen (WorldLayerTool tail, WorldNodeTool.cs, WorldMapAssets.cs, WorldMapEditor.cs, WorldMapHandle.cs, WorldMapLine.cs, WorldMapNodeView.cs, WorldMapScreen.cs, WorldMapSelectionFrame.cs, WorldMapProgress.cs)

- Layer list: front at the top (how a layer stack is read); "+" brings forward and moves the row up; sort orders are RENUMBERED from the list on a move (authored gaps and ties cannot stall it). Orphaned drags (release eaten by a modal) are dropped, never resumed against a stale anchor. Right-click picks pure front-most, ignoring the selection's priority — without it a selected backdrop is under the cursor everywhere and left-click can never reach anything in front. Ctrl-click clones what's under the cursor and drags the copy off it; clones copy through JSON (a field added later is carried without editing the clone code) and are drawn BEFORE the drag begins (the drag moves the new rect by reference). HitLayer prefers the selection wherever it sits (a layer behind others stays draggable). Grab areas never shrink below half the selection frame's floor (a spine layer's rect says nothing about what it draws — scaled to skeleton units it can be a pixel across).
- Node tool: "None" target kind reads as unset but is a deliberate choice. One icon list for the seven frame types plus the folder's pngs — picking a png keeps the TYPE (Key/Lock/Base still mean what they mean, wearing a different face). Destination sits with type (not two headers down). Del deletes (a button sat one slip from the sliders). RefreshLinks during a node drag is cheap (aims existing rects, builds nothing) and without it the node leaves its lines behind until mouse-up — after the moment you needed to see the shape. Rename: one name for both jobs (display and derived id); every in-map reference follows the rename, progress keyed on the old id does NOT. Deletion undo remembers everything that pointed at the node (owner + index). Node picker list exists because nodes overlap; BlockWorldClicks when picking (same-frame click reaches the map underneath). Hubs are levels on disk but offered under their own kind.
- WorldMapAssets: everything built here must be Keep()'d — no file backing, and UnloadUnusedAssets on every room change would free it. Negative results cached (missing files not re-tried per rebuild), cleared by ForgetFailures (the editor saves over art while iterating; a stale negative hides the fix). Spine layers: scale 1, not 0.005 — SkeletonGraphic maps skeleton units onto RectTransform units, and this game authors skeletons ~200× what a 1080p canvas wants, so the game's factor is folded in (a layer scale of 1 means "the size the game draws it"). unscaledTime on graphics (the world is paused under the screen). SkeletonGraphic renders a single texture page only.
- WorldMapEditor host: selection by ID, not reference (rebuilds replace the objects underneath). Tool order: nodes first and selected first (the map is made of nodes; layers dress it, file is housekeeping). Attach(this), not null — passing null dropped hover lines and blocker rects on the floor. The hovered thing keeps the bar until the cursor leaves; map hover is POLLED (nodes are picked geometrically, not through the event system) and skipped over panels (widgets report themselves — stepping in would wipe what they said); re-shown when the bar fell back to the status message (leaving a widget with the cursor still on a node reads the node again). Quicksave arming as the room editor (NoteSaved from the File tool's dialog). Update errors throttled per interval. Dock metrics copied from the room editor (the two bottom bars line up); options panel top-anchored with explicit height (collapsing shrinks it to its own header — it covers a quarter of the map, and placing something under it should not mean leaving the tool). Blockers deduped AND pruned walking backwards (dropdown floats register per open, destroyed on close — an append-only list gains a dead entry per open). BlockWorldClicks is a 0.2s window (a widget click and the tools' polling see the same frame; a dropdown list stands outside every registered rect). RequestOptionsResize = 3 frames (Destroy end-of-frame + staggered fills). The selection ring toggles the GAMEOBJECT, not the Image (CreateIconButton's border starts switched off — enabling the component alone left it invisible).
- Handles/frame: corner handles hang from the screen's gizmo root (nothing the map draws can cover them); tools do the dragging — a handle only says where it is and whether the pointer is on it. One shared screen-space box (WorldMapGizmoGeometry) for frame, handles and picking — a layer must never be outlined in one place, grabbed in another, scaled from a third. A background layer's true corner is off in the dark — the handle rides the corner until it leaves the screen, then holds at the edge on the same side of the centre (the drag still reads the same). On an overlay canvas a world position IS a screen pixel. The selection frame is NOT parented to the layer (it would draw at the layer's depth and anything in front would cover it) — it copies the transform per frame (centre from the rect, not the pivot; localScale including a flip's negative x, which only mirrors the box); edges are divided by the layer's scale (even weight on screen); it has minimum screen sizes (68px, 110px for spine — skeletons draw well outside the rect they report) and the frame's floor is also the tools' grab box (what is outlined is what can be clicked). A redraw replaces the target; the frame/handle notices and destroys itself (the screen makes new ones).
- WorldMapLine: raycastTarget=false (a raycast-catching line shadows the nodes under it); the link's origin sits on the container's centre exactly as the nodes' do (endpoints are handed over as anchored positions). NodeView: the click target is OURS in both modes (the vanilla node's button belongs to the DLC menu's navigation, which this screen is deliberately outside); ApplyScale is live and re-asserted on every state pass (a redraw can never drop back to authored size); the editor's marks: vanilla art wears them on its outline, our disc art tints. Hover pulse unscaled.
- WorldMapScreen: sorting 4500 (under the F7 panel's 5000 and vanilla dialogs, over the HUD). Only one of the three pause-owning systems runs at a time (room editor check on Open). The backdrop swallows every click that misses a node. The gizmo root is the LAST child (marks draw over every layer and node whatever their order). The play-mode badge is permanent ("a line that fades is gone by the time it is wanted") and only shown once the editor has been used this session (a player travelling should not be told about F6). SetModalMode: the naming dialog is a vanilla menu on the game's own canvas BELOW this one — the map hides for it; the prompt runs the clock itself (scaled animations freeze half-open at zero), and the pause is re-pinned the moment it closes. The pause is REASSERTED per frame (a vanilla menu underneath can put the clock back). Unsaved work = JSON comparison. The scene can change under an open map (cutscene, death warp) — close on sceneLoaded. First map of a session waits for the DLC art addressable (rather than opening in our visuals and popping). StepBack is the one way out (Esc and X share it). RebuildVisuals tears down and rebuilds whole — assets are cached in WorldMapAssets, so it is GameObjects only; layers back to front (sibling order is draw order); RefreshLinks is the cheap half (two positions read, a rect aimed). ApplyBackgroundColor repaints without rebuilding (colour sliders fire per drag tick). Node clicks: locked nodes are the one PROMPT left (keys are spent for good; the vanilla map has no equivalent gesture); completing-on-select for destination-less nodes; hubs travel through the hub session, not a CustomDungeon; entering a hub ABORTS tracking (a hub has no success path). Entry order: bind run state first, then the dungeon carrying it; close the screen FIRST (transitions and scene loads need the clock running); BeginTracking AFTER EnterDungeon returns (its first act aborts stale tracking — it would eat this one).
- State resolver: completed nodes radiate; opened locks count as completed; **Base radiates from the start — it never completes, so without this no run could ever begin.** Count gates clamp last, after the cascade. Locked ranks WITH Selectable (neither pulls the other down where branches meet); a closed lock at the frontier previews what lies beyond and goes no further. WorldMapEditorBridge is the screen's only seam to the editor (a build without the editor still shows maps).
- WorldMapProgress: per save slot, never in the game's save; slot checked on EVERY read (no reliable "another save was loaded" hook); written on every change (a crash cannot cost a beaten run). Keys bank on COMPLETION, not click. Only the run the map launched may complete a node (_pendingMap); AbortTracking is the first act of every custom dungeon entry (runs not entered via the map complete nothing); NotifyRunSucceeded is reached only from the success exit.

### F7 panel (ModUI/CultTweakerPanel.cs, first half)

- Built from MapEditorUI attached to NOTHING (Attach(null) — every host call is null-conditional), so it looks like the editor's panels without an editor. **PanelTimeScale 0.1, not 0:** the game switches player skeleton renderers off while paused, and a renderer that never ticks keeps drawing its last mesh — a fleece picked here was correct in the skeleton and stale on screen until the panel closed; a tenth speed keeps everything ticking while slow enough to browse.
- The host survives scene changes but the camera anchor and the room don't — close on sceneLoaded (a panel open across a load holds a pause and a follow target of a dead scene; drop the anchor reference first, it died with the scene). Refuses to open while the map editor is up (two systems fighting over camera/pause/HUD ends with the game unpaused and the camera on a destroyed anchor) or on the title screen (no players, and taking the camera would strand the menu). Content rebuilt per open (player two joins/leaves, mods register late, About counts are only true when read). Players are parked in the game's own cutscene state (TriggerActions.SetControl — held movement keys must not keep the player walking behind the panel); camera hand-over is isolated in try/catch (a rig that refuses must not leave the panel half-open with the game paused). Close: time first (the HUD's show animation needs a running clock); SetControl(true) unconditionally (whatever else went wrong, the player walks again). The 0.1 timescale is re-asserted per frame (a game menu underneath restores it). PlayerPreview.Tick per frame (the portraits are skeletons off the edge of the world; this is the frame they are filmed in).
- Dock rebuilds only for spine changes (a different spine offers different animations); a fleece re-dresses the portrait in place via OnLookChanged (rebuilding the dock for a change of clothes made it jump about) — OnLookChanged fires when a look LANDS on a skeleton, which is not when it was asked for (an unloaded spine arrives seconds later by callback). The three "open a thing" sections are one gesture three times (grown into three headers with five dropdowns); picking IS the gesture. World maps: New always opens an empty canvas under the next free name (not whatever was last saved as "untitledworld"). Hubs enter via the panel because the F4 editor can only be reached once the room it edits is standing; Enter rebuilds the saved hub to play (F4 edits from there); Author() is for a hub that does not exist yet (New Hub).

### F7 panel, continued (CultTweakerPanel.cs tail)

- Base section: no picker, no list — one base, the one the player stands in, file named after the slot; a button plus the WhyNot line. All three entries close the panel FIRST (editor/trip/map all take the pause and the screen; the map's guard refuses to open over the panel; a scene load would strand the held camera rig). Dungeon picker reads registrations at build time, not cached (saving a dungeon registers it immediately — the list differs next open); action dropdowns keep their caption ("Enter Dungeon") until picked — picking IS the gesture. DumpFollowerSlots: the button reads a follower standing in the world NOW and replaces the old dump; with no followers present it arms the config flag instead (the dump happens on the next follower dressed).
- COTL_API internals: CustomPlayerSpines is internal — read via Harmony Traverse (same approach as the enemy list); our spines come from the lazy loader's registry (present whether loaded or not), other mods' API registrations appended; "Placeholder/" entries skipped (the API registers one purely so its dropdown has an entry; selecting it does nothing).
- Free camera: pans by driving the game's rig through a dummy follow target — CameraFollowTarget re-asserts the camera position every frame, so writing to Camera.main is reverted. SuspendAreaCulling: whole areas deactivate when their precomputed bounds leave the viewport, which a roaming camera triggers constantly — the world would appear to delete itself. Pan on unscaled time (world at a tenth speed). Zoom through CameraSetZoom every frame, not the target-only call — the camera chases its target distance on SCALED time (a tenth here), so a target-only write crawls instead of arriving. Wheel routed by hand to our ScrollRects (Rewired's pointer module never delivers scroll to uGUI), back-to-front (an open dropdown is parented last and must win).

### Main menu editor (ModUI/MenuEditor/MainMenuEditor.cs, MenuAssets.cs, MenuButtonInjector.cs, MenuPresetApplier.cs, MenuSceneRefs.cs)

- A third IMapEditorHost, not a variant: no room, no simulation to pause, no player, no world to click. **timeScale is left alone** (the intro is driven by scaled waits and a scaled animator; the load menu's transitions too — freezing strands the screen). **The camera is not taken**: drift stops via CameraSubtleMovementOnInput's own blockMovement flag, never by disabling the component — its OnEnable re-reads the rest position from wherever the camera stands, walking the menu sideways a little per visit. Canvas 4600 (over the menu and the world-map screen, under the F7 panel). Tool order: presets first and selected first (which menu is being worn is the opening question). The working copy is LOADED, not shared with the applier's copy (edits must not reach the config's preset until saved); closing reloads the configured preset (back to what the player actually chose); Save also sets the config value (an editor saving to a file nothing reads would be strange to ship). ForceClose on scene load (the menu being edited no longer exists).
- TakeInput: UINavigatorNew LockInput/LockNavigation (the game's own idiom — the intro uses it), CanvasGroup.blocksRaycasts=false on the menu (a click that misses a panel otherwise lands on Quit), cursor forced visible (the most gamepad-likely screen hides the cursor on seeing a controller; an editor nobody can point at is not an editor). ModalOpen lifts the navigator lock while the name dialog is up (it is one of the game's own menus, driven by the very navigator we locked — holding it gives a text box you cannot type into) and re-takes it after. RegisterUiBlocker/BlockWorldClicks are deliberate no-ops — nothing behind the panels is clickable, so the registered rects have no consumer.
- RebuildPanels is queued for the NEXT frame: every caller is a button's click handler inside the subtree the rebuild destroys — doing it now tears the UI down while the event system is still walking it, and what comes back is an empty panel. Detach stale children before Destroy (end-of-frame deferral; a layout rebuilt in the same breath counts what is leaving). Scroll reset after rebuild (the column keeps its position and the rebuilt content is a different height — a scrolled-down panel came back showing the empty space past its own end, reading as the panel vanishing). Selection rings toggle the GameObject, not the Image.
- MenuAssets: everything Keep()'d (no file backing; the scene-change unload sweep would free it under the menu); negative results cached. AllPalettes finds every loaded Palette (the menu names three, but the comic menu, DLC intro and room managers carry their own); palettes without a texture are excluded (the shader reads Texture/Texture.height unconditionally once HasTexture is set). **Recoloured: one private palette rewritten in place** — never a new asset per slider step, and never the game's own (a ScriptableObject shared with every gameplay scene); cached on (source,h,s,b) because the applier re-runs the whole look per widget change — without the cache ANY look slider drag re-did the per-pixel loop and GPU upload at 60Hz. Shifting the palette's lookup table is the only way to change the gold field (the Stylizer maps the whole screen through it). Palette textures are import-time assets, almost never readable — GetPixels throws; blit through a RenderTexture to read one anyway. GetSkeleton: the menu's skeleton is world-space at 0.005, SpineFolderLoader's default, so folder spines drop in at the right size. Forget() per menu load (art changes on disk between loads; a renamed preset carries copies).
- MenuButtonInjector: clone a SIBLING so hover animation, font, plate and MMButton come along — MMButton is not optional (UINavigatorNew resolves "FindSelectableOnUp() as IMMSelectable"; a plain Button is invisible to the controller). Idempotent (the hook can fire twice within one menu; a second button is worse than none). Template is Achievements, NOT Quit — Quit is the one Explicit-navigation button and a clone of it holds hard-wired neighbours that are not ours; siblings navigate Automatically. Placed directly above Quit (after everything the game offers, before the way out). **ForceRebuildLayoutImmediate on the column immediately:** one more row moves every button, and the menu highlight places itself by reading a button's world position one frame after selection — a still-shifting column leaves it over a neighbour. IN_DEMO hides it (belongs with the buttons EnableDemo takes away). Delocalise: DISABLE the Localize first (OnDisable unhooks the localisation event; Destroy doesn't run it until end of frame — I2 would put the template's term back on enable and on every language change). onClick.RemoveAllListeners (runtime listeners aren't copied by Instantiate; serialized ones are). Quit's Explicit navigation still points up at whatever used to be above it — Navigation is a struct: read, change, write back whole.
- MenuPresetApplier — the two passes: A on scene load before MainMenuController.Start (title, centrepiece, scene effects); B after StartInput (an animation event at the intro's end) — by then Start has run `LerpPalette = 0` and EnableSecondPalette(), so anything on the Stylizer before that moment is gone; the look goes on in B. Each area in its own try/catch (a broken spine costs the centrepiece, not the menu). Palette goes in SLOT TWO, never one (the menu already runs a two-palette blend at zero — Blend is the game's own crossfade and zero is exactly vanilla); assigned, never nulled (DitherRender dereferences Palette2 every frame regardless of UseSecondPalette); the shift is applied to a COPY. The theme tween (isMajorDLC blue theme) drives the very value we write and the load menu re-triggers it per open/close — isMajorDLC is read by nothing but Show/HideBlueTheme, so clearing it makes both permanent no-ops, and the live tween is killed by hand (a DOTween.To over a captured local that DOTween.Kill(target) cannot reach); only while WE drive the palette (a DLC player not overriding keeps the theme they paid for). **No Pixelate, ever** (re-presents the camera through a quad, breaks the menu's click raycasts — the hover-offset bug).
- Background: the dark-mode overlay is the ONE thing that repaints the gold field alone (the palette maps the whole screen — shifting it takes the lamb and text along); its blend ignores the image's hue (why the old colour sliders did nothing) but reads opacity — alpha only. The DarkModeObject beside it re-asserts "enabled" from the accessibility setting via a static event it stays subscribed to whether or not enabled — destroying it is the only way to hold the override (cost: dark mode stops reaching this menu until the scene rebuilds, which quit-to-menu does anyway). Clear colour: the camera clears to pale cream and the Stylizer maps that into gold — this one colour IS the background (the key art is floor and candles; every full-screen canvas image is an overlay on top). With dither on, the result snaps to the nearest palette colour; effect strength below 1 lets the raw colour through.
- Centrepiece: DressNeeded change-detection (Initialize(true) rebuilds the skeleton and restarts its animation — under a slider drag the lamb stutters in place); cache keyed on BindCount (a fresh menu has a fresh lamb wearing the prefab's dress, whatever the cache remembers) and stored in REQUESTED (unvalidated) form (a preset naming a missing skin warns once, not per tick). Dress recipe: validate first (SetSkin throws on a missing name INSIDE Initialize, with the object half built); swap the asset on the existing skeleton (inherits the menu's placement, rotation and LOD free). Scale multipliers are component-wise (the menu authors the lamb at (2.47, 2.47, 1.23) — a uniform multiplier reshapes it).
- Title: plain art with no script re-asserting it (the scene's ChangeLogoPerLanguage is on the press-any-button screen) — nothing to disarm. PreserveAspect keeps HEIGHT fixed (a wider replacement grows sideways instead of being squashed). Detach (LayoutElement.ignoreLayout) while an offset drives it — the logo sits in the menu's layout chain and a layout group rewrites child position/size on every rebuild (the offset sliders would appear dead); the baseline was captured after layout ran, so detached at offset 0 the title stays exactly where the game put it. Edition: no master switch — each field's neutral value means "the game's own"; ApplyEdition refuses to run before LateCaptured (it also runs in pass A, when the baseline is an empty struct — writing that over the live line then CAPTURING it as vanilla is how the edition text vanished); I2 Localize disabled-then-destroyed (same reason as the button); the edition moves via the same Detach mechanism; revert re-attaches FIRST, then the numbers (the group owns them again).
- MenuSceneRefs is the only place that knows the menu's shape (a game update costs one file). Every getter nullable, every caller checks (the menu is rebuilt per quit-to-menu). It holds the BASELINES — what makes "offset 0, scale 1, blend 0 = exactly vanilla" true; the Stylizer is scene-local so restore is belt-and-braces, but the Palette assets it points AT are shared and persistent — never written. **LateCaptured:** binding happens before MainMenuController.Start, and Start has opinions (LerpPalette=0, EnableSecondPalette, glitch off; the intro switches DLC snow on and flashes the red overlay) — reading before Start records the prefab's opinion, and "restoring" writes stale values over the game's own (the palette-overridden-at-launch bug). The whole look reads at the start of pass B. RestoreLook refuses before LateCaptured (writing the struct's zeroes over a live Stylizer blacks the menu out). The title binds in CaptureLate too — UISubmenuBase.Awake calls Hide(immediate:true), so at scene load the whole menu is off and a search insisting on a live image finds nothing (searches must include inactive). Stylizer = Controller._stylizer, never Camera.main (the scene holds TWO Stylizers — menu camera and comic camera; Camera.main is a coin toss). TitlePath is named outright because searching cannot win (five full-screen overlays and save-info plates all outsize the logo); Guess() is the fallback for the day the path stops being true — biggest canvas sprite that is not furniture (small sprites) or a full-screen overlay (≥1820×1020), with active beating inactive (a logo for another edition loses to anything on). _keyArt is the world set (SpriteRenderers — an Image search finds nothing); the title is on the canvas. Edition text is read off the controller by name (the game holds it; the theme methods recolour it). Background = "DarkModeMenu" (_darkModeInvertImage — declared on the controller and never read; only the DarkModeObject drives it, which makes it exactly the lever). BindCount keys every "what I last applied" cache (a fresh menu — same assets, new objects — must never be mistaken for the cached one). InMenuScene is computed, not a flag (a scene load can happen without this file hearing). One log line per bind: which object each area landed on, or MISSING — what to look for when a game update moves something.

### Player dock and portraits (ModUI/PlayerDock.cs, PlayerPreview.cs first half)

- Cards, not rows in the long panel: the controls belong to a particular player and no single-column reading order makes that obvious (the old panel's four headers meant "whichever header you last scrolled past") — the picture is the label. Players 1 and 2 always have a card (the panel is how player two's look is set up BEFORE they join; a card appearing/disappearing with a controller is worse than one saying "not in the game yet"); 3 and 4 stay hidden (nothing about them can be set until they are here). The card wears the game's plate; nested boxes stay plain (planks on planks otherwise). Boxes size themselves by content (every fixed height was a few pixels short — lists spilled out). `_awaiting` is STATIC (the dock is torn down and rebuilt while a spine load is still going; the answer must outlive the card that asked) and is recorded BEFORE the load starts (rebuilding only from the landing callback meant the one state it was meant to report — waiting — was always already over; the selection doesn't change until the load lands, so it can't be read back either).
- The animation box drives the PORTRAIT's skeleton only — PlayerFarming's state machine owns the live player's AnimationState and would overwrite anything set there on its next state change (the whole reason the portrait is its own skeleton). Transmog OFF asks for a skin rebuild explicitly (a plain SetSkin the loader knows nothing about — announce the redraw here); transmog ON goes through ApplyFleece, which announces itself via LookChanged when the fleece actually LANDS (seconds later for an unloaded spine) — so no redraw is requested at pick time. Spine pickers only for players 1–2 (COTL_API tracks a selected spine for two players; a third player's choice would be written into player one's slot). RememberSpine writes the choice down as well as applying it (the API's selection doesn't survive a restart). Captions measure against the width the box WILL give (the general helper measures the current width — right in the wide panel, wrong in a narrow card: a two-line caption got one line of height); and the rect itself is set (these layout groups don't control child height — they read the rect, not the LayoutElement).
- PlayerPreview — three constraints, each ruling out an obvious answer: (1) it must render whatever the spine is — SkeletonGraphic goes through one CanvasRenderer (one texture, one material): a cross-atlas fleece loses everything but its first page, and custom spines are built against the Spine/Skeleton MESH shader, which a borrowed UI material renders with wrong alpha — so SkeletonAnimation + camera; (2) it must animate on its own — filming the live player would mean driving the live player; (3) it must not become part of the game — built by SkeletonAnimation's factory (bare GameObject: MeshFilter, MeshRenderer, SkeletonAnimation; no PlayerFarming/Health/UnitObject/collider — nothing counts, targets or sees them: not PlayerFarming.players, not Health.team1, not the room-lock check). Cloning the player's GameObject WOULD have done all of that — built, not copied. Staged at (12000,-12000), own layer, only this camera has the layer. Supersampled 2× to the box's shape (a square texture in an oblong rect is a squashed lamb). Tick on unscaled time (the panel runs the world at a tenth; a portrait is not the world).

### Player portraits, continued (PlayerPreview.cs tail) and banner

- Portrait skeletons are advanced BY HAND (component switched off; Tick calls Update then LateUpdate — the entire frame): Unity's own Update runs on Time.deltaTime, which the panel scales to a tenth, and compensating through SkeletonAnimation.timeScale would make the portrait depend on a global the panel is deliberately fiddling with. Untint retried per frame until it takes — the materials to override do not exist until a mesh has been generated (running it once straight after Initialize registered nothing and the portrait stayed unlit-dark). Animation lists sorted (authoring order is nobody's browsing order; sorting groups families by prefix). Redress touches nothing but the skeleton — the card's RawImage points at a render target updated in place, so a change of clothes needs no UI work (rebuilding the dock made it jump per fleece). Build keeps the skeleton when the asset matches (only the dressing changes); the factory, NOT Instantiate(player.gameObject) — that would make a second player complete with health, team and roster place.
- **The portrait shader story:** the world lamb is LIT (the game's lighting pass over the sprites); the portrait camera renders a private layer with none of it, so the same lamb came out dim and muddy (first guess — a warm tint being added — was backwards; the side-by-side put it straight). There is no lighting the portrait like the world without the whole rig, so go the other way: for CUSTOM spines (built against Spine/Skeleton) the stock shader is a free no-op; for the STOCK lamb the game's Skeleton_ASE_v1_SoftAlphaTest must be KEPT — the name is the whole story: it alpha-TESTS, discarding texels below the cutoff colour-and-all, while Spine/Skeleton alpha-BLENDS them back (the red haze on the ears and the red shape over the head — no blend mode was ever going to fix pixels meant to be thrown away). So the stock lamb keeps its shader and only the parts depending on where the player is STANDING come off (the woods fade is the one that made it dark — it tints the character toward an environment the staging corner doesn't have). ASE writes a toggle as keyword + same-named float — disable both or a shader branching on the float ignores the keyword list. Registered through Spine's CustomMaterialOverride, never by assigning the MeshRenderer (SkeletonRenderer rewrites sharedMaterials from the atlas on every mesh rebuild).
- **_TimeOfDayColor:** the biome's colours are SHADER GLOBALS (LightingManager drives it from the god-ray colour — torch-lit dungeon = orange lamb, correct in the world, pointless in a portrait). Setting the global to white around Camera.Render did not stick (something writes it inside the frame) — a value set ON THE MATERIAL cannot be raced (Unity resolves a material's own property before the global), so the portrait carries its own neutral copy. The globals set around the synchronous render are put straight back (they belong to the whole game).
- Dress: READ the live skin (the answer the fleece/spine choices already produced — simpler than rebuilding and incapable of disagreeing with the world). **Clear the skin first** — Skeleton.SetSkin early-outs when handed the skin it already has, and ApplyFleeceAttachments mutates the live skin IN PLACE, so the object is the same one every time and the portrait was told, correctly and uselessly, that nothing had changed (why fleece changes didn't show). Facing preserved (a player looking left keeps looking left). Only start an animation when nothing is playing (a picked animation survives a fleece change); prefer the spine's own idle over the live player's current animation — the players are parked in the cutscene state while the panel is up, so copying theirs copies a frozen pose. First() returns null rather than "the first animation there is" (a spine whose first animation is a one-shot death plays once and lies there). Frame from the skeleton's bounds ONCE, at dress — framing a moving skeleton makes the portrait breathe with the animation. Camera centred on the skeleton's middle (GetBounds is root-relative; a skeleton stands on its origin). Culling mask is only our layer (a camera that could see the world is one more camera rendering it).
- PreReleaseBanner: shape of the game's own PrereleaseWatermark, hopping corners every 10s (vanilla's dwell) so it can't be cropped out for long; unscaled timer (editors hold timeScale 0 — a stopped watermark can be parked and cropped). GUI.skin only valid inside OnGUI (style built on first paint); box padding trimmed (CalcSize adds it). Steam reached by name — the plugin doesn't reference Steamworks, and an offline player still gets the banner.

### Patches (AnimatorRebindPatches.cs, BaseEditPatches.cs, CustomEnemyPatches.cs, DungeonPatches.cs, HubBuildPatches.cs, MainMenuPatches.cs, SkeletonAssetPatches.cs)

- AnimatorRebind: AnimationReferenceAssets are ScriptableObjects shared between player instances (P1/P2 co-op) — never mutated; each (source, target skeleton) pair gets one cached clone, and the same source must always map to the same clone (SimpleSpineAnimator.Update compares asset references). Postfix on PlayerFarming.Start so it runs after COTL_API's prefix has swapped the skeleton (also fires on the OnEnable→Start hot-swap). Clones translate back to originals for cache keys (reverting to the Default spine restores the exact original assets). Runtime assets made by the game carry only a name, no animationName. COTL_API leaves the AnimationState on an empty animation after a swap, and UpdateAnimFromState fires only on a state CHANGE — restart the current animation by hand.
- BaseEditPatches — three jobs, all "invisible to the save": (1) the grid postfix (vs the hub's prefix that REPLACES the fill — the base's fill knows about DLC land and the bridge, none of it ours to reimplement; vanilla runs untouched and added ground is a second pass; hub regions skipped). (2) AddStructure interception scoped to mod ground ONLY (in a hub every build is intercepted because the whole room is ours; in the base almost every build is the player's business); BuildStructure also guards the building HISTORY — a save field feeding achievements/unlocks that a type first built on mod ground must not teach. CheckObjectives suppressed for our own placements (the saved base is rebuilt per arrival — quests would tick per building per visit). (3) The save mask hooks `SaveAndLoad.Saving` — the single main-thread statement that spawns the serializer thread; deliberately NOT the writer's generic Write method (the runtime shares one compiled body between the save file and the menu metadata — a patch there fires for writes whose completion is never heard).
- Base arrival spawn point: the obvious hook (LocationManager.PositionPlayer) is too many arrivals — stepping out of the temple, the door room and the shrine room are interior doorways with their own spots, and landing on the spawn point out of the temple door is teleportation, not arrival. Only the teleporter warp-in is hooked. The warp ties player placement, camera snap AND camera framing to the teleporter's transform — re-placing only the player left the camera watching an empty portal (OnConversationNext hands the camera an object to follow and holds it). The camera is handed a STAND-IN parked on the spawn point; the player is placed over several frames (the warp writes the player's position early in its own coroutine on an unreliable frame; the loop stops well before the player has control). The warp effect can't follow (an animation on the portal's own skeleton — plays off-screen; the player simply arrives). Only the teleporter's framing call is patched — every follower and shopkeeper conversation is left alone.
- Loader protection: RepairSavedPositions runs as a prefix on the ITERATOR STUB of PlaceStructures (lands before a single entry is looked at). EnsureWithinBounds (LocationManager): ordering insurance — our polygon is appended after arrival, so a structure the PLAYER moved onto added ground could be read back before its ground exists. Follower.EnsureWithinBounds: a PREFIX doing the same test first (a postfix can't see the decision) — without it followers standing on added ground are teleported to the town centre every ten seconds.
- CustomEnemyPatches: the one Spawn postfix covers every route (enemy tool, blueprint loader, custom dungeon SpawnEnemies) so JSON enemies are finished off without each caller remembering.
- DungeonPatches: LastDoorDirection feeds the next room's entry side; ResetRoomHandoff exists because a floor entered from the adventure map has no door to reset the latch (a latched GenCheck makes the room hook skip the level's first room). EnterNode postfix (not prefix — the vanilla body sets up the floor this adjusts; still early because Regenerate defers into an MMTransition callback). Room lighting re-asserts on BOTH SetRoom (early enough to happen behind the fade) and RoomBecameActive (arrivals that skip it) — generation alone is the wrong signal (rooms are built once and re-activated after; revisits announced nothing, so a custom mood followed the player out and revisited rooms came back plain); the tool dedupes the double announcement. Biome-up teardown: a dungeon entry that brings no level ends the run in progress (asking the dungeon — hardcoding CTLevelDungeon tore down a map dungeon's pre-scene binding); FollowerLocation.None = "no entry pending" (a re-enable must leave a run alone); lighting override, camera offsets, trigger sequence state, room hand-off statics (reset for EVERY biome — a vanilla dungeon after a custom run otherwise starts with the last door latched) and the room-lock net (armed for a room in the departed biome, it would reconcile doors it never watched) all clear here. StartWithBossRoomDoor is switched off for custom dungeons: PlaceEntranceAndExit appends a whole extra room at (-999,-999) and points StartX/StartY at it — the floor comes out a room longer and the arrival room is no longer the entrance (the podium room slipped to second place, and second place gets dealt a blueprint); only reachable under a random walk anyway. Dungeon entry hooks fire in the NEW scene's first moment (whatever the dungeon sets up cannot be undone by the old scene's teardown); the caption coroutine is hosted on the biome (dies with the scene it belongs to) and started by the patch, not OnBiomeReady (an override that forgets base cannot lose it).
- Door prefix: IsPlayerUsingDoor repeats vanilla's own filter IN THE SAME ORDER (the prefix used to act on whatever touched the trigger — followers, thrown items, knocked-back enemies, the player's scripted walk-in — setting the hand-off at arbitrary moments: blueprints re-applied to built rooms with doors visibly moving, and the exit door firing on arrival reopened the dungeon map the instant a node was entered). GoToAndStopping is the scripted walk (vanilla's arrival and the editor's own entry). The custom exit-door branch marks the door Used itself (vanilla marks it as it takes it; this branch never reaches vanilla — otherwise every further overlap re-runs the exit, the map reopening on top of itself). A dungeon-map node bound to a level plays inside a VANILLA dungeon — the hand-off must be recorded there too.
- Generate postfix: everything runs inside GenerateRoom.Generate's own MoveNext — an uncaught throw kills the generation coroutine and leaves a black, soft-locked room; bad content is worth logging, never a broken run. Harmony's enumerator patch supplies a null __instance in some invocations (seen on the boot entrance) — GenerateRoom.Instance is the same object (OnEnable assigns before Generate). CurrentRoom can lag the hook by a step at boot. CTLevelDungeon node blueprints apply for every connection type and DELIBERATELY outside the Completed guard (revisits regenerate vanilla content, so blueprint rooms must re-apply too). RoomLockNet.Arm only outside level runs — a level brings its own net (LockIfContested, which also knows what the blueprint spawned); two would fight over the same doors. LightingTool.OnRoomEntered here is the earliest a fresh room can get its own lighting (revisits come through the arrival hooks).
- HubBuildPatches: the vanilla build flow runs end to end; only the save-list filing is intercepted, scoped to builds driven by OUR region (the game restores the player's real Woolhaven buildings through the same call — deleting those costs them their town). `_ourBuild` is a depth (a build site's completion nests one BuildStructure inside another). X/Y_Constraints widened only while a hub totem stands (the placement loop clamps the cursor to the base's extents — a hub is elsewhere, so the cursor was pinned to a corner or off entirely; the ranches and sleeping followers read the same properties). PlacementRegion.OnDestroy clears the singleton whether or not it held it — harmless with one region per scene, not harmless when a hub copy is torn down while the town's sits switched off (the town would come back with no region); only ours is corrected. **The singleton swap is placement-scoped:** the player-prompt reads PlacementRegion.Instance.StructureType — in a hub that was the town's idle region, so the prompt showed the raw term "Place Structures/NONE". The singleton is NOT handed over for the session (twenty other readers, mostly follower tasks reasoning about the TOWN's grid — beds, composting); it is swapped for exactly the length of a placement, bookended by the edit-mode HUD bar (the placement routine raises/drops it and nothing else calls it; the clock is stopped for most of the window anyway). RestoreTownRegion also on hub teardown (a scene change mid-placement left the town's region unfindable).
- CheckTiles: the buildable squares are pooled from a template the region points at, and the pool hands a FRESH one back without switching it on (only recycled ones get that) — in the town the template is on, but a copy made while the town room is off can inherit an off template: every tile spawns invisible. Build patch covers the branch that hard-codes the base as location (a fence or rubble placed in a hub would land in the player's town). BuildStructure postfix catches the follower-finished end of a build site (while a hub is open nothing else can build in the Woolhaven room — the room IS the hub) and guards the building history. CompleteBuildSite: nobody lives in a hub — a site would stand unbuilt forever (a follower finishes it), so the moment it exists it is done; one frame later (the region stamps cell/bounds after the call). PlaceStructure repair: the game parents a finished structure to the location's "structure layer" — for the town room that reference is the room object the hub session cleared, so the parenting quietly became "no parent": buildings sat outside the room's sorting hierarchy, fought each other by depth alone (flat on one plane), and would have outlived the hub (nothing clears the scene root) — reparent to the hub's content root. ReportSettled waits out the drop-in tween (the game builds one unit back and slides forward over half a second on SCALED time — the placement session stops the clock, and an interrupted tween leaves the building at a depth of its own). Tile tint: vanilla's available square is white at half alpha (dark grey in winter) — tuned for the town's known ground; on an authored hub's ground the grid was a rumour, so the plain "may build" square is recoloured opaque cyan (red/green carry meaning and are already opaque); the region check is a reference compare (runs for every square, every frame). SetTile mirror: floor decorations never reach AddStructure — mirrored AFTER the fact (the game has already recorded them on the region's data, and that list is what is saved); only when the manager doing the work is the hub's copy. OnInteract prefix: MissingDependency check before the menu opens (a null inside the placement coroutine dies quietly with the player locked out) and RefreshOccupancy (never RebuildGrid — see HubBuildRegion).
- MainMenuPatches: StartInput has NO callers in the game's code — it is fired by an animation event on the "Intro" clip, once per menu load: the exact moment after Start and before the player can press anything, which is what both the look and the button want. Bind is re-checked there (a menu reached without OnSceneLoaded — the demo's own reload). ApplyStructural repeats in pass B because the menu is deactivated until this moment and the logo is only found by CaptureLate.
- SkeletonAssetPatches: the game refreshes some skeletons by round-tripping their asset — Clear() then GetSkeletonData() re-parses the JSON (Interaction_EntranceShrine.ReloadStatue does this to the player-dummy statues). Our runtime assets free their JSON once parsed (the file never changes; the cached data IS the file), which turns the round trip into "Skeleton JSON file not set" and a skeleton that never initializes again — Clear() is skipped for those assets. Plus a temp fix: the global Spine LOD manager updates even when its UI is not present.

### Spine patches (Patches/SpinePatches.cs, StructureSpinePatches.cs, WeatherPatches.cs)

- Fleece unlock writes happen inside a lookup accessor — only when something changes, never per hit. Custom fleeces join the cycle from the REGISTRY, not the loaded dictionary (a fleece spine is in the cycle before it has ever loaded). EnsureSelectedLoaded on player spawn: the API's saved selection may not have existed when the mod loaded — the worn look gets a second chance. **The re-SetSkin patch:** a spine hotswap (and respawn, via OnEnable) calls PlayerFarming.Start again; COTL_API's prefix swaps and Initializes, but the original Start returns at its own `if (StartComplete)` guard before reaching the SetSkin it ends with — so on every swap after the first the skin is whatever Initialize left (raw data skin, no weapon or chore overlay, and nothing triggering the postfix). StartComplete is read BEFORE the original (the original sets it); a first genuine Start already called SetSkin itself; limited to spines with a config.json (vanilla respawns unchanged). Co-op seats four; solo is pinned to player one — playerID is only meaningful once the coop manager runs, and reading it otherwise dressed the lone player from whichever seat's settings the field held. HideSlots always runs LAST (the fleece writes to some of the same slots, and the game-rebuilt skin must be stripped fleece or not). DisableFleeceCycling spines skip the fleece (it would write lamb artwork over their own body). The follower costume patch runs per follower per frame — a follower mid-spawn legitimately has null Brain/Info/Spine for a few frames; an unguarded throw surfaces out of Follower.Update per frame per broken follower.
- StructureSpinePatches: PlaceStructure is the one point both routes meet — BuildStructure calls it for a fresh build, InstantiateStructureAsync for every structure restored on location load — and it runs after COTL_API's sprite swap (registered on the addressables handle before the game's callback).
- WeatherPatches: WeatherSystemController.Update clears weather once the PLAYER'S saved weather expires — all values read from their save, which an authored map never touches, so a save carrying already-expired weather makes the test true on the first frame. Worse, in a dungeon the clear can't switch itself off (StopCurrentWeather only writes WeatherType=None when the location is NOT a dungeon), so it ran every frame — authored dungeon weather was wiped within a frame or two of applying, reading as "never saved". While a map holds the weather, that clear does nothing; the timer, the season default and the player's values all remain, and the moment the map lets go they take over.

### Plugin wiring (Plugin.cs)

- Fleece config keeps the NAME as well as the index — the index only means something after the in-game rotation is built, but the lazy loader must know at boot which spine the remembered fleece lives on to load it eagerly. SelectedSpine config exists because COTL_API holds the selection in a static it never persists — a look picked in F7 was gone next launch; re-applied at startup only when the API has no selection of its own. FleeceTransmog is per seat (a spine dressing its own body wants it off while the lamb beside it wants it on), seeded from the old global switch on first run.
- Startup order: fleece config bound BEFORE the spine loader (the saved name decides eager loads); LogWhatIsThere before any loader (the log says which mods bridge content before what loaded); cutscene folder created and listed (no loading — a cutscene is a file read when played), missing audio converted in background; enemies load with the plugin as coroutine host (prefabs load async); TrimRepackCaches at the END of the staged follower bake, NOT in Awake any more (repack scaffolding is pure memory, but trimming while the bake is still running would destroy the region cache every remaining skin is relying on). The boot scene's menu is dressed via a direct OnMenuScene call — sceneLoaded doesn't fire for the scene already running. The test enemy is deliberately NOT added to NormalEnemyList (it would auto-spawn in every dungeon room, in the way of editing). CTLevelDungeon registers AFTER the test dungeon — F5 enters CustomDungeonList[0], which must stay the test dungeon. CTMapDungeon.RegisterAll at startup (registration mints a FollowerLocation per map — once, not every time the tool lists them).
- Update: PumpWarmUp (hands finished background parses to their assets; no-op once drained); SpineMemory.Watch (warns on 256MB/second leaps so the neighbouring log lines name the culprit). F7 opens the panel (used to cycle P1's fleece; F8 keeps the one-key cycle). F5 on the title screen is blocked (it entered the test dungeon straight from the menu with nothing loaded and no way back; the menu editor is one more reason) and inside the map editor F5 means reset-room (the shortcut would throw away the room being edited). F6 hides whichever editor's chrome; the three pause-owning screens win F4 in order; the menu editor needs no F4 guard only because RoomEditor cannot exist on the menu. Hotkeys gated on MenuEditorOpen (every one would do nothing or something unwanted there).
- OnSceneLoaded: TrimRepackCaches (every scene change is a safe moment); HubSession then BaseSession pick up their scenes; WeatherControl.Forget (no map has asked this scene's controller anything yet); **LevelPlayback.ClearContentSuppression only** — never the run: entering a custom level passes through intermediate loads (transition, dungeon-map selector) with the run already bound, and stopping it there handed every custom level to the vanilla generator; the flag alone must not outlive its room (OnRoomGenerated re-arms per room). OnMenuScene is pass A (bind, then title/centrepiece/effects — the palette waits for pass B because Start hasn't run); any other scene Forgets (the Stylizer dies with the scene, but the palette ASSETS outlive it — carelessness there is how a gold title screen follows somebody into a dungeon) and MenuAssets.Forget (art re-read per visit). The room editor host is deliberately NOT DontDestroyOnLoad (scoped to its scene; persisting it would only leak). The events: OnBiomeChangeRoom → LevelPlayback (sequenced behind the fire-and-forget asset unload), LightingTool and WeatherControl (the ONE signal a re-activated room also fires — generation hooks only see first builds, which is why revisited rooms' lighting/weather went missing); OnBiomeLeftRoom → CustomEnemyDressing (corpse sweep).

### Follower spines and skins (SpineLoaderHelper/FollowerSpineLoader.cs)

- FollowerSpines/FollowerSkins folders read via ModContentPaths (ours + bridges). Textures Keep()'d (runtime textures freed by an unload sweep never come back). Skin overrides: each PNG is a separate part; scales/rotations set in the variant's config; variants each carry their own config (file names don't matter). After a repack, parts' CPU copies are freed (the repack baked every part's pixels into its own atlas; the parts are never pixel-read again — half of each texture's memory back). Min() on an empty ColorChoices sequence throws during startup — fall through to the default set. FollowerSlotDumper writes the slot/part list of a live follower's skin to followerSlots.json — the reference an author needs because part names are not guessable; the config-flag path runs once per session (existing file left alone), the panel button overwrites and reports.

### Player spine loading (SpineLoaderHelper/PlayerSpineLoader.cs)

- FleeceIndexes[4] is the source of truth (co-op seats four — players 3/4 were previously dressed by whatever player one picked: two variables and a two-case switch); the two persisted entries mirror it on every write. LookChanged fires when a look LANDS on the skeleton — not when asked for (a non-resident fleece loads and dresses by callback seconds later; redrawing when ApplyFleece returns redraws the old look — why a cross-atlas fleece only appeared on the second pick).
- **Deferred skeleton parsing:** parsing the JSON is four fifths of a spine's load cost (measured: 16.4s of a 20.9s load across 17 skins, vs 2.5s texture decode + 1.9s file reads), and CreateRuntimeInstance(initialize:true) does it eagerly on the main thread. Once the Atlas exists, SkeletonJson.ReadSkeletonData builds plain C# objects and touches no Unity API — so the parse runs on a background thread (IsBackground so a half-finished warm-up can't keep the game from closing; below-normal priority; GetAtlas is the last Unity call, made before queueing; the reader shares the Atlas read-only). Nothing waits: a spine worn early is parsed on demand by spine-unity from the TextAsset exactly as before — the warm-up changes where the cost lands, never whether the spine works. The pump runs on the main thread (handing parsed data to the asset is a Unity-side write); `_warmDrained` stops the per-frame lock once the queue empties, and is reset when lazy loads queue new parses (or the result sits in WarmFinished forever). A demand-parsed spine's warm-up result is DROPPED (overwriting would leave a live Skeleton pointing at data its asset no longer holds). After injecting parsed data, stateData must be built by hand — GetSkeletonData normally builds it as part of parsing, and injection makes it return early; without it GetAnimationStateData is null and SkeletonAnimation.Initialize throws "data cannot be null", taking PlayerFarming.Start down. Verify by asking the asset the same two questions spine-unity will; on failure put everything back so the TextAsset route works (a failed warm-up costs only the time it wasted). ReleaseJson frees the parsed-from TextAsset (a dead copy of a file that can run to tens of MB, one per spine; nothing re-reads it) — see SkeletonAssetPatches for the Clear() interplay.
- ActiveSpineKey reads COTL_API's internal SelectedSpine via Traverse; the API tracks two players, so player 3+ reads player one's (which IS what they wear). RememberedSpineKey is OUR note on disk (at boot the two differ: ours exists from the moment picked, the API's not until something restores it). ConfigFor null = vanilla or configless — leave defaults.
- **HideSlots strips the LIVE skin, not the SkeletonData:** every path that could put a slot back (animations keying it, the game's SetAttachment, the fleece) resolves through Skeleton.GetAttachment — current skin then default skin; no entry in either resolves to nothing. The data asset stays clean (other skins in the file and the vanilla spine unaffected on swap-away). Refuse to strip a DATA skin (between a spine swap and the following SetSkin, the live skin IS one of the SkeletonData's own — stripping edits the loaded asset permanently). Replace every attachment NAME on the slot (CROWN carries five, CROWN_EYE nine; any can be keyed) with transparent COPIES (Blank) — deleting throws ("Attachment not found") because FlyingCrown.Close re-attaches CROWN by name on every return flight/CROWN_HIDE_CANCEL; the copy matters as much as the alpha (the original belongs to the loaded asset, shared with every skin in the file).
- Fleece: ResolveFleeceSkin is shared by the F-keys, the panel and the SetSkin patch (one fix reaches all three); "CultTweaker_<spine></spine>_<fleece></fleece>" splits capped at 3 (fleece names may contain underscores); GetSkeletonData(false), not the field (the field is null until the warm-up reaches the spine — the call parses on the spot when needed). ApplyFleeceAttachments clears slots the fleece doesn't fill (the previous fleece's poncho stays on under the new one otherwise); hidden slots win over fleece slots (nine of fourteen are the poncho). HideSlots after the fleece, never before. Players beyond the second are dressed but NOT remembered (config and SetSkin patch only know two; the choice lasts until the skin next rebuilds — the panel carries the note). A fleece refused by DisableFleeceCycling still strips slots, so AnnounceLook fires anyway. ResolvePlayer: `players` is only populated with coop features on — solo falls back to Instance.
- **Lazy loading:** eighteen installed spines used to pay full cost at boot (whole gigabytes of parsed data) for looks nobody wore. The scan now reads only config.json into a registry; the selected spines (API's saved choice per player + the remembered fleece) load eagerly (the saved look on the player from the first frame — synchronously, at startup); everything else loads on first pick — file IO on a worker Task (fully qualified: a game assembly ships its own 'Task'), texture decodes one per frame, parse on the warm-up thread, applied by callback. FindEntry accepts bare names or "<spine></spine>/<skin></skin>" keys. IsPreparing feeds the F7 panel's "waiting" state. RegisteredSpineNames mints the same per-skin keys AddPlayerSpine does. The load caption is suppressed while the panel is open (it lands where the player dock stands, and the dock says it better — naming who is waiting). Register happens BEFORE the parse lands (the API can resolve the name; the wearer dresses by callback — the smooth swap). Register deliberately does NOT ChangeSelectedPlayerSpine (the old loader selected every spine as it registered — the last folder was worn on every boot regardless of the pick; the API's saved selection rules). **RestoreSelectedSpine: load first, select second** — ChangeSelectedPlayerSpine only takes a spine the API has been handed, and handing happens at load end; selecting first named something that didn't exist, the API kept its default, the player came up plain lamb with the log cheerfully reporting a restore that never happened. Read the selection BACK after (the API takes a string and reports nothing — a silent no-op restore is exactly the failure this catches). Both boot sources are asked (the API's selection is empty in Plugin.Awake — asking only it lost the remembered spine). StartWarmUp last (the worker never competes with the loading loop that queues it). CreateRuntimeInstance(initialize:false) — the third argument is the main-thread whole-JSON parse.
- PlayerSpineConfig: DisableFleeceCycling for spines dressing their own body; HiddenSlots exists because hiding a slot in the Spine editor doesn't export, clearing the setup attachment lasts until the first animation keying the slot, and the game re-attaches the crown by name — transparent live-skin copies are the one thing all three paths resolve through.

### Shared spine folder recipe and memory (SpineFolderLoader.cs, SpineMemory.cs, StructureBuildingOverrideHelper.cs, StructureSpineHelper.cs)

- SpineFolderLoader is THE recipe for "folder of Spine exports → SkeletonDataAsset" (NPCs, structures, world maps): textures NAMED after their file (the atlas resolves pages by name — unnamed, the skeleton renders blank), Spine/Skeleton shader (Shader.Find cached and fallen back — null in a stripped build makes new Material(null) throw inside Plugin.Awake and abort the whole mod), 0.005 game import scale. Auto-discovery: the skeleton is the one non-config .json, the atlas the one .atlas, pages the unclaimed .pngs. Null for a spineless folder is legitimate (a config that only renames things). Keep() everything (runtime assets with no file backing — an unload sweep frees them for good; room changes run that sweep, and the crash-time asset collector thread walks it). Seal() frees the CPU half of decoded PNGs (LoadImage keeps every decode readable — double memory for nothing once on an atlas) — but NEVER for player/follower spines, whose pages the skin repacker still pixel-reads. JsonFreed + placeholder JSON: GetSkeletonData refuses to answer at all when skeletonJSON is null (cached data or not), so freed assets keep a never-parsed stand-in; Clear() is skipped for them (SkeletonAssetPatches).
- SpineMemory: two caches grow during skin repacks and nothing on our path empties them — Spine's AtlasUtilities keeps a CPU copy of every texture a repack read, and COTL_API's Graphics.CopyTexture patch keeps a converted duplicate of every page (cleared only inside its own skin-building path, which we never take). Dropped after startup and on every scene change; both rebuild on demand (cost: a slower next repack, never behaviour). The watcher samples once per second and logs only on leaps (neighbouring lines are the suspect list); split managed/native/graphics because the fix differs. Environment.WorkingSet and System.Diagnostics.Process are STUBS in this Mono profile (answer 0 without erroring) — the OS is asked directly via interop; the mono heap SIZE (the GC's reserved arena — grows in huge steps, never shrinks, invisible to GetTotalMemory) and the working set are the two the first version missed. The API's cache field is internal — reached by name; a build without it logs once and moves on.
- StructureBuildingOverrideHelper: remembers which FOLDER each building's overrides came from (sprites are named relative to it — a bridge folder can't be rebuilt from the plugin path). The converted list is cached per building — it's called from a postfix on Structure.Start, firing for every matching structure on every location load, and each call used to decode every override PNG afresh (a leaked texture per call); negative answers cached too (most structures have none, and all ask).
- StructureSpineHelper: COTL_API builds every custom structure by instantiating one vanilla prefab (Decoration Wreath Stick) and swapping its sprite — not extensible, so the skeleton is added AFTER as a child (sprite renderers off; sprite remains the build-menu icon); brain, bounds, collapse/repair and flipping untouched (the skeleton is just another child transform the game's bookkeeping carries). Cheap checks first (called for every structure placed, custom or not). Idempotent via the CultTweakerStructureSpine marker (placement can run twice for one object — a structure re-placed after location reload keeps its GameObject; a second skeleton draws over the first). The first sprite renderer is the reference for sorting layer (or it draws behind the ground) and rotation. Built inactive so Awake runs once with the skeleton assigned. Skins validated before assignment (SetSkin throws inside Initialize with the structure half built). **WorldTilt -60:** the camera looks down at sixty degrees and everything upright is authored rotated -60 X to meet it (300 in the inspector — same angle); a fresh GameObject has no rotation, so an unrotated skeleton lies flat like a decal; the structure's own sprite is the better reference where there is one, the tilt stands in otherwise, config Rotation overrides both. Empty animation = setup pose (what a static prop wants).

### Dungeon map screen (MapEditor/Tools/DungeonMapCanvas.cs, DungeonMapSkin.cs, DungeonNodeVisual.cs)

- DungeonMapCanvas is the world map's arrangement, not a panel with a picture in it: the map fills the canvas, the editor's furniture floats over it, the room editor's chrome stands down (leaving it on is what made the map look like something inside the room editor). It is a view and a hit test — it never touches the map; DungeonBuilderTool reads the pointer through it and decides what a click means. Metrics copied from the world editor so the two map screens are the same screen with different content. Nodes are drawn from the vanilla prefab (authored for 300 units a step; node art scales by our pitch/300 so a node stays the size it looks in game). Picking is geometric — the hit radius is the node, not its art. Backdrop is OPAQUE, not a dim (this is a screen; a room showing through is what made the old panel read as a panel), added first so everything sorts above it. Nodes drag by reference; links are kept per-drawn-line so they re-aim without a rebuild (lines follow the node during a drag; a line left behind reads as a broken link; re-aiming is just assigning the point pair — the setter marks the renderer dirty). The vanilla node prefab is an addressable loaded on the way into a dungeon — the only place this opens — so it's normally in; otherwise the map draws in our own art and redraws when the real art arrives. The panel collapse rebuild is DEFERRED (the collapse button is one of the children being torn down — immediate would take the click with it). Status bar: two lines both left-aligned (verdict above, event below — side by side they fought for the same middle). The screen is stood down (not closed) while the real map preview is up over it. Node captions: "Wood (L1) - vanilla floor" — no name (the game's map doesn't name nodes; a name would label something the player never sees), but the type isn't always legible from the icon, the level can't be read off it, and the layer is DERIVED from the drop position (the one thing the map can't show itself). Bound nodes take the game's "done" green (authored content reads different from a floor the game fills in). Bosses scale 1.5 as on the real map (AdventureMapNode.Configure — the shape of a run stays recognisable). The bottom node wears the start pin. Faults are a halo BEHIND the node (visible at the same time as the selection instead of fighting for the outline); the selection goes on the node's own outline (what the game lights to say "this one"). Link fallback: tiled dashes at the vanilla ratio (7.5 width at 300 pitch; 8px line, 4px gap) when the dotted material isn't at hand; the real MMUILineRenderer's points live on a branch object a runtime-added component may lack — aim once inside a guard at build time so a throw happens while there's still a plain line to fall back to, not mid-redraw.
- DungeonMapSkin: the game's adventure-map art harvested from the in-run map screen prefabs — same trade as CustomMapSkin (visuals only; scripts stripped — they expect a live run, a MapManager and a node graph). Leaving the dungeon unloads the source prefabs — re-harvest next open rather than draw from dead sources. **The screen's identity is its chrome** (scrolling backdrop, frame, goop — on the overlay prefab), unlike the DLC map whose identity is its nodes — so the whole prefab is cloned and everything belonging to a run is cut: node/connection containers, the crown and eye marking the player, the shuffle prompt. **The nested Canvas must be removed AFTER the strip:** a Canvas cannot be removed while a scaler or raycaster depends on it, and Unity refuses with an error, not an exception — the canvas quietly survived and the log filled with "Can't remove Canvas because MMCanvasScaler depends on it"; a nested canvas would sort against ours rather than inside, and the menu's raycaster would eat the clicks the editor polls. UIMenuBase fades its groups in on show, and show is one of the stripped scripts — force the groups visible or the screen is there but invisible. DestroyImmediate throughout (a deferred destroy still runs Awake when the clone is parented the same frame). Cloned art raycastTarget=false (the canvas polls its own clicks against cells).
- DungeonNodeVisual mirrors AdventureMapNode.SetState exactly (the art is built around those images/materials/colours: the outline material carries the glow; icon tint separates beaten from fresh). Every node drawn as reachable — the run hasn't been played, and a grey node only says "you are not here yet". Hide the furniture vanilla's own Start would have hidden (start pin, quest alert, run modifier — that Start is stripped) except the actual start node keeps its pin (the one thing the map itself says about a node). Same warm-tint trap as the world map: the vanilla "selected" outline material multiplies with the mark colour (green comes out dark red) — only the selection wears it.

### Level layout screen and load browser (MapEditor/Tools/LevelLayoutCanvas.cs, LoadMapScreen.cs)

- LevelLayoutCanvas is the dungeon builder's arrangement one level down (that screen authors the graph of floors; this one the grid of rooms in a floor — the actual shape the game builds, cell for cell, door for door); a view and a hit test that never touches the blueprint. Options-panel height is computed from the canvas, not fixed (a fixed 940 ran under the dock on any other screen, and the controls at its foot — the modifier, the button under the room list — were the ones that went under). Cell size is bounded (a two-room level with screen-sized cells reads as a mistake). The window on the grid = authored extent plus a ring of empty cells (always somewhere visible to put the next room), recomputed per redraw, sized around the panel (folding the panel gives the grid the space) and centred on the space LEFT of the options panel, not the screen. Close confirms any open prompt FIRST — a prompt whose field was destroyed keeps the editor's typing locks on with nothing left to press. The hover preview offset is raised for this wider panel. Hit test: inside the PLATE, not merely nearest cell — the gap between plates is where a click means "empty grid", and treating it as a hit made rooms impossible to deselect. Modifiers draw on the room's own edge (combat/reward pacing must survive a glance at the whole level); doors on the room's edge (where they'll be in game; no layout = no doors drawn — the game rolls its own). Pool captions: "2 of" was a sentence missing its end — a count wants the thing it counts; vanilla is called out separately (the game rolling its own is a different kind of answer from naming a blueprint); one named room says WHICH. Selection is rings, not translucent plates (the plate washed over the caption and doors — the things being selected in order to read); blocking-issue rings sit wider so a room that is both shows both; rings redraw on their own (selecting must not rebuild the grid).
- LoadMapScreen: a screen, not a panel — a room is recognised faster than its name is read, and two 168px thumbnails in a side panel gave that up (every dungeon room the same brown smudge; the name did the work). Three across (the left third is the detail pane); cell width computed from the canvas (the scaler matches width and height evenly, so reference width moves with aspect — a hardcoded cell overflows narrow and half-fills wide). The detail pane is a SHARE of the screen (a fixed 420 made the big picture a thumbnail with a caption — what the grid already is). Backdrop opaque enough to read pictures against (the room behind is a lit dungeon; torches came through every thumbnail). **Clicking a card only selects; loading is a separate deliberate press** — it used to load immediately, a destructive act (clears the room you stand in) fired by one click on a postage stamp; the pane also gives the snapshot somewhere to be shown large, the whole reason snapshots are taken. The picture is the button (a caption says so — a picture that happens to be clickable is a picture nobody clicks); flush to the pane's edges (padding is width spent on nothing; touching the edges reads as part of the pane, not a thumbnail in one); 16:9 (what it is a picture of). The warning sits directly above the Load button — loading clears the room and this is the last moment anyone reads anything; the close prompt guards one door out of an edited room, and loading was the other, unguarded. Facts rows are positioned by index, not a layout group (a group inside a hand-anchored pane fights the anchors). Facts are re-read on every selection (the browser stays up; nothing stops the room behind being edited between glances). The lightbox is built once and hidden (one image and a backdrop; a screen that allocates before answering a click feels slower than it is); anywhere dismisses it; fitted, never stretched. A plate sits under every card picture (a room whose snapshot never arrives is an empty frame, not a hole). The scan's "reading folder" note is handed back so the caller can remove it when the cards arrive.

### Weather control (MapEditor/Tools/WeatherControl.cs)

- The game already does this — BiomeGenerator carries weatherType/weatherStrength and applies at generation; this is the same call with values from a blueprint. **Dungeons and hubs only because ShowCurrentWeather writes the weather into the player's save (type, strength, start time, duration) whenever the location is not a dungeon** — a hub is not a dungeon by that test (Woolhaven), so every call is bracketed to put those four fields back exactly as read (they're written inside the call itself — no thread, no coroutine between; in a dungeon the game skips the write and the restore costs nothing). The weather itself plays off the controller's own state, not the save's. `Holding` is read by WeatherPatches (stops the game's clear).
- (type, strength) pairs are matched against the controller's own table — a missing pair produces NOTHING (the game logs and leaves the room alone), so the tool offers pairs that exist, not full enums. The controller and its table are remade with the scene. **Synthesised strengths:** the game only authors the pairs it uses (snow has every strength for winter; wind/rain one or two) — but a row is just a particle rate, a screen tint, a shader keyword and a sound, so missing rows are built from the NEAREST authored row of the same type, scaled (never from another synthesised row — scaling a scaled row compounds the guesswork; strength weights say how much weather each level is). A type with no rows at all cannot be filled (nothing to scale). Synthesised rows are never rolled by the game's own picker — they exist only for maps that name them. Particle-system references are set in the controller's Start and shared across every strength of a type — scale a copy, never the original.
- Per-room memory mirrors the lighting tool exactly: a blueprint load is not the only way into a room (doors re-activate built rooms; untouched rooms have no blueprint), so weather set two rooms ago followed the player — and while held, the game's own clearing was suppressed too. Each room's ask (including "none" — worth remembering rather than a gap) is recorded at blueprint load and re-asserted on OnBiomeChangeRoom (the one signal covering revisits); the hook stays free when nothing was ever set; the same arrival announced twice is deduped. Only weather this mod put up is taken down (a biome setting its own is left alone). Forget() on scene load — the new scene's controller has its own table and no map has asked it anything; a carried hold would suppress the game's weather in the wrong scene.

### Menu editor tools (ModUI/MenuEditor/Tools/MenuPresetTool.cs, MenuLookTool.cs, MenuCentrepieceTool.cs, MenuTitleTool.cs)

- PresetTool: the picker is the whole switchboard — choosing is both "edit this" and "wear this", and Vanilla is an entry like any other (turning the mod's menu off sits where turning it on does, not in a button meaning the opposite). Vanilla is first, always — not a file, so it can't come from the folder listing, but it IS one of the things the title screen can wear. Save is always "Save as..." under a name (Ctrl+S covers save-in-place); the config is written before the art is copied (the destination folder must exist); a preset is its folder, so a new name takes the title png and spine subfolders along. UseVanilla clears the config value AND reverts live (the menu under the panels is the preview — claiming vanilla while the screen wears a preset would be a lie). The unsaved working copy is the one thing that can't be got back — worth a question; the picker is put back to what actually loaded next frame (it never claims to show something it didn't switch to). Loading a preset makes it the active one (the picker must not sit on a name the title screen isn't wearing). RebuildPanels is queued — this runs inside the dropdown's own click, and rebuilding the dropdown tears down the thing calling us.
- LookTool: the gold field is not a light — there is no LightingManager in this scene at all; it is a palette mapping by a camera post-effect the game already runs with TWO palettes and a blend at zero, so the tool writes slot two and moves the blend (at zero the menu is the game's own pixel for pixel; the crossfade is the thing the game was already doing). One override switch that means what it says — no row below turns it on behind the player's back (a control that quietly flips the master toggle overrides a menu nobody asked it to). The HSB sliders arm their OWN toggle (moving one is the statement of intent) but never the master. Reset-palette resets the palette rows only (a button named for one section shouldn't reach into the next). The clear-colour row is the one control that recolours the background alone; the negative overlay is a switch and a strength, not a colour (its blend ignores hue); the HSB shift applies to a private copy of the palette and moves EVERYTHING (that's what a palette is — one lookup table for the whole image).
- CentrepieceTool: skin/animation lists are read off whichever skeleton is actually loaded (the menu's lamb offers three skins and one animation; a folder spine offers whatever its author gave it) — filled on dropdown open, not BuildPanel (what a source offers depends on what's loaded and which preset folder, both changing under the panel). Placement is offsets and multipliers, never absolutes (the menu authors the skeleton lying back at -80° at a non-uniform scale — a stored absolute goes wrong the first time the scene moves). Deliberately no player-spine source: those are built for the player rig and do not hold together on the menu's skeleton (a folder spine is the way). Vanilla source shows an empty asset list (a one-entry list saying "the skeleton already standing there" is a control that can't be used for anything).
- TitleTool: the logo is plain art with nothing re-asserting it, so drawing over it holds on its own. The vanilla logo is the FIRST ENTRY of the image list, not a button beside it (one of the things the title can show, so it lives with the rest). The edition colour rows hide while the override is off (three dead sliders are noise; hiding is what says "off" at a glance). No override toggle for the edition fields (each control's neutral value already means "the game's own" — a switch would restate it). Text entry goes through the mod's name dialog (the game's keyboard handling; works on a pad).

### Follower skin editor (ModUI/SkinEditor/FollowerSkinEditor.cs, FollowerSkinPreview.cs, CTFollowerSkin.cs, Tools/SkinLayerTool.cs, Tools/SkinFileTool.cs; SpineLoaderHelper/FollowerSpineLoader.cs)

- The document IS the loader's `FollowerSkinConfig` — the editor writes the same `config.json` the loader reads, nothing in between; `templateComment*` keys in the template are dropped on the first save (not fields, so Newtonsoft ignores them). A skin is a folder of variant folders; the variant is the unit of editing, the skin the unit the game registers (`FollowerSpineLoader.CreateNewFollowerType` takes the variant list: variants are separate `CharacterSkin` entries under one Title, colour sets are the `SlotAndColours` rows). Layer key = image file name without extension (the loader's contract), so "pick an image" is really "name the layer"; a colour-only layer is named `color_<partname>` with the loader's own free-number suffixing.
- **Colour sets must be the same length across every part** or the loader drops the skin (`BuildColorsByIndex` takes the min count). `CTFollowerSkinDocument.NormaliseColourSets` pads/truncates to the min on load, save and every refresh; Add/Remove colour set applies to every layer at once and there is no per-layer count.
- Loader refactor for the editor: `LoadAllNonSpineSkins` → per-folder `LoadSkinFolder`; `BuildCustomOverrideSkin` → `ComposeSkin` (returns the unrepacked Skin; the loader then repacks and registers as before) + `ApplyColours` (the FollowerBrain colour loop: `FindSlot(part)?.SetColor`). `Reload(skinName)` drops every dictionary entry with the `skinName_` prefix and the `WorshipperData.Characters` row whose skins carry it, then re-runs `LoadSkinFolder` — followers already wearing the old Skin object keep it until re-dressed; new ones get the rebuilt one. `Reload` swallows and logs, and the editor's save status says whether it took.
- **Preview is an off-screen `SkeletonAnimation`, not a `SkeletonGraphic`:** this Spine build has no `allowMultipleCanvasRenderers` (grep of spine-unity.dll), so a graphic draws every override with the first texture. An unrepacked composed skin has one material per override image — fine for a MeshRenderer with submeshes — so the rig mirrors PlayerPreview (OffscreenLayer, ortho camera, RenderTexture → RawImage, shader globals whitened around the render, materials swapped to the stock Spine shader). Repacking per slider tick was the alternative and allocates a texture each time.
- `ComposeSkin` takes an optional atlas cache keyed by texture instance + part name; the preview owns one (rebuilt skins on every tick otherwise create a SpineAtlasAsset + Material per override per rebuild) and destroys it on Release; the loader passes none and behaves as before. The preview re-reads a png when its mtime changes (drop a new file, next refresh shows it), destroying the old texture.
- Preview refresh is debounced 0.12 s (`Refresh` marks dirty; `RefreshNow` rebuilds) — sliders fire per frame, and composing a skin walks every base attachment. `Play` is a no-op when the same animation/loop is already current so a Look slider does not restart the walk cycle. The rig ticks with unscaled time because the editor pauses the game (`Time.timeScale = 0`, re-asserted every frame like the F7 panel, restored to the saved value or 1 on close).
- Opening: F8 from Plugin.Update, refused (with the reason logged) on the title screen, over any other editor, or when `WorshipperData.Instance.SkeletonData` is missing — `WorshipperData.Instance` instantiates its prefab on demand so it exists everywhere in-game. `ForceClose` on every scene load (the rig is DontDestroyOnLoad but the world it previews is not). The other keys (F4–F7) are gated on `SkinEditorOpen` like `MenuEditorOpen`.
- Layer tool: the part picker lists the BASE SKIN's attachments (slot index + attachment name + slot name), not the skeleton's slots — the loader looks up `baseSkin.GetAttachment(slot, part)` and a part the base skin lacks is silently skipped. Placement sliders show only when the layer has an image (they are attachment transforms; a colour-only layer has nothing to place). Colour sliders edit the CURRENT colour set only; the set picker drives both the sliders and the preview. New layers get white in every set (white = no tint).
- File tool: "New skin"/"New variant" write nothing (free-named `untitledskinN` / `variantN`); "Save as new skin..." writes the config first (folder must exist) then copies this variant's pngs and every other variant folder; "Save as variant..." copies this variant's pngs under the new name — both re-run `Reload` after the copy so the game sees the art, not just the config. A plain Save button exists beside Ctrl+S here (unlike the menu editor) because saving is also the "reload in game" action. Unsaved-edit guard on load is the menu editor's two-pick pattern.
- **A hand-built `RegionAttachment` draws nothing until `UpdateOffset()` is called** (its vertex offsets stay zero) — the in-game path never showed this because `GetRepackedSkin` recomputes offsets while cloning; the unrepacked preview drew every png-backed part as a point (the head vanished, the colour-only body stayed). `ComposeSkin` now calls it in both attachment cases. The preview ticks with a clamped unscaled delta (a paused game can hand back a 0 or a huge first frame) and re-queues the wanted animation if track 0 ever empties; it logs one line a second after opening naming the animation, its track time and the material count, for the next time it looks frozen.
- **The game ships a forked spine-unity: `SkeletonAnimation.Update(float)` returns when `UseDeltaTime && Time.timeScale == 0`, and only ticks itself while `Visible || ForceVisible` (its own OnBecameVisible flag), with a multithreaded `ProcessFrame` behind `Update(float, forceMainThread)`.** So an off-screen rig under a paused game froze at track time 0 no matter what delta was handed in (the F7 panel gets away with it by pausing at 0.1, not 0). The preview sets `UseDeltaTime = false`, `ForceVisible = true`, and calls `Update(delta, true)` so the frame is processed on the main thread before its own `LateUpdate`/render. Decompile with `ilspycmd -t Spine.Unity.SkeletonAnimation spine-unity.dll` when the runtime does not match the public 3.x source.
- **Draw order across override materials:** Unity orders a renderer's submeshes by material render queue before submesh index, so overrides on the stock Spine shader (queue 3000) drew over the body on the game's follower material even though slot order put them underneath — the head sat on top of the eyes. `ComposeSkin` takes a material template; the preview passes the follower atlas's `PrimaryMaterial` so every part shares one shader and queue and slot order decides. `Untint` copies `renderQueue` across explicitly (reassigning a shader resets it).
- Layout: a near-opaque full-screen backdrop (the room behind is noise, and the preview's alpha showed it through), the preview centred in the space left of a 540-wide options column, and the layer list as an icon grid with names (the png itself is the thumbnail; colour-only layers get the placeholder) — the same picture-plus-slot-name shape the cult clothing UI uses, which the user wants to reuse elsewhere.
- **Every preview material is rebuilt on the stock Spine shader with only the texture carried over** — the follower atlas material is the game's world-lit shader: it multiplies in the scene's time-of-day/highlight colours (a white cat came out torch-yellow even with the globals whitened around the render, because the material's own colour properties carry the tint too) and it writes depth, so parts on different submeshes z-fought and limb edges showed through the shirt. On the flat stock shader there is no depth and no lighting; slot order is the only order. The material template passed into `ComposeSkin` only matters for keeping the queue consistent before the swap.
- **Saving warns before it stalls:** `Save()` paints a Warning status and defers the real work (`SaveNow`) two frames by coroutine, because `Reload` repacks every variant's atlas synchronously (the texture bake is the spike) and a status set in the same frame as the stall is never seen. The close-confirm path calls `SaveNow` directly (the UI is going away anyway). Save-as copies the pngs BEFORE `SaveNow` so the single reload sees the art — the earlier order (save, copy, reload again) repacked twice.
- **Name prompt under the editor:** the prompt is the game's cult-name menu on the game's own canvas. The room editor gets it right by disabling its whole canvas while `ModalOpen` and by NOT re-asserting `timeScale = 0` during the modal (the prompt sets 1 so its show tween can run). Mirror both; a hidden editor canvas also means the backdrop cannot eat the dialog's clicks. Canvas order 5000 like the room editor.
- **The preview repacks, exactly like the in-game path — chasing multi-material sorting was the wrong road.** An unrepacked composed skin carries one material per override png, and every attempt to make those sort correctly against the base atlas material failed differently: on the stock shader the head drew over the eyes; on copies of the follower material the head fell behind the clothes; and the tint came back because `Untint` latched (`_untinted` set on the first tick, so the fresh materials a later `Show()` created were never swapped — base parts white, override parts torch-yellow, which is exactly what the screenshots showed). A skin that misses its overrides entirely is the same bug seen from the other side: the custom parts were drawn behind the base ones. `GetRepackedSkin` bakes every attachment into ONE atlas and ONE material, so there is no cross-material sort left to get wrong and slot order alone decides — which is why the game's own skins always looked right. `materialPropertySource` is a plain stock-Spine-shader material, so the result is flat and untinted with no override pass at all. `clearCache: true` on every repack (the util memoises readable texture copies; a re-saved png would otherwise come back from the cache), and the previous material+texture pair is destroyed on each rebuild — a 1024² RGBA bake leaked per refresh otherwise. The 0.12 s debounce is what makes a per-edit repack affordable.
- Layer cells show the layer's own png; a colour-only layer gets the words "no image" in the letter tile (`MapEditorGrid.SetCellLetter`, added beside `SetCellIcon`) rather than the generic placeholder — the placeholder read as "picture missing" when the layer is deliberately image-less. Selecting a layer shows that png full width plus an Image picker, so which art covers a part is changed in place. **Changing the image is a re-key, not a field write** — the loader's contract is layer key = png file name, so the entry is rebuilt into a fresh dictionary preserving order (a plain remove+add would move the layer to the end of the file). Picking "No image" re-keys back to a free `color_<part>`; the picker offers unused pngs plus the layer's own.
- **`clearCache: true` on every repack was the spike.** `AtlasUtilities` memoises one extracted `Texture2D` per `AtlasRegion` (`CachedRegionTextures`, keyed by region object identity) and `ClearCache()` DESTROYS every one of them — so clearing after each repack forced a `GetPixels` + new texture for all ~45 regions of the base cat on the next repack, i.e. on open, on every skin switch, and after every debounced edit. Dropped: a changed png becomes a NEW `Texture2D` and so a new region object, which misses the cache on its own; nothing stale can come back. The cache is cleared once on close (`SpineMemory.TrimRepackCaches`). A repack over 40 ms logs its duration.
- The intermittent "override did not cover the cat face" is the same class of bug in the mod's own atlas cache: it was keyed by `texture.GetInstanceID()`, and Unity recycles instance IDs after a Destroy — a re-read png could land on the id of the one it replaced and hand back the previous atlas, whose material pointed at the destroyed texture. `AtlasFor` now checks the cached atlas's `materials[0].mainTexture` is still the texture asked for.
- **Two panels, no dock.** The skin editor drives both `IMapEditorTool`s at once — left `SkinSetupPanel` (skins, base, save-as, preview, colour sets, layer list), right `SkinLayerPanel` (the selected layer) — instead of a dock that swaps one column. `SidePanel` holds each one's rect/column/collapse state; `FillPanels` builds content AFTER `Preview.Build` (the animation dropdown reads the rig), `DoRebuildPanels` rebuilds both, and `SelectedLayer` lives on the editor because both panels read it. Nothing else changes: the tool interface, the widget kit and the other editors are untouched.
- Skin and variant are ONE picker showing `skin/variant`, with **Create New Skin or Variant** as its first entry — the same shape as the menu editor's Vanilla row (a thing the list can be set to, not a button meaning the opposite). One `skin/variant` prompt covers both jobs: a name whose skin half matches the open document clones its layers (the old New variant), any other name starts empty (the old New skin). `MapNamePrompt` gained an optional `validate` returning a problem string — it fills the disclaimer line AND a per-frame coroutine holds `_confirmButton.Confirmable` false, because the game recomputes that on its own in `OnConfirmButtonSelected` and a one-shot listener loses to it. Splitting happens BEFORE `Sanitize`, which would eat the slash as an invalid filename character.
- The layer list adds by slot: the picker offers only base-skin parts nothing covers yet, and choosing one creates a colour-only layer straight away rather than making the player fill a form first. Its image is chosen afterwards, in the right panel, where the art and the sliders it affects are.
- **The preview keeps four packed skins and only repacks when the ATLAS would differ.** `Signature()` covers skin/variant, base skin, and per layer the key, slot, part, hide flag, placement and the png's mtime — deliberately NOT the colours, because colours are slot tints applied after the fact. So a colour slider or a colour-set switch re-tints the worn skeleton with no compose and no bake, and re-selecting a skin looked at recently comes straight out of the LRU (four entries; a 1024² RGBA bake each, evicted oldest-first and destroyed). Only the first look at a given structure still pays a repack.
- **Selecting a layer rebuilds the right panel only** (`RebuildInspector`, a lower rung than `RebuildPanels`): the grid already moves its own selection ring in `AddCell`, so rebuilding the left panel was pure loss — it destroyed the grid, rebuilt every cell and threw the scroll back to the top under the click. Rebuilds now also restore the scroll position two frames later instead of resetting to the top, since a panel this long is usually being edited somewhere below the fold.
- A skin that does not exist on disk yet always gets `base` as its first variant, whatever the prompt asked for (the status line says so). `LoadWorkingCopy` and every reader look for `base` first, and a skin whose only variant is called something else reads as broken rather than deliberate.
- The status bar goes amber and says "Opening <skin>, lag will occur!" whenever a pack is about to run (`SetBusy`, cleared by the next successful `Show`). Both entry points defer the bake through the 0.12 s debounce rather than calling `RefreshNow` inline, because a status set in the same frame as the stall is never seen — the message has to reach the screen first. The plate's dressed colour is captured after `VanillaChrome.Dress` so it can be put back.
- **The bake cannot be moved off the main thread.** `GetRepackedSkin` is `GetPixels`/`SetPixels`/`Apply`, all Unity calls that throw off a worker thread, and it is one monolithic call — there is no incremental mode to drive from a coroutine. Making it progressive means replacing it: extract each region on the main thread a few per frame, blit into a `Color32[]` on a worker thread (that part is pure array maths), then one `SetPixels32` + `Apply` at the end, and re-derive every attachment's UVs the way the util does. Given how much trouble the material/sort work already caused, that is a deliberate project, not a tweak. What is cheap and already done: cache the packed result, keep the rig alive across opens, and say plainly when a pause is coming.
- **An unedited skin is never baked twice: the preview wears the one startup already registered.** `BuildCustomOverrideSkin` repacks each variant at load and keeps it in `CustomFollowerSkins[skin_variant]` — the editor was ignoring that and baking its own copy of identical art. `Registered()` returns it when the document still matches, checked two ways: the editor passes `!HasUnsavedEdits` (the document equals its file) and `SameParts` compares the document's part configs against `FollowerSkinOverrides[key]`, the exact list the registered skin was built from, so a config.json hand-edited mid-session is caught too. Editing anything moves the signature away and the preview bakes its own copy again, which is the only case that needs one.
- That skin is packed against the game's follower material, so `Flatten()` runs after every `SetSkin`: any material on the renderer that is not already on the stock Spine shader gets a flat override carrying just its texture (the mesh regenerates once more afterwards, since `sharedMaterials` only reflects the last generated mesh). It is the old `Untint` without the latch that caused the yellow-head bug — driven by the worn skin changing, not by a one-shot flag.
- **A layer's thumbnail is read from THIS variant's folder, so it is honestly empty until the art is there.** A new variant clones the config but its pngs only arrive when it is saved (`CopyImages`), and save-as used to refresh nothing but the skin dropdown — so the grid kept saying "no image" for layers whose art had just been copied in. `SaveNow` now rebuilds the panels. The sprite cache also validates `sprite.texture == texture` (it is keyed by instance id, and Unity recycles those after a Destroy — the same trap that hid override parts).
- **Why the FIRST structural edit stalls:** until you touch it the preview is wearing the skin startup already registered, which costs nothing; the first edit makes the document differ from its file, so that copy no longer applies and the editor bakes its own. Colour edits are exempt — colours are not in the signature, so they re-tint the worn skeleton whether it came from the registry or a bake. Placement edits are NOT exempt today even though they never change the atlas: the offsets are baked into the attachments, so moving a part re-packs. Mutating the worn attachments in place instead would need each attachment's pre-offset X/Y stored at pack time.
- A new skin or variant writes its folder and `config.json` at once (no `Reload`, so no bake beyond the preview's own) instead of waiting for a save: the point of making one is to drop pngs into it, and a folder that does not exist yet cannot be opened in a file browser. It also keeps the skin dropdown honest — the list is built from the folders on disk, so an in-memory-only skin had to be spliced in and could not be re-selected after switching away. The right panel carries a "Refresh skin list" button for art added while the editor is open; it is a plain `RebuildPanels`, which re-reads skins, variants and images.
- A new variant copies the source variant's pngs into its folder as it is created, not at the next save: the cloned config names those files as its layer keys, so without them every layer reads "no image" and the preview draws the base skin. Copying happens before the debounced refresh fires, so the first bake already has the art. A new SKIN copies nothing — its config starts empty and names no files.
- **The first-edit bake is pulled forward into an idle moment.** It cannot be pulled back to game start: what has to be baked is the edit nobody has made yet, and until then the registered skin is pixel-identical, so there is nothing to precompute. What CAN be done is to stop it landing on a click — a second and a half after the preview settles, with no modal open and nothing else queued, the editor bakes its own copy and wears it (`Prebake`, announced on the busy bar like every other stall). The idle wait matters: doing it during `Open` would put the pause back where it was, and would bake for people who only came to look.
- Still unsolved, and the bigger prize: **placement, hide and colour-only-layer edits re-pack even though the atlas is unchanged.** Only changing WHICH image a layer uses actually needs a new atlas. Fixing it means packing every image regardless of hide state, keying the cache on the image set alone, and rebuilding the Skin from the packed atlas per edit — copy the attachments, re-derive each one's X/Y from the packed value minus the offsets in force at pack time (linear in both the region and mesh branches, so a stored snapshot is enough), reassign rotation/scale, `UpdateOffset`, and drop hidden ones. Cheap at runtime, but it is the attachment-geometry code that has already produced the draw-order, missing-part and UpdateOffset bugs, so it wants its own pass.

### Play animation on object, and the podium's two looks (MapEditor/Tools/TriggerActions.cs, TriggerTool.cs, PodiumTool.cs)

- `PlayObjectAnimation` reuses the object-picking path (`_pickingObject` -> `TryPickObject` -> `PathOf`), then asks three questions in stages: which animation (read off the picked object's own skeleton), play once or loop for N seconds, and what to do at the end. Target holds the object path and Subtext the animation name — Target is already the resolve key for every object action, so the animation had to go somewhere else. An object with no spine keeps you in pick mode with a warning rather than adding an action that could never run; `SkeletonDataOf`/`AnimationStateOf` cover both `SkeletonAnimation` and `SkeletonGraphic`, falling back to the asset's data when the skeleton has not been built yet.
- `FreezeAtEnd` is a real serialized field rather than a reused `Amount`: it is a lasting property of the action, and the next person reading a blueprint should not have to know that 1.0 in a float means "hold". Resuming captures the track's current animation and loop flag BEFORE overwriting track 0, so an enemy goes back to the idle it actually had rather than a guessed one.
- **The podium's two looks are NOT one prefab.** `PodiumOff`/`PodiumOn` under Shrine are the taken and available states of the weapon podium; toggling them changes nothing useful, because the plain ground plate seen beside a podium in the entrance room is a separate object whose sprites are not in the podium prefab at all (verified by inspecting both). A "Lit ring" toggle was built on the wrong assumption and reverted — the giveaway was that `Interaction_WeaponSelectionPodium` never switches ring art by `Types`, only the icon, its material and particles. If the other plate is ever wanted it belongs as a SECOND placeable, not a toggle on this one; the candidates are `Interaction_LegendaryWeaponSelectionPodium` (a subclass with its own prefab) and the entrance room's Dungeon Devotion Shrine, and either would need its own template lookup since neither is reachable from `Interaction_Chest.WeaponPodiumPrefab`.

### Boot cost: why the window was white for 14s, and what the staged bake changed (Plugin.cs, SpineLoaderHelper/FollowerSpineLoader.cs, SpineMemory.cs)

- **Measure before touching anything.** `SpineMemory.Phase` now reports milliseconds as well as MB, `Awake` closes with a `STARTUP TOTAL` line, and `OnMenuScene` logs `MENU READY` with `Time.realtimeSinceStartup`. The numbers that started this: 26.1s to the title screen, of which **14.9s was CultTweaker's Awake** and 12.7s of that was FollowerOverrides alone. BepInEx runs plugin `Awake` before Unity draws its first frame, so every one of those milliseconds is a frozen white window -- not a slow frame, no frame at all.
- **Part count does not predict bake time; base-skin novelty does.** `hallejrtestskin_base` (45 parts, Cat) took 7076ms while its own `variant` and `variant3` (45 parts, Cat, their own pngs) took 11ms and 8ms. `lmab_base` has FOUR parts and took 3093ms -- it is the only skin built on Lamb. The cost is `AtlasUtilities.ToTexture` pulling each `AtlasRegion` of the base skin out of its atlas, memoised in `CachedRegionTextures`; the first skin on a given base pays for all of them, everyone after is a dictionary hit. Two cold extractions were 10.2s of the 12.7s. Deleting the junk 0-override test skins saves nothing: they cost 2-3ms each.
- **The bake cannot go on a thread, only across frames.** Decompiling `AtlasUtilities` settles it: `ToTexture` is `new Texture2D` + `Graphics.CopyTexture`/`GetPixels`, then `PackTextures` and `new Material` -- all main-thread-only Unity API. The player-spine warm-up threads only because `SkeletonJson.ReadSkeletonData` is pure managed code touching no Unity object. Do not try to reuse that pattern here.
- **Yielding between skins would have achieved nothing** -- the expensive one is a SINGLE call. What works is `WarmRegions`: walk the composed skin's attachments, call `region.ToTexture()` on each distinct `AtlasRegion` with the defaults `GetRepackedSkin` itself passes (RGBA32, no mipmaps, texture property 0, so the keys match), and hand the frame back once 8ms have gone by. The repack that follows finds everything cached and returns in milliseconds.
- **Shape of the refactor.** Each entry point became a pair: `LoadAllNonSpineSkins` / `LoadAllNonSpineSkinsStaged`, `LoadSkinFolder` / `LoadSkinFolderStaged`, `BuildCustomOverrideSkin` / `BuildCustomOverrideSkinStaged`, with `Drain()` replaying an enumerator without pausing. The synchronous names still exist and still block -- the skin editor's `Reload` needs the skin to exist when it returns -- so there is exactly one copy of the logic. The staged build reports the result through an `Action<string>` because an iterator cannot return one.
- **The list must be complete before gameplay, so it is finished rather than raced.** `FinishBakeNow(reason)` drains whatever is left; `OnSceneLoaded` calls it for every scene except the title screen (a follower can be standing in any of the others), and the scene-change trim is skipped entirely while `SkinsReady` is false. `BakeAllInBackground` re-checks `_pendingBake` on every resume so a drain that happened between two of its frames ends the coroutine instead of double-baking.
- **The scene guard skips Splash as well as the title screen, and that is the whole trick.** First attempt excluded only `MenuSceneRefs.SceneName`, which measured as a total non-fix: `Awake` did drop from 14900ms to 4111ms, but Splash loads about a second later and `FinishBakeNow` drained the lot inside `sceneLoaded` -- `SCENE 'Splash': CultTweaker spent 10350ms`, the same freeze one callback to the right. Naming the follower-free scenes was whack-a-mole and grepping the decompiled source for scene literals did NOT settle it -- it turns up `"Main Menu"` and nothing else (the one `"Loading"` hit is an inspector `[Header]`), yet the boot chain is Splash -> **BufferScene** -> Main Menu, and each unlisted one drained the bake the moment it loaded. The rule that holds is the inverted one: every boot scene comes BEFORE the title screen and gameplay is only reachable by leaving it, so `BakeMayContinueDuring` returns true until `_titleScreenSeen` is set in `OnMenuScene` and then only for the menu itself. No scene name has to be known in advance. It does not matter that a player can skip the splash and loading screens -- the title screen is on the same allowlist, so the bake keeps running until they actually start playing.
- **`StartCoroutine` runs the body up to the first yield synchronously**, so `BakeAllInBackground` opens with a bare `yield return null`. Without it the first skin's config read, texture loads and `ComposeSkin` still happened inside `Awake` -- worth ~1.4s, and visible as the 'config binds and folder scans' figure in `STARTUP TOTAL` jumping from 191ms to 1558ms.
- **The warm-up split works exactly as intended, and the log proves it per skin.** `hallejrtestskin_base`: composed 677ms, regions warmed 9449ms, **repacked 5ms** (it was a single 7076ms repack before). `lmab_base`: warmed 4223ms, repacked 6ms. Every warm skin after them warms in 1-16ms. The cost did not vanish -- it cannot, none of it can leave the main thread -- it moved into frame-sized pieces, and the total rises somewhat (14.9s -> ~18.6s of main-thread work) because of the per-frame overhead of yielding after nearly every region.
- **A small frame budget is SLOWER overall, not gentler -- this is the counter-intuitive one.** Total bake work measured across four runs: 14.9s when drained in one go, 18.6s when partly spread, **29.5s at an 8ms budget**. Extracting one region per frame appears to force a GPU->CPU readback stall that back-to-back extractions amortise, and the same deferral made `hallejrtestskin_base`'s repack jump from 5ms to 5378ms when frames passed between its warm-up and its `PackTextures`. So `WarmFrameBudgetMs` is a variable, not a const: 250ms while the splash and buffer scenes are up (nothing on screen can stutter), dropping to `WarmBudgetOnScreen` (8ms) in `OnMenuScene` once the player is actually looking at something. Do not 'improve' this by lowering the boot budget.
- **The repack is atomic.** `GetRepackedSkin`'s `PackTextures` cannot be split across frames, so whatever it costs lands on whichever frame it happens in. That is the floor on how smooth this can be made without replacing the repacker outright.
- Boot after the staged bake: `STARTUP TOTAL` 3119ms (from 14900ms) and **MENU READY at 20.0s, down from 26.1s** -- the mod's main-thread work now overlaps the game's own addressable and bank loading instead of queueing in front of it.

### The atlas page decompression, and why it is off by default (SpineLoaderHelper/FollowerSpineLoader.cs)

- **The real cost of a follower skin bake is a format mismatch.** `REGION PROBE` reports `copyTextureSupport Basic, Copy3D, DifferentTypes, TextureToRT, RTToTexture` and the follower page as **8192x8192 BC7, readable False**, in a **Gamma** colour space project. `AtlasUtilities.ToTexture` copies each region out with `Graphics.CopyTexture` into an **RGBA32** destination, so Unity decompresses that entire page again for every single region -- ~121ms each, 616ms for the worst. That is the whole reason a bake costs seconds. Region counts prove it is per-source, not per-skin: `ruffTemplateSkin_base` warms 118 regions in 20ms once the page work is done, while `hallejrtestskin_base` warms 78 in 9442ms.
- **DEAD END, REMOVED: decompressing the page once. Do not try this again.** The payoff was real and large -- bake work 22194ms -> 6546ms, `hallejrtestskin_base` warm 9442ms -> 1590ms, `lmab_base` 4407ms -> **1ms** (same page, already decompressed), **MENU READY 28.2s -> 16.1s** -- but every base part baked out of the copy lost its art and rendered as a white silhouette with the correct shape and alpha. Both ways of making the copy failed the same way:
  1. `Graphics.Blit` into a RenderTexture plus `ReadPixels` (396ms). The copy was demonstrably not blank: a 32x32 sample grid found 175 distinct colours. The art was still gone.
  2. `Graphics.CopyTexture` for the whole page (2748ms), chosen because it is the exact call AtlasUtilities makes per region and therefore 'could not lose anything the working path keeps'. It did. Whole-page BC7 -> RGBA32 does not behave like the per-region copy with an explicit rect.
  Both were confirmed by the config toggle: art returned immediately with the feature off. Also do not trust a distinct-colour sample as validation -- 175 of 1024 samples read as 'has content' while the art was already gone, so the guard passed a broken page straight through.
- **What is left is scheduling, and the budget is now whole-skin.** Spreading a skin's extraction across frames costs far more in total than doing it in one block (14.9s drained in one go, 18.6s partly spread, 25.2s at 250ms, 29.5s at 8ms -- and 28.2s at 250ms with the page work removed, for a 34.3s boot). So `WarmFrameBudgetMs` starts at `long.MaxValue`: nothing is on screen during the splash, so a skin is baked whole and the coroutine yields BETWEEN skins instead. That keeps `Awake` at ~3.1s (from 14900ms) without paying the spreading tax. It drops to `WarmBudgetOnScreen` (8ms) in `OnMenuScene` so any leftover trickles once the player can see something.
- **The bake cost itself is unsolved.** ~15s of main-thread Unity API that cannot be threaded and cannot be made cheaper without replacing `GetRepackedSkin`. The remaining idea, untried: cache the packed atlas and attachment layout to disk after the first bake, so later boots load a png instead of repacking -- invalidated on config/png mtime.

### The follower skin disk cache (SpineLoaderHelper/FollowerSkinCache.cs)

- **Staged/coroutine baking was reverted on request.** It did cut the frozen white window from 14900ms to ~3100ms, but every arrangement of the frame budget just moved the same ~12s somewhere the player could feel it (menu at 20.0s with stutter, 30.8s clean, 37.1s with whole-skin bites, or 24.5s plus 12727ms on the first level load when paused at the menu). The bake is back in `Awake` behind `SpineMemory.Phase`, and `FinishBakeNow`/`BakePaused`/`WarmRegions`/the scene guard are all gone. Do not reintroduce them: the numbers are in the section above and none of the arrangements won.
- **What is cached, and why it is so small.** Not the Skin -- serialising Spine attachments would mean duplicating their geometry and their maths. Only the packed atlas as a png plus, per attachment, the rect it occupies (`u v u2 v2 x y width height offsetX offsetY originalWidth originalHeight rotate degrees`). Restoring is the same operation `GetRepackedSkin` performs minus the packing: `ComposeSkin` as usual (cheap -- it does no extraction), then `attachment.SetRegion(...)` per part, which is a spine-unity extension that recomputes offsets and UVs itself. That is why the region extraction, the expensive part, never runs on a cache hit.
- **Invalidation is a stamp over every input the bake reads, by CONTENT**: an MD5 of `config.json` and of every png in the variant folder, keyed by file name and sorted, plus the game version, the mod version and a `Format` constant. Content hashing rather than size-and-write-time is what makes a cache **shareable** -- timestamps do not survive zipping, copying, syncing or a git checkout, so a timestamp stamp would miss on somebody else's machine and they would rebake for nothing. It costs almost nothing: the inputs are a few small pngs and a json, and the multi-megabyte `atlas.png` is an OUTPUT, never hashed. The game version is in there because the cached atlas holds pixels baked out of the game's own follower atlas -- if an update redraws that art, an old cache is wrong rather than merely stale. The one case it cannot see is a mod that repaints the atlas without changing the game version; delete the `.ctcache` folders after installing one. Editing a part, adding a png, deleting one, or changing `overrideBaseSkin` all move the stamp, so the next bake is a real one and rewrites the cache. The skin editor's `Reload` goes through the same `BuildCustomOverrideSkin`, so a save from the editor refreshes the cache in that session rather than waiting for a restart. Bump `Format` if the layout shape ever changes, so old caches are ignored instead of misread.
- **Every failure path bakes normally.** A missing file, a stale stamp, a part the layout does not know, an unreadable png, an exception -- all return false and fall through to the real bake. A cache that cannot be read is a slow boot, never a wrong skin, which is the property the atlas-page experiments did not have. `TryApply` also resolves every attachment BEFORE mutating any of them, so a bail-out cannot leave a half-applied skin.
- `FollowerSkinFolders` maps the baked key back to its variant folder. The key is `skinName + "_" + variantName` and skin names contain underscores, so it cannot be split apart reliably -- the folder is remembered at load time instead.
- The cache lives in `FollowerSkins/<skin>/<variant>/.ctcache/` (`atlas.png`, `layout.json`). Deleting that folder forces a rebake and is the first thing to try if a cached skin ever looks wrong.
- **Measured result of the cache, second boot onward:** `STARTUP PHASE FollowerOverrides` 12688ms -> **1964ms**, its memory 2900MB -> **498MB**, `STARTUP TOTAL` 14900ms -> **4526ms**, and **MENU READY 26.1s -> 15.6s**. All 13 variants report `restored from cache in 11-119ms`. The first boot after any edit still bakes that skin normally and rewrites its cache.
- **The editor's prebake was silently a no-op, and the cache had nothing to do with it.** `FollowerSkinPreview.Show` returned early whenever `signature == _worn`, but the registered skin the game baked at startup carries the SAME signature as the editor's own packed build -- `Signature` describes the document, not which kind of skin is worn. So `Prebake()` printed 'one pause now instead of on your first edit', matched the early-out, did nothing, and the stall landed on the first edit exactly as before. The guard is now `signature == _worn && (mayUseRegistered || !WearingRegistered)`: skip the work only when what is worn is the kind of skin the caller asked for.
- Note the editor's own `Pack` still pays a cold region extraction, because a cache hit at startup means `AtlasUtilities.CachedRegionTextures` is never populated. That is what the prebake is for -- it moves the cost to the idle moment after a skin is opened rather than removing it.
- **Shipping a skin with its cache**: include the `.ctcache` folder in the zip and the recipient loads it without ever baking. Nothing in it is machine-specific -- the atlas is a png of finished pixels and the packing rects are stored, so no `PackTextures` layout or GPU behaviour has to be reproduced. `CopyImages`/`CopyOtherVariants` still do NOT copy it, which is correct: a new variant is different content and must bake once for itself.

### hideFromBuildMenu, and the Custom group in the structure tool (APIHelper/CustomStructureLoader.cs, Patches/CustomStructureMenuPatches.cs, MapEditor/Tools/StructureTool.cs)

- **Hiding is a subtraction at the menu, not a refusal to register.** A hidden structure keeps its `TYPES` value, its prefab, its cost and its saved-map placements -- only the build menu drops it. Refusing to register it instead would break every map that already places one, and the map editor could not offer it either.
- **`FollowerCategory.GetStructuresForCategory` is the single funnel.** The decompiled `FollowerCategory` calls it for the Misc, Food and Items tabs (lines 39-41) and again for the aggregate list (132-134), so one postfix covers the whole follower build menu. `FaithCategory` has a PRIVATE method of the same name for a different menu, which COTL_API does not patch, so custom structures never appear there to begin with.
- **Ordering matters: COTL_API adds them in its own postfix on the same method**, so ours has to run after to take any back out -- `[HarmonyPriority(Priority.Last)]` plus `[HarmonyAfter("io.github.xhayper.COTL_API")]`. Without both, the removal can run first and do nothing.
- **The structure tool's Custom group shows ALL custom structures, hidden ones included** -- that is the point of the feature: the player cannot build them, a map maker still has to place them. Custom structures were previously appended to the end of the Build Menu Structures grid; they now have their own group, and `CustomEntries()` is searched alongside `StructureEntries()` so a hidden structure is still findable by name. The group is added to the dropdown only when at least one custom structure exists, and `ShowGroup(string)` replaces the two-way if that the dropdown callback and `ShowCurrentGroup` were each duplicating.
- The group lists everything in `CustomStructureManager.CustomStructureList`, so structures from OTHER mods appear too -- deliberate, it is the Custom group, not the CultTweaker group. Labels stay as `InternalName` rather than the localized name, because another mod's `GetLocalizedName()` may return a raw localization key.

### Compiling against the game's own assemblies (CustomSpineLoader.csproj, lib/GameLibs)

- The `CultOfTheLamb.GameLibs` NuGet package stops at **1.5.15.979** -- `nuget.bepinex.dev` has nothing newer and nuget.org has only a placeholder -- so there is no package for a current game. `lib/GameLibs` is that package rebuilt from an installed **1.5.26.1057**: the same assembly set, publicized and stripped to reference-only with `BepInEx.AssemblyPublicizer.Cli`. `tools/gamelibs.sh` rebuilds it after a game update. Both the libs and that script are gitignored.
- **Newtonsoft comes from the game now**, not as a transitive package dependency. The old package pinned 12.0.3 while the game loads 13.0.0.0, so the compile-time API disagreed with the runtime one.
- **The two Rewired assemblies are copied UNTOUCHED.** Publicizing and stripping `Rewired_Core` makes Windows Defender delete it as `Trojan:MSIL/AgentTesla!pz`. It is a false positive -- the `!pz` suffix is an automated cluster match, not a signature -- but the reasoning is sound from the classifier's side: Rewired is an input library, so its metadata is full of keyboard/mouse/HID names and `user32` P/Invokes, and stripping leaves those declarations with no bodies to show intent, which is the shape of a .NET keylogger. An unmodified copy has the same bytes as the file the game already runs, so there is nothing to score. Do not 'tidy up' the inconsistency by publicizing them.
- **Publicizing was never the reason for any reflection here** -- the 1.5.15 package was publicized too. Only three members were genuinely unreachable, all because they postdate 1.5.15, and all three are now called directly: `Structure.UpdateGraphBounds(bool)`, `BiomeBaseManager.GroundValidationCollider` and `Structure.ObstructionColldiers`. Everything else that still uses reflection does so for a reason that has not changed: COTL_API internals (`CustomEnemyList`, `CustomPlayerSpines`, `SelectedSpine`, `CachedTextures` -- `lib/COTL_API.dll` is not publicized), duck-typing across enemy types that share no base class, `AdventureMapNode` fields read before the component is destroyed, and stack-frame walking in LevelPlayback.

### Dressing the skin editor preview (ModUI/SkinEditor/SkinCostume.cs, FollowerSkinPreview.cs, Tools/SkinSetupPanel.cs)

- **The Costume preview section is the outfit half of the Customize Follower command, as one dropdown.** The command's other four pickers (clothing, hat, special, necklace) were built and then dropped: they hang accessories off a follower rather than changing the art a skin replaces, so there is nothing to preview. `FollowerOutfitType.Custom` is left out of the list for the same reason SpinePatches rewrites it to None on a real follower -- no skin of that name exists in the atlas, so composing it throws. A costume lives on the editor, never on the document; it is never written to config.json.
- **The overlay is computed by subtraction, not by rebuilding the game's name tables.** `FollowerBrain.SetFollowerCostume` composes a costume by stacking skins on a base one and RETURNS the result, so `Overlay()` asks it to dress the very base skin the edited skin is built on, then drops every entry whose attachment is reference-equal to the plain base's. What is left is exactly what the outfit added. Doing the stacking by hand would mean copying `GetOutfitName`/`GetHatName`/`GetRobesName`/`GetClothingName`/`GetNecklaceName` (three of the five are private) and keeping them in step with every game update.
- **Order is base-then-costume, because outfits replace attachments rather than adding new ones.** A robe supplies its own attachment for the slot's existing name (`BODY_TOP` and friends), so `Dress()` does `AddSkin(ourSkin)` FIRST and then `SetAttachment` for each overlay entry. Adding the custom skin last would silently strip the clothing back off -- and this matches the game, which puts a follower's skin down before its clothes.
- **Calling `SetFollowerCostume` sets a skin on the preview skeleton as a side effect** (and applies vanilla slot colours). That is why `Wear()` dresses BEFORE its own `SetSkin`, and why the following `SetSlotsToSetupPose` + `ApplyColours` is what actually decides the colours.
- **`info` is passed as null on purpose.** Every trait, cursed-state, season and `AddAttachments` branch in that method is guarded on `info != null`, so a null follower gets the plain outfit with none of a real follower's state mixed in. `SkinCostume.Level` (3) stands in for the follower level that levelled hats and hooded robes read.
- **A costume change must not cost a repack.** `Show()` keeps `_wornSkin` alongside `_worn`; when the document signature is unchanged and only `SkinCostume.Key` moved, it re-dresses the skin already packed. The empty-string `Key` when nothing is chosen means an untouched Costume preview section never differs from a fresh preview.
- An outfit the atlas has no art for makes `FindSkin` return null and `AddSkin(null)` throw; that is caught and reported as a status line, leaving the bare skin on show. Do not try to pre-validate the choices -- that would mean rebuilding the name tables the subtraction exists to avoid.

### Labelled dropdowns (MapEditor/MapEditorUI.cs, MapEditorWidgets.cs)

- `CreateLabelledDropdown` puts the name on the left and the field on the right, the shape `CreateToggle` already has, for a setting whose name cannot be read off its value -- "Base skin", "Animation", "Outfit". A plain `CreateDropdown` still suits a list whose value names itself (the skin/variant picker, the layer adder), and those keep the full width.
- It wraps `CreateDropdown` rather than reimplementing it: the field is re-anchored to the right of a container row, and the `LayoutElement` `CreateDropdown` leaves on it goes inert because the container has no layout group.
- **The floating option list measures `MapEditorDropdown.ListFrom`, not the field.** A labelled dropdown points it at the whole row so the list stays full width and long option names are not clipped -- the reason the split does not cost readability.

The skin editor's left panel is one "Skins" section down to the layer grid: the skin and variant, its colour set, the base it is built on, and then the two things that only change how it is being looked at (outfit and animation). Headers for Base, Preview, Costume preview and Colour sets were removed once each of those became a single labelled row -- a header over one row is noise.

### The UI says "color", not "colour"

The mod's user-facing strings use the American spelling; the code and these notes keep the British one. Only strings that reach a panel, button, header or status line were changed -- log lines, material names and identifiers (`ColourSet`, `BuildColourSets`, `GripColour`) were left alone, so a grep for `olour` still finds the code that handles it.

### Custom player spine on the UI skeletons (SpineLoaderHelper/PlayerUiSkin.cs, Patches/PlayerUiSpinePatches.cs)

- **Four screens draw the player with a SkeletonGraphic of their own**, so COTL_API's swap of the world `PlayerFarming.Spine` never reaches them: the inventory (`CharacterMenu._skeletonGraphic`, built in `Init()`), Knucklebones (`KBPlayer._lambSpine`, set up in `Configure(PlayerFarming, Vector2, Vector2)`), Flockade (`FlockadePlayer._avatar`, `Configure(side, bag, prompts, parent, playerFarming)` -- the `new` overload on FlockadePlayer itself) and the Flockade result card (`FlockadeEndGameAnnouncement.SetAvatar`, which copies the winner's data asset and starting animation but not its skin). Each has a postfix that hands the graphic to `PlayerUiSkin.Apply`.
- **The data asset cannot just be swapped, because this game's spine-unity predates multi-page SkeletonGraphic.** `lib/GameLibs/spine-unity.dll` has `OverrideTexture` but no `allowMultipleCanvasRenderers`: a SkeletonGraphic draws with exactly one texture. Custom player spines are four pages, so a plain swap renders everything off page one with page one's texture.
- **DEAD END: repacking the live skin onto one page (`GetRepackedSkin`, 4096) and pointing `OverrideTexture` at it.** It worked, but cost a visible pause on first open (~370 regions), 64MB per cached page, and the old repack's handling of rotated regions and mesh UVs showed as small art glitches during animation -- "slight animation errors" compared with the F7 portrait, which draws from the original atlas. Do not go back to it.
- **What is done instead: the graphic runs, a mirror draws.** `Apply` still gives the graphic the custom data asset (`Initialize(true)`) and the player's LIVE skin, so the game's own `SetAnimation` calls (`knucklebones/*`, the Flockade prefab names) keep working on a skeleton that has them. But the graphic's canvas drawing is held at `canvasRenderer.SetAlpha(0)`, and a `PlayerUiMirror` component on the same GameObject renders that same skeleton the way the world does: an offscreen `SkeletonAnimation` whose `skeleton` FIELD is pointed at the graphic's Skeleton object (the `Skeleton` property is read-only here), `LateUpdate()`-ed by hand each frame so it rebuilds a multi-material mesh from the pose the graphic's Update already applied, a camera rendering that into a RenderTexture, and a RawImage child of the graphic showing it. Same rig as the F7 portrait, sharing its `Untint` and `RenderNeutral`.
- **The RawImage sits where the graphic would have drawn -- and a SkeletonGraphic's local unit is NOT a skeleton unit.** `SkeletonGraphic.UpdateMesh` (decompile the live `spine-unity.dll` with `ilspycmd -t Spine.Unity.SkeletonGraphic`; the stripped lib has no bodies) calls `meshGenerator.ScaleVertexData(canvas.referencePixelsPerUnit)`, so the mesh is skeleton units x 100. The first build placed the RawImage in raw skeleton units and produced a 9-pixel lamb, which read as no lamb at all. The child is anchored at the parent's pivot (where the skeleton origin lands) and placed at the setup-pose bounds (`GetBounds`) x ppu, padded by 60% of the larger side; the camera frames the same region in skeleton units. `UpdateMesh` also does `skeleton.SetColor(color)`, so a screen that tints its avatar tints the shared skeleton and the mirror follows -- win/lose animations jump and lean outside the idle silhouette, and the texture must not crop them. The RenderTexture is sized through the scale chain (graphic lossyScale / root canvas lossyScale x scaleFactor -- world corners measured near zero because both screens are set up mid show-tween), 1.5x supersampled, 2048 max, and re-measured for two seconds after attach so it grows once the tween finishes. CanvasGroup fades reach the RawImage as a child; `Graphic.color` tints do not.
- **The live skin is worn as-is, like the F7 portrait.** That carries fleece cycling and hidden slots for free. It also means the weapon skin comes along, which vanilla's inventory lamb does not show -- but `Weapons/Normal` is a real skin (37 attachments: hands, crown, gauntlets), so stripping the weapon skin's entries would strip the hands. Leave it.
- **A missing animation aborts the swap rather than the screen.** `Initialize(true)` plays `startingAnimation` and the screens call `SetAnimation` with names from prefab fields (Flockade) or properties (Knucklebones, which pick the goat or lamb set from the stored PlayerFarming); an unknown name throws inside game code. Every name is checked against the custom data first and the vanilla graphic is left alone if any is absent, with a log line saying which. Custom spines are full lamb skeletons (~725 animations) so this should not fire.
- `initialSkinName` is cleared before `Initialize(true)` when the custom data lacks it (`Skeleton.SetSkin(string)` throws). If the prefab's asset and the custom asset differ in `SkeletonDataAsset.scale`, the RectTransform is scaled by the ratio, reading the previous asset's scale so repeated swaps chain instead of stacking; both are 0.005 today. `Detach` (player no longer on a custom spine when a persisting screen is set up again) restores alpha and the original asset.

### The layer tree (MapEditor/MapEditorLayerPanel.cs; hooks in RuntimeMapEditor.cs, Tools/SelectTool.cs, Tools/TriggerTool.cs)

- **What a "layer" is: exactly what the Select tool would pick.** The tree lists the direct children of `SelectTool.SelectionStops()` (the room, its Custom/Scenery/Heavy groups, the editor's content root) that pass `IsSelectable`, each resolved through `SelectionRoot` so the row is the same object a world click would land on. In the base, `IsRegion` containers are stepped into one level, mirroring the walk in `SelectionRoot`. Anything with a `CTMapTrigger` underneath is skipped there and listed under Triggers from `TriggerTool.Triggers` instead. Those three SelectTool members were private and are now `internal` for this; do not fork the rules.
- **Groups are by what placed it**, in a fixed order: Shapes (`CTEditorShape`/SpriteShapeController), Podiums (`PodiumTool.IsTracked`/`CTPodiumBehavior`), Enemies (`EnemyTool.IsTracked`/`UnitObject`), NPCs (`NpcTool.IsTracked`), Structures (`StructureTool.TryGetPlacedName`, which also supplies the label, or a `Structure` component), and everything else as Props. Group open/closed state is remembered by name across rebuilds.
- **It is not a tool.** It is a panel `RuntimeMapEditor` owns, created in `CreateUi` BEFORE the `_ownChrome` sweep so F6/screen tools hide it with the rest, ticked from `Update` (guarded like the tool update) and sized from `LateUpdate` like the options panel: header + column height, capped so it stops above the collapsed Shortcuts button. `OnToolChanged` hides it for `LevelTool` and `DungeonBuilderTool`, which own the screen.
- **Clicking a row routes through the tools, never around them.** `PickFromLayers` switches to the Select tool (`SelectTool(tool)` early-outs if it already is) and calls the new `SelectObject`, which is the private `Select` - same highlight, same details panel. `PickTriggerFromLayers` does the same with `TriggerTool.SelectTrigger`. `AttachButton` calls `BlockWorldClicks`, so the click does not also fall through to the world.
- **Rebuilding is cheap because it is rare.** The tree re-reads the room when `RuntimeMapEditor.EditCount` moves (every history-recorded edit) and otherwise every 3s, and only rebuilds rows when a signature of (instance id, label) per group changes. Selection highlighting is separate and per frame: `SelectTool.Selected`/`TriggerTool.SelectedTrigger` are compared by reference and only the two affected rows are recoloured, through the row's `MapEditorHover.Idle` so leaving a hover restores the lit colour.
- **Mutual exclusion with the shortcut hints** is two one-way calls: `SetCollapsed(false)` -> `RuntimeMapEditor.CollapseShortcuts()`, and `ToggleShortcutsCollapsed` expanding -> `_layers.SetCollapsed(true)`. Neither path calls back, so there is no ping-pong. The tree starts collapsed so existing users keep their hints until they open it.

### Multi-selection and groups (Tools/SelectTool.cs, MapEditorGroups.cs, MapEditorLayerPanel.cs)

- **The selection is a primary plus extras** (`_selected` + `_extra`; `Selection` returns them primary-first). Moves and depth drags apply the primary's delta to all; the outline box and the yellow grip frame the union of every member's bounds (`TryUnionBounds`); resize, flip and the look toggles stay primary-only and the blue resize node hides while more than one is selected. Gestures record every member, so one undo puts a whole drag back. Delete walks the list and reports how many were protected. Clone stays single (the primary) -- cloning a group is a later problem.
- **Three ways in, one path:** a world click (`Select`), shift-click in the world (`ToggleInSelection` adds or removes), the tree's `PickFromLayers`/`PickManyFromLayers`. All end in `SelectMany`, which runs the wanted list through `MapEditorGroups.Expand` -- picking any member of a group picks the group -- and then `AfterSelectionChanged` for highlight, gizmos, status and details.
- **A group is a tag, not a parent.** `CTEditorGroup {GroupId, GroupName}` sits on each member root and the hierarchy is untouched. DEAD END considered and rejected: reparenting members under a group object. Shapes hang off the room's composite collider, enemies off their containment, structures off their placement regions; moving any of them breaks the game's handling, and `SelectionRoot` would have changed meaning under everything else. `Expand`/`Members` are `FindObjectsOfType<CTEditorGroup>` walks -- groups are few and the calls are per click, not per frame.
- **Ctrl+G toggles.** Two or more selected, not already exactly one whole group -> `Create` (name "Group N", a fresh 8-char id) and a history entry that dissolves it. Exactly one whole group selected -> `Dissolve`, with a history entry that re-creates it under the same id and name. Members of other groups pulled into a new group simply move to it (one tag per object); undoing that new group does not put them back in their old ones.
- **Groups survive saves by name and position, because nothing else survives.** `SelectTool` is an `IMapDataContributor` now and writes `CTNodeBlueprint.Groups` from `Capture()` (id, name, each member's display name and world position). `Restore` runs at the end of `BlueprintLoader.LoadRoutine` (after totems, before `Mark`) and again in `EnterEditorMode` for base and hub sessions whose objects are already in the scene; it matches each saved member to an untagged root from `AllSelectableRoots()` with the same cleaned name within 0.05 units, keeps groups that find two or more, and skips ids that are already live so reopening the editor is quiet. A moved-and-not-saved object loses its group on the next load -- expected, the save is what is restored.
- **The room scan is sliced too, and gated on a cheap stamp.** The 3s fallback sweep used to run `AllSelectableRoots` + `Classify` over the whole room on the spot - tens of milliseconds in a base town, felt as a spike every few seconds while doing nothing. Now `QuickStamp()` (child counts under the selection stops + trigger count, microseconds) decides whether the fallback scans at all, and a scan that does run is a `Scan` object pumped `ScanPerFrame` (60) candidates per Tick, base regions' children appended to its queue FLAGGED so they are never region-checked again (the first cut re-checked them, walked into every large object's parts, grew the queue as it ran and pinned the frame rate until it finished - one level is the rule, as in `SelectionRoot`), finished by `FinishScan` which compares the signature and only then rebuilds. `Invalidate()` forces a scan (open, tool change, fold, reveal, settled edit); `SelectTool.AllSelectableRoots` is the one-shot twin kept for group restore.
- **The list is virtualised.** The tree is a flat list of row MODELS (`_rows`: section / folder / item / trigger, with text, indent, colour, target); only rows inside the viewport plus `ViewMargin` have a GameObject, taken from `_pool` and re-bound by index in `ShowVisibleRows` each LateUpdate. The scroll column's `VerticalLayoutGroup` and `ContentSizeFitter` are destroyed at construction and the content height is set by hand (`SetContentHeight`, `Pitch` = row + spacing); every row sits at `RowTop(index)`, so the sticky header, scroll-to-row and shift-ranges all work in indices. DEAD END before this: one live row per object. A base town lists 600+, and 600 TMP labels meant every hover, highlight and scroll tick re-meshed the whole canvas - that was the "lag only in the base". Folding a section now only rebuilds the model from the last scan's result (`_lastFolders/_lastKinds/_lastTriggers`), no rescan.
- **Edits are debounced.** `EditCount` changes set `_refreshDueAt = now + EditSettle` (0.4s) instead of rescanning at once - a drag marks an edit every frame, and the per-frame `AllSelectableRoots` + `Classify` scan was the lag on selecting and moving structures. A plain selection change no longer forces a layout pass either; only a real rebuild does.
- **The tree follows the selection.** When the primary selection (or the selected trigger) changes, `Reveal` opens the section its row lives in if that is folded - the group folder, or the kind - rebuilds, and queues a scroll; `ScrollToPending` runs from `LateUpdate` once `_rebuildFrames` has drained so the layout and the panel height are settled, and only moves when the row is actually outside the viewport, centring it when it does. Scroll maths are in "distance from content top": `RowTop(row) = -anchoredPosition.y - height*(1-pivot.y)`, viewport top = `(1 - verticalNormalizedPosition) * (contentHeight - viewHeight)`.
- **Section headers are sticky.** A pinned copy of the last section header that has scrolled above the top edge sits over the list (`_sticky`, a child of the panel content ABOVE the scroll root, with an opaque backing so rows do not show through), relabelled each LateUpdate from `_sections` (key, row rect, text - filled by `GroupRow`, top-level sections only, not group folders). The next header pushes it up as it arrives (`push = viewTop + RowHeight - nextTop`). Clicking it toggles that section and then scrolls so the real header lands at the top (`_scrollToHeader`), which is where the eye already is.
- **The tree shows folders.** A grouped object is listed under its folder in the Groups section and NOT under its kind, so every object has one row; a folder left with a single member is shown back under its kind. The folder row selects the whole group; the small +/- at its left folds it. Shift-click on an item selects the run between the last plain-clicked item (`_anchor`) and it in the tree's display order (`_rowOrder`, folders and kinds alike), exactly like a file list. Every selected row is lit (`_lit` set diffed against `SelectTool.Selection` per frame).

