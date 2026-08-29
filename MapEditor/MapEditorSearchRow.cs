using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

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
