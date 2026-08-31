using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.ModUI.SkinEditor.Tools;

public class SkinSetupPanel : IMapEditorTool
{
    public string Name => "Skin";

    private const string CreateNew = "Create New Skin or Variant";
    private const string AddSlot = "Add an override slot";
    private const int NameLimit = 60;

    private readonly FollowerSkinEditor _editor;

    private MapEditorDropdown _pairs;
    private List<string> _pairNames = [];
    private string _pendingLoad;
    private bool _refreshQueued;

    private readonly Dictionary<int, Sprite> _sprites = [];

    private CTFollowerSkinDocument Document => _editor.Document;
    private FollowerSkinConfig Config => Document.Config;

    public SkinSetupPanel(FollowerSkinEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        Document.NormaliseColourSets();
        Config.PartConfigs ??= [];

        BuildSkins(panel, ui);
        BuildBase(panel, ui);
        BuildPreview(panel, ui);
        BuildColourSets(panel, ui);
        BuildLayers(panel, ui);
    }

    // ---- skins ---------------------------------------------------------------------------------

    private void BuildSkins(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Skins");
        ui.CreateLabel(panel, "Custom Skins", 18);

        _pairs = ui.CreateDropdown(panel, Document.ShownName, [], (index, _) =>
        {
            if (index <= 0)
            {
                CreateNewSkin();
                return;
            }

            var wanted = index - 1;
            if (wanted >= _pairNames.Count) return;
            if (CTFollowerSkinSerialization.TrySplitPair(_pairNames[wanted], out var skin, out var variant))
                Load(skin, variant);
        });

        ui.CreateButton(panel, "Save as...", SaveAs, 36f);

        RefreshPairs();
    }

    private void RefreshPairs()
    {
        if (_pairs == null) return;

        _pairNames = CTFollowerSkinSerialization.ListPairs();
        if (!_pairNames.Contains(Document.ShownName)) _pairNames.Insert(0, Document.ShownName);

        var options = new List<string>(_pairNames.Count + 1) { CreateNew };
        options.AddRange(_pairNames);
        _pairs.SetOptions(options);
        _pairs.SetSelected(Mathf.Max(0, _pairNames.IndexOf(Document.ShownName) + 1));
    }

    private void CreateNewSkin()
    {
        var open = Document.SkinName;
        var suggested = CTFollowerSkinSerialization.SkinExists(open)
            ? CTFollowerSkinSerialization.Pair(open, CTFollowerSkinSerialization.FreeVariantName(open))
            : CTFollowerSkinSerialization.Pair(CTFollowerSkinSerialization.FreeSkinName(),
                CTFollowerSkinSerialization.DefaultVariant);

        MapNamePrompt.Show(_editor, suggested, "Name the new skin and variant",
            text =>
            {
                if (!CTFollowerSkinSerialization.TrySplitPair(text, out var skin, out var variant)) return;

                var fromSkin = Document.SkinName;
                var fromVariant = Document.VariantName;
                var sameSkin = string.Equals(skin, fromSkin, StringComparison.OrdinalIgnoreCase);

                var renamed = false;
                if (!CTFollowerSkinSerialization.SkinExists(skin) &&
                    !string.Equals(variant, CTFollowerSkinSerialization.DefaultVariant, StringComparison.OrdinalIgnoreCase))
                {
                    variant = CTFollowerSkinSerialization.DefaultVariant;
                    renamed = true;
                }
                var config = sameSkin
                    ? Clone(Config)
                    : new FollowerSkinConfig { OverrideBaseSkin = "Cat", PartConfigs = [] };

                var made = sameSkin
                    ? $"New variant {variant} created."
                    : $"New skin {skin} created.";

                _editor.LoadDocument(new CTFollowerSkinDocument
                {
                    SkinName = skin,
                    VariantName = variant,
                    Config = config
                }, made + " Baking skin, lag spikes will occur!");

                var folder = CTFollowerSkinSerialization.VariantFolder(skin, variant);
                if (CTFollowerSkinSerialization.Save(_editor.Document) != null) _editor.MarkSaved();
                else _editor.SetStatus($"'{folder}' could not be created; the log says why.", StatusSeverity.Error);

                var copied = sameSkin
                    ? CTFollowerSkinSerialization.CopyImages(fromSkin, fromVariant, skin, variant)
                    : 0;

                _refreshQueued = true;

                if (_editor.Busy) return;

                if (renamed)
                    _editor.SetStatus($"{made} A skin's first variant is always 'base'. Drop pngs into " +
                                      $"FollowerSkins/{skin}/{variant} to use them.");
                else if (copied > 0)
                    _editor.SetStatus($"{made} It has the same layers, with {copied} image(s) copied along.");
                else
                    _editor.SetStatus($"{made} Drop pngs into FollowerSkins/{skin}/{variant} to use them.");
            },
            onClosed: () => _refreshQueued = true,
            existsCheck: CTFollowerSkinSerialization.PairExists, existsNoun: "variant",
            characterLimit: NameLimit, validate: CTFollowerSkinSerialization.PairProblem);
    }

