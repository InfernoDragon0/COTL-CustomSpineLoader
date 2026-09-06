# Cult Tweaker 2.0.0: World Shaper, Pre-release 5

Everything new since pre-release 4.

## Layers

- Added a **Layers** panel at the left edge of the map editor, opened with the + beside the
  shortcut hints: every object in the room listed by what placed it - Shapes, Podiums, Enemies,
  NPCs, Structures, Props and Triggers - with the same names the tools use
- Click a row to select that object with the Select tool; the tree follows your selection in the
  world and scrolls to it, and section headers stay pinned while you scroll
- Shift-click two rows to select the run between them, like a file list
- Built for big rooms: a base town of six hundred objects scrolls and highlights without lag

## Multi-selection and groups

- Added multi-selection to the Select tool: Shift-click adds an object to the selection or
  takes it out; move and depth drags move everything together, one undo puts the whole drag
  back, and Delete removes the lot
- Added groups with **Ctrl+G**: two or more selected objects become a group, and picking any
  member picks the group; Ctrl+G on a whole group dissolves it, and both are undoable
- Groups are saved with the map and come back on load; the Layers panel shows each group as a
  folder, and the folder row selects the whole group

## Whiteboard

- Added a **Whiteboard** tool: draw freehand over the room, in the world, to plan with - a
  Show whiteboard toggle, Draw with a size slider and nine colours, Erase with its own size
- Strokes are never selectable and never part of the room; they show only while the editor is
  open, are saved with the map, and travel to the other player live as you draw
- One drag is one stroke and one undo; an erase drag is one undo too, and **Clear all** wipes the
  board in one undoable step

## Structures

- Added the DLC dungeons' dressing to the structure tool's browser: **DLC Dungeon / Ewefall**
  and **DLC Dungeon / Rot**, with the whole decoration plots beside them as **... Plots**
- Added the **Art** groups (the cave biome pieces, the base's weeds, some boss and shop room
  dressing) and **Tile Decorations** (the destructible tiles)
- The old **Placement Objects / DLC** and **VFX / DLC** folders are now **... / Misc**, so "DLC"
  in the browser means the dungeons and nothing else

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
  down, not only on release
- Saving from either side saves on the host's machine; the guest receives the files
- Custom dungeon levels work in a session: the host starts a level and the guest follows into the
  same layout with the same rooms, and both can edit the rooms together
- Added a "Resync" button in the shortcut panel for when the two rooms ever look different
- Added a `MapEditor / NetVerbose` config that logs every change sent and received, and a
  "Sync self-test" button that runs the room through the sync on one machine, for tracking down
  a desync
- Every saved object now carries an id in its map file, so it can be told apart from its
  neighbours across a save, a load and a second machine; maps saved by pre-release 4 still open

## Fixes

- Fixed door pads growing across the whole room when the room's centre is a hole (a pit, a tree):
  the check that decides whether a doorway reaches the floor now measures against the floor itself,
  so pads stay at their normal length and no longer lay a band of collision across the hole

## Compatibility

- Built for Cult of the Lamb **1.5.26**; earlier versions of the game are not supported
