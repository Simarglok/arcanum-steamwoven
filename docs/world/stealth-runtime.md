# Stealth runtime (ARC-SKILLS-008)

Status: **Implemented bounded component**, ARC-SKILLS-008 (2026-10-21), task S12 of the [skills plan](../research/skills-plan.md):
the concealment toggle of the hero and its followers (saved), the prowling margin an observer's sight or hearing check uses, and
the stealth gait. The awareness functions take the margin as an input ([AI awareness](ai-awareness.md), ARC-WORLD-078, slice I2); their callers come with slice I4 and later. Rules: `StealthRules` in
[`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills/StealthRules.cs); session:
[`GameSessionStealth.cs`](../../src/Arcanum.Session/GameSessionStealth.cs); viewer: the palette's **Prowl (stealth) on/off**.
Snapshot schema 35. Requirements: [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) (perception and stealth),
[FR-CHAR-019](../requirements/characters-skills.md#fr-char-019) (a skill used).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`, read statically per the [original executable check](../research/original-executable-check.md)
  by two independent readers: rows ST1–ST6 of the [verification ledger](../research/ledger/skills.md#stealth-arc-skills-008).
  They agree on every row (ST1's hotkey refusal of a stunned or paralysed PC was read by one reader only). No rule-mode switch.
- Sight, hearing and the margin's terms: [AW1–AW12](../research/ledger/npc-ai.md#npc-ai-awareness-arc-res-056).

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Toggle (ST1) | The hero's concealment turns to the opposite and every follower in the follower list takes the new state; a waiting NPC has left that list and keeps its own (HK2); no check, action points, fate or experience; refused for a dead or unconscious hero. | `ToggleHeroConcealment` |
| Join, leave (ST2) | A new or unwaited follower takes the hero's state; one that leaves (dismissed, by wait expiry or by loyalty) loses it. | `JoinParty`, `ForceJoinParty`, `UnwaitParty`, `LeaveParty` |
| End (ST4) | A dialog's start ends the hero's and that NPC's; an attack on the hero or on an NPC the hero leads (following or waiting) ends the hero's and the follower list's, even when it misses; a waiting victim's stays, and so does an attacker outside the victim's party (the hero attacking its own follower is in it and loses its own). Nothing else ends it (not a map change or a load). | `BeginReactionConversation`, `AttackCritter` |
| Gait (ST3) | Below expert the stealth walk and no run; an expert the ordinary walk and no run; a master runs while it has fatigue. | `StealthRules.Gait` |
| Margin (AW4, ST5) | Per observer at each check: `clamp(E − D, 0, 100)` of the shared check's prowling effectiveness (opposed by the observer's perception) and difficulty (sight or hearing); the observer's perception falls by `P × M / 100`. 0 while the hero is not concealed. | `HeroProwlingMargin`, `StealthRules.Margin`, `PerceptionAfterMargin` |

## Snapshot schema 35

`"concealment":{"hero":bool,"npcs":[entity]}`: the hero's flag (false without a hero) and its concealed followers in strictly
ascending ordinal order. The decoder refuses a missing or unknown field, a repeated or unordered entity (`NotCanonical`); Restore
refuses the hero's flag without a hero, a concealed NPC without a hero, and an entity the hero does not lead (following or waiting: a waiting NPC keeps the state it had when it began waiting) (`InvalidConcealment`). Schema
34 is refused, not migrated ([game session](game-session-runtime.md#snapshot-schema-51)).

## Viewer

The palette's **Prowl (stealth) on/off** stands for the original's skill-bar prowling button and toggles the session hero's
concealment. While the hero is concealed, its steps follow the stealth gait: a run becomes a walk unless the hero is a master of
prowling with fatigue left; the movement status names it. The crouch and the stealth walk animations are not drawn (the ordinary
stand and walk are). Followers keep their ordinary follow gait in the viewer: the stealth gait applies to the hero's steps only.

## Boundaries (NOT READY)

- The observers: no NPC sees or hears in the session yet (the awareness functions of slice I2 take the margin as an input; the target scan and witnesses of slice I4 and later will call `HeroProwlingMargin`); the examine lines (ST6), the
  AI's target rules (ST5) and script condition 51 are its.
- Not modelled: the crouch art and its missing-art refusal (ST1), the hotkey's refusal of a stunned or paralysed PC (ST1, one reader only), the followers' stealth gait in the viewer, summoned NPCs created concealed (ST2), the right-click and goal
  54 ends (ST4), NPC attacks on the hero (no NPC attack path yet), the 2 action points per step in a turn-based round and the light
  avoidance of the path search (ST3), the shadow (ST6), the multiplayer records.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/StealthRulesTests.cs`, `tests/Arcanum.Session.Tests/StealthStateTests.cs` (toggle with
followers, join, unwait and leave, a waiting follower, dialog and attack ends (a miss, a stranger victim), the snapshot and its checks, the margin, refusals), the restore tests' schema
34 rows, the viewer self-test (the prowl toggle and the stealth gait) and the native smoke (`ARC-SKILLS-008 native-smoke prowl PASS`).
