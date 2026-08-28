using System.Collections.Generic;
using System.IO;
using COTL_API.CustomSkins;
using COTL_API.Utility;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI;

// The F7 panel: what used to be a single key that cycled player 1's fleece one step at a time.
//
// Built out of the map editor's own widget layer (MapEditorUI) so it looks and behaves like the
// editor's tool panel - same plates, same dropdowns, same scroll column. MapEditorUI takes a
// RuntimeMapEditor only to report status and register click blockers, and every one of those calls
// is null-conditional, so it works attached to nothing (see Attach(null, ...) below).
//
// While it is open the panel behaves much like the editor does: the HUD is hidden, the players are
// parked in the game's own cutscene state, and the camera is handed to a dummy follow target that
// WASD/Z/X drive. The one difference is time - the world slows to a crawl instead of stopping, for
// the reason given at PanelTimeScale. Closing puts all of that back.
public class CultTweakerPanel : MonoBehaviour
{
    public static CultTweakerPanel Active { get; private set; }

    public bool IsOpen => _open;

    // The world crawls rather than stopping. A full stop looks tidier but breaks the panel's own
    // job: the game switches the players' skeleton renderers off while it is paused, and a
    // renderer that never ticks keeps drawing the mesh it last built - a fleece picked here was
    // correct in the skeleton and stale on screen until the panel closed. A tenth of normal speed
    // keeps everything ticking (so every change shows at once) while leaving the world slow enough
    // to browse in.
    private const float PanelTimeScale = 0.1f;

    private readonly MapEditorUI _ui = new();

    private GameObject _canvasGO;
    private Canvas _canvas;
    private RectTransform _content;
    private bool _open;
    private bool _built;

