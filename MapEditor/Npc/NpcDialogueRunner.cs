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

        // A conversation already owns the screen (and the player's input); starting a second
        // one underneath it would corrupt both.
        if (MMConversation.isPlaying) return;
        if (IsRunning && !IsStale()) return;

        npc.Dialogue.EnsureRegistered(npc);

        IsRunning = true;
        PlayNode(npc, speaker, npc.Dialogue.Start);
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

        var entries = BuildEntries(npc, speaker, node);
        if (entries.Count == 0)
        {
            // A lines-less choice hub still needs one entry for the wheel to hang off; ended
            // instead - Validate should have removed such nodes already.
            EndConversation(npc, node.Id);
            return;
        }

        ConversationObject conversation;

        if (node.Choices != null)
        {
            // Fully qualified: a legacy top-level Response class also exists in the game
            // assembly, and it is the wrong one.
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
            // No editor host in this scene (should not happen in dungeons); play directly and
            // accept the teardown race rather than dropping the branch.
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

    // The one true teardown. Because every Play passed CallOnConversationEnd: false, the
    // letterbox, camera and player input are still in conversation mode until this runs.
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
