# Game session state runtime

Status: **Implemented bounded component**, ARC-WORLD-008; story domain ARC-SCRIPT-006; party ARC-WORLD-013;
inventory ARC-WORLD-014; player character ARC-WORLD-015; randomness ARC-WORLD-019; life state ARC-COMBAT-002;
event queue ARC-WORLD-024; NPC reaction ARC-WORLD-026; turn-based round ARC-COMBAT-003; equipment ARC-WORLD-028;
durable effects ARC-WORLD-029; quelled rumors and the logbook lists ARC-UI-008 (schema 19); experience ARC-WORLD-030; auto-level schemes ARC-WORLD-036 (schema 20); the hero's skills in play ARC-SKILLS-002 (schema 21); item durability ARC-WORLD-053 (schema 22); schematic knowledge ARC-TECHNOLOGY-003 (schema 23); made items ARC-TECHNOLOGY-004 (schema 24); the spell book's mastered college ARC-MAGIC-004 (schema 25); the hero's fate points ARC-WORLD-054 (schema 26, [fate points](fate-points-runtime.md)); the wait timer and follower loyalty ARC-WORLD-045 (schema 27, [party runtime](party-runtime.md#wait-timer-and-loyalty-arc-world-045)); the PC's known world-map areas ARC-WORLD-056 (schema 28, [world-map travel](world-map-travel-runtime.md#session-gamesessionowner-gamesessionareascs)); the NPC inventory changes ARC-WORLD-055 (schema 29, [inventory runtime](inventory-runtime.md#npc-inventory-changes-arc-world-055)); the PC's reputations ARC-WORLD-044 (schema 30, [reputation runtime](reputation-runtime.md)); healing with a healing item ARC-SKILLS-005 (schema 31, the items' remaining uses, [heal runtime](heal-runtime.md)). The hero's items placed on the ground ARC-WORLD-059 (schema 32, [placed items](inventory-runtime.md#placed-items-arc-world-059)). The waiting followers' missed level steps ARC-WORLD-047 (schema 33, [auto-level schemes](advancement-runtime.md#viewer-host)). The ammunition stacks ARC-WORLD-066 (schema 34, [ammunition](inventory-runtime.md#ammunition-arc-world-066)). The NPCs' danger records ARC-WORLD-079 (schema 38, [danger state](ai-danger-state.md#snapshot-schema-42-until-schema-64)); their SpellFlee bit ARC-WORLD-082 (schema 40, [morale](ai-morale.md)). The hero's and its followers' concealment ARC-SKILLS-008 (schema 35, [stealth](stealth-runtime.md)). The hero's background id ARC-WORLD-069 (schema 36, [backgrounds](equipment-runtime.md#backgrounds-arc-world-069)). The story-state number ARC-SCRIPT-019 (schema 37, [story state](../scripting/scr-dispatcher-runtime.md#story-state-and-story-actions-arc-script-019-set-s13)). The items the hero gave NPCs and the tracked NPC gold piles ARC-WORLD-071 (schema 39, [NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071)); the made and placed items' complexity ARC-WORLD-083 (schema 41, [made item complexity](inventory-runtime.md#made-item-complexity-arc-world-083)). The character editor's point pool ARC-WORLD-052 ([character point pool](character-point-pool.md)) adds no saved state: its floor lives only while the editor is open; the kept item script slots and the worn items' item event points ARC-SCRIPT-042 (schema 43, [wield events](equipment-runtime.md#wield-events-arc-script-042)).
A Godot-free C# owner in the `Arcanum.Session` assembly that holds one game session: the
[game clock](game-clock-runtime.md), the [global flags](../scripting/global-flag-state-runtime.md),
the actor's placement, map-local changes and the story domain (PC quest states, shared
quest states, known rumors, quelled rumors), the PC's [party](party-runtime.md) (followers, waiting NPCs), the
[player character](starting-hero-runtime.md) (stats, level, alignment, gold and starting kit), the items the PC
took from the world ([inventory](inventory-runtime.md): pickup, loot, negative dialog `IN`), the hero's worn items
([equipment](equipment-runtime.md): wear slots, wield and remove, worn values for combat), the hero's effect list
([durable effects](durable-effects-runtime.md): numbered effects with a cause, timed ENDs on the event queue), the seeded gameplay
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
(ARC-WORLD-011) writes and reads a snapshot file atomically; the [save list](#save-list-arc-ui-010)
(ARC-UI-010) keeps a folder of such slots with their details and previews. There is no
timed autosave (the original has none either) and no original `.gsi`/sector-diff format.

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
| `GameSessionOwner.Restore(GameSessionSnapshot, GameSessionRestoreOptions)` | Checks the options' rule mode (undefined is `InvalidDescriptor`; the snapshot holds none), schema, world, module, the exact clock profile, actor, map keys, budgets, story, party, hero, inventory, life, reactions and the pending events (under the mode's event profile) **before** building anything, then rebuilds clock (saved time, pause, revision) and GF (saved domain, known values, revision, new epoch). The generator is the saved one under Steamwoven and the options' running `LoadRandom` under Original (GI6, ARC-DEV-077, [session randomness](session-random-runtime.md)). Never touches an existing owner. |
| `Version` / `PlaceActor` / `SetMapChange` / `ClearMapChange` | Actor and map mutations take the expected `GameSessionVersion` (owner identity + revision). Input validation, then version check, then NoChange, then budget, then revision capacity; every refusal leaves state untouched. Each semantic change adds exactly one revision. |
| `ApplyChanges(expected, actor?, changes, moveList?)` | ARC-WORLD-017: one guarded atomic step over the actor (when given) and a list of `MapChangeEdit(Map, Key, Value?)` (null clears the key). Validates the actor, every map and key and that no key is listed twice, then the version; NoChange when nothing would differ; the budget is checked on the state after the whole step (an update or a move of a key never needs room); then revision capacity, then (ARC-WORLD-024) the move of the followers' pending timers (`EventConflict`, `InvalidEventPass`). A refusal applies nothing; otherwise everything applies with one revision. The viewer's map transfer carries the PC's followers with it ([companion map change](companion-follow-runtime.md#map-change-arc-world-017)). ARC-DEV-073 (MIG10): with `moveList` (`QueryTransferMoveList(SessionTransferFacts)`) the step is also the party side of the PC's change of place in the same revision: the listed followers are ordered to wait (unconscious ones; their timers stay on the old map), FM6's links are cleared for the travellers, and on another map the travellers' events and AI ticks move with the PC; a list that does not match the party is `InvalidCompanion`, timers that do not fit `EventBudgetExceeded`. |
| `QueryActor` / `QueryMapChange` / `QueryMapChanges` | Current placement; one value or `null` for "at loaded baseline"; a detached, canonically ordered copy of one map's changes. |
| `QueryClockSnapshot` / `AdvanceGameTime` / `SetPaused` | Forwarded to the session's clock with the clock's own token contract. Clock changes do not move the session revision. |
| `QueryLighting` / `RecordLighting` | Applied ambient lighting (ARC-WORLD-010, FR-LIGHT-012): `SessionLightingState(SchemeId ≥1, Hour 0..23)` or `null` when none was recorded. `RecordLighting` refuses (`InvalidLighting`) a value whose hour is not the clock's current hour, and a changed `AdvanceGameTime` that leaves the recorded hour clears the record, so a capture never pairs a time with another hour's scheme (it carries `null` until the host records the new hour). Equal value is NoChange. It does not move the session revision. |
| `Flags` / `FlagVersion` | A forwarding `IVersionedGlobalFlagOwner` view of the session's GF owner (reads, writes, current version, `GlobalFlagAdapter`, dialog conversations; no Dispose or MarkFailed) and its current version. GF changes do not move the session revision. |
| `SetQuestState` / `LearnRumor` | Story mutations with the same expected-version guard and refusal order (input, version, NoChange, revision capacity). One revision per change. See [Story domain](#story-domain). |
| `QueryQuest` / `QueryGlobalQuest` / `QueryRumor` / `QueryStory` | PC quest record or `null` (never changed); shared state (`Accepted` by default); rumor time or `null`; a detached ascending copy of the whole domain. A number outside the source range throws `ArgumentOutOfRangeException`. |
| `QuellRumor` / `QueryRumorQuelled` / `QueryQuestLog` / `QueryRumorLog` | ARC-UI-008: mark a rumor quelled (game-wide, same guard, one revision) and read the mark; the PC's logbook lists of quests and rumors in the original's order (see [Story domain](#story-domain)). |
| `QueryCompanion` / `QueryParty` / `JoinParty` / `ForceJoinParty` / `UnwaitParty` / `WaitParty` / `LeaveParty` / `SetSpreadOut` | Party domain (ARC-WORLD-013) with the same guard and one revision per change; join and unwait run the follow check and return its reason. See [party runtime](party-runtime.md). |
| `AttachMapInventory` / `QueryMapInventory` / `QueryPcInventory` / `PickUpItem` / `LootItem` / `GiveItemToPc` | Inventory domain (ARC-WORLD-014): a host-attached T0 seed per map (not state, no revision) and the PC's taken items; transfers take the expected version, refuse before any effect and add one revision each. See [inventory runtime](inventory-runtime.md). |
| `QueryEquipment` / `QueryHeroEquipmentCombat` / `Equip` / `Unequip` | Equipment domain (ARC-WORLD-028): the hero's worn items, their part of its combat values (armour class and resistance adjustments, light, the attack it strikes with, weapon speed and attack cost), wielding or wearing an item it holds under the wear check with the host's art and insert checks, and taking it off; one revision per change. The worn normal resistance enters the hero's life facts. See [equipment runtime](equipment-runtime.md). |
| `QueryHeroEffects` / `AddHeroEffect` / `AddHeroTimedEffect` / `RemoveHeroEffect` / `RemoveAllHeroEffects` / `RemoveHeroEffectsCausedBy` / `EndHeroTimedEffect` | Durable effects (ARC-WORLD-029): the hero's effect list after its innate entries, adding and the source's four removals (the first entry of a number whatever its cause), timed effects whose END is a saved event, and an END run early; one revision per change. A change of a maximum moves the hero's damage as the source does; the stats, maxima and reaction effects reach the character owner, the life facts and the reaction facts. See [durable effects runtime](durable-effects-runtime.md). |
| `QueryHeroBlessings` / `AddHeroBlessing` / `RemoveHeroBlessing` | Blessings and curses (ARC-WORLD-035): the hero's two lists with the game time each was received, the add (0x4C42F0, 0x4C3E40: held once, the effect added with the bless or curse cause) and the removal (0x4C4450, 0x4C3FA0: the first effect entry of its number removed); one revision per change. See [durable effects runtime](durable-effects-runtime.md#blessings-and-curses-arc-world-035). |
| `QueryHero` / `QueryHeroStats` / `QueryHeroDialogFacts` / `QueryGold` | Player character (ARC-WORLD-015), read-only: saved fields, the character owner's stats under a context, the DLG reply facts, the PC's gold; `null` (gold 0) for a session without a hero. See [starting hero runtime](starting-hero-runtime.md). |
| `Roll` / `QueryRandom` / `CombatRolls` / `SpellRolls` | Session randomness (ARC-WORLD-019): one labelled uniform roll in an inclusive range, the generator's seed and position, and the generator as the attack resolver's and the magic rules' roll source (ARC-MAGIC-005). A roll takes no expected version and does not move the revision. See [session randomness](session-random-runtime.md). |
| `QueryHeroLife` / `QueryLife` / `QueryLifeRecord` / `ApplyDamage` / `Kill` / `Heal` / `RecoverFatigue` / `RestHeal` / `PoisonDamageTick` / `PoisonRecoveryTick` / `ClearStun` | Life domain (ARC-COMBAT-002): the hero, or an NPC with the host's facts and loaded state; mutations take the expected version, refuse before any roll, add one revision per change and draw their knockout and second-arm rolls from the session generator. See [life state runtime](life-state-runtime.md). |
| `QueryHeroReactionFacts` / `QueryReaction` / `QueryReactionRecord` / `QueryReactions` / `AdjustReaction` / `SetReaction` / `ReactToPcHit` / `BeginReactionConversation` / `EndReactionConversation` / `ForgetReaction` | Reaction domain (ARC-WORLD-026): an NPC's effective reaction to the PC under the caller's facts, and its remembered and conversation slots; mutations take the expected version, refuse before any change and add one revision per changed record. See [NPC reaction runtime](npc-reaction-runtime.md). |
| `QueryCombatRound` / `StartCombatRound` / `EndCombatRound` / `ConsumeActionPoints` / `EndCombatTurn` / `CompleteCombatActions` / `AddCombatant` / `QueryActionPointCheck` / `QueryCombatActionGate` | Turn-based round (ARC-COMBAT-003): participants from the host's `ISessionCombatWorld` scan, action points, charges with the hero's overexertion, subturns and the end of a round one game second later; mutations take the expected version, decide the step on a copy, refuse before any effect and add one revision when the round changes. While a round is active an ordinary `AdvanceGameTime` is NoChange. See [turn-based round runtime](turn-based-round-runtime.md). |
| `QueryHeroAttack` / `AttackCritter` / `QueryNpcKilled`, `SessionCritterCombat.From` | The hero's attack (ARC-COMBAT-005): an NPC's combat values from its stored fields, what the hero attacks with, one attack resolved and applied with its experience, and whether the session killed an NPC (then `LootItem` and `QueryMapInventory` count it dead). No snapshot change. See [hero attack runtime](hero-attack-runtime.md). |
| `QueryCombatMode` / `QueryCombatModes` / `SetCombatMode` / `ToggleHeroCombatMode` / `LeaveHeroCombatMode` / `QueryCanExitCombatMode` / `SwitchTurnBased` / `ChargeCombatRun` | Combat mode (ARC-COMBAT-004): the hero's and NPCs' flags with the source's toggle refusals, the PC's key and exit gate, the turn-based round started or ended by the hero's change, the setting switch, and the run step's fatigue charge; a death leaves combat mode. See [real-time combat runtime](real-time-combat-runtime.md). |
| `QuerySound` / `PlaySoundScheme` / `EnterMapSound` / `EnterSectorSound` / `FinishSoundTransition` / `LockSound` / `UnlockSound` / `AttachSoundSchemes` | Music and ambient (ARC-WORLD-040, [contract](session-sound-runtime.md)): the original's two scheme slots, the transition restore, combat music (started and stopped by combat mode) and the movie lock; a game load takes the loaded map's default through `GameSessionRestoreOptions.SoundMapDefault`. |
| `Rules` / `SelectRules(mode)` | Rule mode (ARC-WORLD-023, [contract](rule-modes.md)): the active `GameRules` (Steamwoven or Original) every domain reads at its decision, and an immediate switch (`Changed`, `NoChange`, `Rejected` for an undefined mode). Starts from `GameSessionDescriptor.Rules` or `GameSessionRestoreOptions.Rules` (default Steamwoven). A setting, not state: no revision, not in the snapshot. |
| `QueryEvents` / `ProcessEvents(world, budget)` | Event queue (ARC-WORLD-024): the pending timers in queue order, and one pass at the session's game time on the actor's map with the host's `ISessionEventWorld` for NPC facts. Life steps set their timers in the same step (`EventBudgetExceeded` when they do not fit); `PlaceActor` and `ApplyChanges` to another map move the followers' timers (`EventConflict` on a collision). See [session wiring](game-event-queue-runtime.md#session-wiring-arc-world-024). |
| `Capture()` | One detached `GameSessionSnapshot` of every component at this call boundary. |
| `Dispose()` | Idempotent; disposes the clock and GF owner. Every later call except `Dispose` throws `ObjectDisposedException`. |

A map transition is several separately guarded calls (for example `PlaceActor`,
`SetMapChange`, `AdvanceGameTime`); the caller sequences them, and a refusal partway
leaves the earlier calls applied. The session revision counts actor and map changes
only, so a caller that needs one coherent point captures after the whole sequence.
The actor and any number of map changes can be applied as one step with `ApplyChanges`
(ARC-WORLD-017); clock, flags, story, party, inventory, equipment, effects, life and reactions are not part of it.

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

Limits (`GameSessionLimits`) can be lowered, never raised: at most 200 maps with
changes (the original's map list size), 262,144 changes per map and 524,288 in total
(ARC-DATA-029: room for a change of every object of the largest map and of all installed
maps, see [limits census](limits-census.md); before, 64, 4,096 and 16,384). Updating an existing key is
allowed at capacity; a new key or map over capacity is `BudgetExceeded`.

### Story domain

ARC-SCRIPT-006 adds the state the original keeps for quests and rumors, with the source
rules (quest state set (0x4C4CB0, 0x4C4D20, 0x4C4E60), quest reset (0x4C45C0, 0x4C4620), rumor known (0x4C57E0, 0x4C58A0, 0x4C58D0)):

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
  The record is stamped with the session clock's current game time. A `Completed` request
  that passes these checks first gives the hero the quest's experience when the session has
  experience data and a hero, in the same revision ([experience](experience-runtime.md),
  ARC-WORLD-030); the overload with `out SessionExperienceAward?` returns it.
- **Rumors** are numbered 1000..2999. `LearnRumor` records the current game time once;
  learning a known rumor is `NoChange` and keeps the first time.
- **Quelled rumors** (ARC-UI-008): `QuellRumor(expected, rumor)` sets a game-wide mark, as the original's dialog `RQ`
  does with no participant (ledger [LB11–LB12](../research/ledger/ui-audio.md#logbook-arc-ui-008)); the PC
  need not know the rumor, quelling a quelled rumor is `NoChange`, nothing clears it, and a number outside 1000..2999 is
  `InvalidRumor` (the original writes it unchecked).
- **Logbook lists** (ARC-UI-008, LB6 and LB9): `QueryQuestLog` lists every PC record whose stored state is not `Unknown`
  (a quest botched while still `Unknown` is left out) with its effective state and time; `QueryRumorLog` every known rumor
  with its time and quelled mark. Both are ordered by time, oldest first; equal times by number (the original's
  run-time library sort is not stable and its order for them is not modelled). Reading them changes nothing.
- Quest alignment adjustments and sound are **not** applied: they need quest data
  and their own owners. The AREA viewer applies the reaction rule of the
  source (accepting lifts the NPC's reaction to 41, completing adds 10) in its own port.

Invalid numbers or states are `InvalidQuest`, `InvalidQuestState` or `InvalidRumor`.
`Restore` also refuses story records the rules cannot produce: a time after the saved
clock (`InvalidSnapshot`), a PC record `Completed`/`OtherCompleted` without shared
`Completed`, a botched one without shared `Botched`, or any other PC record under a shared
state that left `Accepted` (`InvalidQuestState`). A shared state without a PC record is kept,
as the source's shared state is not owned by one PC.


## Snapshot schema 62

`GameSessionSnapshot` (only from `Capture` or `TryDecode`) carries `SchemaVersion`,
`Identity` (GF world/module), session `Revision`, `Clock` (`ProfileBinding`, timestamp,
pause, revision; no owner identity), `Flags` (revision and every admitted flag in
ascending order, `null` = Unknown), `Actor`, `Maps` (ascending map, each with
changes in canonical order: scope, sector, entity, property, ordinal; since schema 62 without the fields of mobile map-data objects, which are `Objects.State`), `Story`
(PC quests, shared quests that left `Accepted`, known rumors, quelled rumor numbers, known world-map areas; each ascending; then the last discovered area, 0 for none; then the PC's reputations in the order gained, [reputation runtime](reputation-runtime.md#snapshot-schema-30); then the story-state number since schema 37, [story state](../scripting/scr-dispatcher-runtime.md#story-state-and-story-actions-arc-script-019-set-s13)), `Party`
(the follower array in order, then waiting and flagged free NPCs, each with its loyalty marks; [party runtime](party-runtime.md#snapshot-schema-4)),
`Hero` (the player character or `null`; [starting hero runtime](starting-hero-runtime.md#snapshot-schema-6); its fate points and armed fates, [fate points](fate-points-runtime.md#snapshot-schema-26); its background id since schema 36, [backgrounds](equipment-runtime.md#backgrounds-arc-world-069), and the background's description number since schema 54, [setting and clearing](equipment-runtime.md#setting-and-clearing-the-background-arc-world-097); its player name and portrait since schema 56, [new-character window](../rendering/terrain-viewer.md#new-character-window-arc-ui-030)),
`Inventory` (the PC's gold, the starting kit, the taken and the made items in list order, the made items added to NPCs by NPC entity, the hero's items placed on the ground in placing order, and since schema 34 the ammunition stacks by entity ([ammunition](inventory-runtime.md#ammunition-arc-world-066)), since schema 39 the kit and taken items the hero gave NPCs and the tracked gold piles by entity ([NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071)), since schema 41 each made and placed item's complexity ([made item complexity](inventory-runtime.md#made-item-complexity-arc-world-083)), since schema 43 the item script slots runs changed ([wield events](equipment-runtime.md#wield-events-arc-script-042)), since schema 46 the junk piles the session made in the order they were made ([junk piles](inventory-runtime.md#junk-piles-arc-world-063)), since schema 48 the items scripts created on the ground among the placed items, source `created` ([template sector plants](template-sector-plants-runtime.md)), since schema 50 the identified items by entity and a container flag on each added list ([barter runtime](barter-runtime.md#session)), since schema 51 the highest made number a restock discarded ([restock event](barter-runtime.md#restock-event-arc-world-094)), since schema 52 the hero's grid slots ([hero grid](inventory-runtime.md#hero-inventory-grid-arc-ui-019)), since schema 55 the containers with flag 0x200 generated once, by map and entity ([generated-once containers](barter-runtime.md#generated-once-containers-arc-world-098)), since schema 57 the worn items of the NPCs the session dressed, by NPC entity and slot ([NPC best items](barter-runtime.md#npc-best-items-after-a-regeneration-arc-world-103)), since schema 58 the pools of blood deaths left, in the order they were left ([pool of blood](hero-attack-runtime.md#pool-of-blood-arc-render-041)); [inventory runtime](inventory-runtime.md#snapshot-schema-5), [placed items](inventory-runtime.md#placed-items-arc-world-059)),
`Objects` (since schema 61: the junk piles and the pools of blood the session made, each in the order made, with the junk and pool number marks; before, they were in `Inventory`; [object directory](../architecture/object-directory.md#mig6d-3-what-is-built); since schema 62 also `State`: the recorded fields of mobile map-data objects by entity then property, each with the map its record names and its value; before, they were entity changes in `Maps`; [object state](../architecture/object-directory.md#mig7a-what-is-built)),
`Equipment` (the hero's worn items in slot order, since schema 43 each with its item event points, since schema 49 with its item spells mark; [equipment](equipment-runtime.md#snapshot-schema-12), [wield events](equipment-runtime.md#wield-events-arc-script-042), [item spell triggers](item-spell-triggers.md#saved-state-arc-magic-022)),
`Durability` (the changed items' conditions, the deleted items, the spent items' remaining uses and, since schema 49, the items' spent spell charges, each in ordinal order; [item durability](item-durability-runtime.md#snapshot-schema-22), [item spell triggers](item-spell-triggers.md#saved-state-arc-magic-022)),
`Schematics` (the hero's found schematics in registration order; [schematic knowledge](schematic-knowledge-runtime.md#session)),
`Effects` (the hero's durable entries in list order and the rule of each number; [durable effects](durable-effects-runtime.md#snapshot-schema-13)),
`Experience` (the changed NPCs' experience worth and pool in ordinal order; the hero's experience is in `Hero`; [experience](experience-runtime.md#snapshot-schema-14)),
`Advancement` (the levelled NPCs' sheets and schemes in ordinal order and, since schema 33, the waiting followers' missed level steps; the hero's fields are in `Hero`; [auto-level schemes](advancement-runtime.md#snapshot-schema-20)),
`Random` (the generator's seed and draws made; [session randomness](session-random-runtime.md#snapshot-schema-7)),
`Life` (the hero's life record and the changed NPCs' in ordinal order; [life state](life-state-runtime.md#snapshot-schema-8)),
`Reactions` (the changed NPCs' reaction slots in ordinal order; [NPC reaction](npc-reaction-runtime.md#snapshot-schema-10)),
`Combat` (the active round or `null`, with the session's subturn starts since schema 59; [turn-based round](turn-based-round-runtime.md#snapshot-schema-11), [load rule](turn-based-round-runtime.md#a-round-at-a-load-gr19-arc-dev-078)),
`CombatMode` (the hero's flag and the NPCs in combat mode; [real-time combat](real-time-combat-runtime.md#snapshot-schema-15)),
`Concealment` (the hero's flag and its concealed followers in ordinal order; [stealth](stealth-runtime.md#snapshot-schema-35)),
`Danger` (the NPCs' danger bits, handles and enemy lists in ordinal order; [danger state](ai-danger-state.md#snapshot-schema-42-until-schema-64)),
`Sound` (the music and ambient scheme slots, the transition restore and combat music; [session sound](session-sound-runtime.md#snapshot-schema-18)),
`Events` (the registry version, the next sequence and the pending timers, the AI ticks since schema 42; [event queue](game-event-queue-runtime.md#snapshot-schema-9), [AI scheduler](ai-scheduler.md))
`Hotkeys` (since schema 53: the hotkey bar's ten entries and two recent actions, each its kind, item and index; [hotkey bar](spell-cast-host.md#the-hotkey-bar-arc-ui-022)) and `Scripts` (since schema 67: the non-zero global variables as index and value pairs, ascending, 0..1999; the hero's flag array as non-zero 32-bit elements, element n holding flags 32n..32n+31, n 0..2047; the hero's variables, 0..65535; [script stores](#script-stores-arc-dev-098)) and `Lighting` (applied scheme and hour, or `null`). Collections are read-only copies.

`GameSessionSnapshotCodec.Encode` writes one canonical compact JSON text:

```json
{"format":"arcanum-steamwoven-session","schemaVersion":29,
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
          "rumors":[{"rumor":1200,"timestampMs":54000000}],"quelledRumors":[1200],"knownAreas":[],"lastArea":0},
 "party":{"followers":[{"entity":"oid:…","forced":false,"spreadOut":false,"loyalty":0}],"others":[]},
 "hero":null,
 "inventory":{"gold":0,"kit":[],"pc":[{"entity":"oid:…","map":79,"type":"Armor","prototype":3804,"name":2804,"description":32804}],
              "made":[],"npcs":[]},
 "equipment":[],
 "effects":{"hero":[],"definitions":[],"blessings":[],"curses":[]},
 "experience":{"npcs":[]},
 "advancement":{"npcs":[],"missed":[]},
 "random":{"seed":"0000000000000000","draws":0},
 "life":{"hero":null,"critters":[]},
 "reactions":{"records":[]},
 "combat":null,
 "combatMode":{"hero":false,"critters":[]},
 "sound":{"slots":[0,0],"restorePending":false,"restore":[0,0],"combatMusic":false,"preCombat":[0,0]},
 "events":{"registry":1,"nextSequence":1,"pending":[]},
 "lighting":{"schemeId":1,"hour":15}}
```

(Line breaks added here; the encoder writes no whitespace.) `TryDecode` returns a
`SnapshotDecodeResult` with a typed `SnapshotDecodeFailure` and a JSON path; it never
throws on bad text and never builds an owner. It refuses: input over 96 MiB characters (8 MiB before ARC-DATA-029)
(`TooLarge`); invalid JSON, comments, trailing commas or depth over 8 (`MalformedJson`);
another `format` (`WrongFormat`) or `schemaVersion` (`UnsupportedSchema`; the older readable schemas are upgraded first, [older saves](#older-saves-arc-ui-037)); unknown,
duplicate or missing fields; wrong JSON types; fractions, exponents and out-of-range
integers; non-canonical GUIDs, enum names or identifiers (`OutOfRange`); unsorted or
repeated flags, maps, change keys, quests, rumors or quelled rumors (`NotCanonical`); quest or rumor
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
snapshot captured while the Esc menu or a save list held a `Menu` pause restores paused,
and the host releases or re-applies that pause from whether the menu is actually open (ARC-DEV-126: no other window pauses).

**Migration decision: none.** Schema 39 is the only accepted version. ARC-WORLD-071 added the required `given` and `piles` lists to the
inventory ([NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071)); schema 38 is refused like the earlier ones. ARC-WORLD-079 had added the required `danger`
section ([danger state](ai-danger-state.md#snapshot-schema-42-until-schema-64)) and ARC-SCRIPT-019 the story-state number; schemas 37 and 36 are refused like the earlier ones. ARC-WORLD-069 had added the hero's required `background`
([backgrounds](equipment-runtime.md#backgrounds-arc-world-069)); schema 35 is refused like the earlier ones. ARC-SKILLS-008 had added the required `concealment` section
([stealth](stealth-runtime.md#snapshot-schema-35)); schema 34 is refused like the earlier ones. ARC-WORLD-066 had added the required `ammo` list to the
inventory ([ammunition](inventory-runtime.md#ammunition-arc-world-066)). ARC-WORLD-047 had added the required `missed` list to
`advancement` ([auto-level schemes](advancement-runtime.md#snapshot-schema-20)); schema 32 is refused like the earlier ones. ARC-WORLD-059 had added the required `placed` list to the
inventory ([placed items](inventory-runtime.md#placed-items-arc-world-059)); schema 31 is refused like the earlier ones. ARC-SKILLS-005 had added the required `uses` list to
`durability` ([heal runtime](heal-runtime.md#snapshot-schema-31)); schema 30 is refused like the earlier ones. ARC-WORLD-044 had added
the story's required `reputations` ([reputation runtime](reputation-runtime.md)); schema 29 is refused like the earlier ones. ARC-WORLD-055 had added the required `npcs` list to the
inventory ([inventory runtime](inventory-runtime.md#npc-inventory-changes-arc-world-055)); schema 28 is refused like the earlier ones.
ARC-WORLD-056 had added the story's required
`knownAreas` and `lastArea` ([world-map travel](world-map-travel-runtime.md#session-gamesessionowner-gamesessionareascs)); schema 27 is refused like the
earlier ones. ARC-WORLD-045 had added the required `loyalty` field to
every party record ([party runtime](party-runtime.md#wait-timer-and-loyalty-arc-world-045)); schema 26 is refused like the
earlier ones. ARC-WORLD-054 had added the hero's required `fatePoints` and
`armedFates` ([fate points](fate-points-runtime.md#snapshot-schema-26)); schema 25 is refused like the earlier ones, also a
heroless one of the same structure. ARC-MAGIC-004 had added the mastered college (`mastery`, −1
none) to the hero's `advancement` and to each NPC advancement record ([spell book](spell-book-runtime.md#snapshot-schema-25));
schema 24 is refused like the earlier ones, also a heroless one without NPC records of the same structure. ARC-TECHNOLOGY-004 had added the required
`made` list to `inventory` ([schematic assembly](schematic-assembly-runtime.md#made-items-snapshot-schema-24)); schema 23 is refused
like the earlier ones. ARC-TECHNOLOGY-003 had added the required `schematics`
section ([schematic knowledge](schematic-knowledge-runtime.md#session)); schema 22 is refused like the earlier ones. ARC-WORLD-053 had added the required `durability`
section ([item durability](item-durability-runtime.md#snapshot-schema-22)); schema 21 is refused like
the earlier ones. ARC-SKILLS-002 had added the hero's `innateSkills` list
(the race and gender effects' skill changes, [hero skills](hero-skills-runtime.md#snapshot-schema-21)); schema 20 is refused
like the earlier ones, also a heroless one of the same structure. ARC-WORLD-036 had added the hero's `advancement` fields
and the root `advancement` section; schema 19 is refused like the earlier ones (see
[auto-level schemes](advancement-runtime.md#snapshot-schema-20)). ARC-UI-008 had added the quelled rumors (schema 19) and
refused schema 18. ARC-WORLD-040 had added the required `sound` section
and refused schema 17. ARC-WORLD-035 had added the required `blessings` and
`curses` lists to `effects`; schema 16 is refused like the earlier ones (see
[durable effects](durable-effects-runtime.md#blessings-and-curses-arc-world-035)). ARC-DEV-009 had bound the clock profile
to the retail executable (schema 16), ARC-COMBAT-004 added `combatMode` (schema 15) and ARC-WORLD-030 the hero's experience
and `experience` (schema 14).
ARC-WORLD-029 had added the required `effects` section and refused schema 12 (see [durable effects](durable-effects-runtime.md#snapshot-schema-13)).
ARC-WORLD-028 had added the required `equipment` list and refused schema 11 (see [equipment](equipment-runtime.md#snapshot-schema-12)).
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
format was schema 3. A quick-save file of an older schema (ARC-WORLD-011) loads (before ARC-UI-037; since then below schema 56) as "written by an incompatible version" with the
running session kept, and the next F7 overwrites it. The only earlier host (the AREA
viewer) had a one-flag placeholder GF domain, so a migrated session could not run any
dialog. ARC-WORLD-030 added the hero's experience and the NPCs' experience records as schema 14 and refuses schema 13.
ARC-COMBAT-004 added the combat-mode flags as schema 15 and refuses schema 14
([real-time combat](real-time-combat-runtime.md#snapshot-schema-15)). ARC-DEV-009 binds the clock profile to the retail
executable (`sourceRevision` is its SHA-256, [clock policy](game-clock-runtime.md#original-clock-evidence-carried-from-the-res-020-policy)) as schema 16, with no structural change, and
refuses schema 15. ARC-WORLD-035 added the hero's blessings and curses as schema 17 and refuses schema 16. ARC-WORLD-040
added the sound state as schema 18 and refuses schema 17. ARC-UI-008 added the quelled rumors to the story domain as
schema 19 and refuses schema 18 (a quick save of an older schema loads as incompatible; since ARC-UI-037 only below schema 56). ARC-WORLD-036 added the hero's
advancement fields and the levelled NPCs' sheets as schema 20 and refuses schema 19
([auto-level schemes](advancement-runtime.md#snapshot-schema-20)). ARC-SKILLS-002 added the hero's innate skill changes as
schema 21 and refuses schema 20. ARC-WORLD-053 added item durability as schema 22 and refuses schemas 1..21. ARC-TECHNOLOGY-003 added the found schematics as
schema 23 and refuses schemas 1..22. ARC-TECHNOLOGY-004 added the made items as schema 24 and refuses schemas 1..23. ARC-MAGIC-004 added the mastered college of
the hero and the NPC advancement records as schema 25 and refuses schemas 1..24. ARC-WORLD-054 added the hero's fate points as
schema 26 and refuses schemas 1..25. ARC-WORLD-045 added the party records' loyalty marks as schema 27 and refuses schemas 1..26. ARC-WORLD-056 added the PC's
known world-map areas and last discovered area as schema 28 and refuses schemas 1..27. ARC-WORLD-055 added the made items added to
NPCs as schema 29 and refuses schemas 1..28. ARC-WORLD-044 added the PC's reputations to the story domain
as schema 30 and refuses schemas 1..29 (no migration). ARC-SKILLS-005 added the items' remaining uses as schema 31 and refuses schemas 1..30. ARC-WORLD-059 added the hero's placed items as
schema 32 and refuses schemas 1..31. ARC-WORLD-047 added the waiting followers' missed level steps as schema 33 and refuses
schemas 1..32. ARC-WORLD-066 added the ammunition stacks as schema 34 and refuses schemas 1..33. ARC-SKILLS-008 added the hero's and its followers' concealment as schema 35 and refuses schemas 1..34. ARC-WORLD-069 added the hero's background as schema 36 and refuses schemas 1..35. ARC-SCRIPT-019 added the story-state number as schema 37 and refuses schemas 1..36. ARC-WORLD-079 added the NPCs' danger records as schema 38 and refuses schemas 1..37. ARC-WORLD-071 added the given items and the gold piles as schema 39 and refuses schemas 1..38. ARC-WORLD-082 added the SpellFlee danger bit as schema 40 and refuses schemas 1..39. ARC-WORLD-083 added each made and placed item's complexity as schema 41 and refuses schemas 1..40. ARC-WORLD-084 added the CheckWeapon danger bit and the saved AI ticks ([AI scheduler](ai-scheduler.md)) as schema 42 and refuses schemas 1..41. ARC-SCRIPT-042 added the kept item script slots and the worn items' item event points as schema 43 and refuses schemas 1..42. ARC-WORLD-085 added the weight rows as schema 44 and refuses schemas 1..43. ARC-WORLD-086 added the NoFlee danger bit ([AI executor](ai-executor.md)) as schema 45 and refuses schemas 1..44. ARC-WORLD-063 added the junk piles to the inventory as schema 46 and refuses schemas 1..45. ARC-WORLD-088 added the LookForAmmo, LookForWeapon and LookForArmor danger bits ([AI support](ai-support.md)) as schema 47 and refuses schemas 1..46. ARC-WORLD-081 added the created placed items (source `created`, a budget of their own) as schema 48 and refuses schemas 1..47. ARC-MAGIC-022 added the items' spent spell charges and the worn items' item spells mark as schema 49 and refuses schemas 1..48. ARC-WORLD-090 added the identified items and the containers' added lists (a barter window's sales into a merchant's substitute container) as schema 50 and refuses schemas 1..49. ARC-WORLD-094 added the highest made number a restock discarded (`inventory.lastDiscardedMade`, so a new made item never takes a discarded one's number) as schema 51 and refuses schemas 1..50. ARC-UI-019 added the hero's inventory grid (`inventory.grid`) as schema 52 and refuses schemas 1..51. ARC-UI-022 added the hotkey bar (`hotkeys`: ten entries and two recent actions; an item entry's item has no grid slot) as schema 53 and refuses schemas 1..52. ARC-WORLD-097 added the hero's background description number (`hero.backgroundDescription`, the original's field 0x102) as schema 54 and refuses schemas 1..53. ARC-WORLD-100 added the containers with flag 0x200 the session generated once (`inventory.generated`: map and entity, whose source and flag the session then clears) as schema 55 and refuses schemas 1..54. ARC-UI-033 added the hero's player name (`hero.playerName`, its bytes as lowercase hex) and portrait (`hero.portrait`) as schema 56 and refuses schemas 1..55. ARC-WORLD-103 added the worn items of the NPCs a regeneration dressed (`inventory.npcWorn`: each NPC with its slots and items, which replace its map data's inventory locations) as schema 57 and refuses schemas 1..56; since ARC-UI-037 schema 56 is upgraded ([older saves](#older-saves-arc-ui-037)). ARC-RENDER-041 added the pools of blood (`inventory.bloodPools`: entity, critter and its death time, map, cell, pixel offsets, creation time) as schema 58; schemas 56 and 57 are upgraded.
ARC-DEV-078 (GR19) added the session's subturn starts to a saved round (`combat.subturnStarts`) as schema 59; schemas 56 to 58 are upgraded. ARC-DEV-084 (MIG6a) added the junk and pool number high-water marks (`inventory.lastJunkNumber`, `inventory.lastPoolNumber`) as schema 60; ARC-DEV-091 (MIG6d-3) moved the junk piles, the pools and the two marks out of `inventory` into the new `objects` section as schema 61; ARC-DEV-092 (MIG7a) moved the entity changes of mobile map-data objects out of `maps` into `objects.state` as schema 62; ARC-DEV-093 (MIG7b) moved the entity changes of static map-data objects (`sector:S:I` on the map that holds them) there too as schema 63; ARC-DEV-094 (MIG7c) added the object field `npc.pos`, the cell and facing a walking map NPC was left on, as schema 64 (step 63 → 64 moves nothing); ARC-DEV-095 (MIG7d) moved the NPCs' links (combat focus, fleeing from, who hit the NPC last, enemy list) out of the `danger` records into the rows of `objects.links`, each with the map it was written on, as schema 65; ARC-DEV-096 (MIG8) moved the script attachments (`dialog.*`, `use.*`, `scr<point>.*` of `oid:` and `sector:` entities) out of `maps` into `objects.state` as schema 66; ARC-DEV-098 (MIG8, GR29) added the `scripts` section, the global variables and the hero's flags and variables, as schema 67 (step 66 → 67 adds it empty); ARC-DEV-106 (MIG9 S6) added the `residency` section, the open map's sector eviction history (the map and the records `[sector id, game ms]`, oldest first, with every loaded sector stamped with the save's time), as schema 68 (step 67 → 68 adds it empty); ARC-DEV-116 (MIG11 S5) added the object field `npc.teleport`, an NPC's teleport map (FM10, [homecoming](../architecture/change-of-place.md#6-what-the-session-and-viewer-do-for-the-homecoming-of-former-followers-s5)), as schema 69 (step 68 → 69 moves nothing); ARC-DEV-122 (MIG12 S3) added the real and animation stamps of the session's three clocks (`clock.realMs`, `clock.animationMs`, [the session's three clocks](game-clock-runtime.md#the-sessions-three-clocks-arc-dev-122-mig12-s3)) as schema 70 (step 69 → 70: real 1, animation the saved game stamp); ARC-DEV-205 (bug B22) added the town maps revealed whole by the script action unfog town map (`story.revealedTownMaps`, ascending town-map numbers 1..4095, [town-map unfog](../scripting/scr-dispatcher-runtime.md#town-map-unfog-arc-dev-205-set-s24)) as schema 71 (step 70 → 71 adds the empty list); ARC-DEV-208 (MIG14 S2) added the `goals` section, the goal runs of the active map (`nextRunId` and the runs in slot order, each with its owner, flags, due time, pace and goal stack of plain frames; [saved runs](../architecture/saved-runs.md)), as schema 72 (step 71 → 72 adds the empty section); ARC-DEV-210 (MIG14 S4) added the `spells` section, the settled spell runs (Maintain and End actions) with the maintain bar's five slots and the last charged cost, and made the `magic.run` event kind saved (step 72 → 73 adds the empty section). ARC-DEV-230 added the `newspaper` section, the original's newspaper issue queue (25 words and the count, written by the dialog action `np`), and the mobile object field `npc.origin`, an NPC's set origin (step 73 → 74 adds a new game's queue); schemas 56 to 73 are upgraded. ARC-WORLD-122 (schema 75) added the cells of town maps the PC revealed by walking to the story section (`story.townMapCells`, a bitmap per town map as lowercase hex, ledger WM78, UF4); schema 74 is upgraded with the empty list. ARC-UI-081 (schema 77) added the PC's key numbers to the story section (`story.heroKeys`, in the order they were added, repeats kept, at most 3000, ledger LB20; the session keeps the list, not the key ring item); schema 76 is upgraded with the empty list. ARC-UI-082 (schema 78) added the PC's kill statistics and injuries to the story section (`story.killLog`: the total, six [description number, value] slots, the injuries as [description number, kind]; ledger LB21); schema 77 is upgraded with a new game's empty log. ARC-DEV-266 (schema 79, MIG8 leftover) moved the PC's flag elements and variables (fields 272 and 273 of the PC, SA14) from the `scripts` section onto the hero's record (`hero.scripts`: `flags` and `variables`, the non-zero cells as [index, value] ascending); `scripts` keeps the global variables alone, and a save without a hero has nowhere to hold PC cells. The step from 78 moves both lists unchanged; a schema 78 save without a hero that held PC cells, which its load refused, is refused by the step (`NotCanonical`). ARC-DEV-288 (schema 80, MIG14 S7) added the `flights` section: the projectiles in flight on the active map (each `projectile:n` with its cell, offsets and flight: the line's two tiles, the next sample, the pixel position, the steps, the shot's flags, the art, the destination and the launch's rows by entity), the shots they carry (the hero's with the weapon, skill and reach it was shot with and the decided check's flags; an NPC's arrow with the shooter's attack values; an NPC's throw with its thrower and item) and the counters `lastProjectile` and `nextShot`; their goal 36 runs are in `goals`. The step from 79 adds the empty section with both counters at 0 ([saved runs](../architecture/saved-runs.md#35-projectiles-missiles-and-spell-runs)). ARC-UI-104 (schema 81) added the notes of the town maps to the `story` section (`story.townNotes`: per town map, ascending, `nextId` and `notes`, each `id`, `flags`, `x`, `y`, `type`, `text`; at most 199 notes per town, ids ascending from 200 below `nextId`, text of at most 249 characters; the original's `<town>.tmn`, ledger WM91, WM128); the session API is `QueryTownNotes` and `RecordTownNotes`. ARC-UI-110 (schema 82) added the notes of the world map's two views to the same section (`story.mapNotes`: per view, ascending, `view` 1 the map view and 2 the continent view, `nextId` and `notes` of the town notes' shape; at most 199 per view, pixels 0..4096 of the world-map picture; Steamwoven rules, no original counterpart); the session API is `QueryMapNotes` and `RecordMapNotes` (failure `InvalidMapNotes`); the step from 81 adds the empty list. ARC-DEV-302 (schema 83, MT13) added the flags the spell components wrote on critters to the `spells` section (`spells.objectFlags`: per entity, ascending, the hero `pc` or a mobile object `oid:`, `spellSet`, `spellCleared`, `critterSet` and `critterCleared`, a change over the spell flags and the critter flags the critter's data stores; [hold flags](spell-cast-host.md#hold-flags-arc-dev-302)); the step from 82 adds the empty list, and the session API is `QueryObjectFlags` and `WriteObjectFlag`. ARC-DEV-289 (schema 84, MIG14 S8) added the spell missiles in flight to the `flights` section (`lastMissile` and `missiles`: each `missile:n` with its cell, offsets and flight: the caster's entity, the spell run it releases, the spell, the target, the faced art, the steps per tick, the pace, the line's two tiles, the next sample, the pixel position, the line's steps, the steps taken, the art's frames and the legs) and saves the NPCs' casting goals with the spell runs before their BEGIN they name, the missiles' goal 28 runs with them and a parentless run with its pending BEGIN event; the step from 83 adds the empty list with the counter at 0 ([saved runs](../architecture/saved-runs.md#35-projectiles-missiles-and-spell-runs)). ARC-DEV-347 (E1-a, schema 85) added the created scenery and eye-candy objects to the `objects` section (`objects.created`: each `scenery:n` or `eyecandy:n` with its kind, prototype, map, cell, offsets, creation time, spell flags, summoner and owning spell run, and `objects.lastCreatedNumber`, the one counter of both kinds); schemas 56 to 84 are upgraded (step 84 → 85 adds the empty list and the counter at 0; [object directory](../architecture/object-directory.md#e1-a-created-scenery-and-eye-candy-objects-arc-dev-347)) ARC-DEV-351 (schema 86) added the object field `obj.teleported` to `objects.state`: the original's OF_TELEPORTED mark (flag 0x40000000 of the object flag word, ledger TG9), written on every object a change of place carries to another map, whose record names the map the object stands on and holds the placement it was written on, and taken off by the open of that map (`GameSessionOwner.TakeTeleportedMarks`), which notifies each object (the script of the tile it stands on, mode 2 of 0x444270); schemas 56 to 85 are upgraded (step 85 → 86 moves nothing). ARC-DEV-348 (schema 87) adds the effect lists of NPCs to the `effects` section (`critters`: entity, entries, rules in use); the step from 86 adds the empty list ([NPC effect lists](durable-effects-runtime.md#npc-effect-lists-arc-dev-348)). ARC-DEV-358 (schema 88) adds the object-flags change (`objectSet`, `objectCleared`, over `obj_f_flags`) to every row of `spells.objectFlags`: the side effects of the spell flags (invisible, translucent, shrunk, water walking, stoned) and don't-draw write it ([the ObjFlag component](spell-cast-host.md#the-objflag-component-and-the-component-table-arc-dev-358-ma-1)); the step from 87 adds both words as 0 to each row, and schemas 56 to 87 are upgraded.
Adding another domain needs the next free schema number and its upgrade step from the previous schema
([older saves](#older-saves-arc-ui-037)).

### Script stores (ARC-DEV-098)

MIG8 slice 3 (GR29, TM9, SA13, SA14) gives the session the script stores the global flags and the story number did not cover: the **2000 global variables** (32-bit, zero at a new game) and the **hero's flag and variable arrays** (fields 272 and 273 of the PC: 65,536 flags in 2,048 words, flag *n* being bit *n* mod 32 of word *n* / 32, and 65,536 variables). `QueryGlobalVariable`/`SetGlobalVariable`, `QueryPcVariable`/`SetPcVariable`, `QueryPcFlag`/`SetPcFlag` and `QueryScriptState` (the stores as saved). A write is versioned like the others (`StaleVersion`, `NoChange` for the same value, one revision when it changes); a zero is not stored, so the saved lists are canonical. The original does not check an index; the session refuses one outside its store with `InvalidScriptIndex` (a query throws `ArgumentOutOfRangeException`). The PC accessors act only on a PC (SA14): a session without a hero reads 0 and refuses the write with `PcScriptsUnavailable`, and a save that has a PC store but no hero is `InvalidSnapshot` on `Restore`. The decode refuses an index outside the store, an unsorted or repeated index and a zero value (`OutOfRange`, `NotCanonical`). Since ARC-DEV-100 (MIG8 slice 4, set S21) the interpreter reads them through the host (`IScrStoreQueries`) and the viewer's dialog and use hosts apply a run's changes with `ApplyScriptStores` (a session without a hero ignores the PC's); since ARC-DEV-152 the item events read and write them too (a move's runs see each other's writes, the move applies them in its one revision, a session without a hero drops the PC's) and the dialog's GV, PV and PF go through `SessionDialogScriptStorePort` (ledger DG1–DG4); **not done:** the PC arrays are still a session domain for the hero, not yet fields of the hero's directory entry (GR29 option B of the gap review).

### Older saves (ARC-UI-037)

Since the first packaged build (ARC-DEV-047, schema 56) a newer build loads the saves of older builds.
`GameSessionSnapshotCodec.TryDecode` reads a text of a schema from `OldestReadableSchemaVersion` (56) up to the one
before the current as follows: it parses the text, applies one upgrade step per schema (`GameSessionSnapshotUpgrade.cs`;
step N turns the text of schema N into the text of schema N + 1 by adding the fields schema N + 1 added, with the value an
older session means, and sets the schema number), then decodes the result with every check of the current schema. A step
never invents what the older schema could not hold: a field the older schema cannot carry is `UnknownField`, a section the
step extends that is missing or not an object is `MissingField` or `WrongType` at its path, a repeated property in an object a step unpacks (the root, a section it extends)
is `DuplicateField` at `$` (elsewhere the strict decode reports it at its path), a lone surrogate escape `MalformedJson` at `$`. Schemas below the oldest readable one, and newer
schemas, stay `UnsupportedSchema`. The save file is not rewritten by a load; the next save of the loaded session writes the
current schema. `GameSaveLibrary` and `GameSessionSaveSlot` are unchanged: they read through the codec.

| From | Step | Meaning |
| --- | --- | --- |
| 56 → 57 | adds `inventory.npcWorn: []` | No NPC was dressed by a regeneration (ARC-WORLD-103), so every NPC keeps its map data's inventory locations, as under schema 56. |
| 57 → 58 | adds `inventory.bloodPools: []` | No death left a pool of blood (ARC-RENDER-041); the deaths of such a save leave none later either (the viewer leaves pools only for deaths it sees happen). |
| 58 → 59 | adds `combat.subturnStarts: 0` to a saved round (`combat: null` stays) | The older load restored no subturn starts (GR19, ARC-DEV-078); a missing `combat` is `MissingField`, one that is neither null nor an object `WrongType`. |
| 59 → 60 | adds `inventory.lastJunkNumber: 0` and `inventory.lastPoolNumber: 0` | The older load's rule: the marks are the highest junk pile and pool numbers held at the load (MIG6a, ARC-DEV-084; [object directory](../architecture/object-directory.md)). |

**Rule for a schema bump.** A change that raises `CurrentSchemaVersion` adds the step from the previous schema in the same
PR, with a test that the previous schema's text decodes to the current text it stands for; `SnapshotUpgradeTests` fails
while any schema from the oldest readable one has no step. Dropping old saves instead (raising `OldestReadableSchemaVersion`)
needs the owner's agreement.

## Save slot (ARC-WORLD-011)

`GameSessionSaveSlot` (same assembly, Godot-free) is one file that holds exactly the
`GameSessionSnapshotCodec.Encode` text of a snapshot. It knows nothing about the
snapshot's sections, so a domain added to the snapshot and its codec (live clock,
dialog state, a schema bump) is saved and loaded by the slot without changing it.

| Entry | Contract |
| --- | --- |
| `new GameSessionSaveSlot(path)` / `QuickSave(directory)` | An absolute file path; `QuickSave` is `<directory>/quicksave.json`. Invalid paths throw (programming error). |
| `Write(snapshot)` | Encodes, decodes the text back in memory (a save this build cannot load is never written), writes `<path>.tmp`, flushes it to disk, reads it back and compares, then moves it over the slot. Creates the directory. Returns `SaveSlotWriteResult` (`None`, `TooLarge`, `WriteFailed`, `VerifyFailed`); on any failure the temporary file is removed and the previous save is untouched. A stale temporary file from an interrupted save is overwritten. |
| `Read()` | `SaveSlotReadResult` with the decoded snapshot or a typed failure: `NoSave` (no file or directory; a directory or unreadable entry at the path is `ReadFailed`), `TooLarge` (over 96 MiB; the codec writes ASCII, so its character limit is the byte limit, also enforced while reading), `ReadFailed`, `NotUtf8` (strict UTF-8), `Corrupt` with the codec's `SnapshotDecodeFailure` and JSON path (truncated, wrong format, unsupported schema, any field error). Never throws on content. |

The slot does not restore: the caller passes the snapshot to `Restore` with its own
expectations. Durability: file data is flushed before the rename; the directory entry
is not separately synced. One process per slot; there is no cross-process lock.

## Save list (ARC-UI-010)

`GameSaveLibrary` (same assembly, Godot-free) is one folder of saves for the
[save and load windows](../rendering/terrain-viewer.md#save-list-arc-ui-010) and F7/F8. Each save
`NAME` is a snapshot slot `NAME.json` (`GameSessionSaveSlot`, unchanged), a details file
`NAME.meta.json` and an optional preview `NAME.png`. The rules are the original's
([ledger SL1–SL8](../research/ledger/ui-audio.md#save-list-arc-ui-010)). The details file (schema 2 since ARC-UI-067) holds the
description, hero name, level, place, map, game date and time with seconds, the hero's portrait id and the story state, the facts the
original's save info keeps ([MW22](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)); a schema 1 file still reads, with
portrait 0, no story state and second 0.

| Entry | Contract |
| --- | --- |
| Names | `quicksave` is the auto slot (the original's `SlotAuto`, described "Auto-Save"; the ARC-WORLD-011 file, so older quick saves are listed); numbered saves are `slot0000`..`slot9998`. Any other file is ignored; a bad name is `BadName`. |
| `List()` | The auto slot first, then newest first by the snapshot file's last write time (ties: the higher name first), as the original's save window sorts by file date. Each `GameSaveEntry` has its byte count, date, preview flag and `GameSaveDetails`: `Present`, `Missing` (no details file), `Damaged` (not strict UTF-8, over 16 KiB, not exactly the written properties, out of bounds) or `Stale` (written for another snapshot: its recorded byte count differs). `DisplayDescription` falls back to "Auto-Save" or the slot name. Bounded to 10,000 entries (`Truncated`); a folder that cannot be listed is `ListFailed`; a missing folder is an empty list. |
| `Latest()` | The newest save by file date, the auto slot included (the original's latest-save helper behind F8), or null. It reads file dates only, no details. |
| `Create(snapshot, summary, png)` | A new save named the highest number plus one (`slot0000` when there is none; gaps are not reused, the auto slot is not counted); `SlotNumbersExhausted` once that reaches 9999, as the original refuses. A truncated list refuses (`ListFailed`) rather than reuse a number. |
| `Write(name, snapshot, summary, png)` | Validates the summary (one-line texts: description ≤ 80, hero ≤ 64, place ≤ 128 characters; level 0..1000; month, day, hour, minute and second in range; portrait not negative) and the preview (PNG signature, ≤ 2 MiB) before anything is written (`InvalidSummary`). Then the snapshot is written atomically by the slot (`SnapshotFailed` keeps the previous save, its details and preview); then the details and the preview, each atomically. A failure there leaves a loadable save and a `Warning`, and the old details or preview of that name are removed so the list never shows another snapshot's (the save is then listed with `Missing` details); a save without a preview removes the old preview. The host asks before overwriting. |
| `Delete(name)` | `Protected` for the auto slot (the original's delete helper refuses its auto-save name); `NoSuchSave`; otherwise the snapshot is deleted first (`DeleteFailed` removes nothing), then the details, preview and temporary files (a leftover is a `Warning`). |
| `ReadThumbnail(name)` | The preview's bytes when present, PNG-signed and within the bound; else null. |

Details are display only: the snapshot is authoritative, and a stale or missing details file
never blocks a load. Caller-serialized, one process per folder, no lock.

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
- `LogbookStateTests` (ARC-UI-008): quelling (game-wide, final, range, stale), the quest and rumor logbook order with the
  botched and quelled marks, and quelled rumors over a map round trip and the snapshot.
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
- `SaveLibraryTests` (ARC-UI-010): numbering from the highest (slot0000 first, gaps not reused, refused at 9999);
  the list order (auto slot first, newest first, ties by name) and the latest save; details round trip, missing,
  damaged and stale details; details that cannot be written are removed; an overwrite without a preview removes the old one;
  `slot9999` is not a save name; the auto slot is not deleted and a
  delete removes the save's files; an invalid summary or preview writes nothing; a failed snapshot write keeps the
  previous save and details; unrelated files and the entry bound.
- `SnapshotRestoreTests`: literal canonical encoding, full restore with pause, Unknown
  flag and stale old tokens, refused restores (world, module, profile, budget, epoch,
  clock schema, empty GF domain) leaving the current session unchanged, typed decode
  failures with paths (including lone surrogates and a newer schema), a restore at the
  per-map policy maximum, revision capacity, and the lifetime-safe flag view.
- `SnapshotUpgradeTests` (ARC-UI-037): every schema from the oldest readable one has a step to the next; a schema 56
  text (plain and with a hero) decodes, encodes and restores as the current text it stands for, with no dressed NPC; a schema 56 save file
  loads through the slot, is left as written and the next save writes the current schema; damaged older texts (no or a
  wrong inventory, a field the older schema cannot hold, current-schema checks after the upgrade, a repeated property in
  the root and in a nested section, a lone surrogate in a value or a property name, a foreign format) are typed failures; schemas 55 and 58 stay refused.

```sh
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build
```

## Not implemented / remaining work

- The in-game menu that opens the save and load windows, backups, the original `.gsi` save format (the save list
  and its windows are ARC-UI-010).
- Script (SCR) state and timers.
- Concrete map-change vocabulary beyond the viewer's NPC dialog keys (doors, containers,
  other NPC state) and their application to rendering or step assessment; original
  sector-diff and mobile-object formats.
- Movement mid-step persistence, follower movement and the cross-map group, the inventory beyond the PC's kit and taken
  items (stacks, drop, chests, barter; see [inventory runtime](inventory-runtime.md)), changes to the hero after the start
  (alignment, purchases; levels and experience since ARC-WORLD-030, [experience](experience-runtime.md)), quest data
  beyond the experience level (the logbook texts are read by the viewer, ARC-UI-008), gameplay rolls by any host, life timers
  and their consequences for other owners (see [life state](life-state-runtime.md#not-implemented)), and other
  domains.
- Schema migration and compatibility with original saves.
