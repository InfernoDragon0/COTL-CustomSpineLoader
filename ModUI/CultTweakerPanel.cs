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

public class CultTweakerPanel : MonoBehaviour
{
    public static CultTweakerPanel Active { get; private set; }

    public bool IsOpen => _open;

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

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_open) return;

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

        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing)
        {
            Plugin.Log.LogInfo("CultTweaker: close the map editor (F4) before opening the mod panel.");
            return;
        }

        if (PlayerFarming.Instance == null)
        {
            Plugin.Log.LogInfo("CultTweaker: the mod panel opens in game, not on the menu.");
            return;
        }

        EnsureUi();

        _open = true;
        _canvas.enabled = true;

        PlayerSpineLoader.LookChanged += OnLookChanged;

        BuildContent();

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = PanelTimeScale;

        TriggerActions.SetControl(false);

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

        _dock?.Teardown();
        PlayerPreview.Release();

        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;

        try
        {
            ReleaseCameraControl();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("CultTweaker: camera restore failed: " + e.Message);
        }

        TriggerActions.SetControl(true);

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
    }

    private void Update()
    {
        if (!_open) return;

        if (!Mathf.Approximately(Time.timeScale, PanelTimeScale)) Time.timeScale = PanelTimeScale;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        HandleCamera();
        HandleWheel();

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

        if (EventSystem.current == null)
        {
            var events = new GameObject("CultTweakerPanel_EventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        _ui.Attach(null, _canvasGO.GetComponent<RectTransform>());

        BuildFrame();
    }

    private const float PanelWidth = 520f;

    private void BuildFrame()
    {
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(_canvasGO.transform, false);

        var panel = panelGO.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, -80f);
        panel.anchoredPosition = new Vector2(-16f, 0f);

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

        _dock ??= new PlayerDock(_ui, _canvasGO.transform) { Changed = RefreshDock };
        _dock.Rebuild();
    }

    private PlayerDock _dock;

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

    private void BuildWorldMapSection()
    {
        var names = MapEditor.CTWorldMapSerialization.ListNames();
        if (names.Count == 0)
        {
            Note("No world maps are saved yet.");
        }
        else
        {
            _ui.CreateDropdown(_content, "Open World Map", names, (index, _) =>
            {
                if (index < 0 || index >= names.Count) return;
                OpenWorldMap(names[index]);
            });
        }

        _ui.CreateButton(_content, "New World Map", () =>
        {
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

    private void BuildHubSection()
    {
        var hubs = new List<string>();
        foreach (var level in MapEditor.CTLevelSerialization.LoadAll())
            if (level is { IsHub: true } && !string.IsNullOrWhiteSpace(level.LevelName))
                hubs.Add(level.LevelName);
        hubs.Sort(System.StringComparer.OrdinalIgnoreCase);

        if (hubs.Count > 0)
        {
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
        Close();

        var error = MapEditor.BaseSession.Enter();
        if (error != null) Plugin.Log.LogWarning("CultTweaker: " + error);
    }

    private void BeginHub(string hubName)
    {
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

        Close();
        screen.Open(mapName, startInEditMode: edit);
    }

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

        _ui.CreateDropdown(_content, "Enter Dungeon", names, (index, _) =>
        {
            if (index < 0 || index >= dungeons.Count) return;
            EnterCustomDungeon(dungeons[index]);
        });
    }

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

    private void DumpFollowerSlots()
    {
        var skeleton = FollowerSlotDumper.FindLiveFollowerSkin();
        if (skeleton == null)
        {
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

    private void Note(string text)
    {
        var label = _ui.CreateLabel(_content, text, 14);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.75f);
    }

    // ---- COTL_API internals ---------------------------------------------------------------------

    internal static List<string> PlayerSpineNames() => PlayerSpines();

    private static List<string> PlayerSpines()
    {
        try
        {
            var dict = Traverse.Create(typeof(CustomSkinManager))
                .Field("CustomPlayerSpines")
                .GetValue<Dictionary<string, SkeletonDataAsset>>();

            var result = PlayerSpineLoader.RegisteredSpineNames();

            if (dict == null) return result;

            foreach (var pair in dict)
            {
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

        var gm = GameManager.GetInstance();
        if (gm != null && gm.CamFollowTarget != null) gm.CameraSetZoom(_zoom);
    }

    private void HandleWheel()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.005f) return;
        if (_canvasGO == null) return;

        var mouse = (Vector2)Input.mousePosition;
        var scrollRects = _canvasGO.GetComponentsInChildren<ScrollRect>(false);

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
