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
(MIG3, done: ARC-DEV-076; below), the body (MIG4, done: ARC-DEV-079; [below](#bodies-and-window-homes-arc-dev-079-mig4)) and one dialog host (MIG5, done: ARC-DEV-081; [below](#one-dialog-host-arc-dev-081-mig5)).

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

### Bodies and window homes (ARC-DEV-079, MIG4)

The companion's body (its T0 draw commands, visuals and frames: `AreaCompanionTemplate`) comes from its home map's data by identity, as its
record does. The viewer's run-local cache of bodies (`_travellers`) and the HUD's "no body kept this run" path (`AreaVisitors.Unbodied`) are
gone: a quick load or a fresh run on a foreign map finds the companion's body the same way a map change in one run does.

| Part | Role |
| --- | --- |
| `AreaHomeRead` | One read of a home map: the resolver's rows (`AreaInventoryMap`), the bodies of the speakers with follow facts and the frames those bodies draw; a window read lists its core sectors. Derived data, never saved. |
| `IAreaHomeSource` / `AreaMapHomes` | Reads a home from the game folder's module content stack: the whole map, or (a map with more sector files than a whole-map plan admits: the main world map) a 3×3 window of core sectors around a stored cell; strict publication, step evidence, scene plan, and only the frames of those bodies. A map the list does not name, a window with no stored cell to centre on or a map that does not read is a typed failure. |
| `AreaHomeDirectory` | Holds the reads (default 4, least recently used out first), answers `Body(entity, homeMap, storedCell)` from a held read that lists the entity or reads the home once, and remembers a failed read. The viewer `Remember`s the map it has just loaded (its own scene and rows), so a companion carried from it needs no read. |
| `AreaMapRecords` | The resolver's record source (behind `AreaRememberedRecords`, the rows of the map the run has just left, MIG2), now through the same directory: a map read for a body is not read again for its record; a window map is read around `AreaVisitors.HomeWindow(session, map)`. |
| `npc.home` (`AreaCompanionPlacement.HomeProperty`) | The one new session fact: on the home map, the world cell the NPC's stored object lies on. It is written by every recorded placement (`Edits`: `Arrive`, `Carry`, `Record`) and cleared by the one that clears the placement (its stored cell facing the default), so it exists while the placement is recorded (a body off its stored cell, or on it facing other than the default). Without it a window home cannot be read (the T0 cell is not a change, and the NPC has left), so it is the "which sector" the model lacked. It is an ordinary entity change of the home map (no schema change), saved and restored with the session. |
| `AreaVisitors.Here` | The companions recorded on the shown map (`npc.cell`, on a loaded cell) that the shown scene does not draw, each with its home map (`npc.map`, else the map itself: another window of it) and stored cell. |

The viewer asks for the bodies of the visitors of the shown map whenever it rebinds its companions (`RefreshVisitorBodies`: a map load, a
quick load, a reset), adds their frames to the shown frame repository and lists a body that could not be read on the HUD with the reason
(`ARC-DEV-079 visitor body unread ...` in the log); nothing is guessed. The Virgil case (his home is a window of the main map) is covered: after a
quick load or a fresh run on a foreign map his window is read around the cell recorded when he left, his body is drawn and follows, and the
resolver finds his record there, so the AI no longer keeps him out of its rules (`AreaAiHost.Unresolved` stays for a record that cannot be read).

## Not ready

- A companion that left its home before this slice recorded `npc.home` (a save from an earlier build, with the companion already away) has no
  stored cell: a whole-map home still reads, a window home does not (typed failure, named on the HUD, no body and kept out of the AI rules). Nothing
  repairs it later: without a body it is not in the group that writes the cell, and it stays on the map it was left on. A new quick save with the
  companion carried by this build writes it; such an old save needs the companion fetched home again (a migration is not planned).
- The resolver holds one window per map number, read around `AreaVisitors.HomeWindow` (the lowest-entity NPC that is away from that map, else the lowest
  with a recorded cell). ARC-DEV-337 (B52) adds one extra window per map: when the held window of the home map does not list the companion (another
  window of the main map is shown, as after a trip town -> temple -> town), `IMapRecordSource.LoadAround` reads the window around his stored cell (the
  source skips the windows the run left behind), the resolver keeps it beside the held map (not counted in `Maps` or the limits) and a miss is remembered
  per map and entity; the home map comes from the away change or, for a companion that stands on its home map's number, from his stored cell
  (`EntityHomes.RecordedHomeOf`). Still not ready: companions of two other windows of one map (the later read replaces the extra window). A failure the
  resolver meets (no stored cell yet) is kept by it for the scene. MIG9 (residency) reads such windows through the sector model.
- The rows are what the resolver needs: a body or frame that does not read leaves the read with its rows and no bodies (named in the read's detail).
- A home map that was not held is read on the calling thread, once (a window read takes the strict publication of 25 sectors: about 3 s for the main map's window in the `transfer` smoke); MIG9 owns moving reads
  off the frame. A map change is where it normally happens.
- `ForVisitor`, the dialog host routing and `refuseUncoveredInventory` went in MIG5 (ARC-DEV-081, [below](#one-dialog-host-arc-dev-081-mig5)); the
  home-map fallbacks of the consumers above went in MIG3 (the `HomeMap` question stays on `AreaCombatHost` for the diagnostics). The hero's attack never
  targets a companion (a visiting one included: `IsAreaCompanion`); the attack cursor on a visiting companion in combat mode is the same "no action" as on a
  home companion, not the talk cursor. The existing boundaries of a visiting companion (no blood pool at its death, no seed items in the shown map's
  inventory window) stay (the seed items wait for the object directory, MIG6d).

## One dialog host (ARC-DEV-081, MIG5)

The viewer has one `AreaConversationHost` per shown map. A companion carried to it (or standing on the main map's other window) is listed by
`AdmitVisitor` as one more speaker after the map's own and the ones admitted before, so a click, the hover action, the walk-to-talk reach,
a scripted conversation, a deferred call and the barter all go through that host; `AreaConversationHost.ForVisitor`, `AreaTalkRouting`, the viewer's
visitor host cache (`_visitorHosts`, `_talkHost`, `VisitorHost`, `VisitorCallHost`, `TalkHosts`) and `refuseUncoveredInventory` are gone.

- **Where the state lives.** The speaker's dialog state (its local flags, script slot, counters and follow consent) stays under its entity on its
  record's map, `template.Home` (`AreaConversationHost.StateMap(speaker)`, `AreaConversationState.MapOf`), as ARC-WORLD-048 had it, so a talk
  where it visits and a talk at home read and write the same keys; the map's own speakers keep the shown map's. Everything else about the
  speaker is the shown map's: its object stands on that map for the scripts (`AreaScriptHost.Object`; `Pc` and the speaker share the map), but a run on it keeps the narrow script set the visitor host ran with
  (`AreaScriptHost.AdmissionFor`: no `QueryHost` queries, since the shown map's use host holds neither a row nor a seed holder for an object of another
  map and would answer a query about it wrongly instead of refusing it; the map's own speakers keep the wide set), its reaction and follow standing are the session's by entity, JO, WA, UW and LV go to the session party
  with the follow facts the template carries. One entity keeps one state until the object directory (MIG6) moves it onto the object.
- **Admission.** The viewer asks for every companion standing here before it looks for a speaker (click, hover, nearest in reach, the status line,
  a deferred call's attachee: `AdmitCompanionSpeakers`, `AdmitCallAttachee`); admitting is idempotent, a companion the host lists already (its own
  speaker, or another row of the same object id) is returned as it is, a carried one counts as present only while the viewer's companion group holds it
  (`AreaConversationHost.IsCarried`, the viewer's `SpeakerPresent`), and an index is never reused. A host that is not party-wired (the exterior and cave without `--area-map`) lists none.
  A companion whose stored state under its entity cannot be read (a damaged save) is refused with a typed reason, named once on the status line and
  in the log (`ARC-DEV-081 companion refused`), and refused again silently until the host is rebound over another session.
- **Quick load.** `Rebind` keeps the map's own speakers only; the companions standing here are admitted again over the new session when asked,
  their state read then (a damaged state refuses that companion, not the quick load).
- **Negative `IN`.** The port is the shown map's (`SessionDialogInventoryPort` by entity); the host has no refusal rule of its own. Since ARC-DEV-087
  (MIG6e) the session (`GameSessionOwner.GiveItemToPc`) finds the speaker's holder in the attached seed that lists it, the hero's map first, then the others, and the
  item enters the hero's list with that map as its origin: a companion carried here from its home map gives its item like one of the hero's own map
  ([object directory](../architecture/object-directory.md#mig6e-what-is-built)). The port's `prepare` hook (`AreaInventoryHost.AttachHolderMap`) attaches the home
  map's seed first when the resolver read it on demand and the session holds none. A speaker whose map has no attached seed (never read, or refused by the
  session) is still refused with `UnknownHolder` (a rejected command). A host with no inventory records the item only, for every speaker.
- **Smoke.** The `transfer` stage's visitor talk (`ARC-WORLD-048 native-smoke visitor-talk`) now runs through the one host and asserts the speaker is
  listed with its state on the home map.

## Not ready (ARC-DEV-081)

- A deferred call whose attachee is a companion that is not standing here is run by the shown host with that attachee's own map (the session role's), as
  any object that is not a speaker of the host.
- A carried companion from another map gives an item by negative `IN` since MIG6e (ARC-DEV-087); an item with an insert-item or remove-item script
  of another map's seed is refused (the script host answers for the shown map's rows), and a companion whose home map's seed is not attached is refused (see above).
- The companion's dialog state is not on its object: a quick save of a companion carried here keeps it under its home map's entity keys, as before.
- The admission order depends on who is asked first, so a speaker's index differs between runs; no state is keyed by it.
