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
parent or without a location; a critter gold handle that is not a gold item in that critter's own list;
an item or holder from a sector file (not admitted); a key ring with keys (not modelled); a missing
mandatory field; duplicate mobile IDs or source indices; more than 2,048 items or holders or 512 list elements.

The research reconciliation covers the list, parent, flag, double-listing and count rules. The other
refusals were added by ARC-DATA-012 and are observed to hold on the exterior and the cave only: ground
items unparented and located, gold handles inside the holder's own list, no sector-file items or holders,
no filled key ring. A context holder whose item lies outside the loaded area would also refuse the view
(`ListedObjectUnresolved`); that does not occur in either profile.

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
map but outside the window, may name it, and the item is left out of this view's items until cross-map holders exist (MIG6e). Such an item still needs its
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
