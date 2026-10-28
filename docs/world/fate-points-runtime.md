# Fate points runtime (ARC-WORLD-054)

Status: **Implemented bounded component**, ARC-WORLD-054 (2026-10-21), task S7 of the [skills plan](../research/skills-plan.md)
(the shared fate owner the skills, combat and magic lanes read). The hero's fate points and armed fates live in the game
session; the pure rules are `FateRules` in [`Arcanum.Simulation.Character`](../../src/Arcanum.Simulation/Character/FateRules.cs),
the session API is [`GameSessionFate.cs`](../../src/Arcanum.Session/GameSessionFate.cs). The hero's attack, the conversation start
and the dialog action `fp` use it now; the skill check and magic take it through the session API. No fate window yet.
Snapshot schema 26. Requirements: [FR-CHAR-024](../requirements/characters-skills.md#fr-char-024) (fate points),
[FR-MAGIC-005](../requirements/combat-magic-tech.md#fr-magic-005) (the two magic fates as inputs).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers: rows FT1–FT8 of the
  [verification ledger](../research/ledger/skills.md#fate-points-arc-world-054). They agree on every row; no
  anomaly, no rule-mode switch. DR6 (force good reaction) and SC14 (the skill fates) were already at F2.
- The function oracle ([ARC-DEV-007](../../tools/original-oracle/README.md)) runs the armed test (0x4F5090), arm (0x4F50C0), undo
  (0x4F51B0) and use (0x4F5270) on 10,000 seeded inputs each (one PC with its points and armed flags, recording stubs of every
  write): no difference, two or three planted mutants caught per function (F3, receipts in
  [`docs/verification/receipts`](../verification/receipts)). The stat setter is a stub there: its clamp to 100 (FT7) is applied
  by the test to the written count, so the refund cap rests on the static reading (F2).
- Installed original (opt-in): the dialog census below.

## Fate kinds

| Kind | Number | Used by (original) | Steamwoven |
| --- | --- | --- | --- |
| Full heal | 0 | at once when armed (0x4F52D0) | `ArmHeroFate` applies it |
| Force good reaction | 1 | conversation start (0x4C1020) | `BeginReactionConversation` |
| Critical hit | 2 | the PC's attack (0x4B3170) | `AttackCritter` |
| Critical miss | 3 | an attack on the PC by a non-PC (0x4B3170) | read into the hero's `Combatant`; no NPC attacks yet |
| Save against magick | 4 | magic resistance (0x4537B0, 0x459C10) | `HeroMagickSave` (ARC-MAGIC-005), which uses it through `UseHeroFate` |
| Spell at maximum | 5 | the spell run (0x4510F0) | `UseHeroFate` (the magic lane) |
| Gambling, heal, pick pocket, repair, pick locks, disarm traps | 6..11 | the skill check (0x4C7160, SC14) | `HeroSkillFate` and `UseHeroFate` (the skill uses) |

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| State (FT1) | Points are stat 20, 0..100 (the stat setter's clamp), 0 for a new PC; kinds 1..11 have one armed bit each (1 << (kind − 1)) of field 0xFE; the full heal has none. | `FateState`, `FateRules.Mask` |
| Armed (FT2) | The kind's bit; the full heal is never armed. | `FateRules.IsArmed`, `GameSessionOwner.HeroFateArmed` |
| Arm (FT3) | Refused when the kind is armed, then when no point is left; else one point is spent and the bit set, or for the full heal the heal is applied at once. | `FateRules.Arm`, `ArmHeroFate` |
| Full heal (FT5) | Hit-point damage, poison and fatigue damage become 0; scars, blindness and crippled limbs stay. | `LifeRules.FateFullHeal` (in `ArmHeroFate`) |
| Undo (FT4) | An armed kind is cleared and its point refunded; at 100 points the refund is lost (FT7). | `FateRules.Undo`, `UndoHeroFate` |
| Use (FT6) | An event clears its armed kind, without a refund, and takes the fated outcome; a kind that is not armed is not used. | `FateRules.Use`, `UseHeroFate` |
| Grant (FT8) | The dialog action `fp` and the script command "grant one fate point" (PC only) add one point, up to 100. | `FateRules.Grant`, `GrantHeroFatePoint`, `SessionDialogFatePort` |

Each session call answers a `SessionFateResult` (disposition, failure, the rules' `FateChange`, the full heal's life outcome).
Refusals: no hero (`FateUnavailable`), an undefined kind (`InvalidFateKind`), the version, the rules' refusal (`FateRefused`),
the revision. Every change is one revision; the full heal writes the points and the life record in the same one.

## The readers

- **Attack** (`AttackCritter`): the hero's `Combatant` carries `CriticalHitFateArmed` and `CriticalMissFateArmed`; when the
  resolver reports `FateUse.AttackerCriticalHit`, the kind is cleared in its own revision before the damage steps.
- **Conversation start** (`BeginReactionConversation`, DR6): an armed "force good reaction" raises a start below 100 to 100 and is
  used in the same revision; at 100 or above it stays armed. The caller no longer passes a flag.
- **Skill check** (`HeroSkillFate(skill)`): the armed kind the hero's check of the skill would use, for
  `SkillCheckRequest.FateArmed`; the caller uses it with `UseHeroFate` when the result is fated.
- **Magic**: "save against magick" through `HeroMagickSave` (the resistance's fate port, ARC-MAGIC-005) and "spell at maximum"
  through `HeroFateArmed` and `UseHeroFate`.
- **Dialog** (`fp`, both NPC and Player actions, behind the optional `IDialogFatePort`; the AREA viewer passes
  `SessionDialogFatePort` with a session hero). `fp` takes no operand in this subset; one is outside it.

## Snapshot schema 26

The hero's object gains `fatePoints` (0..100) and `armedFates` (the bits of kinds 1..11); restore refuses anything else as
`InvalidHero`. Schema 25 is refused, not migrated ([game session](game-session-runtime.md#snapshot-schema-47)).

## Boundaries

- No fate window (FT8: twelve buttons, press arms, release undoes, a refused press pops back; it opens only for a living PC). The
  full heal of a dead hero is refused with nothing spent, which the original's window does not allow to happen.
- Not modelled: the network messages of a multiplayer client (FT3, FT4, FT8), the window's refresh callback, the script command
  "grant one fate point" (the SCR dispatcher does not run it yet), NPC attacks on the hero, the magic uses and the skill uses
  themselves (their lanes).

## Real-data census

`DialogFateRealDataTests` (category `RealData`, `DAT_ROOT` = the game folder), first observation 2026-10-21: 44 `fp` actions in
19 of the 709 DLG files (17 in NPC lines, 27 in replies), none with an operand and none in a condition; of the 44 fields, 13 parse
whole and 31 stop at another operation outside the subset, never at `fp`. The script command appears in 11 scripts
([SCR census](../research/scr-census.md)).

## Checks

`tests/Arcanum.Simulation.Tests/Character/FateRulesTests.cs`, `FateOriginalExeTests.cs` (the oracle, opt-in: `./arcanum oracle`),
`tests/Arcanum.Session.Tests/FateStateTests.cs` (arm, undo, use, grant, the full heal, the dead hero, the snapshot, the dialog port),
`AttackStateTests` (the fated attack), `ReactionStateTests` (the fated start), `tests/Arcanum.Scripting.Tests/Dialog/DialogFateTests.cs`.
