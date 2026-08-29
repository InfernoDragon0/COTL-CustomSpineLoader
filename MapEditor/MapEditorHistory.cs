using System;
using System.Collections.Generic;

namespace CustomSpineLoader.MapEditor;

public class MapEditorHistory
{
    private class Entry
    {
        public string Description;
        public Func<bool> Undo;
    }

    private const int MaxEntries = 256;

    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    public Action Changed;

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: history change handler failed: " + e.Message);
        }
    }

    public void Push(string description, Func<bool> undo)
    {
        if (undo == null) return;

        _entries.Add(new Entry { Description = description, Undo = undo });
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        RaiseChanged();
    }

    public bool Undo(out string description)
    {
        description = null;

        while (_entries.Count > 0)
        {
            var entry = _entries[_entries.Count - 1];
            _entries.RemoveAt(_entries.Count - 1);

            bool undone;
            try
            {
                undone = entry.Undo();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: undo of '{entry.Description}' failed: {e.Message}");
                continue;
            }

            if (!undone) continue;

            description = entry.Description;
            RaiseChanged();
            return true;
        }

        return false;
    }

    public void Clear() => _entries.Clear();
}
