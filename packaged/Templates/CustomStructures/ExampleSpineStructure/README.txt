Spine structure example
=======================

A custom structure that stands in the world as a Spine skeleton instead of a flat
sprite. Copy this folder into CultTweaker/CustomStructures/ and drop the four art
files listed below in beside config.json.

Files this folder needs
-----------------------
  config.json     the structure definition (already here)
  icon.png        the build-menu icon; NOT part of the skeleton
  <name>.json     the Spine skeleton export
  <name>.atlas    its atlas
  <name>.png ...  the atlas pages, one file per page

Nothing is named in the Spine block because everything is auto-discovered: the
skeleton is the one .json that is not config.json, the atlas is the one .atlas,
and the pages are every .png except the one SpritePath claims as the icon. Set
SkeletonPath / AtlasPath / TexturePaths only if a folder holds more than one set.

This config is written against the same skeleton CustomNpcs/TestNpc uses - the
Human.json / Human.atlas / Human.png / Human2.png export - so copying those four
files in makes it work as-is: a scamp in the SF_Occultist_Scamp skin, looping the
"_idles/pray" animation.

The Spine block
---------------
  SkinName        which skin to dress the skeleton in; empty = its default skin
  Animation       looped once placed; empty = holds the setup pose, which is what
                  a static prop wants
  Loop            whether that animation repeats
  SkeletonScale   Spine's import scale. 0.005 matches the game's own skeletons;
                  change it only for art authored at a different unit size
  Offset / Scale  nudge this one structure in the world
  Rotation        left out, the skeleton faces the camera the way the
                  structure's own sprite does, falling back to the world's
                  -60 tilt on X (300 in the inspector). Set it only for a prop
                  that should lie flat on the ground: { "X": 0, "Y": 0, "Z": 0 }
  HideSprite      the sprite the game paints on the building is switched off once
                  the skeleton is up. false keeps it, e.g. a painted base under an
                  animated skeleton

A skin or animation name the skeleton does not have is a warning in the log, not a
crash: the structure falls back to the default skin and the setup pose. The log
lines to look for are "Found custom structure folder: ..." at startup and
"Structure '...': spine attached" the first time one is placed.

The icon still matters
----------------------
SpritePath is only the build-menu icon now, not what stands in the world, but a
structure with no icon gets a placeholder in the menu - so it is worth providing.
It lives in the same folder as the atlas pages and is deliberately excluded from
page discovery; without that it would be loaded as an atlas page and the skeleton
would render blank.
