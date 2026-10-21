# Spell per-target stage (ARC-MAGIC-011)

Status: **Implemented bounded component; part of the run system, no viewer caller.** Work item ARC-MAGIC-011 (2026-10-21),
slice MT10 "Per-target stage" of the [magic implementation plan](../research/magic-implementation-plan.md). It covers what the
original does between a run's target list and its components:
- the anti-magic shell on the source and the target;
- full reflection, with the source/target swap and its cost;
- the per-target resistance (MT6), which writes the run's margin;
- no-stack;
- the cancels by `Cancels_SF:` and `Cancels_EnvSF:`;
- fate maximisation per action.

Godot-free rules in `Arcanum.Simulation.Magic` for any caster, item, target and map. The run system
([spell run rules](spell-run-rules.md)) calls them in the original's order. The world answers through the run system's port.

Requirements: [FR-MAGIC-005](../requirements/combat-magic-tech.md#fr-magic-005) (resistance per target and the shell),
[FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006) (stacking, cancels, the run's life),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (reflection).

## Verification

Two independent readers read every rule in the retail 1.0.7.4 `Arcanum.exe`
([ledger "Spell per-target stage"](../research/original-verification-ledger.md#spell-per-target-stage-arc-magic-011), PT1–PT10).

The function oracle compared three functions with the original on 10,000 seeded inputs each (F3):

| Function | Address | Receipt |
| --- | --- | --- |
| per-target stage | 0x455550 | [`455550.json`](../verification/receipts/455550.json) |
| no-stack test | 0x453410 | [`453410.json`](../verification/receipts/453410.json) |
| cancels | 0x4534E0 | [`4534E0.json`](../verification/receipts/4534E0.json) |

The Original setting matches on every input. The Steamwoven setting differs only on MG15's inputs.

Code:
- [`SpellTargetStage`](../../src/Arcanum.Simulation/Magic/SpellTargetStage.cs);
- the run system's per-target and stacking calls in [`SpellRunSystem`](../../src/Arcanum.Simulation/Magic/SpellRunSystem.cs);
- [`MagicAnomalyProfile`](../../src/Arcanum.Simulation/Magic/MagicAnomalyProfile.cs).

Tests:
- [`SpellTargetStageTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellTargetStageTests.cs);
- [`SpellRunSystemTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellRunSystemTests.cs);
- the three oracle tests in `SpellRunOriginalExeTests`.

## Order (O10)

The order follows the original in both modes:

1. The stage at BEGIN pays, then runs the no-stack test (MG16: a refused recast is paid), then the cancels.
2. The shell test on the source (0x453710, ARC-MAGIC-010).
3. For each target:
   1. the per-target stage: the shell, then reflection;
   2. the resistance;
   3. the components.

## Per-target stage (`SpellTargetStage.Decide`, 0x455550)

**Skipped targets.** A tile entry and the run's parent pass at once.

**Anti-magic shell.** A target under an anti-magic shell (spell flag 0x800) is skipped, unless the run itself set the shell.
The local player is told with spell.mes 603, and its maintain bar is refreshed.

**When reflection applies.** A target with full reflection (0x2000) turns the run, unless the record has `No_Reflect:`. So
does any later target of a run already reflected: the reflected mark stays for the run's life, so every later target and pass
turns on the caster. When the run turns:

1. **Mark.** The run is marked reflected.
2. **Payment.** This happens unless the run itself set the reflection.
   - Who pays: the caster of the target's reflection, which is the parent of the run whose components set it on the target
     (`FlagCaster`, 0x459040).
   - Original setting: the reflecting caster pays the last positive cost any payment charged (MG15).
   - Steamwoven setting: it pays the reflected run's own cost — its BEGIN cost at BEGIN, its maintenance at MAINTAIN,
     nothing otherwise.
   - The payment uses spell 10000 and is applied.
3. **Swap.** This happens unless the target is the run's source or the run set the reflection.
   - A source that itself reflects makes the spell fizzle on the target (602 to the local player).
   - Otherwise the run's source and the target swap: the components act on the caster.
   - After that target's components the run puts its source back (RP6).
   - When the resistance skips the swapped target, the original leaves the source swapped (MG34).

**Resistance.** The resistance (`SpellResistanceRules.Resist`, MT6) runs after the stage, with the run's BEGIN, unresistable
and fate-maximised facts. It writes the run's resistance margin (read by MAINTAIN's surcharge), reports the displayed margin
and the AI's notice of a shrugged aggressive spell, and skips a resisting target (the host shows spell.mes 643).

**Spell flags of an object.** These are the session's own, or the static object fields until P1 brings them into the
session (the port's `SpellFlags`).

## No-stack and cancels (`Blocking`, `Cancelled`, `EnvironmentAllows`)

**No-stack (0x453410).** At BEGIN, a record with `No_Stack:` is blocked by the first other active run of the same spell on the
run's target (or source) as its target object. For a self spell without a target object, the match is by its parent. A blocked
BEGIN is refused without a message and without a refund. Its interrupt then runs its END components.

**Cancels (0x4534E0).** The run then interrupts every other active run of the same source whose `Cancels_SF:` meets its own.
When the record has `Cancels_EnvSF:` and the environment allows it, every other active run of the same source is interrupted.
- The environment allows it when the local player carries all those spell flags, or the mask is detecting invisible alone and
  the player's perception is extraordinary (0x458A80).
- The second loop compares the record's own `Cancels_SF:` with its own `Cancels_EnvSF:`, not the other run's (MG33).
- No installed record has `Cancels_EnvSF:`.

**Cancelled runs during the pass.** The cancelled runs are interrupted while the starting run is being processed:
- in the Original setting the re-entrancy guard refuses their processing, so they are left without a tick or an END (MG30);
- Steamwoven processes them right after the starting run's pass.

## Fate maximisation (MG17)

The PC source's armed fate "spell at maximum" is used on every action with components (ARC-MAGIC-010, RP4). This follows the
original in both modes. An END can be maximised when its BEGIN was not.

## Differences from the original

Owner decisions 2026-10-20 and 2026-10-21: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A reflection charges its caster the global "last cost" of any payment (MG15). | The reflected run's own cost of the action. | `MagicAnomalyProfile.ReflectionPaysLastCost` (Original) |
| The cancels' interrupted runs are refused by the guard and left without a tick or an END (MG30). | Processed right after the pass (pending the owner, recommended). | `MagicAnomalyProfile.ReEntryDropsRuns` (Original) |
| A free removes only the run's ticks; its interrupt or process event can hit a new run in the reused slot (MG32). | Every event of the run goes (pending the owner, recommended). | `MagicAnomalyProfile.FreeKeepsOtherEvents` (Original) |
| The reflection's caster is also searched in the runs' object lists (+0x130, +0x134). | The target object only (the lists are not modelled yet). | — |
| The last-cost global also counts cost checks and component payments. | The rules count the stage's and the reflection's payments; a component's own payments are the host's. | — |

Pending the owner, both modes following the original for now:

- MG31 (an interrupt of a timed run with CALLBACK but no END_CALLBACK components does not end it; recommended keep);
- MG33 (the cancels' environment loop compares its own record; latent, recommended keep);
- MG34 (a resisted target after the reflection swap leaves the run's source swapped for the rest of the run; recommended
  Steamwoven fix with an Original switch: restore the source for a skipped target too).

## Not ready

- No viewer caller: the casting UI (MT20a).
- The components behind the swap are MT12–MT18's.
- The session's own spell flags of objects are P1's.
- Saved runs are MT9b's.
