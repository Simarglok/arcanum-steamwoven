# Technology coverage re-audit: what is built, what is missing, and the queue

Status: **Audit refresh and queue**, work item ARC-RES-125 (2026-11-11), trimmed to the live part on 2026-11-11. It is the status refresh of
the [technology audit](technology-audit-res-104.md) (ARC-RES-104), the technology counterpart of the [magic audit](magic-audit-res-103.md): that audit holds the
matrix of 8 disciplines by 7 degrees and the original-game readings, so this note does **not** redo it. It says what "runs" does not prove and what is left.

Requirements (reference only, no coverage row changes): [FR-ITEM-011](../requirements/items-economy.md#fr-item-011), [FR-ITEM-016](../requirements/items-economy.md#fr-item-016),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017), [FR-ITEM-018](../requirements/items-economy.md#fr-item-018), [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020).

## 1. Answer

1. **The hero's chain is complete on the player's data.** Buy a degree on the sheet, open the schematic window (`T`, dialog `su`, script 129), assemble (118 recipes gate-checked), use the product, wear or fire it:
   covered by the smoke stages `tech-craft` (degrees 1 and 2, assembly, two foods, a firearm) and `tech-products`, and by unit tests. Degrees, expertise, the component gate, charge cells, item durability and repair,
   technology failure and magick cancel rolls, aptitude and its resistance rule are built. No hidden mechanic of the original is missing (the aptitude and expertise readers were checked in ARC-RES-114).
2. **Technology records: 80 of 83 run** on the hero and on an NPC. The census table in `TechCensusRealDataTests` (ARC-DEV-363, kept current by each slice that makes a component ready) holds three rows that are not RUNS:
   183 (PART: creature Summon with AIRedirect, E1-e), 184 (INERT: its Use component runs in the use-script host, ARC-MAGIC-048, which the census does not have) and 222 (PART: ObjFlag, the permanent polymorph).
   Over the 65 records an installed item carries: **63 RUNS, 1 PART, 1 INERT**.
3. **RUNS is a host verdict, not a behaviour check.** The census says every component of a record is taken; it does not say the flag it writes has a reader (section 4).
4. **What is left is short:** the viewer draw of hidden, shrunk and translucent critters and the shielded hit eye candy (E4-a2), creature Summon with AIRedirect (E1-e) and the AI's summoning (E1-f),
   the rest of the Trait polymorph (MA-8), and the smoke gaps of section 5. E1-e, E1-f, E4-a2 and MA-8 are shared with the magic queue ([magic audit](magic-audit-res-103.md)); they are listed once, here.

## 4. Readers of the flags the technology records write

A flag counts as read when a symbol outside the data tables uses it; behaviour against the original is a ledger question. Symbol searches can miss a reader that tests the raw bit.

| Flag (records) | Reader found | Note |
| --- | --- | --- |
| Fatigue limit (153, 195, 210) | yes (spell payment, combat, attack resolver) | |
| Blind (177, 186) | yes (`GameSessionBlinded`, life rules, ground rules) | |
| Flee (148, 165), mind control (142, 194), undead (194) | yes (morale rules, attack resolver, AI host; `SessionSpellCasterSocial` is their writer) | MA-4 |
| Magnetic inversion (182, 187) | yes (attack rules, equipment choice, skill check) | |
| Enshroud (167) | yes (AI host, hostility rules) | |
| **Drunk (172, 181)** | **no, and none in the original** | no code of the executable and no installed record reads the bit ([SFR2](ledger/spell-runs.md#readers-of-the-drunk-and-shielded-spell-flags-arc-res-126)); nothing to build |
| **Shielded (209)** | **no** (the `shielded` in `HostilityRules` is the accidental-hit test, not the spell bit) | the only reader is the hit effect 0x459A30, a hit eye candy (SFR3; built by ARC-MAGIC-045); record 83 lists it in `Disallowed_TSF:`, which the session reads (SFR4) |
| Don't-draw (178, 183, 200, 217) | written; `AreaAiHost` and traps read it | the viewer draw reads it since ARC-MAGIC-045 (`AreaBodyStyleRules`; with translucent, shrunk and invisible) |
| Permanent polymorph (222) | PART: the Trait art is built (ARC-MAGIC-040, -047, -052), the ObjFlag part is not | MA-8 |

## 5. Smoke and test gaps

Stages with a technology record on real data: `tech-craft`, `tech-products`, `grenade` (whatever the map lays) and `cast`. Not exercised on real data: a Chemistry, Therapeutics or Explosives product used,
a found schematic document read, a charge cell spent and an NPC crafter through `su`: see the progress note below. Per the smoke-scope rule these go into `tech-craft` or `tech-products` rather than a new runner.

**T-s progress (ARC-MAGIC-051, stage `tech-products`):** done: Therapeutics degree 1 and 2 potions (records 157 and 158) eaten with the strength +2 measured, a Chemistry poison (record 171) used on a living NPC through the item cursor,
effects and items kept across F7/F8; the seven found documents of Chemistry and Therapeutics read through the Read button, and the Found tab assembles record 4220 (4200 and 4210 were refused for expertise in the run);
Chemistry degree 2 power cells (one stack of 50), the Electric degree-3 powered shield worn (its 15 s call takes a cell, the next call a second one) and the degree-6 maintained armour (record 168 pays a cell a tick). The smoke found a real defect, fixed in the same slice: an assembled item lives in the made items,
which the script host's wear question (`WieldedItemName`) did not read, so the crafted shield's timer ended at its first call (unit test `AreaPoweredShieldCallTests`).
NPC crafter: the window queued through the field the dialog's `su` fills (`_schematicScriptOpen`, not an actual dialog reply) for the cave NPC, whose sheet the stage patches with a Herbology degree and intelligence 8 (the cave's NPCs have no degree; other maps were not searched for an installed crafter), assembles record 2000 from the NPC's own list into its list with the hero's pack unchanged; the dialog's own `su` reply stays covered by the synthetic self-test.
Open: an Explosives product (the timed bombs, items 15154 and 15193, are placed by ARC-WORLD-124 and could join), the cell weapon (degree 4, record 147: it fires only `Target_Hit|Target_Attacker`, when its wearer is hit).

## 6. Queue

Labels are planning labels, not task codes (the coordinator hands codes out). Sizes: S about 200 lines, M one PR with a smoke stage, L more than one slice.

Done since RES-104 (names the ledger rows cite): T-a ARC-DEV-362, T-b ARC-DEV-363, T-h ARC-DEV-364 and -373, T-g ARC-DEV-365, T-m3 ARC-DEV-366, T-j ARC-RES-116, T-m2 powered shield ARC-MAGIC-041,
T-m1 creature items ARC-MAGIC-042, T-c creation editor degree buttons ARC-MAGIC-043, E6-b placed trap devices ARC-WORLD-124, R-1 flag readers ARC-RES-126, R-2 record 214 ARC-RES-127,
E4-c control components ARC-MAGIC-044 and -048 (ledger [CN1–CN7](ledger/spell-runs.md#control-components-arc-magic-044)), E4-e nested Cast (record 203) ARC-MAGIC-049 (ledger [NC1–NC3](ledger/spell-runs.md#cast-component-arc-magic-049)),
E1-b, E1-c and E1-d (Summon of scenery, created critters, NPC effect list).

| Label | Content | Records / products | Size | Needs | Lane |
| --- | --- | --- | --- | --- | --- |
| **E4-a2** shielded hit eye candy and don't-draw draw | The hit effect 0x459A30 plays the run's hit eye candy for shielded (and magnetic inversion) at a hit on a PC or NPC (the entangled branch is built); drunk has none. Plus the viewer's draw of hidden, shrunk and translucent critters and of don't-draw objects | 172, 181, 209, 178, 183, 200, 217 | M | none | spell-cast and viewer |
| **MA-8** polymorph remainder (E4-f) | Trait: the ObjFlag part of the permanent polymorph; drop what the new art cannot wield, restore | 222 (and the Body spells of the magic queue) | M | none | render and equipment |
| **E1-e** creature Summon with AIRedirect | Prototype or list pick, summoned flags, faction; AIRedirect (E4-b remainder) | 183 and 11 summon spells | L | none | Summon file |
| **E1-f** AI summoning | Magic queue's last slice; touches no technology record | n/a | M | E1-e | AI host |
| **T-s** smoke extension | Section 5 gaps in `tech-craft` or `tech-products` | n/a | M | none | smoke |

Rows run one writer at a time in the spell-cast lane (E4-a2, E1-e, E1-f); MA-8 and T-s touch other files. The census row of 184 flips to RUNS when the census gains the use-script host.
