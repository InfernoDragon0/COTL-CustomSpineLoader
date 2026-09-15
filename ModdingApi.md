# CultTweaker for other mods

Two ways to build on CultTweaker: ship content as files and write no code at all, or reference the
assembly and call a small contract. Most mods want the first.

- [Shipping content with no code](#shipping-content-with-no-code)
- [The code contract](#the-code-contract)
- [Reaching it safely](#reaching-it-safely)
- [Content kinds](#content-kinds)
- [Queries](#queries)
- [Actions](#actions)
- [Quests (contract version 2)](#quests-contract-version-2)
- [World map progress (contract version 3)](#world-map-progress-contract-version-3)
- [Follower looks (contract version 4)](#follower-looks-contract-version-4)
- [Where content lives](#where-content-lives)
- [Ids and why you must not save them](#ids-and-why-you-must-not-save-them)
- [Load order](#load-order)
- [Versioning](#versioning)

## Shipping content with no code

Every content folder is read from two places: CultTweaker's own plugin folder, and a folder named
`CultTweaker` inside any other mod's folder. Put your files there and they load beside the
player's own, with no reference to our assembly and no code from you:

```
BepInEx/plugins/YourMod/CultTweaker/CustomDungeonMaps/YourDungeon.json
BepInEx/plugins/YourMod/CultTweaker/CustomLevelBlueprints/YourLevel.json
BepInEx/plugins/YourMod/CultTweaker/CustomNodeBlueprints/YourRoom.json
BepInEx/plugins/YourMod/CultTweaker/CustomNpcs/YourNpc/config.json
BepInEx/plugins/YourMod/CultTweaker/CustomEnemies/YourEnemy/config.json
BepInEx/plugins/YourMod/CultTweaker/PlayerSkins/YourSkin/config.json
```

The search is a bounded walk three folders deep, so a nested install is still found. Three rules
apply: reading is shared but writing is not, so your files are never edited in place; where two
mods use the same name the player's own copy wins and the other is skipped with a warning; and
everything the editors save goes to CultTweaker's own folder, never into yours.

The folder names are in [Content kinds](#content-kinds).

## The code contract

One class: `CustomSpineLoader.Api.CultTweakerApi`, in `CultTweaker.dll`. Everything else in the
assembly is internal in spirit and gets rearranged without notice. Members here are never removed
or changed in meaning; new ones are added and `ContractVersion` is raised.

Reference the DLL with `Private="false"` so you do not ship a copy of it:

```xml
<Reference Include="CultTweaker">
  <HintPath>lib\CultTweaker.dll</HintPath>
  <Private>false</Private>
</Reference>
```

## Reaching it safely

Declare a soft dependency, probe before the first call, and keep every call to our types inside a
method marked no-inlining so the JIT never loads them when CultTweaker is absent:

```csharp
[BepInDependency("InfernoDragon0.cotl.CustomSpineLoader", BepInDependency.DependencyFlags.SoftDependency)]
public class YourPlugin : BaseUnityPlugin
{
    private static bool _available;

    private void Awake()
    {
        _available = Chainloader.PluginInfos.ContainsKey("InfernoDragon0.cotl.CustomSpineLoader")
                     && Probe();
        if (_available) Bridge.WhenReady();
    }

    private static bool Probe()
    {
        try
        {
            var type = Type.GetType("CustomSpineLoader.Api.CultTweakerApi, CultTweaker");
            return type != null && CultTweakerApi.ContractVersion >= 1;
        }
        catch (Exception) { return false; }
    }
}

internal static class Bridge
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WhenReady() => CultTweakerApi.OnReady(() =>
    {
        foreach (var name in CultTweakerApi.Names(CultTweakerApi.Kind.Dungeons))
            Log.LogInfo("CultTweaker dungeon available: " + name);
    });
}
```

Nothing throws out of the contract. A query for a kind this build does not know logs a warning and
returns an empty list.

## Content kinds

Pass one of these to the queries. The constants are on `CultTweakerApi.Kind`.

| Kind | What it lists | Folder |
| --- | --- | --- |
| `Dungeons` | Playable custom dungeons, by internal name | none, registered at runtime |
| `DungeonMaps` | Dungeon map documents | `CustomDungeonMaps` |
| `Levels` | Level documents | `CustomLevelBlueprints` |
| `Rooms` | Room blueprint documents | `CustomNodeBlueprints` |
| `WorldMaps` | World map documents | `CustomWorldMaps` |
| `MainMenus` | Main menu presets | `CustomMainMenus` |
| `Npcs` | Registered custom NPCs | `CustomNpcs` |
| `Enemies` | Registered custom enemies | `CustomEnemies` |
| `Structures` | Registered custom structures | `CustomStructures` |
| `Items` | Registered inventory items | `CustomInventoryItems` |
| `Meals` | Registered meals | `CustomMeals` |
| `Tarots` | Registered tarot cards | `CustomTarotCards` |
| `Weapons` | Custom weapons, as `spineFolder/weaponName` | `PlayerSkins` |
| `PlayerSkins` | Player spines, as `spineFolder/skinName` | `PlayerSkins` |
| `FollowerSkins` | Custom follower skins | `FollowerSkins` |
| `FollowerHats` | Follower hats from wardrobe packs, as `packFolder/skinName` | `FollowerSpines` |
| `FollowerClothes` | Follower clothes from wardrobe packs, as `packFolder/skinName` | `FollowerSpines` |
| `Cutscenes` | Cutscene videos | `CustomCutscenes` |
| `ShapeProfiles` | Sprite shape profiles | `CustomShapeProfiles` |
| `BuildingOverrides` | Buildings with overridden art | `BuildingOverrides` |
| `Quests` | Quests declared by custom NPCs, as `npcInternalName/questId` | `CustomNpcs` |

`CultTweakerApi.Kinds()` returns the whole list, so you can iterate without hard-coding it, and
`FolderFor(kind)` gives the folder name, or null for a kind with no folder.

## Queries

```csharp
IReadOnlyList<string> Names(string kind);   // what this install has
bool                  Has(string kind, string name);
int                   IdOf(string kind, string name);   // see the warning below
string                FolderFor(string kind);
IReadOnlyList<string> Kinds();
bool                  Ready { get; }
string                Version { get; }
void                  OnReady(Action callback);
```

Registered kinds answer with what actually loaded. Document kinds answer with the file names found
across every mod's folders, including yours.

## Actions

```csharp
bool       EnterDungeon(string name);   // by internal name or in-game name
string     CurrentDungeon();            // internal name, or null
GameObject SpawnNpc(string name, Vector3 position, Transform parent = null);
```

`EnterDungeon` starts a run the way the player entering it would, so call it from a normal
gameplay moment rather than during a load. It returns false when this install does not have that
dungeon.

## Quests (contract version 2)

```csharp
string                QuestState(string key);   // notStarted, active, ready, done, failed, or null
IReadOnlyList<string> ActiveQuests();
bool                  GiveQuest(string key);
bool                  TurnInQuest(string key);
bool                  AbandonQuest(string key);
void                  NoteQuestEvent(string flag);
```

Keys are the names `Names(Kind.Quests)` returns: `npcInternalName/questId`. `ready` means every goal
is met and the player has not handed the quest in yet.

`NoteQuestEvent` is the hook for a quest that has to wait on something the game never announces:
declare a goal of type `flag` with a name of your choosing, and raise that name when your own mod
decides the moment has come. Quests, their goals and their text are written by whoever makes the NPC
— see [CustomNpcQuests.md](CustomNpcQuests.md) — so a mod can ship a quest with no code at all and
only reach for these calls when it needs to drive one.

Quest progress is CultTweaker's own per-slot file, not the game's save, and it is keyed by name
throughout, so none of this carries the id warning below.

## World map progress (contract version 3)

```csharp
IReadOnlyList<string> WorldMapNodes(string mapName);
string                WorldMapNodeState(string mapName, string nodeId);
bool                  CompleteWorldMapNode(string mapName, string nodeId);
bool                  UncompleteWorldMapNode(string mapName, string nodeId);
bool                  OpenWorldMapLock(string mapName, string nodeId);
bool                  CloseWorldMapLock(string mapName, string nodeId);
int                   WorldMapKeys(string mapName);
void                  SetWorldMapKeys(string mapName, int keys);
void                  ResetWorldMap(string mapName);
```

Map names are what `Names(Kind.WorldMaps)` returns; node ids come from `WorldMapNodes` and are the
author's own strings, stable across machines.

**State is derived, never assigned.** `WorldMapNodeState` returns `hidden`, `preview`, `locked`,
`selectable` or `completed`, recomputed each time from the map's graph — `InitialState`, the
`Children` cascade, `RequiredNodes` with `RequiredCompletedCount`, and the lock nodes — combined
with the saved record. So there is no "unlock this node" call and there cannot be one. You change
the record, and which nodes are open follows. Reading re-reads the map document from disk, so query
it when something happens, not every frame.

Keys are a per-map currency: a **Key** node banks `KeysGranted` when completed (once, however often
it is replayed), a **Lock** node costs `KeysCost` and blocks its branch until opened. Keys earned on
one map cannot be spent on another. `OpenWorldMapLock` refuses when the player cannot pay — call
`SetWorldMapKeys` first if you mean to force it.

Two asymmetries worth knowing before you rely on them:

- `UncompleteWorldMapNode` on a **Key** node un-banks what it granted, floored at zero, and any lock
  those keys already opened **stays open**. Shutting a door the player paid for would reach well
  beyond the node you named. So un-completing is not always an exact inverse of completing;
  `ResetWorldMap` is, for the whole map.
- You can drive a map into a state its own rules would never produce — a node completed while its
  prerequisites are not, or a lock closed with completed nodes past it. Nothing breaks and nothing
  becomes unreachable, because state is recomputed from scratch; it will simply look odd. Prefer
  driving the record the way play would.

Progress is CultTweaker's own per-save-slot file, not the game's save, and is keyed by name
throughout, so none of this carries the id warning below.

## Follower looks (contract version 4)

```csharp
IReadOnlyList<int> FollowerLookIds();
string             FollowerLook(int followerId);
bool               ApplyFollowerLook(int followerId, string look);
void               MirrorFollowerLooks(bool on);
void               ReloadFollowerWardrobe();
```

A follower's **look** is everything the Customize Follower command gives it: a custom colour and
scale, a costume override, and a hat and clothes from a wardrobe pack. The game's save does not
carry it; CultTweaker keeps it in its own per-save-slot file, keyed by the game's follower id. These
members exist so a multiplayer mod can carry that record to the other machine.

`FollowerLook` returns one opaque string per follower and `ApplyFollowerLook` takes it back on the
other side, redressing the follower at once if it is in the world. Treat the string as bytes: do not
parse it, and do not build one. Everything inside is a name, so the id warning below does not
apply, and the follower id itself is the game's own, which the two machines share once the guest
has the host's save.

`MirrorFollowerLooks(true)` is for the guest: it sets that machine's own records aside, so looks
from the guest's save cannot land on the host's followers, and stops the file being written until
`MirrorFollowerLooks(false)` puts them back. Call it before the first `ApplyFollowerLook` and again
when the session ends.

A hat or clothes names a wardrobe pack by folder name. If the other machine lacks that pack, that
part of the look is skipped with a single log line and the rest applies. `ReloadFollowerWardrobe`
reads the `FollowerSpines` folders again, for a pack that was copied over after boot.

## Where content lives

```csharp
string                ContentRoot(string folderName);        // CultTweaker's own folder
IReadOnlyList<string> ContentRoots(string folderName);       // ours, then every mod's
IReadOnlyList<string> ContentDirectories(string folderName); // per-item folders, absolute
IReadOnlyList<string> ContentFiles(string folderName, string pattern);
string                FindContentFile(string folderName, string fileName);
string                FindContentDirectory(string folderName, string subFolder);
```

Use these to find your own shipped content on disk, or to read a document a player made. Do not
write into `ContentRoot`: that folder holds the player's own creations. Ship your files inside your
own plugin folder as shown at the top.

## Ids and why you must not save them

`IdOf` returns the number the game gave a piece of content, for the kinds backed by a game enum:
items, meals, tarots, structures, enemies, weapons and dungeons.

**These numbers are allocated per install, in load order.** The same dungeon is a different number
on another machine, and adding or removing any mod can renumber everything. Use an id for an
immediate call into the game and nothing else. Never write one to a save file and never send one to
another machine: send the name and call `IdOf` again on the other side.

This is not theoretical. A multiplayer mod sent a custom dungeon's raw id to the other player, whose
install had a different number for it, and the guest sat on a black loading screen because the
dungeon it was told to load did not exist there.

## Load order

CultTweaker registers its content during its own `Awake`, and BepInEx does not promise plugin
order. A mod that reads the registries from its own `Awake` may see nothing. Either read them
lazily, when a scene loads or when the player does something, or pass a callback to
`CultTweakerApi.OnReady`, which runs immediately if content is already loaded and at the end of our
boot otherwise.

## Versioning

`CultTweakerApi.ContractVersion` started at 1 and goes up by one whenever members are added. Members
are never removed and never change meaning, so code written against 1 keeps working against 2.
Check it once at startup if you need a member added later:

```csharp
if (CultTweakerApi.ContractVersion >= 2) { /* the quest members */ }
```

| Version | Added |
| --- | --- |
| 1 | The kinds, the queries, the actions, the content paths, `OnReady`. |
| 2 | `Kind.Quests` and the quest members. |
| 3 | The world map progress members. |
| 4 | The follower look members. |

If you need something the contract does not expose, ask rather than reflecting into the assembly:
anything reached by reflection will break the next time those internals move.
