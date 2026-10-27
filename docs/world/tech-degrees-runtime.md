# Tech degrees runtime

Status: **Implemented bounded component, not wired.** Work item ARC-TECHNOLOGY-002 (2026-10-21), slice TECH-2 of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted).
Godot-free degree rules in `Arcanum.Simulation.Character` and the hero's degrees and expertise in `Arcanum.Session`, bought
and refunded through the [character point pool](character-point-pool.md) (ARC-WORLD-052). No snapshot schema change: the
degrees stay in the hero's advancement fields (`techDegrees`, [auto-level schemes](advancement-runtime.md)).

Requirements: [FR-CHAR-017](../requirements/characters-skills.md#fr-char-017) and
[FR-CHAR-020](../requirements/characters-skills.md#fr-char-020) (their degree part: degrees cost tech points, which move the
aptitude), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (the degree rules of the editor; no UI).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers: the first reading of ARC-RES-062
([disciplines and degrees](../research/technology-evidence.md#disciplines-and-degrees-tech-module-0x4afcd00x4b0340)) and a
second reading for this slice ([ledger "Tech degrees"](../research/ledger/technology.md#tech-degrees-arc-technology-002),
rows TD1–TD9). Code: [`TechDegreeRules`](../../src/Arcanum.Simulation/Character/TechDegreeRules.cs),
[`GameSessionTechnology`](../../src/Arcanum.Session/GameSessionTechnology.cs); tests
[`TechDegreeRulesTests`](../../tests/Arcanum.Simulation.Tests/Character/TechDegreeRulesTests.cs),
[`TechDegreeStateTests`](../../tests/Arcanum.Session.Tests/TechDegreeStateTests.cs).

## Rules

| Rule | Original | Steamwoven |
| --- | --- | --- |
| Stored degree (0x4AFE60) | a PC's or NPC's degree of the discipline (0..7), 0 for any other object | `TechDegreeRules.Degree(sheet, discipline)`, null sheet for a non-critter |
| Raise (0x4AFEC0) | cap 7; the **effective** intelligence must reach the new degree's minimum {0, 5, 8, 11, 13, 15, 17, 19}; the degree rises by one and the base tech points by the cost {0, 1, 1, 1, 1, 1, 1, 1} | `Raise`: `AtMaximum`, `IntelligenceTooLow`; the tables are the auto-level rules' (`AutoLevelRules.DegreeCost`, `DegreeMinimumIntelligence`) |
| Lower (0x4AFFF0) | a degree above 0 falls by one and the base tech points by its cost; no intelligence check | `Lower`: `AtMinimum` at 0 |
| Effective expertise (0x4B00B0) | the stored degree lowered while the effective intelligence is below its minimum, then {0, 10, 20, 35, 50, 65, 80, 100} through the "expertise in discipline" effects (percent, multiply, divide, add, minimum, maximum, every cause), no clamp | `EffectiveExpertise`, `CountingDegree`; `EffectModifiers.Apply` is the aggregation |
| Intelligence guard (0x4B02B0, the base setter 0x4B0980) | a base intelligence decrease is refused when the new effective intelligence no longer holds every known spell rank and then every degree; effects are not guarded | the pool's stat refund (`CharacterPointRules.Guard`: `SpellNeedsStat`, `DegreeNeedsStat`); a temporary intelligence drop only lowers the counting degree |

## Buying and refunding

The editor's buy and sell wrappers (0x589A20, 0x589AF0) are the pool's technology lane, `TechDegreeRules.Lane`
(`IPointLane`): `SpendHeroPoint(expected, AdvancementTrack.TechDiscipline, discipline)` raises one degree for one character
point, `RefundHeroPoint` lowers one and credits the point. The pool checks the unspent points, the open editor and the floor:

- **Order of the buy (0x589A20):** a degree at 7 is "at maximum"; then the cost of the next degree against the unspent
  points (`NotEnoughPoints`); then the raise (`IntelligenceTooLow`). The original reads the cost of degree 8 one past its
  table (the next table's 0), so at the cap it, too, ends at the raise's refusal. Since ARC-MAGIC-004 the lane also states
  that cost (`IPointLane.Cost`: the next degree's, 0 at degree 7), which the pool tests before it asks for the raise.
- **TC10, owner decision 2026-10-20, both modes, no switch:** a degree is sold only down to the value it had when the
  character editor opened (the pool's floor, as for skills and colleges). The original's sell wrapper has no such compare;
  the owner's play experience is that a point invested earlier cannot be taken back.
- The base intelligence a degree needs cannot be refunded while the degree stands (`DegreeNeedsStat`); refund the degree first.

## Session

`QueryHeroTechnology()` gives the hero's eight disciplines now: the stored degree, the degree its effective intelligence
(effects included) lets count, and the effective expertise with the tech-discipline changes of its durable
effects (every cause; counted on the installed data with the repository's effect reader on 2026-10-21: 9 of 310 rules have such a
change, all adds, none a race or the female effect, whose discipline changes the session therefore does not carry). These
changes were inert until now ([durable effects](durable-effects-runtime.md)); the schematic component gate (TECH-4) and the
character sheet will read the expertise. A bought degree moves the tech points and so the aptitude
([character stats](character-stats-runtime.md)). NPC degrees stay in their advancement records or their map data; `QueryNpcTechnology` (ARC-TECHNOLOGY-006, [NPC crafter](schematic-assembly-runtime.md#npc-crafter-arc-technology-006))
reads them for the NPC schematic crafter.

## Differences from the original

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A degree sold in the editor is not compared with the snapshot taken when the editor opened (TC10). | Down to the floor, as every other track. | both (owner decision) |
| A network client sends the raise and lower to the host. | Single player only. | — |
| The editor reads the unspent points through the effect system (stat 21). | The stored value, as the pool (no modelled effect changes stat 21). | both |
| At degree 7 the buy wrapper reads the cost one past its table. | No read past a table: "at maximum" first, the same result. | both |
| The editor shows a refusal message (charedit 18 for too few points, 26 for a refused raise). | The typed status (`IntelligenceTooLow`, `AtMaximum`, `AtMinimum`, `NotEnoughPoints`, `AtFloor`); the texts are the sheet task's. | — |

## Not ready

- No caller of the buy and refund: the character sheet's + and − buttons (skills plan S6) come later; the viewer's
  [schematic window](schematic-window-runtime.md) (ARC-TECHNOLOGY-005) reads the degrees and expertise.
- The schematic knowledge (TECH-3) and the component gate (TECH-4) do not read the expertise yet.
- Spell ranks in the intelligence guard are read from the sheet; the spell lane is the magic slice MT3.
- The race and gender effects' tech-discipline changes are not carried into the expertise (no installed one has any); a
  module with one would need them carried as the innate skill changes are.
