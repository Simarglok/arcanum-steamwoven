# NPC AI support and magic: heal, spells, lists, items and grenades (ARC-WORLD-088)

Status: **Implemented bounded component**, ARC-WORLD-088, NPC combat AI plan slice
[I8](../research/npc-combat-ai-plan.md#phase-1-faithful-reconstruction) (its decision half; the goals the
decisions start are slice I8b, owner decision 2026-10-28). How the retail 1.0.7.4 executable decides whom an NPC heals and with
what, which spell or magick item it uses in a fight, how its ten action lists are built, sorted and cached, why an entry is
refused, when it picks items up from the ground, when it throws a grenade, and whom a spell's AI redirect turns
([support study](../research/npc-ai-support.md), stage A5, and the redirect of the [hostility study](../research/npc-ai-hostility.md#73-redirect);
ledger [SP1–SP18 and HS17](../research/ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059), two readers, re-read by this task:
SP19–SP21). The rules are pure functions in `Arcanum.Simulation.Ai` (`AiSupportRules`, `AiActionLists`, `AiGlobalLists`,
`AiListCache`, `AiGroundRules`) over world interfaces and labelled `IAiRollSource` rolls; the session stores the look-for bits and
the redirect's writes. No host runs them yet: the viewer's AI host is slice I10.

## The action lists

| Rule | Original | What it decides |
| --- | --- | --- |
| `AiGlobalLists.Build` | 0x4CC240 → 0x4CC380 | The ten global lists at init: every spell 0..222 whose value for the list is not −1, in number order, sorted highest first by the runtime's quicksort with the comparator 0x4CC2D0. `Value` (0x4CC310) and `DefensiveSecond` (0x4CC2A0) read a spell's values. |
| `AiActionLists.Quicksort` | 0x58A548, 0x58A69C | The runtime's quicksort, so equal values come out in the original's order (U1): the middle element swapped to the front as pivot, the two scans (`≤ 0` from the left, `≥ 0` from the right), the larger part stacked, parts of 8 or fewer by a selection sort that moves the first maximum to the end. Not stable: three spells of one value 0, 1, 2 come out 1, 2, 0. Checked against the executable by the function oracle (model `ai-global-list`, F3). |
| `AiActionLists.Build` | 0x4CC580 (build), 0x4CC8A0, 0x4CC810, 0x4CC930 | A critter's list, PC or NPC only. In combat mode at or below 50 % fatigue items, spells only when no item entry results; otherwise spells, items only when no spell entry results. Spells: with magick points above 0, the global list's spells it knows, in the global order. Items: the hexed wielded weapon alone, else the inventory in order; per item with a mana store that passes the identification rule (`Identifies`, 0x4CCA30: identified, no PC leader, or food or a scroll), each slot spell with a value for the list that the holder may use (0x456A10), in slot order; then an item with the valid-AI-action flag and this AI action gives an item AI-action entry (spell −1). At most 200 entries. |
| `AiActionLists.UsesItem` | 0x4CC160 | The chooser's item-use test: an item with a mana store whose first slot is empty, whose first spell has item triggers, or that has the valid-AI-action flag is used (pass action 2); any other item's spell is cast with it (action 1). |
| `AiListCache.Request` | 0x4CCA90, 0x4CC580 (cache), 0x4CC6F0, 0x4CC770 | The list request. Steamwoven builds fresh each time. Original (SP11): forty slots by critter; a request without a slot takes a free slot, else the next in round-robin order, and stores its build there; a request with a slot uses the slot's list when it is not empty, however old, and otherwise builds for itself only. So a slot holds only its first list. `Clear` (0x4CCC00, an item inserted into or force-removed from the critter) and `ItemExhausted` (0x4CCC40, a listed item's mana store ran out) drop the slot; `Empty` is the reset (0x4CC4C0). The cache is the host's transient state, never saved: a load starts empty. |

## Choosing (`AiSupportRules`)

