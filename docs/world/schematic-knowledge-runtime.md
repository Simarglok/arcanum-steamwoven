# Schematic knowledge runtime

Status: **Implemented bounded component, not wired.** Work item ARC-TECHNOLOGY-003 (2026-10-21), slice TECH-3 of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted). The
learned and found schematic lists of a crafter (Data, over the [schematic catalogue](../formats/schematics.md)), the
found-schematic registration (Simulation) and the hero's found list in the session (snapshot schema 23). No viewer, reading
window or schematic window calls them yet.

Requirements: [FR-ITEM-017](../requirements/items-economy.md#fr-item-017) (found and learned schematics);
[FR-ITEM-010](../requirements/items-economy.md#fr-item-010) only for the written item's subtype and first text line.

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers: the first reading of ARC-RES-062
([learned and found lists](../research/technology-evidence.md#learned-and-found-lists)) and a second reading for this slice
([ledger "Schematic knowledge"](../research/original-verification-ledger.md#schematic-knowledge-arc-technology-003), rows
SK1–SK4 and TC12). Code: [`CombatFieldReader`](../../src/Arcanum.Data/Combat/CombatFieldReader.cs) (the written block),
[`SchematicCatalog.Tabs`](../../src/Arcanum.Data/Technology/SchematicCatalog.cs),
[`SchematicKnowledgeRules`](../../src/Arcanum.Simulation/Technology/SchematicKnowledgeRules.cs),
[`TechnologyRuleProfile`](../../src/Arcanum.Simulation/Technology/TechnologyRuleProfile.cs),
[`GameSessionSchematics`](../../src/Arcanum.Session/GameSessionSchematics.cs); tests
[`SchematicKnowledgeRulesTests`](../../tests/Arcanum.Simulation.Tests/Technology/SchematicKnowledgeRulesTests.cs),
[`SchematicKnowledgeStateTests`](../../tests/Arcanum.Session.Tests/SchematicKnowledgeStateTests.cs),
[`WrittenSchematicRealDataTests`](../../tests/Arcanum.Data.Tests/Technology/WrittenSchematicRealDataTests.cs).

## Written items

`CombatFields.Written` (`CombatWrittenFields`) holds a written item's (type 13) flags, subtype, first and last text line
(per-type slots 59..62, exe fields 201..204), for a prototype or an instance over it. Subtype 5 is a schematic document; its
first text line is its schematic number. `SessionWrittenFacts.FromCombatFields` gives the session what a reading needs.

## Tabs

`SchematicCatalog.Tabs(degrees, found, crafterIsPc, prototypeDiscipline)` gives per discipline 0..7:

- **learned**: the first "stored degree" records of the catalogue's full list (`SelectLearned`). Intelligence does not hide a
  learned schematic; it only lowers the effective expertise ([tech degrees](tech-degrees-runtime.md)).
- **found**, for a PC crafter only: the crafter's found numbers in list order, each in the discipline of
  `SchematicCatalog.Discipline` (the first product prototype's item discipline field 0x61; `SchematicCatalog.PrototypeDisciplines`
  reads it from prototypes). An NPC crafter (the dialog's `su`) has only the learned tab.

## Registration

Reading a written item (`SchematicKnowledgeRules.Read`, the reading start 0x56BB60, and `Register`, 0x4B0120):

| Document | Result |
| --- | --- |
| subtype other than 5 | `NotSchematic`: the host opens its reading window |
| a number in the found list (linear search) | `AlreadyKnown`: `mes\tech.mes` 88 (message type 13), the document stays |
| a new number | `Registered`: appended at the end, the document destroyed, then message 89 naming the schematic |
| number 0, Steamwoven (TC8) | `RefusedZero`: nothing registers, the document stays, the host shows no message |
| number 0, Original (TC8) | registered as any number (a record of zeros that can never be built) |

TC8 is the switch `TechnologyRuleProfile.RegistersSchematicZero` in `GameRules.Technology` (owner decision 2026-10-20, a
project improvement; [rule modes](rule-modes.md#registry)). One installed document is numbered 0.

## Session

- `QueryHeroFoundSchematics()`: the hero's found numbers in registration order (null without a hero).
- `ReadWrittenItem(expected, entity, facts)`: the hero reads a written item it holds (kit or taken). Refused before any effect:
  no hero or a dead one (`InvalidLifeTarget`, the reading start's dead-reader check), an invalid entity, the version, an item
  the hero does not hold (`ItemNotAvailable`) or that is not a written item (`InventoryMismatch`), a full list
  (`BudgetExceeded`, `GameSessionLimits.MaxFoundSchematics`, 128 by default: the original has no limit, the installed data 62
  found records) and a spent budget of durability records for the document's deletion (`BudgetExceeded`). A registration is one revision; the document is deleted as the [durability](item-durability-runtime.md)
  deletion does (it leaves the hero's lists and every seed). Every other outcome is NoChange with its `Reading`.
- **Snapshot (schema 23)**: `"schematics":{"found":[number]}` in registration order. Restore refuses a repeated number or a list
  without a hero (`InvalidSchematics`) and a list over the limit (`BudgetExceeded`). Schemas 1..22 are refused, not migrated.

## Differences from the original

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A schematic document numbered 0 registers schematic 0 (TC8). | Refused, the document kept. | switch |
| A found number whose discipline is outside 0..7 is counted past the window's eight-entry table (TC12). | Left out of the found tab. | both fixed |
| The found list has no limit. | 128 entries (twice the installed found records). | both |
| The registration does not check the reader. | The session reads only for the hero; NPC readers are not modelled. | — |

## Not ready

- No caller: the reading window (FR-ITEM-010) and the schematic window (TECH-5) come later; the host shows messages 88 and 89.
- No component gate or assembly (TECH-4).
- Five found records have no document (O3: placed on maps or given by scripts); no script path registers schematics.
