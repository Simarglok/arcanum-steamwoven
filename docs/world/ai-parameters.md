# AI parameter table (ARC-DATA-033, ARC-WORLD-077)

Status: **Implemented bounded component**, ARC-DATA-033; typed rows by ARC-WORLD-077 (NPC combat AI
plan slice [I1](../research/npc-combat-ai-plan.md#phase-1-faithful-reconstruction)).
`Arcanum.Data.Characters.AiParameterTable` reads the original's AI parameter table from the content
stack, as the retail executable builds it
([ledger AP1, AP2, AP6](../research/ledger/npc-ai.md#ai-parameters-and-follow-consent-arc-data-033)).
An NPC's AI data field (field 282, `CombatNpcFields.AiData` of ARC-DATA-021) names its row. Each row is
an `AiParameterRow` with the 17 slots named after the
[A1 parameter dictionary](../research/npc-ai-core-loop.md#4-parameter-dictionary). The follow check
reads slots 5..8 ([party runtime](party-runtime.md#follow-check-arcanumsimulationparty)); the other
slots are read, named and kept, and each one's rule lands with its slice (I2..I9).

## Rules

- **Sources.** Rows 0..99 come from `rules/ai_params.mes`, read by the AI init before the module is
  mounted, so from the first base source (ARC-DATA-027, `ContentLoadPhase.GameStart`). Rows 100..149
  come from the module's `rules/gameai.mes`, read at module load (the content stack's winner). A
  missing base table is `BaseMissing`: the original's init fails. A missing module table is no
  failure.
- **Rows.** Each part starts at the entry numbered exactly 0 (100). That entry and every later entry in
  number order fill consecutive rows, whatever their numbers, up to row 99 (149). A part without its
  first entry fills nothing.
- **Fields.** A row's text is split on space, tab and line feed. Its first 17 tokens are read like C
  `atoi` (wrapping in 32 bits). The original leaves the fields after the last token as they were; in a
  table built once they are zero, so missing tokens leave the field 0 here (see *One table per stack*).
  A row no entry filled is all zero, as the original's zero-initialised table.
- **One table per stack.** `Open` and `Create` build a fresh table each time, as the original's first
  module load in a process. The original never clears its table: the module unload keeps rows
  100..149 and a later module load (the module-load dispatcher unloads and reloads; ledger AP6)
  writes only the tokens it reads. Reloading the same module writes the same values again, so the
  two agree. They differ only when another module's `rules/gameai.mes` is loaded in the same process:
  the original then keeps the earlier module's trailing fields of a shorter row and every row past
  the new table's end; Steamwoven starts that module from zero (project policy in both settings; one
  module is installed, so shipped data never shows it).
- **Lookup.** `Lookup(index)` or `Lookup(CombatNpcFields)` (the AI data field) returns an
  `AiParameterLookup`: `Found` with the row for 0..149, `OutsideTable` with no row for any other index,
  where the original reads past its table (ledger LM3; project policy in both settings).
- **Refusals.** A table that cannot be read is `BaseUnreadable` or `ModuleUnreadable`. A table the MES
  grammar does not admit (`MesFile`) is `BaseRefused` or `ModuleRefused`, instead of being read partially.
  A failing module table fails the whole load, where the original keeps rows 0..99 (project policy).

## Slots

Names, units and the readers that compare them are the A1 dictionary's; the rule of each use is its
stage's (A3..A7, S22) and lands with the matching slice, not here.

| Slot | `AiParameterRow` | Unit | Stage |
| ---: | --- | --- | --- |
| 0 | `FleeHpPercent` | % of own HP | A4 |
| 1 | `FleeOutnumbered` | critters | A4 |
| 2 | `FleeOutlevelled` | levels | A4 |
| 3 | `StandWhileTargetWeakPercent` | % of the target's HP | A4 |
| 4 | `FleeDistance` | tiles | A4 |
| 5 | `FollowReaction` | reaction | A7 (follow check, used) |
| 6 | `FollowAlignmentAbove` | alignment | A7 (follow check, used) |
| 7 | `FollowAlignmentBelow` | alignment | A7 (follow check, used) |
| 8 | `JoinLevelGap` | levels | A7 (follow check, used) |
| 9 | `FollowerHitReaction` | reaction change | A3 |
| 10 | `HostileReaction` | reaction | A1, A3 |
| 11 | `HostileAlignmentGap` | alignment | A3 |
| 12 | `GoodFollowerStop` | alignment | A3, A7 |
| 13 | `DefensiveSpellRoll` | d100 threshold | A5 |
| 14 | `CombatHealChance` | % (d100) | A5 |
| 15 | `CombatDistance` | tiles | A4, A5 |
| 16 | `OpensPortals` | flag (nonzero set), kept as stored | S22 |

`Values` gives the 17 in slot order; a row is a value (equal rows compare equal).

## Installed original

Opt-in `AiParameterTableRealDataTests` (`DAT_ROOT`, category `RealData`):

- The base table fills 41 rows (entries 0..40) and the module's table fills one (100).
- Virgil's row 5 has the follow fields −1000, 2001, 2001 and 30.
- By slot name (ARC-WORLD-077): slot 11 is 2001 in all 41 base rows, slot 16 is 1 in 39 and 0 in 2,
  slot 3 is 101 in 8; slot 0 spans 0..100, slot 10 −1000..0 and slot 15 0..5. The module's row 100
  has 14 values, so its slots 14..16 are 0
  ([census](../research/combat-data-census.md#2-ai-parameter-rows)).
- All 2,236 core NPCs of the 80 interiors read their fields. They name 21 different rows, all of them
  filled by an entry. The most common are 19 (1,240 NPCs), 0 (559), 2 (157), 10 (113) and 15 (75).

## Not yet

- The rules of slots 0..4 and 9..16 (flee, hostility, spells, healing, distance, portals) and an AI
  runtime that uses them: plan slices I2..I9. `ReactionHitFacts` (slots 9 and 10) is still filled by
  its caller, not from this table.
- External data overrides of rows (plan item M2).
- The viewer's companions (ARC-WORLD-037) read the follow slots from this table.
