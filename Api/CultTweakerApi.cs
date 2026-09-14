using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.APIHelper.NpcQuests;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.MapEditor.WorldMap;
using CustomSpineLoader.SpineLoaderHelper;
using UnityEngine;

namespace CustomSpineLoader.Api;

public static class CultTweakerApi
{
    /// Raised by one on every addition. A caller written against 1 keeps working on 2.
    /// 2 added the quest kind and the quest members at the end of this class.
    /// 3 added the world map progress members at the end of this class.
    public const int ContractVersion = 3;

    public const string PluginGuid = Plugin.PluginGuid;

    /// The mod's version string, for logging and compatibility notes.
    public static string Version => Plugin.PluginVer;

    /// True once the boot loaders have finished. Content registries are empty before this, so a
    /// mod that reads them from its own Awake may see nothing; use <see cref="OnReady"/>.
    public static bool Ready { get; private set; }

    private static readonly List<Action> Waiting = [];

    /// Runs the callback once content is loaded: immediately when it already is, otherwise at the
    /// end of our boot. Exceptions in a callback are logged, never rethrown into the caller.
    public static void OnReady(Action callback)
    {
        if (callback == null) return;
        if (Ready)
        {
            Invoke(callback);
            return;
        }

        Waiting.Add(callback);
    }

    internal static void MarkReady()
    {
        if (Ready) return;
        Ready = true;

        var callbacks = Waiting.ToArray();
        Waiting.Clear();
        foreach (var callback in callbacks) Invoke(callback);
    }