| Rule | Original | What it decides |
| --- | --- | --- |
| `Heal` | 0x4A8940, helper 0x4A8AA0 | Nothing while the NPC sleeps or flees. The combat argument: 1, or 0 in combat mode when `d100 > parameter 14` (`ai.HealCareRoll`). Targets in order, the first one acted on ending the step: the NPC, its leader (PC leader, else NPC leader), that leader's followers (followers of followers, without the NPC), or without a leader its own followers. Per target, with the healer's lists and the chooser in mode 8: mechanical → nothing (P4); dead → the resurrect list, ending the helper; above 30 % HP with the argument 0 → nothing; out of danger below 90 % → the heal skill (10) with the NPC's healing item when the skill check accepts; in danger while the healer's fatigue is below 20 % → the fatigue list (SP17); heavy below 40 %, medium below 55 %, cure poison when poisoned, light below 70 % (90 % out of danger). `AiHealResult` lists every attempt with its rung and chooser answer. |
| `ChooseSpell` | 0x4ABC20, 0x4ABC70 | The spell and item choice at the danger update's four target points: nothing for an invulnerable or nigh-invulnerable NPC or with the "NPC fighting" switch off; a d100 above `Chance` (`ai.SpellChoiceRoll`) → nothing; then the summon list on the NPC (mode 1) when it has no followers, the defensive list (mode 4) when `parameter 13 < d100` (`ai.DefensiveListRoll`, P7), the offensive list on the target (mode 2). Without a choice the pass's action type is 0. The setter's target afterwards is `HostilityRules.FocusTarget` (SP16, slice I4). |
| `Chance`, `Stronger` | 0x4ABE20, 0x4ABEB0 | 0 when the NPC has a leader, both are above 50 % HP and the target is not stronger; else 50 when parameter 15 is above 1, otherwise 25. Stronger: the target's level at least the NPC's + 10 below level 20, + 5 from 20 to 39, + 0 from 40. |
| `ChooseFleeAction` | (0x4AAF50) | The flee-spell step's chooser (`MoraleRules.FleeSpell`, slice I5): the flee list on the NPC, mode 1. |
| `Choose` | 0x4ABF10 | The chooser over one list: empty → nothing, no roll. Target by mode: 1 the NPC, 2 the pass target, 8 the request's, 4 per entry (the pass target when the spell's second defensive value is not 0, else the NPC). Start at entry 0 when `d100 < 80` (`ai.ChooserStart`), else at `ai.ChooserEntry` 0..n−1; the walk wraps. An item AI-action entry is used at once without a check and the pass record is not written (`ItemUsedAtOnce`: the host runs the item use 0x462CC0 before it goes on); in mode 4 it is used on the NPC itself (SP18, both settings). Any other entry is skipped when the target is under the influence of its spell or `Eligibility` refuses; the first other one is the choice. `AiChooserResult.Checks` names each visited entry, its target and its refusal. |
| `Eligibility` | 0x4AE720 | The refusal codes in the original's order, without an item: 1 unknown; 2 an NPC without a PC leader below 20 % fatigue; 6 an NPC, a maintained spell and a free maintenance count of exactly 0; 2 the cast cost at least the current fatigue; 3 intelligence; 7 willpower; 4 a cast-highest NPC and not spell `5 × college + level − 1`; no target → accepted; 8 a fail chance of 38 or more (not for a healing-college spell on a target at or below 30 % HP); 5 a target other than the critter with a line of fire of cost 100 or more or another object stopping it. With an item: 6, 2 an empty mana store, no target → accepted, 8 (also skipped for a user at or above 80 % fatigue, SP8), 5. |

## Ground and blast (`AiGroundRules`)

