# Object directory (K13, MIG6, MIG7)

Status: **slices MIG6a (ARC-DEV-084), MIG6b (ARC-DEV-085), MIG6c (ARC-DEV-086), MIG6d-1 (ARC-DEV-088), MIG6d-2 (ARC-DEV-090), MIG6d-3 (ARC-DEV-091) and MIG6e (ARC-DEV-087) implemented, and MIG7a (ARC-DEV-092) and MIG7b (ARC-DEV-093) of MIG7** (2026-11-04). Concept document K13 of the
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
`dialog.script` and `use.*` (MIG8).

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
  Every other change of a static, the script attachments `use.*` and the sector-scoped `tile.N.*` keys, stays per map (MIG8 for the attachments).
- **Reads and writes keep their names.** `QueryMapChange`, `QueryMapChanges`, `SetMapChange`, `ClearMapChange` and `ApplyChanges` answer as before for every caller (the viewer's use scripts, traps, locks and object attacks read and write
  a static through its map and `sector:S:I`); the budgets count each static record once against its own map and the session. `QueryObjectState(map, entity)` is the new read by the map-qualified identity;
  `QueryObjectState(entity)` of a `sector:` entity answers the records of every map that holds one (each names its map), and `QueryEntityChanges` lists them in map order.
- **Snapshot (schema 63).** The rows of `objects.state` are sorted by entity, property, then map, so a static's records on different maps are distinct rows (a repeated map, entity and property is `NotCanonical`); a restore refuses a static
  field left in the maps section (`InvalidSnapshot`), a `sector:` row whose property is not a static field (`InvalidChangeKey`), a second record of one map, entity and property (`InvalidSnapshot`) and an over-budget load (`BudgetExceeded`).
  The step from 62 moves every such change out of the maps with the map it was under, merges it into the rows already there in canonical order, and drops a map left empty; a second record of one map, entity and property is not moved and the
  restore refuses it. The 61 to 62 step now reads a frozen copy of the schema 62 vocabulary, so it still moves mobile objects only.
- **Not done here.** The viewer's consumers still read through `QueryMapChange` with the map they always used (the static's own map, which now is also its identity); NPC positions (MIG7c), links as `ObjectId` fields and the GQ7 B switch (MIG7d)
  and the script attachments (MIG8) are other slices. GR5 stays open for NPC positions and the attachments.

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
