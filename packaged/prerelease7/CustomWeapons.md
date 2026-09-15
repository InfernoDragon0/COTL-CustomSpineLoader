# Custom Weapons

A custom player spine can add weapons to Cult of the Lamb. Each one is built on a vanilla weapon
and swaps in your artwork, your combo, your numbers and your timings. This page covers the config
in full and how the hit timings work. The short version lives in the main README.

- [Where the config goes](#where-the-config-goes)
- [Weapon fields](#weapon-fields)
- [Hit fields](#hit-fields)
- [Making the weapon's skin](#making-the-weapons-skin)
- [Hit timings](#hit-timings)
- [Damage and speed](#damage-and-speed)
- [Loading and preloading](#loading-and-preloading)
- [Testing a weapon](#testing-a-weapon)
- [Limits](#limits)

## Where the config goes

Weapons are part of the spine that carries their artwork. They go in the spine's `config.json`,
under `weapons`, beside the usual skin settings:

```
PlayerSkins/
    FlameLamb/
        player-main.json
        player-main.atlas
        player-main.png
        flamesword.png          <- optional icon
        config.json
```

```json
{
    "defaultSkin": "Lamb",
    "skins": [ "Lamb" ],
    "preloadWeapons": false,
    "weapons": [
        {
            "name": "FlameSword",
            "displayName": "Flame Sword",
            "description": "Burns a little.",
            "lore": "Forged in the Old Faith's last fire.",
            "baseWeapon": "Sword",
            "skin": "Weapons/FlameSword",
            "modifierSkin": "Normal",
            "pickupAnimation": "weapons/get-weapon-flamesword",
            "icon": "flamesword.png",
            "inPool": true,
            "combo": [
                { "animation": "attack-combo1-flame", "damage": 1.0, "speed": 1.0 },
                { "animation": "attack-combo2-flame", "damage": 1.0, "speed": 1.2 },
                { "animation": "attack-combo3-flame", "damage": 1.5, "speed": 0.9,
                  "range": 1.2, "hitboxRadius": 0.9, "knockback": 1.5 }
            ]
        }
    ]
}
```

A spine can declare any number of weapons. A weapon is known to the game as `<spine folder>/<name>`,
for example `FlameLamb/FlameSword`; that is the name the map editor's Podium tool shows.

## Weapon fields

| Field | Required | Default | Meaning |
| --- | --- | --- | --- |
| `name` | yes | | Id of the weapon inside this spine. Letters, digits and underscores are safest. Changing it later changes the weapon's id, so saves that had it in the weapon pool forget it. |
| `displayName` | no | `name` | Name on the pickup card and the HUD. |
| `description` | no | empty | Text under the name on the pickup card. |
| `lore` | no | empty | Small italic line on the pickup card. |
| `baseWeapon` | no | `Sword` | The vanilla weapon this one is built on: `Sword`, `Axe`, `Hammer`, `Dagger`, `Gauntlet`, `Blunderbuss`, `Shield` or `Chain`. Decides the heavy attack, the sounds, the swipe shape, the default combo and the default pickup animation. |
| `skin` | no | none | A skin in your Spine file holding the weapon's attachments. Without it the weapon looks like the base weapon. See [Making the weapon's skin](#making-the-weapons-skin). |
| `modifierSkin` | no | `Normal` | The vanilla modifier skin layered under yours: `Normal`, `Poison`, `Critical`, `Healing`, `Fervor`, `Godly` or `Necromancy`. Cosmetic only; the modifier's effect is not applied. |
| `pickupAnimation` | no | base weapon's | Animation played when the weapon is picked up from a podium, the ground or a shop. |
| `icon` | no | `hookIcon`, else the base weapon's | A PNG in the spine folder, used on the HUD, the podium and the pickup card. Drawn at the size of the base weapon's icon. |
| `hookIcon` | no | the chain's hook | Chain-based weapons only: a PNG in the spine folder drawn at the end of the chain while it swings. See [Chain-based weapons](#chain-based-weapons). |
| `hookScale` | no | `1` | Chain-based weapons only: size of `hookIcon` relative to the chain's own hook. |
| `inPool` | no | `true` | Whether weapon podiums, weapon rooms and chests can roll this weapon. `false` keeps it to podiums pinned to it by the map editor. |
| `combo` | no | base weapon's chain | The chain of hits, in order. See [Hit fields](#hit-fields). |

And one field at the top level of the spine config, not inside a weapon:

| Field | Default | Meaning |
| --- | --- | --- |
| `preloadWeapons` | `false` | Load this spine at boot so its weapons never show the base look while loading. See [Loading and preloading](#loading-and-preloading). |

## Hit fields

`combo` is a list; each entry is one hit, in the order they chain. Pressing attack repeatedly plays
hit 1, 2, 3, ... and wraps to hit 1 after the last. Every hit starts as a copy of the base weapon's
hit at the same position in its chain (a longer chain than the base's wraps: hit 4 of a Sword-based
weapon starts as a copy of the sword's hit 1), and each field below replaces one value on that
copy. Anything left out keeps the base hit's value.

| Field | Default | Meaning |
| --- | --- | --- |
| `animation` | base hit's | The Spine animation this hit plays. See [Hit timings](#hit-timings). |
| `damage` | base hit's | Base damage before the run's multipliers. Vanilla light hits are around 1; see [Damage and speed](#damage-and-speed). |
| `speed` | `1` | Playback speed of the animation. `1.5` is half again as fast, `0.5` half speed. Everything in the animation scales with it, including where the hit lands in time. |
| `range` | base hit's | How far in front of the player the hit's circle is placed, in world units. Vanilla swords use about 1. Also the circle's radius unless `hitboxRadius` is set. |
| `hitboxRadius` | `range` | Radius of the hit's circle, in world units, independent of `range`. A large `range` with a small `hitboxRadius` is a poke; a small `range` with a large `hitboxRadius` is a sweep around the player. |
| `knockback` | base hit's | Push applied to whatever the hit strikes. |
| `lungeSpeed` | base hit's | Speed of the forward lunge the player takes while swinging (vanilla default 20). |
| `lungeDuration` | base hit's | How long that lunge lasts, in seconds (vanilla default 0.15). |
| `cameraShake` | base hit's | Camera shake strength when the hit lands. |
| `attackType` | base hit's | `Melee`, `Heavy`, `Projectile`, `Poison`, `NoKnockBack`, `Ice` or `Charm`. Changes how enemies react (`NoKnockBack` is what it says; `Ice` and `Charm` apply those effects the way curses do). |
| `canQueueNext` | base hit's | Whether pressing attack during this hit queues the next one. `false` makes the player wait for the hit to finish before the next input counts. |
| `canTurn` | base hit's | Whether the player may change facing while this hit plays. |
| `hook` | chain's own | Chain-based weapons only: the hook's move on this hit and the shape of its sweep. See [Chain-based weapons](#chain-based-weapons). |
| `hitAt` | `0.35` | Only used when the animation has no attack events: where "Attack Deal Damage" is placed, as a fraction of the animation's length. |
| `breakAt` | `0.65` | Only used when the animation has no attack events: where "Attack Can Break" is placed, as a fraction of the animation's length. |

## Making the weapon's skin

The game does not tell weapons apart by skin. Its weapon skins (`Weapons/Normal`, `Weapons/Poison`
and so on) carry only the tint of the modifier; the shape of the weapon comes from the attack
animations, which key the `WEAPON` slot to a named attachment: `Weapons/Sword`, `Weapons/Axe`,
`Weapons/Hammer`, `Weapons/Dagger`, `Weapons/Blunderbuss`, `Shield`. Your skin works the same way.

Make a skin in your Spine project (any name; `Weapons/FlameSword` follows the vanilla pattern) and
put the weapon's attachments in it, on the `WEAPON` slot. Then:

- **If you reuse the base weapon's animations**, name your attachments the way those animations
  expect them. A Sword-based weapon whose skin puts an attachment named `Weapons/Sword` on the
  `WEAPON` slot shows your image wherever the sword animations showed the sword. Do the same on
  `Weapons/SwordHeavy` for the heavy attack (`AxeHeavy`, `HammerHeavy`, `Blunderbuss_Heavy` for the
  others).
- **If you make your own animations**, key the `WEAPON` slot to whatever you named your attachments,
  and give the skin those attachments. Nothing else is required, but keep the base names too if you
  want the weapon to look right on other spines (next point).

**Other spines.** A custom weapon is not tied to the spine that declares it. Whoever picks it up, on
any skin, gets the skin's attachments copied onto their skeleton the way a fleece is copied. What
cannot be copied is animations, so on a skin without your custom animations the base weapon's
animations play, and they show your artwork only if your skin also provides the base attachment
names. The same applies in multiplayer, where each player may wear a different spine.

The skin only needs to exist; do not add it to the spine's `skins` list unless you also want it
selectable as a look.

## Hit timings

The game's combat code drives an attack by three named events inside the animation. It does not
look at the animation's length or its frames; it waits for the events. Every vanilla attack has
them, and any animation you make for a hit should carry them too. Copy them from a vanilla attack
in Spine, then move them to fit your swing.

| Event name | What happens when it fires |
| --- | --- |
| `Attack Deal Damage` | The hit lands: the swipe circle is spawned in front of the player for a tenth of a second and damages everything it overlaps once. The attack sound plays and the camera shakes. |
| `Attack Can Break` | The hit may now be interrupted: a queued attack starts the next hit immediately, a dodge cancels the swing, the player can move. Before this event the player is locked in the swing. |
| `Attack Has Finished` | The swing is over. The player returns to idle if nothing was queued. |
| `Update Angle` | Optional. The moment the facing is committed: the player turns toward the stick or mouse until this fires, then the swing keeps that direction. Vanilla puts it on or just before "Attack Deal Damage". |

So a hit has three phases:

1. **Wind-up**, from the start to `Attack Deal Damage`. Nothing can be hit yet. This is the part
   that makes a weapon feel heavy or quick.
2. **Follow-through**, from `Attack Deal Damage` to `Attack Can Break`. The hit has landed but the
   player is still committed. Pressing attack here queues the next hit.
3. **Recovery**, from `Attack Can Break` to `Attack Has Finished`. The player may cancel into the
   next hit, a dodge or a move. If nothing is pressed the animation plays out.

The combo counter goes back to hit 1 when 0.2 seconds pass without an attack after a hit finished.

For reference, the vanilla timings, in seconds from the start of the animation:

| Animation | Update Angle | Deal Damage | Can Break | Finished |
| --- | --- | --- | --- | --- |
| Sword hit 1 (`attack-combo1`) | 0.03 | 0.03 | 0.27 | 0.60 |
| Dagger hit 1 (`attack-combo1-dagger`) | 0.03 | 0.03 | 0.20 | 0.53 |
| Axe hit 1 (`attack-combo1-axe`) | 0.17 | 0.20 | 0.63 | 0.83 |
| Axe hit 3 (`attack-combo3-axe`) | 0.20 | 0.30 | 0.63 | 0.97 |
| Hammer hit 1 (`attack-combo1-hammer`) | 0.40 | 0.43 | 0.77 | 1.13 |

The sword and dagger hit almost at once and recover fast; the axe and hammer wind up. A weapon that
wants to feel heavy needs a later "Attack Deal Damage", not just more damage.

**Speed and the events.** `speed` scales the whole animation, events included. Hit 1 of the sword
at `speed: 2` deals damage at 0.017 s and can be broken at 0.13 s. This is how vanilla's weapon
level and the attack-speed tarot cards work too, so a fast weapon is a fast animation, not a short
one.

**If an animation has none of the three events**, the mod adds them when the spine loads, so a hit
can never lock the player in place: "Update Angle" and "Attack Deal Damage" at `hitAt` (default
35% of the animation), "Attack Can Break" at `breakAt` (default 65%), and "Attack Has Finished" at
the end. The log names the animation and the times it used. This only happens when the animation
has none of them; an animation with one or two is assumed to be deliberate and left alone, so make
sure a hand-placed set is complete: without "Attack Can Break" and "Attack Has Finished" the player
stays locked in the swing.

**The pickup animation** has no required events. It is played once and its length is what holds the
player in place, so a long pickup animation is a long pause.

## Damage and speed

Every hit's damage is its `damage` multiplied by the run's bonuses: the weapon's level (vanilla adds
13% plus 7% per level), the fleece, tarot cards, relics, and the difficulty setting. So `damage`
is the number to compare against vanilla, not the number that appears on enemies. Vanilla swords
deal about 1 per light hit, axes and hammers more, daggers less. The pickup card shows the average
of the hits through the same formula, and calls the weapon faster or slower than the held one by
the average `speed` of its hits.

Heavy attacks are not part of the config. They keep the base weapon's animation, damage and fervour
cost, and their animations are looked up by family, so a Sword-based weapon always has the sword's
heavy attack.

## Loading and preloading

The weapon's artwork lives in your spine file, which is large and only loaded when needed. At boot
the spine loads only if a player is wearing it. Otherwise it starts loading the moment one of its
weapons appears in a room, when a podium rolls it, a chest offers it or an enemy drops it, in the
background while the player walks over. A player who reaches it before the load finishes holds it
with the base weapon's look until the artwork lands, usually a few seconds. Nothing pauses.

Set `preloadWeapons: true` at the top level of the spine config to load the spine at boot instead,
whether or not anyone wears it. The weapon then always has its artwork from the first room. The
cost is a longer start and a few hundred megabytes of memory per spine, for as long as the game
runs, so leave it off for spines whose weapons are rarely rolled.

## Testing a weapon

Custom weapons join the weapon pool, so weapon podiums and chests can roll them, but the roll is
random. To get one on purpose:

1. Enter a dungeon and press F4 to open the map editor.
2. Pick the Podium tool. Under the podium type there is a **Weapon** list: "Any" rolls from the
   pool as usual, the vanilla weapons pin the podium to that weapon, and every custom weapon is
   listed by its `<spine>/<name>` key. Picking a weapon sets the podium type to Weapon for you.
3. Place one and close the editor. That podium always offers that weapon.

Podiums placed this way are saved with the room like any other, so a test room can be kept.

The log (`BepInEx/LogOutput.log`) reports each weapon as it registers, with the id it was given,
and warns about a skin or animation the spine does not have, an icon that could not be read, or a
hit whose events were added. Search for the weapon's name.

**Seeing the hit box.** In the mod's config file (`BepInEx/config/InfernoDragon0.cotl.CustomSpineLoader.cfg`)
set `WeaponHitboxes` under `[Debug]` to `true`. Every attack then draws the outline of its hit box
where it lands, with a line from the attacker to its centre, fading out over most of a second: cyan
for a custom weapon's light hit, amber for any other attack of the player's (vanilla weapons, heavy
attacks), red for enemies. Swing a vanilla sword and your weapon side by side to compare `range`
and `hitboxRadius`; the value in the config is in the same units as the drawing. Chain hooks are
drawn too, following the hook while it is out: its circle and the chain's box, dimmed while they
cannot hurt. Leave it off for play.

## Chain-based weapons

The Chain (the hook-and-chain from the Unholy Alliance DLC) is the one weapon whose swing is not in
the Spine animation. The skeleton only plays the body pose; the chain and hook are a separate
object that the game moves along a scripted arc per hit, drawn as a line from the player's hand to
the hook, and the hit is the hook's own collider, not a swipe circle. So a Chain-based custom
weapon changes less than the others:

- `damage`, `speed`, `range` (the hook's collider radius), `knockback`, `cameraShake` and the
  body `animation` work as for any hit. `hitboxRadius` does not apply, because the hook never goes
  through the swipe path; `range` is the hook's circle.
- The hook's move and the shape of its sweep are set per hit with a `hook` block:

  ```json
  { "animation": "attack-chain-combo1", "damage": 1.0,
    "hook": { "pattern": "left", "startAngle": -90, "sweep": 360,
              "width": 2.5, "height": 1.5, "offset": 0, "duration": 0.45 } }
  ```

  `pattern` picks the move: `left` sweeps one hook around the player's left side, `right` the
  other hook around the right, `slam` throws both hooks out and slams them down (the chain's third
  hit, auto-aimed at the nearest enemy), `fallback` is the game's untuned quick sweep, only
  available on hit four or later. Without a pattern the chain's own order applies: left, right,
  slam, then fallback for every hit after the third.

  A sweep is an ellipse around the player, in the plane of the screen. `startAngle` is where on
  the ellipse the hook starts, in degrees from the direction the player faces; `sweep` is how far
  it travels, in degrees, negative the other way round, `360` a full circle; `width` and `height`
  are the ellipse's horizontal and vertical radii in world units; `offset` moves the ellipse's
  centre sideways from the player, to the left for `left` and the right for `right`, `0` centres
  it; `duration` is the sweep's time in seconds at normal attack speed; `colliderOn` and
  `colliderOff` are seconds after the start before the hook starts hurting and when it stops (the
  chain's own thrusts stop at 0.28 s; when you set a `duration` and no `colliderOff`, the hook
  stays live for the whole sweep). Any
  value left out is the chain's own for that move: a hit with a pattern starts as a copy of the
  chain's hit for that move, including its swing effect (the white arc), rather than the chain's
  hit at the same position. The log prints the chain's own numbers for its three hits the first
  time a Chain-based weapon is built, so there is a reference to start from.
  The `slam` pattern reads the same numbers but flies the hooks along the facing direction rather
  than around the player.

  The hook appears at the start of its path rather than travelling out from the hand, so a thrust
  is made with `radiusCurve`: a list of `[time, multiplier]` pairs, time from 0 to 1 across the
  hit, that scales the ellipse's radius as the hit plays. A stab is a sweep of `0` (the chain is
  drawn straight) with `"radiusCurve": [[0, 0], [0.4, 1], [1, 0.2]]`: the hook shoots out to full
  reach by 40% of the hit and pulls most of the way back. `scaleCurve` has the same shape and
  scales the hook picture over the hit, for a wind-up or a flourish. Both default to the chain's
  own curves.

  There are two chains. The game swings one on a sweep and both only on the slam; `hooks: 2` on a
  `left` or `right` hit swings both, the second on the mirrored side of the player and half a turn
  behind the first, which on a full circle puts a head on each end of the spin. `secondStartAngle`
  changes that half turn (`180` by default, `0` puts them together), and `secondDelay` holds the
  second chain back by that many seconds, so the two go out one after the other rather than
  together. The second chain rolls its own critical hit and carries the same damage, reach and
  curves as the first, and takes the direction the player faces at the moment it leaves. Two chains
  are all there are, so a hit that swings both cuts short whatever the previous hit still had in
  the air.

  With the hit box gizmo on (see [Testing a weapon](#testing-a-weapon)) the hook's circle and the
  chain's box are drawn while the hook is out, dimmed while the game has them switched off.
- The chain held in the hand between attacks is part of the skeleton (`Weapons/Chain_Long` and
  `Weapons/GrappleHook` slots), so your skin can restyle it like any weapon.
- The picture at the end of the swinging chain is **not** in the skeleton. Set `hookIcon` to a PNG
  in the spine folder to replace it, an axe head for a flail for instance. Whatever its pixel size,
  it is scaled so its larger side matches the chain's own hook; `hookScale` makes it bigger or
  smaller than that (`1.5` is half again as large). Without `hookIcon` the chain's usual hook is
  used. The heavy attack keeps the game's heavy hook. When there is no `icon`, the hook picture is
  also the weapon's icon on the podium and the HUD.

## Limits

- The hit box is always a circle in front of the player, sized by `range` and `hitboxRadius`. The
  artwork never collides, so a very long or oddly shaped weapon still hits in a circle.
- Heavy attacks, blocking (Shield) and the chain's hook are the base weapon's and cannot be changed.
- A weapon's id comes from its spine folder name and its `name`. Renaming either makes it a new
  weapon; the old one lingers in saves as an unknown id that is skipped by rolls and, if held,
  replaced by the sword.
- In multiplayer both players need the spine installed for the weapon to look right on both
  screens. Only the host's worn skin is shipped to the guest, so a weapon spine the other player
  lacks shows there as the base weapon.
