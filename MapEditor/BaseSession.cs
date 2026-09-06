using MMRoomGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomSpineLoader.MapEditor;

public static class BaseSession
{
    public const string BaseScene = "Base Biome 1";

    public static bool Active { get; private set; }

    public static bool SceneOwnsSessions =>
        SceneManager.GetActiveScene().name == BaseScene;

    // ---- entry ----------------------------------------------------------------------------------

    public static string Enter()
    {
        if (Active) return "The base editor is already open.";

        var blocked = WhyNot();
        if (blocked != null) return blocked;

        Active = true;

        ClaimRoom();
        BaseDelta.EnsureContentRoot();
        BaseGround.RememberVanillaGround();

        var editor = RuntimeMapEditor.Ensure("RuntimeMapEditorHost_Base");
        if (editor == null)
        {
            Active = false;
            return "No map editor could be created here.";
        }

        editor.BeginBaseEditing();
        Plugin.Log.LogInfo($"Base editor: open on save slot {BaseDelta.Slot}.");
        return null;
    }

    public static string WhyNot()
    {
        if (SceneManager.GetActiveScene().name != BaseScene)
            return "The base editor opens in the base, not here.";

        if (PlayerFarming.Instance == null) return "The base editor opens in game, not on the menu.";
        if (BiomeBaseManager.Instance == null) return "The base has not finished loading.";

        if (HubSession.Busy) return "A hub is open here; leave it first.";

        if (PlayerFarming.Location != FollowerLocation.Base)
            return "The player is not in the base right now.";

        if (BaseRoom() == null) return "The base room is not standing here.";

        var town = BiomeBaseManager.Instance.DLC_ShrineRoom;
        if (town != null && town.gameObject.activeInHierarchy)
            return "The town room is up here, not the base.";

        return null;
    }

    public static GenerateRoom BaseRoom()
    {
        var manager = BiomeBaseManager.Instance;
        var room = manager != null ? manager.Room : null;
        return room != null && room.gameObject.activeInHierarchy ? room : null;
    }

    public static void ClaimRoom()
    {
        var room = BaseRoom();
        if (room != null) SceneRefs.RoomOverride = room;
    }

    public static bool CanEnter => WhyNot() == null;

    // ---- lifetime -------------------------------------------------------------------------------

    public static void OnSceneLoaded(Scene scene)
    {
        End();

        BaseDelta.RetireOwnedBrains();

        SaveMask.Forget();
        BaseGround.Forget();
        SceneRefs.RoomOverride = null;
        SceneRefs.ContentRootOverride = null;

        Net.EditorNet.RoomChanged();

        if (scene.name != BaseScene) return;

        BaseDelta.OnArrived();
    }

    public static void End()
    {
        if (!Active) return;

        Active = false;
        Plugin.Log.LogInfo("Base editor: closed.");
    }
}
