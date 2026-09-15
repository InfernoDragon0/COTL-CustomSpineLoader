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

#### Adding brooms

```
{
    "defaultSkin": "A_Tiger",
    "skins": [ "A_Tiger" ],
    "brooms": [ "Mops/Feather", "Mops/Bone" ]
}
```

**brooms** (default empty) lists skins in this spine that are brooms — the thing the lamb sweeps poop
with. They join the broom picker in the F7 panel and can be worn by **any** spine, not just yours,
because only the `TOOLS` slot is taken from them: the artwork comes out of your atlas and goes onto
whatever skeleton the player is wearing, the same way a custom fleece does. They show in the picker
as `YourFolderName: Mops/Feather`, so two mods offering a broom of the same name stay apart.

A broom skin must put its art on the **`TOOLS`** slot. Name the attachment `Tools/Mop`, as the game's
own `Mops/1`–`Mops/10` skins do; if your skin has exactly one attachment on that slot it is taken
whatever it is called, so a hand-built skin works too.

This is independent of `disableFleeceCycling` and of `fleeceCyclingOnly` — a spine can donate brooms
and still be a wearable skin, or be nothing but a bag of brooms. The spine is only loaded when
somebody actually picks one of its brooms, and a broom chosen in a previous session loads its spine
at startup so the player comes up holding it.

### Follower hats and clothes

Followers can wear hats and clothes that other mods draw. A **wardrobe pack** is a Spine export
of the follower rig in `BepInEx > plugins > CultTweaker > FollowerSpines > YourPack`, with a
config that says which of its skins are hats and which are clothes:

```
| FollowerSpines
    | YourPack
        | Follower.json
        | Follower.atlas
        | Follower.png
        | config.json
```

```
{
    "hats": [ "Hats/Crown", "Hats/Beanie" ],
    "clothes": [ "Clothes/Armour", "Clothes/Sundress" ]
}
```

Each name is a skin in your export. To wear one, talk to a follower, pick **Customize Follower**,
turn on **Enable Customization** and **Follower Costume Override**, and choose from **Custom Hat**
and **Custom Clothes**; the follower changes as you scroll and keeps the look when you close the
menu. Turning the costume override off takes the hat and clothes off with it, like the other
costume choices on that page. It shows as
`YourPack: Hats/Crown`, so two packs offering the same name stay apart. The follower's own
hat, necklace and everything else the game dresses it in stays, and can still be changed with the
costume override on the same page: a custom hat covers the game's hat art, custom clothes cover
the game's clothes art, and the game's hat and necklace go back on top of custom clothes.

**How to make one.** Start from the rig in `followerSkel/stripped/` (see its README): open the
JSON in Spine, add a skin, put your art on the slots, export as JSON with an atlas. A **hat** is a
skin with attachments on `HAT_NORMAL` and `HAT_UP` (the two head angles). **Clothes** are one
skin with as many pieces as the garment needs: the game's robe covers `BODY_TOP`, `BODY_BTM`,
`BODY_EXTRA`, `SHAWL_TOP`, `SHAWL_BTM` and the four `SLEEVE_*` slots, and yours can use any of
those or fewer. Every piece in the skin comes across together, so a coat with sleeves and a
collar is still one entry in `clothes`. Keep the attachment names the rig already uses on each
slot (`HAT_NORMAL` on `HAT_NORMAL`, and so on); a slot given exactly one attachment under a
different name is taken as that slot's anyway. Meshes, weights and cross-atlas art all work, as
they do for fleeces, on one condition: **do not delete, add or reorder bones** in the rig. A
pack whose bones differ from the game's is refused with the reason in the log.

**Exporting.** Export the whole template as it is, Cat and all, with its full atlas. Only the
skins you list are ever used; the rest is parsed and ignored. Do **not** delete the Cat or any
other skin before exporting: a skin owns bones, the editor deletes those bones with it, and the
pack is then refused. Linked meshes work, so the easiest clothes are the robe's attachments
duplicated as linked meshes with your image swapped in. A full-template export is bigger than it
needs to be, which only shows as a longer first wear.

