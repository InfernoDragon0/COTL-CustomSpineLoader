using System;
using System.Collections.Generic;

namespace CustomSpineLoader.MapEditor;

// One undo stack for the whole editor.
//
// Every tool used to carry its own "Undo Last X" button, which meant remembering which tool had
// placed the thing you regretted before you could take it back. Tools now push an entry as they
// place, and Ctrl+Z walks the single history regardless of which tool is active.
//
// Entries return false when the thing they would undo has already gone - cleared, loaded over,
// or destroyed by another tool - and the stack simply moves on to the next one.
public class MapEditorHistory
{
    private class Entry
    {
        public string Description;
        public Func<bool> Undo;
    }

    // Deep enough to cover a long authoring session, bounded so a runaway loop cannot grow it
    // without limit.
    private const int MaxEntries = 256;

    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    public void Push(string description, Func<bool> undo)
    {
        if (undo == null) return;

        _entries.Add(new Entry { Description = description, Undo = undo });
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
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
            return true;
        }

        return false;
    }

    // A load or a full clear invalidates everything the stack refers to.
    public void Clear() => _entries.Clear();
}