| Rule | Original | What it decides |
| --- | --- | --- |
| `SearchItems` | 0x4A8E70, 0x4A8F90 | The pipeline step after heal: nothing while the NPC sleeps, is blinded or the interface holds an object. LOOK_FOR_AMMO, LOOK_FOR_WEAPON and LOOK_FOR_ARMOR in that order: each set bit is cleared just before its search, the first search that starts a pickup ends the step, later bits stay. A search: nothing for a monstrous NPC; in the square of half-size `clamp(Perception − 5, 1, 10)` the nearest loose item of the kind that is displayed, NPC-pickable, worth something and takable (the first on a tie, P1); when there is none or its pickup does not start, the first junk pile whose first item of the kind passes the same tests and is not off (SP21). Since ARC-AI-1309 the host's ground is the shown map's (`AreaAiHostGround.cs`). |
| `Grenade` | 0x4AC910 | Combat perform's grenade: no CHECK_GRENADE → none; BACKING_OFF → none, the bit kept; the bit cleared; no throwing skill points, no grenade, the target within the blast radius, any blocker on the line of fire, or a living critter the NPC protects in the square of the radius around the target → none; else the throw at the target's tile. The Original setting scans NPCs only (SP15). |
| `Redirect` | 0x4A9560, record 0x4A9530 | A spell's AI redirect component on its target: every living candidate of the target's vicinity but the target, skipped when `MinIQ:` is above 0 and the intelligence tested exceeds it, or when the mask is not 0 and the candidate has none of its critter flags; each NPC left is redirected (the host runs the attack event with the target as attacker, then the session's `RedirectDanger`). The Original setting tests the target's intelligence for every candidate (P3). |

**Installed data** (`--ai-redirect-census`, below): the only redirect components are the BEGIN and MAINTAIN lines of spell
183 (a technology record), both with critter mask 0x8 (animal) and no `MinIQ:`; no installed spell sets a threshold, so P3 changes
nothing with the installed records.

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `SetAiSupportBits(entity, bits)` | (0x4ADFF0, 0x461AB0) | Sets LOOK_FOR_AMMO, LOOK_FOR_WEAPON, LOOK_FOR_ARMOR or CHECK_WEAPON on the NPC's danger record in one revision: an AI ranged attack without ammunition sets the first two and CHECK_WEAPON, a dropped weapon or armour its look-for bit. Any other bit is `InvalidDangerFacts`. |
| `ClearAiSupportBits(entity, bits)` | (0x4A8E70, 0x4AC910) | Clears the look-for bits (the item search) or CHECK_GRENADE (the grenade step); any other bit is `InvalidDangerFacts`. |
| `RedirectDanger(entity, target)` | 0x4A94C0 | The redirect's writes after the host's attack event: focus and who-hit-me-last := the target, whatever the state, in one revision. A target equal to the NPC is `InvalidDangerTarget` (a critter is never its own focus; the rule never asks it). |

**State.** `DangerFlags.LookForAmmo`, `LookForWeapon` and `LookForArmor` (NPC flags 0x8000000, 0x2000000, 0x4000000) are saved
with the other danger bits: the snapshot moves to schema 47 (flag names `LookForAmmo`, `LookForWeapon`, `LookForArmor`; schemas
1..46 are refused, not migrated). The list cache, the chooser's choice and an item used at once are not saved.

## Rolls

Every draw is a labelled roll of the session's AI random source (`ai.` and the purpose), drawn exactly where the original draws:
`HealCareRoll` (1..100, only in combat mode), `SpellChoiceRoll` (1..100), `DefensiveListRoll` (1..100, only when the summon list
found nothing), `ChooserStart` (1..100, once for every non-empty list the chooser is given) and `ChooserEntry` (0..n−1, after a
start roll of 80 or more; a one-entry list draws 0..0, which takes nothing from the generator). The fail-chance predictors the eligibility test asks draw their d20 from the magic random source
(`magic.FailChanceD20`, ARC-MAGIC-005).

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| SP11 | The list cache keeps, per critter, only the list requested when its slot was taken, frozen until an inventory change, an exhausted listed item, a reset or reuse; every other list is rebuilt on each request. | Each list is built fresh on every request. | `CombatAnomalyProfile.AiListCacheKeepsFirstList` |
| SP15 | The grenade's friendly-fire scan covers NPCs only, so a PC in the blast (a follower's PC leader too) never stops a throw. | PCs and NPCs are scanned with the same protect rule. | `CombatAnomalyProfile.GrenadeIgnoresPcsInBlast` |
| SP17 | The heal ladder's fatigue-recovery rung tests the healer's fatigue but is tried for every heal target. | Tried only when the target is the healer. | `CombatAnomalyProfile.HealFatigueRungForOthers` |
| P3 | The redirect compares the target's intelligence with `MinIQ:` for every candidate, so it redirects all or none. | Each candidate's intelligence is compared. | `CombatAnomalyProfile.RedirectTestsTargetIntelligence` |

