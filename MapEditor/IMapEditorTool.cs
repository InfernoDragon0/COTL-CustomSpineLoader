using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// What a modal dialog needs from whoever hosts it: a way to block that host's input, a status
// line, and a coroutine runner. RuntimeMapEditor always had all three; the world map editor is
// the second host, and the shared dialogs (MapNamePrompt) talk to this instead of to either.
public interface IMapEditorHost
{
    bool ModalOpen { get; set; }
    void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info);
    Coroutine StartCoroutine(IEnumerator routine);
}

public interface IMapEditorTool
{
    string Name { get; }

    // Build this tool's options into the supplied panel. Called once, when the editor UI is created.
    void BuildPanel(RectTransform panel, MapEditorUI ui);

    void OnEnter();
    void OnExit();
    void OnUpdate();
}

public interface IMapEditorShortcuts
{
    IEnumerable<(string Key, string Action)> Shortcuts { get; }
}
