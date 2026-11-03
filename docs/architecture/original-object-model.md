# The original's object model: identity, fields, prototypes and links

Status: **Research**, work item ARC-RES-078 (2026-11-03), slice R1 of the
[architecture audit](../research/architecture-audit-plan.md) (spines SP1 and SP7).
Docs-only: no code changes and no checks run. It is a reference model of the
original game, not a comparison: the "Steamwoven today" column notes only the obvious
differences; the full comparison, its classes and the target model are slice G1's.

Requirements (reference only, no coverage row changes):
[FR-WORLD-005](../requirements/world.md#fr-world-005),
[FR-WORLD-013](../requirements/world.md#fr-world-013),
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

Earlier results reused by reference, not re-read: the follower move between maps
and sectors ([FM1–FM14](../research/follower-map-transfer.md#1-the-original-mechanism)),
the sector cache ([SS1–SS4](../research/main-map-streaming.md#how-the-game-keeps-sectors)),
the event queue's save and map scope ([P1–P7](../research/game-event-queue.md)),
links rebound at map open ([OL31](../research/ledger/social.md#companion-orders-and-loyalty-arc-res-064)) and NPC inventory
across maps ([NI1–NI7](../research/ledger/items.md#npc-inventory-changes-arc-world-055)).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed for this reading), read statically with `llvm-objdump`
per the [original executable check](../research/original-executable-check.md), by two
readers that each saw only the questions and the starting addresses from the
project's records (method step 6). Object fields are named from the executable's
own field name table (0x59BEA8, 338 entries including the closing `obj_f_max`), object, container and NPC flags from
its flag name tables (OF_ at 0x5BA140, OCOF_, ONF_). The direct branches of the
functions read stay inside `.text`; indirect calls go to jump tables, hook
pointers and the `CoCreateGuid` import, none to `.IIDKing`. A row at **F2** has two
readings that agree; **F1** has one reading; a disagreement is written in the row.
Everything is read from the executable unless marked **(inference)**. Steamwoven
facts come from the [audit plan](../research/architecture-audit-plan.md#layer-0-the-engine-spines)
on `origin/main` 384152a4.

## Answer

- **Two names for one object.** In memory an object is addressed by a 64-bit
  *handle* (a pool slot with a serial number); across saves and maps by a 24-byte
  persistent *ID* (a GUID, a positional ID, or a prototype number). One sorted
  registry maps IDs to handles; it is cleared at every map change except for the
  prototypes.
- **Instances store only what differs from their prototype.** Every object has a
  prototype; a field the object has never written is read through the prototype at
  every read. Creation copies almost nothing (type, prototype, location, an ID),
  so a prototype change reaches every instance that has not overridden the field.
- **One field table for all kinds.** 337 field descriptors (0–336, 42 of them section
  markers) in fixed ranges: 36 common to every object, one block per object type (items share an item block, critters
  share a critter block), and transient fields that are never saved. Ownership is
  decided by the type alone.
- **One create path, one destroy path.** Every object the game makes at run time,
  from a blood pool to a summoned creature, goes through the same creation
  (from a prototype, or as a copy of an existing object for monster generators),
  which marks it dynamic and puts it in its sector. Destruction only marks the
  object destroyed after detaching it from the world; memory is freed when the map
  is cleared.
- **Links are object fields, not side tables.** Leader, followers, inventory
  lists, item parent, combat focus, who hit me last, fleeing from, the enemy list,
  the substitute inventory and the ammunition/gold stacks are handle fields of the
  objects themselves. Apart from the destroyed object leaving its leader's follower
  list and its holder's inventory, they are not cleared when the target is destroyed; they
  become null IDs when the holder is saved, and null handles when a saved ID is not
  found at map open. A summoner is not stored on the summoned creature at all.

## 1. Identity: handles and persistent IDs

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven today |
| --- | --- | --- | --- | --- |
| OM1 | handle test 0x4E5470, handle → object 0x408720 → 0x4E4F80 (0x4E5900, 0x4E5960), release 0x408740 → 0x4E4FA0 | A handle is 64 bits: the low three bits are the tag 2, bits 3–25 a serial number, the bits from 29 up the pool slot. A handle is valid when it is null, or when its tag is 2, its slot is inside the pool, the slot's serial equals the handle's and the slot is marked live. Turning a handle into an object pointer does no check at all; the matching release is an empty function in the retail build. The handle-field getter (0x406FB0) does test the stored handle: a stale one reads as null and is overwritten with null in the holder's field. The serial counter is seeded from the clock (second reader), so handle values differ from run to run and are never saved. | F2 (seed F1) | Entities are addressed by (shown map, entity row) or by identity strings, not by a live handle (plan H1). |
| OM2 | slot allocation 0x408710 → 0x4E4E60, slot free 0x4E4FB0 → 0x4E56A0 | At most 2,097,152 objects live at once (a full pool stops the game). A new slot takes the next value of one global serial counter, which wraps from 8,388,607 back to 1; a freed slot goes to a free list. A handle kept after its object was freed fails the validity test where one is made (ID getter, lookup, the handle-field getter, the save conversion). The object lock and the field accessors make none, so a stale handle used directly reaches whatever object now holds the slot, and the free list is last in, first out (second reader), so a freed slot is reused first. A full pool ends the game with a fatal error **(inference** from the call**)**. A requested handle can also be reserved (network paths). | F2 | — |
| OM3 | ID getter 0x407EF0, GUID 0x4E62A0, positional ID 0x4E62D0, ID kind test 0x4E6340, prototype ID 0x4E6540 | An ID is 24 bytes with a kind word: 0 null, 1 prototype (the prototype number), 2 GUID (16 bytes from `CoCreateGuid`), 3 positional (the location, the object's temporary ID field 328 and the map number). Kinds outside 0..3 are invalid. Inside an object a link field holds the same 24 bytes with the kind 0xFFFE and a handle. Both readers see the prototype ID carry the code's basic number plus 20 (0x468860, 0x468890), as [PG8](../research/ledger/scripts.md#template-sector-scripts-arc-res-073) records. | F2 | NPC identity is `oid:` plus a hash of the object ID read from map data; items and other kinds have their own keys (plan SP1). |
| OM4 | create 0x4058E0, ID getter 0x407EF0, tests 0x43D940 and 0x43D990, editor flag 0x5D10F8 (map flush 0x410070) | In the game (the mode flag 0x5D10F8, the same value as 0x5E2F58, is 0; it is the editor flag, **inferred** from the map flush 0x410070) every object created at run time gets a GUID at creation. An object that has no ID yet (an object read from map data without one) gets one at the first request: a positional ID when it is a wall, portal, scenery or trap and is not dynamic (OF_DYNAMIC, 0x2000), otherwise a GUID. The new ID is registered and the object marked as having a new ID. Asked for the ID of a stale handle, the getter looks the handle up in the registry and returns the ID it last had, or null. Confirms FM4. | F2 | — |
| OM5 | registry insert 0x4E4FD0, search 0x4E57E0 (compare 0x4E6360), lookup 0x4E50E0 | One array of {ID, handle} pairs sorted by ID and binary-searched, growing by 512 entries up to 2,097,152; an insert past that is silently dropped. Inserting an ID already present only updates its handle. A lookup returns the handle when the entry exists and the handle is still valid. Otherwise only a positional ID of the current map gets a second chance: the walls, portals, scenery and traps on its tile are searched for the same temporary ID, and a match is registered and returned. Anything else gives null. The temporary ID is given in a sector's load order (second reader), so a positional ID stays resolvable only while that order is stable. | F2 (temporary ID order F1) | — |
| OM6 | free 0x405BF0, registry remove 0x4E52F0, map clear 0x411520, registry compaction 0x4E5300 | Freeing an object does not remove its registry entry (the remove function is an empty function in the retail build); the entry keeps a handle that is now stale, so a lookup of that ID gives null. When a map is cleared, the registry keeps only prototype IDs (kind 1) and drops every other entry, so IDs of objects on other maps are not resolvable while that map is not loaded; an object exists only on the loaded map or in a map's files. | F2 (compaction F1) | — |

## 2. Fields: one table, ownership by type

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven today |
| --- | --- | --- | --- | --- |
| OM7 | object system init 0x405110, descriptor fill 0x40A8A0 and 0x40A400, field name table 0x59BEA8 | A table of 337 field descriptors (28 bytes each) holds, per field, its index in a prototype's full value array, its bit in an instance's presence bitmap and its storage kind. The kinds are: block markers, 32-bit integer, 64-bit location (fields 2, 242, 289, 290), integer arrays, 64-bit arrays, the script array (32), the quest array (259), a string (270), one object handle, and a handle array. | F2 | Mutable state is a project-defined key vocabulary over immutable map rows (hypothesis H3, unconfirmed). |
| OM8 | field check 0x40C260, field walk 0x40C880 | Fields 1–36 belong to every object; fields 315–333 are transient (render state, light and shadow handles, temporary ID, internal flags), kept inline in the object and never saved; field 335 is the type and 336 the prototype handle. The rest is owned by type: wall 38–44, portal 45–54, container 55–67, scenery 68–75, projectile 76–85; the ten item types (weapon, ammunition, armour, gold, food, scroll, key, key ring, written, generic) own the item block 86–110 plus their own block; PC and NPC own the critter block 217–251 plus the PC block 252–278 or the NPC block 279–305; trap 306–312. The ranges include their section markers (the second reader's inner ranges are one shorter at each end). A field outside the object's ranges is an error and reads as 0. | F2 | Per-kind readers and session domains; no single field table. |
| OM9 | value get 0x408A20 (presence 0x40D320, slot 0x40D230, prototype handle 0x40D630) | A prototype stores every field of its type in a full array. An instance stores only the fields it has written, in a compact array indexed by a presence bitmap. Reading a field of an instance gives its own value when the bit is set, otherwise the prototype's value, found through the instance's prototype ID (looked up once and cached). Transient fields are read inline. | F2 | Base records are immutable rows of the loaded map; no prototype fallback at read time (plan SP1). |
| OM10 | field set 0x406D40 → 0x408760 (0x40D370, 0x40D400), reset 0x4080E0, difference save 0x410830 → 0x4067C0 / 0x4067F0 | Writing a field of an instance creates its own slot even when the value equals the prototype's, sets the presence bit, a second "modified" bit and the object's modified mark; a reset function returns a field to the prototype's value. The per-map difference save (`mobile.md`, FM5) walks the objects that came from map data, are not static walls or scenery and are not dynamic; each one with the modified mark is written either as an ID to `mobile.des` (destroyed or extinct) or as a difference record. The full object record (0x409980; sectors, `mobile.mdy`) does not carry the modified bitmap (second reader); the difference record (0x4067F0) does (first reader, confirmed in review). | F1 | Session domains record changes per feature (hypothesis H3, unconfirmed). |

## 3. Prototypes

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven today |
| --- | --- | --- | --- | --- |
| OM11 | prototype init 0x4681B0, check 0x468660, number ranges 0x5B37FC/0x5B384C, file name 0x468720, file exists 0x52FB80 | At start-up the game knows 20 prototype groups and, for each, a range of prototype numbers: 1,423 numbers in all, from 1000 to 28471. For every number it checks whether `proto\<ID number, 6 digits> - <group name>.pro` exists in the game's file search path; the ID number is the table number plus 20 (OM3, PG8). | F2 | Prototypes are read from the installed data only ([content precedence](../research/content-precedence.md)). |
| OM12 | build 0x468890 (group fill 0x468930), save 0x468330, record write 0x406590, file open 0x5303A0 | A missing file is not an error: the prototype is built in code (a new prototype object of the group's type, its name and its ID set, its fields filled by the group's code) and written as that file. If any file was written the object system is restarted; then every `proto\*.pro` file is read (0x406600), so the game always plays from prototype files. An installation whose search path (archives included, **inference**) has no prototype files therefore writes all 1,423 on its first run; where they land is the first writable folder of the file search path **(inference;** the folder choice of the file layer was not read**)**. This is the step behind the open fresh-install problem: a GOG installation the original never ran has no prototype files, so a reader of installed data finds none (found on Windows on 2026-10-29). | F2 (folder: inference) | No build step; a missing prototype file is missing data. Planned: the build interpreted from the player's executable ([prototype extraction](../research/prototype-extraction.md), ARC-RES-085). |
| OM13 | create 0x4058E0 (bitmaps 0x40C580) | Creating an object from a prototype copies only: the type, the prototype's ID (as the object's prototype link), the location, a new GUID, and, for an NPC, the location into the teleport destination and the day and night standpoints, plus NPC flag 0x2 (named ONF_WAYPOINTS_DAY in the executable's table). The presence bitmap starts empty, so everything else (art, name, stats, inventory source, scripts, faction, AI data) is read through the prototype until written. A prototype edited in memory therefore changes every instance that has not overridden the field. | F2 | Created objects get their own records in per-kind session stores (hypothesis H2, unconfirmed). |
| OM14 | duplicate 0x406010 (with a given ID 0x406210) | Copying an object (the "perm dup" path) copies its type, prototype link, presence bitmap and own values, gives it a new GUID when the source had an ID, and then walks a container's or critter's inventory list to copy its items as new objects too **(inference;** the per-item step was not followed**)**. | F1 | — |

## 4. Creation and destruction

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven today |
| --- | --- | --- | --- | --- |
| OM15 | create 0x43CBA0 / with an ID 0x43CBF0 → 0x442130 → 0x4058E0 / 0x405B30; network wrapper 0x4ED8B0; copy 0x43CC30 / 0x43CC70 → 0x4421A0 → 0x406010 / 0x406210 | Two front doors, one tail. Creation from a prototype (0x43CBA0, or 0x43CBF0 with a requested ID) and creation as a copy (0x43CC30) both end in the same finishing step (OM16; the copy path into it was followed by the first reader only). The network wrapper creates locally in single player, and on a host also tells the clients; a client creates nothing itself. | F2 | — |
| OM16 | finish 0x442260 | In the game the new object is marked dynamic (OF_DYNAMIC), and so is every item of its inventory list (container fields 59/60, critter fields 237/238). It is added to its sector's object list (with the sector locked), an NPC gets a normal AI tick ([P7](../research/game-event-queue.md)), its inventory is generated or restocked (0x463C60, [RK3](../research/ledger/items.md#merchant-restock-arc-world-093); the second reader read 0x463C60 as a light update, RK3 and its caller 0x464180 settle it as restock), a location notification runs (0x444270) and its screen area is redrawn. A copy is then moved to its location and an NPC copy's standpoints set to it. | F2 | — |
| OM17 | destroy 0x43CCA0 | An already destroyed object is left alone. Otherwise the object's destroy script (point 2) runs first and can refuse; then, in order: a network message; through 0x45E180 a PC goes to 0x4BA1E0 (not read) and an NPC leaves its leader (follower removal 0x4AA8C0 and 0x45E040, FM13); a PC or NPC loses its AI events (0x4AD7D0); an NPC runs an interface hook for the first PC of its reaction list (0x4C1110, 0x460A20); an item held by someone is taken out of the holder's list and moved to the holder's tile; a container or critter loses its inventory (OM18); then its animations stop (0x423FF0), its spells end (0x459740), a hook runs and it leaves its sector and lights (0x443770). A wall also destroys every other object on its tile. Last the object is flagged OF_DESTROYED. | F2 | Each store removes its own records; no shared destroy path (hypothesis H2, unconfirmed). |
| OM18 | inventory on destroy 0x4639E0 (holder, 1), drop 0x466E50 | When a container or critter is destroyed (a junk pile, prototype 3023, skips this step), a container that has not generated its contents yet (container flag 0x200) generates them first; then only items flagged OF_INVULNERABLE are taken out and dropped on the holder's tile (with the junk-pile placement, [PI3](../research/ledger/items.md#junk-piles-arc-world-063)). Every other item stays in the destroyed holder's list, neither destroyed nor flagged **(inference:** what becomes of these items at save, with a parent that turns into a null ID (OM30), was not read**)**. | F1 | — |
| OM19 | destroy 0x43CCA0, map clear 0x411520 → 0x43CFF0 → free 0x405BF0 | Destroying frees nothing: the object stays in memory, flagged, and keeps its handle valid until the map is cleared. Map clear frees every dynamic and mobile object, then the sectors free their static objects, then the registry is compacted (OM6). Objects are also freed when a sector drops its static objects (0x4F1170), when a sector load drops an extinct object, on load failures and at shutdown (0x4E4DB0). An immediate destroy-and-free (0x43CFF0) is used outside the game's normal destroy only on the branch taken when the mode flag 0x5E2F58 is 1 (0x43CEA0, the editor, OM4), through 0x4E18F0 and 0x4F0790, which also reach the four creators of OM27. On save a destroyed dynamic object is simply not written and a destroyed map-data object's ID goes to `mobile.des` (FM5). | F2 | — |

## 5. Objects created at run time

Every kind below uses the create path of OM15–OM16, so each is an ordinary dynamic
object of its map: drawn, picked, saved in `mobile.mdy`, and freed at map clear like
any other. Callers were found by the call graph of 0x43CBA0, 0x43CBF0, 0x4ED8B0 and
0x43CC30, and named from the project's earlier records. Prototype numbers below (3023, 4047, 4049, 5028) are the code's basic numbers, before the +20 of OM3.

| ID | Kind | Creator (address) | Path | Level |
| --- | --- | --- | --- | --- |
| OM20 | Dropped item, junk pile, ranged-attack projectile | drop and junk-pile placement 0x466EF0 ([PI3](../research/ledger/items.md#junk-piles-arc-world-063)), ranged attack 0x4B24F0 ([RH3](../research/ledger/combat.md#ranged-hero-attack-arc-combat-015)) | A dropped item is the same object moved to a tile; a junk pile (prototype 3023, a container) is created through the network wrapper; every ranged attack (bow, firearm, throw) creates a projectile object through the wrapper, with its parent weapon set. | F2 |
| OM21 | New stacks | gold 0x464970, ammunition 0x465A40 | A new gold or ammunition object of the base prototype carries a split count ([AM7](../research/ledger/items.md#ammunition-arc-world-066)). | F2 |
| OM22 | Made items | assembly 0x56E720 | Products are created through the wrapper (the role from the project's records; the second reader did not name this caller). | F1 |
| OM23 | Generated contents | inventory source 0x463E20, starting kit 0x460FF0, background items 0x4C26F0, player creation 0x40D860 / 0x40DB70 | Items made into an NPC, container or the PC are ordinary objects with their holder as parent. | F2 |
| OM24 | Blood pool, spell missile, trap device | blood 0x432D90 ([BP1, BP2](../research/ledger/combat.md#pool-of-blood-arc-render-041)), missile 0x4248A0, trap 0x4BC480 / 0x4BC7B0 | Created with 0x43CBA0, not the network wrapper. The missile is prototype 5028 and the pool a scenery prototype (second reader). **Disagreement** on 0x4BC480 / 0x4BC7B0: the project's records name 0x4BC480 the trap device on a tile, which creates a marker object from prototype 4047 or 4049 ([TP2](../research/ledger/technology.md#trap-devices-arc-technology-010)); the second reader read both as creating an inventory item. | F2 (trap F1) |
| OM25 | Summoned creature | summon 0x450C40 | Created from the summon prototype or a list pick; it gets critter flags 2 bits, experience worth and pool, and a faction; the spell run keeps it in its summoned list ([MT17](../research/magic-implementation-plan.md#phase-e-world-components)). A PC caster is written into the spell runs (0x4372B0, **inference** of the second reader); an NPC caster only passes on its faction (second reader). | F2 (caster F1) |
| OM26 | Generated critters | monster generator 0x4BA910 → 0x4EF920 → copy 0x43CC30 | A generator spawns **copies** of its template NPC (OM14), not prototype instances, and flags each ONF_GENERATED. | F1 |
| OM27 | World-map encounter, script create, debug | encounter 0x559550, script action 51 ([PG1](../research/ledger/scripts.md#template-sector-scripts-arc-res-073), via the wrapper), cheat key 0x401560 | Same path. Four more callers (0x4DF740, 0x4E0240, 0x4E20A0, 0x4E25B0) create through it, but they are reached only from the editor's destroy branch (OM19; call graph checked in review), so they are not game-mode network handlers; the first reader's network reading is dropped. 0x49D590 (the multiplayer map operation) uses the creation with a given ID. | F1 |

## 6. Links between objects

A link is a field of the holder (OM7): kind "handle" for a single target, kind
"handle array" for a list. In memory a handle value is tagged 0xFFFE; saved, it is
an ID. The three moments that touch links are the same for every link field:

| ID | Function (address) | Original 1.0.7.4 | Level |
| --- | --- | --- | --- |
| OM28 | descriptor kinds 0x40A8A0 | Handle fields: scenery "who is in me" (70), projectile parent weapon (80), item parent (88), fleeing from (230), the critter's gold, arrows, bullets, power cells and fuel stacks (232–236), NPC leader (281), combat focus (283), who hit me last (284), substitute inventory (294), prototype handle (336). Handle arrays: container inventory (60), critter inventory (238), followers (241), the NPC's reaction PC list (297), the enemy list (304). There is no summoner field and no wielder field: an item's holder is its parent (88); a wielded item is told apart by its inventory location (field 94) **(inference** from the field names, not read here**)**. | F2 |
| OM29 | destroy 0x43CCA0 | **Target destroyed:** no other object's link to it is cleared. Only the destroyed object's own memberships are undone: it leaves its leader's follower list, its holder's inventory list, and spell runs that name it end. A link to it from elsewhere stays a valid handle to a flagged object until the map is cleared; consumers must test OF_DESTROYED (the animation code has an assertion for it). After the free, the handle-field getter nulls it at the next read (OM1). | F2 |
| OM30 | handles → IDs 0x4064B0 (single 0x40BBF0, arrays 0x40BD20 → 0x40BF00), once per object (internal flag 332 bit 1) | **Save or move:** every handle field the object stores itself becomes an ID. A stale handle becomes the ID the registry last had for it; a target that is destroyed and not extinct (OF_EXTINCT, 0x800000) becomes a null ID, in an array as a null element in place (the count is kept). A target that left by teleport (destroyed and extinct, FM3) keeps its ID, so a link to a follower that moved maps survives the save as an ID. | F2 |
| OM31 | IDs → handles 0x406520 → 0x40BDB0 / 0x40BE70, post-load 0x410C50 | **Load or arrival:** every ID field becomes the handle the registry gives (OM5), or null when not found; an array keeps a null element in place. Confirms OL31 and FM7: a link to an object of another map becomes null at map open, and that null is saved at the next save. | F2 |
| OM32 | teleport preparation 0x4D3D60, 0x4D3E20 (FM6) | **Move (before any teleport, FM6):** combat focus, who hit me last, the enemy list, the substitute inventory and fleeing-from of the mover are cleared before it leaves; leader, followers and inventory travel with the group and rebind there (OL31). | F2 (from FM6) |

Per link, the three moments:

| Link | Field | On target destroy | On move to another map | On load |
| --- | --- | --- | --- | --- |
| Leader / followers | 281 / 241 | Destroyed follower leaves the list (OM17); a destroyed leader stays in its followers' leader field (NPC leader: no clearing found; PC leader 0x4BA1E0 not read) | Group travels together, rebinds (FM1, OL31) | Rebind; null when the other side is not on the map |
| Item parent / inventory lists | 88 / 60, 238 | Destroyed item leaves the list; destroyed holder drops only invulnerable items (OM18) | Inventory travels with its holder (FM1) | Rebind ([NI4](../research/ledger/items.md#npc-inventory-changes-arc-world-055)) |
| Gold and ammunition stacks | 232–236 | Not cleared | Travel as inventory | Rebind |
| Substitute inventory | 294 | Not cleared | Cleared (FM6) | Rebind or null |
| Combat focus, who hit me last | 283, 284 | Not cleared | Cleared (FM6) | Rebind or null |
| Enemy list | 304 | Not cleared | Cleared (FM6) | Rebind or null per element |
| Fleeing from | 230 | Not cleared | Cleared (FM6) | Rebind or null |
| Scenery "who is in me", projectile parent weapon | 70, 80 | Not cleared | — | Rebind or null |
| Summoner | none | The spell run ends on either side's destroy (0x459740) | The run travels only when caster, parent, targets travel (FM9) | Run restored from `MT.dat` (FM9) |

## Open questions

- Q1. Whether a summoned creature also joins its caster as a follower (leader field)
  was not found in the summon function by either reader; the run's summoned list and,
  for a PC caster, the run records hold the relation (OM25).
- Q2. The folder the file layer writes new prototype files to (OM12) and why a fresh
  Windows install could fail there: a reading of the file layer (0x530C70) or an
  observation on Windows is needed.
- Q3. The difference record format around the modified bitmap (OM10) belongs to slice
  R3 (persistence).
- Q4. The role of 0x4BC480 / 0x4BC7B0 (OM24), of 0x4BF210 (a written item) and of
  0x4A5710 / 0x4A5E10 / 0x4A5EE0 (multiplayer auto-equip in the project's records,
  loot at death for the second reader).
- Q5. What happens at save to the items left in a destroyed holder's list (OM18).
