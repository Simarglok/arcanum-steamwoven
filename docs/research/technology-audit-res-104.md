# Technology audit: eight disciplines, seven degrees, schematics and devices

Status: **Audit and plan**, work item ARC-RES-104 (2026-11-11), trimmed to what is still true on 2026-11-11. Docs-only: the data census below ran locally on the owner's Mac with a
throwaway program outside the repository. It is the technology counterpart of the [magic audit](magic-audit-res-103.md) (ARC-RES-103: it leaves the technology records 140..222 to this document)
and a companion of the [missing-systems plan](../project/missing-systems-plan.md) (ARC-RES-099). The status refresh and the open queue are in the [technology re-audit](technology-audit-res-125.md)
(ARC-RES-125); the live per-record states are the census test `TechCensusRealDataTests` (ARC-DEV-363), whose table is the source for any count. This note keeps the matrix and the readings of the original.
The tables of section 5 carry the states as of the audit; slices since then made every record RUN but 183, 184 and 222 (section 6).

Requirements (reference only, no coverage row changes): [FR-ITEM-011](../requirements/items-economy.md#fr-item-011), [FR-ITEM-016](../requirements/items-economy.md#fr-item-016),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017), [FR-ITEM-018](../requirements/items-economy.md#fr-item-018), [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020).
The original behaviour is in the [technology research](technology-evidence.md) (ARC-RES-062) and the [technology ledger](ledger/technology.md); this document does not re-derive it.

## 1. Answer

1. **The player-facing chain is built for the hero**: buy a degree on the sheet's tech page, open the schematic window (`T`), assemble (all 118 recipes are gate-checked on real data by a unit test), the product lands in the pack,
   and a potion, food, scroll or thrown grenade then runs its spell through the item-trigger host. Smoke stages `tech-craft` (ARC-DEV-362) and `tech-products` (ARC-MAGIC-051) run it on the player's data. Gun Smithy
   and Smithy make **ordinary equipment** with no technology record at all (14 of 14 learned products), and so do most of Mechanical (6 of 7) and Electric (4 of 7); their value is the equipment, attack and wear rules, which are built.
2. **31 learned products carry a technology record** (31 distinct records); 65 records in all are carried by an installed item prototype. When the audit was written 17 of the 65 ran; the gaps were four component groups, all closed or
   narrowed by the slices of section 6: **Summon**, the remaining **ObjFlag bits**, the **control components** and the **Effect component on any critter**.
3. **Technology added six things the missing-systems plan did not hold:** an item **aiming cursor** and use on a critter, object or tile (T-g); NPCs throwing **grenades** (T-h); the generic item flag 0x20 and the **item scripts on crafted gear**
   (read by [ARC-RES-112](ledger/technology.md): the flag only shows an item's hit points; the scripts are creature-item HIT and MISS scripts and equipment scripts, slices T-m1 and T-m2; the tech books' insert and remove scripts, points 21 and 28,
   are [XA9](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114)); **smoke coverage**; an opt-in **coverage test**; a documentation refresh. All are done (section 10).
4. **No missing piece is a hidden mechanic of the original.** Degrees give expertise only, expertise is read only by the sheet and the schematic gate, assembly has no roll, no
   skill and no experience, and a degree has no other effect on a device (technology research, answer 2, 5, 7).

## 2. Method and limits

- **Code:** `origin/main` at `c53887a94`; a read-only survey by symbol search answered each item BUILT, PARTIAL, STUB or ABSENT with a file and symbol.
- **Data:** a throwaway console program outside the repository, built on the repository's own `Arcanum.Data` readers, read the installed game through the content stack and
  printed counts and numbers only (no names, no text). The 798 item prototypes came from the project's prototype cache, built from the retail executable. Its totals agree with the technology research's data census (56 learned, 62 found,
  8 placeholders, 70 spell slots on 70 items, 83 records, 1 learned mismatch). Discipline indexes 0..7 are the order of `mes\tech.mes`; the names below are the owner's list in that order and the content agrees.
- **Component classification** (section 5) is by component type and parameters against what `SessionSpellCaster.Apply` and its helpers do. "Runs" means a code path exists for every component of the record, not that it matches the original down to presentation.

## 3. The chain and where each link stands

| Link | Original (read) | Rules / session | Viewer | Smoke | Remaining |
| --- | --- | --- | --- | --- | --- |
| Degrees 0..7 per discipline, cost 1, intelligence minimum {0, 5, 8, 11, 13, 15, 17, 19}, TC10 floor | F2, [ledger](ledger/technology.md#technology-arc-res-062) | built ([degrees](../world/tech-degrees-runtime.md), `TechDegreeRules`, state tests) | sheet tech page with + and - buttons (`AreaTechPage`, ARC-UI-076) | self-test of the sheet; `tech-craft` buys degrees 1 and 2 and 1 on real data | creation editor: tech page with the degree buttons and the TC10 snapshot floor since ARC-MAGIC-043 (checked against CM8) |
| Expertise {0, 10, 20, 35, 50, 65, 80, 100} % through "expertise" effects | F2 | built (hero and NPC queries) | shown on the sheet and in the window | no | nine effect rules name an expertise: 168 to 175 (+1 per discipline, the tech books' effects, delivered by an insert script the item-event admission refuses today) and 346 (the Arsonist background, explosives +20, live); [ARC-RES-114](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114) XA8, XA9 |
| Learned schematics (7 per discipline) and found ones (62, documents) | F2 | built (catalogue, knowledge, registration) | window tabs, the Read button registers a document | self-test, synthetic | where found documents lie: see the [placement census](technology-evidence.md) (T-r4 done) |
| Component gate and assembly (no roll, TC3 to TC5 fixed) | F2 | built, all 118 recipes real-data tested | window button, hero and NPC crafter (`su`, action 129) | self-test, synthetic; `tech-craft` assembles three learned records on real data | the other 115 records have no run |
| Charge cells (ammunition type 2) and the TC1 switch | F2 | built (rules, hero stack, container switch) | none needed | `tech-products` (charge cell step open) | ChargeNBranch built by ARC-MAGIC-044 |
| Item durability, break, repair | F2 | built | Repair button; durability not shown | repair inside the hero smoke | no repair window or price (E8 area) |
| Item use: self, scroll, food, potion, kit, read | F2 | built (`SessionItemTriggers.UseItem`) | inventory use button and right-click | `inv-ui` (plain food only); `tech-craft` uses two spell-carrying food items (records 150, 151) | the other products are not run |
| Item use at a target, object, tile; trap device placement | F2 (TP1 to TP8) | partly (session) | item cursor (T-g, ARC-DEV-365); placed trap devices (E6-b, ARC-WORLD-124) | their own stages | none |
| Throwing: grenades, thrown weapons | F2 | built | held throw to the cursor | `grenade` (whatever grenade the cave map lays) | Summon of scenery runs (ARC-DEV-352) |
| Item spell triggers (wear, hit, attack start, throw end, pickup, drop) | F2 (IT1 to IT10) | built, wider than wear and unwear | partly | `cast` marker | a few moves are not wired (see [triggers](../world/item-spell-triggers.md)) |
| Firearms, bullets, power cells, technology failure and magick cancel rolls | F2 (DV4, DV5) | built | built | `ranged`, `npc-shot` | none specific to crafted guns |
| Aptitude and resistance (magic resistance scaled by negative aptitude, item power, penalty fields) | F2 | built | sheet | unit tests | the AI and the reactions have no unmodelled reader of aptitude ([ARC-RES-114](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114) XA3, XA4) |
| Technical skills: Repair, Pick Locks, Firearms built; Disarm Traps session only | F2 | partly | partly | partly | E6-a, E8-b |
| NPC use of items in the AI | F1 (AI I8 partly) | potions and food with spells built; grenade pick built by T-h (ARC-DEV-364); heal-with-item hosted (ARC-WORLD-116); read, lockpick, trap device not reachable by installed data (IA15) | n/a | `ai-cast` | none left |

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
(slot 0, charges 1 unless noted) and what its components do; **State** is the verdict of the component classification when the audit was written (the census table is the live source):

- **RUNS**: every component has a code path (Damage, Heal, EyeCandy, the knockback goal, ObjFlag for the bits that exist: entangled, stoned, paralysed, spoken-with-dead (only the entangle, the paralysis and the spoken-with-dead occur in a technology record), Destroy of a lying item).
- **HERO**: runs for the hero (self-use) but not for an NPC (the Effect component kept no effect list for an NPC until ARC-MAGIC-027, E1-d; **no record is HERO any more**, the word stays for the census of slice T-b).
- **PART(x)**: some components run, the named groups do not.
- **INERT**: only the eye-candy log runs.
- **EQUIP**: no technology record; the product is ordinary equipment, ammunition or a tool, whose rules are built.

An eye-candy line is a presentation log in the viewer (no light, sound or tint yet); it is not counted as a gap here. Scripts are item scripts at attach points; their effect was read by [ARC-RES-112](ledger/technology.md) (T-r1, done).

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
| 1 | food, -75, x3 | 1 | 171 poison 290 damage plus an effect (id 359, absent from `effect.mes`: no change), 60 s, aggressive | RUNS (Damage and the effect, on an NPC too since ARC-MAGIC-027) |
| 2 | power-cell ammunition, -30, x5 (its item discipline is Electric: the one mismatch, no effect) | 2 | none | EQUIP (ammunition; the cell stack is built) |
| 3 | food, -85, x1 | 3 | 167 animal-enshroud critter flag, 338 s | INERT (ObjFlag enshroud) |
| 4 | food, -70, x1 | 4 | 166 acid 10 to 100, a flash summon, destroy | RUNS (the flash summon and its destroy run since ARC-DEV-352) |
| 5 | food, -85, x1 | 5 | 165 flee flag, 600 s, aggressive | INERT (ObjFlag fleeing) |
| 6 | food, -80, x1 | 6 | 197 paralysis 6 s | RUNS |
| 7 | food, -75, x1 | 7 | 196 magic damage with the "full" flag | RUNS |

### Electric (discipline 2): tools, armour, a cell weapon, a cell firearm

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic with flag 0x1 (the torch location), -20, x1 | 1 | none | EQUIP (a carried light: the worn-light work V7d, ARC-DEV-335, is the dependency) |
| 2 | ring armour, -25, x1, scripts at 24 and 25 | 2 | none | EQUIP (28012 and 28013 run, T-r1) |
| 3 | shield armour, -40, x1, script at 24 | 3 | none | BLOCKED when audited (29016 refused the wear; ARC-MAGIC-041 built it) |
| 4 | melee weapon, -90, x1 | 4 | 147 electrical 1 to 10, **ChargeNBranch** (one cell per use), cell-powered | PART(control: cell branch) |
| 5 | helmet armour, -50, x1 | 5 | 182 sets and clears magnetic inversion | INERT (ObjFlag magnetic inversion) |
| 6 | torso armour (3 alternatives), -80, x1 | 6 | 168 maintained every 5 s: **ChargeNBranch**, **TestNBranch** on hit points, heal 4 | PART(control: cell and test branches) |
| 7 | power-cell firearm, -55, range 15, x1 | 7 | none | EQUIP (cells are ammunition type 2: built) |

### Explosives (discipline 3): grenades, gases, bombs (every record needs Summon, built for scenery since ARC-DEV-352)

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | generic, -20, x1 | 1 | 219 fire and normal 3 to 15, knockback, flash summon, destroy | RUNS (since ARC-DEV-352) |
| 2 | generic flag 0x10, -15, x5 | 2 | 177 blinding flag 10 s, flash | PART(ObjFlag blind; the flash runs) |
| 3 | generic flag 0x10, -20, x5 | 3 | 178 smoke cloud 10 s (fog scenery), then clears don't-draw | PART(ObjFlag don't-draw; the smoke scenery runs) |
| 4 | generic flag 0x10, -30, x4 | 4 | 179 stun with a flash | RUNS |
| 5 | generic flag 0x10, -60, x3 | 5 | 175 fire and normal 5 to 30, knockback, flash | RUNS |
| 6 | generic flag 0x10, -45, x3 | 6 | 180 fire wall scenery 10 s | RUNS |
| 7 | generic flag 0x4 (trap device), -85, x1, charges 0 | 7 | 176 fire 5 to 20 and normal 100 to 200, knockback, flash; slot 1 of a **timed trap device** (TP2, TP3) | RUNS as a spell (placing the device is E6-b) |

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
| 2 | generic, -50, x3 | 2 | 184 ends with a Use component after 4 s; its begin Destroy takes the pack item itself (also not ready: only lying items) | INERT (control: Use, E4-c; the Destroy of the pack item is outside the census) |
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

### Therapeutics (discipline 7): seven potions of stat effects (all RUNS since ARC-MAGIC-027; HERO when this audit was written)

| Deg | Product | Needs | Record | State |
| --- | --- | --- | --- | --- |
| 1 | food, -56, x3 | 1 | 157 persuasion +2 | RUNS |
| 2 | food, -78, x3 | 2 | 158 strength +2 | RUNS |
| 3 | food, -40, x3 | 3 | 159 perception +2 | RUNS |
| 4 | food, -55, x3 | 4 | 160 dexterity +2 | RUNS |
| 5 | food, -85, x1 | 5 | 161 intelligence, perception, willpower, charisma +2 | RUNS |
| 6 | food, -92, x1 | 6 | 162 strength, dexterity, constitution, beauty +2 | RUNS |
| 7 | food, -90, x1 | 7 | 163 as 161 and 162 together | RUNS |

(The therapeutics effects have no duration of their own in the record, they end by `End` actions; the hero-side Effect component adds and removes them.)

### Found schematics (62 records, by the discipline of the first product)

| Discipline | Records | Products | With a technology record | Records and states |
| --- | --- | --- | --- | --- |
| Herbology | 4 | 3 food, 1 armour | 3 | 189, 190, 192 RUNS |
| Chemistry | 4 | 4 food | 4 | 193 RUNS, 194 PART(flags: undead, mind control), 195 PART(fatigue limit), 197 RUNS (also learned) |
| Electric | 5 | 2 weapons, 2 armour, 1 ammunition | 0 | EQUIP |
| Explosives | 14 | 9 generic, 3 weapons, 2 ammunition | 8 | 148 PART(ObjFlag), 198, 199, 201, 202 RUNS, 200 PART(ObjFlag don't-draw), 203 INERT (nested Cast), 220 RUNS (a timed bomb; placing it is E6-b) |
| Gun Smithy | 12 | 12 weapons | 1 | 204 RUNS (magic damage on a weapon) |
| Mechanical | 12 | 8 generic, 2 weapons, 2 armour | 0 | EQUIP |
| Smithy | 7 | 4 weapons, 2 armour, 1 keyring | 0 | EQUIP |
| Therapeutics | 4 | 4 food | 4 | 141 RUNS, 142 INERT (mind control), 144 RUNS, 206 RUNS |

Quantities: 56 records make one item, one makes 2, four make 3, one makes 5. Two found records (an Electric and a Mechanical one) have three alternatives in a component and the product; the learned ones with three are Electric degree 6 and Smithy degrees 6 and 7.
Quantities: 56 records make one item, one makes 2, four make 3, one makes 5. Two found records (an Electric and a Mechanical one) have three alternatives in a component and the product; the learned ones with three are Electric degree 6 and Smithy degrees 6 and 7.

### Records that are not on any schematic

Of the 65 carried records, 15 are carried only by items that no schematic makes (they lie on maps or in stock sets: 12 of the 15 on a map, 3 scrolls only through scripts of unplaced NPC prototypes, [placement census](technology-evidence.md)): 164, 169, 170, 173, 191, 207, 208, 211, 212, 213 and 172, 214, 183, 210, 218.
Eighteen records are carried by no item: 140, 143, 145, 146, 149, 174, 185, 205, 215, 216, 221, 181, 209, 217, 186, 187, 188, 222. They exist for scripts and the AI's lists; no installed item uses them. 270 items are technological in all
(complexity below 0): 124 are craftable, 146 are not (per discipline 10, 15, 11, 11, 25, 22, 38, 14 not craftable); 61 of the 270 carry a technology record.

## 6. Gaps by component, with the plan slice that closes each

When the audit was written, four component groups and two product areas held the records back. Where each stands (the census table holds the per-record state):

| Gap | Records | Closed by |
| --- | --- | --- |
| Summon (flash 4050 x15, smoke 4033, fire wall 4031, gas 4029, one list) with Destroy of the summoned and the don't-draw callback | 166, 175, 176, 179, 180, 185, 198, 199, 201, 202, 205, 219, 220 and, with another group, 148, 177, 178, 200, 217 | E1-b for scenery (ARC-DEV-352); the creature list of 183 is E1-e, open |
| ObjFlag bits other than entangle, paralysis, spoken-with-dead | fatigue limit 153, 195, 210; blind 177, 186; flee 148, 165; mind control 142, 194; drunk 172, 181; magnetic inversion 182, 187; enshroud 167; undead 194; shielded 209; don't-draw 178, 183, 200, 217; permanent polymorph 222 | E4-a, E4-b (ARC-MAGIC-034 to -040, -047, -052; flag readers: [re-audit section 4](technology-audit-res-125.md#4-readers-of-the-flags-the-technology-records-write)); 222 open; the viewer draw of don't-draw, hidden, shrunk and translucent objects (178, 183, 200, 217) is E4-a2, open |
| Effect on any critter | 22 records (the old HERO group plus 172, 181, 195) | E1-d (ARC-MAGIC-027) |
| Control components: ChargeNBranch, TestNBranch, Terminate, Use, Cast, Movement, Trait, AIRedirect, NoOp | 147, 168, 184, 203, 218, 183, 187, 222 | E4-c (ARC-MAGIC-044, -048), E4-e (ARC-MAGIC-049); AIRedirect 183 (E1-e) open |
| Item aiming and use on a critter, object or tile | every aggressive carried record used as an item, and trap devices | T-g (ARC-DEV-365; [ledger IA1–IA16](ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113)) |
| Trap devices | Mechanical degree 1 and 5, Explosives degree 7 (176 and 220, the timed bombs 15154 and 15193) | E6-b (ARC-WORLD-124: marker, delayed run at the tile after 88 000 ms (TP3), refusals of IA13) |
| NPC use of grenades and devices | the AI's grenade pick | T-h (ARC-DEV-364; no installed AI item entry is a trap device or a written item, IA15) |
| Item scripts and the flag 0x20 | the Electric ring and shield, the Mechanical helmet and the two flag-0x20 items, the Smithy gauntlets | T-m1 (ARC-MAGIC-042, creature items), T-m2 (ARC-MAGIC-041, the powered shield) |

## 7. Smoke and test coverage

- **Stage `tech-craft`** (ARC-DEV-362, slice T-a, on the map "crash site cave": `./arcanum smoke map "crash site cave" --stages tech-craft`): the hero buys Herbology degrees 1 and 2 and Gun Smithy degree 1 with the sheet's + press; assembles Herbology records 2000 and 2010
  and the Gun Smithy record 2800 from components the session creates by prototype; uses the foods hurt to one hit point and one fatigue (records 150 and 151 measured), wears the gun and shoots a living NPC until a shot hits; F7 and F8 keep the degrees, products, gun and bullets.
  Its steps are the helpers of `TerrainViewerTechCraftHarness.cs`, so a later technology stage composes the same steps; counts and prototype numbers only. Sibling stage `tech-products` (ARC-MAGIC-051) is in the [re-audit](technology-audit-res-125.md#5-smoke-and-test-gaps).
- Other stages touching technology: `grenade` (whatever grenade the cave map lays), `cast`, `cast-multi`, `ranged`, `npc-shot`, `spell-end`, `hold-hero`, `ai-cast`, `inv-ui` (plain food only), `pickpocket`, and the repair check inside the hero smoke. The links on synthetic data are covered by
  `./arcanum smoke self-test` and unit tests: `TechDegreeRulesTests`, `SchematicCatalogTests`, `SchematicAssemblyRulesTests`, `SchematicAssemblyRealDataTests` (all 118 recipes), `NpcAssemblyStateTests`, `AreaSchematicsTests`, `ChargeCellRulesTests`,
  `TechMaintainCellsTests`, `TechDeviceRealDataTests`, `ItemDurabilityRealDataTests`, `RepairStateTests`, the trap and throw test families.
- **Technology census `TechCensusRealDataTests`** (ARC-DEV-363, slice T-b; opt-in: `DAT_ROOT` = the game folder, category `RealData`, a few seconds): applies every component of every action of the 83 records 140..222 to the hero and to an NPC on a fresh session each (the harness of the magic census,
  [MA-0](magic-audit-res-103.md#spell-census-ma-0)), and gives each record a state word: **RUNS** (the host takes every component on both targets), **HERO** (on the hero alone), **PART** (a component is not taken, another that is not eye candy is), **INERT** (only the eye-candy line is). The test holds one row per
  record that is not RUNS and fails with the changed rows to paste when a slice moves one; it also counts the records carried by an installed item prototype (65) by state. Two readings are made by rule: a **Destroy** acts on a created object or a lying item, so it is left out of the lists (the records that hold one are pinned);
  a **Summon** on an object entry summons nothing (TA9), so it is ready only when the record names a basic prototype whose file (+20) reads as a scenery, as `SessionSpellCaster.SummonComponent` asks, and not for a list pick or a critter (183). Limits: the census checks the host's component dispatch, not a cast
  (no area, save, run or smoke), and not the item side (the Begin Destroy of a pack item, 184).

## 8. The reads this audit needs (Mac, docs-only; each ends in ledger rows)

All four are done.

| Label | Question and result |
| --- | --- |
| T-r1 ([ARC-RES-112](ledger/technology.md)) | What the generic item flag 0x20 means and the numeric bit of the grenade flag (0x10, AI SP14); what the item scripts at attach points 24, 25, 7 and 8 on crafted gear do (28012 to 28015, 29016, 29017, 1022); whether the SCR host runs them. The flag shows hit points only; the equipment scripts 28012 to 28015 pass the static admission, 29016 refused the wear, the creature items' HIT and MISS scripts did not run |
| T-r2 (ARC-RES-113, [ledger IA1–IA16](ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113)) | Item use with a target: the targeting cursor masks per item kind, what the use does on a critter, an object and a tile, the NPC use of devices and heal items (the AI's grenade rule is ledger SP14, 0x4AC910), refusal messages |
| T-r3 (ARC-RES-114, [ledger XA1 to XA10](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114)) | Consumers of aptitude and expertise outside the combat and gate paths (the getter 0x4B0490 with the literal 16; 0x4B0740 is its base level) and effects that target expertise: no unmodelled reader in the AI or the reactions; expertise has two consumers (the sheet and the gate); nine effect rules name an expertise, delivered by the Arsonist background and by eight tech books |
| T-r4 (ARC-RES-115, [placement census](technology-evidence.md)) | Where found schematic documents lie (five found records have no written prototype, O3 of the research), which NPCs have `su` dialogs and what degrees and inventories they carry (O5), where the 146 non-craftable technology items lie |

## 10. Order of work

Labels are planning labels, not task codes. Every slice below is done or moved to the [re-audit](technology-audit-res-125.md#6-queue) (E4-a2, MA-8, E1-e, E1-f, T-s remainder).

| Slice | Content | Done by |
| --- | --- | --- |
| T-r1 to T-r4 | the reads of section 8 | ARC-RES-112 to -115 |
| T-a, T-b | smoke stage `tech-craft`; the census test (its first run corrected the matrix: no HERO record, Summon of scenery runs) | ARC-DEV-362, -363 |
| T-j | five stale statements of the docs (degree buttons, `tech-degrees-runtime.md`, `tech-devices-runtime.md`, `item-durability-runtime.md`, the "no buttons" note) | ARC-RES-116 |
| E1-b, E1-d | Summon of scenery; NPC effect list | ARC-DEV-352; ARC-MAGIC-027 |
| E4-a, E4-b, E4-c, E4-e | ObjFlag bits; control components with the cell branch; nested Cast | ARC-MAGIC-034 to -040, -044, -048, -049 |
| T-g | item aiming cursor and use on a critter, object or tile (185 installed prototypes open it, IA16) | ARC-DEV-365 |
| E6-b | placed trap devices | ARC-WORLD-124 |
| T-h | NPC picks and throws grenades (rule SP14; the exe's inverted radius test TH1 is a bug the owner fixed in both modes, ARC-DEV-373; [ledger TH1–TH4](ledger/npc-ai.md#npc-grenade-host-arc-dev-364), [grenade step](../world/ai-host.md#grenade-step-arc-dev-364)); heal items hosted by ARC-WORLD-108, -116 | ARC-DEV-364 |
| T-m1, T-m2 | creature items (HIT and MISS item events at a thrown item's landing, actions 40, 21 and 36); the powered shield (actions 54, 125, 135, a deferred 15-second call using one power-cell unit) | ARC-MAGIC-042; ARC-MAGIC-041 |
| T-m3 | tech books: insert script 30017 at point 21 gives +intelligence expertise, remove script 30018 at point 28 ([XA11](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114)) | ARC-DEV-366 |
| T-k | dropped by T-r3's result (no unmodelled reader of aptitude or expertise) | n/a |

The original-behaviour decisions technology needs are recorded (TC1 to TC12, [owner decisions](technology-evidence.md#owner-decisions-2026-10-20)). Eye candy stays the presentation log only (no light, sound or tint) and is not counted as a gap.
