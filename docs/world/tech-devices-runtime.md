# Tech devices runtime

Status: **Bounded rules (Data, Simulation) and a session query; no host runs a device yet.** Work item ARC-TECHNOLOGY-008,
2026-10-21, the device part of slice TECH-6 of the [technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(the cell part is [ARC-TECHNOLOGY-007](charge-cell-runtime.md)). Which technology items and records reach the magic engine and
what each pays, that the engine's MAINTAIN charges a maintained technology record through the charge-cell source, and how a
device works for a holder (magick against technology). The engine's stage code is ARC-MAGIC-009's and is not changed. No saved
state is added (the snapshot schema is unchanged).

Requirements: [FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magick against technology),
[FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) (devices and their spells).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Tech devices"](../research/ledger/technology.md#tech-devices-arc-technology-008),
DV1–DV7, and ["Charge cells"](../research/ledger/technology.md#charge-cells-arc-technology-007) CC7). Code:
[`TechDeviceCatalogue`](../../src/Arcanum.Data/Technology/TechDeviceCatalogue.cs) (Data),
[`TechDeviceRules`](../../src/Arcanum.Simulation/Technology/TechDeviceRules.cs) (Simulation),
[`GameSessionCells`](../../src/Arcanum.Session/GameSessionCells.cs) (`QueryHeroDeviceUse`); tests
[`TechDeviceCatalogueTests`](../../tests/Arcanum.Data.Tests/Technology/TechDeviceCatalogueTests.cs),
[`TechDeviceRealDataTests`](../../tests/Arcanum.Data.Tests/Technology/TechDeviceRealDataTests.cs) (opt-in, `DAT_ROOT`),
[`TechMaintainCellsTests`](../../tests/Arcanum.Simulation.Tests/Technology/TechMaintainCellsTests.cs),
[`TechDeviceRulesTests`](../../tests/Arcanum.Simulation.Tests/Technology/TechDeviceRulesTests.cs) and
[`CellSourceStateTests`](../../tests/Arcanum.Session.Tests/CellSourceStateTests.cs).

## What reaches the engine (DV1, DV7)

`TechDeviceCatalogue.Build(items, records)` reads the five item spell slots of each item (10000 none, as the slot reader
0x4CB7A0) and lists every slot that names a technology record (140..222) as a `TechDeviceSlot` (prototype, object type,
complexity, slot, record, spell charges); a spell outside 0..222 is a `TechDeviceAnomaly`. Each technology record is a
`TechRecordEntry`: `Cost:`, the maintenance, `ChargesFromCells:`, its ChargeNBranch components (action and cost) and what it pays:

| When | What a technology record pays |
| --- | --- |
| BEGIN (the stage) | nothing: every record from 140 has the technology flag, and the stage skips the checks and the cost (CC7) |
| BEGIN components | a ChargeNBranch on the Begin action pays its cost (cells for a cell record, through the payment) |
| each MAINTAIN tick (the stage) | for a PC or NPC parent, the maintenance cost through the payment: cells for a cell record (`TechMaintainPayment.Cells`), else the source's payment (a critter's stores and fatigue, an item's spell charge; another payer pays nothing); a cost of 0 pays nothing |
| MAINTAIN components | a ChargeNBranch on the Maintain action pays its cost each tick |

The installed data (opt-in census): 70 item spell slots name technology records, 65 distinct records, on 70 items (61
technological; nine others: six food and three scrolls); charges −1 ×21, 0 ×2, 1 ×47; no slot out of range. The two devices without charges also lack the magical flag, so their use never takes
the slot-0 spell path (ledger DVD1). ARC-TECHNOLOGY-010 read why ([trap devices](../research/ledger/technology.md#trap-devices-arc-technology-010),
TP1–TP8): both carry generic flag 0x4 (trap device), and the original's use plants them as timed bombs through slot 1 (a marker on
the tile, a "Traps" event 88,000 game ms later, then a source-less run of the record there); the trap-device path is not implemented. Maintained
technology records: 146, 149 (maintenance 1 every 10 s, the source's payment), 168, 187 (maintenance 0: cells come from their
ChargeNBranch, 168 one cell a MAINTAIN tick, 187 one at BEGIN), 209, 216 (maintenance 0). Cell records 147 (one weapon, BEGIN
branch), 168 (three armours, Maintain branch), 187 (no item). Only 168 is both maintained and carried.

## MAINTAIN with charge cells (CC7)

The engine's stage (`SpellRunRules.Stage`, ARC-MAGIC-009) tests no technology flag at MAINTAIN: for a PC or NPC parent it pays
the maintenance through `SpellPaymentRules.Pay` with the run's `ISpellCellSource`. With ARC-TECHNOLOGY-007's
`ChargeCellSource`, a maintained cell record pays its cells each tick in both TC1 settings (the amount differs), and a source with
too few cells refuses: the run turns to END with message 601 for the local player. `TechMaintainCellsTests` runs the stage tick
after tick over a worn device whose host writes each applied plan.

The same over game time with the run system (ARC-MAGIC-010): `TechMaintainCellsTests` begins the run, fires each tick the run
system queues (40,000 game ms for a 5-second maintenance) and writes each plan: the cells are paid tick after tick in both TC1
settings until a tick finds too few, which ends the run and frees it: the end-to-end MAINTAIN test of MT9a-2 is **done**
([#369](https://github.com/Simarglok/arcanum-steamwoven/pull/369), ARC-MAGIC-010). ARC-TECHNOLOGY-009 adds that the MG29 switch
(`SurchargeAlways`) still pays through the cell source: with a resistance margin Steamwoven pays the surcharged maintenance once,
Original the plain one and then the surcharge, and for a free run Original takes the surcharge from the cells while Steamwoven takes
nothing. **Not yet tested:** the ChargeNBranch components (the component runner is a later magic slice).

## A device for its holder (DV2–DV6)

`TechDeviceRules.Use(complexity, aptitude, weapon)` returns a `TechDeviceUse`:

- **Usable**: outside combat nothing refuses a technological item to a mage (DV2): the use, the item trigger executor and the
  run creation read neither complexity nor aptitude for a technology record; the holder gate (0x456A10) refuses only a magickal
  item to a technologist who draws no power from it, and only the AI's item scan asks it. `Usable` is that gate's answer, so a
  technological device is always usable.
- **Power** and **PowerRatio** (0x4614A0, 0x461540): what the holder draws; it scales the item's magic adjustments (a mage
  takes a technological item's penalties in full) and a cell-powered spell's need (TC1).
- **CombatFailureChance** for a weapon (DV4, 0x461700): `aptitude × |complexity| / 100` for a technological weapon in a mage's
  hands; the attack resolver rolls it before the hit roll (not for a throw) and a failure is a critical miss
  (`AttackRules.TechnologyFailureChance`, now marked). The opposed magick cancel by the target's aptitude (DV5, 0x461620) is
  `AttackRules.MagickCancelChance`.

`GameSessionOwner.QueryHeroDeviceUse(complexity, weapon)` answers it for the hero with the hero's effective aptitude; null
without a hero or for a complexity out of range. It changes nothing.

The equipment's refusal of items with spells (`ItemEffectUnsupported`) is not aptitude. Since ARC-MAGIC-020 it holds only without
an item trigger host: with one, the wear and unwear executors run the item's spells ([item spell triggers](item-spell-triggers.md),
IT1–IT9; record 168's three armours start their maintained run on the wear).

## Not implemented

The background refusals of DV6 are implemented by ARC-WORLD-069 ([backgrounds](equipment-runtime.md#backgrounds-arc-world-069)).

- Running a device's spells: the item trigger executor (DV3), the use's spell path, item spell charges spent by the payment's
  item branch in session state, and the lift of the equipment's refusal (the shared item trigger slice).
- The ChargeNBranch components (the end-to-end MAINTAIN over game time is done, ARC-MAGIC-010, #369).
