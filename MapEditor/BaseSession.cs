using MMRoomGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomSpineLoader.MapEditor;

// The player's own base, edited in place.
//
// A hub is authored from nothing: the town room is emptied and whatever the author draws becomes the
// whole of it. The base is the opposite. It already exists, the player lives in it, and most of what
// is standing there is not ours - it is their save. So nothing is ever cleared and nothing is
// rebuilt. The editor opens on the base as it stands and every gesture is recorded as a *difference*
// from it: what was added, what was taken away, what was moved. That difference lives in a file of
// our own, one per save slot, and is re-applied on every arrival.
//
// The rule the whole feature is built around: the game's save file is never written to on the
// editor's behalf. Not one structure added to it, not one removed, not one position changed in it.
// Where a change has to exist at runtime for the game to behave - a moved building the followers
// have to walk to - it is masked back out for the length of every save write (see SaveMask).
public static class BaseSession
{
    public const string BaseScene = "Base Biome 1";

    public static bool Active { get; private set; }

    // Both sessions live in this one scene, so it is also the scene where F4 on its own means
    // nothing: the editor host is standing there to apply a saved base, not to be opened.
    public static bool SceneOwnsSessions =>
        SceneManager.GetActiveScene().name == BaseScene;

    // ---- entry ----------------------------------------------------------------------------------

    // Opens the editor on the base the player is standing in. Returns an error to show, or null.
    //
    // Deliberately does not travel. A hub can be entered from anywhere because it is built from
    // nothing and the trip is part of it; the base editor edits what is under the player's feet, and
    // a version of it that warped them home would be a different feature wearing the same button.
    public static string Enter()
    {
        if (Active) return "The base editor is already open.";

        var blocked = WhyNot();
        if (blocked != null) return blocked;

        Active = true;

        // All three normally happen when a saved base is applied on arrival. A slot with nothing
        // saved yet never runs that, and the first session on it needs them just as much: the room
        // claim is what points the tools at the base, the content root is how anything can tell what
        // this mod placed from what the player owns, and the remembered outline is what the first
        // shape drawn here gets appended to.
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

    // Every reason the base cannot be edited right now, as one message or null. Shared by the panel
    // (which greys the button out and says why) and by Enter itself.
    public static string WhyNot()
    {
        if (SceneManager.GetActiveScene().name != BaseScene)
            return "The base editor opens in the base, not here.";

        if (PlayerFarming.Instance == null) return "The base editor opens in game, not on the menu.";
        if (BiomeBaseManager.Instance == null) return "The base has not finished loading.";

        // A hub is the same scene wearing a different room. Editing "the base" while one is up would
        // record the hub's contents into the base's file - and clear them from it on the way back.
        if (HubSession.Busy) return "A hub is open here; leave it first.";

        if (PlayerFarming.Location != FollowerLocation.Base)
            return "The player is not in the base right now.";

        // The room itself, standing and switched on. Not "is it GenerateRoom.Instance": that static
        // is whichever room enabled last, and this scene has four - see SceneRefs.RoomOverride.
        if (BaseRoom() == null) return "The base room is not standing here.";

        // Raising the town room switches the base room off with it.
        var town = BiomeBaseManager.Instance.DLC_ShrineRoom;
        if (town != null && town.gameObject.activeInHierarchy)
            return "The town room is up here, not the base.";

        return null;
    }

    // The base's own room, or null when it is not the one on screen.
    public static GenerateRoom BaseRoom()
    {
        var manager = BiomeBaseManager.Instance;
        var room = manager != null ? manager.Room : null;
        return room != null && room.gameObject.activeInHierarchy ? room : null;
    }

    // Points every tool at the base rather than at whichever room enabled last. Set for as long as
    // the base is the room; dropped by a hub taking the scene over, and by any scene load.
    public static void ClaimRoom()
    {
        var room = BaseRoom();
        if (room != null) SceneRefs.RoomOverride = room;
    }

    public static bool CanEnter => WhyNot() == null;

    // ---- lifetime -------------------------------------------------------------------------------

    // Called by the plugin for every scene load. A session never survives one: the base is rebuilt
    // from the game's save plus our delta on the way back in, so there is nothing to carry over.
    public static void OnSceneLoaded(Scene scene)
    {
        End();

        // The buildings this editor stood up are ours, and the game clears brains only on quit,
        // death or the menu - left alone they would still be listed against the base the next time
        // anyone walks in, pointing at objects that died with the scene.
        BaseDelta.RetireOwnedBrains();

        // The moved buildings it was masking are gone with the scene; the ones in the next base are
        // registered again as the delta re-applies them. The ground outlines go the same way - the
        // next base has its own, and a bought plot of land makes them different.
        SaveMask.Forget();
        BaseGround.Forget();
        SceneRefs.RoomOverride = null;
        SceneRefs.ContentRootOverride = null;

        if (scene.name != BaseScene) return;

        // Not the session - the delta. Arriving in the base is what re-applies what this slot's
        // author added to it, whether or not anybody is going to open the editor.
        BaseDelta.OnArrived();
    }

    public static void End()
    {
        if (!Active) return;

        Active = false;
        Plugin.Log.LogInfo("Base editor: closed.");
    }
}
