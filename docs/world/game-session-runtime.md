# Game session state runtime

Status: **Implemented bounded component**, ARC-WORLD-008; story domain ARC-SCRIPT-006; party ARC-WORLD-013;
inventory ARC-WORLD-014; player character ARC-WORLD-015; randomness ARC-WORLD-019; life state ARC-COMBAT-002;
event queue ARC-WORLD-024; NPC reaction ARC-WORLD-026; turn-based round ARC-COMBAT-003; equipment ARC-WORLD-028.
A Godot-free C# owner in the `Arcanum.Session` assembly that holds one game session: the
[game clock](game-clock-runtime.md), the [global flags](../scripting/global-flag-state-runtime.md),
the actor's placement, map-local changes and the story domain (PC quest states, shared
quest states, known rumors), the PC's [party](party-runtime.md) (followers, waiting NPCs), the
[player character](starting-hero-runtime.md) (stats, level, alignment, gold and starting kit), the items the PC
took from the world ([inventory](inventory-runtime.md): pickup, loot, negative dialog `IN`), the hero's worn items
([equipment](equipment-runtime.md): wear slots, wield and remove, worn values for combat), the seeded gameplay
generator with labelled rolls ([session randomness](session-random-runtime.md)), the life of the hero and the NPCs it
changed ([life state](life-state-runtime.md): hit points, fatigue, poison, injuries, knockout, death), the game event
queue of their timers ([event queue](game-event-queue-runtime.md#session-wiring-arc-world-024)), the NPCs' reaction
memory toward the PC ([NPC reaction](npc-reaction-runtime.md)), the active turn-based combat round
([turn-based round](turn-based-round-runtime.md)), plus a
versioned snapshot schema with a strict JSON codec.
The AREA viewer keeps every [dialog consequence](../scripting/dialog-conversation-runtime.md#area-viewer-integration-project-diagnostic)
it can store here. It covers the state boundary of
[FR-WORLD-013](../requirements/world.md#fr-world-013) and
[FR-LIFECYCLE-003](../requirements/ui-media-lifecycle.md#fr-lifecycle-003).
The owner itself does no file I/O. One [save slot](#save-slot-arc-world-011)
(ARC-WORLD-011) writes and reads a snapshot file atomically; there is still no
save/load screen or save list, no autosave and no original `.gsi`/sector-diff format.

## Why a separate assembly

The owner composes `Arcanum.Simulation` (clock, `CellCoord`, `NeighborDirection`) and
`Arcanum.Scripting` (GF owner). Neither may reference the other, so the composition
lives one level up. `Arcanum.Simulation` grants `Arcanum.Session` friend access only
for `GameClockOwner.FromDescriptor`, the existing validated descriptor path, so a
saved clock is rebuilt with the same validation as bootstrap. No public clock API changed.

## API

Namespace `Arcanum.Session`. The caller serializes **all** calls, as with the clock
and GF owners. There are no locks, host events, threads, files or Godot types; game timers are queue events that fire
only when the host runs a pass. Two callbacks reach the host: the insert check during an inventory transfer (a call
back into the owner from it makes that transfer `StaleVersion` with no effect) and `ISessionEventWorld.CritterFacts`
during an event pass, which must not call back into the owner (a `PlaceActor` or `ApplyChanges` from it is
`InvalidEventPass`; a `Capture` from it throws).

| Entry | Contract |
| --- | --- |
| `GameSessionOwner.Create(GameSessionDescriptor)` | Clock `ProfileBinding`, GF descriptor, initial `ActorPlacement`, optional limits, optional hero (ARC-WORLD-015) the random seed (ARC-WORLD-019, default 0) and the rule mode (ARC-WORLD-023, default Steamwoven; undefined is `InvalidDescriptor`). Validates the actor and the hero (`InvalidHero`), bootstraps the clock, then creates the GF owner. Any rejection returns no owner, a `GameSessionFailure`, and the inner `ClockFailure` / `GlobalFlagReason`; a clock already built is disposed. |
| `GameSessionOwner.Restore(GameSessionSnapshot, GameSessionRestoreOptions)` | Checks the options' rule mode (undefined is `InvalidDescriptor`; the snapshot holds none), schema, world, module, the exact clock profile, actor, map keys, budgets, story, party, hero, inventory, life, reactions and the pending events (under the mode's event profile) **before** building anything, then rebuilds clock (saved time, pause, revision) and GF (saved domain, known values, revision, new epoch). Never touches an existing owner. |
| `Version` / `PlaceActor` / `SetMapChange` / `ClearMapChange` | Actor and map mutations take the expected `GameSessionVersion` (owner identity + revision). Input validation, then version check, then NoChange, then budget, then revision capacity; every refusal leaves state untouched. Each semantic change adds exactly one revision. |
| `ApplyChanges(expected, actor?, changes)` | ARC-WORLD-017: one guarded atomic step over the actor (when given) and a list of `MapChangeEdit(Map, Key, Value?)` (null clears the key). Validates the actor, every map and key and that no key is listed twice, then the version; NoChange when nothing would differ; the budget is checked on the state after the whole step (an update or a move of a key never needs room); then revision capacity, then (ARC-WORLD-024) the move of the followers' pending timers (`EventConflict`, `InvalidEventPass`). A refusal applies nothing; otherwise everything applies with one revision. The viewer's map transfer carries the PC's followers with it ([companion map change](companion-follow-runtime.md#map-change-arc-world-017)). |
| `QueryActor` / `QueryMapChange` / `QueryMapChanges` | Current placement; one value or `null` for "at loaded baseline"; a detached, canonically ordered copy of one map's changes. |
| `QueryClockSnapshot` / `AdvanceGameTime` / `SetPaused` | Forwarded to the session's clock with the clock's own token contract. Clock changes do not move the session revision. |
| `QueryLighting` / `RecordLighting` | Applied ambient lighting (ARC-WORLD-010, FR-LIGHT-012): `SessionLightingState(SchemeId ≥1, Hour 0..23)` or `null` when none was recorded. `RecordLighting` refuses (`InvalidLighting`) a value whose hour is not the clock's current hour, and a changed `AdvanceGameTime` that leaves the recorded hour clears the record, so a capture never pairs a time with another hour's scheme (it carries `null` until the host records the new hour). Equal value is NoChange. It does not move the session revision. |
| `Flags` / `FlagVersion` | A forwarding `IVersionedGlobalFlagOwner` view of the session's GF owner (reads, writes, current version, `GlobalFlagAdapter`, dialog conversations; no Dispose or MarkFailed) and its current version. GF changes do not move the session revision. |
| `SetQuestState` / `LearnRumor` | Story mutations with the same expected-version guard and refusal order (input, version, NoChange, revision capacity). One revision per change. See [Story domain](#story-domain). |
| `QueryQuest` / `QueryGlobalQuest` / `QueryRumor` / `QueryStory` | PC quest record or `null` (never changed); shared state (`Accepted` by default); rumor time or `null`; a detached ascending copy of the whole domain. A number outside the source range throws `ArgumentOutOfRangeException`. |
| `QueryCompanion` / `QueryParty` / `JoinParty` / `ForceJoinParty` / `UnwaitParty` / `WaitParty` / `LeaveParty` / `SetSpreadOut` | Party domain (ARC-WORLD-013) with the same guard and one revision per change; join and unwait run the follow check and return its reason. See [party runtime](party-runtime.md). |
| `AttachMapInventory` / `QueryMapInventory` / `QueryPcInventory` / `PickUpItem` / `LootItem` / `GiveItemToPc` | Inventory domain (ARC-WORLD-014): a host-attached T0 seed per map (not state, no revision) and the PC's taken items; transfers take the expected version, refuse before any effect and add one revision each. See [inventory runtime](inventory-runtime.md). |
| `QueryEquipment` / `QueryHeroEquipmentCombat` / `Equip` / `Unequip` | Equipment domain (ARC-WORLD-028): the hero's worn items, their part of its combat values (armour class and resistance adjustments, light, the attack it strikes with, weapon speed and attack cost), wielding or wearing an item it holds under the wear check with the host's art and insert checks, and taking it off; one revision per change. The worn normal resistance enters the hero's life facts. See [equipment runtime](equipment-runtime.md). |
| `QueryHero` / `QueryHeroStats` / `QueryHeroDialogFacts` / `QueryGold` | Player character (ARC-WORLD-015), read-only: saved fields, the character owner's stats under a context, the DLG reply facts, the PC's gold; `null` (gold 0) for a session without a hero. See [starting hero runtime](starting-hero-runtime.md). |
| `Roll` / `QueryRandom` / `CombatRolls` | Session randomness (ARC-WORLD-019): one labelled uniform roll in an inclusive range, the generator's seed and position, and the generator as the attack resolver's roll source. A roll takes no expected version and does not move the revision. See [session randomness](session-random-runtime.md). |
| `QueryHeroLife` / `QueryLife` / `QueryLifeRecord` / `ApplyDamage` / `Kill` / `Heal` / `RecoverFatigue` / `RestHeal` / `PoisonDamageTick` / `PoisonRecoveryTick` / `ClearStun` | Life domain (ARC-COMBAT-002): the hero, or an NPC with the host's facts and loaded state; mutations take the expected version, refuse before any roll, add one revision per change and draw their knockout and second-arm rolls from the session generator. See [life state runtime](life-state-runtime.md). |
| `QueryHeroReactionFacts` / `QueryReaction` / `QueryReactionRecord` / `QueryReactions` / `AdjustReaction` / `SetReaction` / `ReactToPcHit` / `BeginReactionConversation` / `EndReactionConversation` / `ForgetReaction` | Reaction domain (ARC-WORLD-026): an NPC's effective reaction to the PC under the caller's facts, and its remembered and conversation slots; mutations take the expected version, refuse before any change and add one revision per changed record. See [NPC reaction runtime](npc-reaction-runtime.md). |
| `QueryCombatRound` / `StartCombatRound` / `EndCombatRound` / `ConsumeActionPoints` / `EndCombatTurn` / `CompleteCombatActions` / `AddCombatant` / `QueryActionPointCheck` / `QueryCombatActionGate` | Turn-based round (ARC-COMBAT-003): participants from the host's `ISessionCombatWorld` scan, action points, charges with the hero's overexertion, subturns and the end of a round one game second later; mutations take the expected version, decide the step on a copy, refuse before any effect and add one revision when the round changes. While a round is active an ordinary `AdvanceGameTime` is NoChange. See [turn-based round runtime](turn-based-round-runtime.md). |
| `Rules` / `SelectRules(mode)` | Rule mode (ARC-WORLD-023, [contract](rule-modes.md)): the active `GameRules` (Steamwoven or Original) every domain reads at its decision, and an immediate switch (`Changed`, `NoChange`, `Rejected` for an undefined mode). Starts from `GameSessionDescriptor.Rules` or `GameSessionRestoreOptions.Rules` (default Steamwoven). A setting, not state: no revision, not in the snapshot. |
| `QueryEvents` / `ProcessEvents(world, budget)` | Event queue (ARC-WORLD-024): the pending timers in queue order, and one pass at the session's game time on the actor's map with the host's `ISessionEventWorld` for NPC facts. Life steps set their timers in the same step (`EventBudgetExceeded` when they do not fit); `PlaceActor` and `ApplyChanges` to another map move the followers' timers (`EventConflict` on a collision). See [session wiring](game-event-queue-runtime.md#session-wiring-arc-world-024). |
| `Capture()` | One detached `GameSessionSnapshot` of every component at this call boundary. |
| `Dispose()` | Idempotent; disposes the clock and GF owner. Every later call except `Dispose` throws `ObjectDisposedException`. |

A map transition is several separately guarded calls (for example `PlaceActor`,
`SetMapChange`, `AdvanceGameTime`); the caller sequences them, and a refusal partway
leaves the earlier calls applied. The session revision counts actor and map changes
only, so a caller that needs one coherent point captures after the whole sequence.
The actor and any number of map changes can be applied as one step with `ApplyChanges`
(ARC-WORLD-017); clock, flags, story, party, inventory, equipment, life and reactions are not part of it.

Owner identity is a fresh process-local reference per create/restore. A restored
owner continues the saved revisions, but versions, clock tokens and GF epochs of any
earlier owner are stale on it.

### Actor placement

`ActorPlacement(SessionMapId Map, CellCoord Cell, NeighborDirection Facing)` is the
actor at a **settled** boundary: runtime map (≥1), nonnegative cell, defined facing.
`PlaceActor` serves both moves within a map and map transitions. In-flight movement
(`MovementState`, active segment, input keys) stays host state and is not saved; after
restore the host starts movement with `MovementState.CreateSettled` at the restored
cell. Saving mid-step is out of scope.

### Map-local changes

Changes are an overlay over the immutable loaded map (`Area`), keyed by
`SessionMapId` and `MapChangeKey`:

- `MapChangeKey.ForSector(sector, property)`: packed sector id ≥0, no entity.
- `MapChangeKey.ForEntity(entity, property)`: stable entity id, sector 0.
- Entity ids (1..128) and property codes (1..64) use ASCII letters, digits and `. _ : -`
  only, with no trimming or case folding. Values are signed 64-bit.

Property codes are **project-defined**: this component does not decide what a door,
container or NPC state is, nor parse original sector diffs. Absent keys mean the
loaded baseline; `ClearMapChange` returns a key to it, and a map left with no changes
is dropped from snapshots. Changes on one map are untouched by moving the actor to
another and back, which is the FR-WORLD-013 survival rule at the state level.

Limits (`GameSessionLimits`) can be lowered, never raised: at most 64 maps with
changes, 4,096 changes per map and 16,384 in total. Updating an existing key is
allowed at capacity; a new key or map over capacity is `BudgetExceeded`.

### Story domain

ARC-SCRIPT-006 adds the state the original keeps for quests and rumors, with the source
rules ([quest state set][quest-set], [quest reset][quest-reset], [rumor known][rumor]):

- **Quests** are numbered 1000..1999. `SessionQuestState` follows the source order
  `Unknown, Mentioned, Accepted, Achieved, Completed, OtherCompleted, Botched`. A PC record
  `SessionQuest(Quest, State, Botched, TimestampMs)` keeps the stored state and a botched
  mark over it; `Effective` reports `Botched` when marked.
- `SetQuestState(expected, quest, requested)`: an effective `Completed`, `OtherCompleted` or
  `Botched`, or a request not above the effective state, is `NoChange`. Otherwise, while
  the quest's shared state is `Accepted` (the source reset value), the request is taken
  and `Completed`/`OtherCompleted` set the shared state to `Completed`, `Botched` to
  `Botched`; once the shared state left `Accepted`, the request becomes `OtherCompleted`
  (shared `Completed`) or `Botched`. `Botched` keeps the stored state and sets the mark.
  The record is stamped with the session clock's current game time.
- **Rumors** are numbered 1000..2999. `LearnRumor` records the current game time once;
  learning a known rumor is `NoChange` and keeps the first time.
- Experience, quest alignment adjustments, logbook and sound are **not** applied: they
  need quest data and their own owners. The AREA viewer applies the reaction rule of the
  source (accepting lifts the NPC's reaction to 41, completing adds 10) in its own port.

Invalid numbers or states are `InvalidQuest`, `InvalidQuestState` or `InvalidRumor`.
`Restore` also refuses story records the rules cannot produce: a time after the saved
clock (`InvalidSnapshot`), a PC record `Completed`/`OtherCompleted` without shared
`Completed`, a botched one without shared `Botched`, or any other PC record under a shared
state that left `Accepted` (`InvalidQuestState`). A shared state without a PC record is kept,
as the source's shared state is not owned by one PC.

[quest-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c#L337-L470
[quest-reset]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c#L95-L110
[rumor]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/rumor.c#L259-L310

## Snapshot schema 12

`GameSessionSnapshot` (only from `Capture` or `TryDecode`) carries `SchemaVersion`,
`Identity` (GF world/module), session `Revision`, `Clock` (`ProfileBinding`, timestamp,
pause, revision; no owner identity), `Flags` (revision and every admitted flag in
ascending order, `null` = Unknown), `Actor`, `Maps` (ascending map, each with
changes in canonical order: scope, sector, entity, property, ordinal), `Story`
(PC quests, shared quests that left `Accepted`, known rumors; each ascending), `Party`
(the follower array in order, then waiting and flagged free NPCs; [party runtime](party-runtime.md#snapshot-schema-4)),
`Hero` (the player character or `null`; [starting hero runtime](starting-hero-runtime.md#snapshot-schema-6)),
`Inventory` (the PC's gold, the starting kit and the taken items in list order; [inventory runtime](inventory-runtime.md#snapshot-schema-5)),
`Equipment` (the hero's worn items in slot order; [equipment](equipment-runtime.md#snapshot-schema-12)),
`Random` (the generator's seed and draws made; [session randomness](session-random-runtime.md#snapshot-schema-7)),
`Life` (the hero's life record and the changed NPCs' in ordinal order; [life state](life-state-runtime.md#snapshot-schema-8)),
`Reactions` (the changed NPCs' reaction slots in ordinal order; [NPC reaction](npc-reaction-runtime.md#snapshot-schema-10)),
`Combat` (the active round or `null`; [turn-based round](turn-based-round-runtime.md#snapshot-schema-11)),
`Events` (the registry version, the next sequence and the pending timers; [event queue](game-event-queue-runtime.md#snapshot-schema-9))
and `Lighting` (applied scheme and hour, or `null`). Collections are read-only copies.

`GameSessionSnapshotCodec.Encode` writes one canonical compact JSON text:

```json
{"format":"arcanum-steamwoven-session","schemaVersion":12,
 "world":"<guid D>","module":"<guid D>","revision":2,
 "clock":{"schemaVersion":1,"policyId":"…","sourceRevision":"…","sourceDataSha256":"…",
          "startupResourceSha256":"…","startYear":1885,"startHour":15,
          "timestampMs":54000000,"paused":false,"pauseReason":"None","revision":0},
 "flags":{"revision":0,"entries":[{"flag":3,"value":false},{"flag":9,"value":null}]},
 "actor":{"map":79,"x":100,"y":200,"facing":"XPlus"},
 "maps":[{"map":79,"changes":[
   {"scope":"sector","sector":5,"property":"door.open","value":1},
   {"scope":"entity","entity":"npc-1","property":"hp","value":12}]}],
 "story":{"quests":[{"quest":1001,"state":"Accepted","botched":false,"timestampMs":54000000}],
          "globalQuests":[{"quest":1002,"state":"Completed"}],
          "rumors":[{"rumor":1200,"timestampMs":54000000}]},
 "party":{"followers":[{"entity":"oid:…","forced":false,"spreadOut":false}],"others":[]},
 "hero":null,
 "inventory":{"gold":0,"kit":[],"pc":[{"entity":"oid:…","map":79,"type":"Armor","prototype":3804,"name":2804,"description":32804}]},
 "equipment":[],
 "random":{"seed":"0000000000000000","draws":0},
 "life":{"hero":null,"critters":[]},
 "reactions":{"records":[]},
 "combat":null,
 "events":{"registry":1,"nextSequence":1,"pending":[]},
 "lighting":{"schemeId":1,"hour":15}}
```

(Line breaks added here; the encoder writes no whitespace.) `TryDecode` returns a
`SnapshotDecodeResult` with a typed `SnapshotDecodeFailure` and a JSON path; it never
throws on bad text and never builds an owner. It refuses: input over 8 MiB characters
(`TooLarge`); invalid JSON, comments, trailing commas or depth over 8 (`MalformedJson`);
another `format` (`WrongFormat`) or `schemaVersion` (`UnsupportedSchema`); unknown,
duplicate or missing fields; wrong JSON types; fractions, exponents and out-of-range
integers; non-canonical GUIDs, enum names or identifiers (`OutOfRange`); unsorted or
repeated flags, maps, change keys, quests or rumors (`NotCanonical`); quest or rumor
numbers outside their ranges, a stored quest state `Botched`, an unmarked `Unknown` record
or a shared state other than `Completed`/`Botched` (`OutOfRange`); and counts above the policy
maxima (`BudgetExceeded`). `-0` is `OutOfRange`; a string with a lone UTF-16
surrogate escape is `MalformedJson`. Format and version are read before the field set, so a newer schema
reports `UnsupportedSchema` rather than a field error. Decoding accepts whitespace,
object field order and JSON string escapes; only `Encode` output is canonical.

Decoding checks structure; `Restore` checks meaning. Restore refuses a snapshot from
another world or module, with a clock profile not equal to the expected one (schema,
policy, source pin, data/startup fingerprints, start year/hour), whose clock or GF
state the component owners refuse, or that exceeds the caller's limits. A refused
restore returns no owner and leaves the caller's current session as it was.

Restore also refuses (`InvalidLighting`) a lighting value outside the ranges or whose
hour differs from the saved clock's hour. A restored clock keeps its saved pause: a
snapshot captured while a dialog or the palette held a `Menu` pause restores paused,
and the host releases or re-applies its modal pause from what is actually open.

**Migration decision: none.** Schema 12 is the only accepted version. ARC-WORLD-028 added the required
`equipment` list; schema 11 is refused like the earlier ones (see [equipment](equipment-runtime.md#snapshot-schema-12)).
ARC-COMBAT-003 had added the required, nullable `combat` section and refused schema 10 (see
[turn-based round](turn-based-round-runtime.md#snapshot-schema-11)). ARC-WORLD-026 had added the
required `reactions` section, which replaces the viewer's `reaction.pc` map changes; schema 9 is refused like the
earlier ones (see [NPC reaction](npc-reaction-runtime.md#snapshot-schema-10)). ARC-WORLD-024 had added the
required `events` section and refused schema 8 (see
[event queue](game-event-queue-runtime.md#snapshot-schema-9)). ARC-COMBAT-002 had added the
required `life` section and refused schema 7 (see
[life state](life-state-runtime.md#snapshot-schema-8)). ARC-WORLD-019 had added the
required `random` section and refused schema 6 (see
[session randomness](session-random-runtime.md#snapshot-schema-7)). ARC-WORLD-015 had added the
required, nullable `hero` field and the inventory's `gold` and `kit`; schema 5
is refused like the earlier ones (see [starting hero runtime](starting-hero-runtime.md#snapshot-schema-6)). ARC-WORLD-014 had added the
required `inventory` domain and refused schema 4 (see
[inventory runtime](inventory-runtime.md#snapshot-schema-5)). ARC-WORLD-013 had added the
required `party` domain and refused schema 3 (see
[party runtime](party-runtime.md#snapshot-schema-4)). Before that, ARC-SCRIPT-006
added the required `story` domain and ARC-WORLD-010 the required, nullable `lighting`
field; each was briefly schema 2 on its own while they landed together, so that merged
format was schema 3. A quick-save file of schema 1..11 (ARC-WORLD-011) loads as "written by an incompatible version" with the
running session kept, and the next F7 overwrites it. The only earlier host (the AREA
viewer) had a one-flag placeholder GF domain, so a migrated session could not run any
dialog. Adding another domain needs schema 13 with its own explicit migration decision.

## Save slot (ARC-WORLD-011)

`GameSessionSaveSlot` (same assembly, Godot-free) is one file that holds exactly the
`GameSessionSnapshotCodec.Encode` text of a snapshot. It knows nothing about the
snapshot's sections, so a domain added to the snapshot and its codec (live clock,
dialog state, a schema bump) is saved and loaded by the slot without changing it.

| Entry | Contract |
| --- | --- |
| `new GameSessionSaveSlot(path)` / `QuickSave(directory)` | An absolute file path; `QuickSave` is `<directory>/quicksave.json`. Invalid paths throw (programming error). |
| `Write(snapshot)` | Encodes, decodes the text back in memory (a save this build cannot load is never written), writes `<path>.tmp`, flushes it to disk, reads it back and compares, then moves it over the slot. Creates the directory. Returns `SaveSlotWriteResult` (`None`, `TooLarge`, `WriteFailed`, `VerifyFailed`); on any failure the temporary file is removed and the previous save is untouched. A stale temporary file from an interrupted save is overwritten. |
| `Read()` | `SaveSlotReadResult` with the decoded snapshot or a typed failure: `NoSave` (no file or directory; a directory or unreadable entry at the path is `ReadFailed`), `TooLarge` (over 8 MiB; the codec writes ASCII, so its character limit is the byte limit, also enforced while reading), `ReadFailed`, `NotUtf8` (strict UTF-8), `Corrupt` with the codec's `SnapshotDecodeFailure` and JSON path (truncated, wrong format, unsupported schema, any field error). Never throws on content. |

The slot does not restore: the caller passes the snapshot to `Restore` with its own
expectations. Durability: file data is flushed before the rename; the directory entry
is not separately synced. One process per slot; there is no cross-process lock.

## Tests

[Session tests](../../tests/Arcanum.Session.Tests) use synthetic ids and fingerprints
and a literal, independently written canonical JSON text:

- `SessionOwnerTests`: creation and rejection, leave-and-return across two maps with an
  untouched neighbour sector, version guards and revision counting, key validation,
  budgets, detached capture, clock forwarding, disposal.
- `SessionLightingTests`: lighting validation and current-hour rule, no revision move,
  `null` round trip, restore refusing another hour, paused restore with lighting.
- `StoryStateTests`: quest rises, shared completion and terminal states, botched over a
  kept state, shared states that convert a request, rumor times, invalid and stale
  requests, survival across a map round trip and the snapshot, story decode failures,
  restore consistency checks, and the original new-game GF domain.
- `PartyStateTests`, `SessionDialogPartyPortTests`: the party domain and its dialog port
  (see [party runtime](party-runtime.md#tests)).
- `SessionRandomTests`: labelled rolls, their save and replay, and the combat roll source
  (see [session randomness](session-random-runtime.md#tests)).
- `LifeStateTests`: the life domain (see [life state](life-state-runtime.md#tests)).
- `ReactionStateTests`: the reaction domain (see [NPC reaction](npc-reaction-runtime.md#tests)).
- `RuleModeTests`: the rule mode composition and names, create and restore under either mode, switching without a
  revision or snapshot change, disposal, and the setting codec (see [rule modes](rule-modes.md)).
- `SessionEventTests`: the event queue and its life timers (see [event queue](game-event-queue-runtime.md#tests)).
- `SaveSlotTests`: the file is exactly the codec text and restores the same session;
  missing file or directory, a directory at the slot path; overwrite; a failure before the replace keeps the previous
  save and removes the temporary file; an unwritable slot; a stale temporary file;
  truncated, empty, BOM, invalid UTF-8, newer-schema, foreign-format and out-of-range
  files as typed failures; the byte limit; path validation.
- `SnapshotRestoreTests`: literal canonical encoding, full restore with pause, Unknown
  flag and stale old tokens, refused restores (world, module, profile, budget, epoch,
  clock schema, empty GF domain) leaving the current session unchanged, typed decode
  failures with paths (including lone surrogates and a newer schema), a restore at the
  per-map policy maximum, revision capacity, and the lifetime-safe flag view.

```sh
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build
```

## Not implemented / remaining work

- Save/load UI, a save list, more than one slot, backups, autosave, thumbnails (the
  one quick-save slot is ARC-WORLD-011).
- Script (SCR) state and timers.
- Concrete map-change vocabulary beyond the viewer's NPC dialog keys (doors, containers,
  other NPC state) and their application to rendering or step assessment; original
  sector-diff and mobile-object formats.
- Movement mid-step persistence, follower movement and the cross-map group, the inventory beyond the PC's kit and taken
  items (stacks, drop, chests, barter; see [inventory runtime](inventory-runtime.md)), changes to the hero after the start
  (alignment, purchases, levels), the quest logbook, experience and quest data, gameplay rolls by any host, life timers
  and their consequences for other owners (see [life state](life-state-runtime.md#not-implemented)), and other
  domains.
- Schema migration and compatibility with original saves.
