# Aniel — test follower built from the LambWar parts

Two folders, both needed. Copy them into the CultTweaker plugin folder so they land at:

```
BepInEx/plugins/CultTweaker/FollowerSkins/Aniel/base/     the head (FollowerSkins method)
BepInEx/plugins/CultTweaker/FollowerSpines/Aniel/         the clothes (FollowerSpines pack)
```

`preview.png` is a setup-pose render of the result (front, facing away, and the sample it was
matched against); `renderer_snapshot.png` adds the vanilla Cat for scale and five expressions.
Both come from a renderer that reproduces the loader's placement maths, not from the game, so
expect to nudge a value or two in the F8 skin editor.

## What went where

| LambWar part | Slot / attachment | Notes |
| --- | --- | --- |
| face.png | HEAD_SKIN_BTM | the Cat's face plate |
| face2.png | HEAD_SKIN_BTM_BACK | shown when facing away |
| ears.png | HEAD_SKIN_TOP, HEAD_SKIN_TOP_BACK | the Cat's colour layer, drawn over the face |
| hair.png | EXTRA_BTM | a Cat placeholder slot behind the head |
| horns.png | MARKINGS | placeholder over the face; the game hides it facing away |
| hair.png | EXTRA_TOP_BACK | facing away, the hair covers the back of the head; no horns from behind |
| Glasses.png | EXTRA_TOP | placeholder drawn before the eyes, so the eye lines sit on the lenses like the sample |
| Eyes*.png | EYE_LEFT / EYE_RIGHT | each strip split in two; see below |
| mouth*.png | MOUTH | six mouths cover the common expressions |
| dress.png | SHAWL_BTM (SHAWL, SHAWL_UP) | clothes pack |
| neck.png | SHAWL_TOP (SHAWL) | clothes pack; hidden facing away |
| skirt.png | BODY_TOP, BODY_TOP_UP | clothes pack, on the basic robe's meshes, widened and lengthened to the ankles |

Eye strips: `Eyes` → EYE, EYE_UP. `Eyes2` → closed / sleeping. `Eyes_Angry` → angry, very angry,
dissenter angry, half-closed angry. `Eyes_Happy` → smile, smile up/down, enlightened. `Eyes_Sassy`
→ half closed, sleepy. `Eyes_Squint` → squint, worried, sick. Expressions not listed keep the
Cat's own eye art.

Mouths: `mouth` → happy-2 (the resting mouth), indifferent, mumble, sleep, bedrest.
`mouth_closed_happy` → cheeky, kiss. `mouth_happy_open` → happy, grin, talk happy. `mouth2` → talk indifferent, hungry, derp.
`mouth_sad` → sad, sadder, worried, sick. `mouth_sad_teeth` → scared, sacrifice.

Arms, legs and the naked body are colour-only entries tinted the face's grey-blue. Legs are the
normal follower legs, as asked.

## Older mod versions

`config.legacy.json` is for CultTweaker builds before 2026-09-14. Rename it to `config.json`
there. It differs in two ways:

- No mouth parts. Those builds could not load a part whose attachment name has a slash (every
  Cat mouth is `Face/MOUTH_*`) and logged `Material with texture name "MOUTH_..." not found`.
  Aniel keeps the Cat's mouths on them. The mouth pngs can stay; unreferenced files are ignored.
- Right-eye parts that share an attachment name with the left eye (EYE, EYE_CLOSED, EYE_SQUINT...)
  have a negative `scaleX`. Those builds named every part texture `<skin>_<part>`, and COTL_API
  caches converted textures by name, so the right eye was drawn from the left eye's pixels and
  crashed the bake with `IndexOutOfRangeException` in `Graphics_CopyTexture` when the sizes
  differed. The crops are now the same size and the mirror turns the left eye into the right.

Do not use the legacy file on the current build: the mirror would flip a correctly loaded right eye.

## Why the clothes are a separate pack

The game always layers a `Clothes/*` skin (at least `Clothes/Robes_Lvl1`) over the animal skin,
and it fills BODY_TOP, BODY_BTM, SHAWL and SLEEVE with attachments of the same names. Anything a
follower skin puts in those slots is covered. So the skirt, cape and bow are a custom clothes
skin, picked in **Customize Follower** with "Custom Clothes" set to `Aniel: Clothes/Aniel`
(both toggles on). The sleeves and the robe's extra layer are blanked in that pack so the bare
arms show under the cape.

## Things to check in game

- The art is drawn at 0.8× its pixel size so Aniel's chin sits near the Cat's and the clothes
  land on the rig's body. Change `G` or `CHIN` in `tools/lambwar_build.py` (or the scale entries
  in `config.json`) if she should be larger or sit differently.
- The robe colour set may tint the clothes slots. If the skirt or cape come out tinted, set those
  slots white in Customize Follower.
- A hooded follower (level-ups) replaces the head placeholders, hair and horns included, the same
  as for every animal.
- The glasses are drawn before the eyes on purpose. If the eyes should sit behind the lenses
  instead, move `glasses` to slot 103 `LESHY_FACE` (all nine names) and cut the lens alpha.
- No ghost tail: the sprite was not supplied.
