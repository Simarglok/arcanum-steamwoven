# Object directory (K13, MIG6, MIG7)

Status: **slices MIG6a (ARC-DEV-084), MIG6b (ARC-DEV-085), MIG6c (ARC-DEV-086), MIG6d-1 (ARC-DEV-088), MIG6d-2 (ARC-DEV-090), MIG6d-3 (ARC-DEV-091) and MIG6e (ARC-DEV-087) implemented, and MIG7a (ARC-DEV-092), MIG7b (ARC-DEV-093), MIG7c (ARC-DEV-094) MIG7d (ARC-DEV-095) of MIG7 MIG8 slices 1 (ARC-DEV-096), 2 (ARC-DEV-097) and 3 (ARC-DEV-098)** (2026-11-04/05). Concept document K13 of the
[architecture audit](../research/architecture-audit-plan.md) and design of MIG6 ([target model](target-model.md#migration-order),
elements TM1, TM2, TM4). Behaviour from the executable stays the original's rows ([object model](original-object-model.md), OM1–OM32);
this page fixes how the session represents them and what is built so far.

## What MIG6 is, and the slices

MIG6 asks for one object directory, one id space and one create and destroy path, with the per-kind stores of `InventoryDomain`
(`Kit`, `Made`, `Npcs`, `Placed`, `JunkPiles`, `BloodPools`, `Given`, `Ammo`, `Piles`) becoming queries over it. The stores
are woven through about thirty session files, so MIG6 is cut into slices that each keep the game and the saves running:

| Slice | Result | State |
| --- | --- | --- |
| **MIG6a** (ARC-DEV-084) | The directory as a read model over the stores: one answer to "where is object X"; the origin of an id read from the id; junk and pool ids never reused (saved high-water marks, schema 60). | **Done** |
| **MIG6b** (ARC-DEV-085) | The seed refusal of the town window (B02, `AreaInventory.Build` `UnlistedInventoryItem`): an item flagged as held that no holder lists no longer refuses the seed; it is a detached item, out of the items ([view](../world/area-inventory-view.md#detached-items-arc-dev-085-mig6b)). | **Done** |
| **MIG6c** (ARC-DEV-086) | One create and one destroy path inside `Arcanum.Session`: the creators of `made:n` items, junk piles and pools of blood call `CreateMadeItem`, `CreateJunkPile` and `CreateBloodPool`; every deletion calls `DestroyObject`, which also dissolves a pile an item left (as the decay step did alone before). | **Done** |
| MIG6d | The stores become entries: the directory holds the entry (origin, location) and the stores keep only what they alone know (kind facts, ammunition units, pile slots); snapshot sections move with a schema step each. Cut into three: **MIG6d-1** (ARC-DEV-088) the holder lists of NPCs and containers (a merchant's stock added by the session); **MIG6d-2** (ARC-DEV-090) the hero's lists, the placed items and the given records; **MIG6d-3** (ARC-DEV-091) the junk piles and pools of blood as records found by entity, and the one snapshot section `objects` built from them with schema step 60 → 61. | **MIG6d-1, MIG6d-2, MIG6d-3 done** |
| **MIG6e** (ARC-DEV-087) | Cross-map holders: an item moves from a holder of another attached map (a carried companion, whose seed is its home map's) to the hero's list; the negative `IN` of a companion carried here stops being `UnknownHolder` (the [MIG5 limit](../world/record-resolver.md#one-dialog-host-arc-dev-081-mig5)). | **Done** |

## MIG6a: what is built

**One id space.** An entity string names exactly one object. Its origin is read from the string by `GameSessionOwner.ObjectOriginOf`:
`kit:n` (the starting kit, numbered once at session creation), `made:n` (made, script-created, split and given-away items, one counter
over every store, the deleted-item records and the regeneration discard mark), `junk:n` (junk piles), `pool:n` (pools of blood), and every other
entity is a map object (`oid:` hash of its persistent id, stable on every map, FM4). No two origins can collide.

**Never reassigned (TM1, OM3).** Junk pile and pool numbers came from the highest number held at the time, so a dissolved pile's or a decayed pool's number
was given again. Two saved marks, `inventory.lastJunkNumber` and `inventory.lastPoolNumber`, keep the highest number ever given; a new pile
or pool takes the mark plus one (`GameSessionOwner.AllocateObjectNumber`, the one allocator; made numbers keep their own rule, `NextMadeNumber`). A load never
sets a mark below a number a saved pile or pool holds. Schema 60; the step from 59 adds both marks as 0, which is the older load's rule (the highest number held now).

**One locator (GR3).** `GameSessionOwner.QueryObject(entity)` returns a `SessionObjectEntry` (origin, place, map, cell, holder, worn slot, pile) or null, across
the deleted items, the hero's lists and worn slots, the items the hero placed or scripts created, the NPC and container lists, junk piles, pools and the attached seeds.
`QueryObjects(origin)` lists the objects of one origin the session made or moved, in entity order. Both are reads of the current state: no revision, no schema, no
game data read (a map object of a map whose seed is not attached and that the session never moved is unknown, which is the
limit MIG6b and MIG6d remove).

Places: `Destroyed`, `HeroPack`, `HeroWorn`, `NpcList`, `ContainerList`, `Ground`, `JunkPile` (an item lying in a pile; the entry names the pile and its cell),
`SeedHolder` (still in the list its map data names, neither taken nor dropped). A pool or a pile is itself `Ground`; a decayed pool is `Destroyed`.

## MIG6b: what is built

The cause of B02 was not the one this table first named (a holder outside the window): in the town window the weapon 2345 (inventory location 1004, held flag set)
names as its parent an NPC of the same window whose list does not hold it, so the holder is loaded and the data is inconsistent. The original finds a holder's
items through its list and never lists a held item in a sector (RL3, RL10), so the item is inert there. `AreaInventoryReader` now keeps such an item in
`AreaInventory.Detached` and out of `Items`; `AreaInventoryMap.Detached` carries the entities and the seed builds (`Ready`, the viewer's detail names the count).
Nothing is created for it: `QueryObject` of a detached entity answers null (a map object the session never moved and that no seed row names), which is the existing
limit MIG6d removes. Only this window is known to hold such an item (the town window, sector 1409,1314; the Crash Site and cave seeds were already `Ready` without one). The rule is general, so another
window or map with one builds too; where the parent is outside the window the item is only left out of the view, which stays a limit: MIG6e (cross-map holders) moves an item out of an NPC's list on an attached map, it does not read a parent outside a window.

## MIG6c: what is built

`GameSessionObjectLifecycle.cs` holds both paths; the per-kind stores stay where they are (MIG6d moves them).

**Create (TM4, GR4).** `NextObjectEntity(origin)` gives the entity the next object of a made, junk or pool origin takes (null when the origin's numbers run out;
it reserves nothing, the allocator of MIG6a stays the only one). `CreateMadeItem(SessionMadeCreation)` is the one registration of a made item: it lands in the hero's
made items, an NPC's or a container's added items, or the ground (a placed item of source `Created` or `Made`), is recorded as taken and keeps its weight facts. Its callers are
the script's create object (`CreateGroundItem`), a new ammunition stack of the hero (`NewHeroStack`, `DropHeroAmmo`), every item added to an NPC or a container
(`AddItemToNpc`, `NewNpcEntry`: barter, restock, a split or given stack), and the hero's and an NPC's assembly products (their entities are planned unit by unit before the
host's insert check, so the numbers come from `NextMadeNumber` once per assembly). `CreateJunkPile` and `CreateBloodPool` take the number through `AllocateObjectNumber` and
record the pile or the pool. The checks that precede a creation (budgets, the host's insert check, the revision) and the item's own records (ammunition units, gold coins, decay)
stay with the caller: the session does not create from a prototype and does not run the location notification, the NPC's heartbeat or the inventory generation (TM4's full tail
waits for the stores to become entries, MIG6d, and for MIG7 and later).

**Destroy (TM4, GR4, B04).** `DestroyObject(entity)` replaces the private `Delete` of the durability file and is the only destroyer: an item leaves its wear slot, every list, the
ground and its junk pile, is hidden from every seed, loses its records and decay and is recorded as deleted; then the pile it left is checked as the original's removal does
(JP5, JP7): with no container shown in the loot window, a pile left with one item or none dissolves at once and the last item is dropped again with a new decay
([view](../world/inventory-runtime.md#junk-piles-arc-world-063)). While the window shows the pile (a decay waits for it; the window's own loot never dissolves its pile, which
`CloseJunkPile` does on closing) the pile stands. A junk pile or a pool of blood is destroyed by the same call (the pile's record only, its items lie loose; a decayed pool is
purged by the next creation through it). The path is session-wide: the ammunition spend, assembly components, barter's gold piles, a used scroll or food, a broken item, a healed
item's last use, the restock's old stacks, a schematic and the merge of an ammunition stack all end in it, so no destroy path leaves a pile with one item or none standing except under the loot-window rule below (before,
only the decay step dissolved). The shown-window state is read from the event world of the pass the destroy runs in (the decay); outside a pass the session knows none and the
pile is checked at once, which dissolves a pile the original would keep while a container window is open (a limit: the host's window state is not session state; the `windowShowsContainer` argument is for hosts that know it and has no production caller yet). The original's "decay on" flag (0x5E87E4) of JP5 is not modelled.

## MIG6e: what is built

The cause of the MIG5 limit was the session's lookup, not a missing store: the negative `IN` (`GiveItemToPc`) and the steal (`TakeItemFromNpc`) looked for the NPC
in the seed of the actor's map only, and the item entered the hero's list with the actor's map as its origin. A companion carried here is listed by the seed of its
home map (attached to the session by the host: the given maps with the host, a map the resolver read on demand when first asked about), so it was unknown.

- **Where the holder is.** `NpcHolderSeed(npc)` answers the attached seed that lists the NPC: the actor's map first, else the other attached maps in runtime-map order
  (an object lies on one map, so at most one lists it). `GiveItemToPc` and `TakeItemFromNpc` move the item from that seed (its list, its dropped cells and its
  `HolderOf` are that map's); the unchanged refusals are `InventoryUnavailable` (no seed on the actor's map and none that lists the NPC) and `UnknownHolder`.
- **Origin.** `Take` keeps the seed's map, not the actor's: the hero's `SessionPcItem` origin is the map whose seed lists the item (`Taken`, the viewer's hidden
  items and the attach check of a later seed read it), so a save and a re-attach of both maps agree. The stale check follows that seed: a new seed of the item's map
  during the host's insert check refuses the move with no effect. No new state, no schema step (snapshot schema stays 60).
- **Scripts.** The script host answers for the shown map's rows (every object of a run stands on that map), so an item that is a row of another map's seed and
  carries a remove-item or insert-item script refuses the move (`ItemScriptUnsupported`) instead of moving it with a script skipped; no installed item is known to
  carry one. A holder's own scripts do not matter: the gate of a move out of a holder is the PC's transfer script, which the host answers. With a host the PC's gate
  still runs with the shown map as the item's map (harmless while the item guard holds).
- **Host.** `SessionDialogInventoryPort` takes an optional `prepare(entity)` hook; `AreaInventoryHost.DialogPort` passes `AttachHolderMap`, which attaches the seed of the
  map that lists the speaker when the resolver read it on demand and the session holds none (`AttachReadMap`, the rule `AreaCombatHost` already used for its holder
  index). Only the dialog port attaches: the steal (`TakeItemFromNpc`) has no such hook in the viewer, so it moves from a read-on-demand home map only after something
  else (the combat host's holder index) attached its seed. The other direction, the hero giving an item to a companion of another map (`GiveItemToNpc`), already knew every attached seed and keeps the item's
  origin (`Given`); a companion's item the hero gave back is in its list again.
- **Smoke.** The `transfer` stage's visitor talk is followed by `visitor-give` (`ARC-DEV-087 native-smoke visitor-give`): on the cave (map 79) the dialog port takes an item
  from Virgil, whose home is the main map's window (map 1), the hero's list holds it with origin 1, and the hero gives it back. It needs a hero (`--hero human-male`);
  without one it prints `NONE`. Observed on the original data: PASS (candidates 2, item name 2000).

## MIG6d-1: what is built

The lists of made, given and gold items the session added to an NPC or to a container (a merchant's substitute container a barter window filled, ARC-WORLD-090) were two
stores, `Npcs` (holder to list) and `Containers` (which keys are containers), and found an item's holder by a scan of every list. They are now one `HolderLists`
(`GameSessionHolderLists.cs`): an entry per held item, keyed by the item's entity, holding its holder and its place in the holder's list; a holder's list is the entries that
name it, in the order they entered. `InventoryDomain.Lists` is the only owner; `AddedTo`, `NpcOf`, `AddToNpc`, `AddToContainer`, `RemoveFromNpc` and `NpcHeld` keep their meaning and
now answer by the entity index, so no caller scans. What the entry alone does not know stays where it was: kind and complexity on `SessionMadeItem`, ammunition units in `Ammo`, a given item's
origin in `Given`, the worn slot in `NpcWorn`. An item held twice is a broken invariant (`ENTITY_HELD_TWICE`); a restore already refuses it before it reaches the lists.

- **Snapshot.** Unchanged: `inventory.npcs` is written from the entries in the old order (holders in ordinal order, each list in order, the container mark), so no schema step
  (schema stays 60); an old save loads as before. The one section of entries and its schema step belong to MIG6d-3, when every kind is an entry.
- **Query.** `GameSessionOwner.QueryHeldBy(holder)` answers the stock of one holder, an NPC's or a container's (the merchant's substitute container too): the items its map data
  lists that nothing moved (`SeedHolder`, in the seed's order), then the items the session added (`NpcList` or `ContainerList`, in the order they entered). It is a read of the current
  state in no revision; the seed items of a holder whose map has no seed attached are not known, as for `QueryObject`.
- **Not done here.** The hero's lists (`Kit`, `Pc`, `Made`), the placed items, the `Given` records, the junk piles and the pools of blood are still their own stores, and the created
  items of the NPC lists still differ from map items in how they are saved (GR4, GR17 stay open).

## MIG6d-2: what is built

The hero's lists were four stores of `InventoryDomain`, `Kit`, `Pc`, `Made` and `Placed`, plus the `Given` records, and every question "is this item in the hero's pack" or "which list holds it" scanned them in turn (about thirty
call sites). They are now one `HeroLists` (`GameSessionHeroLists.cs`), the only owner, with an index from entity to place (`HeroPlace`: kit, taken, made, placed), so a place is found in one step; `InventoryDomain.Kit`, `Pc`,
`Made` and `Placed` stay as read-only views of its lists and `InventoryDomain.Hero` is the owner for every change. An item is in one place at a time (a second entry of an entity is `ENTITY_HELD_TWICE`, as for `HolderLists`).

- **Entries.** `PlaceOf`, `KitItem`, `PcItem`, `MadeItem`, `PlacedItem`, `InPack` (kit, taken or made, a worn item too) and `PackKind` answer by entity; `PackKinds` is the pack in list order; `AddKit`, `InsertKit`
  (at the kit number's place), `AddPc`, `AddMade`, `AddPlaced`, `ReplacePlaced` (a thrown item that landed keeps its place in the order) and `Remove` (out of whichever list holds the item) are the only changes. The destroy
  (`DestroyObject`), the drop (`PlaceHeroItem`), a give to an NPC or a container (`GiveItemTo`), the return of a placed or given item (`Return`, `ReturnGiven`) and the restore all go through them; the checks "the hero holds this" of the
  durability, repair, healing, lock-pick, wear and equipment files call `InPack` instead of three scans.
- **Given records.** `SetGiven`, `GivenOf`, `IsGiven`, `TakeGiven` and `GivenAll` (entity order) replace the dictionary `Given`: the record keeps what the NPC's list entry does not know, the origin of a given taken item (a given kit item has
  none), and is found by the entity as before.
- **Snapshot.** Unchanged: `Capture` writes the same sections in the same order (kit, taken, made, placed, given), so schema 60 stays and an old save loads as before; `ImportInventory` validates as before and fills the lists through the
  same entry points. The one section of entries and its schema step belong to MIG6d-3.
- **Query.** `QueryObject` answers the hero's pack and placed items by the index (`InPack`, `PlacedItem`); no public method changed, and `QueryObjects` reads the given records through `GivenAll`.
- **Not done here.** The junk piles, the pools of blood, the ammunition units, the weight facts and the taken mark are still their own stores (MIG6d-3 and later), and the created items of an NPC list and the placed items still differ from map items
  in how they are saved (GR4, GR17 stay open).

## MIG6d-3: what is built

The junk piles and the pools of blood were two lists of `InventoryDomain` (`JunkPiles`, `BloodPools`) with two number marks, and "which pile holds this item" scanned every pile. They are now one `ObjectRecords`
(`GameSessionObjectRecords.cs`, `InventoryDomain.Objects`), the only owner: pile and pool records found by entity, an item index so `PileOf(item)` answers in one step, the made order kept, the two high-water marks
beside them. Changes go through `AddPile`, `ReplacePile`, `RemovePile`, `LeaveItem`, `AddPool` and `RemovePool` only, so the item index cannot drift (a pile entity or an item recorded twice is a broken invariant,
`JUNK_PILE_TWICE`, `ITEM_PILED_TWICE`, `BLOOD_POOL_TWICE`). `CreateJunkPile`/`CreateBloodPool` (MIG6c) and `DestroyObject` are the callers; `QueryObject`, `QueryObjects` and the drop, loot and seed checks read the same records.

- **Snapshot, schema 61.** One new root section `objects` (`SessionObjectRecords`, `GameSessionSnapshot.Objects`) holds `junkPiles`, `bloodPools`, `lastJunkNumber` and `lastPoolNumber`, written from `ObjectRecords.Capture()`;
  the four fields leave the `inventory` section. The upgrade step `[60]` (`GameSessionSnapshotUpgrade.cs`) moves them as saved, in order, so every schema 56 to 60 save still loads (a save missing one of the four is
  `MissingField $.inventory.<name>`, a schema 60 text that already has `objects` is `UnknownField $.objects`). The restore checks are unchanged: `RestoreJunkPiles` runs right after the inventory, `ImportBloodPools` and
  `ImportObjectMarks` as before, from the new section.
- **Tests.** `ObjectRecordsTests` (index, order, invariants), `SnapshotUpgradeTests.A_schema_60_save_decodes_as_the_current_schema` (a schema 60 text written by hand with a pile, a pool and marks above the held
  numbers, and each damaged variant), the older schemas upgrade through 60, and the whole session suite runs on schema 61 fixtures.
- **GR4, GR17.** GR4 is closed for the kinds the session has: items, junk piles and pools of blood have one create path, one destroy path and records found by entity. GR17 is closed for the piles and pools only, saved as one
  section of records. **Not closed:** the made and placed items are still in the `inventory` section and the other TM4 kinds (NPCs, projectiles, summons, traps) have no store; the section of whole objects per map (TM8) is later work.

## MIG7a: what is built

MIG7 (object state on the entry, TM2, TM10, GR5, GR10, GR36) is cut into four slices, each its own PR: **MIG7a** (ARC-DEV-092) the fields of mobile map-data objects by identity;
**MIG7b** (ARC-DEV-093) the changes of static objects per sector, keyed by their positional identity; **MIG7c** (ARC-DEV-094) NPC positions as state; **MIG7d** (ARC-DEV-095) links as `ObjectId` fields and
the GQ7 B switch. MIG8 (script attachments on the object) rests on the store MIG7a builds.

**The store.** The entity-scoped map changes that are the state of a mobile map-data object were keys under a map (`MapChangeKey.ForEntity`, one value per map, entity and property). `SessionMapChanges`
(`GameSessionMapChanges.cs`) now keeps those, the **object fields** of `ObjectStateVocabulary`, by the object's identity: one record per entity and property, naming the map it is kept on (the object's home
map, which the map budgets and `QueryMapChanges` use). A key is an object field when it is entity-scoped, its entity starts with `oid:` (a mobile object; a static object's positional `sector:` entity is
not one) and its property is one of `obj.off`, `obj.destroyed`, `npc.cell`, `npc.map`, `npc.home`, `npc.faction`, `follow.pc`, `item.dropped`, `object.damage`, `object.condition`, `lock.flags`, `lock.frame`,
`lock.kind`, `trap.state`. Every other change stays in its map as before: the sector changes, the changes of static objects (MIG7b), and the script attachments `dialog.lf`, `dialog.counters`,
`dialog.script` and `use.*` (MIG8, done below).

- **Reads and writes keep their names.** `QueryMapChange`, `QueryMapChanges`, `SetMapChange`, `ClearMapChange` and `ApplyChanges` take the same arguments and answer as before for every caller: a read, a removal or a
  clear that names a map the record is not on finds none, a write that names another map **moves the record** there (the old model kept a second, independent value; no code wrote one key under two maps).
  The budgets count each record once, against the map it names and the session. The new reads are by identity: `QueryObjectState(entity)` (the entry's fields, each with its map) and
  `QueryEntityChanges` for an object field (one answer, whatever map).
- **Snapshot (schema 62).** The maps section no longer holds the object fields: they are the rows of `objects.state` (entity, property, map, value; sorted by entity then property, ordinal). A restore refuses an
  object field left in the maps section (`InvalidSnapshot`), a row whose entity is not an `oid:` object or whose property is not an object field (`InvalidChangeKey`) and an over-budget load
  (`BudgetExceeded`); decode refuses rows out of order (`NotCanonical`). The step from 61 moves every such change out of the maps, drops a map left empty, and for a key under two maps keeps the lowest map's value.
- **Not done here.** The viewer's consumers still read through `QueryMapChange` with the map they always used (the home map); moving them to `QueryObjectState` waits for the slices that change
  what they read. `QueryObject` does not carry the fields (the directory's entry shape stays that of MIG6). Statics, NPC positions, links and the script attachments are MIG7b, MIG7c, MIG7d and MIG8.
  GR5 stays open for the static class.

## MIG7b: what is built

The static objects of a map, the doors, containers, traps and scenery that a sector file lists without an id of their own, carry the positional entity `sector:S:I` (the sector and the object's ordinal in it). Two maps can hold the
same sector coordinates and ordinals, so the entity alone does not name one object; **the identity of a static object is the map and the entity together**, and a static never leaves its map. MIG7a left their changes
under their map on purpose; MIG7b keeps them in the same store as the fields of mobile objects, by that map-qualified identity.

- **Vocabulary.** `ObjectStateVocabulary` tells the two classes apart: a mobile object (`oid:`) has the 14 fields of MIG7a and one record per entity and property wherever it stands; a static object (`sector:`) has
  `obj.off`, `obj.destroyed`, `object.damage`, `object.condition`, `lock.flags`, `lock.frame`, `lock.kind` and `trap.state` (`StaticProperties`: a static is no NPC, item or follower), and one record per **map**, entity and property.
  A static's record is written, read and removed on the map the caller names and never moves (a write on another map is another object's record), so `SetMapChange`'s move rule and the move-aware budget walks apply to the mobile fields only.
  Every other change of a static and the sector-scoped `tile.N.*` keys stay per map (the script attachments `use.*` joined the fields in MIG8, ARC-DEV-096, schema 66).
- **Reads and writes keep their names.** `QueryMapChange`, `QueryMapChanges`, `SetMapChange`, `ClearMapChange` and `ApplyChanges` answer as before for every caller (the viewer's use scripts, traps, locks and object attacks read and write
  a static through its map and `sector:S:I`); the budgets count each static record once against its own map and the session. `QueryObjectState(map, entity)` is the new read by the map-qualified identity;
  `QueryObjectState(entity)` of a `sector:` entity answers the records of every map that holds one (each names its map), and `QueryEntityChanges` lists them in map order.
- **Snapshot (schema 63).** The rows of `objects.state` are sorted by entity, property, then map, so a static's records on different maps are distinct rows (a repeated map, entity and property is `NotCanonical`); a restore refuses a static
  field left in the maps section (`InvalidSnapshot`), a `sector:` row whose property is not a static field (`InvalidChangeKey`), a second record of one map, entity and property (`InvalidSnapshot`) and an over-budget load (`BudgetExceeded`).
  The step from 62 moves every such change out of the maps with the map it was under, merges it into the rows already there in canonical order, and drops a map left empty; a second record of one map, entity and property is not moved and the
  restore refuses it. The step does not repair damage: a state row that is unreadable, a `sector:` row or rows out of order in the schema 62 text are `NotCanonical` (or `WrongType`), whether or not anything moves. The 61 to 62 step now reads a frozen copy of the schema 62 vocabulary, so it still moves mobile objects only.
- **Not done here.** The viewer's consumers still read through `QueryMapChange` with the map they always used (the static's own map, which now is also its identity); NPC positions (MIG7c), links as `ObjectId` fields and the GQ7 B switch (MIG7d)
  and the script attachments (MIG8) are other slices. GR5 stays open for NPC positions and the attachments.

## MIG7c: what is built

Where a map NPC stands was run-local state: the AI host's movers (`AreaAiMovers`) held the cell and facing of every NPC the host had walked or turned, and a quick load, a map change or a window move built the host again
with no movers, so every NPC stood on its map data's cell (GR10, bugs B06 and B10 of the [smoke plan](../research/open-bugs-migration-smoke-plan.md)). MIG7c makes the place **object state**: one more field of the mobile
object's entry, in the store MIG7a built.

- **The field.** `npc.pos` (`SessionNpcPlacement.Property`), a mobile object field of `ObjectStateVocabulary`: the world cell and the facing packed into one value (the packing of a companion's `npc.cell`, now one
  `SessionNpcPlacement.Encode/Decode` that `AreaCompanionPlacement` delegates to), kept on the map whose data lists the NPC. It is **not** `npc.cell`: that field names the companions the roster stands on a map
  (`AreaVisitors.Here` lists every `npc.cell` of a map that is no template of its own scene), and a walking resident is no companion. An NPC with no record stands on its map data's cell.
- **Session API.** `RecordNpcPlace(expected, map, entity, cell, facing)` (one revision; the same place again is no change; a cell or facing outside the packing, a bad entity or one that is not a mobile `oid:` object is `InvalidChangeKey`),
  `QueryNpcPlace(entity)` (the place with the map it names) and `QueryNpcPlaces(map)` (by entity, ordinal). `QueryObjectState(entity)` lists it among the entry's fields; the budgets count it like any change.
- **Who writes it.** The AI host, at the two places it moves a map NPC's mover: `BeginStep` (the mover takes the next cell at once, so the cell recorded is the one the NPC walks to) and the turn toward a cell
  (`FaceNpc`). A companion's step goes through the roster's own walk and writes `npc.cell` as before. A refused write only leaves that place unsaved (the host's trace says so).
- **Who reads it.** A host built for a map (`AreaAiHost` constructor and `Reset`) restores a mover, with no step under way, for each NPC it runs that has a place on that map (not one switched off or destroyed, and not a place outside the chart, not admitted or on the hero's cell: that NPC stands on its map data's cell and the host's trace says so): after a quick load, a map change or a
  window move the NPC stands where it was left. The NPC's own T0 record stays the map data's, so the occupancy and the drawing of the moved body are those of a mover that walked there in this run.
- **Snapshot (schema 64).** The place is a row of `objects.state` (`oid:` entity, `npc.pos`, map, value), sorted with the others; a restore refuses it left in the maps section (`InvalidSnapshot`) or on a static
  entity (`InvalidChangeKey`). No build before wrote the field, so the step from 63 moves nothing (it checks that the objects state is there, as the older schema carried it).
- **Smoke.** The `ai` stage checks, after the fighter walked, that the session holds the cell and facing of its mover and that a host built again over the same session and scene stands it there. A stage that
  starts from the map data's cells (`ai`, `ai-turn-based`, `ai-cast`, `ai-follower`) forgets the places the stage before it left, since the stages of one run share a session.
- **Not done here.** The place is written when the host moves the NPC; nothing else moves a map NPC yet (a script teleport, the waypoint re-placement of the fold, an NPC thrown or knocked back), and an NPC outside
  the shown window is not walked (MIG9). An NPC that joins the party starts from its T0 cell, not from the place it was left on (the roster's `npc.cell` is its own record), and its `npc.pos` stays on the map as a stale record (the host ignores it while the NPC is a companion); an NPC that leaves for another map
  keeps a place on the map it left (MIG11). The goal run, its animation and the AI tick are not saved with the place (GR18, MIG14), so after a load an NPC stands where it was and chooses its next goal afresh. The
  links as `ObjectId` fields and the GQ7 B switch are MIG7d (done, below); GR5 stays open for the script attachments (MIG8).

## MIG7d: what is built

The NPC's links to other objects (the combat focus, who hit it last, who it flees from, its enemy list) were handles inside the danger records, which carried no
map and no rule for a target that had left. MIG7d makes them **link fields of the holder's directory entry** (TM10), each naming the map it was written on, and
builds the owner's GQ7 B choice for GR36 on that map.

- **The fields.** `ObjectLinkVocabulary` (`GameSessionObjectLinks.cs`): `link.focus` (field 0x11B), `link.fleeing` (0xE6), `link.hitby` (0x11C) hold one target, `link.enemies` (0x130) holds its
  entries in the original's order (appended at the end, a removal fills the hole with the last entry), at least one and without repeat. A target is the object's identity, the entity, or the hero;
  it never names a position or a map. `QueryObjectLinks(entity)` answers a holder's fields, each a `SessionObjectLink` (holder, field, map, targets), in field order (ordinal);
  `QueryObjectLinks(map)` every field written on that map, by holder then field. The danger bits (the state word) stay in the danger domain beside them, and `QueryDanger` still composes both as a `SessionDanger`.
  The leader link of a follower is the existing object field `follow.pc` (MIG7a); the NPC leader, the summoner, the substitute inventory and the scenery and projectile links of TM10 are not modelled yet (GR31, K5).
- **The map of a holder's links.** A write of a link (the setter, the attack event's writes, the enemy list adds and removals, the follower rules) stamps the holder's links with the hero's map at that call: the fight is on the hero's map, so the holder
  is an object of it. A write that leaves the links as they are (a danger bit alone) keeps the map; a holder left with no link has none. The move of a travelling follower still clears its links before it leaves (FM6, ARC-DEV-073).
- **Snapshot (schema 65).** The links are the rows of the new `objects.links` (holder, field, map, targets; a target is the entity id or `""` for the hero), sorted by holder then field, and the `danger` section holds the bits alone, for the NPCs
  that have some (`records[].entity`, `flags`). The step from 64 moves the four handles of every danger record into rows on the map the save was on (an older session did not record the map, and the hero's map is the one the fight was on), drops a
  record that held links and no bit, and does not repair: a handle that is not text or null, a list that is not an array, a missing field and records out of order are typed failures with their paths. A restore refuses a field outside the vocabulary,
  a single-valued field without exactly one target, an enemy list that repeats an entry, a holder whose rows name two maps, rows out of order (decode), an invalid handle or map and the danger budgets (records, enemies).
- **GQ7 B, the switch.** `LifecycleRuleProfile.OpeningMapNullsLinksToLeftObjects` (registry GR36, [rule modes](../world/rule-modes.md)): the **Original** setting erases, when a map opens, the links written on it whose target
  has left; **Steamwoven** keeps the identity (GQ7 A as the default). A map opens when the hero's change of place (`ApplyChanges` with an actor on another map, after that call's edits, or `PlaceActor` on another map) lands on it, and when a snapshot is restored (the saved map opens); a restore under the Original setting therefore changes state without a revision step, so a capture right after it can differ from the snapshot at the same revision.
  A target is *there* when it is the hero, or in the group that travels with the hero (the move list's travellers, else the party, FM6, FM7: it arrives together and rebinds), or has no away record (it stands at home, taken to be the map the link was
  written on), or its away record (`npc.map`) names the map that opens. Otherwise its handle is not found and the link is erased: a single link becomes none, an enemy list loses the entry. The rule judges only the links written on the opening map.
- **Tests.** `ObjectLinkStateTests` (the fields and their maps, the round trip, the refusals, the schema 64 upgrade written by hand with its damaged variants, the switch in both modes, the travelling group, the standing-here cases, only the opening map judged,
  a load in each mode). The older danger and snapshot tests changed with the layout.
- **Limits.** The original nulls an enemy-list entry **in place** (OM30: the count stays); the session removes the entry and keeps the order, so the pick over the shorter list draws from fewer entries than the original's list with a hole
  (the pick skips an invalid entry either way; the draw start is over the count). A target's map is known only through its away record, so an NPC that went home to another map without a record is taken to be there. The
  link of a holder that is not on the hero's map when it is written (a script or an effect that names a far NPC) would be stamped with the hero's map; no caller does this today. The rule judges the links by the map they were stamped with, not by where the holder stands now: a holder that later moves (a follower carried by a change of place with no move list, which FM6 does not clear, or one that gets an away record) keeps links stamped with the map it left and is judged when that map opens, not when its present map does; MIG11's single change-of-place request is where holders are cleared and re-stamped. The session does not model the rest of TM10 (GR31); a destroyed target's links
  stay until the map is cleared, as in the original (OM29, GR30 is still open for the generic destroy).

## MIG8: what is built (slice 1, ARC-DEV-096)

The script attachments of a map-data object were keys under its map (`dialog.lf`, `dialog.counters`, `dialog.script`, `use.*` and `scr<point>.*` of the viewer's hosts), found by the map a host happened to name, and
a companion carried to another map found its dialog slot only through the home map the dialog host remembered (GR7, MIG5's `StateMap`). MIG8 slice 1 makes them **fields of the object's entry** (TM9, SA1–SA7, SA17),
in the store MIG7a and MIG7b built; the inheritance rule, the global variables and the PC's flags and variables are the later slices below.

- **Vocabulary.** `ObjectStateVocabulary.Attachment(property)`: `use.<part>` (point 1), `dialog.<part>` (point 9) and `scr<point>.<part>` for the other points 0..35, with the part one of `script`, `lf` and
  `counters` (point 31, the dialog override, is accepted by the spelling although the dispatcher runs no slot on it) (SA1: the script number, the 32 local flags and the four counters of the 12-byte record). The spelling is canonical (no `scr1.` or `scr9.`, no leading zero, no point beyond 35): one slot has one name. An
  attachment part is a field of **both** classes: of a mobile object (`oid:`, one record whatever map it names, so it **travels with the object**) and of a static one (`sector:S:I`, one record per map and entity).
  An entity that is neither (the PC, the session's own entities) keeps such keys per map as before.
- **Inheritance from the base record.** A part with no record is the loaded one: the viewer's hosts take the row's effective attachment (the prototype's, through the base record) and write a part only when it differs,
  and a part back at its loaded value is cleared. The original copies **every** point from the prototype on the first write (SA3); with the base data fixed for the whole session this gives the same reads, and the
  copy is not kept (the record budget counts the parts that differ only). What is not equal is an object created from a prototype by the session (a script's create or copy): it has no row to read the loaded slot
  from, so it starts with no attachments where the original inherits the prototype's (SA4); that stays open (GR7, next slice).
- **Reads by identity.** `QueryObjectChange(map, key)` answers a mobile field wherever its record sits (a static field and any other key as the map's, as `QueryMapChange` does), and `QueryObjectRecordMap(key)` names the map
  of a mobile record, so a host that must clear a part back to its loaded value clears it where it is. The dialog host, the use/heartbeat host, the trap view and the conversation state's stored-value check read
  the parts through `QueryObjectChange`; a write still names the map the host works on (the dialog host's state map for a carried companion), and a write on another map moves the one record.
- **Snapshot (schema 66).** The layout is the one of schema 65 (the rows of `objects.state`, sorted by entity, property, map); the rows now include the attachment parts. A restore refuses an attachment key left in the
  maps section (`InvalidSnapshot`) and an attachment on an entity that is no map object or a part that is not canonical (`InvalidChangeKey`). The step 65 → 66 moves every `dialog`, `use` and `scr<point>` part of an `oid:` or
  `sector:` entity out of the maps with the map it was under (a mobile object that held the same part under two maps keeps the lowest map's value, a known lossy choice: the older session kept an independent value per map, a carried companion's dialog slot under its home map and a heartbeat's call-ex of the dialog point under the map it stood on, and the save does not say which one the game read; a second record of one map, entity and part, mobile or static, stays for the
  restore to refuse), merges the rows into the state in canonical order and drops a map left empty. It does not repair damage: a state row that is unreadable, out of order or already an attachment part (schema 65 could
  not carry one) is a typed failure whether or not anything moves.
- **Tests.** [ScriptAttachmentStateTests](../../tests/Arcanum.Session.Tests/ScriptAttachmentStateTests.cs) (vocabulary, identity reads, the move of a mobile record, statics per map, budgets, save and load, the
  hand-written schema 65 text and its damaged variants); [AreaVisitorTalkTests](../../tests/Arcanum.TerrainViewer.Tests/AreaVisitorTalkTests.cs) (a carried companion's dialog slot is the object's wherever a
  script wrote it).
- **Not done here (the next slices of MIG8).** The inheritance of a created object (SA4, GR7), the writeback semantics of SA8–SA11 (GR28), the scripts that start goals (GR27) and **GR29** (slices 3 and 4, below): the 2000 global variables, the
  PC's flags and variables (fields 272 and 273 of the hero) that the admission refused. The tile and sector script slots (`tile.N.*`) stay per sector (TM5, SA18, SA19). GR5 stays open for what the entry does not carry yet
  (the items' scripts by entity, S12).

## MIG8 slice 2: what is built (ARC-DEV-097)

A created object owns no script array: the original's create allocates zeroed own and modified bitmaps and never touches field 32, so the new object reads its prototype's array until its first write (SA2, SA4), and that
first write deep-copies **every** point of the prototype's array onto the object before storing the one element (SA3). Slice 1 left this open because the loaded base data was equal to the object's reads; a
created object has no loaded row.

- **What the session creates from a prototype.** Only the made items (`made:n`: a script's create object, an assembly product, a split or given stack; the one registration `CreateMadeItem`, whose `SessionItemKind` names
  the prototype). The session creates no other object from a prototype (no NPC, container or portal; junk piles and pools of blood have no script), so the mobile `oid:` objects of the vocabulary are all map data and need no inheritance.
- **The prototype's array.** `CombatObjectFields.ScriptAttachments` reads every element of the prototype's field 31 (SA1: flags, counters, script number, the script-0 elements included) next to `ScriptPoints`.
  The host hands them to the session by prototype with `GameSessionOwner.AttachItemScripts` (`SessionPrototypeScript`; not state, never saved, the host attaches it again after a restore, the way `AttachItemWeights` is);
  `AreaPrototypeItems` supplies them from the prototype files and `AreaUseScripts` attaches them with the weights when a script creates an item.
- **Reads.** `QueryItemScriptSlot(entity, point)` and the item script runs (`SessionFlagReads.Slot`) answer a kept slot first, else, for a made item that has no kept slot at all, the prototype's element; a point the prototype has no
  element at reads none (SA1: an absent index reads as no script). An array the store could not keep (a point outside 0..35 or twice, the dialog-override point 31, a script number outside 0..99999) counts as none, whole.
- **The first write.** A run that changes a made item's slot (a wield-on script counting into a counter) commits the whole prototype array onto the item's kept slots, then the written points over it (`ItemScriptRuns.Writes`,
  also what the slot budget counts, planned once in `Prepare` so the commit asks the host nothing; an all-zero element is not kept, it reads as an absent one); from then on the item owns its array and a different prototype source no longer matters. The kept slots are the existing `inventory.scriptSlots` rows (the item scripts' S12 store): **no schema change**.
- **Tests.** [PrototypeScriptInheritanceTests](../../tests/Arcanum.Session.Tests/PrototypeScriptInheritanceTests.cs) (inherited reads, invalid arrays, the first-write copy through a worn ring, save and restore, destroy, a restored session with and without a source),
  [CombatFieldReaderTests](../../tests/Arcanum.Data.Tests/Combat/CombatFieldReaderTests.cs) (the array as 12-byte elements).
- **The source is attached wherever the viewer builds or restores a session (ARC-DEV-099).** `AttachAreaPrototypeScripts` (viewer, `TerrainViewerGroundItems`) hands the session a source that reads the module's
  prototype when asked (`AreaPrototypeItems.Item`, opened on first use and kept), after the session is built or carried (`TerrainViewerAreaTransition`) and in `ReplaceAreaSession` (quick load and every restore),
  so a made item inherits whether or not a script has created an item yet, and after a save load, as the original does for every created object. The save carries no source (it is the host's data), so a session
  restored by other code (a test, a tool) has none until the host attaches one (`HasItemScriptSource` says); in it a made item has no attachment to inherit, and a first write copies nothing but the written point.
  The use of a script's create still attaches the weights and the same source, which stays harmless. A session without the module's content (the self-test, a module whose stack does not open) answers none, the
  older behaviour. The inheritance reaches the item script events (equipment moves, transfers, drops) of a made item while a source is attached. The session makes no copy of an
  object (the second half of SA4). The use script of a made item on the ground is run by no host. GR29 (global variables, the PC's flags and variables) is slices 3 and 4, below.

## MIG8 slice 3: what is built (ARC-DEV-098)

GR29: the admission refused the 2000 global variables and the PC's flags and variables because the session had nowhere to keep them. Slice 3 adds the stores and their save, **not** the interpreter's use of them.

- **Stores.** `GameSessionScriptStores.cs`: `ScriptStores` (sparse, a zero is not kept) holds the global variables (0..1999, SA13), the hero's flag array (2,048 32-bit words, flag *n* = bit *n* mod 32 of word *n* / 32) and the hero's variable array (0..65535, SA14). The global flags stay in the `GlobalFlagOwner` (S2, already 0..3199 for a new game) and the story number in the story domain (S5); with the variables they are the three module blocks of PS9.
- **API.** `QueryGlobalVariable`/`SetGlobalVariable`, `QueryPcVariable`/`SetPcVariable`, `QueryPcFlag`/`SetPcFlag`, `QueryScriptState`, versioned like every session write. An index outside a store is `InvalidScriptIndex` (the original reads past its array; the project does not). The accessors act on a PC only (SA14): a session without a hero reads 0 and refuses a PC write with `PcScriptsUnavailable`.
- **Save.** Schema 67 adds the root section `scripts` (`globalVariables`, `pcFlags`, `pcVariables`, each the non-zero cells as `[index, value]` pairs ascending); the step 66 → 67 adds it empty (an older session had no store, so every value was zero). `Restore` refuses a PC store in a save with no hero (`InvalidSnapshot`); the decode refuses an out-of-range, repeated, unsorted or zero cell.
- **Where the PC arrays live.** The gap review's option B: a session domain beside the hero, one schema step. Since ARC-DEV-266 (schema 79) they are on the hero's record (below); nothing reads them by identity yet, and the session has one PC.
- **Not done here.** The interpreter and the admission sets (slice 4, below): value kinds 1, 5, 6 (`ScrValueKind.GlobalVariable`, `PcVariable`, `PcFlag`) stayed unadmitted, `IScrHost` had no member for them and a run no writeback record (all done in slice 4, below); the dialog conditions and actions over PC flags and variables (0x4150D0, 0x415BA0) likewise. The prototype source at session build and load is ARC-DEV-099 (above).
- [Tests](../../tests/Arcanum.Session.Tests/ScriptStoreStateTests.cs).

## MIG8 slice 4: what is built (ARC-DEV-100)

GR29, the other half: a run reads and writes the stores slice 3 added. Read in the executable first (ledger [GV1–GV4](../research/ledger/scripts.md#script-stores-arc-dev-100)).

- **Admission.** Set S21 is S20 plus the three value kinds (`ScrAdmissionSet.StoreValueKinds`); a PC operand packs a focus kind in its high 16 bits, which admission checks against the set's focus kinds. No schema change.
- **Interpreter.** A global variable is an unmasked 32-bit word (index 0..1999); a PC variable or flag is read and written for the first object of the operand's focus kind when it is a PC, else reads 0 and ignores the write (SA14); a flag write spills into its 32-flag word under the Original setting as the global flag's does (`GlobalFlagWriteSpills`). The run keeps one overlay (shared with called scripts), reads its own writes first and hands the changed values back as `ScrEffects.GlobalVariables`, `PcVariables`, `PcFlags`.
- **Host.** `IScrStoreQueries` (`GlobalVariable`, `PcVariable`, `PcFlag`), checked with the set; `GameSessionOwner.ApplyScriptStores(effects)` applies a run's changes as versioned writes (a session without a hero ignores the PC's). The viewer's dialog host (`AreaScriptHost`) and use host (`AreaUseScriptHost`: use, tile, sector, heartbeat sets) answer from the session and apply with the other effects; Dernholm's heartbeat 1015 is admitted.
- **Not done here.** The item events and the DLG conditions and actions over PC flags and variables (both ARC-DEV-152, below), a second PC (the interface takes an index alone), and moving the PC arrays onto the hero's directory entry.
- [Tests](../../tests/Arcanum.Scripting.Tests/Scr/ScrStoreTests.cs), [session](../../tests/Arcanum.Session.Tests/ScriptStoreApplyTests.cs), [viewer](../../tests/Arcanum.TerrainViewer.Tests/AreaScriptStoreTests.cs).

## MIG8 leftover: item events and DLG stores (ARC-DEV-152)

What slice 4 left open, read in the executable first (ledger [DG1–DG4](../research/ledger/scripts.md#dialog-script-stores-arc-dev-152)). No schema change.

- **Item events.** `SessionItemScripts.Admission` adds the value kinds 1, 5 and 6. The session's run host (`SessionFlagReads`) answers `IScrStoreQueries`, the earlier runs' pending writes first; `ItemScriptRuns` collects each run's store writes beside its global flags and the commit writes them with the move (one revision; a declined move keeps them). A session without a hero drops the PC's writes (SA14).
- **DLG.** The conversation grammar admits `GV i v`, `PV i v` and `PF i v` (conditions: the cell equals v; actions of either side: the cell takes v) when the ports carry an `IDialogScriptStorePort`; `SessionDialogScriptStorePort` binds it to the session (without a hero the PC cells read 0 and a PC write is ignored, SA14) and the viewer always passes it. PF admits only 0 and 1; a global index outside 0..1999 or a negative PC index is invalid, a PC index above 65535 outside the subset. The installed dialogs use GV only (1285 operations in 32 files), no PV or PF.
- **Not done here.** A second PC (ARC-DEV-265, below) and the PC arrays on the hero's record (ARC-DEV-266).
- [Dialog](../../tests/Arcanum.Scripting.Tests/Dialog/DialogScriptStoreTests.cs), [census](../../tests/Arcanum.Scripting.Tests/Dialog/DialogScriptStoreRealDataTests.cs), [port](../../tests/Arcanum.Session.Tests/DialogScriptStorePortTests.cs), [item events](../../tests/Arcanum.Session.Tests/ItemEventScriptStoreTests.cs).

## MIG8 leftover: the PC cells by their PC (ARC-DEV-265)

The interface took an index alone, so a second PC would have shared the hero's arrays; in the original they are fields 272 and 273 of each PC object (SA14). No schema change.

- **Interpreter.** `IScrStoreQueries.PcVariable(pc, index)` and `PcFlag(pc, index)` name the PC; the run's overlay keys a cell by its PC and index, and `ScrEffects.PcVariables` and `PcFlags` are `ScrPcVariableWrite` and `ScrPcFlagWrite` with the PC, ordered by the PC and then the index (`ScrPcOrder`).
- **Session and hosts.** The session holds the hero's arrays alone (`SessionScriptLimits.IsHero`: the script object `pc` on any map). The dialog and use hosts and the item events answer 0 for another PC; `ApplyScriptStores` and the item events' commit drop its writes. Only a network game has another PC (FR-NET, after the alpha), so nothing reachable changes.
- **Not done here.** The PC arrays on the hero's record (ARC-DEV-266, below).
- [Interpreter](../../tests/Arcanum.Scripting.Tests/Scr/ScrStoreTests.cs), [session](../../tests/Arcanum.Session.Tests/ScriptStoreApplyTests.cs).

## MIG8 leftover: the PC arrays on the hero's record (ARC-DEV-266)

Slice 3 kept the PC's arrays in a session domain beside the hero (the gap review's option B) and saved them in the root `scripts` section; in the original they are fields 272 and 273 of the PC object (SA14), saved with it. Schema 79.

- **Session.** The hero (`HeroDomain.PcFlags`, `PcVariables`) owns the arrays; `ScriptStores` keeps the global variables alone. A session without a hero has no arrays to read or write, structurally (it read 0 and refused a write before, and still does). The API (`QueryPcVariable`, `SetPcFlag`, `QueryScriptState`, `ApplyScriptStores`) is unchanged.
- **Save.** The `hero` section ends with `scripts` (`flags`, `variables`, the non-zero cells as `[index, value]` pairs ascending, the same bounds and canonical spelling); the root `scripts` holds `globalVariables` alone. The step 78 → 79 moves both lists unchanged; a schema 78 save without a hero that held a PC cell (refused at its load before) is refused by the step, and the encoder refuses a hand-built snapshot without a hero that holds one.
- **Why the hero's record and not a directory entry.** The hero has no entry in the object directory yet (TM14, GR33); its record is the `hero` section and `HeroDomain`. When the hero becomes a directory entry, its record moves as a whole and the arrays with it.
- [Tests](../../tests/Arcanum.Session.Tests/ScriptStoreStateTests.cs).

## Not ready

- With the town seed `Ready` the combat and inventory stages of the map smoke run in the town window for the first time (before, the refused seed left no combat host and they
  skipped silently). Their fixtures were written for the Crash Site and failed there; ARC-DEV-089 adapted the smoke checks (no game change): the `town` step of the full chain
  passes, see [the open-bugs re-smoke](../research/open-bugs-resmoke-2026-11-04.md#follow-ups-this-result-points-to-not-started).
- Except the lists of NPCs and containers (MIG6d-1), the hero's lists, placed items and given records (MIG6d-2) and the junk piles and pools (MIG6d-3), the stores are still per kind (the ammunition units, the weight facts and the taken mark) and the directory is derived from them on each query: a consumer that wants "where is X" can ask now, but nothing else is moved. A holder whose map has no seed attached (a map the run never read, or one the session refused) is still unknown; the loot of a dead NPC and a container
  of another map, and the barter with them, still look at the actor's map only (MIG6e moved the negative `IN` and the steal of an NPC list). Both paths are internal to the session; the public per-kind methods (`PlaceHeroItem`, `LeaveBloodPool`, `AddItemToNpc`, ...) keep their signatures and results.
- The destroy script of point 2 (which may refuse), an NPC's leave of its leader, the drop of an invulnerable item of a destroyed holder and the spell runs that name the object
  are not part of `DestroyObject`: it destroys items, piles and pools only (TM4's NPC, projectile, summon and trap kinds have no store in the session yet). Created items and map items still use different stores, so GR4 and GR17 are closed only for the kinds the session has (see MIG6d-3).
- An NPC's worn items (`NpcWorn`) and an item's slot inside a holder are not part of an entry; a given kit or taken item is `NpcList` with its
  entity unchanged.
- A queried item of a map that is not attached, and not taken or placed by the session, answers null (it is not a destroyed object); so does an item marked taken that no list holds, which no move of the session leaves.
- Whether B01 (items turning into other models after a window move) comes from per-kind keys is an inference of the [smoke plan](../research/open-bugs-migration-smoke-plan.md),
  not confirmed; MIG6a removes one possible cause (a reused junk or pool id) and nothing else of it. B02 is MIG6b's; B04 is MIG6c's: the one destroy closes the paths that left a pile with one item or none standing, whose reported case (an emptied pile staying after Take all, window still open) is the window's own rule (JP5) that `CloseJunkPile` ends, so the `junk` stage on the ruins decides whether the player's report is gone.