    private void SaveAs()
    {
        MapNamePrompt.Show(_editor, Document.ShownName, "Save the skin as",
            text =>
            {
                if (!CTFollowerSkinSerialization.TrySplitPair(text, out var skin, out var variant)) return;

                var fromSkin = Document.SkinName;
                var fromVariant = Document.VariantName;

                Document.SkinName = skin;
                Document.VariantName = variant;

                _editor.SetStatus($"Saving as '{skin}/{variant}' and rebuilding it for the game - " +
                                  "expect a short pause.", StatusSeverity.Warning);

                _editor.StartCoroutine(AfterPaint(() =>
                {
                    var copied = CTFollowerSkinSerialization.CopyImages(fromSkin, fromVariant, skin, variant);
                    if (!string.Equals(fromSkin, skin, StringComparison.OrdinalIgnoreCase))
                        copied += CTFollowerSkinSerialization.CopyOtherVariants(fromSkin, skin, variant);

                    _editor.SaveNow();

                    if (copied > 0)
                        _editor.SetStatus($"Saved as '{skin}/{variant}' with {copied} file(s) copied along. " +
                                          "Followers can wear it now.", StatusSeverity.Success);

                    _refreshQueued = true;
                }));
            },
            onClosed: () => _refreshQueued = true,
            existsCheck: CTFollowerSkinSerialization.PairExists, existsNoun: "variant",
            characterLimit: NameLimit, validate: CTFollowerSkinSerialization.PairProblem);
    }

    private static IEnumerator AfterPaint(Action work)
    {
        yield return null;
        yield return null;
        work();
    }

    private static FollowerSkinConfig Clone(FollowerSkinConfig config) =>
        JsonConvert.DeserializeObject<FollowerSkinConfig>(JsonConvert.SerializeObject(config))
        ?? new FollowerSkinConfig { PartConfigs = [] };

    private void Load(string skin, string variant)
    {
        if (skin == Document.SkinName && variant == Document.VariantName) return;

        var target = CTFollowerSkinSerialization.Pair(skin, variant);
        if (_editor.HasUnsavedEdits && _pendingLoad != target)
        {
            _pendingLoad = target;
            _refreshQueued = true;
            _editor.SetStatus($"'{Document.ShownName}' has unsaved changes. Save it, or pick this again " +
                              "to discard them.", StatusSeverity.Warning);
            return;
        }

        _pendingLoad = null;

        var loaded = CTFollowerSkinSerialization.Load(skin, variant);
        if (loaded == null)
        {
            _editor.SetStatus($"'{target}' has no readable config.json; the log says why.", StatusSeverity.Error);
            _refreshQueued = true;
            return;
        }

        _editor.SelectedLayer = null;
        _editor.LoadDocument(loaded);
        _editor.SetStatus($"Editing '{loaded.ShownName}'.");
    }

    // ---- base ---------------------------------------------------------------------------------

    private void BuildBase(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Base");

        var bases = SkinParts.BaseSkinNames();
        var picker = ui.CreateDropdown(panel, Config.OverrideBaseSkin ?? "Cat", bases, (_, name) =>
        {
            if (string.IsNullOrEmpty(name) || name == Config.OverrideBaseSkin) return;
            Config.OverrideBaseSkin = name;
            Touch($"The skin is built on '{name}'.");
            _editor.RebuildPanels();
        });

        var current = bases.IndexOf(Config.OverrideBaseSkin ?? "");
        if (current >= 0) picker.SetSelected(current);
    }

    // ---- preview ------------------------------------------------------------------------------

    private void BuildPreview(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Preview");

        var animations = _editor.Preview.Animations();
        var picker = ui.CreateDropdown(panel, _editor.Animation ?? "Animation", animations, (_, name) =>
        {
            if (string.IsNullOrEmpty(name) || name == _editor.Animation) return;
            _editor.Animation = name;
            _editor.Preview.Play(name, _editor.Loop, _editor.Speed);
            _editor.SetStatus($"Playing '{name}'.");
        });

        var current = animations.IndexOf(_editor.Animation ?? "");
        if (current >= 0) picker.SetSelected(current);

        ui.CreateToggle(panel, "Loop the animation", _editor.Loop, on =>
        {
            _editor.Loop = on;
            _editor.Preview.Play(_editor.Animation, on, _editor.Speed);
        });

        ui.CreateSlider(panel, "Animation speed", 0.1f, 3f, _editor.Speed, v =>
        {
            _editor.Speed = v;
            _editor.Preview.Play(_editor.Animation, _editor.Loop, v);
        });
    }

    // ---- colour sets --------------------------------------------------------------------------

