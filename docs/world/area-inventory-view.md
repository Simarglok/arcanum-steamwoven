# Area T0 inventory view

ARC-DATA-012 delivers step 1 of the [ARC-RES-023 proposal](../research/crash-site-item-inventory.md#proposal-for-implementation-p):
a Godot-free, **read-only** view of who holds which item in one strict Area snapshot, before any tick,
script or player action. It is `Arcanum.Data.Inventory.AreaInventoryReader.Read(AreaSnapshot)`
([code](../../src/Arcanum.Data/Inventory/AreaInventory.cs),
[tests](../../tests/Arcanum.Data.Tests/Inventory)).

## What it reads

Only the snapshot's effective fields (instance value, else the authenticated prototype value); no
archive, mobile file or prototype is reopened. The field mapping is the one the research document cites
from the source: container and critter `*_INVENTORY_LIST_IDX` object-ID arrays with their
`*_INVENTORY_NUM` counts and `*_INVENTORY_SOURCE`; item `OBJ_F_ITEM_PARENT`, `OBJ_F_ITEM_INV_LOCATION`,
item flags, weight, worth, complexity, unknown description, gold/ammunition quantity and key ID; the common
object flags, `OBJ_F_NAME`, `OBJ_F_DESCRIPTION` and script attachments; critter `OBJ_F_CRITTER_GOLD`; NPC
leader and substitute-inventory handles.

Object identity is the SHA-256 of the raw 24-byte ID, the digest the snapshot already keys every record
by (`AreaObjectKey.ObjectIdSha256`) and stores for handle fields. A list element is rebuilt from its six
words and hashed, so it matches only the exact raw ID, padding included.

## Result

`AreaInventory` holds every holder (container, PC, NPC) and every item (types 5..14) of the loaded area,
core and context, ordered by aggregate source index:

- `AreaInventoryHolder`: type, prototype, scope, location, description, inventory source, its items as
  source indices **in serialized list order**, script attachments; container flags and key ID for
  containers; the resolved gold item for critters; leader and substitute-inventory handles for NPCs
  (each with its digest and, when loaded, its source index).
- `AreaInventoryItem`: type, prototype, scope, location, the numeric fields above, script attachments,
  and for listed items the holder and list index. `Name` is the numeric name class, not display text;
  `Identified` follows the research rule (complexity not positive, or the identified flag).
- `ListOf(holder)` returns a holder's items in list order.

## Fail-closed reconciliation

Any of these refuses the whole view with a typed `AreaInventoryFailure` and the offending source index:
a snapshot that is not strictly published; a list that is not dense or whose element is not a six-word
ID; a declared count that differs from the list length; an element that names no mobile record, or a
record that is not an item; an item listed twice; a listed item whose parent field names another object
or that lacks the inventory flag; a ground item with a
parent or without a location;
an item or holder from a sector file (not admitted); a key ring with keys (not modelled); a missing
mandatory field; duplicate mobile IDs or source indices; more than 2,048 items or holders or 512 list elements.

The research reconciliation covers the list, parent, flag, double-listing and count rules. The other
refusals were added by ARC-DATA-012 and are observed to hold on the exterior and the cave only: ground
items unparented and located, no sector-file items or holders, no filled key ring. A holder whose listed item
is not a record of the loaded area refuses the view (`ListedObjectUnresolved`); that does not occur in either
whole-map profile, and a window of the main map no longer meets it (next section but one).
A critter gold handle that names no gold item of its own list is not a refusal any more (`GoldHandleMismatch` stays in the enum, unused): see below.

## Stale gold handles and held items outside a window (ARC-WORLD-2036)

Two data facts of the main map's city windows refused the whole view, and with it the window's inventory, combat and AI hosts (no moving NPCs, loot or fights there):

- **A gold handle outside the holder's own list.** In Ashbury five NPCs (source indices 1162, 3030, 3377, 4068 and 6955 in the aggregate; in Tarant one, 1461) have a
  gold handle (`OBJ_F_CRITTER_GOLD`) that does not name a gold item of their own list. NPC 1162 (prototype 17146) names a gold stack that NPC 5664 (the same prototype)
  lists and whose parent is 5664. The handle is kept as `AreaInventoryHolder.StaleGoldHandle` (with the source index of the named record when it is in the area) and the
  holder has no `GoldItem`; the holder's own list is read as it stands. Nothing reads the field: the seed and the hosts find gold through the list. How the original
  reads a shared handle (GT164 reads a victim's gold through it) is not modelled here; [#2088](https://github.com/Simarglok/arcanum-steamwoven/issues/2088) holds it. The viewer's `inventory-seed` line adds `N gold handles outside their holder's list (stale)`.
- **A listed item whose own location lies outside the window.** A held item keeps the location it had when it was made or picked up, not its holder's, and a window keeps
  the mobile records that stand in its sectors, so a holder inside the window could list an item the window did not hold (Tarant: in the window at sector 969,1026 NPC 599 lists an item whose own location is in another sector, one such item; the window at 970,1026 has seven). A **window** plan now also admits every mobile item (types 5..14) whose parent field names a mobile
  object admitted by location, wherever the item lies: `AreaDataOwner` builds it after the located rows as a **context** row (no body, disposition `ContextOnly`, its own
  serialized location kept) and the plan counts it and its prototype with the window's objects. `AreaReconciliation` still counts by serialized location (the item counts as
  outside), so its numbers do not change. Whole maps and the pinned profiles are unchanged. An item whose parent is not admitted stays out (it is a detached item
  of the next section if flagged, or a ground item of the area it stands in).

Real data (opt-in `RealData`, `CityWindowInventoryRealDataTests`): the windows 504..506,975..977 (Ashbury) and 969,1026 and 970,1026 (Tarant) read, with the stale handles above; the synthetic
tests are `AreaInventoryTests.A_gold_handle_outside_the_holders_list_is_kept_apart_not_refused` and `AreaWindowTests.A_window_admits_the_items_its_holder_lists_wherever_they_lie_and_no_other_outside_record`.

## Detached items (ARC-DEV-085, MIG6b)

An item that carries the inventory object flag (`OF_INVENTORY`, 0x1000) but that no holder of the loaded area lists is **not** a refusal any more
(`UnlistedInventoryItem` stays in the enum, unused). It goes to `AreaInventory.Detached` (`AreaInventoryDetachedItem`: key, type, prototype, scope,
inventory location, parent handle with its source index when the parent is in the area) and stays out of `Items`, so a consumer neither places it on the
ground nor lends it to a holder. The seed of the area builds; the viewer's `inventory-seed` line adds `N held but unlisted (detached)`.

Why: the town window of the main map (sector 1409,1314, runtime map 1) holds one such item, a weapon (prototype 6049, inventory location 1004) whose parent
is an NPC of the same window whose list (7 of 7) does not name it. The refusal made the whole seed unavailable (bug B02: no inventory, no loot, the hero's
worn set not shown, until the hero went back into the temple map whose own seed is ready). The first plan guessed a holder outside the window; the reading of the
real data showed the holder inside it. The original never lists an `OF_INVENTORY` item in a sector (insert refuses it, [RL3](../architecture/original-object-model.md))
and its fold and queries skip a held item (RL10), and a holder's items are found through its list, so an item whose parent is in the area (or null) and
that the list does not name is reachable by nobody: it is inert, as it is here (F2 for the sector part, F1 for the list walk of the inventory functions, which was
not read one by one). An item whose parent lies **outside** the loaded area is also kept apart, but that is a limit, not a claim: the parent's own list, on the
map but outside the window, may name it, and the item is left out of this view's items (a limit that stays: MIG6e, ARC-DEV-087, moves an item out of an NPC's list on an attached map to the hero, it does not read a parent outside a window). Such an item still needs its
inventory location field (a missing one is `FieldUnresolved`, as for every item).

