using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

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
