# Original verification ledger: Items and inventory

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Equipment (ARC-WORLD-028)

Source: [equipment runtime](../../world/equipment-runtime.md) and the item requirements
[FR-ITEM-005..007](../../requirements/items-economy.md#fr-item-005). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below, and both found EQ1.
EQ7 was read the same by both; the second reader adds that `item_is_identified` answers true for complexity ≤ 0, so the earlier reading's
differently written gate has the same effect. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| EQ1 | `sub_464D20` (0x464D20) | The wear check, in order: a critter spell flag of polymorph or a "body of" form → not wieldable; slot 1002 read as 1001; complexity > 0 with background 69 (Magick Allergy) → no magick items; the destroyed bit of the item's current art → broken; `item_location_get` ≠ slot → wrong type. Weapon: two-handed with a fixed hand count → no free hand with a shield, crippled with either arm flag; other weapons crippled only with both arms; then the critter art with the weapon's subtype must exist. Shield: no free hand next to a fixed two-handed weapon; **crippled with either arm flag**; art only for an armour shield. Body armour: the race's size bit, male-only before female-only, unique NPC art, then the art. No minimum-strength test. | Original, except the shield: the earlier reading tested only "both arms crippled", a reconstruction slip; both settings refuse a shield with one crippled arm |
| EQ2 | `sub_465010` (0x465010) | Always allows (the hexed-item refusal is never returned). | Original |
| EQ3 | `item_check_remove` (0x466DA0) | The no-drop item flag (0x20) refuses first; a worn item then asks EQ2. | Original |
| EQ4 | `sub_464C80` (0x464C80) | Taking a worn item off: remove check, insert check into the owner, remove, insert at the free grid place; a non-worn item: true, unchanged. | Original |
| EQ5 | `item_wield_set` (0x464AE0) | Same item in the slot: true. Then the wear check, the item's remove check, the occupant's remove check, both removed, the occupant's insert check (on failure both go back where they were), the occupant into the grid, the item into the slot. Last, for slot 1003 (medallion) only, the animation speed is recalculated **for the item**, not the wearer. | Original; the medallion recalculation is a bug of the original (an item has no running animation, so it does nothing), no session effect, no switch |
| EQ6 | `item_location_get` (0x4655D0), `item_armor_coverage` (0x465C30), `tig_art_item_id_armor_coverage_get` (0x504570) | Armour by the coverage bits 14..16 of its inventory art (torso 1006, shield 1005, helmet 1000, gauntlets 1007, boots 1008, ring 1001, medallion 1003), a weapon 1004, a generic item with the torch/shield flag 1005, else −1; an art that is not item art reads coverage 0. | Original |
| EQ7 | `item_armor_ac_adj` (0x465BA0) | Not armour: 0. AC adjustment, plus the magic AC adjustment (scaled with an owner) unless the "unidentified" argument is set for an unidentified magickal item. Combat passes 0, so the magic part always counts. | Original |
| EQ8 | `object_get_resistance` (0x43D6D0) | Base, then for a critter each worn armour item's adjustment and scaled magic adjustment (same gate), the poison bonus `5 × (CN − 4)` when positive, effects; a monstrous NPC unclamped, else 0..95. | Original |
| EQ9 | `item_effective_power` (0x4614A0), `item_effective_power_ratio` (0x461540), `item_adjust_magic` (0x461590) | Power `(complexity + aptitude) / 2` with signed truncation, clamped to 0..complexity on the item's side (a non-critter owner gets the complexity); ratio 100 for a neutral item, else `100 × power / complexity`; adjustment `value × power / complexity` (magickal), `value − value × power / complexity` (technological), unchanged (neutral). | Original |
| EQ10 | `item_weapon_magic_speed` (0x465CF0) | No item 10; the magic speed adjustment scaled for an owner, + 5 at apprentice training in the weapon's skill, + the speed factor. | Original (as TB10) |
| EQ11 | `item_weapon_range` (0x465E30), `item_weapon_min_strength` (0x465E90) | Not a weapon: 1. Range + scaled magic range. Minimum strength + magic adjustment (scaled with a critter); without the fixed hand count, two-handed with a shield +4, one-handed without one −2. | Original |
| EQ12 | `item_recalc_light` (0x467E80) | The worn items' light flags, each shifted by its effective power ratio (≤ 25 by 3, ≤ 50 by 2, ≤ 75 by 1), are ORed; the critter keeps **only the largest** size flag. | Reconstruction slip (the earlier reading set every size flag); the light drawn is the same (the largest wins in both) |
| EQ13 | `item_weapon_damage` (0x465F70), unarmed part | Bare-handed melee reads the unarmed bonus of whatever item is in slot 1007, without a type test; normal and fatigue `max(1, bonus − 25) .. bonus + 5`, other types 0; a monstrous critter its own table. | Original |
| EQ14 | race to armour size table (0x5B32C0) | Human, elf, half-elf, half-orc, dark elf, orc medium; dwarf, gnome, halfling small; half-ogre, ogre large. | Original |

## Item durability (ARC-WORLD-053)

Source: the [item durability runtime](../../world/item-durability-runtime.md) (the item durability slice of the
[technology research](../technology-evidence.md#item-durability-fr-item-011)) and the [Technology](technology.md#technology-arc-res-062) rows
above. Re-read on 2026-10-20 for this slice by a fresh agent that saw only the questions (method step 6): the maximum, the
current hit points, the damage setter, the break, the price, the sale check, the repair quote and the item side of the repair
service. Every direct branch of these functions stays inside `.text`. Both readers agree on every point the first reading
recorded; the second reader added the details below. The second reader disclosed that one line of the technology study (the
price is scaled only when the PC sells) reached it after it had read 0x461F80; its reading of that function rests on the
listing, and the row was F2 before. The rows are F2.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| DU1 | maximum (0x43D5A0) | A non-critter: hp_pts (27) + hp_adj (28), no effects, no clamp. (A critter adds level, strength and willpower and its effects: `LifeRules.MaxHitPoints`.) | Original |
| DU2 | current (0x43D600) | Maximum − hp_damage (29), no clamp. | Original |
| DU3 | damage setter (0x43D530) | A network client writes nothing; otherwise a negative value is stored as 0 through the synced setter; a critter with damage above 0 starts its rest timer; the change is notified. It never breaks the object. | Original |
| DU4 | break (0x43F1C0) | An object with object flag 0x1 returns at once. Items (types 5..14): when bit 0x400 of the current art is clear, set it on the current and the inventory art, set the damage to 0, take the item off its holder through the unwield (0x464C80, which moves only items in wear slots) and drop it when that fails, play the bust sound and run script point 30 (BUST) with the breaker as triggerer; when the bit is set, delete the object with the destroy sound, no script. Walls, portals, containers, scenery and traps have their own branches. | Original |
| DU5 | price (0x461F80) | When the first party (the seller) is a PC: worth (300 unidentified, else at least 2), the buyer's multiplier, the haggle share, then, when current < maximum, `current × price / maximum`; a negative price is 0 and exactly 1 is 2 (these bounds belong to this branch; the buy branch has its own). Signed 32 bits, truncating. The buy branch does not read damage. | Original |
| DU6 | sale check (0x462170) | Refuses a worth of 0, a broken item (current art bit 0x400), an item the buyer does not deal in or whose buy script refuses below expert haggle, and an item with item flag 0x8000 that the buyer's NPC flags do not admit; a refused item's price is 0. | Original |
| DU7 | repair quote (0x578760) | A broken item: only a repairer with master training in Repair, with the damage taken as the maximum, else a refusal line; damage 0: a "nothing to repair" line; otherwise `trunc(P / 2) × damage / maximum`, at least 2, where P is the PC's damage-scaled sale price to the repairer (TC7). | Original (TC7: a switch, owner decision 2026-10-20) |
| DU8 | repair service, item part (0x4C8E60) | The skill run is forced to succeed and the payment follows unconditionally. Nothing changes for an item without damage unless it is broken and the repairer a master. The amount is the maximum with the PC's fate full repair, else `maximum × effectiveness / 100`, at least 1; damage `max(0, damage − amount)`; hp_adj `− trunc(maximum × pct / 100)` with the maximum before the repair and no clamp; pct by the repairer's training 10, 5, 1, 0 (none, apprentice, expert, master) and 5 for a broken item. A master clears bit 0x400 of the current art only; the inventory art keeps it. | Original (corrected by RP2: the 5 % is a master's only; the item part is the run's case) |

## NPC inventory changes (ARC-WORLD-055)

Source: the [inventory runtime](../../world/inventory-runtime.md#npc-inventory-changes-arc-world-055). Read on 2026-10-21 for this slice by
two agents that saw only the questions (method step 6), independently: how the original keeps an NPC's inventory changes across
map leave and return, how items created into an NPC and original items that left its list are kept, and how a follower's
inventory moves on a cross-map teleport. Every direct branch read stays inside `.text`. The two readings agree on NI1–NI5 (F2;
the second reader saw one grep line of the companion study's C16 summary, so NI4 is not perfectly blind); NI6 and NI7 were seen by
one reader or at medium confidence (F1).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| NI1 | map open in game (0x40FC70), map flush (0x410070, 0x410720, 0x410830, 0x410B20, 0x410C50), map close (0x4106D0, 0x411520) | Before the next map loads, every non-static object's handles become object IDs; an original object (dynamic bit clear) that was modified is written as its changed fields to the map's `mobile.md` (rewritten), or as its bare ID to `mobile.des` (append-only) when destroyed; every dynamic object that is not destroyed is written whole to `mobile.mdy` (rewritten); then every object is freed. An NPC's inventory is fields of the NPC (a count and a list of references), each item its own object with its parent field, so a changed list is stored whole as one changed field. Nothing of the map survives except through these files. | Original |
| NI2 | map open (0x40F4E0, 0x410D10, 0x411450, 0x410C50), ID lookup 0x4E50E0, rebinding 0x406520 / 0x40BDB0 / 0x40BE70 / 0x40BFC0 | The map's original objects are read, then `mobile.md` differences applied, then every `mobile.des` ID freed, then `map.patch` (no inventory attribute), then `mobile.mdy` read; only then every reference is resolved back to a handle: an ID not found becomes a null list element and the count is left unchanged. | Original |
| NI3 | creation 0x4ED8B0 → 0x43CBA0 → 0x442130 → 0x442260, transfer 0x4617F0 / insert 0x466640, destroy 0x43CCA0 | An object created in play is dynamic: an item created into an NPC is saved whole in `mobile.mdy` with its parent the NPC, and the NPC's changed list goes to `mobile.md` (or into its own record when it is dynamic); a destroyed dynamic item is not written and is gone. A destroyed original item leaves its holder's list and its ID goes to `mobile.des`, so it never comes back from the map data; an original item moved within the map stays original with its changed parent and location. | Original |
| NI4 | teleport 0x4D3460, scheduler 0x4D3760, payload 0x4D39A0 (0x4D3E20, 0x4D3D60, 0x4064B0, 0x406590, 0x406520, 0x4D3E80) | The PC's group is scheduled recursively: its followers (not those with NPC flag 0x8, waiting) and, for every PC, NPC or container, every item of its list (a count that differs from the list stops it). For another map, all scheduled objects are converted to IDs before any is written, so a follower's list keeps its items' IDs and each item's parent keeps the follower's ID; each is then appended whole to the destination's `mobile.mdy` as dynamic and the live copy marked destroyed (an original one's ID later goes to the source's `mobile.des`). A follower carries its inventory and is that map's object from then on. | Original |
| NI5 | Player save/load (0x40D760, 0x40D7C0), the save modules (table 0x59A6A0) | No NPC inventory state is kept outside the per-map files: the player part saves only the PC's ID; the PC, its inventory and the followers that travelled with it are dynamic objects of the current map's `mobile.mdy`. | Original |
| NI6 | barter close (around 0x5733A9), 0x4640C0, 0x464180 → 0x463C60 → 0x463E20 | Closing a barter window schedules a time event (type 0x16, 12–24 game hours) that regenerates the partner's inventory from its inventory source, for a living NPC with no leader (followers are exempt): the inventory is cleared (items without the persistent item flag destroyed) and refilled. A medium-confidence reading; whether NPCs of the original map data are ever restocked was not settled. | Original (F1; barter not modelled) |
| NI7 | teleport preparation 0x4D3E20 / 0x4D3D60, insert 0x466640 | A teleport clears an NPC's substitute-inventory link; an insert into an NPC sets NPC flag 0x40, after which the AI re-equips its best items. One reader. | Original (F1; not modelled) |

## Placed items (ARC-WORLD-059)

Source: the [inventory runtime](../../world/inventory-runtime.md#placed-items-arc-world-059). Question: what happens to an item a PC
drops, while it lies on the ground, and when it is picked up again? Read on 2026-10-21 by this task and, independently, by a fresh
agent that saw only the questions and the project's records of the callees (method step 6). Both agree on every row; the rows are F2
except where a row says otherwise. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PI1 | drop (0x461A90, 0x461AB0) | A distance 2..4 is drawn first (0x461A70 drops on the owner's tile, distance 0); a network client only sends a request. Then, in order: the owner; the item's drop script (point 5, the owner as triggerer) may refuse; the remove check 0x466DA0 refuses an item with the no-drop flag (item flag 0x20; the worn-slot check 0x465010 always allows), and for a PC the reason is shown to the local player (0x4673F0); the tile (CR14); the removal 0x466E00 (PI2) and the placement 0x466E50 (PI3). Only an NPC owner then gets its look-for-weapon or look-for-armour flag. | Original |
| PI2 | removal (0x466E00, 0x467860, 0x467CB0) | The item's running magictech ends (0x457450); it leaves its owner's list and its inventory location becomes −1. From a worn slot 1000..1008: the owner's art follows (weapon, shield, body armour), the item's spells for unwielding fire, the owner's light is recalculated from the items still worn (0x467E80), then the item's wield-off script (point 25). For any slot: the spells for removal fire, and last the item's remove-item script (point 28). | Original |
| PI3 | placement (0x466E50, 0x466EF0, 0x4415C0) | A junk pile (description 3023) on the tile with room takes the item. Else, when loose items lie there, a new junk pile is created from prototype 3023, every loose item is moved into it with the ordinary pickup (scripts and insert check included) and then the dropped item. Else the item is moved to the tile. Every path ends with the decay timer (PI4). A junk pile left with one item is dissolved and the item placed again (0x463540, a new timer). | Original |
| PI4 | decay (0x468090, 0x468010, 0x467FC0) | An item that lies loose or in a junk pile, without item flag 0x20000, gets an Item Decay event (kind 25) 172,800,000 ms (48 hours) on; an earlier one of the item is cancelled first. When it fires, a container open in the loot window delays it by 60,000 ms; otherwise an item still decayable is destroyed (0x43CCA0). A pickup cancels the event. The global that disables decay (0x5E87E4, set at item init) and the event's clock column (the one Poison and Resting use) were not fully determined. | Original (the disabling global and the clock settled by [ID1, ID5](#item-decay-arc-world-062)) |
| PI5 | PC re-wield (0x4B83E0, 0x4B8330) | After a combat drop or loss of its weapon a PC wields the best item for slot 1004 (0x465170) when the integer setting "auto switch" is non-zero (single player; in multiplayer the player's option bit 0x400). | Original |
| PI6 | pickup (0x4617F0, 0x461810) | A loose item: the item's get script (point 4) may refuse, the insert check 0x466510 may refuse (a PC sees the reason), then the item leaves the tile, enters the inventory and its decay event is cancelled. A dropped item keeps no mark of having been dropped. | Original |

No anomaly: the session's differences are NOT READY in the [inventory runtime](../../world/inventory-runtime.md#placed-items-arc-world-059)
(decay, junk piles, the removal's scripts and spells, the PC's re-wield); the tile and its draws are ARC-COMBAT-010's
([drop cell](combat.md#drop-cell-arc-combat-010)).

## Freed cells (ARC-WORLD-065)

Source: the [object damage runtime](../../world/object-damage-runtime.md) and DC5/DC7. Question: does a container or scenery the
session destroyed or switched off still block a step onto its tile? Read on 2026-10-21 by this task and, independently, by a fresh
agent that saw only the questions (method step 6). Both agree on every row; the rows are F2. Every direct branch stays inside
`.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| FC1 | step objects (0x43FD70, 0x43FDC0, 0x43FE00) | The step's object test lists the objects through 0x4407C0: walls and portals (mask 0x3) on the edge of the tile left, then walls, portals, containers, scenery, projectiles, PCs, NPCs and traps (mask 0x3801F) on the tile stepped onto, and skips an object with the no-block flag 0x400 itself. | Original |
| FC2 | tile objects (0x4407C0) and its filter (global 0x5E2F88) | Every object whose object flags share a bit with the global filter is skipped, before its type is matched. The object module's init sets the filter to 3 (destroyed 0x1, off 0x2) unless the editor flag is set (then 0); the game's startup passes the editor flag 0, and the filter toggle (0x43AAB0) is reached by nothing in the executable. So in the game destroyed and switched-off objects are never listed for a step. | Original |
| FC3 | tile blocking (0x4D7110) | The sector's block mask, a facade tile's bit or the tile name's blocking flag; no object is read, so a removed object leaves its tile's own blocking as it was. | Original |

No anomaly: Steamwoven follows the original (the live overlay moves the removed objects' T0 records for the PC and the companions);
a respawning scenery blocks again once it is back, as its off flag clears.

## Ammunition (ARC-WORLD-066)

Source: the [inventory runtime](../../world/inventory-runtime.md#ammunition-arc-world-066). Question: how does the original insert an
ammunition item into a holder (merge, stack field, limits), where it keeps the unit count and type, how a use spends units and
removes an emptied stack, what other removals do to the critter's stack field, and whether gold works the same way? Read on
2026-10-21 by two fresh agents that saw only the questions (method step 6), independently; they agree on every row, so the rows
are F2. Every direct branch stays inside `.text`. Prototype numbers below are the project's (the executable's basic number + 20).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| AM1 | insert check (0x466510), insert (0x466640), same-prototype finder (0x462930) | For a critter the weight is checked first (an ammunition item weighs `weight × quantity / 4`, gold nothing); then an item of gold or ammunition whose prototype another item of the holder's list shares needs no free slot. The insert adds the inserted item's quantity to that stack with a plain 32-bit add (no cap), fires the insert-item event, moves the inserted object to the holder's location and destroys it (0x43CCA0). The merge target is the first other item of the holder's list with the same prototype, not the critter's stack field; prototype 15085 never merges. Containers merge the same way. Pickup, loot and moves between inventories all insert this way. | Original |
| AM2 | insert (0x466640), field map (0x462410) | Without a stack to merge into, a critter's stack field of the item's kind is set to the new item (gold 0xE8, ammunition 0xE9 + type) and the item is inserted as any other; an earlier handle in the field is overwritten. | Original |
| AM3 | fields, tables (0x5B32A0, 0x5B32B0) | The unit count is field 0x8E (gold 0xA5) and the ammunition type field 0x8F, signed 32-bit, set without clamping (0x4EFDD0). Types 0..3 index a four-entry table of critter fields and one of base prototypes (7058..7061; each holds 10 units); no bounds check. A count of 0 still merges and inserts; a use of 0 succeeds at once. | Original (AMA2, AMA3) |
| AM4 | unit use (0x4658E0), stack lookup (0x465870) | Arguments: source, destination (or none), count, type, an optional explicit stack. The source stack is the explicit one, else a critter's stack field (unvalidated) or a container's lookup (AM6). Fewer units than the count: nothing changes, 0 returned. Exactly the count: the stack is destroyed; more: it keeps the rest. A destination's stack grows by the count, or a new object of the type's base prototype carries it in (0x465A40, 0x4617F0). A source without any stack credits the destination all the same. Callers: the attack (0x4B65D0: the weapon's ammunition type, field 0x7D, and per-shot count 0x7E; a normal shot, a critical miss taking the whole stack or five shots' worth; an NPC whose leader chain reaches no PC never spends), the cell payment (CC5), script action 135 (cells), assembly (one unit a component), a steal (at most 20 units, gold 100) and the quantity interface. | Original (AMA6) |
| AM5 | removal (0x467860, from 0x466E00, 0x43CCA0, the drop and loot) | Every removal of gold or ammunition from a critter writes a null handle into its stack field of that kind, whether or not the field named the removed item, and never re-points it to another stack of the type. | Original (AMA4) |
| AM6 | container lookups (0x465870, 0x4647D0) | A container's stack of a type is looked up by passing the raw base prototype number where a prototype handle is compared (every other caller converts the number first), so it never matches: a container's ammunition or gold is never counted or spent this way. | Original bug (AMA5; Steamwoven switch, ARC-TECHNOLOGY-009) |
| AM7 | gold (0x4647D0, 0x464780, 0x464830, 0x464970) | The same mechanism: critter field 0xE8, quantity 0xA5, base prototype 9056 (written 9076 here first; corrected by NT1), merge by prototype. | Original (gold modelled since ARC-WORLD-071, NT1) |

Anomalies:

- AMA1 (AM1, AM4): the merge sum, a destination's add and the weight product wrap past 32 bits. Crash class: both settings refuse a
  merge, an assembly or a received stack whose sum does not fit (`BudgetExceeded`).
- AMA2 (AM3, AM4): a negative count is accepted (a use with one grows the source). Both settings refuse a negative spend
  (`InvalidAmmo`) and a seed item with a negative count.
- AMA3 (AM3): a type outside 0..3 indexes past the tables. Crash class: both settings refuse such a seed item (the seed is refused)
  and a spend of such a type.
- AMA4 (AM5): a removal clears the critter's field while another stack of the type stays, so that stack is not counted until a
  later new stack sets the field again. Latent: each installed ammunition type has one prototype, so a holder never has two
  stacks of a type. The session spends from the holder's first stack of the type in both settings. **Decided** (owner, 2026-10-21):
  no switch, as for TC4 and TC6 (latent, unreachable in the installed data).
- AMA5 (AM6): a container's ammunition (and gold) is never found by the type lookups, so a device in a container has no cells.
  **Decided** (owner, 2026-10-21): a switch, `ItemRuleProfile.ContainerStackLookupFails` (ARC-TECHNOLOGY-009): Steamwoven finds the
  container's first stack of the type (a device in a container pays from its cells through `ContainerCellSource`, a spend from a
  container through `SpendAmmo`); Original keeps the failed lookup. Gold is not modelled; the same fix will apply to it.
- AMA6 (AM4, NT1): a source without a stack (or, for gold, a pile) still credits the destination. Since ARC-WORLD-071 the session
  moves units and coins between the hero and an NPC from a named ammunition stack or the NPC's first gold pile, and a source without one
  is refused in both settings (`ItemNotAvailable` for ammunition, `GoldShort` for gold): nothing is credited from nothing. Unreachable
  from the planned callers (a steal moves from the dragged pile or stack, PP3; a sale's price is capped at the merchant's gold, NT4).
  **Proposed** (ARC-WORLD-071): no switch while no caller reaches it; to be decided by the owner if one does.

## Item decay (ARC-WORLD-062)

Source: the [inventory runtime](../../world/inventory-runtime.md#item-decay-arc-world-062). Question: when does the original destroy an
item lying on the ground, what keeps it, what delays it, and which clock and saving rules its event follows? It settles the two
points PI4 left at one reading (the disabling global and the clock). Read on 2026-10-21 by this task and, independently, by a fresh
agent that saw only the questions (method step 6); both agree on every row, so the rows are F2. ID9 was read the same way on
2026-10-27. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| ID1 | time-event type table (0x5B2188, entries of 0x2C bytes: name, saved flag, parameter types, clock, handler, remove callback, save filter) | Kind 25, "Item Decay": saved, parameters an object (the item) and an integer (the game time in seconds when scheduled, never read), clock 1 (game time), handler 0x467FC0, no callback or filter; saved and on the game clock like Poison (6), Resting (7) and Lock (21). | Original (settles PI4's clock) |
| ID2 | decays (0x468010) | No object: no. The global 0x5E87E4 set: no. Item flag 0x20000 (field 0x57, the field of the no-drop flag): no. The item's parent (0x4612A0): none, yes; a parent whose description (0x49B290) is 3023 (a junk pile), yes; any other parent, no. | Original |
| ID3 | firing (0x467FC0) | While the counter 0x5E8800 is 0 or below, the event is scheduled again 60,000 ms on (0x468090, which tests ID2 again); otherwise an item that still decays is destroyed (0x43CCA0). | Original |
| ID4 | schedule (0x468090), cancel (0x468110) and their callers | The schedule tests ID2; when the item decays it removes the item's pending kind-25 events and adds one at the game clock plus the delay. The cancel removes them when the item no longer decays. The drop's placement (0x466E50) schedules 172,800,000 ms (2 game days); the pickup (0x461810) cancels after the insert; the item transfer into another holder cancels and then schedules 2 days again (so a move into a junk pile restarts the time, any other holder ends it); loose items swept into a new junk pile keep their time. The plain ground placement (0x4415C0, a drop with its argument 0) schedules nothing. | Original (confirms PI4, PI6) |
| ID5 | item module start (0x460E70), game start (0x4020F0), WinMain (0x4012E7) | 0x5E87E4 takes the first field of the start-up options every module start gets; the game start reads it as the editor flag (non-zero: the world editor's configuration) and WinMain sets it to 0, so in the game decay is always on. | Original (settles PI4's global) |
| ID6 | 0x5E8800 (0x460E70, 0x468180, 0x468190, 0x4681A0; window open 0x5727B0, close 0x573440) | Set to 1 at the item module's start, not saved; lowered when the inventory window opens in mode 2 (loot) or mode 4 (a second container view whose use was not identified) on a container (object type 2), raised when it closes. While it is 0 every decay event that fires waits a minute; a dead critter opened for loot does not count. | Original (mode 4's use unknown) |
| ID7 | container break (0x43F1C0 → 0x463730), drop (0x461A90, 0x461AB0) | A broken container drops each item through the drop's placement and an NPC's or PC's drop goes there too, so each such item gets its decay event (OD13, PI3). | Original |
| ID8 | event queue (ledger Q6), map close (0x45C2F0) and open (0x45C610) | A firing whose item was destroyed or can no longer be found is dropped without running. Closing a map moves its events, with their absolute game time, into the map's save; reopening loads them back, so an overdue decay fires right after the return. A dead body's decay (kind 24) and the destruction of a holder also drop items through the placement, so they decay too (not modelled: no body decay or holder destruction drops yet). | Original |
| ID9 | object creation (0x4ED8B0), transfer refused by the insert check (0x4617F0 → 0x461810, 0x466510) | Neither reaches the schedule 0x468090: its only direct callers are the item transfer into a holder (0x461CA0), the drop's placement (0x466E50) and the firing's retry (0x467FC0). Creation lays the object on the tile without the drop; a refused transfer shows its message and returns before the removal from the tile, the insert and the cancel. So an assembly product the crafter's inventory refuses (SA3–SA5, NC7) lies on the tile with no Item Decay event until something moves it. Read on 2026-10-27 by this task and, independently, by a fresh agent that saw only the questions; neither followed the indirect calls of the placement (redraw and sector hooks) or what the item's get script may do. | Original |

No anomaly. The session's limits (no junk pile yet, no container in the viewer's loot window, a bound on pending events, the no-decay
flag kept only on seed rows) are in the
[inventory runtime](../../world/inventory-runtime.md#item-decay-arc-world-062).

## NPC gold and transfers (ARC-WORLD-071)

Source: [inventory runtime](../../world/inventory-runtime.md#npc-gold-and-transfers-arc-world-071). Read on 2026-10-21 by two independent
readers that saw only the questions (method step 6); they agree on every row below unless a row says otherwise. The insertion rules are
AM1–AM5; this section adds the gold units, the move of an item between critters and what the barter window calls. Both readers found
that the gold base prototype is 9056, not the 9076 of AM7 (a slip of that row; the repository's other gold numbers use 9056).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| NT1 | gold units 0x464830 (source, destination, count, explicit pile or none) | A count of 0 succeeds and changes nothing. A source pile with fewer coins changes nothing and returns 0; exactly the count destroys the pile (0x43CCA0, which nulls field 0xE8); more leaves the rest. With a destination, the insert-item event (21) fires first on the source pile with the destination as triggerer; it cannot refuse. A destination pile (field 0xE8) grows by a plain 32-bit add: no cap, no insert check, no event, no NPC flag. Without one, 0x464970 creates a pile of prototype 9056 at the destination's tile and 0x4617F0 moves it in (its events and insert check); a refused insert leaves the coins on the ground while the call still reports success. A source without a pile credits the destination all the same. The PC's gold is a pile of its list named by field 0xE8, as an NPC's. | Original |
| NT2 | item move 0x4617F0 → 0x461CA0 | In order: the new parent's transfer script (point 32; 0 refuses); the item's no-drop flag 0x20 refuses (a worn item is always removable, 0x465010); the insert check 0x466510 refuses on weight, a PC-only condition (code 15, the technophobia rule of ARC-WORLD-069) or no free cell, while a same-prototype gold or ammunition stack needs no cell. Any refusal leaves the item with its old parent. | Original |
| NT3 | removal 0x467860, insert 0x466640 | The removal takes the item out of the list, nulls the stack field of its kind (AM5), unwields a worn item (wield-off event 25), sets NPC flag 0x40 on an NPC giver and fires remove-item (28). The insert merges a gold or ammunition item into the holder's first item of its prototype (the moved object destroyed) or appends it at the end of the list in the checked cell, sets the stack field when there was no stack, sets NPC flag 0x40 on an NPC receiver and fires insert-item (21). The item's decay timer is cancelled. Flag 0x40 makes the AI re-wield its best items (0x4654F0, around 0x4A8570) and is then cleared. No other state (no separate gold count, no "given by the PC" mark). | Original; flag 0x40 F1 (not modelled) |
| NT4 | the barter window | Items move through 0x461810 (the core of 0x4617F0), the price through 0x464830 after the item and only when its move succeeded. Buying is refused for a price of 0 or a PC with less gold; a sale's price is capped at the merchant's gold (a merchant without gold takes the item for nothing). A sold item goes into the merchant's substitute container (field 0x126, used within 20 for an NPC with no leader) and gets the identified flag 0x1; the merchant's gold in that container is moved onto the merchant when the window opens. A failed payment rolls back only gold or ammunition. One reader also reads a gift-reaction call (0x4C11D0 through 0x4C0DE0) whose positive branch looks gated on the love reaction. Where the restock event (NI6) is scheduled is read differently: at every opening of the inventory window (0x5733A9 inside 0x5727B0, one reader) or at the close (the other, inferred). | Original; the barter window's (ARC-UI-050), the gift reaction and NI6's point to re-read there |
