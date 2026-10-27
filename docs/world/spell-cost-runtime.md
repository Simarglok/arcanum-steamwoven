# Spell cost and payment runtime (ARC-MAGIC-003)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-003 (2026-10-21), slice MT5 "Cost and payment" of
the [magic implementation plan](../research/magic-implementation-plan.md). Godot-free pure rules in
`Arcanum.Simulation.Magic`: the original's three cost formulas, the affordability checks, the payment plan of a cast and
the recharge of a worn mana item. No session state, snapshot or viewer change: the run (MT9a) and the host apply a plan.

Requirements: [FR-MAGIC-004](../requirements/combat-magic-tech.md#fr-magic-004) (casting spends the stated resource).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell cost and payment"](../research/ledger/magic.md#spell-cost-and-payment-arc-magic-003),
rows PC1–PC12), and three functions were compared with the original in the function oracle on 10,000 seeded inputs each
(F3, receipts in [`docs/verification/receipts`](../verification/receipts)). Where the
[magic study](../research/magic-core-evidence.md#paying-fr-magic-004) differs, the ledger is the newer evidence.

Code: [`SpellCostRules`](../../src/Arcanum.Simulation/Magic/SpellCostRules.cs),
[`SpellPaymentRules`](../../src/Arcanum.Simulation/Magic/SpellPaymentRules.cs); tests:
[`tests/Arcanum.Simulation.Tests/Magic`](../../tests/Arcanum.Simulation.Tests/Magic).

## Inputs

The rules take the record's values (`Cost:`, `Maintain:`, `Duration:`, `ChargesFromCells:`; from the
[spell record reader](../formats/spell-records.md)) and the caster's facts as plain values, so they work for any critter
or item on any map: `SpellCaster` (is it a PC or NPC, its race stat, its mastered college or −1) and `SpellPayer` (none,
critter, item or other object; fatigue-limiting flag; the mana store of the item worn in each of the nine slots; current
fatigue and stored fatigue damage; an item's spell charges). Arithmetic is the original's 32-bit integer arithmetic.

## Cost formulas (MG1, MG2: the original's, no switch)

| Rule | Formula | Original |
| --- | --- | --- |
| `RunCost` (charged at BEGIN) | for a spell 0..79 of the caster's mastered college (spell / 5): `cost / 2` toward zero, `+ 1` when `cost` is odd; then 1 under the cheat flag; then `× 2` for a dwarf PC or NPC | 0x4507D0, oracle F3 |
| `CheckCost` (the spell UI's affordability check) | `cost`, 1 under the cheat flag, `× 2` for a dwarf PC or NPC; no mastery | 0x4508A0, oracle F3 |
| `PreAnimationCost` (the casting animation's check, MG1) | the cast spell's `Cost:` for a BEGIN, its maintenance cost otherwise; then as `CheckCost` for the run's source, which pays; not tested for a free run or a run without a source (`PreAnimationChecked`) | 0x450940 |
| `CastCost` (the spell UI shows it, the AI weighs it) | `× 2` for any object of the dwarf race (no PC/NPC test), then `/ 2` toward zero when spell / 5 is the mastered college (any spell number) | 0x4B1660, oracle F3 |
| `MaintainCost` (UI and AI) | maintenance cost `× 2` for a dwarf (any object); period `× 2` when spell / 5 is the mastered college; the cost is never halved | 0x4B16C0 |
| `RankPointCost` | the character points a rank costs or refunds: 1 | 0x4B1650 |

Worked example: `Cost: 7`, mastered college, dwarf: run time `7 / 2 = 3`, odd → 4, `× 2` = 8; UI `7 × 2 = 14 / 2 = 7`;
check 14.

A BEGIN pays (`BeginPays`, the stage's BEGIN branch 0x452F20) when the run is not free, its parent is a critter, the
record is not technology and it does not set `ChargeBeginCost: 0`; the run's source pays. (The same branch first ends a
cast whose critter source is dead or unconscious, and refuses a parent below intelligence 5 or the record's willpower for
a critter source: MT9a and MT7.) With no source, the run-time cost succeeds without paying while the UI check fails (the
two functions differ there; the rules take a payer, the null case is the caller's).

## Payment plan (0x450420)

`SpellPaymentRules.Pay(payer, cost, apply, spell, record, cells)` returns a `SpellPayment`: a status, the plan of what
to write and the "last cost" the original keeps for reflection (MG15, slice MT10). The steps in the original's order:

1. A spell number outside 0..222 that is not 10000 (no record) is refused (`SpellOutOfRange`).
2. A cost of 0 or less pays nothing (no last cost); no payer pays nothing (the last cost is kept).
3. **Charge cells first and alone**: a record with `ChargesFromCells:` asks the cell source port (`ISpellCellSource`,
   filled by the technology lane, [charge cells](charge-cell-runtime.md), ARC-TECHNOLOGY-007: a critter pays `cost` cells, an item source its surcharge, TC1) before any other
   payment step and pays nothing else; the source refusing is `CellsRefused`. The original spends cells **whether or not
   the payment is applied** (the affordability checks spend them too); the request carries the apply flag for the source.
4. **An item source** (object types 5..14) spends one spell charge when applied: 0 charges refuse (`NoCharges`), a negative
   store never runs out; a positive store marks the item magical even when not applied.
5. **A critter**: a fatigue-limiting critter pays half (toward zero, at least 1). The items worn in the nine slots pay
   from their mana stores first, slot by slot (a store covering the rest keeps the difference; a smaller one is emptied; a negative one is emptied and adds to the rest);
   when applied, each slot with a store gets a drain in the plan, also one of 0 after the cost is covered, as the original
   writes it and schedules its recharge; an emptied store is reported (the original notifies the AI). The rest is
   fatigue: refused when `current fatigue − rest ≤ −15` (`FatigueRefused`) — **the stores are drained all the same**
   (MG20, the original's behaviour). Without applying, the plan writes nothing and succeeds. When applied, the fatigue
   damage grows by the rest (the original's setter clamps a new damage of 0 or less to 0, writes it and reports a failure,
   `FatigueNotSet`, whose plan sets the damage to 0; a positive rest reaches it only from a negative stored damage); then a maintained record (a maintenance period) or one without a duration **fails after
   paying** when fatigue is now below 0 (`FailedAfterPay`), while a timed, non-maintained one lands. With no record
   (spell 10000) the cast lands.
6. Any other object pays nothing.

Knocking the caster out when fatigue reaches 0 or below, and the runs that ends, are the life state's and MT11's.

## Recharge of a worn mana item

Each drain schedules a recharge event 60 real seconds (480 game seconds) later; drains of the same item merge into its
pending event (0x459590, 0x459640; the event is MT11's). When it fires, `SpellCostRules.Recharge(elapsed, remaining)`
restores `2 × max(1, elapsed game seconds since the drain / 120)` points, at most what is left, and the rest is
rescheduled without merging (0x459680).

## Function oracle

`./arcanum oracle --function 0x4507D0` (and `0x4508A0`, `0x4B1660`) runs each function of the unchanged executable in the
emulator over a world model of one caster (type, race, mastered college), one spell record's cost, the cheat flag and, for
the two run-time and check functions, a recording payment stub; the `OriginalExe` tests (`SpellCostOriginalExeTests`)
compare `RunCost`, `CheckCost` and `CastCost` on every input and catch the planted mutants. Result: no difference on
10,000 inputs each ([tools/original-oracle](../../tools/original-oracle/README.md)).

## Differences from the original

Owner decision 2026-10-20: every difference and every fix is documented. MG1, MG2 and MG20 follow the original in both
rule modes; this slice adds no rule-mode switch.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| The run-time cost reads the spell table before testing the spell number, and the payment reads the record of a number outside the table on its critter path (out of bounds). | The rules take the record's values; a spell number outside 0..222 and not 10000 is refused at once (`SpellOutOfRange`), also where the original would succeed without reading it (a cost of 0 or less, no payer, an item or another object), and then no last cost is kept. No caller passes such a number. | both |
| The fatigue setter's failure (a new damage of 0 or less, written as 0) ends the payment. | The same (`FatigueNotSet`, the plan brings the damage to 0); reachable only with a negative stored damage. | both |
| A network client sends the payment to the host. | Single player only. | — |
| The plan is carried out inside the payment (fields written, recharge scheduled, AI notified, UI refreshed). | The plan says what to write; the host and the run apply it. | — |

## Not ready

- The run (MT9a) charges BEGIN and MAINTAIN through these rules. The character sheet's spells page (ARC-MAGIC-008) shows
  `CastCost` and `MaintainCost` in its hints, as the original's text bar does. The casting UI (MT20a) and the AI (I8) will show
  and weigh `CastCost`.
- The charge-cell amounts and refusals are the technology lane's (TC1), implemented by ARC-TECHNOLOGY-007 ([charge cells](charge-cell-runtime.md)); this slice defines only the port and its place.
- The recharge event (scheduling, merging, saving) is MT11's; reflection's use of the last cost is MT10's (MG15).
