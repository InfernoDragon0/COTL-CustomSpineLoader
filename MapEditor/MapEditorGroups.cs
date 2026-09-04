using System;
using System.Collections.Generic;
using System.Linq;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

/// <summary>
/// Marks a selectable root as a member of an editor group. The hierarchy is left alone on purpose:
/// shapes live under the room's composite collider, enemies under their containment, and reparenting
/// any of them to a group object would break the game's own handling. A group is only this tag, and
/// the Select tool widening a selection to every object that carries the same id.
/// </summary>
public class CTEditorGroup : MonoBehaviour
{
    public string GroupId = "";
    public string GroupName = "";
}

[Serializable]
public class MapGroupData
{
    public string Id = "";
    public string Name = "";
    public List<MapGroupMemberData> Members = [];
}

[Serializable]
public class MapGroupMemberData
{
    public string Name = "";
    public SerializableVector3 Position;
}

public static class MapEditorGroups
{
    // Objects have no identity that survives a save, so a saved member is found again by its cleaned
    // name and its position; this is how far apart the two may be and still count.
    private const float MatchDistance = 0.05f;

    public static string GroupOf(GameObject root)
    {
        if (root == null) return null;
        var tag = root.GetComponent<CTEditorGroup>();
        return tag != null && !string.IsNullOrEmpty(tag.GroupId) ? tag.GroupId : null;
    }

    public static string NameOf(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return null;
        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorGroup>())
            if (tag.GroupId == groupId) return string.IsNullOrEmpty(tag.GroupName) ? "Group" : tag.GroupName;
        return null;
    }

    public static List<GameObject> Members(string groupId)
    {
        var members = new List<GameObject>();
        if (string.IsNullOrEmpty(groupId)) return members;

        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorGroup>())
            if (tag.GroupId == groupId && tag.gameObject != null) members.Add(tag.gameObject);

        return members;
    }

    /// <summary>
    /// The selection a click on any of <paramref name="objects"/> means: each object, plus every other
    /// member of any group it is in. Distinct, nulls dropped, first-seen order kept.
    /// </summary>
    public static List<GameObject> Expand(IEnumerable<GameObject> objects)
    {
        var result = new List<GameObject>();
        var seen = new HashSet<GameObject>();
        var groupsDone = new HashSet<string>();

        foreach (var go in objects ?? [])
        {
            if (go == null || !seen.Add(go)) continue;
            result.Add(go);

            var id = GroupOf(go);
            if (id == null || !groupsDone.Add(id)) continue;

            foreach (var member in Members(id))
                if (seen.Add(member)) result.Add(member);
        }

        return result;
    }

    public static string Create(IEnumerable<GameObject> members, string name = null, string id = null)
    {
        id ??= Guid.NewGuid().ToString("N").Substring(0, 8);
        name ??= NextName();

        foreach (var go in members)
        {
            if (go == null) continue;
            var tag = go.GetComponent<CTEditorGroup>() ?? go.AddComponent<CTEditorGroup>();
            tag.GroupId = id;
            tag.GroupName = name;
        }

        return id;
    }

    public static void Dissolve(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return;
        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorGroup>())
            if (tag.GroupId == groupId) UnityEngine.Object.Destroy(tag);
    }

    private static string NextName()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorGroup>()) taken.Add(tag.GroupName ?? "");

        for (var n = 1; n < 1000; n++)
            if (!taken.Contains("Group " + n)) return "Group " + n;

        return "Group";
    }

    /// Every live group with at least two members, as (id, name, members), members in a stable order.
    public static List<(string Id, string Name, List<GameObject> Members)> All()
    {
        var byId = new Dictionary<string, (string Name, List<GameObject> Members)>();

        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorGroup>())
        {
            if (tag == null || string.IsNullOrEmpty(tag.GroupId)) continue;
            if (!byId.TryGetValue(tag.GroupId, out var entry))
                byId[tag.GroupId] = entry = (tag.GroupName ?? "", []);
            entry.Members.Add(tag.gameObject);
        }

        var list = new List<(string, string, List<GameObject>)>();
        foreach (var pair in byId)
        {
            if (pair.Value.Members.Count < 2) continue;
            pair.Value.Members.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            list.Add((pair.Key, pair.Value.Name, pair.Value.Members));
        }

        list.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static string DisplayName(GameObject go)
    {
        if (go == null) return "(unnamed)";
        var clean = (go.name ?? "").Replace("(Clone)", "").Trim();
        return clean.Length > 0 ? clean : go.name;
    }

    // ---- saving and loading --------------------------------------------------------------------

    public static List<MapGroupData> Capture()
    {
        var list = new List<MapGroupData>();
        foreach (var (id, name, members) in All())
        {
            var data = new MapGroupData { Id = id, Name = name };
            foreach (var go in members)
                data.Members.Add(new MapGroupMemberData
                {
                    Name = DisplayName(go),
                    Position = MapEditorSerialization.V3(go.transform.position)
                });
            list.Add(data);
        }

        return list;
    }

    /// <summary>
    /// Puts saved groups back on the objects a load has just placed. Runs after everything is in the
    /// room, matches each member by name and position, and drops any group that ends up with fewer
    /// than two members found. Safe to call more than once: already-tagged objects are skipped.
    /// </summary>
    public static int Restore(CTNodeBlueprint map)
    {
        if (map?.Groups == null || map.Groups.Count == 0) return 0;

        var roots = SelectTool.AllSelectableRoots();
        var free = roots.Where(r => GroupOf(r) == null).ToList();
        var live = new HashSet<string>(All().Select(g => g.Id));
        var restored = 0;

        foreach (var data in map.Groups)
        {
            if (data?.Members == null || data.Members.Count < 2) continue;

            // The editor reopens many times in one session; a group that is already on its objects
            // is left alone rather than reported as unmatched.
            if (!string.IsNullOrEmpty(data.Id) && live.Contains(data.Id)) continue;

            var found = new List<GameObject>();
            foreach (var member in data.Members)
            {
                var wanted = MapEditorSerialization.ToVector3(member.Position);
                GameObject best = null;
                var bestDistance = float.MaxValue;

                foreach (var candidate in free)
                {
                    if (found.Contains(candidate)) continue;
                    if (!string.Equals(DisplayName(candidate), member.Name, StringComparison.Ordinal)) continue;

                    var distance = Vector3.Distance(candidate.transform.position, wanted);
                    if (distance > MatchDistance || distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = candidate;
                }

                if (best != null) found.Add(best);
            }

            if (found.Count < 2)
            {
                Plugin.Log.LogInfo($"MapEditor: group '{data.Name}' could not be restored - " +
                                   $"{found.Count} of {data.Members.Count} member(s) found.");
                continue;
            }

            Create(found, string.IsNullOrEmpty(data.Name) ? null : data.Name,
                string.IsNullOrEmpty(data.Id) ? null : data.Id);
            foreach (var go in found) free.Remove(go);
            restored++;
        }

        return restored;
    }
}
