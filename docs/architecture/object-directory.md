# Object directory (K13, MIG6)

Status: **slices MIG6a (ARC-DEV-084), MIG6b (ARC-DEV-085) and MIG6c (ARC-DEV-086) implemented** (2026-11-04). Concept document K13 of the
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
item's last use, the restock's old stacks, a schematic and the merge of an ammunition stack all end in it, so no path leaves a pile with one item or none standing (before,
only the decay step dissolved). The shown-window state is read from the event world of the pass the destroy runs in (the decay); outside a pass the session knows none and the
pile is checked at once (the host's window state is not session state).

## Not ready

- With the town seed `Ready` the combat and inventory stages of the map smoke run in the town window for the first time (before, the refused seed left no combat host and they
  skipped silently). `cast`, `ai`, `turn-based`, `ai-cast`, `ai-follower`, `wreck` and `altar` fail there on fixtures written for the Crash Site (target not visible, no cell next to
  the NPC, the companion template), so the `town` step of the full chain stops at the first of them; `transfer` (follow, carry, visitors) passes. A follow-up task, ARC-DEV-089, adapts them (owner decision, 2026-11-04).
- The stores are still per kind and the directory is derived from them on each query: a consumer that wants "where is X" can ask now, but nothing is moved
  (MIG6d, MIG6e). Both paths are internal to the session; the public per-kind methods (`PlaceHeroItem`, `LeaveBloodPool`, `AddItemToNpc`, ...) keep their signatures and results.
- The destroy script of point 2 (which may refuse), an NPC's leave of its leader, the drop of an invulnerable item of a destroyed holder and the spell runs that name the object
  are not part of `DestroyObject`: it destroys items, piles and pools only (TM4's NPC, projectile, summon and trap kinds have no store in the session yet). Created items and map items still use different stores, so GR4 and GR17 stay open.
- An NPC's worn items (`NpcWorn`) and an item's slot inside a holder are not part of an entry; a given kit or taken item is `NpcList` with its
  entity unchanged.
- A queried item of a map that is not attached, and not taken or placed by the session, answers null (it is not a destroyed object); so does an item marked taken that no list holds, which no move of the session leaves.
- Whether B01 (items turning into other models after a window move) comes from per-kind keys is an inference of the [smoke plan](../research/open-bugs-migration-smoke-plan.md),
  not confirmed; MIG6a removes one possible cause (a reused junk or pool id) and nothing else of it. B02 is MIG6b's; B04 is MIG6c's: the one destroy closes the paths that left a pile with one item or none standing, whose reported case (an emptied pile staying after Take all, window still open) is the window's own rule (JP5) that `CloseJunkPile` ends, so the `junk` stage on the ruins decides whether the player's report is gone.
