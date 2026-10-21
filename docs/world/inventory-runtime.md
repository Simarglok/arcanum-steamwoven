# Session inventory runtime

Status: **Implemented bounded component**, ARC-WORLD-014 (steps 2 and 3 of the
[ARC-RES-023 proposal](../research/crash-site-item-inventory.md#proposal-for-implementation-p)). A Godot-free
inventory domain in the [game session](game-session-runtime.md) (`Arcanum.Session`): it moves Crash Site items to the
PC by ground pickup, by looting a dead NPC and by the dialog's negative `IN`, and saves the items the PC took in
snapshot schema 5. It covers the transfer part of [FR-ITEM-001](../requirements/items-economy.md#fr-item-001) for
these three paths only. Since ARC-UI-004 the AREA viewer uses it: seeds from its loaded maps, pickup and loot clicks,
the inventory window and Virgil's `IN` ([viewer](../rendering/terrain-viewer.md#inventory-window-pickup-and-loot-arc-ui-004)).

Categories as in the research: source claims (S) are a reading of the original's functions at the cited addresses,
evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them. Data claims (D) are checked by the opt-in
real-data test below.

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

**State.** The ordered list of items the PC took from the world (`SessionPcItem`: entity, origin map, kind);
`QueryPcInventory` returns it. Since ARC-WORLD-015 the session also holds the hero's gold (`QueryGold`) and starting kit
(`SessionKitItem`: `kit:1`, `kit:2`, … with no origin map; `QueryHeroKit`; [starting hero](starting-hero-runtime.md)), and since ARC-TECHNOLOGY-004 the items it made from schematics (`SessionMadeItem`: `made:1`, … with no origin map; `QueryMadeItems`; [schematic assembly](schematic-assembly-runtime.md#made-items-snapshot-schema-24)),
which precedes the taken items in the PC's inventory and counts toward `MaxPcItems`. A taken item is hidden from its
seed's ground and holder lists (`QueryMapInventory`). The PC object, its
attachments and gold pickup are not session state yet; the hero's worn slots are ([equipment](equipment-runtime.md),
ARC-WORLD-028; a worn item stays in its list).

All entity ids follow the session rule (ASCII letters, digits and `. _ : -`, 1..128 characters); the AREA host uses
`oid:<SHA-256 of the raw 24-byte object ID>`, the id the conversation host already uses.

## Transfers

Each transfer takes the expected `GameSessionVersion`, works on the seed of the actor's map
(`InventoryUnavailable` without one), refuses before any effect, and adds one revision when it moves an item to the end
of the PC's list. `InventoryMutationResult` names the moved item and the insert check's answer.

| Call | Source path (S) | Admitted here |
| --- | --- | --- |
| `PickUpItem(item)` | left click → `AG_PICKUP_ITEM` → the item transfer (0x4617F0) of an unparented item: item `SAP_GET`, insert check, the object pickup (0x4417A0), the item insert (0x466640; item `SAP_INSERT_ITEM`) | an item on the ground of the actor's map |
| `LootItem(holder, item)` | click on a dead critter opens it as a container (target `SAP_USE` checked first); a moved item runs the parented transfer: PC `SAP_TRANSFER`, insert check, the item removal (0x466E00; item `SAP_REMOVE_ITEM`, unequip when worn), the item insert (0x466640) | an item still listed by an NPC the seed marks dead, or one the session killed (ARC-COMBAT-005, [hero attack](hero-attack-runtime.md)) |
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
`ItemScriptUnsupported` (an item `SAP_GET` for pickup or `SAP_REMOVE_ITEM` for loot and IN, or an item
`SAP_INSERT_ITEM` when the caller passes no script host; other attachments such as examine and use do not run on a
transfer), `BudgetExceeded` (`GameSessionLimits.MaxPcItems`, at most 512), `RevisionExhausted`, then `InsertRefused`, and
last the item's insert-item script (below).

**Insert check.** The original's insert check (0x466510) depends on the created character (carry weight against its strength, the
Technophobia background against technology items, free grid room around its own items), none of which the session owns.
The host answers it per item with a `SessionInsertCheck` delegate (`Accepted`, `TooHeavy`, `TechnologyRefused`,
`NoRoom`; any other value refuses). It runs only after every other refusal. If the host calls back into the owner from it and anything the transfer read
changed (revision, actor map, the attached seed, the item already taken), the transfer is `StaleVersion` with no effect.
A refusal changes nothing: for pickup and
loot that is the source behaviour; for IN the source would drop the item at the PC's location (into a junk pile when
items lie there), which is not modelled, so IN is refused instead.

Not modelled, on purpose: the PC's `SAP_TRANSFER` attachment (no PC object), reach and the walk to an item, the
combat action-point cost of a pickup, item decay timers, the item insert's slot choice and hotkeys, encumbrance effects,
dropping, barter, theft, positive `IN`, NPC respawn and chest generation.

## Insert-item script (ARC-SCRIPT-013)

The original inserts the item and then runs the item's insert-item point (21) with the new holder as triggerer and the
item as attachee; the point cannot cancel and its result is ignored (ledger IF10). The Crash Site amulet (2527) is the
one Crash Site item with such a script; [ARC-RES-055](../research/item-insert-script.md) found it only prints a line.

`PickUpItem`, `LootItem` and `GiveItemToPc` take an optional `ISessionItemScriptHost` (an `IScrHost` plus
`PrintedLine(script, line)`). With it, a scripted item's transfer runs `ScrDispatcher.RunObject` for point 21 with the
item (`ScrObjectRef(actor map, item)`) and the player (`SessionEventKinds.PlayerEntity`) under the session's script rules
and `SessionItemScripts.Admission` (S6 without the dialog action, the story-state actions and the deferred calls). The
run is made after the insert check and before the commit; the admitted commands read nothing the insertion changes, so
the order is equivalent. The transfer then commits together with what the session applies from the run, or is refused
with `ItemScriptUnsupported` and no effect when:

- the script is outside the set (`Refused`) or its file exists but cannot be read (`UnreadableScript`);
- it reads a global flag the session does not hold as known (the run reads flags from the session, not from the host);
- the run changed the attachment (local flags, counters, its number), started a conversation, set story state, asked for
  a deferred call or a trap destruction: the moved item has no home for a changed slot yet;
- it writes a global flag the session does not hold as known;
- a printed line is `Unsupported` for the host (an action field, generated text, a name the host cannot fill).

Global flag writes are applied with the transfer, in its one revision. A printed line the host shows comes back in
`InventoryMutationResult.Messages` (`SessionScriptMessage`: script, line, kind, text); a missing DLG prints nothing
(`NoDialog`: no DLG or one without records), as does a missing script file (`MissingScript`, the transfer proceeds); a
DLG the host cannot read is `Unsupported`. `InventoryMutationResult.InsertScript` carries the run (also on a refusal). A
call back into the owner from the host during the run (any revision, actor, seed or global-flag change) makes the
transfer `StaleVersion`. Without a host the item is refused, as before.

The AREA viewer passes `AreaItemScriptHost` when the game's script content loaded: an item's slot from its area row,
scripts and DLGs from the content stack (the session's data-fix rules), the session's flags, the player and the party;
the printed line uses the hero's gender (male without a hero) and the diagnostic PC name; a line naming
`@npcname@` is `Unsupported` (whose name it would be on an item run is not read), so such an item is refused. Lines a
script prints during Virgil's `IN` reach `SessionDialogInventoryPort.LastResult`, but the conversation panel does not show
them yet (no Crash Site `IN` gives a scripted item). The status line and the loot window show the printed text after the item's name.

## Dialog seam

`IDialogInventoryPort` (Scripting) takes the conversation's `GiveItemCommand`. When a host passes it as the last
`DialogConversationPorts` argument (`Inventory`), negative `IN` goes there instead of the world port, and `IN` needs only
one of the two ports. `SessionDialogInventoryPort` runs `GiveItemToPc` on the session's current version with the host's
NPC-to-entity map and insert check: a transfer and the no-match case are `Applied`, a refusal is `Rejected` (the
conversation then fails with `CommandRejected`), and `LastResult` keeps the session's answer. It must be rebuilt when
the host replaces the owner, like the party port.

## Snapshot schema 5

(Schema 7 since ARC-WORLD-019, schema 8 since ARC-COMBAT-002, schema 9 since ARC-WORLD-024; the inventory part is described here.)

`GameSessionSnapshot.Inventory` is encoded between `hero` and `random` (between `hero` and `lighting` before schema 7). Schema 6 (ARC-WORLD-015) added `gold` and the
`kit` list:

```json
"inventory":{"gold":0,"kit":[],"pc":[{"entity":"ring","map":79,"type":"Armor","prototype":3804,"name":2804,"description":32804},
                            {"entity":"apple","map":79,"type":"Food","prototype":4600,"name":3600,"description":33600}]}
```

(The synthetic test's values; line breaks added.)

Decoding keeps the list order and refuses an entity listed twice, in either list (`NotCanonical`), an unknown type name or a map below
1 (`OutOfRange`), missing or unknown fields and more than 512 items (`BudgetExceeded`). `Restore` refuses a type a
transfer cannot produce (gold, ammunition, key, key ring: `InvalidItem`), a repeated entity (`InvalidSnapshot`) and the
caller's lower limit, kit and taken items together (`BudgetExceeded`); gold or a kit item without a hero, and a kit
item that is not the next `kit:n`, are `InvalidSnapshot`. After a restore no seed is attached; `AttachMapInventory` then refuses
(`InventoryMismatch`) a seed that lacks an item the PC took from that map, gives it another kind, or lists an item the
PC took from another map, so a save of other data cannot silently duplicate or lose an item. For the same reason a seed
may not list an item another attached seed lists (one object lies on one map).

**Migration decision: none.** Schemas 1..5 are `UnsupportedSchema` (schema 6: [starting hero](starting-hero-runtime.md#snapshot-schema-6)); a schema-4 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. The viewer has no inventory state
in schema 4 to lose (its `IN` record was in memory only).

## NPC inventory changes (ARC-WORLD-055)

One store of NPC inventory changes for every NPC on every map (`GameSessionNpcInventory.cs`), read in the executable first
([ledger "NPC inventory changes"](../research/original-verification-ledger.md#npc-inventory-changes-arc-world-055), NI1–NI7).

**Original (NI1–NI5).** The original keeps no inventory store of its own: an NPC's list is fields of the NPC, each item its own
object with a parent field, and all of them are saved with the map the NPC stands on when the PC leaves it (changed fields of
original objects, deleted original objects by ID, whole records of objects created in play) and read back when the PC returns.
A follower carried to another map takes its whole inventory with it, parentage kept, and from then on is that map's object.

**Steamwoven.** The session keys the changes by the NPC's entity identity (the `oid:` entity of its home map), not by the map:
the NPC's list is its seed list on whatever attached seed lists it, minus the items the PC took or the session deleted, followed by
the made items the session added to it. Because an object stands on one map at a time and carries its items (NI4), keying by the
NPC gives the same visible result: the changes are there after the PC leaves and returns, and they stay the NPC's when it
visits another map (`QueryNpcInventory`). The map-bound calls (`QueryMapInventory`, `LootItem`, `GiveItemToPc`) still read the
holders of the actor's map seed, so on a map whose seed does not list a visiting companion its seed and added items are not shown
or taken there (as before this task for its seed items). The changes are:

- **Added made items** (`SessionInventoryState.Npcs`: `SessionNpcInventory(npc, items)`; `QueryNpcInventory(npc)`): `made:n`
  items with their saved kind, numbered with the hero's made items (one past every made number held by the hero or an NPC, deleted
  or with a durability record), in the order they entered the NPC's list. `AddItemToNpc(expected, npc, kind, insertCheck)` makes one
  (the creation 0x4ED8B0 and the transfer 0x4617F0 into the NPC, as the NPC crafter's product enters its inventory).
- **Deleted items**: `RemoveItemFromNpc(expected, npc, item)` deletes an added item or a seed item still in the NPC's list with the
  [durability](item-durability-runtime.md) deletion (hidden from every seed, the condition record dropped, recorded as deleted;
  the original's destroyed original object, whose ID goes to `mobile.des`, NI3). No item script runs.
- **Items the PC takes** (loot, negative `IN`) leave the list as before; an added made item keeps its entity and kind and joins
  the hero's made items (`QueryMadeItems`), never the taken world items.

`QueryMapInventory`, `LootItem` and `GiveItemToPc` see the added items after the seed items; a corpse's added items are loot (life
from the session's life records), a living NPC's are not. The AREA viewer's loot window reads `QueryMapInventory`, so it shows
them. The NPC schematic crafter (ARC-TECHNOLOGY-006, [assembly](schematic-assembly-runtime.md#npc-crafter-arc-technology-006)) deletes its
components and adds its products through the same rules.

The NPC must be known to the session: an NPC holder of an attached seed, a party record, or an NPC that already holds added items
(else `UnknownHolder`). Refusals, before any effect: `InvalidItem` (an invalid id, a `made:` id as the NPC, an undefined type),
`StaleVersion`, `UnknownHolder`, then for an addition `ItemTypeUnsupported` (gold, ammunition, keys and key rings, as for the PC),
`BudgetExceeded` (the NPC's list at 512 with its seed items, the store at `GameSessionLimits.MaxNpcItems` = 4,096, or no free made
number), `RevisionExhausted` and last `InsertRefused` (the host's insert check for the NPC; a call back into the owner from it, a seed
attach included, is `StaleVersion`); for a deletion `ItemNotAvailable` (not in the NPC's current list), `WornItemUnsupported` (unequipping an NPC is not
modelled), `BudgetExceeded` (the durability records) and `RevisionExhausted`. Each change is one revision. A seed that lists an NPC
holding added items as a container is `InventoryMismatch`, and one whose holder's current seed items and added items would exceed
512 is `BudgetExceeded` (a restore followed by an attach).

**Snapshot schema 29.** `"inventory":{…,"made":[…],"npcs":[{"npc":"oid:…","items":[{"entity","type","prototype","name","description"}]}]}`,
the NPCs in ordinal order. Decoding refuses an unordered NPC list or an empty item list, and an item held twice in the whole
inventory (`NotCanonical`); restore refuses an item that is not `made:n`, a `made:` NPC id, a deleted item or one held twice
(`InvalidSnapshot`), a type the lists do not admit (`InvalidItem`) and more than the caller's `MaxNpcItems` (`BudgetExceeded`).
Since the PC can take a made item from an NPC, the hero's made list keeps entry order and needs no hero. Schemas 1..28 are refused,
not migrated.

**Differences from the original (both modes).**

| Original 1.0.7.4 | Steamwoven |
| --- | --- |
| Changes are saved with the map the NPC stands on (NI1–NI3). | Changes are saved by NPC identity; the visible result is the same because the NPC carries its items (NI4). |
| A reference to an item no longer found is kept as a null list element with the count unchanged (NI2). | The list holds only items it has; a deleted item is gone from it. |
| Closing a barter window restocks a non-follower NPC with an inventory source after 12–24 game hours, destroying items without the persistent flag (NI6, F1). | Not modelled (no barter); added items stay. |
| A teleport clears an NPC's substitute-inventory link; an insert makes the NPC re-equip its best items (NI7, F1). | Not modelled. |
| Any object type can enter an NPC's list. | Gold, ammunition, keys and key rings are refused, as for the PC's lists. |

Not modelled: moving a seed item from one NPC to another (no caller yet), giving an NPC an item from the PC (barter, drop on a
follower), NPC worn slots, stacks, and a visiting companion's items in the map-bound calls of a map whose seed does not list it.

## Evidence

- Synthetic (ARC-WORLD-055): [NpcInventoryStateTests](../../tests/Arcanum.Session.Tests/NpcInventoryStateTests.cs) (addition and numbering,
  every refusal, a party record without a seed, loot and `IN` of added items, deletion with the durability rules, round trip, the
  attach check, codec and restore refusals).
- Synthetic: [InventoryStateTests](../../tests/Arcanum.Session.Tests/InventoryStateTests.cs) (seed validation and
  copy, every refusal and its order, the insert check, list order, budget, codec and restore refusals, attach after
  restore, the dialog port) and [DialogInventoryTests](../../tests/Arcanum.Scripting.Tests/Dialog/DialogInventoryTests.cs)
  (routing, capability, rejected and throwing port).
- Real data (opt-in `RealData`, `ARC_AREA_CONVERSATION_GAME_ROOT`):
  [CrashSiteInventoryTests](../../tests/Arcanum.TerrainViewer.Tests/CrashSiteInventoryTests.cs). The step rules mark
  exactly the four research NPCs with items dead (5699, 4457, 4200, 1201). Virgil's original first conversation, opened
  by the viewer's conversation host, runs node 1's `IN -2804` through the session port and the ring (1714) enters the
  PC's list, leaving his weapon and worn armor. Without script content five of the six dead-NPC items loot and 2527 is
  refused for its `SAP_INSERT_ITEM` script; with the content (ARC-SCRIPT-013) it loots too, script 1341 prints line 1 of
  its DLG and writes no flag, and once global flag 1085 is set it returns at once and prints nothing. All 37 core ground items pick up. Virgil and a chest are not lootable. A save and load keep the 43 items,
  the re-attached seed agrees, and a second `IN -2804` changes nothing.

## Next steps

The AREA viewer is wired (ARC-UI-004). Next: the insert check from the [session hero](starting-hero-runtime.md)'s carry
weight (the viewer accepts every item and starts no hero yet), and the remaining source
paths (drop and junk piles, stacks, chests) after their own research.
