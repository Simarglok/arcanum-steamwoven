# Crash Site items and inventory at T0

## Status, scope and evidence discipline

ARC-RES-023 answers, for the nine-sector Crash Site core only, four questions
behind FR-ITEM-001/003/004/008: who holds which item when a new game starts, how
the player can obtain those items, how an item's display name is chosen, and what
the dialog `IN` operation touches. It is **research, not an inventory runtime**.
No `src/**`, viewer, `Arcanum.Session` or launcher file changes. It builds on, and
does not repeat, the [negative IN contract](../scripting/negative-inventory-transfer-contract.md)
(ARC-RES-021) and the [area inventory](crash-site-area-inventory.md) (ARC-RES-015).

Categories, as in RES-021:

- **Source (S):** read branches of `alexbatalov/arcanum-ce` at
  `a7ff41b300ef712f0e7d088183a3d08957110cdd` (Sustainable Use License 1.0, not MIT),
  not original-executable parity. No CE code or structure is copied.
- **Data (D):** the authenticated normal-game Crash Site aggregate and archives,
  read by the probe below. T0 means the serialized state before any tick, script,
  AI step or player action, the same T0 as the [passability closure](area-passability-closure.md).
- **Proposal (P):** project recommendations for a later implementation; not
  accepted gameplay policy.
- **Unknown (U):** a named gap with its next check.

No original text, item bytes or private paths are published. Items and NPCs are
identified by aggregate source index, prototype and description numbers. Virgil
is named because earlier research already identified him.

## Inputs and reproduction

Inputs are the six pinned archives of the [cave research](cave-transition-evidence.md)
(module, resource1..4, PATCH0), the normal-game exterior aggregate
(`a548f611…de6a5`, 1,823,280 bytes) and the prototype directory. The probe opens
the strict Area snapshot through `AreaOriginalValidator.OpenFiles`, re-hashes
every mobile record and its own object ID inside the aggregate, and compares all
archive and aggregate fingerprints before and after the run.

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --item-inventory-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --item-inventory \
  --prototype-root "$GAME/data/proto" --mobile "$GAME/modules/Arcanum/maps/<exterior aggregate>" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE/items.json"
