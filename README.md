# Download
- Download via [NexusMods](https://www.nexusmods.com/cultofthelamb/mods/49)

# Links
- Read more about my mods at my [Website](https://cotlminimod.infernodragon.net/)
- Join the discord server for support, feedback, suggestions and general modding talk: [modding discord](https://discord.gg/MUjww9ndx2)
- If you like the mod, consider donating [here](https://ko-fi.com/infernodragon0)! Thank you for checking out the mod!

# Cult Tweaker
A mod that allows players to add their own custom content into game! The features will be updated over time to match COTL API's functionalities.

Currently features:
- Load Custom Player Spines
- Load Custom Follower Spines
- Custom Follower Color Control
- Override Original Structure Designs
- Load and Create Custom Items

More features to come soon!

### How to Load Player Skins
After installing this plugin correctly, you should be able to navigate to ```Bepinex > plugins > CotLSpineLoader > PlayerSkins``` folder and setup your custom spine skins there.

#### Exporting a usable Spine Skin
- Ensure that you have exported the Spine Skin via ```3.8.99 Spine```
- Export as a JSON file.
- Output: ```Nonessential data: TRUE, Animation cleanup: TRUE, Warnings: TRUE```
- Texture atlas: ```Pack TRUE``` with default Pack Settings
- If any warnings occur, it may be best to fix them, or the skin may not load into game.

#### Setting up the Player Spine Skin for Custom Spine Loader
- In the ```PlayerSkins``` folder, create a new folder for each individual spine that you want to load into game
- The folder should be named as your skin name, for this example, we will use ```DEBUGSKIN``` as the skin name
- In the folder ```PlayerSkins/DEBUGSKIN``` add the following files
- A .json file that is your exported Spine Skeleton
- An .atlas file that is your exported Spine Atlas
- Any amount of .png files that are packed together with it
- A config.json file which specifies settings, more info below.

A Complete Skin folder would look something like this
```
| CustomSpineLoader.dll
| PlayerSkins
    | DEBUGSKIN
        | player-main.json
        | player-main.png
        | player-main.atlas
        | config.json
```

### Config File For Spine Skins
Each Spine Skin folder must have a config.json file in it. The following is how you should create the file:
``` 
{   
    "defaultSkin": "CustomSkinName",
    "skins": [
        "CustomSkinName",
        "CustomSkinName2" 
    ]
}
```
defaultSkin will be the first skin that is loaded when the game starts

skins is an array of any amount of strings of the Skins that exist in your Spine Skeleton that you want to load into game

#### Optional settings

``` 
{   
    "defaultSkin": "A_Tiger",
    "skins": [ "A_Tiger" ],
    "disableFleeceCycling": true,
    "hiddenSlots": [
        "CROWN",
        "CROWN_EYE",
        "images/PonchoLeft",
        "images/PonchoRight"
    ]
}
```

**disableFleeceCycling** (default `false`) stops fleece transmog from dressing this spine. The fleece
writes to the body, the poncho, the rope and the bell, so on a skin that draws its own body the
lamb's artwork replaces yours. The fleece picker keeps working and keeps remembering your choice -
it simply is not applied while this spine is worn, and comes back when you swap to one that allows
it.

**hiddenSlots** (default empty) is a list of slot names this spine never draws. Use it for parts of
the lamb rig your art replaces, such as the crown. Hiding a slot **in the Spine editor does not
export**, and clearing its setup attachment only lasts until the first animation that keys the slot -
the lamb skeleton keys `CROWN` in 276 animations and `PonchoLeft` in 181, and the game re-attaches
the crown by name whenever it flies back or you change room. Listing the slot here replaces its
artwork with fully transparent copies in the skin that all of those resolve through, so it stays
hidden however it is re-attached. The attachments are replaced rather than deleted because some are
required to exist: the game throws `Attachment not found: CROWN` if the crown cannot be resolved by
name.

Both settings apply per player and only while that spine is worn; other skins in the same file, and
the vanilla lamb, are untouched.

### Custom Enemies

Enemies live in `BepInEx > plugins > CultTweaker > CustomEnemies`, one folder each, with a
`config.json`. **The AI is a vanilla enemy's**: an enemy here names one of the game's own enemy
prefabs to mimic and keeps its brain wholesale — its states, attacks, animations and death — then
changes what can be changed from outside.

```json
{
  "EnemyName": "Test Brute",
  "Mimic": "Assets/Prefabs/Enemies/DLC/Enemy Swordsman Wolf.prefab",
  "Health": 24,
  "Scale": 1.5,
  "BossHealthBar": true,
  "BossBarName": "THE BRUTE",
  "SkinName": "",
  "Tuning": {
    "maxSpeed": 0.075,
    "AttackWithinRange": 5.0,
    "MaintainTargetDistance": 3.0,
    "DoubleAttack": 1
  }
}
```

| Field | Default | What it does |
| --- | --- | --- |
| `EnemyName` | — | Required. The name it is listed under in the map editor. |
| `Mimic` | a bat | The vanilla enemy prefab this one is built from, and whose AI it uses. |
| `Health` | `5` | Total HP. |
| `Scale` | `1` | Multiplies the spawned enemy's size. A brute is the same enemy at 1.6. |
| `SkinName` | — | A skin on the skeleton — your own if the folder ships one, otherwise the mimic's. |
| `BossHealthBar` | `false` | Shows the game's boss bar across the top of the screen while it is alive. |
| `BossBarName` | `EnemyName` | What that bar is labelled. |
| `Tuning` | — | Any public field or property on the mimic's own controller, by name. |
| `SkeletonPath`, `AtlasPath`, `TexturePaths`, `SkeletonScale` | auto-discovered | Set only when the folder holds more than one Spine export. |

Drop a Spine export (`.json` skeleton, `.atlas`, `.png` pages) in the folder to give it your own
art; without one it wears the mimic's skeleton, and `SkinName` then picks a skin off *that*.

**Tuning** is where the behaviour lives. Every vanilla enemy exposes its numbers as public fields —
`EnemySwordsmanWolf` alone has around fifty — so `AttackWithinRange`, `MaintainTargetDistance`,
`DoubleAttack`, `ChargeAndAttack`, `KnockbackModifier`, `Damage`, plus `maxSpeed` and
`SpeedMultiplier` on every enemy, are all reachable by name. Values are numbers because a tuning
table is numbers; a true/false field takes 0 or 1. A name that does not exist on that enemy is
reported in the log rather than ignored silently, so a typo is findable.

Custom enemies appear in the map editor's enemy tool under **Custom (mods)**, place like any other,
and save into blueprints.

### Custom Structures

Structures live in `BepInEx > plugins > CultTweaker > CustomStructures`, one folder each, with a
`config.json`:

```json
{
  "StructureName": "Custom Altar",
  "StructureDescription": "A custom altar for your cult.",
  "SpritePath": "icon.png",
  "BuildDurationMinutes": 60,
  "BuildOnlyOne": true,
  "RequiresTempleToBuild": true,
  "CanBeFlipped": false,
  "Bounds": { "X": 2, "Y": 2 },
  "ItemCost": { "LOG": 20, "STONE": 10 }
}
```

#### Building a Spine structure instead of a sprite

Add a `Spine` block and drop the Spine export (`.json` skeleton, `.atlas`, `.png` pages) into the
same folder. The structure is then built as an animated skeleton rather than a flat sprite:

```json
  "Spine": {
    "SkinName": "Marble",
    "Animation": "idle",
    "Loop": true,
    "Offset": { "X": 0, "Y": 0, "Z": 0 },
    "Scale": { "X": 1, "Y": 1, "Z": 1 }
  }
```

Every field is optional:

| Field | Default | What it does |
| --- | --- | --- |
| `SkinName` | the skeleton's default skin | Which skin to dress the structure in. |
| `Animation` | none | Which animation to play once placed. Empty holds the setup pose, which is what a static prop wants. |
| `Loop` | `true` | Whether that animation loops. |
| `Offset` / `Scale` | zero / one | Nudges the skeleton relative to the structure's tile. |
| `Rotation` | matches the structure's sprite | The world is drawn on a tilt, so anything standing upright in it is rotated `-60` on X (`300` in the inspector). Left out, the skeleton copies whatever the structure's own sprite does, falling back to that tilt. Set it to `{ "X": 0, "Y": 0, "Z": 0 }` for a prop meant to lie flat on the ground. |
| `SkeletonScale` | `0.005` | Spine's import scale, for art authored at a different unit size. |
| `SkeletonPath`, `AtlasPath`, `TexturePaths` | auto-discovered | Set these only when the folder holds more than one export. |
| `ShaderName` | `Spine/Skeleton` | The material shader used for the skeleton. |
| `HideSprite` | `true` | Hides the flat sprite underneath. Set `false` to keep a painted base under an animated skeleton. |

`SpritePath` is still used as the **build menu icon**, so keep a sprite for it; the icon PNG is
skipped when the atlas pages are auto-discovered. Without one the structure falls back to a
placeholder icon and still builds as the skeleton.

A ready-made example is in `Templates/CustomStructures/ExampleSpineStructure` — an occultist scamp
knelt in prayer, written against the same Spine export the `CustomNpcs/TestNpc` sample uses, so
copying those four art files in beside it makes it work as-is.

## Known Issues
- The Custom Player Spines may not have the correct color when attacking with certain weapons.

## Developed by [InfernoDragon0](https://github.com/InfernoDragon0)

Try [CotLMiniMods](https://cult-of-the-lamb.thunderstore.io/package/InfernoDragon0/CotLMiniMods/) for lots of custom stuff, or [Supercharged Tarots](https://thunderstore.io/c/cult-of-the-lamb/p/InfernoDragon0/Supercharged_Tarots/) for overpowered tarots, and [Supercharged Followers](https://thunderstore.io/c/cult-of-the-lamb/p/InfernoDragon0/SuperchargedFollowers/) to bring your followers to battle!