Every other reconciliation rule is unchanged: a listed item still needs its parent and flag, a ground item still no parent and a location.

## Original data (opt-in `RealData` tests)

Exterior aggregate `a548f611…de6a5`: the view reconciles 53 items and 45 holders and the tests assert the
research tables' lists, counts, prototypes, name and description numbers, gold, scripts and NO_DECAY items: 52 core items, 37 on the ground (21 food, 16 generic, none scripted) and
15 in eight NPC lists; Virgil (2907) lists the ring 1714 at index 0, whose name class 2804 is unique in
the area; the gold stacks 4459 (21) and 4458 (17) are their holders' gold handles; the two containers
4453 and 6536 are empty. The one context list is holder 1382 with item 3488.

Cave aggregate `aa29827d…58318` (first observation, new in this task): 28 items and 11 holders; every
item sits in one of four containers (lists of 12, 3, 5 and 8), the seven NPCs hold nothing, and there is
no ground item.

## NOT READY

Life state (it needs the effect rules of `AreaStepInspector`), display text and chest
generation belong to later tasks. Transfers (pickup, loot, the dialog `IN` command) and their save state live in the
[session inventory runtime](inventory-runtime.md) (ARC-WORLD-014), which takes this view as its T0 seed.
The view describes serialized T0 only; nothing here mutates or tracks item location.