```

The report holds only numbers, booleans and SHA-256 digests. MES and DLG text is
parsed in memory and only hashed. Effective fields come from the Area snapshot,
so absent instance fields inherit the authenticated prototype value.

| Resource | Archive hits | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `mes/description.mes` | resource3 only | 39,124 | `d019b4ec…471dc91` |
| `mes/gamedesc.mes` | module only | 34,179 | `8254286b…f68e60` |
| `mes/gamekey.mes` | module only | 3,258 | `21fce72d…0efb35` |
| `rules/invensource.mes` | resource3 **and** resource4 | 35,008 / 35,149 | `fc7936ff…d4ef4` / `b66b9cd9…cfda17` |

The two inventory-source files differ. Under the merged
[content precedence](content-precedence.md) (ARC-DATA-011) the later-named
`arcanum4.dat` copy wins. The three entries the Crash Site uses (56, 69, 84) have
equal text hashes in both copies, so nothing below depends on the choice. No loose
`data/` or module-folder file overrides any of these four resources or the DLG.

## 1. The T0 inventory graph (D)

The core holds **52 items**: **37 on the ground** and **15 inside the inventories
of 8 NPCs**. Both containers are empty. Field mapping (S): the container and
critter lists are `OBJ_F_CONTAINER_INVENTORY_LIST_IDX` / `OBJ_F_CRITTER_INVENTORY_LIST_IDX`
(object-ID arrays, dense keys 0..n-1) with their `*_INVENTORY_NUM` counts; each
item carries `OBJ_F_ITEM_PARENT`, `OBJ_F_ITEM_INV_LOCATION` and the common
`OF_INVENTORY` flag ([container](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L66-L78),
[item](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L97-L121), [critter](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L228-L262) fields,
[flag](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L23),
[list fields by owner type](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L1068-L1101)).

Reconciliation over the whole loaded area (core plus context ring, 53 items and
45 owners) has no exception: every one of the 16 listed elements resolves to
exactly one item record by its raw 24-byte object ID; each
listed item's parent field names its listing owner; each carries `OF_INVENTORY`;
no inventory-flagged item is unlisted; no item is listed twice; every declared
count equals its list length. The sixteenth element is the one context item,
held by a context NPC, outside this scope. Inventory items are the 15 records the
Area already marks `ExteriorSectorInsertionRefusedByInventory`; ground items are
the 37 `BodyEligibleConditionally` bodies the viewer draws.

Life follows the passability closure's F6 rule (current HP ≤ 0): the four owners
carrying 32,000 damage are dead; the other four inventory owners carry no damage.

| Owner (source index, description, prototype) | At T0 | Items: list index → (source index, type, slot, `OBJ_F_NAME`, description) |
| --- | --- | --- |
| 2907, 31073 Virgil, 17102 | alive, no leader, no substitute inventory | 0 → (1714, armor, pack 0, 2804, 30082) **the ring**; 1 → (2421, weapon, pack 1, 2000, 6037); 2 → (2908, armor, worn armor 1006, 2800, 8094) |
| 6611, 30299, 17092 | alive | 0 → (4459, gold ×21, 3200, 9056), also its `OBJ_F_CRITTER_GOLD` handle; 1..3 → (4699, 5821, 5820, food, pack 2/3/4, 3600, 10066) |
| 980, 30298, 17132 | alive | 0 → (4458, gold ×17, 3200, 9056), also its gold handle |
| 6460, 27339, 27359 | alive; inventory source 84 | 0 → (70, weapon, wielded 1004, 2000, 6178) |
| 5699, 30105, 17198 | **dead** | 0 → (3312, generic, pack 0, 5600, 31070); 1 → (7282, generic, pack 2, 5671, 30522) |
| 4457, 30308, 17088 | **dead** | 0 → (4698, generic, pack 0, 5600, 15089); 1 → (6177, generic, pack 1, 5607, 31182) |
| 4200, 30307, 17099 | **dead** | 0 → (4201, written, pack 0, 5242, 30622) |
| 1201, 17285, 17261 | **dead** | 0 → (2527, armor, pack 0, 2807, 8159) |

Ground items, all without script attachments, all identified, none with
`OIF_NO_DROP`/`OIF_NO_DECAY`/`OIF_NO_DISPLAY`:

| Type | Count | Prototypes (count) | `OBJ_F_NAME` | Description |
| --- | ---: | --- | --- | --- |
| food | 21 | 10081 (8), 10082 (10), 10113 (3) | 3600, 3608 | 10061, 10062, 10093 |
| generic | 16 | 15089, 15091, 15102, 15107 (4 each) | 5600 | 15069, 15071, 15082, 15087 |

Item attachments exist only on inventory items: the ring (1714) has only an
`SAP_EXAMINE` script (1142); 2527 has `SAP_EXAMINE` and `SAP_INSERT_ITEM` (1341);
3312 and 6177 have `SAP_EXAMINE` (1327, 1118); the written item 4201 has
`SAP_USE` (2014). Two items carry `OIF_PERSISTENT|OIF_NO_DECAY|OIF_NO_NPC_PICKUP`
(4201, 6177; 4201 also `OIF_CAN_USE_BOX`); the ring carries no item flag at all.

**Containers (D + S).** Source indices 4453 (prototype 3055, inventory source 69)
and 6536 (prototype 3067, source 56) have an empty list. Their flags are
`OCOF_LOCKED|OCOF_NEVER_LOCKED|OCOF_INVEN_SPAWN_ONCE` with key ID 0. `object_locked_get`
returns unlocked whenever `NEVER_LOCKED` is set, so the lock bit has no effect and
no key is needed ([lock query](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4024-L4057)).
Their contents do not exist at T0: opening a spawn-once container in loot mode
generates them once ([loot open](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L690-L702)),
from the inventory-source set's coin range and per-prototype rates, with the
random generator seeded from the container location plus the PC's race,
background, gender and name characters, then the source and spawn flag are cleared
([generation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2073-L2164)).
So chest contents are a function of the created character, not stored data.

**NPC inventory sources (S).** The generator's callers that can reach a Crash Site
NPC are object creation/duplication and the NPC respawn timer, which closing an
inventory UI on that NPC arms for 12 to 24 game hours
([UI close](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L1080-L1084)); loading a saved map object does
not call it. PC creation and the new-game shop are separate callers
([creation call](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4237-L4298),
[gate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2004-L2070),
[respawn timer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2167-L2201)).
Only NPC 6460 has a source (84); at T0 its serialized one-item list is
authoritative, and a later respawn could regenerate it.

## 2. How the player gets each item (S)

- **Ground items.** A left click on any item type queues
  `AG_PICKUP_ITEM`, in or out of combat; out of combat Alt+click takes a
  different action instead ([click dispatch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L3317-L3497)).
  The goal accepts only an unparented or junk-pile item, needs the item decay
  process enabled (its counter starts at 1), consumes one action point (a combat
  cost) and calls `item_transfer(item, pc)`
  ([goal](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7850-L7891)).
  For an unparented item that runs `SAP_GET` (triggerer PC, attachee item),
  `item_check_insert` (weight against carry limit; a technology item, complexity
  below 0, is refused to a PC with the Technophobia background; free grid room),
  `object_pickup`,
  `item_insert` into the chosen slot, then cancels decay
  ([transfer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L603-L688),
  [insert check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3639-L3677),
  [insert](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3680-L3777)).
  All 37 Crash Site ground items have no attachment, and an absent attachment
  counts as script success, so only the insert check can refuse (D + S). 34 of the
  37 ground items have negative complexity, so a technophobe cannot pick them up.
- **Dead NPCs (6 items).** Out of combat a left click on a dead critter opens it
  as a container (`AG_USE_CONTAINER` → loot inventory UI); in combat it needs Alt.
  Loot admission checks the PC, target visibility and the target's `SAP_USE`
  script (none of the four dead NPCs has one) before the UI opens
  ([UI admission](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L475-L599)).
  Moving 2527 into the PC fires its own `SAP_INSERT_ITEM` script: a U frontier.
- **Living NPCs.** Virgil's ring arrives through dialog (section 4). No other
  item of a living NPC (gold, food, two weapons, worn armor) is named by a dialog
  `IN` operand. Virgil's DLG has one barter reply (`b:`), so barter can reach his
  inventory; SCR-level item actions in his other scripts (dialog override 1848,
  heartbeat 1100 and others) and in the other NPCs' scripts were not scanned and
  stay Unknown, as do theft and death.
- **Containers.** Generated on first loot open as above; there is nothing to take
  before that.

## 3. Display names (S + D)

`object_examine` picks the name source by type: a key uses `gamekey.mes` at its key
ID; an item that is not identified (`OBJ_F_ITEM_MAGIC_TECH_COMPLEXITY > 0` and no
`OIF_IDENTIFIED`) uses `OBJ_F_ITEM_DESCRIPTION_UNKNOWN`; everything else uses
`OBJ_F_DESCRIPTION`. `description_get` reads numbers below 30000 from
`description.mes` and numbers from 30000 up from the module's `gamedesc.mes`
([examine](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3934-L3992),
[identified](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2285-L2294),
[description files](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/description.c#L123-L165)).

`OBJ_F_NAME` is **not** display text. It is a numeric name class the prototype
initializer sets from a per-type base (generic 5600, food 3600, gold 3200 and so
on) or a specific number, and it is the key that `IN` and other name lookups
compare ([type bases](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L419-L465),
[assignment](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L494), with per-prototype overrides further down).
All 16 Crash Site generic ground items share 5600, so a name lookup cannot
distinguish them.

Data: all 52 core items are identified at T0 (complexity ≤ 0, or the flag set);
there are no keys. Every selected name number is present: 47 resolve in
`description.mes`, 5 in `gamedesc.mes` (the ring, 3312, 7282, 4201, 6177). Item
6177 has a different unknown description (15097 vs 31182); it is identified, so
the shown name is 31182. Quantity (gold) and identification-dependent effect
lines are separate examine/UI work.

## 4. Dialog `IN` at the Crash Site (D + S)

Only Virgil has an `SAP_DIALOG` attachment in the core (script 1324). Its DLG
(resource3, 56,782 bytes, `34a49a2c…5e53a`, 409 records) was scanned with the
source rule for conditions and actions: skip to a letter or `$`, take two
characters as the code, read a C integer after it
([condition scanner](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1360-L1406),
[action scanner](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1861-L1895)). Player replies whose text
is the barter directive `b:` were counted separately ([options](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2405-L2415)).
Player conditions and all actions were scanned; NPC condition fields were not,
because they are speech metadata.

Result: there is **exactly one** item operand in the whole dialog: the entry
action of NPC node 1 is `IN` with name **−2804**. There is no positive `IN`, no
`IN` condition and no `$$` gold operation; there is one barter reply. This is the inventory step of the
first-conversation effect sequence GF, GF, GF, IN, GF, QU, RU already recorded
in the [NPC dialog evidence](crash-site-npc-dialog-evidence.md).

Negative `IN` looks up the name in the NPC's own list first, then in his
substitute inventory, transfers to the PC and, if the item's parent is then not
the PC, removes it and places it at the PC's location; positive `IN` takes the
item from the PC or a follower and gives it to the NPC; if the NPC did not receive
it, it is dropped at the NPC's location when the NPC has a PC leader and destroyed
otherwise
([action](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2004-L2056),
[condition](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1519-L1546)).

For this one operand the probe closes the RES-021 Unknowns that depend on data:

| RES-021 Unknown | T0 answer (D) |
| --- | --- |
| List order / first match | Virgil's list index 0 is item 1714, the first and only match of 2804 in his list. |
| Name collisions | 2804 occurs on exactly one item in the whole loaded area. |
| Substitute inventory | Virgil has none (null handle), and no leader; the substitute branch is never reached. |
| Parent consistency | The item's parent field names Virgil and it carries `OF_INVENTORY`. |
| Type / stack / key | Armor type, not gold or ammo, so no stack merge and no quantity; not a key or key ring. |
| Worn slot | Pack location 0, not a wear slot, so no unequip effects on removal. |
| Item callbacks | Only `SAP_EXAMINE`; no `SAP_GET`, `SAP_INSERT_ITEM`, `SAP_REMOVE_ITEM` or wield scripts on the item. |
| Decay on fallback | No `OIF_NO_DECAY`, so if the PC cannot take it and it is placed on the ground, it is decay-eligible. |

Still Unknown (U): the recipient PC's own `SAP_TRANSFER` attachment and its
insert check (weight, Technophobia and free room; the ring has complexity 0, so
the Technophobia branch does not apply) depend on the created character, which is not
in the map data. The [character creation research](character-creation-evidence.md)
covers the PC prototypes and starting inventory, not this callback or the
carry/room check against the ring.

## 5. Stacks, keys and key rings (D + S)

- **Stacks (FR-ITEM-003).** The only quantity items at T0 are the two gold
  stacks (21 and 17), each also referenced by its holder's `OBJ_F_CRITTER_GOLD`
  handle. There is no ammunition. Food and generic items are separate objects even
  when they share a prototype. Gold and ammunition merge on insertion by
  prototype, as RES-021 E06 records.
- **Keys and key rings (FR-ITEM-004).** The core has no key and no key ring, both
  containers have key ID 0 and are never locked. The ring Virgil hands over is an
  armor-type item, not a key ring. FR-ITEM-004 has nothing to
  exercise at the Crash Site.

## Proposal for implementation (P)

1. **T0 inventory import (Data).** Delivered by ARC-DATA-012 as the
   [Area T0 inventory view](../world/area-inventory-view.md). A Godot-free, read-only view over the Area
   snapshot: owners with ordered lists and declared counts, items with parent,
   inventory location, flags and the name/description fields, and the
   reconciliation above as a fail-closed check. No new reader is needed; the Area
   effective fields already carry everything.
2. **Inventory owner (Simulation/Session).** One owner of item location
   (ground cell, owner list and slot), with typed results for pickup, loot
   transfer and the negative-`IN` give, and refusal before effects when an
   unadmitted attachment, a stack/key type or an insert check is involved. Admit
   first only what the Crash Site needs: unscripted ground pickup, loot from the
   four dead NPCs (excluding 2527 until its insert script is resolved) and the
   one negative `IN` for the ring.
3. **Conversation binding.** ARC-SCRIPT-005 already records `IN` as a typed
   command; the owner consumes that command instead of the dialog changing items.
4. Keep chest generation, barter, theft, NPC respawn and positive `IN` out until
   their own research. The PC's insert check needs the stats, background and
   starting inventory from [character creation](character-creation-evidence.md).
