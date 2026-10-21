# Spell run rules (ARC-MAGIC-009, ARC-MAGIC-010)

Status: **Implemented bounded component; held by the session, no viewer caller.** Work items ARC-MAGIC-009 (2026-10-21, slice
MT9a-1 "Run creation and payment stage") and ARC-MAGIC-010 (2026-10-21, slice MT9a-2 "Scheduler and interrupts") of the
[magic implementation plan](../research/magic-implementation-plan.md) (the owner split MT9a into the two on 2026-10-21). The
original's run table, a run's creation, its stage at BEGIN and MAINTAIN with the payments, its time intervals and the maintenance
cap (MT9a-1); the processing of each action, the scheduler, the magic-tech events and the interrupts (MT9a-2), as the original has
them. Godot-free rules in `Arcanum.Simulation.Magic` for any caster, item, target and map, over ports for the world and the
components; the per-target stage, the no-stack test and the cancels are ARC-MAGIC-011's ([spell per-target
stage](spell-target-stage.md)); the session holds the runs and queues their events. Saved runs are MT9b's. Rule-mode switches MG3,
MG11, MG19, MG29 (and, with the per-target stage, MG15, MG30, MG32); runs and their events are not saved yet.

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
magic-tech event processes its BEGIN 2 ms later (code 3; ARC-MAGIC-010, RP14).

## Stage (`Stage`, 0x452F20)

Before an action's components:

- **BEGIN.** A PC or NPC source that is dead or cannot act (fatigue gone, not undead) stops the run silently. For a PC or NPC
  parent and a magic record: intelligence below 5 and willpower below the record's minimum fail (only when the source is a PC or
  NPC: an item's spell ignores them); then the source pays the BEGIN cost (`SpellCostRules.RunCost`, `SpellPaymentRules.Pay`)
  unless the run is free or the record skips its BEGIN cost. A technology record, or a parent that is no PC or NPC, skips the
  checks and the cost. Then, when the record has stacking rules, a run of the spell already on the target (else the source) may
  block it (0x453410; no refund), and the cancels run (0x4534E0) — the [per-target stage](spell-target-stage.md)'s rules
  (ARC-MAGIC-011).
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

## Processing and scheduling (ARC-MAGIC-010)

