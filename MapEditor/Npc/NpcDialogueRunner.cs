using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using MMTools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Npc;

public static class NpcDialogueRunner
{
    public static bool IsRunning { get; private set; }

    private static float _lastNodeAt;

    private static bool IsStale()
    {
        if (MMConversation.isPlaying) return false;
        if (Time.unscaledTime - _lastNodeAt < 10f) return false;

        Plugin.Log.LogWarning("Custom NPC dialogue was left running by an interrupted chain; resetting.");
        IsRunning = false;
        return true;
    }

    public static void Play(CustomNpc npc, UnityEngine.GameObject speaker)
    {
        if (npc?.Dialogue == null || speaker == null) return;

        if (MMConversation.isPlaying) return;
        if (IsRunning && !IsStale()) return;

        npc.Dialogue.EnsureRegistered(npc);

        try
        {
            APIHelper.NpcQuests.QuestRuntime.NoteTalkedTo(npc.InternalName);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom NPC quests: noting the conversation failed: " + e.Message);
        }

        IsRunning = true;
        PlayNode(npc, speaker, ChooseStart(npc));
    }

    /// Picks the way in: the first entry whose conditions all hold, else the plain start node.
    private static string ChooseStart(CustomNpc npc)
    {
        var entries = npc.Dialogue.Entry;
        if (entries == null) return npc.Dialogue.Start;

        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Node)) continue;
            if (npc.Dialogue.FindNode(entry.Node) == null) continue;
            if (!Holds(npc, entry)) continue;

            return entry.Node;
        }

        return npc.Dialogue.Start;
    }

    private static bool Holds(CustomNpc npc, NpcDialogueEntry entry) =>
        Is(npc, entry.QuestNotStarted, APIHelper.NpcQuests.QuestStatus.NotStarted) &&
        Is(npc, entry.QuestActive, APIHelper.NpcQuests.QuestStatus.Active) &&
        Is(npc, entry.QuestReady, APIHelper.NpcQuests.QuestStatus.Ready) &&
        Is(npc, entry.QuestDone, APIHelper.NpcQuests.QuestStatus.Done) &&
        Is(npc, entry.QuestFailed, APIHelper.NpcQuests.QuestStatus.Failed);

    private static bool Is(CustomNpc npc, string idOrKey, APIHelper.NpcQuests.QuestStatus wanted)
    {
        if (string.IsNullOrWhiteSpace(idOrKey)) return true;

        var quest = APIHelper.NpcQuests.QuestRegistry.Resolve(npc.InternalName, idOrKey);
        if (quest == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': dialogue asks about quest " +
                                  $"'{idOrKey}', which nothing declares.");
            return false;
        }

        return APIHelper.NpcQuests.QuestRuntime.State(quest.Key) == wanted;
    }

    /// The four things a node or an answer can do to a quest.
    private static void QuestActions(CustomNpc npc, string give, string turnIn, string abandon,
        string flag)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(flag)) APIHelper.NpcQuests.QuestRuntime.NoteFlag(flag);

            Act(npc, give, APIHelper.NpcQuests.QuestRuntime.Accept);
            Act(npc, turnIn, APIHelper.NpcQuests.QuestRuntime.TurnIn);
            Act(npc, abandon, APIHelper.NpcQuests.QuestRuntime.Abandon);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': a quest action failed: " + e.Message);
        }
    }

    private static void Act(CustomNpc npc, string idOrKey,
        Func<APIHelper.NpcQuests.QuestDefinition, bool> action)
    {
        if (string.IsNullOrWhiteSpace(idOrKey)) return;

        var quest = APIHelper.NpcQuests.QuestRegistry.Resolve(npc.InternalName, idOrKey);
        if (quest == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': dialogue names quest " +
                                  $"'{idOrKey}', which nothing declares.");
            return;
        }

        action(quest);
    }

    private static void PlayNode(CustomNpc npc, UnityEngine.GameObject speaker, string nodeId)
    {
        _lastNodeAt = Time.unscaledTime;

        var node = npc.Dialogue.FindNode(nodeId);
        if (node == null)
        {
            EndConversation(npc, nodeId);
            return;
        }

        try
        {
            npc.OnDialogueNode(node.Id);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}' OnDialogueNode failed: {e.Message}");
        }

        QuestActions(npc, node.GiveQuest, node.TurnInQuest, node.AbandonQuest, node.SetFlag);

        var entries = BuildEntries(npc, speaker, node);
        if (entries.Count == 0)
        {
            // A node with no words has already done whatever it was for; carry on if it points
            // somewhere, otherwise the conversation is over.
            Continue(npc, speaker, node.Id, node.Next);
            return;
        }

        ConversationObject conversation;

        if (node.Choices != null)
        {
            var responses = new List<MMTools.Response>(2);
            for (var i = 0; i < node.Choices.Count; i++)
            {
                var choice = node.Choices[i];
                var index = i;
                responses.Add(new MMTools.Response(choice.Term, () =>
                {
                    try
                    {
                        npc.OnDialogueChoice(node.Id, index, choice.Id);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}' OnDialogueChoice failed: {e.Message}");
                    }

                    QuestActions(npc, choice.GiveQuest, choice.TurnInQuest, choice.AbandonQuest,
                        choice.SetFlag);

                    Continue(npc, speaker, node.Id, choice.Next);
                }, choice.Term));
            }

            conversation = new ConversationObject(entries, responses, null);
        }
        else
        {
            conversation = new ConversationObject(entries, null,
                () => Continue(npc, speaker, node.Id, node.Next));
        }

        MMConversation.Play(conversation, CallOnConversationEnd: false);
    }

    private static void Continue(CustomNpc npc, UnityEngine.GameObject speaker, string fromNodeId, string nextId)
    {
        if (string.IsNullOrEmpty(nextId))
        {
            EndConversation(npc, fromNodeId);
            return;
        }

        var host = RuntimeMapEditor.Active;
        if (host != null)
        {
            host.StartCoroutine(ContinueNextFrame(npc, speaker, fromNodeId, nextId));
        }
        else
        {
            PlayNode(npc, speaker, nextId);
        }
    }

    private static System.Collections.IEnumerator ContinueNextFrame(CustomNpc npc,
        UnityEngine.GameObject speaker, string fromNodeId, string nextId)
    {
        while (MMConversation.isPlaying) yield return null;
        yield return null;

        if (speaker == null)
        {
            EndConversation(npc, fromNodeId);
            yield break;
        }

        PlayNode(npc, speaker, nextId);
    }

    private static List<ConversationEntry> BuildEntries(CustomNpc npc, UnityEngine.GameObject speaker,
        NpcDialogueNode node)
    {
        var entries = new List<ConversationEntry>(node.Lines.Count);
        foreach (var line in node.Lines)
        {
            if (line == null || string.IsNullOrEmpty(line.Term)) continue;

            var animation = string.IsNullOrEmpty(line.Animation) ? npc.TalkAnimation : line.Animation;

            entries.Add(new ConversationEntry(speaker, line.Term, animation)
            {
                CharacterName = npc.Dialogue.NameTerm ?? "-",
                LoopAnimation = line.Loop,
                DefaultAnimation = line.Loop ? "" : npc.IdleAnimation
            });
        }

        return entries;
    }

    private static void EndConversation(CustomNpc npc, string lastNodeId)
    {
        IsRunning = false;

        try
        {
            GameManager.GetInstance()?.OnConversationEnd();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom NPC conversation teardown failed: " + e.Message);
        }

        try
        {
            npc.OnDialogueEnded(lastNodeId);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}' OnDialogueEnded failed: {e.Message}");
        }
    }
}
