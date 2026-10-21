# Technology: disciplines, schematics, assembly, devices and item durability

Status: **Research**, work item ARC-RES-062 (2026-10-20). This document fixes
how the game runs technology: the eight disciplines and their degrees, the
expertise a degree gives, the learned and found schematics, the schematic
window and its component gate, assembly, how technological items and the
technology records 140..222 of the shared magick/tech table behave, which
scripts and dialogs touch technology, and how item durability (hit points,
breaking, price and the item side of repair) works. It is the technology
counterpart of the [magic core research](magic-core-evidence.md)
(ARC-RES-043) and builds on the [combat-domain load map](combat-domain-load-map.md)
(ARC-RES-040). Unlike those two, every fact below was **read in the retail
executable** under the [original executable check](original-executable-check.md);
no source reconstruction was used. It changes no production API, reader,
viewer, session schema or dialog port.

Requirements: [FR-ITEM-017](../requirements/items-economy.md#fr-item-017)
(found and learned schematics), [FR-ITEM-018](../requirements/items-economy.md#fr-item-018)
(assembly), [FR-ITEM-011](../requirements/items-economy.md#fr-item-011)
(durability, break and the item side of repair),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magic
against technology), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011)
(which tech items use the item triggers), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012)
(the schematic window and degree editing), and the degree part of
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017) and
[FR-CHAR-020](../requirements/characters-skills.md#fr-char-020) (tech points
and aptitude).

## Boundary with the magic and skills research

Agreed through the coordinator on 2026-10-20:

- The **magic** research owns the shared magick/tech engine: the record
  reader for all 223 records, the run pipeline, the 25 component types
  (ChargeNBranch included), resistance and the payment step. This document
  owns the **charge-cell source** behind that payment (which ammunition pool,
  how much an item or critter pays), the list of tech items and devices that
  use the engine, and device behaviour in magickal hands.
- The item trigger executor (0x4CB830) is one shared later slice for magic and
  tech items, owned by the magic research; this document only says which tech items reach it.
- The **skills** research owns the four technical skills, the Repair skill
  rule ([SK7](original-verification-ledger.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027)),
  fate points (built as ARC-WORLD-054) and the shared character-point spend and refund
  path (built as ARC-WORLD-052). Item durability (item hit points and the broken
  state) is owned here and is the input of the Repair slice ARC-SKILLS-011.

## Vocabulary and evidence

- **Reading**: a static reading of one function of `Arcanum.exe` 1.0.7.4 with
  Xcode's `llvm-objdump` at the address given, on the owner's Mac, by the
  method's steps. Field numbers come from the executable's own object field
  name table. Every direct branch of every function named here stays inside
  `.text`; none reaches the GOG `.IIDKing` section.