    private void Awake()
    {
        Active = this;

        // The host survives scene changes, but the camera anchor and the room behind the panel do
        // not - a panel left open across a load would hold a pause and a follow target belonging
        // to a scene that no longer exists.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_open) return;

        // The anchor died with the old scene; drop the reference before Close tries to tidy it.
        _cameraAnchor = null;
        _suspendedCulling.Clear();
        Close();
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Active == this) Active = null;
        if (_open) Close();
    }

    public void Toggle()
    {
        if (_open) Close();
        else Open();
    }

    public void Open()
    {
        if (_open) return;

        // The map editor owns the camera, the pause and the HUD while it is up; two panels
        // fighting over all three ends with the game unpaused and the camera stuck on a
        // destroyed anchor.
        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing)
        {
            Plugin.Log.LogInfo("CultTweaker: close the map editor (F4) before opening the mod panel.");
            return;
        }

        // Everything in here is about the players and the world they are standing in; on the
        // title screen there is neither, and taking the camera there would strand the menu.
        if (PlayerFarming.Instance == null)
        {
            Plugin.Log.LogInfo("CultTweaker: the mod panel opens in game, not on the menu.");
            return;
        }

        EnsureUi();

        _open = true;
        _canvas.enabled = true;

        PlayerSpineLoader.LookChanged += OnLookChanged;

        // Rebuilt on every open: player two joins and leaves, mods register content late, and
        // the counts in the About section are only true at the moment they are read.
        BuildContent();

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = PanelTimeScale;

        // The same freeze the trigger sequences use - the game's own cutscene state, so held
        // movement keys do not keep the player walking behind the panel.
        TriggerActions.SetControl(false);

        // Isolated: a camera rig that refuses to hand over must not leave the panel half-open
        // with the game paused and no way to interact with it.
        try
        {
            TakeCameraControl();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("CultTweaker: free camera unavailable here: " + e.Message);
        }
    }

    public void Close()
    {
        if (!_open) return;

        _open = false;
        PlayerSpineLoader.LookChanged -= OnLookChanged;

        if (_canvas != null) _canvas.enabled = false;
        _ui.CloseTransientUi();

        // Four render targets, and nothing but the dock ever looks at them.
        _dock?.Teardown();
        PlayerPreview.Release();

        // Time first: the HUD's show animation needs a running clock.
        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;

        try
        {
            ReleaseCameraControl();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("CultTweaker: camera restore failed: " + e.Message);
        }

        // The whole point of the toggle: whatever else went wrong, the player walks again.
        TriggerActions.SetControl(true);

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
    }

    private void Update()
    {
        if (!_open) return;

        // A game menu opened underneath would restore timeScale and put the world back to full
        // speed under the panel.
        if (!Mathf.Approximately(Time.timeScale, PanelTimeScale)) Time.timeScale = PanelTimeScale;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        HandleCamera();
        HandleWheel();

        // The portraits are their own skeletons standing off the edge of the world; this is the
        // frame they are filmed in.
        PlayerPreview.Tick();
    }

    // ---- ui -----------------------------------------------------------------------------------

    private void EnsureUi()
    {
        if (_built) return;
        _built = true;

        _canvasGO = new GameObject("CultTweakerPanel_Canvas");
        _canvasGO.transform.SetParent(transform, false);

        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // Clicks are silently swallowed without an EventSystem, and the base scene does not
        // always have one up yet.
        if (EventSystem.current == null)
        {
            var events = new GameObject("CultTweakerPanel_EventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        // No editor to attach to - see the class comment.
        _ui.Attach(null, _canvasGO.GetComponent<RectTransform>());

        BuildFrame();
    }

    private const float PanelWidth = 520f;

    private void BuildFrame()
    {
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(_canvasGO.transform, false);

        // Anchored down the right edge rather than sized to its content: the list is long enough
        // to want the full screen height, and a fixed frame keeps the scroll column honest.
        var panel = panelGO.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, -80f);
        panel.anchoredPosition = new Vector2(-16f, 0f);

        // The game's own plate, as the editors' panels wear - this is where they are opened
        // from, so it should not be the one surface still in the mod's own colours.
        MapEditor.VanillaChrome.Dress(panelGO.AddComponent<Image>());

        const float headerHeight = 44f;

        var header = new GameObject("Header");
        header.transform.SetParent(panel, false);
        var headerRt = header.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, headerHeight);
        headerRt.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header.transform, Plugin.PluginName, 24);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(14f, 0f);
        titleRt.offsetMax = new Vector2(-46f, 0f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.enableWordWrapping = false;

        var close = _ui.CreateButton(header.transform, "X", Close, 30f);
        var closeRt = close.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.sizeDelta = new Vector2(30f, 30f);
        closeRt.anchoredPosition = new Vector2(-6f, 0f);

        var body = new GameObject("Body");
        body.transform.SetParent(panel, false);
        var bodyRt = body.AddComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = Vector2.zero;
        bodyRt.offsetMax = new Vector2(0f, -headerHeight);

        _content = _ui.CreateScrollColumn(bodyRt, "PanelContent", out _);
    }

    // ---- content --------------------------------------------------------------------------------

    private void BuildContent()
    {
        if (_content == null) return;

        _ui.CloseTransientUi();
        foreach (Transform child in _content)
            Destroy(child.gameObject);

        BuildAboutSection();
        BuildControlsSection();

        // The players live in their own dock along the bottom left - see PlayerDock.
        _dock ??= new PlayerDock(_ui, _canvasGO.transform) { Changed = RefreshDock };
        _dock.Rebuild();
    }

    private PlayerDock _dock;

    // The dock is torn down and built again, which it must be when the list of controls itself
    // changes - a different spine has a different set of animations to offer. Only the spine picker
    // asks for this; a fleece re-dresses the portrait in place through OnLookChanged below, because
    // rebuilding the whole dock for a change of clothes is what made it jump about.
    private void RefreshDock()
    {
        if (!_open) return;
        StartCoroutine(RefreshDockNextFrame());
    }

    private System.Collections.IEnumerator RefreshDockNextFrame()
    {
        yield return null;
        yield return null;

        if (_open) _dock?.Rebuild();
    }

    // Raised when a look actually lands on a skeleton, which is not the same moment as asking for
    // it: a fleece whose spine had to load first arrives seconds later, by callback.
    private void OnLookChanged(int playerId)
    {
        if (_open) PlayerPreview.Redress(playerId);
    }

    private void BuildAboutSection()
    {
        _ui.CreateHeader(_content, "- About -", 22);

        Note($"{Plugin.PluginName}  v{Plugin.PluginVer}");
        Note("Map editor: F4 in a dungeon.");
        Note("Loaded content:");

        Note($"Custom items: {Count(() => CustomItemLoader.loadedItems.Count)}");
        Note($"Custom meals: {Count(() => CustomMealLoader.loadedMeals.Count)}");
        Note($"Custom tarot cards: {Count(() => CustomTarotLoader.loadedTarots.Count)}");
        Note($"Custom structures: {Count(() => CustomStructureLoader.loadedStructures.Count)}");
        Note($"Building overrides: {Count(() => StructureBuildingOverrideHelper.StructureBuildingOverrides.Count)}");
        Note($"Follower skin overrides: {Count(() => FollowerSpineLoader.CustomFollowerSkins.Count)}");
        Note($"Custom NPCs: {Count(() => CustomNpcManager.CustomNpcList.Count)}");
        Note($"Player spine options: {PlayerSpines().Count}");
        Note($"Fleeces in rotation: {Count(() => PlayerSpineLoader.FleeceRotation.Count)}" +
             $"  (custom spines: {Count(() => PlayerSpineLoader.FleeceCyclingSpines.Count)})");
        Note($"Saved map blueprints: {FileCount(MapEditorSerialization.FolderName)}");
        Note($"Saved level blueprints: {FileCount(CTLevelSerialization.FolderName)}");
    }

    // One section for the three places the panel can send you, because they are the same gesture
    // three times over and had grown into three headers with five dropdowns between them. Each of
    // these opens the thing playable; the editor is a key press away once you are inside it, which
    // is what the second dropdown in each pair used to be for.
    private void BuildControlsSection()
    {
        _ui.CreateHeader(_content, "- Go -", 22);
        BuildDungeonPicker();
        BuildHubSection();
        BuildBaseSection();
        BuildWorldMapSection();

        _ui.CreateHeader(_content, "- Extras -", 22);
        _ui.CreateButton(_content, "Dump Follower Spine Atlas", DumpFollowerSlots);
        Note("WASD / arrows pan, Z and X zoom.");
        Note("F7 or Esc to close.");
    }

    // Saved world maps, openable from anywhere in game. Listed fresh on every open for the same
    // reason the dungeon picker is: saving a map changes the list.
    private void BuildWorldMapSection()
    {
        var names = MapEditor.CTWorldMapSerialization.ListNames();
        if (names.Count == 0)
        {
            Note("No world maps are saved yet.");
        }
        else
        {
            // An action dropdown like Enter Dungeon: picking is the gesture.
            _ui.CreateDropdown(_content, "Open World Map", names, (index, _) =>
            {
                if (index < 0 || index >= names.Count) return;
                OpenWorldMap(names[index]);
            });
        }

        _ui.CreateButton(_content, "New World Map", () =>
        {
            // A named folder is the map's identity, so a name comes first - the next free one in
            // the series, so this button always opens an empty canvas instead of handing back
            // whatever was last saved as "untitledworld". Rename it from the editor's File tool.
            var fresh = new MapEditor.CTWorldMap
            {
                MapName = MapEditor.CTWorldMapSerialization.FreeName("untitledworld")
            };

            fresh.Nodes.Add(new MapEditor.CTWorldMapNode
            {
                Id = "start",
                DisplayName = "Start",
                NodeType = "Base",
                InitialState = "Selectable"
            });
            MapEditor.CTWorldMapSerialization.Save(fresh);
            OpenWorldMap(fresh.MapName, edit: true);
        });
    }

    // Hubs are authored in the game's own town room rather than in a dungeon, so their entry point
    // is here rather than in the F4 editor's Level tool: the editor can only be reached once the
    // room it edits is standing.
    private void BuildHubSection()
    {
        var hubs = new List<string>();
        foreach (var level in MapEditor.CTLevelSerialization.LoadAll())
            if (level is { IsHub: true } && !string.IsNullOrWhiteSpace(level.LevelName))
                hubs.Add(level.LevelName);
        hubs.Sort(System.StringComparer.OrdinalIgnoreCase);

        if (hubs.Count > 0)
        {
            // Entered rather than opened for editing: this rebuilds the saved hub and leaves the
            // player standing in it, and F4 edits it from there. Author() is for a hub that does
            // not exist yet, which is what New Hub is.
            _ui.CreateDropdown(_content, "Enter Hub", hubs, (index, _) =>
            {
                if (index < 0 || index >= hubs.Count) return;
                EnterHub(hubs[index]);
            });
        }
        else
        {
            Note("No hubs saved yet.");
        }

        _ui.CreateButton(_content, "New Hub", () => BeginHub(MapEditor.HubSession.FreeName()));
    }

    // The base editor. It has no picker and no list: there is one base, it is the one the player is
    // standing in, and its file is named after their save slot. So the section is a button and a
    // line saying why it is not available when it is not.
    private void BuildBaseSection()
    {
        var blocked = MapEditor.BaseSession.WhyNot();

        if (blocked != null)
        {
            Note("Base editor: " + blocked);
            return;
        }

        _ui.CreateButton(_content, "Edit Base", EditBase);
        Note($"Edits the base for save slot {MapEditor.BaseDelta.Slot}. The game's own save is not " +
             "written to.");
    }

    private void EditBase()
    {
        // Closed first: the editor takes the screen and the pause.
        Close();

        var error = MapEditor.BaseSession.Enter();
        if (error != null) Plugin.Log.LogWarning("CultTweaker: " + error);
    }

    private void BeginHub(string hubName)
    {
        // Closed first: the trip runs a transition and the editor takes the screen.
        Close();

        var error = MapEditor.HubSession.Author(hubName);
        if (error != null) Plugin.Log.LogWarning("CultTweaker: " + error);
        else Plugin.Log.LogInfo($"CultTweaker: authoring hub '{hubName}'.");
    }

    private void EnterHub(string hubName)
    {
        Close();

        var error = MapEditor.HubSession.Play(hubName);
        if (error != null) Plugin.Log.LogWarning("CultTweaker: " + error);
    }

    private void OpenWorldMap(string mapName, bool edit = false)
    {
        var screen = MapEditor.WorldMap.WorldMapScreen.Instance;
        if (screen == null)
        {
            Plugin.Log.LogWarning("CultTweaker: the world map screen is not available.");
            return;
        }

        Plugin.Log.LogInfo($"CultTweaker: opening world map '{mapName}'.");

        // Closed FIRST: both hold the pause and the screen, and the map's guard refuses to
        // open over the panel.
        Close();
        screen.Open(mapName, startInEditMode: edit);
    }

    // Every custom dungeon registered this session, not just the first one found: the map
    // editor's level runner, whatever test dungeons the mod registers, and one per dungeon built
    // with the dungeon builder. Read when the panel is built rather than cached, because saving
    // a dungeon in the map editor registers it straight away - the list is different the next
    // time the panel opens.
    private void BuildDungeonPicker()
    {
        var dungeons = new List<CustomDungeon>();
        foreach (var entry in CustomDungeonManager.CustomDungeonList.Values)
            if (entry != null) dungeons.Add(entry);

        if (dungeons.Count == 0)
        {
            Note("No custom dungeon is registered.");
            return;
        }

        dungeons.Sort((a, b) => string.Compare(DungeonLabel(a), DungeonLabel(b),
            System.StringComparison.OrdinalIgnoreCase));

        var names = new List<string>(dungeons.Count);
        foreach (var dungeon in dungeons) names.Add(DungeonLabel(dungeon));

        // No preselection: the dropdown is an action, so its caption stays "Enter Dungeon" until
        // something is picked, and picking is the whole gesture the button used to be.
        _ui.CreateDropdown(_content, "Enter Dungeon", names, (index, _) =>
        {
            if (index < 0 || index >= dungeons.Count) return;
            EnterCustomDungeon(dungeons[index]);
        });
    }

    // What the dungeon calls itself in game. A dungeon with no name of its own falls back to the
    // internal one, which is at least unique.
    private static string DungeonLabel(CustomDungeon dungeon)
    {
        if (dungeon == null) return "";
        return !string.IsNullOrWhiteSpace(dungeon.DungeonName) ? dungeon.DungeonName
            : !string.IsNullOrWhiteSpace(dungeon.InternalName) ? dungeon.InternalName
            : dungeon.Location.ToString();
    }

    private void EnterCustomDungeon(CustomDungeon dungeon)
    {
        if (dungeon == null) return;

        Plugin.Log.LogInfo($"CultTweaker: entering custom dungeon '{DungeonLabel(dungeon)}' " +
                           $"({dungeon.Location}).");

        // Closed FIRST: entering loads a scene, and the panel is holding the pause, the HUD and
        // the camera rig that the load would strand.
        Close();

        try
        {
            dungeon.EnterDungeon();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"CultTweaker: entering '{DungeonLabel(dungeon)}' failed: {e}");
        }
    }

    // The config flag does the same job, but only on the next follower the game happens to dress,
    // and only if the file is not already there. Pressed here it is an explicit request: it reads
    // a follower standing in the world right now and replaces whatever was dumped before.
    private void DumpFollowerSlots()
    {
        var skeleton = FollowerSlotDumper.FindLiveFollowerSkin();
        if (skeleton == null)
        {
            // Nothing to read here (a dungeon with no followers along). Arming the flag means the
            // dump happens by itself the next time a follower is dressed.
            Plugin.DebugDumpFollowerSpineAtlas.Value = true;
            Plugin.Log.LogWarning("No follower in this scene to read; the dump will run the next " +
                                  "time a follower is dressed. Delete followerSlots.json first if " +
                                  "one already exists.");
            return;
        }

        var path = FollowerSlotDumper.Dump(skeleton, overwrite: true);
        Plugin.Log.LogInfo(path != null
            ? "Follower slots dumped to " + path
            : "Follower slot dump failed; see the log above.");
    }

    // The editor's own note size, so the panel and the tool panels read as the same thing.
    private void Note(string text)
    {
        var label = _ui.CreateLabel(_content, text, 14);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.75f);
    }

    // ---- COTL_API internals ---------------------------------------------------------------------

    // CustomPlayerSpines is internal to COTL_API, so it is read through Harmony's traverse rather
    // than by depending on a publicized build of it - the same approach the map editor's enemy
    // picker uses for the custom enemy list. The selected spine is read the same way, but by
    // PlayerSpineLoader, which needs it to find the active skin's config.
    internal static List<string> PlayerSpineNames() => PlayerSpines();

    private static List<string> PlayerSpines()
    {
        try
        {
            var dict = Traverse.Create(typeof(CustomSkinManager))
                .Field("CustomPlayerSpines")
                .GetValue<Dictionary<string, SkeletonDataAsset>>();

            // Ours come from the lazy loader's registry - present whether loaded yet or not -
            // and anything another mod registered straight with the API is appended after.
            var result = PlayerSpineLoader.RegisteredSpineNames();

            if (dict == null) return result;

            foreach (var pair in dict)
            {
                // The API registers this one purely so its settings dropdown has an entry to
                // show before any mod adds a real spine; selecting it does nothing.
                if (pair.Key.StartsWith("Placeholder/")) continue;
                if (!result.Exists(name => string.Equals(name, pair.Key, System.StringComparison.OrdinalIgnoreCase)))
                    result.Add(pair.Key);
            }

            return result;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("CultTweaker: could not read COTL_API player spine list: " + e.Message);
            return [];
        }
    }

    private static int Count(System.Func<int> read)
    {
        try
        {
            return read();
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    private static int FileCount(string folderName)
    {
        try
        {
            var path = Path.Combine(Plugin.PluginPath, folderName);
            return Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Length : 0;
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    // ---- camera -----------------------------------------------------------------------------

    // The map editor's free camera, minus the parts that only make sense while editing a room.
    // Panning drives the game's own rig through a dummy follow target because CameraFollowTarget
    // re-asserts the camera position every frame, so writing to Camera.main is reverted.
    private GameObject _cameraAnchor;
    private float _zoom = 12f;
    private float _savedTimeScale = 1f;
    private readonly List<BaseBiomeAreaCulling> _suspendedCulling = [];

    private const float PanSpeed = 14f;
    private const float ZoomSpeed = 18f;
    private const float MinZoom = 4f;
    private const float MaxZoom = 45f;

    private void TakeCameraControl()
    {
        var start = PlayerFarming.Instance != null
            ? PlayerFarming.Instance.transform.position
            : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        start.z = 0f;

        _cameraAnchor = new GameObject("CultTweakerPanel_CameraAnchor");
        _cameraAnchor.transform.position = start;

        var gm = GameManager.GetInstance();
        if (gm != null && gm.CamFollowTarget != null)
            _zoom = Mathf.Clamp(gm.CamFollowTarget.targetDistance, MinZoom, MaxZoom);

        CinematicCameraManager.SetCameraLimits(false, default);
        CinematicCameraManager.SetFollowTarget(_cameraAnchor);

        SuspendAreaCulling();
    }

    // Whole areas deactivate when their precomputed bounds leave the viewport, which a roaming
    // camera triggers constantly - the world would appear to delete itself as you look around.
    private void SuspendAreaCulling()
    {
        _suspendedCulling.Clear();

        foreach (var culling in Resources.FindObjectsOfTypeAll<BaseBiomeAreaCulling>())
        {
            if (culling == null || !culling.enabled) continue;
            culling.enabled = false;
            _suspendedCulling.Add(culling);
        }
    }

    private void ReleaseCameraControl()
    {
        foreach (var culling in _suspendedCulling)
            if (culling != null) culling.enabled = true;
        _suspendedCulling.Clear();

        CinematicCameraManager.ResetCameraTargets();
        CinematicCameraManager.ZoomReset();

        if (_cameraAnchor == null) return;
        Destroy(_cameraAnchor);
        _cameraAnchor = null;
    }

    private void HandleCamera()
    {
        if (_cameraAnchor == null) return;

        // Unscaled, so panning stays responsive while the world runs at a tenth speed.
        var dt = Time.unscaledDeltaTime;

        var move = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;

        if (move != Vector3.zero)
            _cameraAnchor.transform.position += move.normalized * (PanSpeed * dt);

        var zoomDelta = 0f;
        if (Input.GetKey(KeyCode.Z)) zoomDelta += 1f;
        if (Input.GetKey(KeyCode.X)) zoomDelta -= 1f;

        if (Mathf.Abs(zoomDelta) > 0.001f)
            _zoom = Mathf.Clamp(_zoom + zoomDelta * ZoomSpeed * dt, MinZoom, MaxZoom);

        // Every frame, and through CameraSetZoom rather than the target-only call: the camera
        // chases its target distance on SCALED time, which is a tenth of normal here, so a
        // target-only write would crawl towards the new zoom instead of arriving at it.
        var gm = GameManager.GetInstance();
        if (gm != null && gm.CamFollowTarget != null) gm.CameraSetZoom(_zoom);
    }

    // This game installs Rewired's pointer module, which never delivers scroll events to uGUI
    // ScrollRects, so the wheel is routed to our own lists by hand.
    private void HandleWheel()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.005f) return;
        if (_canvasGO == null) return;

        var mouse = (Vector2)Input.mousePosition;
        var scrollRects = _canvasGO.GetComponentsInChildren<ScrollRect>(false);

        // Back to front: an open dropdown list is parented last and must win over the panel
        // underneath it.
        for (var i = scrollRects.Length - 1; i >= 0; i--)
        {
            var rect = scrollRects[i];
            if (rect == null || rect.viewport == null || rect.content == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect.viewport, mouse, null)) continue;

            var hidden = rect.content.rect.height - rect.viewport.rect.height;
            if (hidden > 1f)
                rect.verticalNormalizedPosition =
                    Mathf.Clamp01(rect.verticalNormalizedPosition + Mathf.Sign(scroll) * 90f / hidden);
            return;
        }
    }
}
