# Spell run rules (ARC-MAGIC-009)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-009 (2026-10-21), slice MT9a-1 "Run creation and
payment stage" of the [magic implementation plan](../research/magic-implementation-plan.md) (the owner split MT9a into MT9a-1
and MT9a-2, ARC-MAGIC-010, on 2026-10-21). The original's run table, a run's creation, its stage at BEGIN and MAINTAIN with the
payments, its time intervals and the maintenance cap, as the original has them. Godot-free pure rules in
`Arcanum.Simulation.Magic` for any caster, item, target and map. The scheduler, the magic-tech events, the processing of the
actions and the interrupts are MT9a-2's; saved runs are MT9b's. Three rule-mode switches (MG3, MG11, MG19); no saved state.

Requirements: [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006) (a spell's life: begin, maintenance and its
payment, duration, the cap of maintained spells).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell runs"](../research/original-verification-ledger.md#spell-runs-arc-magic-009), rows RN1–RN14), and the time
scaling (0x455250), the stage (0x452F20 with its payment helpers) and the cap (0x4545E0) were compared with the original in the
function oracle on 10,000 seeded inputs each (F3, receipts [`455250.json`](../verification/receipts/455250.json),
[`452F20.json`](../verification/receipts/452F20.json), [`4545E0.json`](../verification/receipts/4545E0.json); re-run with
`./arcanum oracle --function 0x452F20` and the others). The Original setting matches on every input; the Steamwoven setting
differs only on MG3's inputs. Code: [`SpellRunRules`](../../src/Arcanum.Simulation/Magic/SpellRunRules.cs),
[`MagicAnomalyProfile`](../../src/Arcanum.Simulation/Magic/MagicAnomalyProfile.cs); tests:
[`SpellRunRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellRunRulesTests.cs),
[`SpellRunOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellRunOriginalExeTests.cs).

## The run and its table

`SpellRunTable` holds 512 slots; a run's id is its slot. A run (`SpellRun`) keeps its spell, current action (BEGIN 0,
MAINTAIN 1, END 2, CALLBACK 3, END_CALLBACK 4), the source (the object whose spell it is), the parent (the caster, or an item's
holder), the source's location at the start, the target object and tile, the other object, each captured at creation with its type and, for a
PC or NPC, its aptitude then; its flags (active; free; processed — set by every pass of the processing, MT9a-2; unresistable;
final END), the item triggers it was cast with, the resistance margin (written by the per-target stage) and the trigger count
left (`DurationTriggerCount:`).

## Creation (`Create`, 0x455C30)

Nothing happens while magic is switched off. The invocation check (MT7) runs first; a refusal creates nothing. The run takes the
first free slot; with all 512 taken the original quits the game (MG28) and here the cast is refused. Its flags: active; free when
the request asks it; unresistable when the request asks it (a script's unresistable cast) or the record has `No_Resist:`. Its
trigger count is `DurationTriggerCount:`. When the check replaced an NPC's target by a tile (MT7's fallback), the run aims at the
tile. How it starts is the caller's: with a parent the casting goal — 26, or 82 when the record names a casting animation, or 73
without the goal's animation — whose release frame starts BEGIN (the run is freed when the goal cannot start); without a parent a
magic-tech event starts BEGIN at once (MT9a-2).

## Stage (`Stage`, 0x452F20)

Before an action's components:

- **BEGIN.** A PC or NPC source that is dead or cannot act (fatigue gone, not undead) stops the run silently. For a PC or NPC
  parent and a magic record: intelligence below 5 and willpower below the record's minimum fail (only when the source is a PC or
  NPC: an item's spell ignores them); then the source pays the BEGIN cost (`SpellCostRules.RunCost`, `SpellPaymentRules.Pay`)
  unless the run is free or the record skips its BEGIN cost. A technology record, or a parent that is no PC or NPC, skips the
  checks and the cost. Then, when the record has stacking rules, a run of the spell already on the target (else the source) may
  block it (`ISpellRunStacking`, 0x453410; no refund), and the cancels run (0x4534E0) — both MT10's.
- **MAINTAIN.** For a PC or NPC parent — **the technology flag is not tested**, so maintained technology records (146, 149, 168,
  187, 209, 216) are checked and pay — the same checks; the maintenance (`Maintain:` cost, ×2 for a dwarf PC or NPC source, no
  mastery or cheat) unless the run is free or the intelligence check failed; with a resistance margin the surcharged maintenance
  (`cost + cost × margin / 100`, 32-bit and toward zero, a share of 0 counting 1; ×2 for a dwarf). Any failure turns the run to END; the caller empties its target list,
  processes the END at once and updates the local player's maintain bar.