    private void BuildColourSets(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Colour sets");

        var setCount = Document.ColourSetCount;
        var setNames = new List<string>();
        for (var i = 0; i < setCount; i++) setNames.Add("Set " + (i + 1));

        var picker = ui.CreateDropdown(panel, "Set " + (_editor.ColourSet + 1), setNames, (index, _) =>
        {
            if (index == _editor.ColourSet) return;
            _editor.ColourSet = index;
            Touch($"Colour set {index + 1} is showing on the preview.");
            _editor.RebuildPanels();
        });
        picker.SetSelected(Mathf.Clamp(_editor.ColourSet, 0, setCount - 1));

        ui.CreateButton(panel, "Add colour set", () =>
        {
            foreach (var part in Config.PartConfigs.Values)
            {
                part.ColorChoices ??= [];
                part.ColorChoices.Add(part.ColorChoices.Count > 0 ? part.ColorChoices[^1] : "#FFFFFF");
            }

            _editor.ColourSet = Document.ColourSetCount - 1;
            Touch($"Colour set {Document.ColourSetCount} added; it starts as a copy of the last one.");
            _editor.RebuildPanels();
        }, 36f);

        if (setCount <= 1) return;

        ui.CreateButton(panel, "Remove last colour set", () =>
        {
            foreach (var part in Config.PartConfigs.Values)
                if (part.ColorChoices is { Count: > 1 }) part.ColorChoices.RemoveAt(part.ColorChoices.Count - 1);

            _editor.ColourSet = Mathf.Min(_editor.ColourSet, Document.ColourSetCount - 1);
            Touch($"Colour set {setCount} removed.");
            _editor.RebuildPanels();
        }, 36f, MapEditorEmphasis.Quiet);
    }

    // ---- layers -------------------------------------------------------------------------------

    private const float GridHeight = 360f;

    private void BuildLayers(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Layers");

        var covered = new HashSet<string>(
            Config.PartConfigs.Values.Where(p => p != null).Select(p => p.PartName ?? ""),
            StringComparer.OrdinalIgnoreCase);

        var free = SkinParts.Of(Config.OverrideBaseSkin)
            .Where(p => !covered.Contains(p.Part))
            .ToList();

        var options = new List<string> { AddSlot };
        options.AddRange(free.Select(p => p.Label));

        var picker = ui.CreateDropdown(panel, AddSlot, options, (index, _) =>
        {
            if (index <= 0 || index - 1 >= free.Count) return;
            AddLayer(free[index - 1]);
        });
        picker.SetSelected(0);

        var keys = Config.PartConfigs.Keys.ToList();
        if (keys.Count == 0)
        {
            var empty = ui.CreateLabel(panel, "Pick a slot to override", 16);
            MapEditorUI.FitLabelHeight(empty);
            return;
        }

        if (_editor.SelectedLayer == null || !Config.PartConfigs.ContainsKey(_editor.SelectedLayer))
            _editor.SelectedLayer = keys[0];

        var grid = ui.CreateIconGrid(panel, "LayerGrid", columns: 4, cellSize: 112f, scrollHeight: GridHeight);
        grid.ShowNames = true;

        foreach (var key in keys)
        {
            var localKey = key;
            var part = Config.PartConfigs[key];
            var icon = IconFor(key);

            grid.AddCell(key, part.PartName ?? key, icon, () =>
            {
                if (localKey == _editor.SelectedLayer) return;
                _editor.SelectedLayer = localKey;
                _editor.RebuildInspector();
            });

            if (icon == null) grid.SetCellLetter(key, "no image");
        }

        grid.SetSelected(_editor.SelectedLayer);
    }

    private void AddLayer((int Slot, string Part, string Label) slot)
    {
        var key = SkinParts.FreeKey(Config, "color_" + slot.Part.ToLowerInvariant());

        Config.PartConfigs[key] = new FollowerSkinPartConfig
        {
            SlotIndex = slot.Slot,
            PartName = slot.Part,
            ColorChoices = Enumerable.Repeat("#FFFFFF", Document.ColourSetCount).ToList()
        };

        _editor.SelectedLayer = key;
        Touch($"'{slot.Part}' is overridden now; pick its image on the right.");
        _editor.RebuildPanels();
    }

    private Sprite IconFor(string key)
    {
        var texture = _editor.Preview.TextureFor(
            CTFollowerSkinSerialization.ImagePath(Document.SkinName, Document.VariantName, key));
        if (texture == null) return null;

        var id = texture.GetInstanceID();
        if (_sprites.TryGetValue(id, out var sprite) && sprite != null && sprite.texture == texture) return sprite;

        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        _sprites[id] = sprite;
        return sprite;
    }

    private void Touch(string message)
    {
        _editor.Refresh();
        if (message != null) _editor.SetStatus(message);
    }

    public void OnEnter() =>
        _editor.SetStatus("Each layer is one part of the follower: an image that replaces it, a colour, or both.");

    public void OnExit() => _pendingLoad = null;

    public void OnUpdate()
    {
        if (!_refreshQueued) return;

        _refreshQueued = false;
        RefreshPairs();
    }
}
