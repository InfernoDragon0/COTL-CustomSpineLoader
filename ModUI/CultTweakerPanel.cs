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
        if (_canvas != null) _canvas.enabled = false;
        _ui.CloseTransientUi();

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

        var plate = panelGO.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.72f);

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
        BuildFleeceSettings();

        // Players one and two always have a section, present or not: the panel is also how you
        // set up player two's look BEFORE they join, and a section that appears and disappears
        // as a controller connects is worse than one that says "not in the game".
        var playerCount = Mathf.Max(2, PlayerFarming.players != null ? PlayerFarming.players.Count : 0);
        for (var i = 0; i < playerCount; i++) BuildPlayerSection(i);

        BuildControlsSection();
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

    private void BuildFleeceSettings()
    {
        _ui.CreateHeader(_content, "- Fleece & Skin Settings -", 22);

        // The F9 toggle, given a face. Off, the SetSkin patch stops re-dressing the players, so
        // the game's own fleece comes back on the next skin rebuild.
        _ui.CreateToggle(_content, "Fleece cycling enabled", Plugin.FleeceCyclingEnabled.Value, value =>
        {
            Plugin.FleeceCyclingEnabled.Value = value;

            if (value)
            {
                ReapplyFleeces();
                return;
            }

            foreach (var player in TriggerActions.LivePlayers()) player.SetSkin();
        });
    }

    private void BuildPlayerSection(int playerId)
    {
        var player = PlayerSpineLoader.ResolvePlayer(playerId);

        _ui.CreateHeader(_content, $"- Player {playerId + 1} -", 22);
        if (player == null) Note("Player 2 not in the game!");

        // ---- fleece
        var fleeces = PlayerSpineLoader.FleeceRotation;
        if (fleeces.Count == 0)
        {
            Note("No fleeces found yet; enter a level once.");
        }
        else
        {
            // A label above each picker as well as in it: the dropdown's own caption is replaced
            // by whatever was chosen, so after one selection the row would no longer say what it
            // controls.
            FieldLabel("Fleece Transmog");

            var fleeceDropdown = _ui.CreateDropdown(_content, "Fleece Transmog", fleeces,
                (index, _) => ApplyFleece(playerId, index));

            var current = PlayerSpineLoader.GetFleeceIndex(playerId);
            if (current >= 0 && current < fleeces.Count) fleeceDropdown.SetSelected(current);
        }

        // ---- spine
        var spines = PlayerSpines();
        if (spines.Count == 0)
        {
            Note("No custom player spines are registered.");
            return;
        }

        // COTL_API tracks a selected spine for players one and two only; a third player's spine
        // would be written into player one's slot, so the picker is not offered for them.
        if (playerId > 1)
        {
            Note("Spine selection is limited to players 1 and 2.");
            return;
        }

        FieldLabel("Player Spine");

        var spineDropdown = _ui.CreateDropdown(_content, "Player Spine", spines,
            (_, value) => ApplySpine(playerId, value));

        var selected = spines.IndexOf(SelectedSpine(playerId));
        if (selected >= 0) spineDropdown.SetSelected(selected);
    }

    private void BuildControlsSection()
    {
        _ui.CreateHeader(_content, "- Camera -", 22);
        Note("WASD / arrows pan, Z and X zoom.");
        Note("F7 or Esc to close.");

        _ui.CreateHeader(_content, "- Extras -", 22);
        _ui.CreateButton(_content, "Enter Editor Dungeon", EnterCustomDungeon);
        _ui.CreateButton(_content, "Dump Follower Spine Atlas", DumpFollowerSlots);
    }

    private void EnterCustomDungeon()
    {
        CustomDungeon dungeon = null;
        foreach (var entry in CustomDungeonManager.CustomDungeonList.Values)
        {
            // The first registered dungeon is the test one; the level runner's dungeon is
            // registered after it and is entered through the map editor's level tool instead.
            if (entry == null) continue;
            dungeon = entry;
            break;
        }

        if (dungeon == null)
        {
            Plugin.Log.LogWarning("CultTweaker: no custom dungeon is registered.");
            return;
        }

        // Closed FIRST: entering loads a scene, and the panel is holding the pause, the HUD and
        // the camera rig that the load would strand.
        Close();
        dungeon.EnterDungeon();
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

    private void Note(string text)
    {
        var label = _ui.CreateLabel(_content, text, 16);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.75f);
    }

    // Names the control below it. Brighter than a note, because it is part of the control.
    private void FieldLabel(string text)
    {
        var label = _ui.CreateLabel(_content, text, 17);
        label.GetComponent<TMP_Text>().color = new Color(0.98f, 0.94f, 0.85f);
    }

    // ---- actions ----------------------------------------------------------------------------

    private void ApplyFleece(int playerId, int index)
    {
        if (!Plugin.FleeceCyclingEnabled.Value)
        {
            Plugin.Log.LogWarning("Fleece cycling is disabled; enable it above first.");
            return;
        }

        PlayerSpineLoader.ApplyFleece(playerId, index);
    }

    private void ReapplyFleeces()
    {
        for (var i = 0; i < 2; i++)
        {
            var index = PlayerSpineLoader.GetFleeceIndex(i);
            if (index >= 0) PlayerSpineLoader.ApplyFleece(i, index);
        }
    }

    private static void ApplySpine(int playerId, string spineKey)
    {
        try
        {
            CustomSkinManager.ChangeSelectedPlayerSpine(spineKey, playerId);
            Plugin.Log.LogInfo($"Player {playerId + 1} spine set to {spineKey}.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Spine swap failed: " + e.Message);
        }
    }

    // ---- COTL_API internals ---------------------------------------------------------------------

    // CustomPlayerSpines and SelectedSpine are internal to COTL_API, so they are read through
    // Harmony's traverse rather than by depending on a publicized build of it - the same approach
    // the map editor's enemy picker uses for the custom enemy list.
    private static List<string> PlayerSpines()
    {
        try
        {
            var dict = Traverse.Create(typeof(CustomSkinManager))
                .Field("CustomPlayerSpines")
                .GetValue<Dictionary<string, SkeletonDataAsset>>();

            if (dict == null) return [];

            var result = new List<string>(dict.Count);
            foreach (var pair in dict)
            {
                // The API registers this one purely so its settings dropdown has an entry to
                // show before any mod adds a real spine; selecting it does nothing.
                if (pair.Key.StartsWith("Placeholder/")) continue;
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

    private static string SelectedSpine(int playerId)
    {
        try
        {
            return Traverse.Create(typeof(CustomSkinManager))
                .Field(playerId == 1 ? "SelectedSpine2" : "SelectedSpine")
                .GetValue<string>() ?? "";
        }
        catch (System.Exception)
        {
            return "";
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
