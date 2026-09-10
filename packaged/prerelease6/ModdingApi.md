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

If you need something the contract does not expose, ask rather than reflecting into the assembly:
anything reached by reflection will break the next time those internals move.
