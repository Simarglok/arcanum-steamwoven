# Object directory (K13, MIG6)

Status: **slices MIG6a (ARC-DEV-084) and MIG6b (ARC-DEV-085) implemented** (2026-11-04). Concept document K13 of the
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
| MIG6c | One create and one destroy path: the creators of `made:n` items, piles and pools call `Create`; every deletion goes through one `Destroy` that also dissolves a pile left with one item or none (today `Delete` does not, and only the decay step dissolves). | Planned |
| MIG6d | The stores become entries: the directory holds the entry (origin, location) and the stores keep only what they alone know (kind facts, ammunition units, pile slots); snapshot sections move with a schema step each. | Planned |
| MIG6e | Cross-map holders: an item moves between a holder of another map (read through the [record resolver](../world/record-resolver.md)) and the hero's list; the negative `IN` of a carried companion whose holder is on its home map stops being `UnknownHolder` (the [MIG5 limit](../world/record-resolver.md#one-dialog-host-arc-dev-081-mig5)). | Planned |

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
limit MIG6b, MIG6d and MIG6e remove).

Places: `Destroyed`, `HeroPack`, `HeroWorn`, `NpcList`, `ContainerList`, `Ground`, `JunkPile` (an item lying in a pile; the entry names the pile and its cell),
`SeedHolder` (still in the list its map data names, neither taken nor dropped). A pool or a pile is itself `Ground`; a decayed pool is `Destroyed`.

## MIG6b: what is built

The cause of B02 was not the one this table first named (a holder outside the window): in the town window the weapon 2345 (inventory location 1004, held flag set)
names as its parent an NPC of the same window whose list does not hold it, so the holder is loaded and the data is inconsistent. The original finds a holder's
items through its list and never lists a held item in a sector (RL3, RL10), so the item is inert there. `AreaInventoryReader` now keeps such an item in
`AreaInventory.Detached` and out of `Items`; `AreaInventoryMap.Detached` carries the entities and the seed builds (`Ready`, the viewer's detail names the count).
Nothing is created for it: `QueryObject` of a detached entity answers null (a map object the session never moved and that no seed row names), which is the existing
limit MIG6d removes. Only this window is known to hold such an item (the town window, sector 1409,1314; the Crash Site and cave seeds were already `Ready` without one). The rule is general, so another
window or map with one builds too; where the parent is outside the window the item is only left out of the view, which MIG6e (cross-map holders) settles.

## Not ready

- The stores are still per kind and the directory is derived from them on each query: a consumer that wants "where is X" can ask now, but nothing is moved
  (MIG6c–MIG6e). Created items and map items still use different stores, so GR4 and GR17 stay open.
- An NPC's worn items (`NpcWorn`) and an item's slot inside a holder are not part of an entry; a given kit or taken item is `NpcList` with its
  entity unchanged.
- A queried item of a map that is not attached, and not taken or placed by the session, answers null (it is not a destroyed object); so does an item marked taken that no list holds, which no move of the session leaves.
- Whether B01 (items turning into other models after a window move) comes from per-kind keys is an inference of the [smoke plan](../research/open-bugs-migration-smoke-plan.md),
  not confirmed; MIG6a removes one possible cause (a reused junk or pool id) and nothing else of it. B02 is MIG6b's; B04 is MIG6c's.
