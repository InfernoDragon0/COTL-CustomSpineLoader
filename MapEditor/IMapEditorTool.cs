using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// What a modal dialog needs from whoever hosts it: a way to block that host's input, a status
// line, and a coroutine runner. RuntimeMapEditor always had all three; the world map editor is
// the second host, and the shared dialogs (MapNamePrompt) talk to this instead of to either.
//
// The rest is what the shared WIDGETS need, and it is here for the same reason. MapEditorUI used
// to name the two hosts outright - MapEditorHover reached for RuntimeMapEditor.Active or
// WorldMapEditor.Instance to show a line, AttachButton for the same two to shut the world out for
// a moment - so a third host got plates that lit up and said nothing, and dropdowns whose blocker
// rects went nowhere. Routing both through the host the UI was attached to costs nothing and means
// the next editor works by being built, not by being added to a list.
public interface IMapEditorHost
{
    bool ModalOpen { get; set; }
    void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info);
    Coroutine StartCoroutine(IEnumerator routine);

    // A hovered widget's line, and the cursor leaving it.
    void ShowHoverStatus(string message);
    void ClearHoverStatus();

    // A rect the host's own pointer polling must treat as "not the world".
    void RegisterUiBlocker(RectTransform rect);

    // A widget was pressed: whatever is under it must not read that as a click too.
    void BlockWorldClicks();

    // Something in the options panel changed size and the layout needs another pass.
    void RequestOptionsResize();
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

// A tool that sometimes wants Escape for itself - cancelling a pick, stepping out of a mode it put
// the editor into. Escape closes the editor, so the host asks the active tool first: a key that
// means "back out of this" has to back out of the innermost thing before it reaches the outermost.
// Return true when the gesture was used, and the editor stays open.
public interface IMapEditorEscapeHandler
{
    bool HandleEscape();
}

// A tool that takes the whole screen for itself - the dungeon map view, which is a map screen and
// not a panel. While it owns the screen the host's own chrome stands down, the camera keys stop
// panning the room behind it, the wheel stops switching tools, and Ctrl+S saves what is on screen
// rather than the room.
public interface IMapEditorScreenTool
{
    bool OwnsScreen { get; }
    void ScreenQuickSave();

    // A hovered widget's line, routed here because the host's status bar is switched off behind
    // this tool's screen. Null means the cursor left and the bar goes back to what it was saying.
    void ScreenHoverStatus(string message);

    // One step back out of whatever this tool has on screen - a prompt, then the screen itself.
    // True when it handled the gesture, so F4 closes the tool's screen before it closes the editor
    // and cannot throw away unsaved work on the way past.
    bool ScreenStepBack();
}
