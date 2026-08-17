using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using MMTools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Npc;

// Drives a custom NPC's dialogue tree through the game's own conversation system.
//
// One dialogue NODE = one MMConversation. That granularity is forced by the game: the choice
// wheel only appears after a conversation's LAST line has finished typing, so a mid-tree branch
// has to end its conversation and start the next one from the response callback. Every Play here
// therefore passes CallOnConversationEnd: false, and the letterbox/camera/input teardown
// (GameManager.OnConversationEnd) runs exactly once, when the chain truly ends.
//
// Callback rules learned from the decompiled game: ConversationEntry.Callback is a UnityEvent
// whose runtime listeners NEVER fire (MMConversation only invokes it when its persistent count
// is non-zero), so everything routes through ConversationObject.CallBack (an Action) and
// Response.ActionCallBack.
public static class NpcDialogueRunner
{
    // True from the first line until the chain truly ends. MMConversation.isPlaying is NOT a
    // substitute: each dialogue node is its own conversation, so between nodes it drops to false
    // for a frame or two - long enough for a trigger sequence waiting on it to decide the chat was
    // over and carry on talking over itself.
    public static bool IsRunning { get; private set; }

    private static float _lastNodeAt;

    // A chain lives across frames on the editor's coroutine host, so a room reload can kill it
    // mid-flight and leave IsRunning stuck - which would then refuse every future dialogue.
    // Nothing on screen and no node started for ten seconds means the chain is gone, not slow:
    // a node the player is reading keeps MMConversation.isPlaying true the whole time, and the
    // gap between nodes is a frame or two.
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

        // The game rebuilds its language source during load, which wipes terms registered at
        // plugin Awake - re-registered here whenever they are found missing, or the bubbles
        // show raw term keys instead of the dialogue.
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

    // A response callback fires while the previous conversation is still tearing itself down
    // (DoClose invokes it just before isPlaying drops), so the next node cannot Play
    // immediately - it waits a frame on the editor host, which lives in every dungeon scene.
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

            // Per-line animation, falling back to the NPC's talk animation. The game guards
            // every name with FindAnimation before playing, so a typo is a static pose, not an
            // exception.
            var animation = string.IsNullOrEmpty(line.Animation) ? npc.TalkAnimation : line.Animation;

            entries.Add(new ConversationEntry(speaker, line.Term, animation)
            {
                CharacterName = npc.Dialogue.NameTerm ?? "-",
                LoopAnimation = line.Loop,
                // Only one-shot lines get a default queued behind them. The game queues
                // DefaultAnimation unconditionally when it is set, and Spine starts a queued
                // animation after ONE CYCLE of a looping predecessor - so a looping line with
                // idle as its default played a single loop and stopped, which read as the Loop
                // flag doing nothing. Empty means nothing is queued and the loop holds until
                // the next line changes it.
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
