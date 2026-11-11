# Technology audit: eight disciplines, seven degrees, schematics and devices

Status: **Audit and plan**, work item ARC-RES-104 (2026-11-11). Docs-only: no source change and no checks run (the data census below ran
locally on the owner's Mac with a throwaway program outside the repository, not as a repository check). It is the technology counterpart
of the [magic audit](magic-audit-res-103.md) (ARC-RES-103, merged: it leaves the technology records 140..222 to this document) and a companion of the [missing-systems plan](../project/missing-systems-plan.md) (ARC-RES-099):
it does not repeat that plan's epics, it says which of their slices the technology content needs, what is left over, and in what order.

Update 2026-11-11 (ARC-DEV-362): slice T-a is done: the smoke stage `tech-craft` runs the chain on the player's data (section 7). It corrected one row of section 5: the Herbology degree 2 record 151 heals 20 **fatigue**, not a magic-typed amount.

Requirements (reference only, no coverage row changes): [FR-ITEM-011](../requirements/items-economy.md#fr-item-011), [FR-ITEM-016](../requirements/items-economy.md#fr-item-016),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017), [FR-ITEM-018](../requirements/items-economy.md#fr-item-018), [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020).
The original behaviour is in the [technology research](technology-evidence.md) (ARC-RES-062) and the [technology ledger](ledger/technology.md); this document does not re-derive it.

## 1. Answer

1. **The player-facing chain is built for the hero** (rules and viewer, covered by unit tests and a synthetic self-test, not by any smoke stage on real data): buy a degree on the sheet's tech page, open the schematic window (`T`), assemble (all 118 recipes
   are gate-checked on real data by a unit test), the product lands in the pack, and a potion, food, scroll or thrown grenade then runs its spell through the item-trigger host. Gun Smithy
   and Smithy make **ordinary equipment** with no technology record at all (14 of 14 learned products), and so do most of Mechanical (6 of 7) and Electric (4 of 7); their value is
   the equipment, attack and wear rules, which are built.
2. **What the item does when used is the open part.** Of the 31 learned products that carry a technology record (31 distinct records), by the component table of
   section 5: **8 run today** (the six Herbology heals and two Chemistry attacks), **8 work for the hero only** (the seven Therapeutics stat potions and Chemistry's poison
   draught: the Effect component keeps an effect list for the hero alone), **7 run partly** and **8 do nothing but play their eye candy**. Over all 65 carried records (not
   only learned ones): 17 run, 18 hero-only, 16 partial, 14 inert.
3. **Four component groups explain every gap,** and all four are already in the missing-systems plan: **Summon** (13 records blocked by it alone, 19 in all: every
   grenade, gas, fire wall and the timed bombs) is E1-b; the remaining **ObjFlag bits** (blind, flee, fatigue limit, mind control, drunk, magnetic inversion, undead,
   enshroud, shield, don't-draw, permanent polymorph; 12 records blocked by them alone) are E4-a and E4-b; the **control components** (ChargeNBranch, TestNBranch, Terminate,
   Use, Cast, Movement, Trait, AIRedirect; 5 records alone) are E4-c and E4-e; the **Effect component on any critter** (22 records) is E1-d.
4. **Technology adds six things the plan does not hold:** (a) an item **aiming cursor** and use on a critter, object or tile (the viewer uses an item on the hero only;
   item targeting is "NOT READY"); (b) NPCs choosing and throwing **grenades** and using devices (`AreaAiHost.Grenade` answers "none"); (c) the generic item flag 0x20 (2 learned and 3 found products; flag 0x10 is the grenade flag the AI ledger SP14 already uses, its numeric bit never written down) and the **item scripts on crafted gear** (6 learned products, attach points 24, 25, 7, 8), neither of which had been read when this audit was written; read since by [ARC-RES-112](technology-item-scripts-res-112.md) (the flag only shows an item's hit points; the scripts are creature-item HIT and MISS scripts and equipment scripts: two slices, T-m1 and T-m2, section 10); (d) **smoke coverage**: no
   stage assembles anything, uses a technology item with a real record, or buys a degree (all three are covered by synthetic self-tests and unit tests only); (e) an opt-in
   **coverage test** that keeps the status table of section 5 true to the code; (f) a documentation refresh (five stale statements, section 9).
5. **No missing piece is a hidden mechanic of the original.** Degrees give expertise only, expertise is read only by the sheet and the schematic gate, assembly has no roll, no
   skill and no experience, and a degree has no other effect on a device (technology research, answer 2, 5, 7). The remaining unknowns are small reads, listed in section 8 (T-r1 to T-r4).
6. **Order (section 10):** wave 0 is four short reads, the smoke stage, the coverage test and the doc refresh, all independent of E1; the content gaps are E1-b, E1-d, E4-a to E4-e and E6-b as
   already planned, with the acceptance lists of section 6; the two new product slices (aim cursor, NPC grenades) follow the item-aim read. Owner decisions are in section 11 (five rows).

## 2. Method and limits

- **Documents read:** the technology research and ledger (ARC-RES-062, TECHNOLOGY-001 to -010), the world runtime documents of degrees, schematics, assembly, charge cells, devices, item
  spell triggers, durability, repair and traps, the requirement rows and coverage rows named above, the missing-systems plan, the magic implementation plan, the summon note
  ([ARC-RES-101](summon-objects-res-101.md)) and both bug triages.
- **Code:** `origin/main` at `c53887a94` (the one-component dispatch `SessionSpellCaster.Apply` was re-read by hand after the survey and is unchanged by the merged E1-a, ARC-DEV-347).
  One read-only survey by symbol search answered each item BUILT, PARTIAL, STUB or ABSENT with a file and symbol; the claims that decide the plan were re-checked by hand:
  the dispatch chain, that nothing handles ChargeNBranch outside the data catalogue, `AreaAiHost.Grenade` answering none, and the sheet's degree buttons (`AreaTechPage`).
- **Data:** a throwaway console program outside the repository, built on the repository's own `Arcanum.Data` readers, read the installed game through the content stack and
  printed counts and numbers only (no names, no text). The 798 item prototypes came from the project's prototype cache, built from the retail executable (the installation has no prototype folder
  of its own). Its totals agree with the technology research's data census (56 learned, 62 found, 8 placeholders, 70 spell slots on 70 items, 83 records, 1 learned mismatch); the
  only differences are counting conventions. Discipline indexes 0..7 are the order of `mes\tech.mes`; the names below are the owner's list in that order and the content agrees
  (Herbology's components are herbs, Gun Smithy's products are all firearms, and so on).
- **Component classification** (section 5) is by component type and parameters against what `SessionSpellCaster.Apply` and its helpers do today. "Runs" means a code path
  exists for every component of the record, not that it matches the original down to presentation, and not that a smoke stage ran it (section 7 says which were).
- **Not done:** no viewer run, no executable read, no map or vendor scan (so where found schematics and technology items lie in the world was open here: T-r4, answered by the [placement census](technology-census-res-115.md), ARC-RES-115). Reads of the original that
  already exist stay at their ledger level (F1 or F2).

## 3. The chain and where each link stands

| Link | Original (read) | Rules / session | Viewer | Smoke | Remaining |
| --- | --- | --- | --- | --- | --- |
| Degrees 0..7 per discipline, cost 1, intelligence minimum {0, 5, 8, 11, 13, 15, 17, 19}, TC10 floor | F2, [ledger](ledger/technology.md#technology-arc-res-062) | built ([degrees](../world/tech-degrees-runtime.md), `TechDegreeRules`, state tests) | sheet tech page with + and - buttons (`AreaTechPage`, ARC-UI-076) | self-test of the sheet; `tech-craft` buys degrees 1 and 2 and 1 on real data | creation editor: buttons for degrees not confirmed |
| Expertise {0, 10, 20, 35, 50, 65, 80, 100} % through "expertise" effects | F2 | built (hero and NPC queries) | shown on the sheet and in the window | no | no effect in the installed data was counted that targets expertise (T-r3) |
| Learned schematics (7 per discipline) and found ones (62, documents) | F2 | built (catalogue, knowledge, registration) | window tabs, the Read button registers a document | self-test, synthetic | where found documents lie: see the [placement census](technology-census-res-115.md) (T-r4 done) |
| Component gate and assembly (no roll, TC3 to TC5 fixed) | F2 | built, all 118 recipes real-data tested | window button, hero and NPC crafter (`su`, action 129) | self-test, synthetic; `tech-craft` assembles three learned records on real data | the other 115 records have no run |
| Charge cells (ammunition type 2) and the TC1 switch | F2 | built (rules, hero stack, container switch) | none needed | none | no component pays them (ChargeNBranch absent) |
| Item durability, break, repair | F2 | built | Repair button; durability not shown | repair inside the hero smoke | no repair window or price (E8 area) |
| Item use: self, scroll, food, potion, kit, read | F2 | built (`SessionItemTriggers.UseItem`) | inventory use button and right-click | `inv-ui` (plain food only); `tech-craft` uses two spell-carrying food items (records 150, 151) | the other products are not run |
| Item use at a target, object, tile; trap device placement | F2 (TP1 to TP8) | partly (session) | **not built**: no item aiming cursor; trap devices "NOT READY" | none | slices T-g, E6-b |
| Throwing: grenades, thrown weapons | F2 | built | held throw to the cursor | `grenade` (whatever grenade the cave map lays) | only the damage and destroy parts of a grenade run, Summon is absent |
| Item spell triggers (wear, hit, attack start, throw end, pickup, drop) | F2 (IT1 to IT10) | built, wider than wear and unwear | partly | `cast` marker | a few moves are not wired (see [triggers](../world/item-spell-triggers.md)) |
| Firearms, bullets, power cells, technology failure and magick cancel rolls | F2 (DV4, DV5) | built | built | `ranged`, `npc-shot` | none specific to crafted guns |
| Aptitude and resistance (magic resistance scaled by negative aptitude, item power, penalty fields) | F2 | built | sheet | unit tests | AI and reaction readers of aptitude not read (T-r3) |
| Technical skills: Repair, Pick Locks, Firearms built; Disarm Traps session only | F2 | partly | partly | partly | E6-a, E8-b |
| NPC use of items in the AI | F1 (AI I8 partly) | potions and food with spells built; **grenade pick absent**; heal-with-item, read, lockpick, trap device not ready | n/a | `ai-cast` | slice T-h |

## 4. What a degree gives

Seven degrees, same table in every discipline. A degree is bought with one point, needs the **effective** intelligence given below, and gives the expertise percentage;
it also unlocks exactly one learned schematic (the k-th record of the discipline's learned list, number `1990 + 200 d + 10 × degree`). The component gate asks that the crafter's
expertise in the component's own discipline be at least the component's complexity (a component's |complexity| of 0 is no requirement).

| Degree | Intelligence needed | Expertise | Largest component requirement among the 8 schematics of the degree (max over disciplines) | Built |
| --- | --- | --- | --- | --- |
| 1 | 5 | 10 % | 10 | yes |
| 2 | 8 | 20 % | 20 | yes |
| 3 | 11 | 35 % | 35 | yes |
| 4 | 13 | 50 % | 46 | yes |
| 5 | 15 | 65 % | 65 | yes |
| 6 | 17 | 80 % | 80 | yes |
| 7 | 19 | 100 % | 92 | yes |

Data check (section 5 columns "needs degree"): in every discipline the component requirement of the schematic learned at degree *n* is met at degree *n* or lower, so every learned
schematic can be assembled the moment it is learned, provided intelligence has not been temporarily lowered (effective expertise then drops to a lower degree's percentage). Gun Smithy degree 4 and 7,
Mechanical degrees 3 and 4, and Smithy degree 5 need less than their degree.

## 5. The matrix: discipline by degree

Columns: **Product** is the first learned product (kind, item complexity, quantity per assembly); **Needs** is the lowest degree whose expertise meets the larger of the two components'
requirements (all components are in the schematic's own discipline; 24 component alternatives in 17 schematics are neutral, complexity 0, and ask nothing); **Record** is the technology record the product carries
(slot 0, charges 1 unless noted) and what its components do; **State** is the verdict of the component classification:

- **RUNS**: every component has a code path (Damage, Heal, EyeCandy, the knockback goal, ObjFlag for the bits that exist: entangled, stoned, paralysed, spoken-with-dead (only the entangle, the paralysis and the spoken-with-dead occur in a technology record), Destroy of a lying item).
- **HERO**: runs for the hero (self-use); the Effect component keeps no effect list for an NPC (E1-d).
- **PART(x)**: some components run, the named groups do not.
- **INERT**: only the eye-candy log runs.
- **EQUIP**: no technology record; the product is ordinary equipment, ammunition or a tool, whose rules are built.

An eye-candy line is a presentation log in the viewer (no light, sound or tint yet); it is not counted as a gap here. Scripts are item scripts at attach points; their effect was read by [ARC-RES-112](technology-item-scripts-res-112.md) (T-r1, done).

### Herbology (discipline 0): seven food items

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | food, -65, x5 | 1 | 150 heal 20 hit points | RUNS (run on real data) |
| 2 | food, -40, x4 | 2 | 151 heal 20 fatigue (its Heal component has damage type 4, fatigue; the first reading "magic-typed" was wrong, found by the smoke `tech-craft`, ARC-DEV-362) | RUNS (run on real data) |
| 3 | food, -55, x3 | 3 | 152 heal 80 poison | RUNS |
| 4 | food, -60, x2 | 4 | 153 sets and clears the fatigue-limiting flag, 180 s | INERT (ObjFlag fatigue limit) |
| 5 | food, -70, x1 | 5 | 154 heal 1 at begin and each maintain tick, 4 s | RUNS |
| 6 | food, -85, x1 | 6 | 155 heal 60 normal and 60 magic | RUNS |
| 7 | food, -90, x1 | 7 | 156 full heal with scars and crippling, magic 160, poison 32000 | RUNS |

### Chemistry (discipline 1): food items and one ammunition

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | food, -75, x3 | 1 | 171 poison 290 damage plus an effect (id 359, absent from `effect.mes`: no change), 60 s, aggressive | HERO (Damage runs, the effect for the hero only) |
| 2 | power-cell ammunition, -30, x5 (its item discipline is Electric: the one mismatch, no effect) | 2 | none | EQUIP (ammunition; the cell stack is built) |
| 3 | food, -85, x1 | 3 | 167 animal-enshroud critter flag, 338 s | INERT (ObjFlag enshroud) |
| 4 | food, -70, x1 | 4 | 166 acid 10 to 100, a flash summon, destroy | PART(Summon) |
| 5 | food, -85, x1 | 5 | 165 flee flag, 600 s, aggressive | INERT (ObjFlag fleeing) |
| 6 | food, -80, x1 | 6 | 197 paralysis 6 s | RUNS |
| 7 | food, -75, x1 | 7 | 196 magic damage with the "full" flag | RUNS |

### Electric (discipline 2): tools, armour, a cell weapon, a cell firearm

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic with flag 0x1 (the torch location), -20, x1 | 1 | none | EQUIP (a carried light: the worn-light work V7d, ARC-DEV-335, is the dependency) |
| 2 | ring armour, -25, x1, scripts at 24 and 25 | 2 | none | EQUIP (28012 and 28013 run, T-r1) |
| 3 | shield armour, -40, x1, script at 24 | 3 | none | BLOCKED (29016 refuses the wear, T-m2) |
| 4 | melee weapon, -90, x1 | 4 | 147 electrical 1 to 10, **ChargeNBranch** (one cell per use), cell-powered | PART(control: cell branch) |
| 5 | helmet armour, -50, x1 | 5 | 182 sets and clears magnetic inversion | INERT (ObjFlag magnetic inversion) |
| 6 | torso armour (3 alternatives), -80, x1 | 6 | 168 maintained every 5 s: **ChargeNBranch**, **TestNBranch** on hit points, heal 4 | PART(control: cell and test branches) |
| 7 | power-cell firearm, -55, range 15, x1 | 7 | none | EQUIP (cells are ammunition type 2: built) |

### Explosives (discipline 3): grenades, gases, bombs (every record needs Summon)

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic, -20, x1 | 1 | 219 fire and normal 3 to 15, knockback, flash summon, destroy | PART(Summon) |
| 2 | generic flag 0x10, -15, x5 | 2 | 177 blinding flag 10 s, flash | INERT (ObjFlag blind, Summon) |
| 3 | generic flag 0x10, -20, x5 | 3 | 178 smoke cloud 10 s (fog scenery), then clears don't-draw | INERT (ObjFlag don't-draw, Summon) |
| 4 | generic flag 0x10, -30, x4 | 4 | 179 stun with a flash | PART(Summon) |
| 5 | generic flag 0x10, -60, x3 | 5 | 175 fire and normal 5 to 30, knockback, flash | PART(Summon) |
| 6 | generic flag 0x10, -45, x3 | 6 | 180 fire wall scenery 10 s | INERT (Summon) |
| 7 | generic flag 0x4 (trap device), -85, x1, charges 0 | 7 | 176 fire 5 to 20 and normal 100 to 200, knockback, flash; slot 1 of a **timed trap device** (TP2, TP3) | PART(Summon) and E6-b |

### Gun Smithy (discipline 4): seven firearms, no technology record

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | firearm, bullets, -20, range 15 | 1 | none | EQUIP |
| 2 | firearm, bullets, -35, range 15 | 2 | none | EQUIP |
| 3 | firearm, bullets, -50, range 15 | 3 | none | EQUIP |
| 4 | firearm, bullets, -30, range 9 | 3 | none | EQUIP |
| 5 | firearm, bullets, -70, range 25 | 5 | none | EQUIP |
| 6 | firearm, bullets, -85, range 12 | 6 | none | EQUIP |
| 7 | firearm, bullets, -80, range 15 | 3 | none | EQUIP |

### Mechanical (discipline 5): trap devices, a lockpick, helmets and keyed tools

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic flag 0x4 (trap device), -20, x3, script 30001 at attach point 1 | 1 | none (the native trap script: normal damage) | EQUIP, device placement is E6-b |
| 2 | generic, -50, x3 | 2 | 184 ends with a Use component after 4 s; its begin Destroy takes the pack item itself (also not ready: only lying items) | INERT (control: Use; Destroy of a held item, E4-c) |
| 3 | generic flag 0x2 (lockpick), -35, x1 | 2 | none | EQUIP (lockpicks work) |
| 4 | helmet armour, -85, x1, scripts at 24 and 25 (the same two numbers as the Electric ring) | 3 | none | EQUIP (28012 and 28013 run, T-r1) |
| 5 | generic flag 0x4 (trap device), -10, x3, script 30001 | 5 | none | EQUIP, E6-b |
| 6 | generic flag 0x20, -70, x2, script at 7 and 8 | 6 | none | INERT (a creature on throw, T-m1) |
| 7 | generic flag 0x20, -100, x1, script at 7 and 8 | 7 | none | INERT (a creature on throw, T-m1) |

### Smithy (discipline 6): melee weapons and armour, no technology record

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic, -20, x1 | 1 | none | EQUIP |
| 2 | melee weapon, -5, x1 | 2 | none | EQUIP |
| 3 | melee weapon, -20, x1 | 3 | none | EQUIP |
| 4 | gauntlets, -15, x1, scripts at 24 and 25 | 4 | none | EQUIP (28014 and 28015 run, T-r1) |
| 5 | helmet, -10, x1 | 2 | none | EQUIP |
| 6 | torso armour (3 alternatives), -20, x1 | 6 | none | EQUIP |
| 7 | torso armour (3 alternatives), -20, x1 | 7 | none | EQUIP |

### Therapeutics (discipline 7): seven potions of stat effects (all HERO)

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | food, -56, x3 | 1 | 157 persuasion +2 | HERO |
| 2 | food, -78, x3 | 2 | 158 strength +2 | HERO |
| 3 | food, -40, x3 | 3 | 159 perception +2 | HERO |
| 4 | food, -55, x3 | 4 | 160 dexterity +2 | HERO |
| 5 | food, -85, x1 | 5 | 161 intelligence, perception, willpower, charisma +2 | HERO |
| 6 | food, -92, x1 | 6 | 162 strength, dexterity, constitution, beauty +2 | HERO |
| 7 | food, -90, x1 | 7 | 163 as 161 and 162 together | HERO |

(The therapeutics effects have no duration of their own in the record, they end by `End` actions; the hero-side Effect component adds and removes them.)

### Found schematics (62 records, by the discipline of the first product)

| Discipline | Records | Products | With a technology record | Records and states |
| --- | --- | --- | --- | --- |
| Herbology | 4 | 3 food, 1 armour | 3 | 189 HERO, 190 RUNS, 192 RUNS |
| Chemistry | 4 | 4 food | 4 | 193 HERO, 194 PART(flags: undead, mind control), 195 PART(fatigue limit; Effect), 197 RUNS (also learned) |
| Electric | 5 | 2 weapons, 2 armour, 1 ammunition | 0 | EQUIP |
| Explosives | 14 | 9 generic, 3 weapons, 2 ammunition | 8 | 148 INERT, 198 to 202 PART(Summon), 203 INERT (nested Cast), 220 PART (timed bomb) |
| Gun Smithy | 12 | 12 weapons | 1 | 204 RUNS (magic damage on a weapon) |
| Mechanical | 12 | 8 generic, 2 weapons, 2 armour | 0 | EQUIP |
| Smithy | 7 | 4 weapons, 2 armour, 1 keyring | 0 | EQUIP |
| Therapeutics | 4 | 4 food | 4 | 141 HERO, 142 INERT (mind control), 144 HERO, 206 HERO |

Quantities: 56 records make one item, one makes 2, four make 3, one makes 5. Two found records (an Electric and a Mechanical one) have three alternatives in a component and the product; the learned ones with three are Electric degree 6 and Smithy degrees 6 and 7.

### Records that are not on any schematic

Of the 65 carried records, 15 are carried only by items that no schematic makes (they lie on maps or in stock sets: 12 of the 15 on a map, 3 scrolls only through scripts of unplaced NPC prototypes, [placement census](technology-census-res-115.md)): 6 RUN (164, 169, 170, 191, 207, 208), 5 are HERO (173, 211 to 214), 3 are INERT (183, 210, 218), 1 is partial (172). Eighteen records are carried by no item: 9 RUN
(140, 143, 146, 149, 174, 188, 215, 216, 221), 1 is HERO (145), 4 are partial (181, 185, 205, 209), 4 are inert (186, 187, 217, 222). They exist for scripts and the AI's lists; no installed item uses them. 270 items are technological in all
(complexity below 0): 124 are craftable, 146 are not (per discipline 10, 15, 11, 11, 25, 22, 38, 14 not craftable); 61 of the 270 carry a technology record.

## 6. Gaps by component, with the plan slice that closes each (acceptance lists)

Every list below is the acceptance of the plan slice named: when that slice lands, those records go from the state shown to RUNS and the coverage test of slice T-b flips them.

| Gap | Records | Carried | Plan slice | Dependency | What the slice must show |
| --- | --- | --- | --- | --- | --- |
| Summon (flash 4050 ×15, smoke 4033, fire wall 4031, gas 4029, one list) with Destroy of the summoned and the don't-draw callback | blocked by Summon alone: 166, 175, 176, 179, 180, 185, 198, 199, 201, 202, 205, 219, 220; with another group: 148, 177, 178, 183, 200, 217 | 16 of 19 | E1-b (objects; E1-a, ARC-DEV-347, is merged), E1-e for 183's creature list | E1-a (done), ARC-RES-101 | one stage per kind: a thrown flash grenade (175, 176, 219), the smoke (178), the wall (180), the stun (179), the acid (166), a gas (200) |
| ObjFlag bits other than entangle, paralysis, spoken-with-dead | fatigue limit 153, 195, 210; blind 177, 186; flee 148, 165; mind control 142, 194; drunk 172, 181; magnetic inversion 182, 187; enshroud 167; undead 194; shielded 209; don't-draw 178, 183, 200, 217; permanent polymorph 222 | 14 | E4-a (blind, don't-draw) and E4-b (flee, mind control, undead) as the plan names them; **drunk, fatigue limit, magnetic inversion, shielded and enshroud are in neither plan row and must be added to E4-a**; E4-f (polymorph 222) | E1-d | each bit as an object-flag write with its clear at the record's End |
| Effect on any critter | 22 records (the HERO group plus 172, 181, 195) | 20 | E1-d, then E4-a | none | a stat potion given to a companion by a script or thrown; effect list saved |
| Control components: ChargeNBranch, TestNBranch, Terminate, Use, Cast, Movement, Trait, AIRedirect, NoOp | 147, 168, 184, 203, 218, plus 183, 187, 222 | 6 | E4-c (branches, Use, Terminate, NoOp), E4-e (teleport 218, nested Cast 203), E4-f (Trait 222), E4-b (AIRedirect 183) | E1-d | the cell weapon spends one cell (147), the maintained armour pays one cell a tick and tests hit points (168), the cell items refuse without cells |
| Item aiming and use on a critter, object or tile | every aggressive carried record used as an item, and trap devices | n/a | **new: T-g** (T-r2 is done: [ledger IA1–IA16](ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113)) | none | the item cursor of IA3 to IA6 (mask by item kind, interface mode 0x10, left release picks, right release cancels), the apply of IA8 (an object: a charged or magical item in the spell's range is used at once, out of range or with the no-ranged flag the hero walks next to the target first; any other item is used at once) and IA9 (a tile: charged items cast at once, a trap device walks to it as goal 41), the use on a tile of IA12 (no script, no scroll check), refusals and messages of IA14 |
| Trap devices | Mechanical degree 1 and 5, Explosives degree 7 (176), 148 and 220 timed bombs | 3 learned products | E6-b (already in the plan) | E1-b for 176, T-g | a placed device spends itself, the marker, the delayed run at the tile after the original's 88 000 ms (TP3), and the refusals of IA13 (a script trap on a target that is not a free portal or container, or on a tile that already has a trap, keeps the device and shows trap.mes 2 or 3) |
| NPC use of grenades and devices | the AI's grenade pick | n/a | **new: T-h** | E1-b, T-r2 (done) | an NPC picks a grenade from its pack and throws it. T-r2 found that no installed AI item entry is a trap device or a written item and the AI always passes a target (IA15, data-dependent), so those need no action; heal-with-item and the pick-locks goal are already read (SP2, PL7), and the chooser's use-at-once path for items with no spell (IA15 a) is the only other item path to check |
| Item scripts and the flag 0x20 | 6 learned products with scripts (the Electric ring and shield, the Mechanical helmet and the two flag-0x20 items, the Smithy gauntlets), 2 with the flag, 3 found with the flag | n/a | **T-r1 done** ([ARC-RES-112](technology-item-scripts-res-112.md)): the flag shows hit points only; the equipment scripts 28012 to 28015 pass the static admission, 29016 refuses the wear, the creature items' HIT and MISS scripts do not run | **T-m1** (creature items), **T-m2** (the powered shield) | none | as the note's section 5 |

## 7. Smoke and test coverage

- **Smoke stages that touch technology** (`./arcanum smoke pr --stages ...`): `grenade` (a thrown grenade's spell runs, damage lands, the grenade is destroyed; uses whatever grenade the crash-site cave lays,
  so the record under test is the map's, not a chosen one), `cast` (item-trigger marker, item spell pane), `cast-multi` (area damage), `ranged` and `npc-shot` (firearms, bow), `spell-end` and `hold-hero` (hold flags and run ends),
  `ai-cast` (NPC item and spell use), `inv-ui` (its item steps use plain, non-magical food only), `pickpocket`, and the repair check inside the hero smoke.
- **Stage `tech-craft`** (ARC-DEV-362, slice T-a, run on the map "crash site cave" whose bodies hold bullets: `./arcanum smoke map "crash site cave" --stages tech-craft`): the hero, given points, buys Herbology degrees 1 and 2 and Gun Smithy degree 1 with the sheet's + press; assembles Herbology records 2000 and 2010 and the Gun Smithy record 2800 in the schematic window from components the session creates by prototype; uses one unit of each food item hurt to one hit point and one fatigue (record 150 heals hit points, 151 heals fatigue, both measured), wears the gun, takes the map's bullets and shoots a living NPC until a shot hits (the shots spend the bullets); F7 and F8 keep the degrees, the products, the worn gun and the bullets. Its steps are the helpers of `TerrainViewerTechCraftHarness.cs` (`TechSmokeBuyDegree`, `TechSmokeGive`, `TechSmokeCraft`, `TechSmokeHurtHero` and the held-item lines), so a later technology stage composes the same steps; it prints counts and prototype numbers only.
- **No stage** reads a schematic document, spends a charge cell, uses a Chemistry, Therapeutics or Explosives product, or assembles for an NPC crafter on real data. Those links, and the ones above on synthetic data, are covered by
  `./arcanum smoke self-test` (synthetic window, assembly and script-129 path) and by unit tests: `TechDegreeRulesTests`, `SchematicCatalogTests`, `SchematicAssemblyRulesTests`,
  `SchematicAssemblyRealDataTests` (all 118 recipes), `NpcAssemblyStateTests`, `AreaSchematicsTests`, `ChargeCellRulesTests`, `TechMaintainCellsTests`, `TechDeviceRealDataTests`, `ItemDurabilityRealDataTests`, `RepairStateTests`, the trap and throw test families.
- Per component, the survey's evidence: Damage, Heal and EyeCandy are covered by `SessionSpellCasterTests` and the `cast` stages; ObjFlag and Effect by `SessionSpellEntangleTests`, `EffectStateTests` and `hold-hero`; Destroy of a lying item by `SessionSpellDestroyTests` and `grenade`.
  The statuses of section 5 were computed from the component table, not by running each of the 31 learned records; slice T-b turns that into a test.

## 8. The reads this audit needs (Mac, docs-only; each ends in ledger rows)

| Label | Question | Size |
| --- | --- | --- |
| T-r1 **(done, [ARC-RES-112](technology-item-scripts-res-112.md))** | What the generic item flag 0x20 means (two learned Mechanical items and three found products) and the numeric bit of the grenade flag (0x10, read by AI SP14); what the item scripts do that sit at attach points 24, 25, 7 and 8 on crafted armour and devices (28012 to 28015, 29016, 29017, 1022); whether the SCR host runs them | S to M |
| T-r2 | Item use with a target: the targeting cursor masks per item kind, what the use does on a critter, an object and a tile, and the NPC use of devices and heal items (the AI's grenade rule is already read, ledger SP14, 0x4AC910); refusal messages (**done**, ARC-RES-113: [ledger IA1–IA16](ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113)) | M |
| T-r3 | Consumers of aptitude and expertise outside the combat and gate paths: callers of the aptitude stat (0x4B0740) in the AI and reaction code; effects in the installed data that target expertise in a discipline (race, background, items) | S |
| T-r4 | **Done (ARC-RES-115, [placement census](technology-census-res-115.md)).** Placement census (installed maps, scripts, shop lists): where found schematic documents lie (five found records have no written prototype, O3 of the research), which NPCs have `su` dialogs and what degrees and inventories they carry (O5), where the 146 non-craftable technology items lie | S to M |

## 9. Documents that are behind the code (to fix inside E0-a, the plan's documentation slice)

Five statements are contradicted by `origin/main`: (1) the coverage rows FR-CHAR-017, FR-CHAR-020 and FR-MAGIC-012 say there are no degree buttons in the viewer (`AreaTechPage` has them);
(2) [tech-degrees-runtime.md](../world/tech-degrees-runtime.md) says no caller; (3) [tech-devices-runtime.md](../world/tech-devices-runtime.md) says the item executor and device spells are not implemented
(ARC-MAGIC-020 to -025 delivered them; the runtime path is `SessionItemTriggers`, the `TechDeviceCatalogue` has no consumer in `src`); (4) [item-durability-runtime.md](../world/item-durability-runtime.md) says no viewer shows an item's condition
(broken art shows in the inventory, check when the slice runs); (5) the ledger's "no buttons in the sheet" note of degrees (S6). None is a behaviour change.

**Fixed by ARC-RES-116** (docs-only; the coverage rows of (1) had already been refreshed by ARC-RES-107): (2) `tech-degrees-runtime.md` and the catalogue row of tech degrees now name the sheet's technology page as the caller; (3) `tech-devices-runtime.md` lists the item executor and the device spells as delivered by ARC-MAGIC-020 to ARC-MAGIC-025 (the trap-device path and a `TechDeviceCatalogue` consumer stay open); (4) `item-durability-runtime.md` states what the viewer shows of an item's condition; (5) the "no buttons" note is replaced in `docs/project/status/technology.md`, and the same stale claim in `character-point-pool.md` (skills, stats, technology and spell pages all have buttons).

## 10. Order of work

Labels are planning labels, not task codes (the coordinator hands out codes). Sizes as in the plan: S about 200 lines, M one PR with a smoke stage.

| Slice | Content | Size | Needs | Lane |
| --- | --- | --- | --- | --- |
| T-r1 | read: flag 0x20, the grenade flag's bit, item scripts on crafted gear. **Done** ([ARC-RES-112](technology-item-scripts-res-112.md)): flag 0x20 is the item window's hit-point line only, the grenade bit is 0x10, and the scripts need T-m1 and T-m2 | S to M | Mac read | none |
| T-r2 | read: item aiming, AI grenade pick and device use (**done**, ARC-RES-113) | M | Mac read | none |
| T-r3 | read: aptitude and expertise consumers | S | Mac read | none |
| T-r4 | census: where found schematics, `su` crafters and the non-craftable items are (**done**, ARC-RES-115: [placement census](technology-census-res-115.md)) | S to M | Mac data run | none |
| T-a | **done (ARC-DEV-362)**: smoke stage `tech-craft` on real data: buy degree 1 and 2 on the sheet, assemble a Herbology heal and a Gun Smithy firearm, use the heal, equip and fire the gun, save and load once; asserts item counts, the heal, a firearm hit | M | none (T-r4 only helps with found documents) | viewer smoke |
| T-b | opt-in real-data test: runs each of the 83 records with the hero as target and fails when the classification of section 5 disagrees with the code (it flips with the epic slices) | S to M | none | tests |
| T-j | the five documentation fixes of section 9 (inside E0-a, cloud, docs-only) | S | none | docs |
| E1-b | Summon objects (plan), acceptance: section 6 first row | M | E1-a done | spell-cast lane |
| E1-d | NPC effect list and flags (plan) | M | none | directory lane |
| E4-a, E4-b | ObjFlag bits (plan), acceptance: section 6 second row | M, M to L | E1-d | spell-cast lane |
| E4-c | control components with the cell branch (plan) | L | E1-d | spell-cast lane |
| E4-e, E4-f | teleport and nested Cast, polymorph (plan) | M to L, M | E4-c, E4-a | spell-cast lane |
| T-g | **new**: item aiming cursor and use on a critter, object or tile in the viewer (185 installed prototypes open it, IA16) | M | T-r2 (done) | viewer |
| E6-b | placed trap devices (plan), now also fed by the three trap-device products | M | E1-a, T-g | directory lane |
| T-h | **new**: NPC picks and throws grenades (the rule is SP14; `AreaAiHost.Grenade` answers none today), uses heal items and the chooser's items (devices need nothing, IA15) | M | E1-b | AI lane |
| T-k | **conditional**: expertise effects, aptitude readers in the AI and reaction code, per T-r3's result | S to M | T-r3 | by result |
| T-m1 | **needed** (T-r1's result): creature items. HIT and MISS item events at a thrown item's landing, script actions 40, 21 and 36, the created creature in the directory and the follower rules, a viewer smoke step ([ARC-RES-112](technology-item-scripts-res-112.md) section 5) | M to L | E1-c created critters (planned; E1-a does not create critters) | directory lane and viewer |
| T-m2 | **needed** (T-r1's result): the powered shield. Item-event set admits actions 54, 125 and 135, a deferred 15-second call that checks the wear and uses one power-cell unit, saved with the game; today the Electric degree-3 shield cannot be worn | S to M | the game event queue's script call kind, catalogue entries for action 135 | directory lane |

Waves (a label starts when its needs are done): **wave 0** T-r1 to T-r4, T-a, T-b, T-j (no mutual dependency; T-a and T-b share the viewer and session test harness, so one at a time on the Mac's single Godot slot);
**wave 1** E1-b, E1-d, T-g (after T-r2), T-m1, T-m2 (needed by T-r1's result) and T-k (after T-r3); **wave 2** E4-a to E4-c, E6-b, T-h; **wave 3** E4-e, E4-f. The spell-cast lane is one writer at a time (the plan's conflict rule);
T-g and T-a touch the viewer only; T-h the AI host. The plan's own waves place E1-d in wave 0 and E4-a and E4-c in wave 1; here they sit one wave later only because of the "starts when needs are done" rule, and nothing stops them earlier. This document only adds T-a, T-b, T-g and T-h in front of or beside it.

## 11. Decisions for the owner

| # | Question | Recommendation |
| --- | --- | --- |
| 1 | Put the technology smoke stage `tech-craft` and the coverage test T-b into wave 0, before the content slices? | Yes: they cost no code in the engine and make every later claim checkable |
| 2 | Add T-g (item aiming cursor, use on critter, object or tile) as its own slice now, or fold it into E8-g? | Own slice after T-r2 (the plan's E8-g is "heal another critter", a loose fit): it is a viewer and rules change that trap devices, scrolls and aimed items all need |
| 3 | Add T-h (NPC grenades and devices) before or after E1-b? | After E1-b: a grenade without its flash has nothing to throw at |
| 4 | Keep eye candy as the log only (no light, sound, tint) for technology records until the media lane? | Yes, not counted as a gap here |
| 5 | May T-b be an opt-in real-data test category that fails on drift of the status table? | Yes, the usual opt-in rule applies (an environment variable and a category) |

The original-behaviour decisions technology still needs are already recorded (TC1 to TC12, [owner decisions](technology-evidence.md#owner-decisions-2026-10-20)); the reads T-r1 to T-r4 may add rows, each decided under the bug rule when it comes.
