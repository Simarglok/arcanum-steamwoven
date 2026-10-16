# Session inventory runtime

Status: **Implemented bounded component**, ARC-WORLD-014 (steps 2 and 3 of the
[ARC-RES-023 proposal](../research/crash-site-item-inventory.md#proposal-for-implementation-p)). A Godot-free
inventory domain in the [game session](game-session-runtime.md) (`Arcanum.Session`): it moves Crash Site items to the
PC by ground pickup, by looting a dead NPC and by the dialog's negative `IN`, and saves the items the PC took in
snapshot schema 5. It covers the transfer part of [FR-ITEM-001](../requirements/items-economy.md#fr-item-001) for
these three paths only. Since ARC-UI-004 the AREA viewer uses it: seeds from its loaded maps, pickup and loot clicks,
the inventory window and Virgil's `IN` ([viewer](../rendering/terrain-viewer.md#inventory-window-pickup-and-loot-arc-ui-004)).

Categories as in the research: source claims (S) are read from `alexbatalov/arcanum-ce` at
`a7ff41b300ef712f0e7d088183a3d08957110cdd` (Sustainable Use License, not MIT); nothing is copied. Data claims (D) are
checked by the opt-in real-data test below.

## Model

**Seed (baseline, not state).** The host attaches one `SessionMapInventorySeed` per loaded map with
`AttachMapInventory(map, seed)`: every holder (container or NPC) with its list in serialized order, script attachment
points, a non-null substitute-inventory handle and its life fact (`Dead`, set only when the host knows the NPC is
dead), and every item with its facts (`SessionItemKind`: type, prototype, name class `OBJ_F_NAME`, description number;
weight, complexity, inventory location, quantity, script attachment points) and either a ground cell or exactly one
listing holder. For the Crash Site the host builds it from the [T0 inventory view](area-inventory-view.md)
(ARC-DATA-012) and life from the [step rules](area-step-assessment.md) (`CRITTER_DEAD` evidence); the viewer's
`AreaInventoryMap.Build` holds that mapping (ARC-UI-004). The seed is validated (unique entity ids, each item listed once or
on the ground, no dead or substitute container, defined item types 5..14, a quantity on gold and ammunition and only
there, distinct script points 0..35 (`SAP_EXAMINE`..`SAP_CRITICAL_MISS`); at most 2,048 items and holders and 512 per list; otherwise `InvalidInventorySeed`) and
copied; at most `MaxMaps` seeds are attached (`BudgetExceeded`). Attaching moves no revision, is never saved, and is
repeated after every restore.

**State.** The PC's ordered list (`SessionPcItem`: entity, origin map, kind) and its gold. Since ARC-WORLD-015 the
list starts with the hero's starting kit (`kit:1`, `kit:2`, …, no origin map; [starting hero](starting-hero-runtime.md))
and the hero's coins are the gold (`QueryGold`); the items the PC took from the world follow. A taken item is hidden from
its seed's ground and holder lists (`QueryMapInventory`); `QueryPcInventory` returns the list. The PC object, its
attachments, worn slots and gold pickup are not session state yet.

All entity ids follow the session rule (ASCII letters, digits and `. _ : -`, 1..128 characters); the AREA host uses
`oid:<SHA-256 of the raw 24-byte object ID>`, the id the conversation host already uses.

## Transfers

Each transfer takes the expected `GameSessionVersion`, works on the seed of the actor's map
(`InventoryUnavailable` without one), refuses before any effect, and adds one revision when it moves an item to the end
of the PC's list. `InventoryMutationResult` names the moved item and the insert check's answer.

| Call | Source path (S) | Admitted here |
| --- | --- | --- |
| `PickUpItem(item)` | left click → `AG_PICKUP_ITEM` → `item_transfer` of an unparented item: item `SAP_GET`, insert check, `object_pickup`, `item_insert` (item `SAP_INSERT_ITEM`) | an item on the ground of the actor's map |
| `LootItem(holder, item)` | click on a dead critter opens it as a container (target `SAP_USE` checked first); a moved item runs the parented transfer: PC `SAP_TRANSFER`, insert check, `item_remove` (item `SAP_REMOVE_ITEM`, unequip when worn), `item_insert` | an item still listed by an NPC the seed marks dead |
| `GiveItemToPc(npc, name)` | `DIALOG_ACTION_IN` with a negative value: first item in the NPC's list with that name class, else in its substitute inventory; parented transfer to the PC; dropped at the PC's location when the PC did not get it | the NPC's current list; no match and no substitute is NoChange, as in the source |

Refusals, in order, after input (`InvalidItem`), version (`StaleVersion`) and seed (`InventoryUnavailable`) checks:

- pickup: `ItemNotAvailable` (not on that map's ground, or already taken);
- loot: `UnknownHolder`, `HolderNotLootable` (a living NPC, or a container: chest contents are generated on first open
  from the created PC and are not modelled), `HolderScriptUnsupported` (a holder `SAP_USE` script), `ItemNotAvailable`
  (not in that holder's current list);
- IN: `UnknownHolder` (not an NPC of that map), then no own match: NoChange, or `SubstituteInventoryUnsupported` when the
  NPC has a substitute inventory;
- then the shared item rules: `ItemTypeUnsupported` (gold and ammunition merge into stacks, a key may go onto the PC's
  key ring), `WornItemUnsupported` (a parented item in slots 1000..1008),
`ItemScriptUnsupported` (an item `SAP_INSERT_ITEM`, or `SAP_GET` for pickup, or `SAP_REMOVE_ITEM` for loot and IN;
other attachments such as examine and use do not run on a transfer), `BudgetExceeded` (`GameSessionLimits.MaxPcItems`,
at most 512), `RevisionExhausted`, and last `InsertRefused`.

**Insert check.** `item_check_insert` depends on the created character (carry weight against its strength, the
Technophobia background against technology items, free grid room around its own items), none of which the session owns.
The host answers it per item with a `SessionInsertCheck` delegate (`Accepted`, `TooHeavy`, `TechnologyRefused`,
`NoRoom`; any other value refuses). It runs only after every other refusal. If the host calls back into the owner from it and anything the transfer read
changed (revision, actor map, the attached seed, the item already taken), the transfer is `StaleVersion` with no effect.
A refusal changes nothing: for pickup and
loot that is the source behaviour; for IN the source would drop the item at the PC's location (into a junk pile when
items lie there), which is not modelled, so IN is refused instead.

Not modelled, on purpose: the PC's `SAP_TRANSFER` attachment (no PC object), reach and the walk to an item, the
combat action-point cost of a pickup, item decay timers, `item_insert` slot choice and hotkeys, encumbrance effects,
dropping, barter, theft, positive `IN`, NPC respawn and chest generation.

## Dialog seam

`IDialogInventoryPort` (Scripting) takes the conversation's `GiveItemCommand`. When a host passes it as the last
`DialogConversationPorts` argument (`Inventory`), negative `IN` goes there instead of the world port, and `IN` needs only
one of the two ports. `SessionDialogInventoryPort` runs `GiveItemToPc` on the session's current version with the host's
NPC-to-entity map and insert check: a transfer and the no-match case are `Applied`, a refusal is `Rejected` (the
conversation then fails with `CommandRejected`), and `LastResult` keeps the session's answer. It must be rebuilt when
the host replaces the owner, like the party port.

## Snapshot schema 5

(Schema 6 since ARC-WORLD-015; the inventory part is described here.)

`GameSessionSnapshot.Inventory` is encoded between `hero` and `lighting`. Schema 6 (ARC-WORLD-015) added `gold` and a
`null` map for kit items:

```json
"inventory":{"gold":0,"pc":[{"entity":"ring","map":79,"type":"Armor","prototype":3804,"name":2804,"description":32804},
                            {"entity":"apple","map":79,"type":"Food","prototype":4600,"name":3600,"description":33600}]}
```

(The synthetic test's values; line breaks added.)

Decoding keeps the list order and refuses an entity listed twice (`NotCanonical`), an unknown type name or a map below
1 (`OutOfRange`), missing or unknown fields and more than 512 items (`BudgetExceeded`). `Restore` refuses a type a
transfer cannot produce (gold, ammunition, key, key ring: `InvalidItem`), a repeated entity (`InvalidSnapshot`) and the
caller's lower limit (`BudgetExceeded`); gold or a kit item without a hero, and a kit item that is not
the next `kit:n` at the head of the list, are `InvalidSnapshot`. After a restore no seed is attached; `AttachMapInventory` then refuses
(`InventoryMismatch`) a seed that lacks an item the PC took from that map, gives it another kind, or lists an item the
PC took from another map, so a save of other data cannot silently duplicate or lose an item. For the same reason a seed
may not list an item another attached seed lists (one object lies on one map).

**Migration decision: none.** Schemas 1..5 are `UnsupportedSchema` (schema 6: [starting hero](starting-hero-runtime.md#snapshot-schema-6)); a schema-4 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. The viewer has no inventory state
in schema 4 to lose (its `IN` record was in memory only).

## Evidence

- Synthetic: [InventoryStateTests](../../tests/Arcanum.Session.Tests/InventoryStateTests.cs) (seed validation and
  copy, every refusal and its order, the insert check, list order, budget, codec and restore refusals, attach after
  restore, the dialog port) and [DialogInventoryTests](../../tests/Arcanum.Scripting.Tests/Dialog/DialogInventoryTests.cs)
  (routing, capability, rejected and throwing port).
- Real data (opt-in `RealData`, `ARC_AREA_CONVERSATION_GAME_ROOT`):
  [CrashSiteInventoryTests](../../tests/Arcanum.TerrainViewer.Tests/CrashSiteInventoryTests.cs). The step rules mark
  exactly the four research NPCs with items dead (5699, 4457, 4200, 1201). Virgil's original first conversation, opened
  by the viewer's conversation host, runs node 1's `IN -2804` through the session port and the ring (1714) enters the
  PC's list, leaving his weapon and worn armor. Five of the six dead-NPC items loot; 2527 is refused for its `SAP_INSERT_ITEM` script (the research's open
  question). All 37 core ground items pick up. Virgil and a chest are not lootable. A save and load keep the 43 items,
  the re-attached seed agrees, and a second `IN -2804` changes nothing.

## Next steps

The AREA viewer is wired (ARC-UI-004). Next: the insert check from the [session hero](starting-hero-runtime.md)'s carry
weight (the viewer accepts every item and starts no hero yet), and the remaining source
paths (drop and junk piles, stacks, chests) after their own research.
