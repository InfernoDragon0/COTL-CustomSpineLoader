using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using I2.Loc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomSpineLoader.MapEditor.Npc;

[Serializable]
public class NpcDialogue
{
    public string Start = "";
    public List<NpcDialogueNode> Nodes = [];

    public NpcDialogueNode FindNode(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var node in Nodes)
            if (node != null && node.Id == id) return node;
        return null;
    }

    public bool Validate(string npcName)
    {
        if (Nodes == null || Nodes.Count == 0)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npcName}': dialogue has no nodes.");
            return false;
        }

        Nodes.RemoveAll(n => n == null || string.IsNullOrEmpty(n.Id));

        if (FindNode(Start) == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npcName}': start node '{Start}' not found, " +
                                  $"using '{Nodes[0].Id}'.");
            Start = Nodes[0].Id;
        }

        foreach (var node in Nodes)
        {
            node.Lines ??= [];
            node.Lines.RemoveAll(l => l == null || string.IsNullOrWhiteSpace(l.Text));

            if (!string.IsNullOrEmpty(node.Next) && FindNode(node.Next) == null)
            {
                Plugin.Log.LogWarning($"Custom NPC '{npcName}': node '{node.Id}' points to missing " +
                                      $"node '{node.Next}'; it will end the conversation instead.");
                node.Next = null;
            }

            if (node.Choices == null || node.Choices.Count == 0)
            {
                node.Choices = null;
                continue;
            }

            node.Choices.RemoveAll(c => c == null || string.IsNullOrWhiteSpace(c.Text));

            if (node.Choices.Count != 2)
            {
                Plugin.Log.LogWarning($"Custom NPC '{npcName}': node '{node.Id}' has " +
                                      $"{node.Choices.Count} choice(s); the game's dialogue wheel " +
                                      "shows exactly 2, so its choices are dropped.");
                node.Choices = null;
                continue;
            }

            foreach (var choice in node.Choices)
            {
                if (string.IsNullOrEmpty(choice.Next) || FindNode(choice.Next) != null) continue;
                Plugin.Log.LogWarning($"Custom NPC '{npcName}': choice '{choice.Id}' points to missing " +
                                      $"node '{choice.Next}'; it will end the conversation instead.");
                choice.Next = null;
            }
        }

        Nodes.RemoveAll(n => n.Lines.Count == 0 && n.Choices == null);
        return FindNode(Start) != null;
    }

    // ---- localization ------------------------------------------------------------------------

    public void EnsureRegistered(CustomNpc npc)
    {
        try
        {
            if (!string.IsNullOrEmpty(NameTerm))
            {
                var translation = LocalizationManager.GetTranslation(NameTerm);
                if (!string.IsNullOrEmpty(translation) && translation != NameTerm) return;
            }
        }
        catch (Exception)
        {
        }

        RegisterTerms(npc);
    }

    public void RegisterTerms(CustomNpc npc)
    {
        LanguageSourceData source;
        try
        {
            source = LocalizationManager.Sources[0];
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom NPC '{npc.InternalName}': no localization source, " +
                                "dialogue text will not display: " + e.Message);
            return;
        }

        var prefix = "CultTweaker/NPC/" + npc.InternalName;

        NameTerm = Register(source, "CultTweaker/NAMES/" + npc.InternalName, npc.DisplayName);

        foreach (var node in Nodes)
        {
            for (var i = 0; i < node.Lines.Count; i++)
                node.Lines[i].Term = Register(source, $"{prefix}/{node.Id}/{i}", node.Lines[i].Text);

            if (node.Choices == null) continue;
            for (var i = 0; i < node.Choices.Count; i++)
                node.Choices[i].Term = Register(source, $"{prefix}/{node.Id}/Choice/{i}",
                    node.Choices[i].Text);
        }

        source.UpdateDictionary();
    }

    private static string Register(LanguageSourceData source, string term, string text)
    {
        try
        {
            var data = source.AddTerm(term, eTermType.Text);

            var languageCount = Math.Max(1, source.mLanguages?.Count ?? 1);
            if (data.Languages == null || data.Languages.Length < languageCount)
                Array.Resize(ref data.Languages, languageCount);

            for (var i = 0; i < data.Languages.Length; i++)
                data.SetTranslation(i, text ?? "");

            return term;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Localization term '{term}' failed to register: {e.Message}");
            return term;
        }
    }

    [NonSerialized] public string NameTerm;
}

[Serializable]
public class NpcDialogueNode
{
    public string Id = "";
    public List<NpcDialogueLine> Lines = [];

    public string Next;

    public List<NpcDialogueChoice> Choices;
}

[Serializable]
[JsonConverter(typeof(NpcDialogueLineConverter))]
public class NpcDialogueLine
{
    public string Text = "";

    public string Animation = "";

    public bool Loop = true;

    [NonSerialized] public string Term;
}

public class NpcDialogueLineConverter : JsonConverter<NpcDialogueLine>
{
    public override NpcDialogueLine ReadJson(JsonReader reader, Type objectType,
        NpcDialogueLine existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
            return new NpcDialogueLine { Text = (string)reader.Value };

        if (reader.TokenType != JsonToken.StartObject) return null;

        var obj = JObject.Load(reader);
        return new NpcDialogueLine
        {
            Text = (string)obj["Text"] ?? "",
            Animation = (string)obj["Animation"] ?? "",
            Loop = obj["Loop"]?.ToObject<bool>() ?? true
        };
    }

    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, NpcDialogueLine value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[Serializable]
public class NpcDialogueChoice
{
    public string Id = "";
    public string Text = "";

    public string Next;

    [NonSerialized] public string Term;
}
