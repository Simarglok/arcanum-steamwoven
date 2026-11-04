# Record resolver (ARC-DEV-072, MIG1)

Slice MIG1 of the [target model](../architecture/target-model.md#migration-order) (element TM3, companion fix V1 of the
[follower research](../research/follower-map-transfer.md#fix-plan)): the one query that answers "what is this object's base record",
by identity alone, in its final place, the Godot-free world runtime `src/Arcanum.World` (decision GQ6 A).

## Why

The original addresses an object by its identity; the object carries every field wherever it stands (FM3, FM4). Steamwoven addressed
the base record (object row, combat fields, build, seed holder, worn set) by *(shown map, entity)*, so a companion carried to a map
other than its home had no record there (gap register GR1, GR2, GR11). The resolver removes that key: callers ask for the entity.

## Parts

| Part | Role |
| --- | --- |
| `AreaInventoryMap` (moved here from the viewer, same API) | One map's session inventory seed with the object rows of its items and holders; the unit the resolver holds. |
| `RecordResolver` | Holds the maps it was given plus maps read on demand, indexes their rows by entity, and answers `Resolve(entity, home)` with a `RecordLookup`. |
| `IMapRecordSource` | Reads one map's rows by runtime number (rows only, no scene). The viewer's `AreaMapRecords` reads the game folder's module content stack; tests give synthetic maps. |
| `RecordResolverLimits` | The explicit bounds: `MaxMaps` (given plus read on demand, default 8) and `MaxRowsPerMap` (items plus holders, default 100 000). The constructor throws `ArgumentException` for more given maps or rows than that. |
| `EntityHomes` | The session side: the map that carries the entity's `npc.map` change is its home map (ARC-WORLD-017), so a travelling NPC's home is known without having loaded it (`GameSessionOwner.QueryEntityChanges`, a read only). |

## Rules

1. A held map answers first: an entity listed by a given or already read map resolves without the source.
2. Otherwise, with a home map named, the resolver reads that map once from the source, holds it, and looks the entity up in it. Without a
   named home nothing is read (`UnknownEntity`).
3. Typed failures, no exceptions at the boundary (a source that throws is the same `HomeUnread`; only a cancelled read propagates and is asked again): `UnknownEntity` (no held map, or the named home, lists it), `HomeUnread` (no source, the
   source could not read the map, or it answered another map) and `LimitExceeded` (the given maps fill `MaxMaps`, or the map has more rows
   than `MaxRowsPerMap`; such a map is never partly held).
4. A map the source could not read (or that passes a limit) is remembered with its status and not asked again in the resolver's life.
   The session's attach of a map read on demand is best effort: `AreaInventoryHost.Attach` fails only on the given maps.
5. Given maps are never dropped; maps read on demand leave least recently used first when the bound is reached.
6. The held maps are derived data: nothing here is saved, a quick load or a fresh run loses nothing, and the next question reads again.
7. A map read on demand has no seed in the session until it is first asked about; `AreaCombatHost` attaches it then (a given map's seed is
   attached with the host and again after every restore), so the session's holder entries and worn set of a companion whose home map this
   run never loaded are the same as on that map.

## In the viewer

`AreaInventoryHost(maps, records: new AreaMapRecords(arguments))` makes the resolver; `AreaCombatHost` answers by identity
(`Record`, `HomeMap`, `Fields(entity)`, `Build(entity)`, `Build(session, entity)`, `Holder(session, entity)`, `Worn(session, entity)`)
and keeps its map-keyed questions with their meaning (the rows of that map), so the consumers move one by one: carried companions in the AI
(MIG2, done: ARC-DEV-075, the AI host asks only by identity; `AreaCombatHost` gained `WornItems`, `NpcItems`, `HeldItemFields`, `NpcHolder` and
`NpcItemFields` by identity, the last two over every held map), then spell targets, hover attack, life bars, turn facts, sheet and levels, the event world and the item/armour questions
(MIG3, done: ARC-DEV-076; below), the body (MIG4) and one dialog host (MIG5).

### Consumers by identity (ARC-DEV-076, MIG3)

| Consumer | Before | Now |
| --- | --- | --- |
| Hover, click and life bar of a drawn NPC | `Target(session, shownMap, key)`: only a row of the shown map | `Target(session, key)`: an object key names its map (`AreaObjectKey.RuntimeMap`), so a visiting companion's key resolves through its home map's rows when this host holds that map (a key reads no map: the hover asks every frame); `Target(session, entity)` by identity reads the home on demand once, and the viewer asks it by entity for a carried companion whose home is not held yet |
| Life bars, follower portraits, transfer's no-fatigue test | the shown map's combat values, else the home map's (a fallback) | `AreaLife.Of` / `AreaLife.Unconscious(session, host, entity)`: no fallback |
| Turn-based facts (`AreaCombatantFacts`) | `HomeMap(entity) ?? shown map` | `Build(entity)` and `Fields(entity)` |
| Sheet, levels, scheme and crafter of a follower | `HomeMap(entity)` then `Fields(home, entity)` | `Fields(entity)` |
| Event world (`AreaEventWorld.CritterFacts`, `ObjectExists`, the restock wielder) | the shown map's rows, then `HomeMap` | `IAreaEventObjects.Facts(entity)`, `HasObject(entity)`, `Critter(entity)`: the interface has no home-map question |
| Spell targets (`AreaSpellCastMap`) | rows of the shown map only; a visiting companion was no target, no party member, no critter | the viewer gives the companions carried here (`visitors`): they are found by `IdOf`, `Object`, `ObjectsAt`, `ObjectsInSquare`, `Party` and `Critter` while the group places them on a cell here; their combat values, worn items and acid resistances come from the resolver (`Build(session, entity)`, `Worn(session, entity)`); item spells from `NpcItemSpells(session, item)` |
| Hit sound's armour, the stunned/hit notices of the AI, the throw's struck-worn set | `(shown map, entity)` | `WornArmor(session, entity)`, `Build(session, entity)`, `WornItems(session, entity)` |

`AreaCombatHost.Build(map, entity)` no longer throws for an entity the seed does not list as a holder (an item's row): it is `NotAnNpc`, since
the identity questions reach any entity. The map-keyed questions stay for code that asks about one map's own rows (smokes, the shop, the loot
windows).

## Not ready

- A map that opens only as a window (the main world map, more sector files than a whole-map plan admits) is not read on demand
  (`HomeUnread` with the plan status); its NPCs resolve while the viewer holds that map. Since ARC-DEV-075 (MIG2) the viewer also keeps the
  maps it left at a map change of the same run (`AreaRememberedRecords`, a record source that answers from them before the game folder's
  reader), so Virgil carried from the main map into the Crash Site cave resolves there. A quick load to another map or a fresh run starts with
  none: Virgil's home (a main-map window) is then unresolved, and the AI keeps him out of its rules (`AreaAiHost.Unresolved`) until MIG9
  reads such a window on demand (the F7 in the cave, a fresh run, F8 case). The Crash Site's own NPCs are held because the
  viewer gives the resolver the launch maps.
- The read happens on the caller's thread (once per map, then cached, failures too); a frame that asks for a never-held home map pays for
  it. MIG9 (residency) owns moving reads off the frame.
- `ForVisitor`, `_travellers` and the other visitor routing (the body, the dialog host) go in MIG4 and MIG5; the home-map fallbacks of the
  consumers above went in MIG3 (the `HomeMap` question stays on `AreaCombatHost` for the diagnostics). The hero's attack never targets a
  companion (a visiting one included: `IsAreaCompanion`); the attack cursor on a visiting companion in combat mode is now the same "no
  action" as on a home companion, not the talk cursor. The existing boundaries of a visiting companion (no blood pool at its death, no seed items in the shown map's
  inventory window) stay until MIG4/MIG5. The
  `Stranger` skip went in MIG2 but for one case: a companion with no record (a home on the main map's window) is kept out of the AI rules by
  `AreaAiHost.Unresolved`, until MIG9 reads such a window on demand.
