# Repair runtime (ARC-SKILLS-007)

Status: **Implemented bounded component**, ARC-SKILLS-007 (2026-10-21), task S11 of the [skills plan](../research/skills-plan.md),
on the session's [item durability](item-durability-runtime.md) (ARC-WORLD-053), the [shared skill check](skill-check-runtime.md)
and the [fate points](fate-points-runtime.md). The hero repairs one of its items with the Repair skill: the check (whose roll
changes nothing), the amount, the loss of hp_adj, a master's mending of a broken item and the fate full repair. Rules:
`ItemDurabilityRules.SkillRepairAmount`, `RepairAdjustmentPercent` and `Repair` in
[`Arcanum.Simulation.Items`](../../src/Arcanum.Simulation/Items/ItemDurabilityRules.cs); session: `RepairWithSkill` in
[`GameSessionRepair.cs`](../../src/Arcanum.Session/GameSessionRepair.cs); viewer: **Repair** in the AREA inventory window. No
snapshot schema change (the condition is the durability section's record).
Requirements: [FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (durability and repair),
[FR-CHAR-019](../requirements/characters-skills.md#fr-char-019) (a skill used), [FR-CHAR-017](../requirements/characters-skills.md#fr-char-017)
(tech skills).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`, read statically per the [original executable check](../research/original-executable-check.md)
  by two independent readers: rows RP1–RP5 of the [verification ledger](../research/original-verification-ledger.md#repair-arc-skills-007).
  They agree on every row; SK7 (the roll changes nothing) is the original's behaviour, no rule-mode switch.
- RP2 corrects two details of the item rules: the 5 % loss for a broken item is a master's only (a repairer who is not a master
  keeps the training table), and the skill's own run restores nothing at an effectiveness of 0. `RepairAdjustmentPercent` now
  follows RP2; the service's `RepairAmount` (forced, at least 1) is unchanged.

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Roll (RP1) | Every roll counts as a success: the roll and the critical change nothing. | `RepairWithSkill` |
| Nothing to repair (RP2) | An item without damage, unless broken and the repairer a master, is not touched. | `ItemRepairStatus.NothingToRepair` |
| Amount (RP2, RP3) | The whole maximum with the fate full repair, else `trunc(maximum × effectiveness / 100)`, at least 1 only above an effectiveness of 0. | `ItemDurabilityRules.SkillRepairAmount` |
| Loss (RP2) | hp_adj falls by 10 / 5 / 1 / 0 % of the maximum by training; 5 % when a master mends a broken item, which clears the current art's broken bit only. | `RepairAdjustmentPercent`, `Repair` |
| Fate (RP3) | The armed repair fate is used after the rolls, also when there is nothing to repair. | `SessionRepairResult.FateUsed` |
| Start (RP4) | The hero is always the repairer (no follower); any item it holds (grid or wear slot); no action points; nothing refused before the check. | `RepairWithSkill` |

`RepairWithSkill(version, entity, baseline)` takes the item's T0 condition (the host's: the kit's from the hero load, a taken item's
from its object row). Refused before any roll: no hero, or a dead or unconscious one (`RepairUnavailable`), an invalid entity or
baseline (`InvalidItem`), an item the hero does not hold or the session deleted (`ItemNotAvailable`), the version. Then the check,
the fate and the item's record are one revision, also when nothing is repaired; a worn item's values follow its record.

## Viewer

The AREA inventory window lists **Repair:** for each item in the PC's list that is damaged or broken (its condition is the session's
record over the hero load's or the object row's baseline) and runs `RepairWithSkill`. The window reports the hit points repaired,
"nothing to repair" or "the repair restored nothing". The original's skill bar accepts any item and answers "nothing to repair"
for an undamaged one; the viewer lists only items that need it.

## Boundaries (NOT READY)

- **Not modelled:** the paid repair service of a merchant (the trade window, its quote and payment, ARC-UI-050), the dialog, AI
  and script uses of the skill, repairing an item an NPC holds or one in a container, the floating text and its messages, the
  multiplayer records.
- The broken bit of the inventory art stays set after a master's mending, as in the original; the viewer draws no item art.

## Checks

`tests/Arcanum.Simulation.Tests/Items/ItemDurabilityRulesTests.cs` (the skill amount, the master's 5 %, an amount of 0),
`tests/Arcanum.Session.Tests/RepairStateTests.cs` (a repair whatever the roll in one revision, nothing to repair with the fate used,
the fate full repair and a master's mending, refusals), `tests/Arcanum.TerrainViewer.Tests/AreaRepairTests.cs`, the viewer self-test
(the repair button) and the native smoke (`ARC-SKILLS-007 native-smoke repair PASS`: an original hero's kit item takes 12 damage
and is repaired through the window's button).
