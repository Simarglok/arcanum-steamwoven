# AI parameter table (ARC-DATA-033)

Status: **Implemented bounded component**, ARC-DATA-033. `Arcanum.Data.Characters.AiParameterTable`
reads the original's AI parameter table from the content stack, as the retail executable builds it
([ledger AP1, AP2](../research/ledger/npc-ai.md#ai-parameters-and-follow-consent-arc-data-033)).
An NPC's AI data field (field 282, `CombatNpcFields.AiData` of ARC-DATA-021) names its row. The follow
check reads fields 5..8 ([party runtime](party-runtime.md#follow-check-arcanumsimulationparty)); the
other fields (flee, combat, portals and others) are read and kept but not interpreted here.

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
  `atoi` (wrapping in 32 bits), and missing tokens leave the field 0. A row no entry filled is all zero,
  as the original's zero-initialised table.
- **Lookup.** `Row(index)` returns the 17 values for 0..149. For an index outside that range it
  returns null, where the original reads past its table (project policy).
- **Refusals.** A table that cannot be read is `BaseUnreadable` or `ModuleUnreadable`. A table the MES
  grammar does not admit (`MesFile`) is `BaseRefused` or `ModuleRefused`, instead of being read partially.
  A failing module table fails the whole load, where the original keeps rows 0..99 (project policy).

## Installed original

Opt-in `AiParameterTableRealDataTests` (`DAT_ROOT`):

- The base table fills 41 rows (entries 0..40) and the module's table fills one (100).
- Virgil's row 5 has the follow fields −1000, 2001, 2001 and 30.
- All 2,236 core NPCs of the 80 interiors read their fields. They name 21 different rows, all of them
  filled by an entry. The most common are 19 (1,240 NPCs), 0 (559), 2 (157), 10 (113) and 15 (75).

## Not yet

- The meaning of fields 0..4 and 9..16 (flee, combat and the others) and their use by an AI runtime.
- The viewer's companions (ARC-WORLD-037) read the follow fields from this table.
