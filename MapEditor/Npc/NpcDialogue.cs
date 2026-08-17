using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using I2.Loc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomSpineLoader.MapEditor.Npc;

// The JSON dialogue schema a custom NPC carries in its config.json, plus validation and the
// localization plumbing that makes the text actually render.
//
// The game's conversation UI only draws LOCALIZED text: MMConversation.UpdateText runs every
// line through LocalizationManager.GetTranslation, and a term that does not exist comes back
// null - raw strings never appear. So every line, choice and character name here is registered
// as a real I2 term at load time (AddTerm + SetTranslation on language 0), which is also the
// game's own runtime idiom (ConversationEntry.AddTerm). English fills slot 0 and I2's fallback
// serves it to every other language.
[Serializable]
public class NpcDialogue
{
    // Node id the conversation opens with.
    public string Start = "";
    public List<NpcDialogueNode> Nodes = [];

    public NpcDialogueNode FindNode(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var node in Nodes)
            if (node != null && node.Id == id) return node;
        return null;
    }

    // Normalizes the tree so the runner never has to defend itself:
    //  - Start must resolve (falls back to the first node);
    //  - every Next must resolve or be null;
    //  - a node offers 0 or exactly 2 choices - the game's DialogueWheel is a fixed two-option
    //    serialized array, one response throws, three render as two. Offending nodes keep their
    //    lines and lose their choices.
    // Cycles are legal (a hub node that loops back is the normal shape).
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

    // Registration at plugin Awake is not enough on its own: the game (re)builds its I2 language
    // source while loading, and terms registered before that ended up displaying as their raw
    // keys. This runs at the start of every conversation and re-registers only when the terms
    // are actually missing from the live source, so the one UpdateDictionary rebuild it costs is
    // paid at most once per source rebuild.
    public void EnsureRegistered(CustomNpc npc)
    {
        try
        {
            // The translation itself is the test, not the TermData: a term can exist with its
            // translation missing for the CURRENT language, and this game's source is configured
            // to answer that with the term string itself (MissingTranslationAction.ShowTerm) -
            // which is exactly the placeholder-key text on screen.
            if (!string.IsNullOrEmpty(NameTerm))
            {
                var translation = LocalizationManager.GetTranslation(NameTerm);
                if (!string.IsNullOrEmpty(translation) && translation != NameTerm) return;
            }
        }
        catch (Exception)
        {
            // Source not ready to answer; fall through and (re)register.
        }

        RegisterTerms(npc);
    }

    // Registers every string as an I2 term and remembers the generated term keys on the nodes.
    // Idempotent: AddTerm returns the existing TermData when the term already exists, so a
    // re-register (or two NPCs with the same name) only rewrites the same translations.
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

        // One dictionary rebuild for the whole NPC, not one per term.
        source.UpdateDictionary();
    }

    private static string Register(LanguageSourceData source, string term, string text)
    {
        try
        {
            var data = source.AddTerm(term, eTermType.Text);

            // Sized when the term was first created; a source whose language list grew since
            // would index out of range below.
            var languageCount = Math.Max(1, source.mLanguages?.Count ?? 1);
            if (data.Languages == null || data.Languages.Length < languageCount)
                Array.Resize(ref data.Languages, languageCount);

            // EVERY language slot, not just slot 0: the source resolves by the current
            // language's index and answers a missing translation with the raw term
            // (MissingTranslationAction.ShowTerm) rather than falling back - filling slot 0
            // alone is why the dialogue showed its keys. Custom NPC text has no translations,
            // so the same string in every slot IS the correct content.
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

    // The registered term for the NPC's display name; set by RegisterTerms.
    [NonSerialized] public string NameTerm;
}

[Serializable]
public class NpcDialogueNode
{
    public string Id = "";
    public List<NpcDialogueLine> Lines = [];

    // Node to continue into when the last line finishes and there are no choices; null ends the
    // conversation.
    public string Next;

    // Exactly 2 or absent (enforced by Validate). Shown after the node's LAST line has finished
    // typing - the game renders choices only at the end of a conversation, which is why each
    // node is its own MMConversation.
    public List<NpcDialogueChoice> Choices;
}

// One spoken line. In JSON either a bare string ("Hello.") or an object
// ({ "Text": "Hello.", "Animation": "cheer1", "Loop": false }) - the converter below accepts
// both, so existing configs keep working.
[Serializable]
[JsonConverter(typeof(NpcDialogueLineConverter))]
public class NpcDialogueLine
{
    public string Text = "";

    // Spine animation played while this line types; empty falls back to the NPC's TalkAnimation.
    public string Animation = "";

    // true = the animation loops for the whole line; false = it plays once, then the skeleton
    // drops back to the NPC's idle (the game queues the default animation behind a one-shot).
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

    // Reading is the whole job (configs are authored by hand); letting the default writer run
    // through this converter again would recurse.
    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, NpcDialogueLine value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[Serializable]
public class NpcDialogueChoice
{
    public string Id = "";
    public string Text = "";

    // Node this choice branches to; null ends the conversation.
    public string Next;

    [NonSerialized] public string Term;
}