The pack is only loaded when a follower first wears something from it, so unused packs cost
nothing at startup.

In multiplayer, a follower's hat, clothes, colour and costume override reach the other player only
with a COTL MP Steam build that carries follower looks; the pack itself must be installed on both
machines, or that follower shows its ordinary look there.

### Custom NPC Quests

A custom NPC can hand out quests. They appear in the objectives panel on the right of the screen
beside the game's own, with the same tick boxes and counters, and they finish on the same kinds of
criteria: gathering items, killing enemies, clearing a dungeon, building, performing a ritual,
growing the flock, or any of the game's own story beats. Dialogue offers them, nags about them and
takes them in. Quests are written in the NPC's `config.json`; the full guide is in
[CustomNpcQuests.md](CustomNpcQuests.md).

```
"Quests": [
    {
        "Id": "firewood",
        "Title": "Wood for Bramble",
        "Goals": [ { "Type": "collectItem", "Target": "LOG", "Count": 10, "Text": "Gather logs" } ],
        "Reward": { "Items": [ { "Item": "GOLD_NUGGET", "Count": 5 } ] }
    }
]
```

### Custom Weapons

A player spine can add weapons to the game. They are declared in the same `config.json`, under
`weapons`, and each one is built on a vanilla weapon: it keeps that weapon's heavy attack, sounds,
hit shapes and pickup card, and swaps in your look, your combo and your numbers. The full field
reference and a guide to hit timings are in [CustomWeapons.md](CustomWeapons.md).

```
{
    "defaultSkin": "Lamb",
    "skins": [ "Lamb" ],
    "weapons": [
        {
            "name": "FlameSword",
            "displayName": "Flame Sword",
            "description": "Burns a little.",
            "baseWeapon": "Sword",
            "skin": "Weapons/FlameSword",
            "pickupAnimation": "weapons/get-weapon-flamesword",
            "icon": "flamesword.png",
            "combo": [
                { "animation": "attack-combo1-flame", "damage": 1.0, "speed": 1.0 },
                { "animation": "attack-combo2-flame", "damage": 1.0, "speed": 1.2 },
                { "animation": "attack-combo3-flame", "damage": 1.5, "speed": 0.9 }
            ]
        }
    ]
}
```

**name** is the weapon's id inside this spine. **baseWeapon** is one of `Sword`, `Axe`, `Hammer`,
`Dagger`, `Gauntlet`, `Blunderbuss`, `Shield`, `Chain` (default `Sword`).

**skin** is a skin in your Spine file holding the weapon's artwork. Make it the way the game makes
`Weapons/Poison`: a skin whose attachments sit on the `WEAPON` slot (and `Weapons/SwordHeavy` for
the heavy attack). If you reuse the base weapon's animations, name the attachments the way those
animations expect them - `Weapons/Sword`, `Weapons/Axe`, `Weapons/Hammer`, `Weapons/Dagger`,
`Weapons/Blunderbuss`, `Shield` - so the animation shows your image where it showed the vanilla
one. If you make your own animations, key the `WEAPON` slot to whatever you named your attachments.

**combo** is the chain of hits, in order; leave it out to keep the base weapon's chain. Each hit
takes an `animation` (default: the base weapon's hit at that position), a `damage` (the base
damage before level, fleece and tarot bonuses; vanilla swords hit for about 1), and a `speed`
(playback speed of the animation, 1 = as authored).

**Hit boxes.** A hit lands as a circle spawned in front of the player when the animation fires its
"Attack Deal Damage" event; the artwork itself never collides. Every hit starts as a copy of the
base weapon's hit at the same position in its chain, so a Sword-based weapon swings like a sword
and an Axe-based one like an axe, and these optional fields on a hit reshape it:

