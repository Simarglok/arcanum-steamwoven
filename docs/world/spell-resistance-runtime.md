# Spell resistance runtime (ARC-MAGIC-005)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-005 (2026-10-21), slice MT6 "Resistance and save"
of the [magic implementation plan](../research/magic-implementation-plan.md). The per-target resolver of a magic action (the
margin, or a skip) and the fail-chance predictor the AI and the spell UI ask, as the original has them. Godot-free pure rules
in `Arcanum.Simulation.Magic`; the session supplies the labelled seeded rolls and the hero's fate save. The first magic slice
with a rule-mode switch: `MagicAnomalyProfile` in `GameRules` (MG4). No saved state.

Requirements: [FR-MAGIC-005](../requirements/combat-magic-tech.md#fr-magic-005) (aptitude, resistance and fate give a
predictable outcome), [FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magic against technology: technology
records skip the resistance, a technologist's negative aptitude raises it).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell resistance"](../research/original-verification-ledger.md#spell-resistance-arc-magic-005), rows RS1–RS10), and the
resistance (0x4537B0) and the predictor (0x453B20) were compared with the original in the function oracle on 10,000 seeded inputs
each (F3, receipts [`4537B0.json`](../verification/receipts/4537B0.json), [`453B20.json`](../verification/receipts/453B20.json);
re-run with `./arcanum oracle --function 0x4537B0` and `0x453B20`). The Original setting matches on every input; the Steamwoven
setting differs only where MG4's penalty reaches the save. Code:
[`SpellResistanceRules`](../../src/Arcanum.Simulation/Magic/SpellResistanceRules.cs),
[`MagicAnomalyProfile`](../../src/Arcanum.Simulation/Magic/MagicAnomalyProfile.cs),
[`GameSessionMagic`](../../src/Arcanum.Session/GameSessionMagic.cs); tests:
[`SpellResistanceRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellResistanceRulesTests.cs),
[`SpellResistanceOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellResistanceOriginalExeTests.cs),
[`SpellResistanceSessionTests`](../../tests/Arcanum.Session.Tests/SpellResistanceSessionTests.cs).

## Inputs

The rules take what the original reads as plain values, so they work for any target on any map:

- `SpellResistRecord`: the spell number (Disintegrate, 34, has a special case), technology (`Is_Tech:` and every record from
  140), a damage component, aggressive `Info:`, the maintenance period, the `Resist:` stat (−1 for none) and modifier — the
  [spell record](../formats/spell-records.md)'s values.
- `SpellResistTarget`: whether it is the run's parent (the caster), PC, NPC or another object, the "no disintegrate" critter
  flag, the **stored** magic resistance (the object's resistance 4: no armour, effects or 0..95 clamp, O9, both modes), the
  effective magick/tech aptitude, the effective value of the `Resist:` stat and whether it is extraordinary (a PC or NPC, a
  primary stat, 20 or more).
- `SpellResistRun`: BEGIN or not, unresistable (`No_Resist:` records and the script's "cast unresistable", MG19), and
  fate-maximised ("spell at maximum" fired for this action).
- The rolls (`ISpellRollSource`, purposes `ResistanceD100`, `SaveD20`, `FailChanceD20`) and the PC target's fate save
  (`ISpellFateSave`, asked only for a PC target that is not the caster).

## Resistance (`Resist`, 0x4537B0)

1. An action other than BEGIN, a technology record or an unresistable run: applies, and the run keeps its previous margin.
2. Both margins become 0. Disintegrate against a PC or NPC with "no disintegrate": skipped.
3. For a target other than the caster: a PC whose armed fate "save against magick" fires gets margin **10** and displayed
   margin **100** (MG13, both modes). Otherwise `R` is the stored resistance, raised for a negative aptitude `A` unless the action
   is fate-maximised: `R = 100 − (100 − R) × (A + 100) / 100` (toward zero). A d100 below `R` makes the margin `R − roll`. For
   `R > 0`, a record without damage and a maintenance period not above 0: with a save the penalty is `margin / 10`; without one
   a roll below `R` skips the target.
4. The caster, a record without a save, or a target that is no PC or NPC: applies with the margin.
5. A willpower save against an extraordinary willpower: skipped; the target's AI is told when the record is aggressive.
6. The save `S = stat − penalty + modifier` (**Steamwoven: `+ penalty`**, MG4). `S ≤ 0` or a d20 above `S`: applies. Otherwise the
   run's margin grows by `S − d20` and the displayed margin is set to it (a fate's display 100 is replaced); a damaging record
   applies with at least 50; a record whose maintenance period is other than 0 applies; any other is skipped.

`SpellResistance` answers whether the spell applies, the run's margin and the displayed margin (null when step 1 returned),
the penalty and the AI notice. Worked example: resistance 30 and aptitude −40 give `R = 100 − 70 × 60 / 100 = 58`; a d100 of
20 gives margin 38.

## Fail-chance predictor (`FailChance`, 0x453B20; `ItemFailChance`, 0x453CC0)

The predictor follows the resistance's shape but differs (MG14, both modes): a technology record answers 0; it uses `R`
instead of a rolled margin (a record without a save answers 100 above `R` 40, a save's penalty is `R / 10`); the caster, a
record without a save or an object answers `R`; an extraordinary willpower against a willpower save answers 100; a failed or
impossible save answers `R`; a made save answers at least 50 for a damaging record, `R` for a maintained one, 100 for any
other ("maintained" there is a period other than 0, as in step 6). It rolls its **own d20** (`FailChanceD20`) whenever the save is reached, so asking it moves the random stream, as in the
original. It reads the fate-maximised flag of the last processed action (the original's global), which the caller passes.
`ItemFailChance` answers it for an item's first spell when the item is magical (spell charges or the magical flag) and 0
otherwise. The AI refuses a cast at 38 or more (RS7, read by one reader, F1; slice I8).

## Session

- `SpellRolls`: the session's seeded source; each roll is a session roll labelled `magic.` and the purpose, so it moves the saved
  random position and replays after a restore. A roll does not move the revision.
- `HeroMagickSave`: the hero's fate save against magick from the fate owner (ARC-WORLD-054): asking it uses the armed
  `FateKind.SaveAgainstMagick` through `UseHeroFate` (0x4F5270, one revision when it fires) and answers whether it was armed.
- `Rules.Magic`: the active `MagicAnomalyProfile` (`SaveSubtractsResistance`, Original `true`).

## Differences from the original

Owner decisions 2026-10-20: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| The resistance term (a tenth of the margin; of `R` in the predictor) is subtracted from the saving throw (MG4). | Added, as the manual says (+1 per 10 %), in the resistance and the predictor. | `MagicAnomalyProfile.SaveSubtractsResistance` (Original) |
| The resolver reads its run, record, target and `Resist:` pair from globals and writes the two margins into the run and a global. | The caller passes the facts and applies the answer. | — |
| The predictor reads the fate-maximised flag the last processed action left, whatever the run it is asked about. | The caller passes that flag. | — |
| A network game and the "resisted" effect are the caller's. | The caller's (MT10). | — |

O9 (the stored resistance), MG13 (fate margin 10, display 100, and the save still rolls) and MG14 (the predictor's own d20)
follow the original in both modes, as decided.

## Not ready

- No caller: the per-target stage (MT10) runs the resolver for each target and applies the margin to the components
  (MT12–MT18); the AI (slice I8) and the casting UI (MT20a) ask the predictor.
- The entangle effect's re-resistance (0x459C10, RS10) is not implemented (slice MT13).
