# Cult Tweaker 2.0.0: World Shaper, Pre-release 3

Everything new since pre-release 2.

## Base editor

- Added editing of your own base town: move and place structures, terrain and paths with the F4 tools
- Added save protection - your changes live in the mod's own files, and the game's save is never written to
- Added protection so bought buildings can be moved but never deleted
- Added see-through for structures that block the camera while editing
- Improved the Select, Shape and Structure tools for base work

## Hub building

- Added a build totem for hubs: build the game's own structures through the game's own build menu
- Added building costs and refunds, kept in the hub rather than the save
- Added a placement region so hub building cannot reach your real town

## Main menu editor

- Added a "Customize Menu" button on the title screen
- Added named menu presets, kept as folders with their art; one applies on every launch
- Added a Look tool: palette pick and blend, background colour, hue / saturation / brightness,
  dither, grain, the glitch effect, and a negative effect for the field behind the lamb
- Added a Centrepiece tool: skin, animation, position, scale and rotation for the lamb, or a
  custom spine dropped into the preset's folder
- Added a Title tool: replace the logo with any png, move, resize or hide it, and set, move,
  recolour or hide the edition line

## Editor look

- Improved every editor panel with the game's own artwork
- Improved toggles and sliders into the game's own settings widgets
- Improved section headers into the game's ornate dividers
- Improved buttons with the pause menu's ribbon: accented for main actions, grey inside lists
- Improved dropdowns with the game's own caret
- Improved the confirm prompt with a translucent backdrop

## Weather

- Added per-room weather to the Lighting tool, for dungeons and hubs
- Added every strength of every weather, including ones the game never shows (extreme wind)
- Added protection so the game cannot clear a room's chosen weather early

## Triggers

- Added a Camera shake action

## Fixes

- Fixed an issue where the room editor's status bar went quiet after using the world editor
- Fixed an issue where dragging an editor slider stuttered the menu's lamb animation
- Fixed an issue where the menu editor's panels vanished after loading a preset
- Fixed an issue where the editor buttons had no ribbon until a save had been entered
- Fixed an issue where preset art changed on disk was not picked up until restart