```
{
    "animation": "attack-combo3-flame", "damage": 1.5, "speed": 0.9,
    "range": 1.2,          // how far in front of the player the circle is placed (world units)
    "hitboxRadius": 0.9,   // radius of the circle; defaults to range, as in vanilla
    "knockback": 1.5,      // push on whatever it hits
    "lungeSpeed": 20,      // forward lunge while swinging, and
    "lungeDuration": 0.15, //   how long it lasts
    "cameraShake": 0.5,
    "attackType": "Melee", // Melee, Heavy, Projectile, Poison, NoKnockBack, Ice, Charm
    "canQueueNext": true,  // the next hit may be queued during this one
    "canTurn": true        // the player may turn while it plays
}
```

Anything left out keeps the base hit's value. Vanilla swords reach about 1 unit; the range
multiplier from tarot cards applies to both `range` and `hitboxRadius` as it does in vanilla.

Your attack animations should carry the three events the game's combat waits for: `Attack Deal
Damage`, `Attack Can Break` and `Attack Has Finished` (copy them from a vanilla attack). An
animation with none of them gets them added when the spine loads, at 35% and 65% of its length and
at its end; `hitAt` and `breakAt` on the hit (fractions from 0 to 1) move the first two.

**pickupAnimation** plays when the weapon is picked up (default: the base weapon's). **icon** is a
PNG in the spine folder for the HUD and the podium (default: the base weapon's). **modifierSkin**
is the vanilla modifier layered under yours (`Normal`, `Poison`, `Critical`, `Healing`, `Fervor`,
`Godly`, `Necromancy`; default `Normal`). `inPool: false` keeps the weapon out of random podium and
chest rolls.

**Loading.** The weapon's artwork lives in your spine file, which is only loaded when needed: at
boot if a player is wearing it, otherwise the moment one of its weapons appears in a room (a podium
rolls it, a chest or a fallen enemy drops it), in the background while the player walks over.
Someone who reaches it before the load finishes holds it with the base weapon's look until the art
lands, a few seconds at most. To avoid even that, set **preloadWeapons** to `true` at the top level
of the config (beside `defaultSkin`): the spine then loads at boot whether or not anyone wears it,
at the cost of a longer start and a few hundred megabytes of memory per spine.

**Any spine can wield it.** A custom weapon is not tied to the spine that declares it. Whoever holds
it, on any skin, gets its artwork copied onto their skeleton the way a fleece is, and the numbers
above. Animations are the one thing that cannot travel: on a skin that does not have your custom
animations, the base weapon's animations play instead, still with your artwork if your attachments
use the base weapon's names as described above. The same applies in multiplayer, where each player
may wear a different spine.

**Getting one in a run.** Custom weapons join the weapon pool, so weapon podiums and chests can roll
them. To place one on purpose, open the map editor's Podium tool: its **Weapon** list has every
vanilla weapon and every custom one (`<spine>/<name>`), and a podium placed with one picked always
offers that weapon.

### For other mod authors

Other mods can add content to CultTweaker without any code: put your files in a `CultTweaker`
folder inside your own plugin folder, for example
`BepInEx/plugins/YourMod/CultTweaker/CustomDungeonMaps/YourDungeon.json`, and they load beside the
player's own. There is also a small code contract for listing, finding and entering custom content.
Both are documented in [ModdingApi.md](ModdingApi.md).

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
  "HideFromBuildMenu": false,
  "Bounds": { "X": 2, "Y": 2 },
  "ItemCost": { "LOG": 20, "STONE": 10 }
}
```

Set `HideFromBuildMenu` to `true` to keep a structure out of the player's build menu while still
being able to place it yourself in the map editor. Useful for scenery that belongs to a map rather
than to a cult. Hidden or not, every custom structure appears in the map editor's structure tool
under the **Custom** group, and saved maps load it either way.

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