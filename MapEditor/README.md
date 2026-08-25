# CultTweaker Map Editor

An in-game room editor for Cult of the Lamb. It captures, edits and rebuilds a dungeon room as a
**node blueprint** (`CustomNodeBlueprints/<name>.json`), and chains those rooms into a **level
blueprint** (`CustomLevelBlueprints/<name>.json`) that plays through the mod's own dungeon.

| Key | Action |
| --- | --- |
| `F4` | Open / close the editor (Dungeon1 scenes only) |
| `F5` | Enter the test dungeon — or, with the editor open, reset the room |
| `F6` | Hide / show the editor's UI while it stays paused (screenshots); on an open world map, flip between play view and editing |
| `F7` | CultTweaker panel (fleeces, player spines, mod info) — not part of the editor |
| `WASD` / arrows | Pan the camera |
| `Z` / `X` | Zoom in / out |
| Wheel | Switch tool (or scroll the list under the cursor) |
| `Ctrl+Z` | Undo the last placement |
| `Ctrl+S` | Quicksave under the current name (both editors) — see below |
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
  - [Clear](#clear) · [Load Map](#load-map) · [Level](#level) · [Hubs](#hubs) · [Dungeon Builder](#dungeon-builder)
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

| Panel control | What happens |
| --- | --- |
| **New Hub** | Travels to the base, raises the town room, empties it, opens the editor on a blank hub |
| **Edit Hub** | The same trip, then rebuilds the saved hub in the room for editing |
| **Visit Hub** | The same trip, then rebuilds it and leaves the player in it |

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

| Action | Target | Behaviour |
| --- | --- | --- |
| Move players to trigger | another trigger's Id | walks the players to that volume's centre |
| Move players to object | a clicked object | walks them to that object (falls back to the authored position) |
| Talk to custom NPC | a registered `InternalName` | runs that NPC's dialogue tree and waits for it |
| Play animation on players | an animation on the player skeleton | plays it once, or loops it for 2 / 5 / 10s |
| Hub spawn point (player arrives here) | — | a mark, not a step: the trigger's position is where a hub's arrival walks the player to. Does nothing when the volume is entered, and a hub cannot be saved without one |

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
| Open world map | a saved world map | opens it and waits until it closes — a hub's travel portal |
| Return to base | — | ends any run and sends the players home, as the dungeon portal does; nothing after it runs |

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
| `Tools/DungeonMapCanvas.cs` | The dungeon map screen: chrome, free-placed node graph, geometric picking |
| `Tools/DungeonMapSkin.cs`, `Tools/DungeonNodeVisual.cs` | The game's own adventure-map art, cloned and stripped (twins of `CustomMapSkin` / `CustomNodeVisual`) |
| `../APIHelper/ModContentPaths.cs` | Finds the same content folders inside other mods' `CultTweaker` folders |
| `Tools/*.cs` | One file per tool, plus shared gizmos, ghosts and protection rules |
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

| Gesture | What it does |
| --- | --- |
| Left click a node | selects it; its outline turns red |
| Left drag a node | moves it |
| Left click empty map | clears the selection |
| **Ctrl** + left click | places a new node there (the only way to add one) |
| Drag the blue corner | resizes the selection |
| **Del** | deletes the selected node (there is no button for it) |
| Right click a node | links the selection to it, or removes that link if it exists |
| Right click a link | removes it |

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

| NodeType | Vanilla style |
|---|---|
| `Base` | the home node |
| `Dungeon` | a standard Ewefall dungeon |
| `MiniBoss` / `Boss` | the miniboss / boss nodes |
| `Key` / `Lock` | the key and lock nodes, with their extra outline |
| `Reward` | the reward cache |

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

| File | What it is |
|---|---|
| `CTWorldMap.cs` | World map schema and JSON read/write |
| `WorldMapProgress.cs` | Per-save-slot completion, key/lock bookkeeping, run tracking |
| `WorldMap/WorldMapScreen.cs` | The full-screen map: canvas, pause, layers, states, travel |
| `WorldMap/WorldMapNodeView.cs` | One node's icon, label and state rendering |
| `WorldMap/WorldMapLine.cs` | The link lines |
| `WorldMap/WorldMapAssets.cs` | The map's sprite/spine cache (and the Keep() discipline) |
| `WorldMap/CustomMapSkin.cs` | Loads the game's DLC map prefab and cuts stripped clones from it |
| `WorldMap/CustomNodeVisual.cs` | Drives a cloned vanilla node's state visuals and hover |
| `WorldMap/CustomLineVisual.cs` | Drives a cloned vanilla link's four line renderers |
| `WorldMap/WorldMapEditor.cs` | The editor host: dock, options panel, undo, tool plumbing |
| `WorldMap/Tools/*.cs` | The four world tools |