    private static void Invoke(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Api: a ready callback threw: " + e.Message);
        }
    }

    // ---- kinds ---------------------------------------------------------------------------------

    /// The content kinds this contract knows. Pass one to <see cref="Names"/> and friends.
    public static class Kind
    {
        /// Playable custom dungeons, registered at runtime. Names are internal names.
        public const string Dungeons = "dungeons";

        /// Dungeon map documents (the node graph a dungeon plays through).
        public const string DungeonMaps = "dungeonMaps";

        /// Level documents (an ordered set of rooms).
        public const string Levels = "levels";

        /// Room blueprint documents.
        public const string Rooms = "rooms";

        /// World map documents.
        public const string WorldMaps = "worldMaps";

        /// Main menu preset documents.
        public const string MainMenus = "mainMenus";

        /// Registered custom NPCs.
        public const string Npcs = "npcs";

        /// Registered custom enemies.
        public const string Enemies = "enemies";

        /// Registered custom structures.
        public const string Structures = "structures";

        /// Registered custom inventory items.
        public const string Items = "items";

        /// Registered custom meals.
        public const string Meals = "meals";

        /// Registered custom tarot cards.
        public const string Tarots = "tarots";

        /// Custom weapons declared by player spines. Names are "spineFolder/weaponName".
        public const string Weapons = "weapons";

        /// Player spines available to wear. Names are "spineFolder/skinName".
        public const string PlayerSkins = "playerSkins";

        /// Custom follower skins by skin name.
        public const string FollowerSkins = "followerSkins";

        /// Follower hats donated by wardrobe packs. Names are "packFolder/skinName".
        public const string FollowerHats = "followerHats";

        /// Follower clothes donated by wardrobe packs. Names are "packFolder/skinName".
        public const string FollowerClothes = "followerClothes";

        /// Cutscene videos available to play.
        public const string Cutscenes = "cutscenes";

        /// Custom sprite shape profiles usable by the shape tool.
        public const string ShapeProfiles = "shapeProfiles";

        /// Buildings whose artwork is overridden, by building name.
        public const string BuildingOverrides = "buildingOverrides";

        /// Quests declared by custom NPCs. Names are "npcInternalName/questId".
        public const string Quests = "quests";
    }

    private static readonly string[] AllKinds =
    [
        Kind.Dungeons, Kind.DungeonMaps, Kind.Levels, Kind.Rooms, Kind.WorldMaps, Kind.MainMenus,
        Kind.Npcs, Kind.Enemies, Kind.Structures, Kind.Items, Kind.Meals, Kind.Tarots,
        Kind.Weapons, Kind.PlayerSkins, Kind.FollowerSkins, Kind.FollowerHats, Kind.FollowerClothes,
        Kind.Cutscenes, Kind.ShapeProfiles,
        Kind.BuildingOverrides, Kind.Quests
    ];

    /// Every kind this build knows, so a caller can iterate without hard-coding the list.
    public static IReadOnlyList<string> Kinds() => AllKinds;

    /// The content folder a kind is read from, or null for a kind with no folder of its own.
    /// Combine with <see cref="ContentRoot"/> or ship files under your own CultTweaker folder.
    public static string FolderFor(string kind)
    {
        switch (Normalise(kind))
        {
            case "dungeonmaps": return CTDungeonMapSerialization.FolderName;
            case "levels": return CTLevelSerialization.FolderName;
            case "rooms": return MapEditorSerialization.FolderName;
            case "worldmaps": return CTWorldMapSerialization.FolderName;
            case "mainmenus": return CTMenuPresetSerialization.FolderName;
            case "npcs": return "CustomNpcs";
            case "enemies": return "CustomEnemies";
            case "structures": return "CustomStructures";
            case "items": return "CustomInventoryItems";
            case "meals": return "CustomMeals";
            case "tarots": return "CustomTarotCards";
            case "weapons":
            case "playerskins": return "PlayerSkins";
            case "followerskins": return "FollowerSkins";
            case "followerhats":
            case "followerclothes": return FollowerWardrobe.FolderName;
            case "cutscenes": return APIHelper.CustomCutsceneLoader.FolderName;
            case "shapeprofiles": return "CustomShapeProfiles";
            case "buildingoverrides": return "BuildingOverrides";
            case "quests": return "CustomNpcs";
            default: return null;
        }
    }

    private static string Normalise(string kind) => (kind ?? "").Trim().ToLowerInvariant();

    // ---- queries -------------------------------------------------------------------------------

    /// What this install has of a kind. Registered kinds answer with what actually loaded;
    /// document kinds answer with the file names found, ours and every other mod's.
    public static IReadOnlyList<string> Names(string kind)
    {
        try
        {
            switch (Normalise(kind))
            {
                case "dungeons": return DungeonNames();
                case "dungeonmaps": return DocumentNames(CTDungeonMapSerialization.FolderName);
                case "levels": return DocumentNames(CTLevelSerialization.FolderName);
                case "rooms": return MapEditorSerialization.SavedNames();
                case "worldmaps": return CTWorldMapSerialization.ListNames();
                case "mainmenus": return CTMenuPresetSerialization.ListNames();
                case "npcs": return [.. CustomNpcManager.CustomNpcList.Keys];
                case "enemies":
                case "structures":
                case "items":
                case "meals":
                case "tarots": return ContentIds.Names(Normalise(kind));
                case "weapons": return WeaponNames();
                case "playerskins": return PlayerSpineLoader.RegisteredSpineNames();
                case "followerskins": return [.. FollowerSpineLoader.CustomFollowerSkins.Keys];
                case "followerhats": return FollowerWardrobe.HatKeys();
                case "followerclothes": return FollowerWardrobe.ClothesKeys();
                case "cutscenes": return APIHelper.CustomCutsceneLoader.Names();
                case "shapeprofiles": return ShapeProfileNames();
                case "buildingoverrides":
                    return [.. StructureBuildingOverrideHelper.StructureBuildingOverrides.Keys];
                case "quests": return QuestKeys();
                default:
                    Plugin.Log.LogWarning($"Api: unknown content kind '{kind}'.");
                    return [];
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: listing '{kind}' failed: {e.Message}");
            return [];
        }
    }

    /// Whether this install has that one, by the same names <see cref="Names"/> returns.
    public static bool Has(string kind, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        foreach (var known in Names(kind))
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// The game-side id a registered thing was given, as an int, or int.MinValue when it has none.
    /// Item, meal, tarot, structure, enemy, weapon and dungeon ids exist for kinds backed by a game
    /// enum.
    ///
    /// These ids are allocated per install, in load order, so THE SAME CONTENT IS A DIFFERENT
    /// NUMBER ON ANOTHER MACHINE, and can change when a mod is added or removed. Use them for
    /// immediate calls into the game only. Never save one, and never send one to another machine:
    /// pass the name and resolve it there.
    /// </summary>
    public static int IdOf(string kind, string name)
    {
        if (string.IsNullOrEmpty(name)) return int.MinValue;

        try
        {
            switch (Normalise(kind))
            {
                case "dungeons":
                {
                    var dungeon = FindDungeon(name);
                    return dungeon != null ? (int)dungeon.Location : int.MinValue;
                }
                case "weapons":
                {
                    var weapon = CustomWeapons.ForKey(name);
                    return weapon != null ? (int)weapon.Type : int.MinValue;
                }
                case "enemies":
                case "structures":
                case "items":
                case "meals":
                case "tarots":
                    return ContentIds.Id(Normalise(kind), name);
                default:
                    return int.MinValue;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: id lookup for '{kind}/{name}' failed: {e.Message}");
            return int.MinValue;
        }
    }

    // ---- actions -------------------------------------------------------------------------------

    /// Starts a run in a custom dungeon, by internal name or by the name it shows in game.
    /// False when this install does not have it. The transition runs as if the player had entered
    /// it themselves, so call it from a normal gameplay moment, not mid-load.
    public static bool EnterDungeon(string name)
    {
        var dungeon = FindDungeon(name);
        if (dungeon == null)
        {
            Plugin.Log.LogWarning($"Api: no custom dungeon called '{name}' is registered here.");
            return false;
        }

        try
        {
            dungeon.EnterDungeon();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Api: entering dungeon '{name}' failed: {e}");
            return false;
        }
    }

    /// The custom dungeon the player is in, by internal name, or null when they are not in one.
    public static string CurrentDungeon()
    {
        try
        {
            var biome = MMBiomeGeneration.BiomeGenerator.Instance;
            var location = biome != null ? biome.DungeonLocation : PlayerFarming.Location;
            return CustomDungeonManager.CustomDungeonList.TryGetValue(location, out var dungeon)
                ? NameOf(dungeon)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Spawns a registered custom NPC in the current room. Null when this install does not have
    /// it, or when its prefab is not built yet.
    public static GameObject SpawnNpc(string name, Vector3 position, Transform parent = null)
    {
        if (string.IsNullOrEmpty(name)) return null;

        try
        {
            return CustomNpcManager.Spawn(name, position, parent);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Api: spawning NPC '{name}' failed: {e}");
            return null;
        }
    }

    // ---- quests (contract version 2) --------------------------------------------------------------

    /// <summary>
    /// Where a quest stands in the save the player has open: "notStarted", "active", "ready" (every
    /// goal met, waiting to be handed in), "done" or "failed". Null when this install has no such
    /// quest. Names come from <see cref="Names"/> with <see cref="Kind.Quests"/>.
    /// </summary>
    public static string QuestState(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            if (QuestRegistry.Find(key) == null) return null;

            return QuestRuntime.State(key) switch
            {
                QuestStatus.Active => "active",
                QuestStatus.Ready => "ready",
                QuestStatus.Done => "done",
                QuestStatus.Failed => "failed",
                _ => "notStarted"
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: reading quest '{key}' failed: {e.Message}");
            return null;
        }
    }

    /// Every quest under way in the save the player has open.
    public static IReadOnlyList<string> ActiveQuests()
    {
        try
        {
            return QuestRuntime.RunningKeys();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// Hands a quest to the player as its NPC would. False when it is already under way, already
    /// done and not repeatable, or unknown here.
    public static bool GiveQuest(string key) => QuestAction(key, QuestRuntime.Accept);

    /// Finishes a quest, paying out its reward. Only works once every goal is met.
    public static bool TurnInQuest(string key) => QuestAction(key, QuestRuntime.TurnIn);

    /// Gives a quest up. Its record goes away, so its NPC can offer it again.
    public static bool AbandonQuest(string key) => QuestAction(key, QuestRuntime.Abandon);

    /// <summary>
    /// Raises a named flag. Any quest goal of type "flag" watching for that name counts one step.
    /// This is the hook for a mod that wants a quest to turn on something the game itself never
    /// announces.
    /// </summary>
    public static void NoteQuestEvent(string flag)
    {
        try
        {
            QuestRuntime.NoteFlag(flag);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: raising quest flag '{flag}' failed: {e.Message}");
        }
    }

    private static bool QuestAction(string key, Func<QuestDefinition, bool> action)
    {
        if (string.IsNullOrEmpty(key)) return false;

        try
        {
            var quest = QuestRegistry.Find(key);
            if (quest == null)
            {
                Plugin.Log.LogWarning($"Api: no quest called '{key}' is registered here.");
                return false;
            }

            return action(quest);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Api: quest action on '{key}' failed: {e}");
            return false;
        }
    }

    private static List<string> QuestKeys()
    {
        var names = new List<string>();
        foreach (var quest in QuestRegistry.All)
            if (quest != null && !string.IsNullOrEmpty(quest.Key)) names.Add(quest.Key);

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // ---- world map progress ------------------------------------------------------------------

    /// <summary>
    /// The node ids of a world map, in the order the document lists them. Empty when this install
    /// has no such map. Ids are what every other world map member takes; they are the author's own
    /// strings and are stable across machines, unlike <see cref="IdOf"/> numbers.
    /// </summary>
    public static IReadOnlyList<string> WorldMapNodes(string mapName)
    {
        var map = WorldMap(mapName);
        if (map == null) return Array.Empty<string>();

        var ids = new List<string>(map.Nodes.Count);
        foreach (var node in map.Nodes)
            if (node != null && !string.IsNullOrEmpty(node.Id)) ids.Add(node.Id);

        return ids;
    }

    /// <summary>
    /// Where a node stands in the save the player has open: "hidden", "preview", "locked",
    /// "selectable" or "completed". Null when this install has no such map or node.
    /// <para>
    /// State is DERIVED, never stored: it is recomputed from the map's graph and the saved record
    /// every time. So there is nothing to assign - change the record with the members below and the
    /// state follows. Reading re-reads the map document from disk, so do not poll this per frame.
    /// </para>
    /// </summary>
    public static string WorldMapNodeState(string mapName, string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;

        try
        {
            var map = WorldMap(mapName);
            if (map == null || map.FindNode(nodeId) == null) return null;

            var states = WorldMapStateResolver.Resolve(map, WorldMapProgress.For(map.MapName));
            return states.TryGetValue(nodeId, out var state) ? state.ToString().ToLowerInvariant() : null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: reading world map '{mapName}' node '{nodeId}' failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Marks a node completed, as finishing a run from it would. A Key node also banks its keys,
    /// once however often it is redone. False when this install has no such map or node.
    /// </summary>
    public static bool CompleteWorldMapNode(string mapName, string nodeId)
    {
        var map = WorldMap(mapName);
        if (map == null || string.IsNullOrEmpty(nodeId) || map.FindNode(nodeId) == null) return false;

        return Guarded($"completing '{nodeId}'", () =>
        {
            WorldMapProgress.MarkCompleted(map, nodeId);
            return true;
        });
    }

    /// <summary>
    /// Takes a node back out of the completed set; anything downstream re-locks on its own, because
    /// state is derived. A Key node un-banks what it granted, floored at zero - keys already spent
    /// cannot be clawed back - and any lock those keys opened STAYS open. False when the node was
    /// not completed here.
    /// </summary>
    public static bool UncompleteWorldMapNode(string mapName, string nodeId)
    {
        var map = WorldMap(mapName);
        if (map == null || string.IsNullOrEmpty(nodeId)) return false;

        return Guarded($"un-completing '{nodeId}'", () => WorldMapProgress.Uncomplete(map, nodeId));
    }

    /// <summary>
    /// Opens a Lock node, spending its <c>KeysCost</c>. False when the node is not a lock, is
    /// already open, or the player cannot pay - use <see cref="SetWorldMapKeys"/> first to force it.
    /// </summary>
    public static bool OpenWorldMapLock(string mapName, string nodeId)
    {
        var node = WorldMapNode(mapName, nodeId, out var map);
        if (node == null || !node.IsLock) return false;

        return Guarded($"opening lock '{nodeId}'", () => WorldMapProgress.OpenLock(map, node));
    }

    /// Shuts an opened Lock node and refunds its cost. False when it was not open.
    public static bool CloseWorldMapLock(string mapName, string nodeId)
    {
        var node = WorldMapNode(mapName, nodeId, out var map);
        if (node == null || !node.IsLock) return false;

        return Guarded($"closing lock '{nodeId}'", () => WorldMapProgress.CloseLock(map, node));
    }

    /// Keys banked on this map in the save the player has open. Keys are per map, not shared.
    public static int WorldMapKeys(string mapName) =>
        string.IsNullOrEmpty(mapName) ? 0 : WorldMapProgress.For(mapName).KeysHeld;

    /// Sets the key count directly, floored at zero. Which Key nodes are banked is left alone, so
    /// this does not let a node grant its keys twice.
    public static void SetWorldMapKeys(string mapName, int keys)
    {
        if (string.IsNullOrEmpty(mapName)) return;
        Guarded($"setting keys on '{mapName}'", () => { WorldMapProgress.SetKeys(mapName, keys); return true; });
    }

    /// Throws away every completion, opened lock and key on this map for the save the player has
    /// open. The map document itself is untouched.
    public static void ResetWorldMap(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return;
        Guarded($"resetting '{mapName}'", () => { WorldMapProgress.WipeMap(mapName); return true; });
    }

    private static CTWorldMap WorldMap(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return null;

        try
        {
            return CTWorldMapSerialization.LoadByName(mapName);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Api: world map '{mapName}' could not be read: {e.Message}");
            return null;
        }
    }

    private static CTWorldMapNode WorldMapNode(string mapName, string nodeId, out CTWorldMap map)
    {
        map = WorldMap(mapName);
        return map == null || string.IsNullOrEmpty(nodeId) ? null : map.FindNode(nodeId);
    }

    private static bool Guarded(string what, Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Api: world map, {what} failed: {e}");
            return false;
        }
    }

    // ---- where content lives ---------------------------------------------------------------------

    /// <summary>
    /// Our own folder for a content folder name, which is where a player's own creations are saved.
    /// A mod ships content WITHOUT writing here: put it in a "CultTweaker" folder inside your own
    /// plugin folder, for example
    /// BepInEx/plugins/YourMod/CultTweaker/CustomDungeonMaps/YourMap.json,
    /// and it is found alongside ours. Never write into another mod's folder, ours included.
    /// </summary>
    public static string ContentRoot(string folderName) =>
        string.IsNullOrEmpty(folderName) ? null : ModContentPaths.OwnRoot(folderName);

    /// Every folder a content folder name is read from: ours first, then each mod that ships some.
    public static IReadOnlyList<string> ContentRoots(string folderName) =>
        string.IsNullOrEmpty(folderName) ? [] : ModContentPaths.RootsFor(folderName);

    /// Every per-item folder found for a content folder name, across all mods, ours winning on a
    /// name clash. Absolute paths.
    public static IReadOnlyList<string> ContentDirectories(string folderName) =>
        string.IsNullOrEmpty(folderName) ? [] : ModContentPaths.DirectoriesIn(folderName);

    /// Every file matching the pattern in a content folder, across all mods. Absolute paths.
    public static IReadOnlyList<string> ContentFiles(string folderName, string pattern) =>
        string.IsNullOrEmpty(folderName) ? [] : ModContentPaths.FilesIn(folderName, pattern ?? "*");

    /// One file by name across all mods' copies of a content folder, or null.
    public static string FindContentFile(string folderName, string fileName) =>
        string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName)
            ? null
            : ModContentPaths.FindFile(folderName, fileName);

    /// One per-item folder by name across all mods' copies of a content folder, or null.
    public static string FindContentDirectory(string folderName, string subFolder) =>
        string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(subFolder)
            ? null
            : ModContentPaths.FindDirectory(folderName, subFolder);

    // ---- internals -----------------------------------------------------------------------------

    private static List<string> DocumentNames(string folderName)
    {
        var names = new List<string>();
        foreach (var file in ModContentPaths.FilesIn(folderName, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<string> DungeonNames()
    {
        var names = new List<string>();
        foreach (var dungeon in CustomDungeonManager.CustomDungeonList.Values)
        {
            var name = NameOf(dungeon);
            if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static string NameOf(CustomDungeon dungeon)
    {
        if (dungeon == null) return null;
        return !string.IsNullOrEmpty(dungeon.InternalName) ? dungeon.InternalName : dungeon.DungeonName;
    }

    private static CustomDungeon FindDungeon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var dungeon in CustomDungeonManager.CustomDungeonList.Values)
        {
            if (dungeon == null) continue;
            if (string.Equals(dungeon.InternalName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dungeon.DungeonName, name, StringComparison.OrdinalIgnoreCase))
                return dungeon;
        }

        return null;
    }

    private static List<string> WeaponNames()
    {
        var names = new List<string>();
        foreach (var weapon in CustomWeapons.All)
            if (weapon != null && !string.IsNullOrEmpty(weapon.Key)) names.Add(weapon.Key);

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<string> ShapeProfileNames()
    {
        var names = new List<string>();
        foreach (var shape in CustomShapeProfiles.All)
            if (shape != null && !string.IsNullOrEmpty(shape.name)) names.Add(shape.name);

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// Names and game ids of the registered kinds, filled by the loaders as they register. The
    /// game's own registries keep the id but not the name we knew it by, so this is the only way
    /// back from one to the other.
    /// </summary>
    internal static class ContentIds
    {
        private static readonly Dictionary<string, Dictionary<string, int>> ByKind =
            new(StringComparer.OrdinalIgnoreCase);

        internal static void Note(string kind, string name, int id)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(name)) return;

            if (!ByKind.TryGetValue(kind, out var names))
            {
                names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ByKind[kind] = names;
            }

            names[name] = id;
        }

        internal static IReadOnlyList<string> Names(string kind)
        {
            if (!ByKind.TryGetValue(kind, out var names)) return [];

            var result = new List<string>(names.Keys);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        internal static int Id(string kind, string name) =>
            ByKind.TryGetValue(kind, out var names) && names.TryGetValue(name, out var id)
                ? id
                : int.MinValue;
    }

    /// Called by the loaders. Not part of the contract.
    internal static void NoteContent(string kind, string name, int id) => ContentIds.Note(kind, name, id);
}