- **F1** is one reading; **F2** is a row a second, independent reader (a fresh
  agent that did not see the first verdicts) re-read with the same result.
  Thirteen decision-bearing questions (as the readers counted them) were re-read:
  10 agree, 2 were corrected to the second reader's version, 1 disagreement stands
  ([Second reading](#second-reading)). Rows without a mark are F1.
- **Anomaly**: a branch that looks like a slip, recorded as **TC1..TC10**
  under [Anomalies](#anomalies-tc1tc10), each with a verdict and a proposal
  for the two rule modes of owner decision D8
  ([rule modes](../world/rule-modes.md)). Nothing is adopted silently; under
  D2 the slice that meets an anomaly records the decision.
- **Project policy**: a choice this project makes where the original leaves
  one open. Every policy below is a **proposal**.

Data counts come from the repository's own DAT, MES and prototype readers over
the content stack (precedence of [content-precedence](content-precedence.md)),
counting only: no game text, record contents, bytes or listings are stored.
Map instances (objects placed on maps) and scripts were not counted except
where a row says so. Executable: SHA-256
`9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`,
`Version.txt` 1.0.7.4.

Roles: the **crafter** is the critter whose degrees and inventory the
schematic window uses; the **viewer** is the second party, the one who sees the
window and its messages. Integer arithmetic is C division, truncating toward
zero.

## Answer

1. **Eight disciplines, degrees 0..7, one point each.** A degree is stored per
   critter in the spell/tech array (elements 17..24). Raising it needs the
   **effective** intelligence (with effects) at least {0, 5, 8, 11, 13, 15,
   17, 19} for the new degree, stops at 7 and adds 1 to the base tech points;
   lowering refunds the point and needs no check. A base intelligence decrease
   is refused when the new effective intelligence would no longer hold every
   known spell or degree (F2).
2. **A degree gives expertise in percent, not a level.** Effective expertise
   is {0, 10, 20, 35, 50, 65, 80, 100}[D], where D is the stored degree lowered
   until the effective intelligence meets its minimum, then passed through the
   "expertise in discipline" effects with no clamp (F2). Only the character
   sheet and the schematic component gate read it; devices do not.
3. **Schematics are 7-line records in `rules\schematic.mes`.** Name,
   description, art, component 1, component 2 (each up to three alternative
   prototypes), product (up to three alternatives) and quantity; texts in
   `mes\schematic_text.mes` (F2). Learned schematics are 2000..3999, seven per
   discipline; found schematics 4000 and up; placeholders 6000 + 10 ×
   discipline for an empty tab.
4. **Learned schematics come with degrees; found ones are documents.** The
   learned tab shows the first *stored degree* records of each discipline in
   file order (always the numbers `1990 + 200 × discipline + 10 × degree` in
   the installed data). Reading a schematic written item (subtype 5) appends
   its number to the PC's found list once and destroys the document (F2).
5. **The window gates by component, assembly does not re-check.** Each
   component must be in the crafter's own inventory, not worn, not broken, as
   an exact prototype; the crafter's expertise in the **component's**
   discipline must be at least minus the component's complexity. Assembly
   finds both components, consumes them (one unit for ammunition), then creates
   the product the given number of times into the crafter's inventory (F2). No
   roll, skill, action points, time or experience.
6. **Who crafts.** Key `T` and the interface button open the window for the
   player; a dialog's `su` action opens it with the **NPC as crafter** (its
   degrees, its inventory, the product goes to it) (F2). Script action 129
   calls an uninitialised pointer (TC2) and is unused in the data.
7. **Technological items are not refused to mages.** Item power
   `(complexity + aptitude) / 2` clamped to `complexity..0` makes the item's
   "magic adjustment" fields a penalty for a magickal holder; use, wear and
   item triggers never test the complexity sign. The only extra device cost is
   charge cells for the three cell-powered records, and their item-source
   formula is inverted (TC1, F2). Tech records skip the BEGIN checks and
   payment but **do** pay maintenance (refines the magic study).
8. **Durability is three saved fields.** Maximum = points + adjustment;
   current = maximum − damage. Combat damage, armour and weapon wear and area
   effects add damage; at 0 the item **breaks** (the destroyed art bit, damage
   reset to 0, unwielded, script BUST), a second break deletes it (F2). Broken
   blocks wearing, selling and schematic use, not use or item spells (TC9).
   Damage lowers the price only when the PC sells; the repair quote counts
   damage twice (TC7).
9. **This reading records ten anomalies (TC1..TC10)**, among them the inverted
   cell payment (TC1), the uninitialised script callback (TC2), an assembly
   that can consume without producing (TC3) and degree refunds below the level
   start in the character editor (TC10).

## State model

| State | Where | Notes |
| --- | --- | --- |
| Degrees | critter spell/tech array elements 17..24, one per discipline, 0..7 | zeroed on creation and on a level reset to 1 (0x4AFDE0, 0x4A6F00); non-critters read 0 (0x4AFE60) |
| Tech points | stat 23 base, moved by the degree cost (1) and by tech skill points | aptitude `(50 × MP − 55 × TP) / 10` + sector, already in [`CharacterStatRules`](../world/character-stats-runtime.md) |
| Found schematics | PC field `OBJ_F_PC_SCHEMATICS_FOUND_IDX` (0x10B): schematic numbers, append-only, no capacity limit | registration (0x4B0120) |
| Item complexity, discipline | item fields 0x60 (sign: < 0 technological, > 0 magickal, 0 neutral) and 0x61 | power (0x4614A0) |
| Item spells, charges | item fields 0x64..0x68 (five spell numbers), 0x69 (charges; negative unlimited) | trigger executor (0x4CB830), payment (0x450420) |
| Item durability | object fields hp_pts 0x1B, hp_adj 0x1C, hp_damage 0x1D, saved per object | max (0x43D5A0), current (0x43D600), set (0x43D530) |
| Broken | the destroyed bit of the item's current and inventory art | break (0x43F1C0) |
| Window | Schematic-UI module: interface mode 14, current tab, discipline, page | init (0x56CD60), open (0x56D130) |

## Findings

### Disciplines and degrees (tech module 0x4AFCD0..0x4B0340)

The module has 17 functions:

- **Init** (0x4AFCD0, F2 by ARC-TECHNOLOGY-001, [ledger SD1](original-verification-ledger.md#schematic-data-arc-technology-001))
  loads `mes\tech.mes`, 90 entries: 0..7 discipline names,
  8..15 degree names, 16..23 discipline descriptions, 24..87 the 64 degree
  descriptions at `24 + discipline + 8 × degree`, 88 and 89 the two
  registration messages (read by the registration, not by the init). A missing file fails the module. Exit unloads it
  (0x4AFDD0). Text getters: discipline name (0x4AFE10), its description
  (0x4AFE20), degree name (0x4AFE30), degree description (0x4AFE40).
- **Zero** (0x4AFDE0) clears the eight degrees; called at object creation and
  by the level reset (0x4A6F00).
- **Get** (0x4AFE60): a PC or NPC's stored degree, 0 for any other object.
  **Set** (0x4AFF90) writes without checks and returns the value.
- **Raise** (0x4AFEC0, F2, agrees with ledger AL8): a non-critter gets 0; a
  multiplayer client changes nothing; degree 7 is the cap; the **effective**
  intelligence must be at least the next degree's minimum; then base stat 23
  rises by the cost and the degree by one. It does not check unspent points:
  the character editor's buy wrapper (0x589A20) spends the point and shows the
  refusal texts.
- **Lower** (0x4AFFF0, F2): at degree 0 nothing; otherwise base stat 23 falls
  by the current degree's cost and the degree by one, no intelligence check.
  The editor's sell wrapper (0x589AF0) refunds the unspent point.
- **Tables**: cost (0x4B00A0) {0, 1, 1, 1, 1, 1, 1, 1}; intelligence minimum
  (0x4B0110) {0, 5, 8, 11, 13, 15, 17, 19}; the level percentage {0, 10, 20,
  35, 50, 65, 80, 100} (table 0x5B5144) is read only by effective expertise.
- **Effective expertise** (0x4B00B0, F2): start from the stored degree D;
  while the effective intelligence is below the minimum of D, lower D; the
  result is the percentage of D passed through the attribute effects
  "expertise in discipline" (effect evaluation 0x4EA9F0 → 0x4EA6C0: percent,
  multiply, divide, add, min, max, every cause). No clamp follows: an effect
  can push it above 100 or below 0. Callers: the character sheet (display) and
  the schematic component gate only.
- **Intelligence guard** (0x4B02B0, F2 after correction): "would intelligence
  I hold every current degree" (0 when some `minimum[stored degree] > I`). The
  base stat setter (0x4B0980) calls it only for intelligence (stat 4) and only
  on a decrease: it writes the new base, takes the new **effective**
  intelligence, checks the spells (0x4B1B90) and then the degrees, and on
  refusal restores the base. The editor calls it too. A temporary drop of
  intelligence through effects is not guarded: it only lowers the effective
  degree in 0x4B00B0. Since raising also reads the effective intelligence, a
  temporary intelligence effect lets a degree be bought that later counts as a
  lower one.
- **Learned schematic number** (0x4B0320, F2): `1990 + 200 × discipline + 10 ×
  degree`. Used by the editor's degree tooltips (schematic names, 0x56EA10,
  0x56EA30) and by the multiplayer auto-equip (0x4A5EE0, `Rules\AutoEquip.mes`,
  which gives the product of the highest learned schematic per discipline).
  The schematic window does not use it (below).

### Schematic records (FR-ITEM-017, FR-ITEM-018)

The Schematic-UI module runs from init 0x56CD60 (reset 0x56D0B0, exit
0x56D040) to the next module at 0x56EA90.

- **Init** (0x56CD60, F2 by ARC-TECHNOLOGY-001, [ledger SD2](original-verification-ledger.md#schematic-data-arc-technology-001))
  loads `rules\schematic.mes` and `mes\schematic_text.mes`;
  without the second it unloads the first and fails. The number of learned
  schematics is the count of entries in 2000..3999 divided by 7 (0 when entry
  2000 is missing); for a count above 0 the starts 2000, 2010, … below 3999
  whose entry exists form the "full list", whose length for the window is the
  count (TC11).
- **Record** (0x56DBD0, F2): seven consecutive entries n..n+6 of
  `rules\schematic.mes`: n the name's entry in `schematic_text.mes` (`atoi`),
  n+1 the description's entry, n+2 the art, n+3 and n+4 components 1 and 2,
  n+5 the product, n+6 the quantity. Components and products hold up to three
  alternative base numbers; the prototype is the number + 20.
- **Parse** (0x56DD20, F2 by ARC-TECHNOLOGY-001, [ledger SD6](original-verification-ledger.md#schematic-data-arc-technology-001)):
  the line is copied into a 2000-byte buffer, split at
  single spaces with `strtok`, each token through `atoi`. One or two tokens: the last value repeats
  to fill three; no token: the field is left unwritten; more than three: the
  write runs past the field (TC6). A missing entry yields the message module's
  error text, so `atoi` gives 0; on a list line its six tokens write past the
  field (ledger SD5).
- **Discipline of a schematic** (0x56DB00, F2 by ARC-TECHNOLOGY-001): number ≥ 4000 → the discipline
  field of the first product's prototype; 2000..3999 → `(n − 2000) / 200`;
  otherwise 0.
- **Selection** (0x56DB60, F2 by ARC-TECHNOLOGY-001, [ledger SD4](original-verification-ledger.md#schematic-data-arc-technology-001)):
  the k-th record (k the page) of the current tab
  with the current discipline; a tab count of 0 for the discipline (or no
  match) gives −1, and the record read then reads
  the placeholder record `6000 + 10 × discipline`.

### Learned and found lists

- **Learned tab** (0x56D130): the full list, with the count per discipline
  equal to the crafter's **stored** degree: the first "degree" records of the
  discipline in file order. Intelligence does not hide a learned schematic;
  it only lowers the effective expertise the gate uses.
- **Found tab** (PC crafter only): the numbers of field 0x10B, grouped by
  discipline through 0x56DB00. An NPC crafter is reset to the learned tab.
- **Registration** (0x4B0120, F2), from reading a written item of subtype 5
  (0x56BB60; a dead reader is refused, other subtypes open the reading
  window): the schematic number is the written item's "start text line"
  field; a linear search of the found list; already known → `tech.mes` 88
  (message type 13) and the document stays; new → appended (no limit), the
  document **destroyed**, then 89. The message names the schematic
  (0x56E9D0). No sound. The reader's type is not checked.

### The window and its gate (FR-ITEM-018, FR-MAGIC-012)

- **Open or toggle** (0x56D130) takes the crafter A and the viewer B. An
  open window closes (0x56D2D0). A null handle or a dead A or B refuses.
  Interface mode 14 is set by 0x551A80, which always succeeds; the open
  function has **no combat or dialog check**, so the guard is the caller's.
- **Openers**: key `T`/`t` in both key-handler tables (0x54BCA0, 0x54CFB0) and
  the interface button, both with (player, player) and the same guard as the
  character sheet and logbook keys
  ([CS1](original-verification-ledger.md#character-sheet-arc-ui-007),
  [LB1](original-verification-ledger.md#logbook-arc-ui-008)); the dialog action
  `su` sets outcome 7, which calls 0x567E30 → 0x56D130(**NPC**, player) (F2);
  script action 129 (TC2).
- **Component gate** (0x56E190, F2 by ARC-TECHNOLOGY-004, [ledger SA1](original-verification-ledger.md#schematic-assembly-arc-technology-004)), drawn per component slot: only the
  crafter's own inventory is searched (0x462540, flags 7), without the party
  or nested containers; a match is the exact prototype; worn items (slots
  1000..1008), broken items and items with a changed description are skipped,
  and items in slots 2000..2009 are only a fallback. The first alternative
  found sets the prototype, otherwise the first alternative is shown.
  **Expertise**: the crafter's effective expertise in the **component
  prototype's discipline** must be at least `−complexity` of that prototype, a
  signed compare, so neutral and magickal components always pass.
- **Button** (page draw 0x56DDC0): three states, "can assemble", "an item is
  missing" and "expertise is lacking for at least one component"; the button
  assembles in the first, otherwise shows `schematic_text.mes` 0 or 1; hover
  texts 3, 4, 5. Code 2 is "expertise lacking" (checked first, message 1) and
  code 1 "an item is missing" (message 0): settled by a third reading and the
  installed texts (ARC-TECHNOLOGY-004, [ledger SA2](original-verification-ledger.md#schematic-assembly-arc-technology-004);
  O7 closed). Sounds: page 3010, tab 3011, open 3008.

### Assembly (0x56E720, FR-ITEM-018, F2)

1. A multiplayer client changes nothing (the client's button sends packet
   0x4F); schematic −1 returns 0.
2. Find component 1 (the first alternative present, index i1), then component
   2 (index i2); either missing → 0, nothing spent. Both searches happen before
   any consumption (TC4).
3. The product is the product alternative i1 when i1 ≠ 0, else i2.
4. Consume component 1, then 2: ammunition (type 6) loses one unit of its
   kind (0x4658E0), anything else is destroyed.
5. "Quantity" times: create the product on the crafter's tile (0x4ED8B0) and
   transfer it to the crafter (0x4617F0, result unchecked). A failed creation
   returns 0 at once, components already spent (TC3).
6. Completion (0x4EE0F0 → 0x56E950): when the viewer is the local player,
   `schematic_text.mes` 2 and sound `3018 + current discipline` (3018..3025;
   corrected by ARC-TECHNOLOGY-005: the first reading wrote "current tab", but
   the value is read from the current discipline, not the learned/found tab;
   [ledger SW6](original-verification-ledger.md#schematic-window-arc-technology-005)).

Assembly does **not** check expertise (TC5). There is no random roll, skill
check, action-point or time cost and no experience.

### Technological items (FR-MAGIC-010, FR-MAGIC-011)

- **Power** (0x4614A0, 0x461520, 0x461540, 0x461590; agrees with
  [EQ9](original-verification-ledger.md#equipment-arc-world-028)): a critter
  holder gives `p = (c + aptitude) / 2`, clamped to `[c, 0]` for technology and
  `[0, c]` for magick (a non-critter holder gives c); ratio `100 × p / c` (100
  for c = 0); adjustment `v × p / c` (magick) or `v − v × p / c` (technology).
  So a technological item's "magic adjustment" fields are a penalty that
  applies in full when the holder's aptitude is at least `|c|` (strongly
  magickal) and vanishes at an aptitude of c or below.
- **Holder can use** (0x456A10): complexity ≤ 0 always allowed; a holder with
  aptitude ≥ 0 always allowed; otherwise the power must be > 0. It only stops
  magickal items in technological hands; the target's aptitude is read and
  dropped. Use (0x462CC0), the item trigger executor (0x4CB830) and this gate
  never test the sign, so **nothing refuses a technological item to a mage
  outside combat**. Technological items weaken only through their penalty
  adjustments (armour class, resistances, weapon) and the cell payment; in
  combat the [technology failure roll](combat-core-evidence.md) applies.
- **Item triggers** (0x4CB830): each of the five item spells whose
  `ItemTriggers` mask matches, chance flags 10/20/40/66 %, target self, the
  worn weapon (1004), armour (1006) or the object, then a run (0x455A20,
  0x455AC0). No test of complexity sign, power, degree or broken state.
- **Charge cells** (payment 0x450420, record flag 0x20 `ChargesFromCells:`,
  F2): checked before every other payment branch and always spends (the
  "apply" flag is ignored). A critter source pays exactly `cost` cells
  (ammunition type 2) from itself. An item source takes cells from its parent
  (field 0x58) and needs `2 × cost − adjust(item, parent, cost)` = `cost +
  trunc(cost × p / c)`: `cost` for a holder whose magickal aptitude is at least
  `|c|` (power 0), about `1.5 × cost` for a neutral holder, up to `2 × cost` for
  a holder with aptitude at or below c or a non-critter parent (TC1). Too few cells
  refuse and take nothing. A record without cells makes an item source spend
  one charge (field 0x69; 0 refuses, negative is unlimited).
- **ChargeNBranch** (0x451850, component type 5 of `rules\magictech.mes`)
  pays the component's `Cost:` through 0x450420(source, cost, apply, spell);
  on failure it jumps to `Branch:` or ends the list; without a source it does
  nothing. (0x452300 is type 14, Interrupt, as the magic study says.)
- **Tech records 140..222 against magic**: BEGIN (0x452F20) with the tech flag
  (bit 0x4) skips the intelligence and willpower checks and the BEGIN payment,
  and resistance is skipped, as the [magic study](magic-core-evidence.md#resistance-saving-throw-and-aptitude-fr-magic-005-fr-magic-010)
  says. **MAINTAIN (0x453170) does not test the tech flag**: for a critter
  parent it checks the record's intelligence and willpower and charges the
  maintenance cost through 0x450420 (dwarves × 2) unless the run is free, so a
  maintained tech record pays each period (fatigue per the magic study, cells
  for the cell-powered records 168 and 187).
- **Degrees do not change devices.** Effective expertise has no caller outside
  the sheet and the schematic gate; degrees act on devices only through tech
  points → aptitude → item power. The four tech skills work in their own
  domains and do not modify device spells.
- **Wear**: the weapon wear on a hit (0x4B5580, [combat core](combat-core-evidence.md) C21) **falls** with
  `|complexity|`, to nothing at 100, for magickal and technological weapons
  alike (ARC-COMBAT-007 corrected the earlier "up to × 2",
  [ledger CW6](original-verification-ledger.md#combat-wear-arc-combat-007)).

### Scripts and dialogs

- **Scripts**: no SCR action or condition reads or writes degrees or
  schematics. Technology is reached only indirectly: get stat (33) and adjust
  stat (137) on stat 23, get skill (47) for tech skills, add effect (77) with
  expertise effects.
- **Action 129 "start schematic UI"** (case at 0x44A7BD, F2): a PC calls
  0x460B50(PC, PC), an NPC calls it with its PC leader (0x45DDA0) and does
  nothing without one. 0x460B50 calls the pointer at 0x5E8768, copied
  (0x45FD40) from slot 0x110 of the interface callback structure; the builder
  0x57C6E0 never fills that slot (nor 0x148 and 0x154) and the structure is
  uninitialised stack, so the call goes through garbage (TC2). No installed
  script uses action 129 ([SCR census](scr-census.md)).
- **Dialog**: action `su` opens the window with the NPC crafter (8 lines in 5
  of 709 DLG files); condition `ta` tests technological aptitude (`−stat 16 ≥
  N`, mirrored for a negative N; 88 lines in 14 files); `ma` magickal
  aptitude; `sk` with index ≥ 12 a tech skill (0x4C65E0). No dialog condition
  on a degree or a schematic. A follow-up count found the dialog script of
  none of the five `su` files on any of the 497 NPC prototypes (18 NPC
  prototypes hold a dialog script, 3 an auto-level scheme), so the speakers,
  their degrees and inventories are set by map instances (O5).
- **Multiplayer**: the auto-equip (0x4A5EE0); assembly, raise and lower are
  host-only. Out of scope (single player).

### Item durability (FR-ITEM-011)

Re-read for ARC-WORLD-053 by a second reader (F2, [ledger DU1–DU8](original-verification-ledger.md#item-durability-arc-world-053)): every row
below agrees; the second reader added the price's final bounds (negative 0, exactly 1 is 2), that the unwield moves only items in wear
slots and the item is dropped when it fails, and the repair table (training none 10, apprentice 5, expert 1, master 0; 5 for a broken item).

- **Fields and formulas**: hp_pts (0x1B), hp_adj (0x1C) and hp_damage (0x1D)
  are ordinary saved object fields (an instance stores overrides). A
  non-critter's maximum is `pts + adj` (effects only for critters,
  0x43D5A0); current is `maximum − damage` (0x43D600); the damage setter
  (0x43D530) turns a negative value into 0 (a multiplayer client does not
  write).
- **What damages an item**:
  - the combat damage step (0x4B4390) on a non-critter target: normal +
    electrical + fire damage (poison and fatigue do not count), criticals × 3,
    × 2 or × 1.5, a material reduction for portals and containers, at least 0,
    added to the damage; at current ≤ 0 a trap fires and the item breaks.
    Spell and explosion areas reach objects in the area this way, and so does
    script action 60 (damage);
  - armour at the hit location (head 1000, arms 1007, legs 1008, else 1006):
    +10 on fire damage after resistance, with weapon flag 0x100 or on the "damage armour" critical, once
    ([combat core](combat-core-evidence.md) C20); at 0 it breaks (message 24, else 7); the invulnerable flag is not tested;
  - the weapon of the critter that took the damage (the target, or the attacker of a critical self-hit) on critical
    flags, unless it is invulnerable: "damage weapon" +10, breaking it at
    0 (messages 23, 8); "destroy weapon" (and "explode weapon") deletes it at once without a break
    (message 22);
  - the attacker's own weapon on a melee hit that dealt damage (0x4B5580, combat core C21) by the target's
    material: stone 4, brick 3, wood 2, plant 1, metal 5, fire 5, glass 5 only for
    an unarmed attacker, others 0; **minus** `trunc(wear × min(|complexity|, 100) / 100)`; minus 2 for
    art subtype 4 and minus 1 for subtypes 5 and 13. Unarmed it hurts the attacker
    but never takes its last hit point (ARC-COMBAT-007, [ledger CW5–CW7](original-verification-ledger.md#combat-wear-arc-combat-007));
  - script action 40 (transform) keeps the health percentage. Items inside an
    inventory, other than worn ones, are not reached by areas.
- **Break** (0x43F1C0, item types 5..14, F2): the first time sets the destroyed
  bit on the current and inventory art and **resets the damage to 0** (a
  broken item reads as healthy), takes it off its holder (0x464C80) or drops
  it when that fails, plays a sound and runs the BUST script (30). The second
  time deletes the object with a sound and no BUST. Traps and scenery have
  their own branches.
- **What "broken" blocks**: wearing
  ([EQ1](original-verification-ledger.md#equipment-arc-world-028)); selling
  (0x462170 refuses, price 0); the schematic component search; repair, which
  needs a master. Use (0x462CC0) and item spells (0x4CB830) do not check it
  (TC9). The inventory shows the broken art through the current art.
- **Price** (0x461F80, F2): damage lowers the price only when the PC sells,
  `× current / maximum`, truncated; buying from an NPC ignores damage. Buy
  prices (0x462230, 0x4C1150) were not re-read (ledger R9 and O6 stay with
  their tasks).
- **Repair, item side**: the quote (0x578760) is `trunc(P / 2) × damage /
  maximum`, at least 2, where P is the PC's sale price, already scaled by
  `current / maximum` (TC7). A broken item is quoted only by an NPC repair
  master, with damage taken as the maximum, so the quote is `P / 2`; others
  refuse; damage 0 is "not needed". The service (0x4C8E60) runs the skill
  (0x4C7160) and then pays (0x464830) unconditionally. On the item, damage
  falls and hp_adj falls by a percentage of the maximum (a table by training,
  with its own step for a broken item); a master clears the destroyed bit on
  the current art only. The skill rule is
  [SK7](original-verification-ledger.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027),
  owned by the skills research.

## Data census

Prototypes and message files through the content stack, counts only.

| Source | Count |
| --- | --- |
| `mes\tech.mes` | 90 entries, 0..89 (arcanum3), ranges as in the init row |
| `rules\schematic.mes` | 882 entries, 2000..6076, no duplicates (arcanum4 overrides arcanum3); 118 records + 8 placeholders, all with seven lines |
| Learned | 56 records (392 entries = 56 × 7), seven per discipline; each discipline's k-th record in file order sits at `1990 + 200d + 10k` (56 of 56), so the window and the formula agree on the installed data |
| Found | 62 records, 4000..5440 |
| Placeholders | 8, 6000..6070 |
| `mes\schematic_text.mes` | 258 entries; every referenced name and description exists |
| Alternatives | always 1 or 3 per list (never 2 or more than 3): as the reader counted them: learned lists 53 × 1 and 3 × 3; found: component 1 always 1, component 2 and product 68 × 1 and 2 × 3 (the reader notes these tallies need not sum to the record counts) |
| Quantity | learned 1 ×37, 2 ×2, 3 ×11, 4 ×2, 5 ×4; found 1 ×64, 2 ×1, 3 ×4, 5 ×1 |
| Referenced prototypes | all exist; a follow-up count over all 118 records found no prototype in both component lists (TC4 latent) |
| Products | weapons, armour, food and potions, generic items, ammunition (1 learned, 3 found), one key |
| Ammunition components | 1 learned (component 1), 2 found (component 2) |
| Component complexity sign | learned: component 1 60 tech + 2 neutral, component 2 40 tech + 22 neutral; found: component 1 57 tech + 13 neutral, component 2 58 tech + 14 neutral + 2 magickal |
| Discipline mismatch | one learned record whose first product's discipline differs from the record's (no effect: a learned schematic's discipline comes from its number) |
| Written prototypes | 68 (14062..14129), 58 of subtype 5: 57 point at a found record's start without duplicates, 1 points at 0 (TC8); 5 found records have no written prototype (O3) |
| Item prototypes | 798: 270 technological, 248 magickal, 280 neutral |
| Technological by type | weapon 60, armour 52, generic 88, food 65, ammunition 3, scroll 1, key 1 |
| Technological by discipline 0..7 | 20, 24, 27, 32, 44, 42, 56, 25; complexity −100..−1, evenly spread |
| Tech spells on items | 70 slots, 65 distinct records, on 61 technological and 9 other items (6 food, 3 scrolls); charges 1 ×47, −1 ×21, 0 ×2 |
| Mage penalties | non-zero magic adjustments on technological items: armour magic AC 8 (all negative), armour magic resistance 30 (all negative), magic weight 3 (positive) |
| `rules\spelllist.mes` 140..222 | all 83 records; `Cost:` set on 146, 149, 200, 207, 208, 209, 218, 221 (not paid at BEGIN); `Maintain:` on 146, 149, 168, 187, 209, 216; `ChargesFromCells:` on 147, 168, 187, exactly the records with ChargeNBranch; `ChargeBeginCost:` on 218 |
| Tech component lines | EyeCandy 159, ObjFlag 50, Effect 50, Damage 35, Destroy 35, Heal 22, Summon 19, AGoal 8, ChargeNBranch 3; one or two each of NoOp, AIRedirect, TestNBranch, Use, Terminate, Cast, Movement, Trait |
| Cell-powered carriers | record 147 on one weapon, 168 on three armours, 187 on no item |
| Critter prototypes | 0 of 510 with a degree; no PC prototype with found schematics (NPC degrees come from auto-level schemes, not counted) |
| Item durability | every one of the 798 item prototypes has a maximum of at least 1; none has stored damage or is broken |
| DLG | 709 files: `su` 8 lines in 5 files, `ta` 88 lines in 14 files |

## Anomalies (TC1..TC10)

Verdicts as in the [ledger](original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings).
The owner rule that the Original mode never reproduces a crash, a hang or an
out-of-bounds access ([rule modes](../world/rule-modes.md#what-is-a-mode-difference))
puts TC2, TC4 and TC6 in "both fixed". The mode column was a proposal; the owner
decided every row on 2026-10-20 ([owner decisions](#owner-decisions-2026-10-20)), and the slice named implements it.

| ID | Where | Original 1.0.7.4 | Verdict | Proposal (Steamwoven / Original) |
| --- | --- | --- | --- | --- |
| TC1 | cell payment (0x450420 with 0x461590), F2 | An item source pays `cost × (1 + p / c)` cells: a strong technologist pays double, a mage the minimum; the technological adjustment branch looks swapped | Original bug | **Decided** ([owner](#owner-decisions-2026-10-20)): switch. Steamwoven `2 × cost − trunc(cost × p / c)` (a technologist pays fewer cells); Original keeps it. Reaches records 147 (one weapon) and 168 (three armours); **implemented** by ARC-TECHNOLOGY-007 |
| TC2 | action 129 (0x44A7BD, 0x460B50, 0x45FD40, 0x57C6E0), F2 | Calls an unfilled slot of an uninitialised callback structure: undefined | Original bug (crash class) | **Decided** ([owner](#owner-decisions-2026-10-20)): both fixed: open the window with (PC, PC) or (NPC, its PC leader); unused in the data; **implemented** for the PC by ARC-TECHNOLOGY-005 and for an NPC with a PC leader by ARC-TECHNOLOGY-006 |
| TC3 | assembly (0x56E720), F2 | A failed product creation returns after the components are spent and some products given | Original bug (latent) | **Decided** ([owner](#owner-decisions-2026-10-20)): both modes all or nothing (room and creation checked before consuming); no switch; TECH-4 |
| TC4 | assembly (0x56E720), second reader | Both components are found before consuming, so one item matching both lists would be consumed twice (the second time a deleted object) | Original bug (latent, use of a deleted object) | **Decided** ([owner](#owner-decisions-2026-10-20)): both fixed: a found item is reserved for one component; no installed record triggers it; TECH-4 |
| TC5 | assembly (0x56E720) | Expertise is gated only by the button state; assembly itself does not check (a multiplayer host does not re-check a client) | Original (no single-player effect) | **Decided** ([owner](#owner-decisions-2026-10-20)): both modes: assembly re-checks the gate, which changes nothing a single-player player can do; TECH-4 |
| TC6 | parse (0x56DD20) | More than three tokens write past the field and the record; a line over 2000 bytes overflows the buffer | Original bug (latent, out of bounds) | **Decided** ([owner](#owner-decisions-2026-10-20)): both fixed: strict parse refuses; the installed data never has more than three. **Implemented** by ARC-TECHNOLOGY-001 ([schematics](../formats/schematics.md)) |
| TC7 | repair quote (0x578760) | Damage counts twice (sale price already × current / maximum, then × damage / maximum): a badly damaged item is nearly free to repair, the dearest is half damaged | Original (intent unknown) | **Decided** ([owner](#owner-decisions-2026-10-20)): switch. Steamwoven quotes from the undamaged sale price; Original keeps the double count; item durability slice |
| TC8 | data: one schematic written prototype | Its schematic number is 0: reading registers a schematic 0 whose record reads all zeros and can never be built | Original (data) | **Decided** ([owner](#owner-decisions-2026-10-20)): switch, a recorded project improvement: Steamwoven refuses the registration and keeps the document; Original registers it; TECH-3 |
| TC9 | use (0x462CC0), triggers (0x4CB830) | A broken item can still be used and its item spells fire | Original (intent unknown) | **Decided** ([owner](#owner-decisions-2026-10-20)): both follow the original (broken items still usable) |
| TC10 | editor decrease (0x55F5F0, 0x55D940) → sell (0x589AF0), F2 | Selling a degree is not compared with the editor-open snapshot, unlike skills, tech skills and colleges, so degrees bought on earlier levels could be refunded; the snapshot (0x64DEEC) is read only by the tech page's draw, which in the in-game editor hides the − button at it (ledger [CP2](original-verification-ledger.md#character-point-pool-arc-world-052); the 0x64DF10 named first is a per-player copy, never read) | Original bug (if the button is reachable, O2) | **Decided** ([owner](#owner-decisions-2026-10-20)): both modes sell a degree only down to its value when the character window was opened; no switch; TECH-2 |
| TC11 | window init (0x56CD60), selection (0x56DB60), found by ARC-TECHNOLOGY-001 ([ledger](original-verification-ledger.md#schematic-data-arc-technology-001)) | The learned list has "count" (entries / 7) slots but is filled with every existing start without a bound and walked to the count; a start at 3600 or above counts past the eight-entry discipline table | Original bug (latent, out of bounds) | both fixed (owner rule of 2026-10-17): the schematic catalogue refuses such a file; the installed data has 56 starts for a count of 56 |
| TC12 | window open (0x56D130), found by ARC-TECHNOLOGY-003 ([ledger](original-verification-ledger.md#schematic-knowledge-arc-technology-003)) | A found number whose discipline is outside 0..7 is counted past the window's eight-entry table | Original bug (latent, out of bounds) | both fixed (owner rule of 2026-10-17): left out of the found tab; the installed found records all have disciplines 0..7 |

## Owner decisions (2026-10-20)

Recorded by ARC-TECHNOLOGY-001 from the owner's messages of 2026-10-20 in the project thread. They settle the proposals
of the anomaly table above under D2; each is implemented by the slice named.

- **TC3, no loss in either mode.** From the owner's play experience assembly never loses components: the product is
  always created. Both modes check room and creation before consuming anything, so a failed creation spends nothing.
  No switch (TECH-4).
- **TC1, switch.** Steamwoven: an experienced technologist pays fewer cells; an item source needs `2 × cost −
  trunc(cost × p / c)` cells (a strong technologist `cost`, a neutral holder about `1.5 × cost`, a mage `2 × cost`).
  Original keeps the executable's `cost + trunc(cost × p / c)`. Implemented with the cell source by ARC-TECHNOLOGY-007
  ([charge cells](../world/charge-cell-runtime.md), ledger [CC1–CC8](original-verification-ledger.md#charge-cells-arc-technology-007)).
- **TC7, switch.** Steamwoven: the repair quote starts from the undamaged sale price, `trunc(P_full / 2) × damage /
  maximum`, at least 2, so the cost grows with the damage and a nearly intact item is cheap. Original keeps the
  executable's double count. Implemented in the item durability slice.
- **TC10, no switch.** From the owner's play experience the original never lets a player take back a point invested
  earlier: choices can be undone only until the character window is closed. Both modes allow selling a degree only down
  to the value it had when the character window was opened (the snapshot the executable takes at 0x64DEEC when the editor opens, as for
  skills and colleges; ledger CP1). The reading's missing comparison is classified as not reachable in play by the owner's
  observation (an owner report, not a game trace; O2 is answered by it), and ARC-WORLD-052 found why: the in-game editor
  does not draw the − button at the snapshot (ledger CP2). The floor is in the shared point pool (ARC-WORLD-052); the
  degree lane is TECH-2's.
- **The other rows, as proposed** (the owner approved the remaining proposals the same day):
  - **TC2, both fixed.** Script action 129 opens the window with (PC, PC), or (NPC, its PC leader) and does nothing
    without a leader (TECH-5).
  - **TC4, both fixed.** A found item is reserved for one component, so one item never serves both (TECH-4).
  - **TC5, no switch.** Both modes re-check the expertise gate in assembly (TECH-4).
  - **TC6, both fixed.** The schematic parse refuses a list line with more than three tokens or of 2,000 bytes or more
    (this slice, [schematics](../formats/schematics.md)).
  - **TC8, switch** (a recorded project improvement). Steamwoven refuses to register a schematic document numbered 0
    and keeps the document; Original registers it (TECH-3).
  - **TC9, no switch.** Both modes follow the original: a broken item can still be used and its item spells fire.

Every TC row is decided: switches TC1, TC7, TC8; both fixed TC2, TC4, TC6 (and TC11 and TC12, found later by ARC-TECHNOLOGY-001 and -003 and fixed under the owner's no-out-of-bounds rule); no switch TC3, TC5, TC9, TC10
([rule modes](../world/rule-modes.md#registry)).

## Second reading

A fresh agent re-read, without the first verdicts, the questions the readers
counted as 13: effective expertise, raise and lower, the intelligence guard,
the learned numbers, registration, the record load, the component gate and
button state, assembly, action 129, cell payment, degree selling, the break,
the price and the repair quote, and the dialog opener. Rows the readers
report as confirmed are marked F2; the component gate stays F1 with the
button state.

- 10 agree.
- The intelligence guard (0x4B02B0) was corrected to the second reader's
  version (new effective intelligence, only on a decrease).
- The editor's decrease branch is 0x55F5F0 (not 0x55F450), per the second
  reader.
- **Disagreement**: the button state codes in 0x56DDC0. The first reader reads
  2 as "expertise lacking" and 1 as "item missing"; the second reader the
  other way round. The first reader re-checked the stack layout and keeps its
  reading. **Settled by ARC-TECHNOLOGY-004:** a third independent reading of
  0x56DDC0 and its click handler and the installed texts (message 0 names missing
  items, message 1 lacking expertise) confirm the first reader
  ([ledger SA2](original-verification-ledger.md#schematic-assembly-arc-technology-004)).
- TC4 was found by the second reader.

## Open questions

- **O1** What action 129 actually calls (stack contents): observation only;
  unused in the data.
- **O2** Whether the editor's "−" button is reachable for a degree bought on an
  earlier level (TC10): answered by the owner's play experience (not reachable;
  [owner decisions](#owner-decisions-2026-10-20)), not by a game trace; ARC-WORLD-052 read the reason, the tech page's
  draw hides the button at the editor-open degree (ledger CP2).
- **O3** The 5 found schematics without a written prototype: placed on maps or
  given by scripts (map instances and scripts were not counted).
- **O4** Answered by a follow-up count: no record lists the same prototype in
  both component lists (TC4 is latent).
- **O5** Degrees and inventories of the NPCs whose dialogs use `su`: set by
  map instances and their auto-level schemes; needs a pass over map instances.
- **O6** Buy prices (0x462230, 0x4C1150) were not re-read (ledger R9 and O6
  stay with their tasks).
- **O7** The button state codes: answered by ARC-TECHNOLOGY-004 (code 2 expertise lacking, code 1 an item missing; [ledger SA2](original-verification-ledger.md#schematic-assembly-arc-technology-004)).

## Steamwoven inputs that already exist

- [`AutoLevelRules`](../../src/Arcanum.Simulation/Character/AutoLevelRules.cs)
  already buys degrees (0x4A79C0, 0x4AFEC0 at F2) with the intelligence table
  and the cost table, and the session's advancement sheet stores
  `techDegrees` ([advancement](../world/advancement-runtime.md)).
- [`CharacterStatRules`](../../src/Arcanum.Simulation/Character/CharacterStatRules.cs)
  derives aptitude from tech points; the character sheet shows tech aptitude
  (ARC-UI-007).
- [`EquipmentRules`](../world/equipment-runtime.md) already has item power and
  the magic adjustment (EQ9) and refuses broken items (EQ1); it currently
  refuses items with item spells on wear.
- The attack resolver has the technology failure and magick cancel rolls
  ([attack resolver](../world/attack-resolver-runtime.md)).
- [`EffectRules`](../../src/Arcanum.Data/Characters/EffectRules.cs) parses
  `effect.mes`; the durable effects owner holds the hero's effects
  ([durable effects](../world/durable-effects-runtime.md)), whose tech
  discipline targets are inert until a degree owner exists.
- The session inventory (T0 `Inventory`) holds items; `MesFile` reads message
  files; the [rule modes](../world/rule-modes.md) registry takes switches.

Missing: degrees as live session state with expertise, the
found list, the window, assembly, cell payment (schematic data:
ARC-TECHNOLOGY-001, [schematics](../formats/schematics.md); item durability: ARC-WORLD-053,
[runtime](../world/item-durability-runtime.md)).

## Proposed implementation slices (project policy, not accepted)

Type `TECHNOLOGY` per the [naming policy](../workflow/task-naming.md); codes are
allocated when each task is created, after checking they are unused. Each
slice is a separate PR; all are universal (content stack, any map, any
critter), keep Data/Simulation/Session free of Godot, refuse with typed
results, and settle the TC rows they meet under D2 with a rule-mode switch
only for an original bug that gets a fix. Per owner decision 2026-10-20 (F0 facts are
re-read domain by domain when the domain is implemented), each slice also
re-reads in the executable every fact of its domain that the
[ledger](original-verification-ledger.md) or the domain documents still hold
at F0 (for example the F0 addresses of FR-ITEM-011 and FR-MAGIC-012 and the
tech parts of the magic and skills studies) and raises its level; the rows of
this document start at F1 or F2.

1. **TECH-1 Schematic data** (done: ARC-TECHNOLOGY-001, [contract](../formats/schematics.md);
   Data; FR-ITEM-017/018 data): read `tech.mes`
   texts, `rules\schematic.mes` and `schematic_text.mes` into immutable
   records (seven lines, alternatives, quantity, learned/found/placeholder
   ranges, the discipline rule of 0x56DB00), with a strict parse that refuses
   TC6 (and TC11) and a typed refusal for a missing text. The written item's
   schematic number (subtype 5) moved to TECH-3, which needs the written-item read path. Synthetic fixtures; a `RealData` test on the Mac counts
   56 learned, 62 found and 8 placeholders and checks the learned-number
   formula.
2. **TECH-2 Degrees and expertise** (done: ARC-TECHNOLOGY-002, [runtime](../world/tech-degrees-runtime.md); Simulation, Session; FR-CHAR-017/020,
   FR-MAGIC-012 rules): degree get, raise and lower with the shared
   spend/refund path of ARC-WORLD-052, effective expertise through the
   durable-effects owner (expertise effects become live), the intelligence
   guard on a base decrease, and the level-start snapshot for selling (TC10
   decided). Reuses the auto-level degree rules; no schema change if the
   degrees stay in the advancement sheet.
3. **TECH-3 Schematic knowledge** (done: ARC-TECHNOLOGY-003, [runtime](../world/schematic-knowledge-runtime.md); Session; FR-ITEM-017): learned lists from
   stored degrees, the PC's found list (append once, no limit), registration
   from reading a schematic document (its schematic number from the written item's
   start text line field, destroy on first read, messages 88/89, TC8 decided; needs a written-item read path, FR-ITEM-010); snapshot schema bump (the next free number, coordinated).
4. **TECH-4 Assembly** (done: ARC-TECHNOLOGY-004, [runtime](../world/schematic-assembly-runtime.md); Simulation pure + Session transaction; FR-ITEM-018):
   the component gate (own inventory, exact prototype, worn, broken and
   changed-description items skipped, fallback slots, expertise per component
   discipline against `−complexity`), the three button states (O7 settled),
   assembly as one all-or-nothing session change (TC3, TC4, TC5), ammunition
   units, product alternative and quantity into the crafter's inventory, an NPC
   crafter for the dialog path (its degrees and inventory come from map
   instances, O5).
5. **TECH-5 Schematic window** (done for the PC: ARC-TECHNOLOGY-005,
   [runtime](../world/schematic-window-runtime.md); viewer, UI lane; FR-MAGIC-012, FR-ITEM-017/018
   integration): key `T` with the sheet's guard, learned and
   found tabs, discipline tabs, pages, button texts and hover text, sounds
   3008/3009/3010/3011/3018 + discipline, and script action 129 for the PC (TC2). The NPC
   crafter (the dialog action `su` and action 129 for an NPC with a PC leader) is done by
   ARC-TECHNOLOGY-006 over the NPC inventory store (ARC-WORLD-055), its degrees from the NPC's
   map data ([ledger NC1–NC8](original-verification-ledger.md#npc-schematic-crafter-arc-technology-006)).
   No launcher change (no new argument).
6. **TECH-6 Charge cells and tech devices** (Simulation, with the magic
   engine; FR-MAGIC-010/011): the cell source behind the engine's payment port
   (critter `cost`, item source TC1 decided, ammunition type 2; **done** as rules and a
   session port by ARC-TECHNOLOGY-007, [runtime](../world/charge-cell-runtime.md); the
   consumption in session state waits for session ammunition, ARC-WORLD-066), the list of
   tech items and records that reach the engine (70 item spell slots, records
   147, 168 and 187; **done** with the MAINTAIN test and the device rule by ARC-TECHNOLOGY-008,
   [runtime](../world/tech-devices-runtime.md)), a test that the engine's MAINTAIN charges maintained
   tech records (the stage itself stays with the magic slices), and the rule that
   tech items are not refused to mages outside combat (the wear refusal of
   item-spell items is lifted with the shared item trigger slice, which the
   magic research owns).

**Item durability** (FR-ITEM-011; done: ARC-WORLD-053, [runtime](../world/item-durability-runtime.md), ledger DU1–DU8) is one more slice, owned by this research and
needed by ARC-SKILLS-011 (Repair). Its type is `WORLD` (item state in the
session runtime; the next free `WORLD` number at task creation): hp fields per item in the session (prototype values with
instance overrides), maximum and current, the damage setter, the two-step
break (destroyed bit, damage reset, unwield or drop, BUST hook, deletion),
what broken blocks (wear, sale, schematic use; TC9 kept), the sale price
scaling and the repair quote (TC7) as pure rules for the trade and repair
owners, and the damage entry points that already exist (armour and weapon wear
from the attack resolver). Snapshot schema bump. Combat damage to scenery and
area damage to items join when those owners exist.

Order and dependencies: TECH-1 → TECH-2 → TECH-3 → durability → TECH-4 →
TECH-5; TECH-6 after the magic engine's payment step and the shared item
trigger slice. TECH-2 needs ARC-WORLD-052 (spend/refund); TECH-4 needs the
inventory owner and durability (the broken skip); TECH-3 needs a written-item read path
(FR-ITEM-010); TECH-5 needs the dialog port for `su`, and the NPC crafter path
needs NPC degrees and inventories from map instances (O5).

## Method

Read on 2026-10-20 on the owner's Mac by a Remote Control session with Xcode's
`llvm-objdump`: the tech module (0x4AFCD0–0x4B0340), the Schematic-UI module
(0x56CD60–0x56EA90), the editor's degree wrappers and decrease branch
(0x589A20, 0x589AF0, 0x55F5F0, 0x55D940), the item power and gate functions,
use and the item trigger executor, the payment function's cell branch,
ChargeNBranch, the BEGIN and MAINTAIN stages, the SCR case of action 129 and
its callback chain, the dialog outcome path of `su` and the dialog conditions
`ta`, `ma`, `sk`, the durability getters and setter, the combat damage step on
objects, weapon wear, the break function, the sale check, the price and the
repair quote and service. Data were counted with the repository's probes. No
bytes, listings, decompiled code or game text left the scratchpad; nothing in
the install was changed.
