# Technology placement census: found schematics, `su` crafters and the non-craftable items

Status: **Research (data census)**, work item ARC-RES-115 (2026-11-11), item T-r4 of the [technology audit](technology-audit-res-104.md) (ARC-RES-104). Docs-only: no source, test or build
change and no repository checks run (the census ran locally on the owner's Mac with a throwaway program outside the repository, built on the repository's own `Arcanum.Data`
readers; it is not a repository check). Counts and prototype numbers only: no object or text names, no coordinates, nothing derived from the game is committed.

Requirements (reference only, no coverage row changes): [FR-ITEM-016](../requirements/items-economy.md#fr-item-016), [FR-ITEM-017](../requirements/items-economy.md#fr-item-017),
[FR-ITEM-018](../requirements/items-economy.md#fr-item-018), [FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011),
[FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012), [FR-CHAR-017](../requirements/characters-skills.md#fr-char-017), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020).
It answers the open questions O3 and O5 of the [technology research](technology-evidence.md#open-questions) and the third question of the audit's read T-r4 (where the 146 non-craftable
technological items lie). The original behaviour is not re-derived here: where a count depends on a rule (item generation from inventory sources, the inventory flag), the rule is the ledger's
([RK2 to RK6](ledger/items.md), [RL3](../architecture/original-object-model.md)).

## 1. Answer

1. **In the data this census follows, found schematics reach the hero in two ways: a document lying on a map, or a chance line of a generated inventory source.** No verb registers a schematic
   (per the [SCR census](scr-census.md) the one schematic verb of the script catalogue, action 129 `start_schematic_ui`, is used by no script; the dialog action table has only `su`, which opens the window). Of the 62 found records, **20 have a document lying on a map** (24 document
   instances on 8 maps: 22 in containers, 2 in NPC packs; 10 of the 20 are also in a source set), **30 more appear only as lines of five generated source sets** that 15 placed holders use
   (14 containers of two prototypes and one NPC; chances 2 to 75 percent), **7 have a document that none of the followed paths places** (section 2 lists what is not followed), and **5 have no written prototype at all** (O3).
   So **5 of the 62 found records cannot be obtained at all** (nothing can give a missing prototype), **7 are not found anywhere in what was scanned**, and 30 come only by chance.
2. **O5: six NPC instances carry the five `su` dialogs** (five on the main map, one in a second map; script 1650 is on two NPCs). They have stored degrees (4 to 8 learned records each in
   the window), but each holds one to five items, mostly worn, and **none holds both components of any record its window lists**: the window opens and cannot assemble anything from the NPC's
   own stock. An NPC crafter has no found tab. 68 NPC instances in all carry stored degrees (52 of them in Gun Smithy); the other 62 have no `su` dialog and no script action 129.
3. **The 146 non-craftable technological items:** 122 lie on maps (1,600 instances on 29 maps; 1,178 on the main map), 6 only as lines of sets that placed holders use, 1 only in the set of
   an NPC prototype that no map places, 1 (a scroll) only through a script of an unplaced NPC prototype, and **16 nowhere** (6 weapons, 5 armours, 1 food, 4 generic items). Only 9 of the 146
   carry a technology record. Among the 124 craftable items **73 appear nowhere but as a crafted product**; 49 also lie on maps (531 instances).
4. **Records:** of the 65 carried technology records, 36 have a carrier lying on a map. Of the 15 records carried only by items that no schematic makes, 12 have such a carrier on a map and 3
   (191, 207, 208: scrolls) only through scripts of two NPC prototypes that no map places. Of the 16 Summon-dependent records the items carry, 9 have a carrier lying on a map (19 instances).
5. **Two side findings.** The schematic document of number 0 (the technology research's TC8 case: reading it registers a record that can never be built) is **placed ten times** (3 maps), so the anomaly is
   reachable in play. 164 items (144 on the main map, 17 in Tulla Interior, 3 elsewhere) have the inventory flag but no holder (75 of them technological); the original never lists such an item in a sector, so they are inert and are
   not counted as placed anywhere below.

## 2. Method and limits

- **Data:** the installed game read through the repository's own content stack (`ContentStack`, `WorldCatalog`, the map patch as an area plan binds it, `SchematicCatalog`,
  `InventorySourceRules`, `ScriptContent`), module `Arcanum` only (81 maps; the mobile aggregates the installation holds for all 81, 15,470 object rows of types 2, 5 to 16 and every object with an
  attached script, and every sector file's tile and sector scripts). The 798 item prototypes come from the prototype files the stack reads. Item holders were resolved through the objects' own
  ID digests (7,466 of 7,467 held items resolved; one item of one map names a parent the aggregate does not hold).
- **Baseline check:** the census reproduces the audit's figures exactly (798 item prototypes, 270 technological = complexity below 0, 124 craftable = a product of a learned or found record,
  146 not, per discipline 10, 15, 11, 11, 25, 22, 38, 14; 61 technological items with a technology record, 65 records on 70 items; 56 learned, 62 found and 8 placeholder records).
- **"Lies on a map"** is an instance in a map's mobile aggregate that has a holder or no inventory flag: on the ground, in a container's list, or in an NPC's list (worn or in the pack). There
  are no containers, items or critters in any sector file (static objects there are walls, portals, scenery and traps only). **"Set"** is a line of `rules\InvenSource.mes` (the basic
  prototype number plus 20 is the prototype, the rate a percent chance per generation, RK6); a set counts as used when a placed container or NPC (instance field, else its prototype's) names it.
- **Not followed** (so the counts are floors for "nowhere"): scripts reached only by a call or a change of script, objects a script or a monster generator creates at run time, sets named by a
  script, the second module `Vormantown`, and anything a save adds. Script creation was read as `create_item` (action 66) and `create_object` (52) with a literal number, read as the basic number
  plus 20: all 60 `create_item` numbers resolve under that reading. Five source sets (113, 115, 116, 119, 120) resemble sets 5, 7, 11, 13, 14 (set 113 equals set 5; the others add or drop a few lines and change some chances, for
  example 5440 at 100 percent in set 120 against 75 in set 14) and are named by no placed holder or prototype; if something names them, 5250 and 5440 change.
- **Shop stock** is the loot lines of `rules\InvenSource.mes` (the set's coins and rate/prototype pairs, RK6). The lines of `rules\InvenSourceBuy.mes` say what a merchant *buys* (BT8) and give
  no items, so they were not read; no other stock list exists.
- **Evidence level:** F0 (data observation by the project's readers, no executable read). Nothing here changes an original-behaviour verdict.

## 3. Found schematics (O3)

### 3.1 Documents and records

58 written prototypes have subtype 5 (schematic). 57 point at a found record, one points at number 0 (TC8). Five found records have none: Gun Smithy 4810 and 4900, Smithy 5270 and 5280,
Therapeutics 5430. The discipline of a record is its first product's, as in the audit; a record's number block does not give it (4020, 4440, 5050, 5070 are Mechanical in the 4000 to 5200
range).

By discipline (a record is "placed" when at least one of its document instances lies on a map, "set" when a source set that a placed holder uses has a line for its document):

| Discipline | Records | Placed (also set) | Placed only | Set only | Document, nowhere | No document |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Herbology | 4 | 2 | 0 | 0 | 2 | 0 |
| Chemistry | 4 | 1 | 3 | 0 | 0 | 0 |
| Electric | 5 | 1 | 0 | 4 | 0 | 0 |
| Explosives | 14 | 1 | 1 | 12 | 0 | 0 |
| Gun Smithy | 12 | 2 | 0 | 7 | 1 | 2 |
| Mechanical | 12 | 2 | 3 | 3 | 4 | 0 |
| Smithy | 7 | 1 | 0 | 4 | 0 | 2 |
| Therapeutics | 4 | 0 | 3 | 0 | 0 | 1 |
| **Total** | 62 | 10 | 10 | 30 | 7 | 5 |

### 3.2 Where the documents lie

- **On maps:** 24 instances of found documents: the main map 12 (10 in containers, 2 in NPC packs), Wheel Clan 4, Stonecutter Clan 2, BMC 2, and one each in Tarant Sewers 01 and 02, Ashbury
  Scienticians Underground and Scienticians Underground level 2. None of the 24 lies on the ground.
- **In sets:** the five sets are used by 15 placed holders: set 5 by six containers (prototypes 3044 and 3045; five on the main map, one in Wheel Clan), set 7 by three containers and one NPC
  (prototype 17100), set 11 by two main-map containers, set 13 by one, set 14 by two. A document line is a percent chance per generation: set 5 20 percent for four records; set 7 2 to 10;
  sets 11 and 13 5 to 10; **set 14 has 20 document lines** (3 percent each, 5 percent for 4620 and 5250, 75 percent for 5440).
  The 30 "set only" records are therefore reachable from a few chests, by chance.
- **The Crash Site cave** holds 23 technological item instances (14 generic, 6 weapons, 2 food, 1 ammunition) but no schematic document.

### 3.3 Per record

| Record | Discipline | Document | On maps | Sets of placed holders (chance %) | Verdict |
| ---: | --- | ---: | ---: | --- | --- |
| 4000 | Herbology | 14093 | 2 | 5 (20) | placed and set |
| 4010 | Herbology | 14094 | 0 | - | nowhere |
| 4020 | Mechanical | 14095 | 0 | - | nowhere |
| 4030 | Herbology | 14096 | 1 | 5 (20) | placed and set |
| 4200 | Chemistry | 14097 | 1 | - | placed |
| 4210 | Chemistry | 14098 | 1 | - | placed |
| 4220 | Chemistry | 14099 | 1 | 5 (20) | placed and set |
| 4230 | Explosives | 14100 | 3 | 5 (20) | placed and set |
| 4240 | Chemistry | 14101 | 1 | - | placed |
| 4400 | Electric | 14102 | 1 | 7 (2) | placed and set |
| 4410 | Electric | 14103 | 0 | 11 (5), 13 (5) | set only |
| 4420 | Electric | 14104 | 0 | 7 (10) | set only |
| 4430 | Electric | 14105 | 0 | 7 (10) | set only |
| 4440 | Mechanical | 14106 | 0 | - | nowhere |
| 4450 | Electric | 14107 | 0 | 11 (5), 13 (5) | set only |
| 4600 | Explosives | 14108 | 1 | - | placed |
| 4610 | Explosives | 14109 | 0 | 14 (3) | set only |
| 4620 | Explosives | 14110 | 0 | 14 (5) | set only |
| 4630 | Explosives | 14111 | 0 | 14 (3) | set only |
| 4640 | Explosives | 14112 | 0 | 14 (3) | set only |
| 4650 | Explosives | 14113 | 0 | 14 (3) | set only |
| 4660 | Explosives | 14114 | 0 | 14 (3) | set only |
| 4670 | Explosives | 14115 | 0 | 14 (3) | set only |
| 4680 | Explosives | 14116 | 0 | 14 (3) | set only |
| 4690 | Explosives | 14117 | 0 | 14 (3) | set only |
| 4700 | Explosives | 14118 | 0 | 14 (3) | set only |
| 4710 | Explosives | 14119 | 0 | 14 (3) | set only |
| 4800 | Gun Smithy | 14120 | 0 | 14 (3) | set only |
| 4810 | Gun Smithy | - | 0 | - | no document |
| 4820 | Gun Smithy | 14121 | 0 | 14 (3) | set only |
| 4830 | Gun Smithy | 14122 | 0 | 14 (3) | set only |
| 4840 | Gun Smithy | 14123 | 0 | 14 (3) | set only |
| 4850 | Gun Smithy | 14124 | 1 | 11 (5), 13 (5) | placed and set |
| 4860 | Gun Smithy | 14125 | 1 | 14 (3) | placed and set |
| 4870 | Gun Smithy | 14126 | 0 | - | nowhere |
| 4880 | Gun Smithy | 14127 | 0 | 14 (3) | set only |
| 4890 | Gun Smithy | 14128 | 0 | 14 (3) | set only |
| 4900 | Gun Smithy | - | 0 | - | no document |
| 5000 | Mechanical | 14129 | 0 | 7 (2) | set only |
| 5010 | Mechanical | 14130 | 1 | - | placed |
| 5020 | Mechanical | 14131 | 0 | 11 (8), 13 (8) | set only |
| 5030 | Mechanical | 14132 | 0 | 11 (5), 13 (5) | set only |
| 5040 | Mechanical | 14133 | 1 | 7 (10) | placed and set |
| 5050 | Mechanical | 14134 | 0 | - | nowhere |
| 5060 | Mechanical | 14135 | 1 | - | placed |
| 5070 | Mechanical | 14136 | 0 | - | nowhere |
| 5080 | Mechanical | 14137 | 2 | 7 (10) | placed and set |
| 5090 | Mechanical | 14138 | 1 | - | placed |
| 5200 | Herbology | 14139 | 0 | - | nowhere |
| 5210 | Smithy | 14140 | 0 | 11 (5), 13 (5) | set only |
| 5220 | Smithy | 14141 | 0 | 11 (10) | set only |
| 5230 | Smithy | 14142 | 0 | 11 (5), 13 (5) | set only |
| 5240 | Smithy | 14143 | 1 | 11 (5), 13 (10) | placed and set |
| 5250 | Gun Smithy | 14144 | 0 | 14 (5) | set only |
| 5260 | Smithy | 14145 | 0 | 11 (10) | set only |
| 5270 | Smithy | - | 0 | - | no document |
| 5280 | Smithy | - | 0 | - | no document |
| 5400 | Therapeutics | 14146 | 1 | - | placed |
| 5410 | Therapeutics | 14147 | 1 | - | placed |
| 5420 | Therapeutics | 14148 | 1 | - | placed |
| 5430 | Therapeutics | - | 0 | - | no document |
| 5440 | Explosives | 14149 | 0 | 14 (75) | set only |

### 3.4 What this means

- Nothing can give the hero 4810, 4900, 5270, 5280 or 5430 in the installed data; a design that lists them as "found" is listing records without a source.
- 4010, 4020, 4440, 4870, 5050, 5070 and 5200 have a document that no installed map, set or script this census follows places (section 2 lists what it does not follow).
- For the smoke slice T-a (found documents): the main map holds 12 of the 24 instances (records 4000, 4030, 4220, 4230 three times, 4240, 4850, 4860, 5040 and 5080 twice, in containers and two NPC packs); a stage that must read
  a document needs one of those containers or a synthetic placement.
- The TC8 document (prototype 14087) lies in two main-map containers, on the main map's ground twice, in five Vendigroth Underground containers and one Wheel Clan NPC pack: reading one
  registers schematic 0 in the Original setting (a record of zeros that can never be built); Steamwoven refuses the registration and keeps the document (owner decision on TC8).

## 4. NPCs with `su` dialogs (O5)

The five dialog scripts with the action `su` (1046, 1650, 1677, 2834, 2886; eight lines) are the dialog point (attachment point 9) of six NPC instances in the aggregates; no tile script,
sector script or critter prototype carries them (matches the earlier prototype count). Stored degrees are instance fields (0 of the 510 critter prototypes has one); the learned records shown are
those the stored degrees give (the first "degree" records of each discipline, no found tab for an NPC):

| Map | NPC prototype | Dialog script | Degrees (disciplines 0..7) | Learned records shown | Items held (worn) | Both components held |
| --- | ---: | ---: | --- | ---: | --- | --- |
| arcanum1-024-fixed | 17226 | 2834 | 0 0 4 4 0 0 0 0 | 8 | 4 (4) | none |
| arcanum1-024-fixed | 17190 | 1650 | 0 2 0 0 3 0 0 0 | 5 | 5 (2) | none |
| arcanum1-024-fixed | 17088 | 1677 | 1 1 1 1 1 1 1 1 | 8 | 3 (1) | none |
| arcanum1-024-fixed | 17167 | 2886 | 2 0 0 0 0 0 0 2 | 4 | 1 (1) | none |
| arcanum1-024-fixed | 17122 | 1046 | 0 0 0 0 0 2 2 0 | 4 | 4 (2) | none |
| vollingers basement | 17182 | 1650 | 0 0 0 0 5 0 0 0 | 5 | 3 (1) | none |

- No NPC holds both components of any record its window shows, with or without the worn items (the original skips worn items for the gate in any case). Their stock is one to five items,
  mostly worn equipment; six of the 20 items held are a component of some record of the catalogue.
- 68 NPC instances in 9 maps carry stored degrees (29 prototypes; 53 on the main map; by discipline 0 to 7: 14, 3, 3, 2, 52, 4, 2, 8 NPCs). Only the six above have an `su` dialog; the others
  have no way to open the window (action 129, which only a party member runs, is used by no script), so the NPC crafter path of the audit's chain has six users and none of them can assemble from its own stock.
- **For the plan:** the audit's T-a stage should not use an NPC crafter on real data (nothing to assemble from its stock); the hero's own window is the testable path.

## 5. The 146 non-craftable technological items

"Best tier" is the first that applies (no item is in tier 5, so the table has no column for it): **1** an instance lies on a map; **2** a line of a set that a placed holder uses; **3** a line of a set only a prototype names; **4** a script creates it;
**5** only an unreferenced set; **6** none of these.

| Discipline | Items | Tier 1 on maps | Tier 2 placed-holder set | Tier 3 prototype set | Tier 4 script | Tier 6 nowhere | With a technology record |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Herbology | 10 | 9 | 0 | 0 | 1 | 0 | 3 |
| Chemistry | 15 | 14 | 1 | 0 | 0 | 0 | 4 |
| Electric | 11 | 11 | 0 | 0 | 0 | 0 | 0 |
| Explosives | 11 | 9 | 1 | 0 | 0 | 1 | 1 |
| Gun Smithy | 25 | 19 | 0 | 0 | 0 | 6 | 0 |
| Mechanical | 22 | 16 | 3 | 0 | 0 | 3 | 0 |
| Smithy | 38 | 31 | 1 | 1 | 0 | 5 | 0 |
| Therapeutics | 14 | 13 | 0 | 0 | 0 | 1 | 1 |
| **Total** | 146 | 122 | 6 | 1 | 1 | 16 | 9 |

By type: weapons 26 (19 on maps, 1 set, 6 nowhere), armour 27 (21, 0, 1 prototype set, 5 nowhere), food 35 (34, 0, 1 nowhere), scroll 1 (script), generic 57 (48, 5 set, 4 nowhere).

- **Instances:** 1,600 on 29 maps: containers 935, worn by NPCs 299, on the ground 232, in NPC packs 134. The main map holds 1,178 of them, then BMC 106, Wheel Clan 86, Caladon Castle main
  floor 39, Tulla Interior (water battle) 38, the Crash Site cave 17.
- **Tier 2 (6 items):** 6196 (weapon), 15094, 15156, 15187, 15197, 15198 (generic), each a line of a set that placed containers or NPCs use (main map, Wheel Clan, Tarant Sewers 02).
- **Tier 3 (1):** 8121 (armour, Smithy), set 97 at 100 percent, named only by NPC prototype 17332, which no map places. **Tier 4 (1):** 11162 (scroll, record 191), created by script 30302, attached
  to NPC prototype 28487, which no map places.
- **Nowhere (16):** weapons 6104, 6121, 6122, 6123, 6127 (Gun Smithy) and 6186 (Mechanical); armour 8123, 8128, 8133, 8141, 8144 (Smithy); food 10126 (Therapeutics); generic 15117, 15190
  (Mechanical), 15132 (Explosives, carries record 180) and 15133 (Gun Smithy). Only 15132 carries a technology record.
- **The 124 craftable items:** 73 lie nowhere (so the crafted product is their only source), 49 lie on maps (531 instances), 2 are script-created (both by scripts attached on maps).
- **Carriers of a technology record among the 146:** nine items, records 161, 164, 169, 170, 173, 183 and 190 (all lying on maps), 191 (the script scroll) and 180 (nowhere).

## 6. What this says about the plan's gap groups

Computed from the carriers of each record of the audit's section 6 rows (items that lie on a map, any technological or not):

| Group | Records | Carried | Carried by an item lying on a map | Instances on maps |
| --- | ---: | ---: | ---: | ---: |
| Summon (E1-b) | 19 | 16 | 9 | 19 |
| ObjFlag bits (E4-a, E4-b, E4-f) | 20 | 14 | 8 | 241 |
| Control components (E4-c, E4-e, E4-f) | 8 | 6 | 5 | 30 |

The rows overlap (record 183 is in all three; 148, 177, 178, 200, 217 are in Summon and ObjFlag; 187 and 222 in ObjFlag and Control), and 218 of the 241 ObjFlag instances are one
item, prototype 10113 (food, record 172, the drunk bit).

- The seven Summon records whose carrier lies on no map (180, 198, 199, 202, 220, 148, 200: generic items) can reach the hero only by crafting; the three uncarried ones
  (185, 205, 217) by nothing. The E1-b acceptance stages of the audit can use the nine placed carriers for maps and crafted items for the rest.
- Of the 65 carried records, 36 have a carrier on a map. The 29 that have none: 141, 142, 144, 148, 153, 157, 159, 160, 162, 163, 180, 189, 191 to 200, 202 to 204, 206 to 208 and 220
  (191, 207 and 208 are the script scrolls named in the last bullet; the others are carried by crafted products or by the unplaced items of section 5).
- The 15 records carried only by non-schematic items: 164, 169, 170, 172, 173, 183, 210 to 214 and 218 lie on maps (a carrier each); 191, 207, 208 are only script-made scrolls.

## 7. Open and not counted

- Items a script creates through a call chain, a monster generator or a spawned NPC with an inventory source (the sets 97 and 58 and the two NPC prototypes above are the visible cases); a
  later census with the script call graph and the map's monster generators would turn "nowhere" into a measured floor and ceiling.
- Whether the five unreferenced sets (113 to 120) are named by something the census does not read (a script action that sets a source).
- The second module (`Vormantown`), which has its own maps and stock sets.
- Per-instance values the aggregate stores for the documents (the instance's own item flags, identified state): not read.