Read in the executable by two readers ([ledger "Spell run processing"](../research/original-verification-ledger.md#spell-run-processing-arc-magic-010),
RP1–RP14); the scheduler (0x453FA0 with its tick helpers) and the interrupt's action (0x457110) were compared with the original in
the function oracle on 10,000 seeded inputs each (F3, receipts [`453FA0.json`](../verification/receipts/453FA0.json),
[`457110.json`](../verification/receipts/457110.json)). Code: [`SpellRunSystem`](../../src/Arcanum.Simulation/Magic/SpellRunSystem.cs)
(one per host call: the table, the re-entrancy and callback guards, the events to queue, the ticks to remove and the host notes),
`SpellRunScheduler.Decide` (the scheduler as a pure decision) and the session's
[`GameSessionSpellRuns`](../../src/Arcanum.Session/GameSessionSpellRuns.cs); tests
[`SpellRunSystemTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellRunSystemTests.cs),
[`SessionSpellRunTests`](../../tests/Arcanum.Session.Tests/SessionSpellRunTests.cs) and the two oracle tests in
`SpellRunOriginalExeTests`.

**A processing pass** (0x451070, 0x4510F0):

1. A run is refused while another one is processed (the re-entrancy guard): nothing happens to it (a host note). Otherwise the
   pass's state starts empty, the run is marked processed and the guard set.
2. At BEGIN the forbidden flags (MT7's 0x456430); every action: the stage (MT9a-1; END, CALLBACK and END_CALLBACK pass it, a
   failed MAINTAIN ends the run at once); a source under an anti-magic shell refuses BEGIN and MAINTAIN unless the run itself set
   the shell (602 to the local player). A refusal of BEGIN or MAINTAIN ends the run by event: its interrupt 1 ms later, so its
   END components still run.
3. An action without components counts as applied. Otherwise the target list (MT8, the host's port); a PC source's armed fate
   "spell at maximum" is used on every such pass (both aptitudes 100); an empty list counts as applied. For each target: the
   per-target stage (the shell and reflection, [ARC-MAGIC-011](spell-target-stage.md)) may skip it; a resisted target (MT6, the run's
   margin written) is reported and skipped; each component's gate (its area
   test and re-aim, a port) and item triggers (TA15), then applied, the AI notice (a note) and the component itself (a port): it may
   set spell flags on the run, jump to another component, let go of the run's source, or ask for its callback (pending while its
   animation plays, or at once when the animation could not start). When the source was let go, the current target becomes the
   run's source after its components, for every later target too.
4. The guard drops and the scheduler decides.

**The scheduler** (0x453FA0): an immediate callback runs the run's CALLBACK (or END_CALLBACK) once, and its own pass frees the
run; BEGIN, MAINTAIN and CALLBACK with the final END mark end; a maintained spell ends when nothing was applied, with a source
checks the cap (601 and END at once over it) and ticks after its period (the local player's magic spell needs a maintain-bar slot,
or it ends at once), without a source counts its ticks (the record's count, or 8) and ends after them; a timed spell ends when
nothing was applied or it has no duration, else counts its trigger and ticks after its duration; the tail ends a run without
callback components (or a CALLBACK without END components), turns END_CALLBACK into END and leaves a BEGIN waiting for its pending
callback; an END frees the run unless its callback is pending. Ending a run processes its END at once.

**Events and interrupts.** A run keeps at most one tick. A magic-tech event fires (0x459430): a tick processes the run (BEGIN turning
to MAINTAIN), an interrupt interrupts it, a process event processes its current action (the BEGIN of a run without a parent, 2 ms
after its creation). The interrupt (0x457110) turns MAINTAIN to END when the record has END_CALLBACK components, else to CALLBACK
with CALLBACK components, else END, and any other action to END_CALLBACK, CALLBACK or END the same way, removes the tick and
processes the run. Further: BEGIN (the casting goal's release frame), an animation's CALLBACK or END_CALLBACK, the free (charged
when the goal ended before BEGIN), the end by event, the forced end, the source detached (the final END mark unless it happens in
the run's own pass, MG19) or cleared, an object's runs losing their source, a fainting source's runs ending (0x457450), the
script's stop of a cast (0x4573D0).

**In the session.** `StartSpellRun` creates a run (and queues the BEGIN of a run without a parent); `ActOnSpellRun` carries out one of
the actions above with the host's world; the event kind `magic.run` (values: the time it was set at, the run, the code) runs in the
session's event pass through an `ISessionSpellWorld` host, and a pass with another host consumes it and reports it not run. Runs
and `magic.run` events are not saved (the kind is `NotSaved`) until MT9b: a restored session has neither.

## Differences from the original

Owner decisions 2026-10-20: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| With a resistance margin a maintenance tick charges the plain maintenance and then the surcharged one (MG3). | Only the surcharged maintenance. | `MagicAnomalyProfile.MaintenancePaysBoth` (Original) |
| The surcharged maintenance is charged by a free run, after a failed check or payment, and — at least 1 — by a timed spell whose maintenance cost is 0 (MG29). | None in those three cases (owner decision 2026-10-21). | `MagicAnomalyProfile.SurchargeAlways` (Original) |
| The maintained-run count answers 0, so free slots are always intelligence / 4 (MG11). | The runs holding the critter's slots are counted. | `MagicAnomalyProfile.MaintainedCountIsZero` (Original) |
| One run flag is both "unresistable" and "final END scheduled": an unresistable spell ends after BEGIN, a run whose END is scheduled is unresistable (MG19). | Two flags. | `MagicAnomalyProfile.UnresistableIsFinalEnd` (Original) |
| A full run table quits the game (MG28). | The cast is refused. | both (no-crash) |
| The cap reads a missing parent's type through handle 0 (MG26). | A missing parent is no PC or NPC: the cap refuses. | both (no-crash) |
| An aptitude difference that wraps to −100 divides by zero in the time scaling. | The time is left unscaled (unreachable with aptitudes of −100..100). | both (no-crash) |
| The cap asks the range test and ignores its answer. | Not asked. | — |
| A network client's processing returns at once; the host sends packets for frees, ticks and maintain-bar slots. | Single player only. | — |
| The guard of nested event removals (0x5E7604). | Not modelled: every removal is made. | — |
| A tick that cannot be queued (a full queue) takes the scheduler's failure branches. | The session refuses an action whose events do not fit; the branches stay in the decision (oracle-checked). | — |
| A non-critter's aptitude and a missing target's type keep the slot's previous values. | No aptitude, no target. | — |
| The run reads its objects, the record and the globals directly; the presentation (facing, eye candy, casting goal) and the multiplayer packets are part of creation. | The caller passes the facts and starts the run; multiplayer is not modelled. | — |

MG6 (the aptitude scaling of durations never applies) follows the original in both modes, as decided. Three more are recorded for the owner by ARC-MAGIC-010, each following the
original in both modes until decided: MG30 (the cancels' interrupts are refused by the guard and leave runs without a tick or an
END), MG31 (interrupting a timed run with CALLBACK but no END_CALLBACK components does not end it; one technology record), MG32 (an
interrupt or process event outlives the free and can hit a new run in the reused slot). ARC-MAGIC-011 built the recommended fixes
of MG30 and MG32 as switches (`ReEntryDropsRuns`, `FreeKeepsOtherEvents`; still pending the owner); MG31 keeps the original. The session keeps the runs unchanged when
an action's or event's queued events do not fit; what the host's ports already did (a component's effect) is the host's.

## Not ready

- No viewer caller: the casting UI (MT20a) starts runs and their BEGIN; Damage and Heal are ARC-MAGIC-012's ([spell damage and heal](spell-damage-heal.md), the host's `Apply` calls `SpellLifeComponents`), the other components MT13–MT18's; the maintain bar is MT20b's;
  the endings' callers (fainting, rest, travel, death) are MT11's; saved runs and events are MT9b's.
- The other object (the invocation's +0xA0, copied to the run's +0x108) is not carried by MT7's invocation yet, so a run's
  `Other` is always null; the slice that needs it (a spell aimed at a second object) adds it to the invocation.
- The casting goal and its eye candy are MT20a's.
- While the rules act, the session refuses calls back into its runs and an event pass from a host port (so nothing is overwritten
  or consumed unrun); a component that casts a spell (MT12–MT18) will need its own path, as the original's guard refuses only
  processing.
