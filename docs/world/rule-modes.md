# Rule modes: Steamwoven and Original

Status: **Implemented bounded component**, ARC-WORLD-023. The game has two rule modes (owner decision D8 of the
[NPC combat AI plan](../research/npc-combat-ai-plan.md#owner-decisions-2026-10-16), extended to every domain by the
owner on 2026-10-17): **Steamwoven**, the original game with the recorded fixes, which is the default, and
**Original**, the retail 1.0.7.4 behaviour including the bugs of the original game. The player will pick the mode in
the settings menu and change it during play without a restart (there is no menu yet, see [Not ready](#not-ready)); the
mechanism below already allows it. This contract fixes the mechanism every domain uses and
keeps the registry of mode differences. The architecture decision is recorded in
[architecture direction](../architecture/decisions.md).

## What is a mode difference

The [original executable check](../research/original-executable-check.md) classifies each anomaly a domain study finds,
and the [verification ledger](../research/original-verification-ledger.md) records the verdicts:

| Verdict | Steamwoven | Original |
| --- | --- | --- |
| Bug of the original game, fix recorded under D2 | the fix | the original's bug (a switch) |
| Bug of the original game, no fix recorded | the original's bug | the original's bug |
| Behaviour of the original | the original | the original |
| Project improvement recorded by the owner (for example persisted AI ticks, OQ2) | the improvement | the original's behaviour (a switch) |

So a switch exists only where Steamwoven deliberately differs from the retail game. A row that only corrects an earlier reading of a function states the original's behaviour and never becomes a switch.
Owner decision (2026-10-17): **the Original mode never reproduces a crash, a hang or an out-of-bounds access**; such
bugs are fixed in both modes and listed in the registry as "both fixed", because
reproducing them would end the game rather than show the original rules.

## Mechanism

- `Arcanum.Session.RuleMode` names the mode (`Steamwoven`, `Original`); `RuleModeNames` gives the stable names
  `steamwoven` and `original`, exact and case-sensitive.
- Each domain that has a switch owns one **profile**: an immutable, stateless record in its own assembly with a stable
  `Name`, two static instances `Steamwoven` and `Original`, `All` and `FromName` (the shape of
  `CombatAnomalyProfile`). Each switch is a boolean named after the original's behaviour and documented with its
  ledger ID. A profile changed with `with` is a custom mix for comparison runs and must get its own name.
- `Arcanum.Session.GameRules` composes the domain profiles of one mode: `GameRules.Steamwoven`, `GameRules.Original`,
  `For(mode)`, `All`, `Name`. Every profile in a `GameRules` carries the mode's name (a test checks it).
- The session holds the active rules: `GameSessionDescriptor.Rules` and `GameSessionRestoreOptions.Rules` start a
  session in a mode (default Steamwoven; an undefined value is `InvalidDescriptor`), `GameSessionOwner.Rules` returns
  them and `GameSessionOwner.SelectRules(mode)` switches at once (`Changed`, `NoChange`, or `Rejected` for an
  undefined value).
- `RuleModeSettingCodec` stores the player's choice as `{"schema":1,"rules":"steamwoven"|"original"}`; a missing,
  unreadable or unknown value loads as Steamwoven. The settings menu writes it and calls `SelectRules`; the next new
  game or load passes it to the descriptor or restore options.

### Rules that make switching safe without a restart

1. **Read at the decision.** A domain takes its profile from the session's current `Rules` at the moment it decides
   (an attack, a cast, a script action, a restore) and does not keep it between calls.
2. **No state depends on the mode.** Saved and live state have one schema for both modes; a switch changes how the
   next decision is made, never converts existing state (design rule 7 of the AI plan, D4).
3. **Not saved.** The snapshot does not record the mode; any save loads under either mode. Switching moves no session
   revision and draws no random number.
4. **One switch, one difference.** A domain adds a switch only for a ledger row whose verdict makes it a mode
   difference (table above), and adds its profile to `GameRules` in the same PR, with tests for both modes.

## Registry

Implemented switches (the Original value is `true`; Steamwoven `false`):

| Domain | Profile | Switch | Ledger ID | Original behaviour |
| --- | --- | --- | --- | --- |
| Combat | `CombatAnomalyProfile` | `InvocationModifierCountedTwice` | A2 | the bed's −30 counts twice |
| Combat | `CombatAnomalyProfile` | `ArmorClassEffectsCountedTwice` | A6 | AC-adjustment effects count twice |
| Combat | `CombatAnomalyProfile` | `CriticalSelfHitRolledTwice` | A7 | a critical self-hit rolls the critical procedure twice |
| Combat | `CombatAnomalyProfile` | `BackstabDamageOnSelfHit` | A10 | the backstab bonus applies to a self-hit |
| Combat | `CombatAnomalyProfile` | `FirstSubturnStartedTwice` | A9 | after the next-subturn skip loop runs past the end, the new round's first subturn starts twice (ARC-COMBAT-003) |
| Combat | `CombatAnomalyProfile` | `StallCheckKeepsPreviousActionPoints` | A11 | the stalled-actor check compares a new actor's second check with the previous actor's action points (ARC-COMBAT-003) |
| Combat | `CombatAnomalyProfile` | `OverexertionKnockoutClearsNextActionPoints` | A12 | an overexertion that knocks the PC out zeroes the next participant's action points (ARC-COMBAT-003) |
| Event queue | `GameEventQueueProfile` | `RebuildOnLoad` | OQ2 | AI ticks are rebuilt with fresh jitter on load |
| Event queue | `GameEventQueueProfile` | `PoisonRecoveryTwice` | TE10 | from the second recovery tick on, poison recovers twice every 120 s (ARC-WORLD-024 life timers) |
| Scripts | `ScrRuleProfile` | `GlobalFlagWriteSpills` | X10 | a value written into a global flag is ORed, shifted, into the flag's 32-flag word (ARC-SCRIPT-008) |
| Scripts | `ScrRuleProfile` | `TileScriptChangesDiscarded` | X3, tile | a tile script's changes to its own number, flags and counters are not stored (ARC-SCRIPT-008) |
| NPC reaction | `ReactionRuleProfile` | `FollowerHitRaisesReaction` | reputation A2 | a deliberate hit by the PC on its own follower adds AI parameter 9 to the follower's reaction (Steamwoven lowers it as an ordinary drop, so an extraordinary-charisma leader is immune and other followers get the "check leader" mark; ARC-WORLD-026) |
| Scripts | `ScrRuleProfile` | `CallDelayWraps` | TE1 | a call script in delay is computed in 32 bits (wraps above 536,870 s; a negative count gives weeks) (ARC-SCRIPT-010) |
| Game data | `DataFixProfile` | `StoneExamineTextLost` | DF1 | four stone-examination DLGs lack their record's effects field, so the game opens none and examining the stones shows nothing ([data fixes](../formats/data-fixes.md), ARC-DATA-022) |
| Game data | `DataFixProfile` | `NpcLineEffectsInResponseLost` | DF2 | an NPC line's effects written in its response field are never applied (ARC-DATA-022) |
| Game data | `DataFixProfile` | `ReplyFinalLineInResponseLost` | DF3 | a reply's final-line action written in its response field reads as 0 and ends the talk without the final line (ARC-DATA-022) |
| Music and ambient | `SoundRuleProfile` | `CombatSaveLosesSchemes` | SN18 | a save made while combat music plays stores empty scheme slots; on load they replace the saved pre-combat pair, so the loaded map's default (or silence, without one) plays through combat and leaving it restores silence ([audio study](../research/audio-evidence.md), ARC-WORLD-040) |
| Music and ambient | `SoundRuleProfile` | `EmptyHitRecordSilent` | SN23 | a weapon hit falls back to the material sound only when its id is in no sound table; an empty record counts as held and plays nothing, so 128 installed weapons hit silently; Steamwoven lets an empty record fall back to the material sound (the cue player of ARC-UI-042) |
| Music and ambient | `SoundRuleProfile` | `HourWindowsNeverWrap` | SN14 | a scheme sound's hour window never wraps, so a reversed window (20 installed night entries such as `19-0`) never plays; Steamwoven runs it past midnight (the scheme player of ARC-UI-041) |
| Experience | `ExperienceRuleProfile` | `ProgressProductWraps` | XP3 | the progress toward the next level multiplies `1000 × to-next` in 32 bits and wraps above 2,147,483 to-next (found by the function oracle of ARC-RES-051; ARC-WORLD-033, checked by the oracle in both modes) |
| Items | `ItemRuleProfile` | `RepairQuoteCountsDamageTwice` | TC7 | the repair quote starts from the PC's sale price, already scaled by `current / maximum`, and multiplies by `damage / maximum` again, so a badly damaged item is nearly free to repair; Steamwoven quotes from the undamaged sale price, so the cost grows with the damage (owner decision 2026-10-20, ARC-WORLD-053) |
| Technology | `TechnologyRuleProfile` | `RegistersSchematicZero` | TC8 | reading the one installed schematic document numbered 0 registers schematic 0 (a record of zeros that can never be built) and destroys the document; Steamwoven refuses it and keeps the document (owner decision 2026-10-20, a project improvement, ARC-TECHNOLOGY-003) |
| Magic | `MagicAnomalyProfile` | `SaveSubtractsResistance` | MG4 | the resistance term (a tenth of the margin the d100 gave; in the predictor a tenth of the resistance) is subtracted from the target's saving throw, so resisting the magic first makes the save harder; Steamwoven adds it, as the manual says (+1 per 10 %), in the resistance and in the fail-chance predictor (owner decision 2026-10-20, ARC-MAGIC-005) |
| Magic | `MagicAnomalyProfile` | `SummonedTestReadsLastFlags` | MG25 | the area test's summoned bit checks bit 0x4000 of the flags word the test read last — the critter flags ("surrendered") when the area has the critter bit, a door's or chest's flags after a lock test — instead of the spell flag "summoned"; Steamwoven checks the spell flag; no installed record reaches it (records 183 and 217 join the bits only in action areas the collection does not test) (ARC-MAGIC-007) |
| World-map travel | `TravelRuleProfile` | `DiscoveryUsesBestRadius` | WT3 | discovery compares a later area with the current best's radius and the result with the radius loaded last, so where two areas overlap a nearer area outside its own radius makes the step discover nothing; Steamwoven discovers the nearest area within its own radius (A1, owner decision 2026-10-21; ARC-WORLD-056) |
| World-map travel | `TravelRuleProfile` | `DetourKeepsStaleStep` | WT7 | after a detour the route counts one direction more than it wrote, so the walker takes a stale byte of an earlier route; Steamwoven holds only the steps it wrote (A2, owner decision 2026-10-21; ARC-WORLD-056) |
| World-map travel | `TravelRuleProfile` | `SegmentEndAddsNoTime` | WT11 | the step that ends a segment adds no game time, so n sectors over k waypoints take n − k hours; Steamwoven counts every step (A3, owner decision 2026-10-21; ARC-WORLD-056) |
| World-map travel | `TravelRuleProfile` | `CancelSkipsAreaSnap` | WT12 | cancelling travel lands on the sector centre without the area snap; Steamwoven lands as an arrival does (A4, owner decision 2026-10-21; ARC-WORLD-056) |
| Follower orders | `CompanionOrderRuleProfile` | `FollowOrderNeverUnwaits` | OL6 (D1) | the follow order reaches only followers in the array and its unwait never acts, so it changes no state; Steamwoven also brings back the waiting followers within 10 tiles that pass the follow check (owner decision 2026-10-21, D1; ARC-WORLD-046) |
| Follower orders | `CompanionOrderRuleProfile` | `PresetSixAddressesNobody` | OL14 (D2) | F6's preset has no `#` and names no follower, so it does nothing; Steamwoven sends "follow" to every follower in reach (owner decision 2026-10-21, D2) |
| Follower orders | `CompanionOrderRuleProfile` | `FarewellRangesSwapped` | OL13 (D3) | the leave farewell takes the range labelled friendly at reaction 40 or less and the hostile one above; Steamwoven the other way round (owner decision 2026-10-21, D3) |
| Follower orders | `CompanionOrderRuleProfile` | `PortraitAddressesByNamePrefix` | OL15 (D4) | the portrait menu's order goes to the first follower in reach whose known name prefixes "name label", not necessarily the clicked one; Steamwoven gives it to the clicked follower in reach (owner decision 2026-10-21, D4) |

Reserved and candidate differences, added by the slice that implements the rule (the slice records the D2 decision;
until then both modes behave as the original):

| Domain | Ledger IDs | Note |
| --- | --- | --- |
| Combat | NPC goal-start gate with a maximum fatigue of 0 (both fixed: division by zero, ARC-COMBAT-003) | A9 is implemented (above). |
| Magic | MG3, MG9, MG10, MG11, MG15, MG19 (Steamwoven fixes with Original switches, added by their slices: MG3, MG11 and MG19 with MT9a, MG9 with MT17, MG10 with MT18, MG15 with MT10; owner decision 2026-10-20); LM10 (the items owner) | MG4 is implemented (above). MG5, MG6, O9 (the magic resistance source: the stored value, ARC-MAGIC-005), O10 (shell and reflection order), the charm bonus and the summon palette follow the original in both modes, no switch; MG13 and MG14 (the fate save and the fail-chance predictor, ARC-MAGIC-005) follow the original; MG7 is record only (no installed record has an Interrupt component); MG12, LM20 and MG24 (out-of-bounds writes and copies; MG24: the target list's 256 entries, ARC-MAGIC-007) and MG26 (reads through a missing object, ARC-MAGIC-007) are fixed in both modes; MG18 and MG21–MG23 (target values without a kind bit) are the original's intent and follow it in both modes, and MG27 (the tile-radius test of a tile against itself) follows the original as MG5 does (ARC-MAGIC-007); effect min/max is decided in the effect rules. |
| Skills | SK8, LM17, SK3 repair off-by-one (multiplayer only), auto-level A3 (both fixed: null read), CP6 (a basic skill's − click passes the editor's floor when an effect holds its level; pending owner decision, recommended and built: fixed in both modes, ARC-WORLD-052) | SK1, SK2, SK4, SK5, LM1, LM2 and dodge are the original's behaviour in both modes, no switch; dodge already follows the original's 2 × level in `AttackRules`. |
| AI and reaction | LM3 (both fixed: out-of-bounds, latent), CL4 `SurrenderKeepsCombatModeFlag` and CL5 `GateRecoveryWritesStaleFlags` (Original switches decided by the owner 2026-10-20, ARC-RES-054; added with slices I3 and I6), MR3 `NpcLeaderPartyCountsAlone`, MR7 `StaleSpellFleeSkipsFleeSpell`, MR9 `FleeGoalAfterRefusedSurrender` and MR13 `MindControlLeavesNoFlee` (Original switches decided by the owner 2026-10-20, ARC-RES-058), AW13/AW14 `CornerStepCountsBothRoutes` and AW16 `WitnessHandledTwice` (Original switches decided by the owner 2026-10-20, ARC-RES-056), LM12 (both fixed; the reaction rules refuse a PC race outside the table, ARC-WORLD-026), reputation A5 (both fixed: out-of-bounds read, latent), reaction A4 charm asymmetry (decision D2 of that study; the original has no charm bypass of the PC's adjustment effects, ledger R2), P3 `RedirectTestsTargetIntelligence` and HS9 `UpsetPartyMatchesMissingLeader` (Original switches decided by the owner 2026-10-20, ARC-RES-057), HS22 (both fixed: null call, latent), SP11 `AiListCacheKeepsFirstList`, SP15 `GrenadeIgnoresPcsInBlast`, SP16 `EnemyListFocusTakesSpellTarget` and SP17 `HealFatigueRungForOthers` (Original switches decided by the owner 2026-10-20, ARC-RES-059), SP18 (both fixed: out-of-bounds read, latent), EX6 `EndStepRechargesPendingCost`, EX9 `RangedApproachSkipsClearShot`, EX12 `SpreadOutSetsNoFlee` and EX15 `RangedBlockerHitNotifiesAimedTarget` (proposed, ARC-RES-066), FO7 `NoAttackSurvivesLeaving`, FO8 `NoAttackKeepsChosenAction` and FO14 `LockedNoneStateFights` (proposed, ARC-RES-067), FO16 (both fixed: null call, latent) and FO17 (both fixed: null object, latent) | P1 (confirmed by ARC-RES-059), P2 (confirmed by ARC-RES-058), P3 (the earlier reading's comparison), HS19–HS21 (ARC-RES-057), LM13, LM18, reputation P1 (D1 decided 2026-10-17: written scope in both modes), reaction R1's effect owner (the original applies the PC's `reaction` effects, ARC-WORLD-026) and the companion wait timer follow the original in both modes, no switch; so do the awareness rows AW15 and AW17–AW19 (ARC-RES-056) and P4, P7 (the original's behaviour) and SP8 (ARC-RES-059). Reaction A2 is implemented (above). EX13 and EX20 (ARC-RES-066) are the original's behaviour in both modes, no switch; so are FO9, FO10 and FO13 (ARC-RES-067) |
| Scripts | X2 (both fixed: uninitialised read, with call-script); `int.MinValue / −1` divide (both fixed: the original faults); a global flag number outside 0..3199 (both fixed: out of bounds); admission strictness in both modes: negative gotos (X6) and literal change-this-script numbers outside 0..99999, which no installed script uses | X1, X3 sector, X6 detail, X7, X8 follow the original in both modes, no switch. X3 tile and X10 are implemented (above). |
| Event queue | TE3/OQ1 tie order (FIFO decided without a switch) | TE1 is implemented in scripts (above). |
| Lighting | LT5 A1 (the nocturnal refresh reuses its tile counter, so some scenery is not switched at a refresh) and A2 (handles inserted again); both fixed in both modes, no switch (owner decision 2026-10-21, ARC-RENDER-030: every nocturnal light switches with the hour; the refresh's skip of scenery flag 0x400 is not emulated either) | A3 (the previous-frame step stops at frame 0) has no effect on the animation, which only steps forward. |
| Items | EQ5 (the medallion slot recalculates the animation speed of the item; no session effect, so no switch, ARC-WORLD-028) | EQ1 (a shield refused with one crippled arm) and EQ12 (only the largest light flag) follow the original in both modes, no switch. |
| Real-time combat | a repeat shot that would go before frame 0 (project guard in both modes, ARC-COMBAT-004) | RT6 (auto attack registered on), RT8 (the invisibility test of the attack start) and RT10 (Speed 8, LA1) follow the original in both modes; everything else is the original's behaviour, no switch (ARC-COMBAT-004). |
| Experience | none open | XP3 is implemented (above). XP1 (hard difficulty takes a quarter) and XP5 (the last-level experience, no recursion) follow the original in both modes; everything else is the original's behaviour, no switch (ARC-WORLD-030). |
| Advancement | AL7, AL8 (both fixed: a college target above 5 buys the next college's spell, a degree target above 7 reads past the cost table; the scheme reader refuses such a rule, no installed rule has one, ARC-WORLD-036); A3 (above, under Skills); a hero's bought hit or fatigue points above 100 (a project guard in both modes: the scheme run buys nothing; no pre-made hero comes near it) | AL4 (stat purchases continue from the base value) follows the original in both modes; everything else is the original's behaviour, no switch. |
| Effects | EF10 (an effect that lowers a maximum so far that no fatigue or hit points remain skips the faint and death handling; no fix recorded, ARC-WORLD-029); an effect number outside 0..399 (both fixed: the original reads past its effect tables; the session refuses it); an effect divisor `/0` (both fixed: the original would divide by zero; the effect reader refuses it, ARC-WORLD-035); a multiply or divide change on a maximum (a project guard in both modes: the session refuses it so the effect bounds hold; the original would apply it; no installed rule has one) | EF1–EF9 and the typed removal of the first entry whatever its cause are the original's behaviour, no switch. |
| Music and ambient | SN31 (hot-key equip plays the slot number as a sound; candidate, ARC-UI-044); SN12 (a 26th scheme record; both fixed: the original dereferences a null pointer, the catalogue refuses the scheme) | SN18, SN14 and SN23 are implemented (above). |
| Blessings and curses | BC1 (the original copies a blessing's text without a length limit, which no installed text can overflow) | BC2–BC5 are the original's behaviour; BC6 (curses 52–54, unused placeholder slots that would add the module's effect 0 and that nothing in the installed game gives) follows the original, no switch (owner decision 2026-10-20, ARC-WORLD-035). |
| World | LA4 direction-5 leaving on the portal tile; clock day carry only above one day (off by one), clock subtraction underflow, exchanged current day/month getters (dead code); wilderness A6 (both fixed: a null name faults) | LA1, LA2 and the tile swap itself follow the original in both modes, no switch. |
| Technology | Every TC row is decided by the owner (2026-10-20, [owner decisions](../research/technology-evidence.md#owner-decisions-2026-10-20), ARC-RES-062). Switches: TC1 (item-source cell payment: Steamwoven `2 × cost − trunc(cost × p / c)`, so an experienced technologist pays fewer cells; Original keeps `cost + trunc(cost × p / c)`; TECH-6), TC7 (repair quote: Steamwoven from the undamaged sale price, `trunc(P_full / 2) × damage / maximum`, at least 2; Original keeps the double count; **implemented** by ARC-WORLD-053, above), TC8 (a project improvement: Steamwoven refuses to register a schematic document numbered 0 and keeps it; Original registers it; **implemented** by ARC-TECHNOLOGY-003, above). Both fixed: TC2 (action 129 opens the window with (PC, PC) or (NPC, its PC leader) instead of an uninitialised callback; **implemented** for the PC by ARC-TECHNOLOGY-005 and for an NPC with a PC leader (the NPC as crafter) by ARC-TECHNOLOGY-006, [schematic window](schematic-window-runtime.md#script-action-129-sw8-tc2)), TC4 (a found item is reserved for one component; **implemented** by ARC-TECHNOLOGY-004), TC6 (out-of-bounds parse; **implemented** by ARC-TECHNOLOGY-001: the schematic catalogue refuses a list line with more than three tokens or of 2,000 bytes or more, `TechnologyDataStatus.TooManyTokens`/`LineTooLong`), TC11 (a learned range the original's full list cannot hold, latent; found and implemented by ARC-TECHNOLOGY-001: the catalogue refuses it, `LearnedListMismatch`/`LearnedDisciplineOutOfRange`), TC12 (a found schematic's discipline outside 0..7; found and implemented by ARC-TECHNOLOGY-003: left out of the found tab) ([technology study](../research/technology-evidence.md#anomalies-tc1tc10)) | No switch: TC3 (both modes check room and creation before consuming, components are never lost; **implemented** by ARC-TECHNOLOGY-004), TC5 (both modes re-check the expertise gate in assembly; **implemented** by ARC-TECHNOLOGY-004), TC9 (broken items still usable, the original's behaviour), TC10 (both modes sell a degree only down to its value when the character window was opened, as for skills and colleges; TECH-2). |

The list covers every ledger row whose verdict is a bug of the original or a D2 decision on changing the original.

## Not ready

- The attack resolver is not wired into the session yet; the turn-based round (ARC-COMBAT-003) reads `Rules.Combat` at
  each round call, but no host runs a round yet. The event queue reads the rules too
  (ARC-WORLD-024): a restore uses `Rules.Events` for `RebuildOnLoad`, and the poison recovery timer reads
  `PoisonRecoveryTwice` when it fires.
- There is no settings menu yet; the viewer has no mode toggle. The codec is ready for the menu. The AREA quick load
  (F8) keeps the current session's mode.
- The SCR dispatcher (ARC-SCRIPT-008) takes `GameRules.Scripts` through its run options; no session code calls it yet.
- The reaction domain (ARC-WORLD-026) reads `Rules.Reaction` in `GameSessionOwner.ReactToPcHit`; no host calls it yet.
- The game data fixes (ARC-DATA-022) are read by `ScriptContent.Dialog` with `Rules.Data`; the AREA conversation
  passes the session's rules at each dialog read.
- The experience domain (ARC-WORLD-033) reads `Rules.Experience` in `GameSessionOwner.QueryHeroExperience`.
- The sound domain (ARC-WORLD-040) reads `Rules.Sound` when a session is restored (a load of a save made during combat
  music); the viewer's scheme player (ARC-UI-041) reads it when a map opens or a quick load replaces the session.
- Domains other than combat, the event queue, scripts, NPC reaction, game data, experience and sound have no profile yet;
  their first switch adds one.
