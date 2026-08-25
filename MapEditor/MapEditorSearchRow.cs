using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// A "search by name" field above a tool's icon grid.
//
// Text entry rides RuntimeMapEditor.PromptText: Input.inputString with the EventSystem suspended
// and the game's UI navigator locked, because a TMP_InputField on the editor's own canvas loses a
// fight with Rewired's input module and the game's navigator. See the Structure tool's chapter in
// the README for why that is, and what the E key used to do.
//
// The tool supplies two callbacks: one to show the matches for a query, one to put its ordinary
// view back. Everything about the typing, the label and the debounce lives here.
public class MapEditorSearchRow
{
    private readonly RuntimeMapEditor _editor;
    private readonly Action<string> _onSearch;
    private readonly Action _onRestore;
    private readonly string _placeholder;

    private readonly GameObject _button;
    private readonly TMP_Text _label;

    private string _query = "";
    private int _version;

    public string Query => _query;

    public bool Active => !string.IsNullOrWhiteSpace(_query);

    public MapEditorSearchRow(RuntimeMapEditor editor, MapEditorUI ui, Transform panel,
        Action<string> onSearch, Action onRestore, string placeholder = "Search by name...")
    {
        _editor = editor;
        _onSearch = onSearch;
        _onRestore = onRestore;
        _placeholder = placeholder;

        _button = ui.CreateButton(panel, "", Begin, 40f);
        _label = _button != null ? _button.GetComponentInChildren<TMP_Text>() : null;
        Paint();
    }

    // Clicking a result is a way of saying "this one", so it ends the search the way Enter does -
    // otherwise picking something meant confirming first and then clicking it.
    public void Confirm()
    {
        _editor.ConfirmPrompt();
        Paint();
    }

    public void Clear()
    {
        _query = "";
        _version++;
        Paint();
    }

    private void Paint()
    {
        if (_label == null) return;

        var typing = _editor.IsTyping;
        _label.text = string.IsNullOrEmpty(_query)
            ? (typing ? "Search:  _" : _placeholder)
            : "Search:  " + _query + (typing ? "_" : "");
    }

    private void Begin()
    {
        _editor.PromptText("search", _query,
            onDone: _ => Paint(),
            onChanged: query =>
            {
                _query = query;
                Paint();

                // Debounced through a coroutine rather than the tool's OnUpdate, which does not run
                // while a prompt is open - the editor returns from Update as soon as it has read the
                // keystroke. Without the delay every letter would start and cancel a screenful of
                // async icon loads.
                _version++;
                _editor.StartCoroutine(ApplySoon(_version));
            },
            onCancelled: () =>
            {
                _query = "";
                _version++;
                Paint();
                Restore();
            },
            inTitle: false);
    }

    private IEnumerator ApplySoon(int version)
    {
        var until = Time.unscaledTime + 0.18f;
        while (Time.unscaledTime < until) yield return null;

        // A later keystroke has already asked for its own pass.
        if (version != _version) yield break;

        if (string.IsNullOrWhiteSpace(_query)) Restore();
        else Search(_query.Trim());
    }

    private void Search(string query)
    {
        try
        {
            _onSearch?.Invoke(query);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: search failed: " + e);
        }
    }

    private void Restore()
    {
        try
        {
            _onRestore?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: restoring the browser failed: " + e);
        }
    }

    // How many hits a grid is filled with before the rest are left to a narrower query: the cells
    // are real objects with async icons behind them, and a thousand of them is a stall.
    public const int MaxResults = 60;

    public void ReportCount(int shown, int total, string query)
    {
        _editor.SetStatus(total == 0
            ? $"Nothing matches '{query}'."
            : total > shown
                ? $"{total} match '{query}' - showing the first {shown}. Keep typing to narrow it."
                : $"{total} match '{query}'.");
    }
}
