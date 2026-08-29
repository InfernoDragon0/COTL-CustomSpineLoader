using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public interface IMapEditorHost
{
    bool ModalOpen { get; set; }
    void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info);
    Coroutine StartCoroutine(IEnumerator routine);

    void ShowHoverStatus(string message);
    void ClearHoverStatus();

    void RegisterUiBlocker(RectTransform rect);

    void BlockWorldClicks();

    void RequestOptionsResize();
}

public interface IMapEditorTool
{
    string Name { get; }

    void BuildPanel(RectTransform panel, MapEditorUI ui);

    void OnEnter();
    void OnExit();
    void OnUpdate();
}

public interface IMapEditorShortcuts
{
    IEnumerable<(string Key, string Action)> Shortcuts { get; }
}

public interface IMapEditorEscapeHandler
{
    bool HandleEscape();
}

public interface IMapEditorScreenTool
{
    bool OwnsScreen { get; }
    void ScreenQuickSave();

    void ScreenHoverStatus(string message);

    bool ScreenStepBack();
}
