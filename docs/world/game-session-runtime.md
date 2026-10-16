# Game session state runtime

Status: **Implemented bounded component**, ARC-WORLD-008. A Godot-free C# owner in
the new `Arcanum.Session` assembly that holds one game session: the
[game clock](game-clock-runtime.md), the [global flags](../scripting/global-flag-state-runtime.md),
the actor's placement and map-local changes, plus a versioned snapshot schema with a
strict JSON codec. It covers the state boundary of
[FR-WORLD-013](../requirements/world.md#fr-world-013) and
[FR-LIFECYCLE-003](../requirements/ui-media-lifecycle.md#fr-lifecycle-003).
It is **not** a save system: there is no save/load screen, save store or file I/O,
no module list, no autosave and no original `.gsi`/sector-diff format.

## Why a separate assembly

The owner composes `Arcanum.Simulation` (clock, `CellCoord`, `NeighborDirection`) and
`Arcanum.Scripting` (GF owner). Neither may reference the other, so the composition
lives one level up. `Arcanum.Simulation` grants `Arcanum.Session` friend access only
for `GameClockOwner.FromDescriptor`, the existing validated descriptor path, so a
saved clock is rebuilt with the same validation as bootstrap. No public clock API changed.

## API

Namespace `Arcanum.Session`. The caller serializes **all** calls, as with the clock
and GF owners. There are no locks, events, callbacks, timers, files or Godot types.

| Entry | Contract |
| --- | --- |
| `GameSessionOwner.Create(GameSessionDescriptor)` | Clock `ProfileBinding`, GF descriptor, initial `ActorPlacement`, optional limits. Validates the actor, bootstraps the clock, then creates the GF owner. Any rejection returns no owner, a `GameSessionFailure`, and the inner `ClockFailure` / `GlobalFlagReason`; a clock already built is disposed. |
| `GameSessionOwner.Restore(GameSessionSnapshot, GameSessionRestoreOptions)` | Checks schema, world, module, the exact clock profile, actor, map keys and budgets **before** building anything, then rebuilds clock (saved time, pause, revision) and GF (saved domain, known values, revision, new epoch). Never touches an existing owner. |
| `Version` / `PlaceActor` / `SetMapChange` / `ClearMapChange` | Actor and map mutations take the expected `GameSessionVersion` (owner identity + revision). Input validation, then version check, then NoChange, then budget, then revision capacity; every refusal leaves state untouched. Each semantic change adds exactly one revision. |
| `QueryActor` / `QueryMapChange` / `QueryMapChanges` | Current placement; one value or `null` for "at loaded baseline"; a detached, canonically ordered copy of one map's changes. |
| `QueryClockSnapshot` / `AdvanceGameTime` / `SetPaused` | Forwarded to the session's clock with the clock's own token contract. Clock changes do not move the session revision. |
| `Flags` / `FlagVersion` | A forwarding `IGlobalFlagOwner` view of the session's GF owner (reads, writes, `GlobalFlagAdapter`; no Dispose or MarkFailed) and its current version. GF changes do not move the session revision. |
| `Capture()` | One detached `GameSessionSnapshot` of every component at this call boundary. |
| `Dispose()` | Idempotent; disposes the clock and GF owner. Every later call except `Dispose` throws `ObjectDisposedException`. |

A map transition is several separately guarded calls (for example `PlaceActor`,
`SetMapChange`, `AdvanceGameTime`); the caller sequences them, and a refusal partway
leaves the earlier calls applied. The session revision counts actor and map changes
only, so a caller that needs one coherent point captures after the whole sequence.
There is no transactional multi-component transition operation yet.

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

## Snapshot schema 1

`GameSessionSnapshot` (only from `Capture` or `TryDecode`) carries `SchemaVersion`,
`Identity` (GF world/module), session `Revision`, `Clock` (`ProfileBinding`, timestamp,
pause, revision; no owner identity), `Flags` (revision and every admitted flag in
ascending order, `null` = Unknown), `Actor` and `Maps` (ascending map, each with
changes in canonical order: scope, sector, entity, property, ordinal). Collections are
read-only copies.

`GameSessionSnapshotCodec.Encode` writes one canonical compact JSON text:

```json
{"format":"arcanum-steamwoven-session","schemaVersion":1,
 "world":"<guid D>","module":"<guid D>","revision":2,
 "clock":{"schemaVersion":1,"policyId":"…","sourceRevision":"…","sourceDataSha256":"…",
          "startupResourceSha256":"…","startYear":1885,"startHour":15,
          "timestampMs":54000000,"paused":false,"pauseReason":"None","revision":0},
 "flags":{"revision":0,"entries":[{"flag":3,"value":false},{"flag":9,"value":null}]},
 "actor":{"map":79,"x":100,"y":200,"facing":"XPlus"},
 "maps":[{"map":79,"changes":[
   {"scope":"sector","sector":5,"property":"door.open","value":1},
   {"scope":"entity","entity":"npc-1","property":"hp","value":12}]}]}
```

(Line breaks added here; the encoder writes no whitespace.) `TryDecode` returns a
`SnapshotDecodeResult` with a typed `SnapshotDecodeFailure` and a JSON path; it never
throws on bad text and never builds an owner. It refuses: input over 8 MiB characters
(`TooLarge`); invalid JSON, comments, trailing commas or depth over 8 (`MalformedJson`);
another `format` (`WrongFormat`) or `schemaVersion` (`UnsupportedSchema`); unknown,
duplicate or missing fields; wrong JSON types; fractions, exponents and out-of-range
integers; non-canonical GUIDs, enum names or identifiers (`OutOfRange`); unsorted or
repeated flags, maps or change keys (`NotCanonical`); and counts above the policy
maxima (`BudgetExceeded`), plus `-0` and strings with a lone UTF-16 surrogate escape
(`MalformedJson`). Format and version are read before the field set, so a newer schema
reports `UnsupportedSchema` rather than a field error. Decoding accepts whitespace,
object field order and JSON string escapes; only `Encode` output is canonical.

Decoding checks structure; `Restore` checks meaning. Restore refuses a snapshot from
another world or module, with a clock profile not equal to the expected one (schema,
policy, source pin, data/startup fingerprints, start year/hour), whose clock or GF
state the component owners refuse, or that exceeds the caller's limits. A refused
restore returns no owner and leaves the caller's current session as it was.

There is **no migration**: schema 1 is the only accepted version, and adding a
domain (inventory, quests, party) needs schema 2 with an explicit migration decision.

## Tests

[Session tests](../../tests/Arcanum.Session.Tests) use synthetic ids and fingerprints
and a literal, independently written canonical JSON text:

- `SessionOwnerTests`: creation and rejection, leave-and-return across two maps with an
  untouched neighbour sector, version guards and revision counting, key validation,
  budgets, detached capture, clock forwarding, disposal.
- `SnapshotRestoreTests`: literal canonical encoding, full restore with pause, Unknown
  flag and stale old tokens, refused restores (world, module, profile, budget, epoch,
  clock schema, empty GF domain) leaving the current session unchanged, typed decode
  failures with paths (including lone surrogates and a newer schema), a restore at the
  per-map policy maximum, revision capacity, and the lifetime-safe flag view.

```sh
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build
```

## Not implemented / remaining work

- Save/load UI, save slots, files, atomic writes, backups, autosave, thumbnails.
- Session revision or GF/clock integration with dialog, scripts, lighting or the viewer.
- Concrete map-change vocabulary (doors, containers, NPC state) and their application
  to rendering or step assessment; original sector-diff and mobile-object formats.
- Movement mid-step persistence, party, inventory, quests, RNG and other domains.
- Schema migration and compatibility with original saves.
