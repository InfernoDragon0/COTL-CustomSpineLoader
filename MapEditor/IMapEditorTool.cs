using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// One tool per editing mode. Exactly one tool is active at a time; the host calls OnEnter/OnExit
// as the user switches and OnUpdate every frame while active.
public interface IMapEditorTool
{
    string Name { get; }

    // Build this tool's options into the supplied panel. Called once, when the editor UI is created.
    void BuildPanel(RectTransform panel, MapEditorUI ui);

    void OnEnter();
    void OnExit();
    void OnUpdate();
}