- **END, CALLBACK, END_CALLBACK** go on.

The local player is told why (600 the cost, 601 the maintenance, 602 intelligence or willpower); anyone else is refused silently.
The payments are plans the host applies whatever the outcome.

## Times (`ScaleTime`, `DurationTime`, `MaintainTime`)

Game time runs 8 times as fast as a record's seconds. A maintained run ticks every `Maintain:` period: seconds × 1000 (0 counts a
millisecond), scaled, × 8. A timed run's interval: `Duration:` seconds (0 counts 1) plus `per point × max(0, level − stat)` with
`DurationStatInfo:` (the parent's stat for a PC or NPC parent, a PC or NPC target's without a parent), × 1000 (0 gives a second),
scaled, × 8. The scaling doubles the time for a spell of the college the PC or NPC parent mastered (0..79); the aptitude scaling
never applies (MG6). A timed run's trigger count: none left makes the next event its END (the final END flags); otherwise one
fewer (`CountTrigger`; the scheduler that calls it is MT9a-2).

## Maintenance cap (`WithinCap`, `MaintainSlots`, `MaintainedRuns`, `FreeMaintainSlots`)

A critter has `intelligence / 4` maintenance slots (toward zero). The cap (0x4545E0, asked by the scheduler before each tick of a
maintained run with a source) counts the runs holding the parent's slots — active, processed, of a magic record with a
maintenance period, that parent, the run itself included — and refuses when they exceed its slots; a run without a source is
within it and one whose parent is no PC or NPC is not. The count the AI and the spell UI ask (0x450AC0) answers 0 in the original
(MG11); Steamwoven counts like the cap, so a critter's free slots (0x450B40) drop as it maintains spells.

## Differences from the original

Owner decisions 2026-10-20: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| With a resistance margin a maintenance tick charges the plain maintenance and then the surcharged one (MG3). | Only the surcharged maintenance. | `MagicAnomalyProfile.MaintenancePaysBoth` (Original) |
| The maintained-run count answers 0, so free slots are always intelligence / 4 (MG11). | The runs holding the critter's slots are counted. | `MagicAnomalyProfile.MaintainedCountIsZero` (Original) |
| One run flag is both "unresistable" and "final END scheduled": an unresistable spell ends after BEGIN, a run whose END is scheduled is unresistable (MG19). | Two flags. | `MagicAnomalyProfile.UnresistableIsFinalEnd` (Original) |
| A full run table quits the game (MG28). | The cast is refused. | both (no-crash) |
| The cap reads a missing parent's type through handle 0 (MG26). | A missing parent is no PC or NPC: the cap refuses. | both (no-crash) |
| An aptitude difference that wraps to −100 divides by zero in the time scaling. | The time is left unscaled (unreachable with aptitudes of −100..100). | both (no-crash) |
| The cap asks the range test and ignores its answer. | Not asked. | — |
| A non-critter's aptitude and a missing target's type keep the slot's previous values. | No aptitude, no target. | — |
| The run reads its objects, the record and the globals directly; the presentation (facing, eye candy, casting goal) and the multiplayer packets are part of creation. | The caller passes the facts and starts the run; multiplayer is not modelled. | — |

MG6 (the aptitude scaling of durations never applies) follows the original in both modes, as decided. MG29 is recorded for the
owner: the surcharged maintenance is paid even for a free run, after a failed check, and — at least 1 — by a timed spell whose
maintenance cost is 0; both modes follow the original until the owner decides (recommendation in the
[plan](../research/magic-implementation-plan.md)).

## Not ready

- No caller: the processing, the scheduler's order, the magic-tech events, the maintain bar and the interrupts are MT9a-2's
  (ARC-MAGIC-010); the per-target stage (resistance, shells, reflection, stacking and cancels behind `ISpellRunStacking`) is
  MT10's; saved runs are MT9b's.
- The other object (the invocation's +0xA0, copied to the run's +0x108) is not carried by MT7's invocation yet, so a run's
  `Other` is always null; the slice that needs it (a spell aimed at a second object) adds it to the invocation.
- The casting goal and its eye candy are MT20a's.