Both settings follow the original in P1 (the nearest item), P4 (no healing of mechanical critters), P7 (parameter 13 is a skip
threshold) and SP8 (the item spell's fatigue exemption). Both fix SP18 (an item AI-action entry in mode 4 is used on the NPC, where
the original reads 4 bytes before the spell table) and SP19 (the identification rule uses the critter's current PC leader, where the
Original cache's hit path builds with a request field that no cache function sets), as the owner's rule for out-of-bounds and
uninitialised reads has it (SP18, SU8).
Both also fix TH1 (the grenade's blast radius, 0x466230, tests the item's first spell slot the wrong way round, so every installed grenade reads
radius 0 in the original and an NPC throws at a target beside it or its friends): the owner decided on 2026-11-11 (ARC-DEV-373) that it is a bug of
the game fixed in both modes without a switch, so the radius is the Begin radius of the slot's spell (0 for a slot with none).

## Data probe

```sh
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --ai-redirect-census --game-root <game folder> [--module <name>]
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --ai-redirect-census-self-test
```

The census reads the spell records as the original does and prints, for every AI redirect component, the spell number, the action
line, the critter mask and the threshold, with counts by threshold. It prints no text of the game.

## Not ready

- **Goals (slice I8b, done).** The casting, item-use, skill, throw and pickup goals, their starters and perform's actions are the
  [action goals](ai-action-goals.md) (ARC-WORLD-089); the host calls `AiExecutorRules.UseItemNow` for an AI-action entry used at
  once (0x462CC0, EX4) and `AiExecutorRules.StartPickup` from the item search.
- **Host (slice I10).** ARC-WORLD-102: the viewer's AI host implements `IAiSupportWorld`'s spell half, holds the list cache and calls
  `ChooseSpell` from the danger stage, with the magic predicates from the map's cast host
  ([casting](ai-host.md#casting-arc-world-102)); the item half (inventory, hexed weapon, item spells and AI actions) and the `IAiRedirectWorld` world have no host yet (`Heal` and `Grenade` have since ARC-WORLD-108 and ARC-DEV-364). Since ARC-AI-1139 the host runs `SearchItems` over `IAiItemSearchWorld` and since ARC-AI-1309 with the shown map's ground: `LooseItems` (the session's ground items of the kind in the square, with the item flags NO_DISPLAY and NO_NPC_PICKUP, the worth field and the NPC's carry-weight verdict that the search tests), `JunkPiles` (the session's piles in the square), `FirstInPile` (the first item of the kind of the pile's list), `StartPickup` (goal 15 through `AiExecutorRules.StartPickup`, `NpcPickupsOn` true) and the goal's end `AreaAiHost.PickUp` (0x42AA70: 1 action point of the round's actor, then `GameSessionOwner.PickUpItemByNpc`, the NPC's list takes the item; the viewer gets a `PickUp` notice); the gate's re-wield is `GameSessionOwner.RewieldNpc`. Since ARC-AI-1486 the pickup also reads the item switch (`AreaAiScene.LootWindowOpen`: a loot or See Contents window open on a container lowers 0x5E8800, so no NPC starts a pickup and the goal's end of one under way fails before anything is charged or moved), searches and takes from the map's own standing junk piles (`AreaCombatHost.MapJunkPiles`, `PickUpItemByNpc(..., mapPile)`), dissolves a pile it leaves with one item or none at once (the removal's check 0x467860 at 0x467C26, NL2 in the [ledger](../research/ledger/npc-ai.md#npc-pickup-limits-arc-ai-1486)) and counts a carried companion's whole list from its home map's seed. Since ARC-AI-1654 the host's verdict also asks the room of 0x466510 (the NPC's 10 x 12 grid, `GameSessionOwner.QueryHolderRoom` over `InventoryGridRules.HolderFreeSlot`: the seed's locations and the arranged slots, the worn and quick-slot items taking no cell, an item without a known place taking the first free one in list order; an ammunition stack of a held prototype needs no place; `SessionInsertVerdict.NoRoom` after the weight's `TooHeavy`), so an NPC whose grid has no free rectangle for the item does not search for it or take it (NL7, NL8 in the [ledger](../research/ledger/npc-ai.md#npc-pickup-limits-arc-ai-1486)). NOT READY (#1485 for the weapon art; #1655 for the get and insert-item scripts of the item, which the session refuses for an NPC: no installed object carries a get script and only script 1341 of the 39 weapon and armour insert-item slots lies in the item-script set; the item sound of a local picker): the NPC's weapon art in its hand after a re-wield, the interface test of the search (the item system's switch is read, the interface test is always off), the item flags and worth a script wrote (the host reads the map row or prototype), a load in the middle of a pickup walk (the restored run ends as the object gone).
- **Setters.** The out-of-ammunition attack (0x4ADFF0) calls `SetAiSupportBits` since ARC-AI-1139 (`AreaAiHost.NeedsAmmunition`: LOOK_FOR_AMMO, LOOK_FOR_WEAPON and CHECK_WEAPON after a refused ranged attack of an NPC); the item drop (0x461AB0), which ARC-AI-1309 checked against its ground search, sets LOOK_FOR_WEAPON (a dropped weapon, item type 5) or LOOK_FOR_ARMOR (armour, type 7) on an NPC owner since ARC-WORLD-1288 (`GameSessionOwner.NoteDropForAi`, in the session's one drop step: a critical's, an arm hit's and a spell's weapon block; the hero and other item types set none; written straight to the danger record, no separate revision); the
  cache's clears on an insert, a force-removal and an exhausted item are the host's calls.
- **Redirect callers.** The AI redirect component is run by the spell run's component step (the magic domain), which does not call
  `Redirect` yet.

## Tests

`SupportRulesTests` (Simulation, an invented world): the runtime sort's tie order and the global lists; the list build (items or
spells by fatigue and combat mode, the hexed weapon, the identification rule, the item gate, the AI-action entry, 200 entries); the
Original cache (frozen first list, lists built for the caller, round-robin reuse, clears) against Steamwoven's fresh build; the
item-use test; the chooser's start, wrap, modes, SP18 and immediate item use; every eligibility refusal in order with SP8; the heal
ladder's targets, rungs, P4, the combat argument, the heal skill and SP17 in both settings; the spell chance, stronger and the list
order with P7; the item search's bit clearing, nearest item, junk-pile fallback and stops; the grenade's steps and SP15; the
redirect's P3 in both settings and its mask. `AiListOriginalExeTests` (opt-in): the global list against the executable on the
oracle's 10,000 seeded inputs. `SupportTests` (Session): the support bits, their refusals and the schema-47 round trip, and the
redirect's writes.
