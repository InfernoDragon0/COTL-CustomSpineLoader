# Custom NPC quests

A custom NPC can hand out quests. They show up in the objectives panel on the right of the screen,
next to the game's own, with the same look, the same tick boxes and the same counters.

Everything lives in the NPC's `config.json`, next to its dialogue.

- [A first quest](#a-first-quest)
- [Quest fields](#quest-fields)
- [Goal types](#goal-types)
- [Naming content in a goal](#naming-content-in-a-goal)
- [Handing quests out in dialogue](#handing-quests-out-in-dialogue)
- [Different words for different states](#different-words-for-different-states)
- [Rewards](#rewards)
- [Where progress is saved](#where-progress-is-saved)
- [Limits](#limits)

## A first quest

```json
{
  "NpcName": "Bramble",
  "SkinName": "A_Tiger",

  "Quests": [
    {
      "Id": "firewood",
      "Title": "Wood for Bramble",
      "ReturnText": "Bring the wood back to Bramble",
      "Goals": [
        { "Type": "collectItem", "Target": "LOG", "Count": 10, "Text": "Gather logs", "Consume": true }
      ],
      "Reward": { "Items": [ { "Item": "GOLD_NUGGET", "Count": 5 } ] }
    }
  ],

  "Dialogue": {
    "Start": "greet",
    "Entry": [
      { "QuestReady": "firewood", "Node": "handin" },
      { "QuestActive": "firewood", "Node": "nag" },
      { "Node": "greet" }
    ],
    "Nodes": [
      {
        "Id": "greet",
        "Lines": [ { "Text": "My fire is dying. Would you fetch me wood?" } ],
        "Choices": [
          { "Id": "yes", "Text": "I will.", "Next": "taken", "GiveQuest": "firewood" },
          { "Id": "no",  "Text": "Find your own.", "Next": "bye" }
        ]
      },
      { "Id": "taken",  "Lines": [ { "Text": "Ten logs. I will count them." } ] },
      { "Id": "nag",    "Lines": [ { "Text": "Still no wood?" } ] },
      { "Id": "handin", "Lines": [ { "Text": "Wood! Take this gold." } ], "TurnInQuest": "firewood" },
      { "Id": "bye",    "Lines": [ { "Text": "Then walk safely." } ] }
    ]
  }
}
```

That is a whole quest: the NPC offers it, the panel counts the logs, the NPC takes them and pays.

## Quest fields

| Field | What it does |
| --- | --- |
| `Id` | Unique within this NPC. Dialogue names the quest by this. |
| `Title` | The header the quest gets in the panel. |
| `ReturnText` | The line shown once every goal is met. Defaults to "Return to \<NPC\>". |
| `TurnIn` | `true` (the default): the quest finishes when the player talks to the NPC again. `false`: it finishes the moment the last goal is met. |
| `Repeatable` | Lets the player take the quest again after finishing it. |
| `AutoTrack` | `true` (the default) pins the quest to the panel when it is accepted. The game shows at most three pinned groups and drops the oldest. |
| `ExpireSeconds` | Game seconds before the quest fails on its own. Omit or `0` for never. A quest with a timer gets the game's countdown wheel. |
| `Goals` | One or more lines of work. Every one has to be met. |
| `Reward` | What the player gets on completion. |

## Goal types

| `Type` | `Target` | What moves it |
| --- | --- | --- |
| `collectItem` | an item | The player holding that many. `Consume: true` takes them at hand-in. |
| `killEnemies` | an enemy | Kills of that enemy anywhere. |
| `buildStructure` | a structure | Structures of that type standing in the settlement. |
| `followers` | — | How many followers the cult has. |
| `completeDungeon` | a dungeon | Walking out of a run in that dungeon alive. Custom dungeons work. |
| `performRitual` | a ritual | Performing it. |
| `gameEvent` | one of the game's own quest events | The game reaching that beat. |
| `talkTo` | another custom NPC's internal name | Speaking to them. |
| `flag` | any name you choose | `CultTweakerApi.NoteQuestEvent("name")`, or a dialogue node's `SetFlag`. |

Goal fields:

| Field | What it does |
| --- | --- |
| `Text` | The line shown in the panel. When `Count` is 2 or more the game appends " 3 / 10" to it; when it is 1 the counter is hidden, so write the line as a whole sentence. |
| `Count` | How many. Default 1. |
| `Total` | `collectItem`, `buildStructure` and `followers` only. Counts what the player already has instead of only what they gather after taking the quest. |
| `Consume` | `collectItem` only. Takes the items at hand-in. |

The first four types are read back from the world rather than counted up, but only when the game
says that part of the world changed: the inventory was updated, an enemy died, a building went up or
came down, a follower joined or left. They are the same signals the game's own objectives listen to.
That means these goals also catch up on their own after a load, a cheat, or another mod handing the
player items, without anything running on a timer. The rest are counted as the game announces them.

`gameEvent` is the wide one: it listens to the same channel the game's own story quests use, so any
of its roughly two hundred beats can end a custom quest — `CookFirstMeal`, `CatchFish`,
`BuryBody`, `DeclareDoctrine`, `PlayFlockade` and so on. The names are the game's own; a wrong one
is reported in the log at load.

## Naming content in a goal

Vanilla names come first: `LOG`, `CRYSTAL`, `GOLD_NUGGET` for items, `Scamp` for enemies,
`Ritual_Feast` (or just `Feast`) for rituals, `Dungeon1_1` for dungeons, `BED_3` for structures.

Custom content is named the same way it is named everywhere else in CultTweaker: by its internal
name. A custom item, meal, enemy, structure or dungeon is found by name, and the number the game
gave it is looked up fresh each session. That is deliberate — those numbers are handed out per
install, in load order, so a saved number would mean a different thing on another machine or after
a mod is added. Nothing but names is ever written to the progress file.

## Handing quests out in dialogue

Four fields work on a dialogue node and on an answer:

| Field | What it does |
| --- | --- |
| `GiveQuest` | Hands the quest out. |
| `TurnInQuest` | Finishes it and pays the reward, if every goal is met. |
| `AbandonQuest` | Gives it up. The record goes away, so it can be offered again. |
| `SetFlag` | Raises a flag any `flag` goal is watching for. |

On an answer, these are how a real yes or no works. On a node they fire as the node is reached.

A node with no `Lines` is allowed when it does one of these: a silent node is the tidy way to give a
quest between two spoken ones. It runs its action and moves on to `Next`.

A quest id is enough for the NPC's own quests. To name another NPC's quest, write
`NpcInternalName/questId` — the internal name is `CultTweaker_` plus the NPC's name with spaces
turned into underscores.

## Different words for different states

`Entry` is a list of conditional ways into the conversation, checked in order. The first one whose
conditions all hold decides where the conversation starts; if none do, `Start` is used.

```json
"Entry": [
  { "QuestReady":  "firewood", "Node": "handin" },
  { "QuestActive": "firewood", "Node": "nag" },
  { "QuestDone":   "firewood", "Node": "thanks" },
  { "Node": "greet" }
]
```

The conditions are `QuestNotStarted`, `QuestActive`, `QuestReady`, `QuestDone` and `QuestFailed`,
each naming a quest. An entry with several of them needs all of them to hold. An entry with none is
the catch-all, so put it last.

`Ready` means every goal is met and the player has not handed the quest in yet. That is the state
the "come back to me" line is written for.

## Rewards

```json
"Reward": { "Items": [ { "Item": "GOLD_NUGGET", "Count": 5 }, { "Item": "MyCustomItem", "Count": 1 } ] }
```

Items are handed over as the game hands over any item. Custom items and meals work by internal name.

## Where progress is saved

In `BepInEx/plugins/CultTweaker/QuestProgress/quests_slot<N>.json`, one file per save slot, keyed by
quest name.

The game's own save file is deliberately left out of it. The lines a quest puts in the objectives
panel are real vanilla objectives whose text points at a term this mod registers, so leaving them in
the save would strand them there if CultTweaker were ever removed: a raw term in the quest panel that
nothing could finish. They are lifted out for the moment the game writes, and put straight back.

The practical consequence is that quest progress travels with the CultTweaker folder, not with the
save file. Copy both when you move a save.

## Limits

- Quests are single-player state. In a multiplayer session each player has their own.
- The game pins at most three objective groups at a time and drops the oldest, so a player carrying
  three quests will see one fall off the panel. It is still running; it comes back when another is
  finished.
- The dialogue wheel shows exactly two answers, so a node offering a quest offers one yes and one no.
- A quest with no goals is refused at load, since nothing could finish it.
- Abandoning is not the same as failing: the record is deleted, so the NPC can offer the quest again.
  A quest that runs out of time is failed and stays failed unless it is `Repeatable`.
