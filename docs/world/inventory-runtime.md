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
- then the shared item rules: `ItemTypeUnsupported` (gold merges into the PC's gold, a key may go onto the PC's
  key ring; since ARC-WORLD-066 ammunition is admitted and merges into a held stack, [below](#ammunition-arc-world-066)), `WornItemUnsupported` (a parented item in slots 1000..1008),
`ItemScriptUnsupported` (an item `SAP_GET` for pickup or `SAP_REMOVE_ITEM` for loot and IN, or an item
`SAP_INSERT_ITEM` when the caller passes no script host; other attachments such as examine and use do not run on a
transfer), `BudgetExceeded` (`GameSessionLimits.MaxPcItems`, at most 512), `RevisionExhausted`, then `InsertRefused`, and
last the item's insert-item script (below).

**Insert check.** The original's insert check (0x466510) depends on the created character (carry weight against its strength, the
Technophobia background against technology items, free grid room around its own items). The session applies the Technophobia
part for the hero after the host's weight answer ([backgrounds](equipment-runtime.md#backgrounds-arc-world-069)); the weight and the
room are the host's. The host answers it per item with a `SessionInsertCheck` delegate (`Accepted`, `TooHeavy`, `TechnologyRefused`,
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
transfer cannot produce (gold, key, key ring: `InvalidItem`; ammunition without its stack record: `InvalidSnapshot`, ARC-WORLD-066), a repeated entity (`InvalidSnapshot`) and the
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
`StaleVersion`, `UnknownHolder`, then for an addition `ItemTypeUnsupported` (gold, keys and key rings, as for the PC; ammunition since
ARC-WORLD-066, with its type and units: `InvalidAmmo` without them),
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
| Any object type can enter an NPC's list. | Gold, keys and key rings are refused, as for the PC's lists; ammunition merges into the NPC's stack of its prototype (ARC-WORLD-066). |

Not modelled: moving a seed item from one NPC to another (no caller yet), giving an NPC an item from the PC (barter, drop on a
follower), NPC worn slots, gold, and a visiting companion's items in the map-bound calls of a map whose seed does not list it.

## Placed items (ARC-WORLD-059)

The hero's items the session placed on the ground (`GameSessionOwner.QueryPlacedItems`, `SessionPlacedItem`), read in the
executable first ([ledger "Placed items"](../research/original-verification-ledger.md#placed-items-arc-world-059), PI1–PI6). In the
original an item is an object anywhere: dropping it removes it from the owner (unwielding a worn one) and moves it to a tile,
where it stays until something picks it up or its 48-hour decay destroys it; a pickup is the ordinary transfer.

**Store.** One saved list of the hero's items on the ground (only a session with a hero has one), in placing order: entity, kind, the hero's list it came from
(`SessionPlacedSource`: the kit, a world item the PC took, with the map whose seed lists it, or a made item), and the map and cell
it lies on. It admits the types the hero's lists admit (wider than the worn items the drops place today, for a later drop
command). A placed item stays taken, so its seed never shows it at its seed position, and it counts in the hero's item budget
(`MaxPcItems`), so it can always come back. `QueryMapInventory` lists the map's placed items after the NPC drops.

- **Placing** (internal, the hero's critical drops of [ARC-COMBAT-009](hero-attack-runtime.md#critical-effects-arc-combat-009):
  `DropHelmet`, `DropWeapon` and the arm-hit drops): the item leaves its wear slot and its list and lies on the tile the
  [drop cell](#drop-cell-arc-combat-010) chooses, or at the hero's cell when the host passes no map
  (`SessionCriticalStepOutcome.Dropped`). One revision. A no-drop item is refused, as in the original (PI1); an item an earlier
  step of the same attack broke has already left its slot (`NoItem`); a made item's number is never handed out again while it lies
  on the ground.
- **Assembly products** (ARC-WORLD-070): a schematic product unit the inventory's insert check refuses lies on the crafter's tile
  as a made item (source `made`): the hero's tile for the hero's assembly, the tile the host gives for an NPC crafter (on the hero's
  map), as the original's creation on the tile and unchecked transfer leave it (SA3–SA5). An ammunition unit there is a stack of its
  own with its units. The assembly checks the store's bound before it spends anything ([assembly](schematic-assembly-runtime.md)).
- **Pickup**: `PickUpItem` of a placed item on the actor's map returns it to its list, a kit item at its kit number's place (the
  kit stays in number order), a taken or made item at the end of its list. The host's insert check runs first (a call back into the
  owner from it is `StaleVersion`); one revision. On another map the item is not available.
- **Deletion**: a placed item the durability rules delete leaves the store.
- **Seeds**: an attached seed must still list a placed world item of its map with its saved kind and no seed may list a placed kit
  or made item (`InventoryMismatch`), as for the hero's lists.

**Snapshot schema 32.** `"inventory":{…,"npcs":[…],"placed":[{"entity","source":"kit"|"pc"|"made","origin","map","x","y","type",
"prototype","name","description"}]}`, `origin` the seed map of a `pc` item and 0 otherwise, the cell 0..2^24 − 1 per coordinate.
Decoding refuses an unknown source or out-of-range value (`OutOfRange`), an origin that does not fit the source or an item held twice
in the whole inventory (`NotCanonical`) and more than the policy's hero items (`BudgetExceeded`); restore refuses any placed item without a hero, a kit item with no kit number or one beyond the kit's numbers (its kit, placed
and deleted items together), a made item without a made number, a world item with a kit or made id, a deleted item (`InvalidSnapshot`)
and a type the lists do not admit (`InvalidItem`). The kit's numbers skip its placed items as they skip its deleted ones. Schemas
1..31 are refused, not migrated.

**Not ready (the original's behaviour, PI1–PI6).**

- The 48-hour Item Decay (PI4): the original destroys an item lying on the ground or in a junk pile 48 game hours after it was
  dropped (unless its item flag 0x20000 is set; a container open in the loot window delays it by a minute); placed items, NPC drops
  and spilled containers' items stay. A follow-up task.
- Junk piles (PI3): a dropped item joins a junk pile on its tile, or makes one with the loose items there; placed items lie loose.
- The scripts and side effects of the removal (PI2) and of the pickup (PI6): item spells, the wield-off, remove-item and drop scripts,
  and the get and insert-item scripts of a pickup, none of which runs. A world item whose attached origin seed shows a drop script
  is not dropped (`ScriptUnsupported`), one with a get or insert-item script is not picked up (`ItemScriptUnsupported`); with its
  seed not attached, and for a kit or made item, the session does not know the scripts and the item goes unchecked. The light and armour of the hero follow its equipment at once.
- The PC's re-wield after losing a weapon (PI5): with the "auto switch" setting on, the original wields the best weapon; the session
  leaves the slot empty.
- NPC made items (ARC-WORLD-055) and the junk pile of OD13 can use this store later. The viewer draws placed items and picks
  them up since ARC-RENDER-033 ([ground items](#ground-items-in-the-viewer-arc-render-033)); a placed made item (an assembly
  product left on the tile) is drawn from its product prototype's art since ARC-WORLD-070.

## Drop cell (ARC-COMBAT-010)

Where a dropped item lands, read in the executable first ([ledger "Drop cell"](../research/original-verification-ledger.md#drop-cell-arc-combat-010),
DC1–DC8, with CR14). `DropPlacementRules` (Simulation) is the drop's draws and tries: `Distance` draws 2..4 (0x461A90), `Place` makes
up to ten tries of 0x4F4E40, each with a direction draw 0..8 (8 stays), over an injected `IDropMap`: the original's step
(0x4B8FF0), its path request with the drop's flags (0x41F3C0, flags 0x10) for a `DropOwner` (an object, the PC side, or an NPC on
its own; the trap rule tells them apart) and its PC-or-NPC test on a tile (0x4407C0, the dead included).

**Session.** A critical drop (`SessionAttack.DropMap`, `SessionObjectAttack.DropMap`) with a map draws the distance from the
session generator before the drop's refusals (the drop script, the no-drop flag), then the tries after them; the generator's
exhaustion is `RandomExhausted`. The hero, and an NPC following it, are the PC side; any other NPC is an NPC on its own. The hero's
item joins its placed items on that tile; an NPC's seed item is the `item.dropped` map change at that tile. Without a map the item
lands on its holder's cell and nothing is drawn (a host without one).

**Viewer port.** `AreaDropMap` (viewer Core) is the AREA viewer's map for the hero's attacks on NPCs and objects
(`AreaDropMapNow`): the path's gate on the target's tile, the direct attempt and the 64 × 64 search exactly as read, with the
step tests of the shown map's T0 step evidence (built on the same tile and traversal functions) and the doors' runtime passage;
critters never block a step (mode 4), and an object with flag 0x400 does not close the target's tile; containers and scenery the session destroyed or switched off no longer block; a trap's
flags come from its map data (slot 36). PCs and NPCs on a tile are the map's critter records that have not moved, dead or alive,
plus the hero's and the companions' live cells.

**Not ready.**

- The NPC search budget per real second (DCA2): the session never runs out of it.
- The map's bounds are the step evidence the viewer holds (the exterior's nine sectors, the cave); a tile or step the evidence
  cannot settle is treated as blocked (DCA3).
- A generator that runs out during the tries (after about 2^63 draws) leaves the distance draw made and the item where it was
  (`RandomExhausted`); the drop's draws are not taken on trial.

## Ground items in the viewer (ARC-RENDER-033)

The AREA viewer draws what the session holds on the ground but no T0 body drew, and the art a T0 body changes to:

- **Items.** Every item in `QueryMapInventory(map).Ground` that does not lie on its own T0 body's cell: spilled from a container
  (ARC-COMBAT-008), dropped by an NPC (ARC-COMBAT-009/010) or placed by the hero (ARC-WORLD-059). Its ground ART is its map row's
  current art (field 0; for a placed world item, the row of the map whose seed lists it) or, for a kit item, its prototype's
  (`AreaStartingHero.KitArt`), or, for a made item (ARC-WORLD-070), its product prototype's current art from the schematic data
  (`AreaSchematicData.ArtOf`). The viewer's own insert check accepts every item, so in play a product reaches the ground only with a
  refusing host; the native smoke checks the store and that the product's art reads. It is placed on its cell as the plan places a body there with no object offset and scale 100
  (`AreaScenePlan.Place`; whether the original keeps a dropped item's offsets and scale was not read in the executable: NOT
  VERIFIED), flat when its object flags carry OF_FLAT (0x4), and joins the moved bodies: an upright one the upright order,
  a flat one the flat commands in plan order (`AreaScenePlan.WithMovingFlat`). It is lit where it lies by its own art's channel.
  A click picks it up as any ground item, walking to its cell; a placed item without a row of the shown map (a kit item, a world
  item from another map) is named and picked under a key of its own, with its origin row's fields when there is one.
- **Destroyed ART.** A scenery or trap the session busted is drawn from its destroyed ART (field 24) at its T0 anchor
  (`AreaScenePlan.Repose`, as a fallen pose).
- **Art.** `AreaExtraArt` reads an art id the T0 snapshot did not decode from the game's content stack in its search order
  (ARC-DATA-018), named by the area owner's rules for classes 4, 6 and 7 (`AreaObjectArt`, shared with the owner) and decoded by
  its frame rules (the selected frame, group 0 of a single-rotation ART, the ART's own extent); explicit limits
  (`AreaExtraArtLimits`, for the whole viewer run of one module: 4,096 art ids, 64 MiB of pixels, 16 MiB per file; the opt-in
  census `AreaExtraArtOriginalTests` reads every destroyed ART and every held item's ground ART of the 80 interiors: 4 destroyed
  ARTs, 2,449 held items, 328 distinct art ids, none failing). An art id that does not resolve or read leaves the
  body as it was and is logged once (`ARC-RENDER-033 extra-art`).

**Not ready.** The junk pile of OD13
(ARC-WORLD-063) and the items' decay (ARC-WORLD-062); a busted window's busted ART.

## Evidence

- Synthetic (ARC-COMBAT-010): [DropPlacementRulesTests](../../tests/Arcanum.Simulation.Tests/Items/DropPlacementRulesTests.cs)
  (the distance, the critter test of the walk, no path, a long path, the direct attempt's partial count, tries off the map,
  direction 8), [AreaDropMapTests](../../tests/Arcanum.TerrainViewer.Tests/AreaDropMapTests.cs) (rotation and step tables,
  the direct attempt, the target's gate, the search round a container, traps by owner, a window, critters on a tile) and the
  session's drops through a map in [CriticalEffectsStateTests](../../tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs).

- Synthetic (ARC-WORLD-059): [PlacedItemsStateTests](../../tests/Arcanum.Session.Tests/PlacedItemsStateTests.cs) (placing a kit and a
  taken item, the view, another map, the insert check, the kit's order on return, save and restore, the attach check, deletion, codec and
  restore refusals) and the hero's critical drop in [CriticalEffectsStateTests](../../tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs).

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

## Ammunition (ARC-WORLD-066)

Ammunition (type 6) is admitted to the hero's and NPCs' lists since ARC-WORLD-066 (snapshot schema 34), read in the executable
([ledger AM1–AM7](../research/original-verification-ledger.md#ammunition-arc-world-066), anomalies AMA1–AMA6). Gold, keys and key
rings stay out. Code: [`GameSessionAmmo`](../../src/Arcanum.Session/GameSessionAmmo.cs) and the transfers above; tests
[`AmmoStateTests`](../../tests/Arcanum.Session.Tests/AmmoStateTests.cs) and
[`AreaAmmoLinesTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaAmmoLinesTests.cs).

- **Facts.** A seed's ammunition item carries its unit count (`Quantity`, field 0x8E) and its ammunition type (`AmmoType`,
  field 0x8F, 0..3: arrows, bullets, charge cells, fuel). A type outside 0..3 or a negative count refuses the seed (AMA2, AMA3:
  the original indexes past its tables). The Data reader gives the type (`AreaInventoryItem.AmmoType`).
- **State.** The session keeps a `SessionAmmoRecord(entity, type, units)` for every ammunition item the hero holds or placed,
  every one added to an NPC, and any other whose units it changed (a seed stack an NPC spent from or received into); by entity in
  ordinal order, at most `GameSessionLimits.MaxAmmoRecords` (4,096). A record's units are 0 or more (the original never removes a
  stack at 0 by itself). `QueryAmmo(entity)` gives a tracked stack or a seed stack's units; `QueryHeroAmmo()` the hero's stacks in
  list order; the map views (`QueryMapInventory`) show the current units.
- **Insertion (AM1, AM2).** Pickup, loot, negative `IN` and a placed item picked up again: an ammunition item merges into the
  hero's first held stack of the same prototype (its units added, the inserted item deleted with the durability deletion, as the
  original destroys it); without one it enters the list as a new stack. The insert check still runs. A sum that does not fit 32 bits
  is refused (`BudgetExceeded`, AMA1). `AddItemToNpc` takes ammunition with its type and units and merges it into the NPC's stack of
  the prototype in the same way.
- **Spending (AM4).** `SpendAmmo(expected, holder, type, count)` spends from the hero's, an NPC's or (ARC-TECHNOLOGY-009,
  `SessionAmmoHolder.ContainerOf`) a container's first stack of the type (the original's one stack a critter field names; AMA4
  latent; a container's only where the item rules find it, AMA5 below): fewer units refuse with nothing spent (`AmmoShort`), exactly the count
  deletes the stack, more leave the rest; a count of 0 changes nothing; a negative count or a type out of 0..3 is `InvalidAmmo`.
  One revision.
- **Charge cells (ARC-TECHNOLOGY-007).** `HeroCellSource(itemComplexity?)` is the engine's `ISpellCellSource` over the hero's own
  power-cell stack: an applied, paid plan spends the cells at once through `SpendAmmo`; an unapplied request writes nothing.
  `ContainerCellSource(container, itemComplexity)` (ARC-TECHNOLOGY-009) does the same for a device in a container from the
  container's stack. AMA5 is a switch (owner decision 2026-10-21): Original keeps the failed lookup (AM6: a container's stack is
  never found, so a spend finds nothing and a device in it has no cells), Steamwoven finds the container's first stack of the type
  (`ItemRuleProfile.ContainerStackLookupFails`). A container's spent units are kept as the session's record of that seed stack.
- **Assembly (TECH-4).** An ammunition component loses one unit (0x4658E0) and its stack goes at its last unit; an ammunition product
  (the product prototype's type and units, `productAmmo`) merges each unit into the crafter's stack of its prototype, one new
  `made:n` stack only when it holds none; a unit the insert check refuses is a stack of its own on the crafter's tile
  (ARC-WORLD-070). The installed recipes use it: four make ammunition (cells ×5 and ×1, bullets ×2, the fourth
  type ×5) and three take one unit of ammunition as a component. The NPC crafter does the same in its list.
- **Not modelled.** Gold (the AMA5 fix will apply to its container lookup too); transfers of units between holders (AM4's destination); the critter field and its
  clearing (AM5: the session finds a holder's stack by type); the weight of a stack (the host's insert check). A shot's ammunition
  should call `SpendAmmo` with the weapon's ammunition type and per-shot count (ARC-COMBAT-012); the critical misses that take the
  whole stack or five shots' worth do since ARC-COMBAT-013 ([hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)).

## Next steps

The AREA viewer is wired (ARC-UI-004). Next: the insert check from the [session hero](starting-hero-runtime.md)'s carry
weight (the viewer accepts every item and starts no hero yet), and the remaining source
paths (drop and junk piles, stacks, chests) after their own research.
