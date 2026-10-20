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

Reserved and candidate differences, added by the slice that implements the rule (the slice records the D2 decision;
until then both modes behave as the original):

| Domain | Ledger IDs | Note |
| --- | --- | --- |
| Combat | NPC goal-start gate with a maximum fatigue of 0 (both fixed: division by zero, ARC-COMBAT-003) | A9 is implemented (above). |
| Magic | MG3, MG4, MG5, MG6, MG7, MG9, MG10, MG11, MG15, MG18, MG19, LM10; D2 decisions on changing the original: shell order, charm bonus, summon palette, resistance source (O9), effect min/max | MG12 and LM20 (out-of-bounds writes and copies) are fixed in both modes. |
| Skills | SK8, LM17, SK3 repair off-by-one (multiplayer only), auto-level A3 (both fixed: null read) | SK1, SK2, SK4, SK5, LM1, LM2 and dodge are the original's behaviour in both modes, no switch; dodge already follows the original's 2 × level in `AttackRules`. |
| AI and reaction | LM3 (both fixed: out-of-bounds, latent), CL4 and CL5 (proposed switches, ARC-RES-054), MR3, MR7 and MR9 (proposed switches, ARC-RES-058), MR13 (candidate for the owner, ARC-RES-058), LM12 (both fixed; the reaction rules refuse a PC race outside the table, ARC-WORLD-026), reputation A5 (both fixed: out-of-bounds read, latent), reaction A4 charm asymmetry (decision D2 of that study; the original has no charm bypass of the PC's adjustment effects, ledger R2), P7 (label open) | P1, P2 (confirmed by ARC-RES-058), P3, LM13, LM18, reputation P1 (D1 decided 2026-10-17: written scope in both modes), reaction R1's effect owner (the original applies the PC's `reaction` effects, ARC-WORLD-026) and the companion wait timer follow the original in both modes, no switch. Reaction A2 is implemented (above). |
| Scripts | X2 (both fixed: uninitialised read, with call-script); `int.MinValue / −1` divide (both fixed: the original faults); a global flag number outside 0..3199 (both fixed: out of bounds); admission strictness in both modes: negative gotos (X6) and literal change-this-script numbers outside 0..99999, which no installed script uses | X1, X3 sector, X6 detail, X7, X8 follow the original in both modes, no switch. X3 tile and X10 are implemented (above). |
| Event queue | TE3/OQ1 tie order (FIFO decided without a switch) | TE1 is implemented in scripts (above). |
| Items | EQ5 (the medallion slot recalculates the animation speed of the item; no session effect, so no switch, ARC-WORLD-028) | EQ1 (a shield refused with one crippled arm) and EQ12 (only the largest light flag) follow the original in both modes, no switch. |
| Real-time combat | a repeat shot that would go before frame 0 (project guard in both modes, ARC-COMBAT-004) | RT6 (auto attack registered on), RT8 (the invisibility test of the attack start) and RT10 (Speed 8, LA1) follow the original in both modes; everything else is the original's behaviour, no switch (ARC-COMBAT-004). |
| Experience | none open | XP3 is implemented (above). XP1 (hard difficulty takes a quarter) and XP5 (the last-level experience, no recursion) follow the original in both modes; everything else is the original's behaviour, no switch (ARC-WORLD-030). |
| Advancement | AL7, AL8 (both fixed: a college target above 5 buys the next college's spell, a degree target above 7 reads past the cost table; the scheme reader refuses such a rule, no installed rule has one, ARC-WORLD-036); A3 (above, under Skills); a hero's bought hit or fatigue points above 100 (a project guard in both modes: the scheme run buys nothing; no pre-made hero comes near it) | AL4 (stat purchases continue from the base value) follows the original in both modes; everything else is the original's behaviour, no switch. |
| Effects | EF10 (an effect that lowers a maximum so far that no fatigue or hit points remain skips the faint and death handling; no fix recorded, ARC-WORLD-029); an effect number outside 0..399 (both fixed: the original reads past its effect tables; the session refuses it); an effect divisor `/0` (both fixed: the original would divide by zero; the effect reader refuses it, ARC-WORLD-035); a multiply or divide change on a maximum (a project guard in both modes: the session refuses it so the effect bounds hold; the original would apply it; no installed rule has one) | EF1–EF9 and the typed removal of the first entry whatever its cause are the original's behaviour, no switch. |
| Music and ambient | SN31 (hot-key equip plays the slot number as a sound; candidate, ARC-UI-044); SN12 (a 26th scheme record; both fixed: the original dereferences a null pointer, the catalogue refuses the scheme) | SN18, SN14 and SN23 are implemented (above). |
| Blessings and curses | BC1 (the original copies a blessing's text without a length limit, which no installed text can overflow) | BC2–BC5 are the original's behaviour; BC6 (curses 52–54, unused placeholder slots that would add the module's effect 0 and that nothing in the installed game gives) follows the original, no switch (owner decision 2026-10-20, ARC-WORLD-035). |
| World | LA4 direction-5 leaving on the portal tile; clock day carry only above one day (off by one), clock subtraction underflow, exchanged current day/month getters (dead code); wilderness A6 (both fixed: a null name faults) | LA1, LA2 and the tile swap itself follow the original in both modes, no switch. |

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
