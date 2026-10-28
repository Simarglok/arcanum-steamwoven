# Pick pocket runtime (ARC-SKILLS-010)

Status: **Implemented bounded component**, ARC-SKILLS-010 (2026-10-28), task S15 of the [skills plan](../research/skills-plan.md),
on the session's [NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071) (ARC-WORLD-071), the
[shared skill check](skill-check-runtime.md), the fate points (ARC-WORLD-054) and the AI's attack event (ARC-WORLD-080, ARC-WORLD-092).
The hero steals an item from a living NPC or plants one of its own on it: the start's refusals, the check, the amounts that move, the
detection of a failure, the theft's alignment change and the consequences of a caught thief. Pure rules: `PickPocketRules` in
[`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills/PickPocketRules.cs); session: `PickPocket` in
[`GameSessionPickPocket.cs`](../../src/Arcanum.Session/GameSessionPickPocket.cs); viewer: the palette's **Pick pocket (then press an
NPC)** and the inventory window in steal mode in map mode, on any map
([`TerrainViewerAreaPickPocket.cs`](../../tools/terrain-viewer/Godot/TerrainViewerAreaPickPocket.cs),
[`AreaPickPocket.cs`](../../tools/terrain-viewer/Core/AreaPickPocket.cs)). No snapshot schema change: what a theft changes is the
inventory transfers', the alignment and the fate the hero's, the catching-thief script's slot map changes.
Requirements: [FR-ITEM-015](../requirements/items-economy.md#fr-item-015) (steal and plant),
[FR-CHAR-019](../requirements/characters-skills.md#fr-char-019) (a skill used).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`: rows PP1–PP8 of the [verification ledger](../research/ledger/skills.md#pick-pocket-arc-skills-010),
  read on 2026-10-21 by two independent readers who agree on every row, and PP9–PP11, one reading by this task (the refusal bits, the
  window's mode, the skill bar's file, the drop handlers without the quantity window, no stolen flag on the item, the check node's refused charge). No row is an
  anomaly that needs a rule-mode switch.
- The check, its difficulty and the fate: [SC1–SC17](../research/ledger/skills.md#skill-check-arc-skills-003) (pick pocket is opposed
  by the victim's perception; SC11 its worn-slot and size terms; SC14 its fate kind 8), FT6.
- Installed original: the native smoke below (outcomes and counts only).

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Start (PP1, PP9) | A dead thief, a stoned target (spell flag 0x10000) and the thief itself are refused silently; a target with critter flags 2 bit 0x80 ("no pickpocket") or critter flag 0x20000000 (mechanical) floats `skill_ui.mes` line 525, before the life test; a dead target is looted; a living one opens the window in steal mode only while the thief sees it. Followers are not refused; the start costs nothing. | `PickPocketRules.Open`, `NotAllowed` |
| Attempt (PP2) | Each drag is one attempt: 4 action points in the thief's subturn of a turn-based round, then the shared check. Steal-mode drags never ask how many (PP10). | `GameSessionOwner.PickPocket(..., world)` |
| Amount (PP3) | On a success: gold min(quantity, 100) (the rest of the pile stays), ammunition min(quantity, 20), any other item whole; a steal from the victim to the thief, a plant the other way. No item flag or worn slot refuses (a worn slot raises the difficulty, SC11). A move the insertion refuses is a failure that is never noticed; a critical success adds nothing. | `PickPocketRules.Amount` |
| Detection (PP4) | Only a failure: a critical failure always (a master is caught on one), any other only when the thief is untrained and the victim's `random(1, 20)` is at most its effective perception; a master planting never; a fated attempt is a critical success and never noticed. | `PickPocketRules.Detection`, `NoticedByRoll` |
| Alignment (PP5) | After a theft that moved something (never a plant, never from a victim the thief summoned): with `a` the victim's alignment and `b` the thief's, `a ≥ 0` lowers the thief's base by `trunc(trunc(a / 20) / 2)` to no less than `−100 − a`; `a < 0` with `b ≤ a` by `trunc(trunc(a / 20) / 2)` to no less than `a − 100`; else nothing. A base already at or below the bound stays. | `PickPocketRules.AlignmentAfterTheft` |
| Caught thief (PP7) | After a noticed failure: the thief's script at point 33 (answer ignored), then the victim's at point 11 (catching thief), whose answer other than run default stops the rest; then a victim led by the thief stops it; else the attack event of the thief on the victim (loudness 1). Nothing else changes reaction, alignment or marks. | `PickPocketRules.Caught` |
| No experience, no stolen flag (PP6, PP10) | No branch grants experience; a theft sets no item flag 0x8000 (only the sale check reads it, BT3). | — |

`PickPocket(version, victim, direction, item, newStackKind, insertCheck, world?, scripts?, triggers?)` takes a `SessionPickPocketVictim`
(the NPC, its effective perception ±1,000, its alignment, its state for the check's awareness term, whether it is unaware of the hero,
whether the hero summoned it), the direction, the item (a listed item of the victim for a steal, one of the hero's for a plant) and,
for ammunition, the kind of a new stack. Refused before anything is charged or rolled: no hero or a dead one (`PickPocketUnavailable`),
victim facts out of bounds or a victim that is no living NPC of the actor's map seed (`InvalidPickPocketVictim`), the version, no seed
(`InventoryUnavailable`), an item the side does not hold (`ItemNotAvailable`; the hero's coins too, a count and not a pile), a gold pile
with an insert-item script (`ItemScriptUnsupported`), an item the victim wears (`WornItemUnsupported`: the original steals it, the
session does not unequip an NPC), ammunition without a stack kind (`InvalidAmmo`), a key or key ring (`ItemTypeUnsupported`). With a world in a turn-based round, another's subturn or an inactive
hero is `PickPocketOutOfTurn`; in the hero's subturn it charges 4 action points first (its own revision), and a charge the hero
cannot pay ends its subturn and refuses the attempt (`PickPocketOutOfTurn` with the charge). The draws come from the session generator: the check's two d100, then the
perception die when it is rolled. On a success the move runs through `TakeGoldFromNpc`, `TakeAmmoFromNpc`, `TakeItemFromNpc`,
`GiveAmmoToNpc` or `GiveItemToNpc`; the check, the move, the alignment and the fate share the move's revision, and an attempt that moves
nothing takes one revision of its own. The check's draws are committed before the move (whose item scripts and pickup or removal
triggers draw after them) and put back when the move is refused for any reason but the insert check (or throws), unless a host's call back into the owner
committed state meanwhile. The thief's alignment `b` is the hero's stored alignment (the
session has no effects on alignment). A move refused for any reason but the insert check (a script the session does not run, a
budget, the revision) refuses the attempt after the charge, with nothing rolled or moved. The answer carries the check, the amount
moved, the move's own result, the die, whether the failure was noticed, the new alignment and whether the fate was used.

## Viewer

The palette's **Pick pocket (then press an NPC)** stands for the skill bar's pick-pocket button (the bar's button is a later task)
and needs the map's AI host (normal play has it; the smokes turn it on). The next left press on an NPC of the map's seed walks within
reach 1 and runs the start (`AreaPickPocket.Start`): the hero's sight of the target (0x4AF260 through the AI host) is taken with the
hero turned toward it, as the viewer does not track the hero's facing; a press anywhere else, a walk or another click cancels it. A
dead target opens its loot window as any body. The steal window lists the victim's current items with **Steal:** buttons (no Take
all, no quantity window) and the hero's with **Plant:** buttons; each press is one attempt with the AI host's victim facts
(`AreaPickPocket.Victim`: effective perception, stored alignment, unconscious, asleep or stunned, unaware when out of combat mode and
neither seeing nor hearing the hero at normal loudness) and the round's world in a turn-based round. The status shows `skill_ui.mes`
line 500 or 501 (the project's text when the file does not read). A noticed failure runs `AreaPickPocket.Caught`: the victim's
catching-thief script through the use-script host (`RunCatchingThief`, its slot kept as `scr11.*` map changes), the leader test and
the AI host's attack event of the hero on the victim, then the window closes.

## Boundaries (NOT READY)

- **Walk per attempt:** the original opens the window at any distance the thief sees and each drag's goal walks to range 1; the
  viewer walks before it opens and the attempts do not walk (the world waits while the window is open).
- **The thief's own script** (point 33) is not run: the viewer's hero carries no scripts. A victim's catching-thief script that the
  script host cannot run (an action outside its admission set, no content) stops the consequences as Not ready, with nothing applied.
- **Worn items:** the original steals a worn item (its slot raises the difficulty, SC11); the session refuses it, as it does not
  unequip an NPC (the slot's difficulty term is in the check for the day it does).
- **Not modelled:** the victim's "item stolen" mark (critter flags 2 bit 0x01, PP6; its one reader found is a PC victim's inventory
  window, and no NPC steals from the hero here), NPC thieves and script action 115 (PP8, SX5), the plant of the hero's coins, summoned
  victims (the viewer has none), the floating text over the thief (the status line shows it), the skill bar's button and the hotkey
  bar's recent entry, the window's behaviour after a caught thief (the viewer closes it; the original's was not read), the
  multiplayer packets.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/PickPocketRulesTests.cs` (start order, amounts, the detection table, the alignment bounds, the
caught order), `tests/Arcanum.Session.Tests/PickPocketStateTests.cs` (a theft in one revision with its alignment, the summoned
victim, 100 coins and 20 units, plants, the perception die and the critical failure, a trained failure and a master's plant never
noticed, the fate, a refused insertion, the turn-based charge, refusals before any roll), the viewer self-test (the palette's control
count) and the native smoke `ARC_AREA_MAP_SMOKE_ONLY=pickpocket` with `--hero` (only when named; marker
`ARC-SKILLS-010 native-smoke pickpocket PASS`): the AI host on, the hero next to a living free NPC with a plain item, the palette arms,
the start opens the steal window, a fated theft and a fated plant move the item, then up to ten plain thefts until one is noticed
(a noticed one closes the window after its consequences); a critter the flags refuse floats the refusal.
