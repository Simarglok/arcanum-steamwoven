# Versioned global flag state runtime

## Status and boundary

`Arcanum.Scripting.GlobalFlags` implements ARC-SCRIPT-003's bounded, Godot-free,
synchronous in-memory GF owner.
It lives in the existing Scripting assembly, with no new package or runtime
dependency. Calls, input collections and all external mutations must be
serialized by the host; this is not thread-safe or distributed exactly-once.

This is **not** a DLG expression parser, SCR VM,
world/new-game bootstrap, save service, LF owner, inventory/IN, reaction/RE,
quest/QU, rumor/RU, UI or conversation implementation. The
[conversation runtime](dialog-conversation-runtime.md) (ARC-SCRIPT-005) reads and writes GF in DLG conditions
and actions through this owner (Unknown is a frontier there). The standalone typed GF
compare/write adapter of ARC-SCRIPT-003 had no production caller and was
removed on 2026-11-26, [#3384](https://github.com/Simarglok/arcanum-steamwoven/issues/3384). The
[original dialog evidence](dialog-conversation-runtime.md#original-dialog-evidence-carried-from-the-state-contract) holds the source readings; the
retired RES-019 proposal for the other seams is [in git history](https://github.com/Simarglok/arcanum-steamwoven/blob/e4f18fd4bd5c7798aab3f451ed52dc24e8ec2420/docs/scripting/dialog-state-contract.md).

`IVersionedGlobalFlagOwner` (ARC-SCRIPT-006) adds the current `Version` to
`IGlobalFlagOwner`; `GlobalFlagOwner` and the game session's flag view implement it, and
the conversation runtime takes it as its GF port. `OriginalGlobalFlags.NewGame` builds the
original new-game descriptor: flags 0..3199 admitted and known false (the source keeps
100 32-bit words and zeroes them on init and reset).

## Binding and explicit bootstrap

The host supplies `GlobalFlagOwnerDescriptor`:

- `GlobalFlagIdentity(WorldId, ModuleId)` has two nonempty GUIDs. These opaque
  typed identities are distinct from NPC, PC, map, script or DLG identities.
- `OwnerEpoch` is a separate nonempty GUID. The host must issue a new epoch on
  owner replacement/reset and maintain one authoritative owner per identity;
  this class does not register worlds or guarantee epoch uniqueness globally.
- `InitialRevision` is explicitly in `0..9223372036854775807`. Normally use0;
  an authoritative host may supply a later revision. This is not persistence
  or reconstruction of a saved/original world.
- `Domain` is a nonempty finite set of unique integral `FlagId`s in
  `0..2147483647`, with **at most4096 entries**. Sparse catalogues are supported;
  membership, not a guessed dense game range, governs admission.
- `Bootstrap` supplies at most4096 unique domain members and explicit C# bools.
  Missing admitted members remain **Unknown**, not known false. An empty
  bootstrap is permitted but does not mean “all flags unset”. An out-of-domain
  or duplicate bootstrap entry rejects the entire descriptor.

`GlobalFlagOwner.Create` validates before publishing an owner, returning
`GlobalFlagCreationResult(null, safeReason)` on descriptor rejection. It copies
bounded lists by index, not an unbounded enumerator. Inputs must be stable,
ordinary in-memory lists with truthful counts and bounded accessors. Arbitrary
throwing/side-effectful collection accessors and concurrent mutation are outside
that host contract, not a callback sandbox. The binding sorts its detached
read-only domain by numeric flag ID; it does **not** sort dialogue records.

Transport structs deliberately permit `default`/invalid values. Every owner
and snapshot request boundary validates them (nonempty request/identity/
epoch, nonnegative revision/index). A valid FlagId0 is not confused with an
invalid empty GUID. No arbitrary opcode adds a domain member or creates an owner.

## Typed read and failure semantics

`GlobalFlagReadRequest(RequestId, Expected, Flag)` requires a complete expected
`GlobalFlagVersion(Identity, Epoch, Revision)`. The production
`IGlobalFlagOwner.Read` implementation is `GlobalFlagOwner.Read`:

| Result | Value / meaning |
| --- | --- |
| `Known` | Explicit bool true **or false**, reason `None`; current version equals expected. |
| `Unknown` | Null value, `MissingBootstrapValue`, admitted member with no known bit. |
| `Unavailable` | Null value, `OwnerDisposed`. |
| `Failed` | Null value, safe reason such as `WrongWorld`, `WrongModule`, `FlagNotAdmitted`, invalid request/version, or `OwnerFailed`. |
| `Stale` | Null value, `StaleEpoch` or `StaleRevision`; never a false bit. |

No guessed boolean stands for a status other than `Known`. Reads,
version inspection and snapshot capture never change any bit or
flag revision.

Owner checks validate request/identity/domain/epoch first, then disposed/failed
lifecycle, then current revision. This fixed precedence means a foreign invalid
request cannot masquerade as a lifecycle failure. Wrong world/module and domain
have different reasons. After `MarkFailed`, valid owner reads fail and writes
reject. After `Dispose`, valid reads are unavailable and writes reject; both
transitions are terminal and preserve data/revision. Disposal wins if both were
reported. Neither transition rotates the epoch or silently creates a replacement.

## Writes and receipts

`GlobalFlagWriteRequest(RequestId, Expected, Flag, bool)` writes the requested
value, **not toggle**, LF or an unconditional successful mock operation.
The owner requires exact identity, epoch and current revision before effects.
Foreign, invalid, stale, failed or disposed requests return `Rejected` without
changing the whole state. A valid write to an Unknown bit establishes knowledge,
even if the requested value is false.

| Disposition | Production behavior |
| --- | --- |
| `Applied` | Value/knowledge changed; revision advances exactly once. |
| `NoChange` | Value was already known and equal; successful explicit receipt, no state/revision bump. |
| `Rejected` | Safe refusal, attempted write has no effects. |
| `Indeterminate` | For an external `IGlobalFlagOwner` whose write cannot prove no effects; `GlobalFlagOwner` never returns it. Stop and inspect authoritative state; do not replay automatically. |

At maximum revision, changed/Unknown-to-known writes reject with
`RevisionExhausted` atomically. Equal known writes still return `NoChange`.
The production owner validates and allocates its resulting receipt before the
commit; its commit has no callbacks, external calls or allocations. This is a
bounded per-write guarantee, not a multi-owner transaction or a promise of
recoverability from process death/out-of-memory.

Receipts contain only typed request identity, operation (`Write`), expected
version, admitted flag/bool where available, resulting owner version, disposition
and enum reason. Owner refusals return current version. There is no original text, raw expression,
exception object/message or file path. Receipts are returned, **not retained in
an unbounded history**; the host owns any bounded audit storage.

Request IDs are nonempty correlation identifiers, not idempotency tokens.
Repeated calls produce receipts; no distributed deduplication or exactly-once
claim is made. An old version after a changed write rejects; an equal write may
be repeated with the same version. A new valid version with the same request ID
can authorize another different write. Epoch/version admission does not prove
an entire dialogue entry is safe to replay.

## External-port contract

An `IGlobalFlagOwner` must be bounded, serialized, read-only on reads, no-effect on
`Rejected`, and truthful about effects and versions. A blocked port cannot be
interrupted by these synchronous APIs. `GlobalFlagOwner` is the tested authoritative
implementation.

## Detached snapshots

`CaptureSnapshot` returns `GlobalFlagSnapshot` with immutable detached
`Entries` (every admitted flag and nullable bool) and exact historical version.
It remains available for diagnostic inspection even after owner failure/disposal.
`GlobalFlagSnapshot.Read` validates against **that snapshot's** identity/epoch/
revision, not current live-owner freshness. Old valid snapshot reads remain
readable after later writes or owner disposal; a query carrying a newer live
revision against the old snapshot returns Stale. A snapshot is evidence of
historical data, not a live-owner health/capability assertion or a writable port.
Snapshot capture costs O(domain), bounded by4096 entries. Retaining arbitrarily
many snapshots is the host's resource responsibility, not an owner history.

## Synthetic usage

All identifiers/data in tests and this example are invented:

```csharp
var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
var creation = GlobalFlagOwner.Create(new GlobalFlagOwnerDescriptor(
    identity, new OwnerEpoch(Guid.NewGuid()), 0,
    new[] { new FlagId(3), new FlagId(9) },
    new[] { new GlobalFlagBootstrapValue(new FlagId(3), false) }));
if (creation.Owner is not { } owner) throw new InvalidOperationException("Invalid synthetic binding.");
using (owner)
{
    var before = owner.CaptureSnapshot();
    var receipt = owner.Write(new(new GlobalFlagRequestId(Guid.NewGuid()), before.Version, new FlagId(3), true));
    var read = owner.Read(new(new GlobalFlagRequestId(Guid.NewGuid()), owner.Version, new FlagId(3)));
    // receipt: Applied/revision1; read: Known(true).
    // before.Read at before.Version still returns Known(false) for3;9 remains Unknown.
}
```

## Verification and provenance

Focused [production-path tests](../../tests/Arcanum.Scripting.Tests/GlobalFlags/)
use literal whole-state, version and receipt expectations. Tests cover bootstrap
rejection/bounds, identity/module/world isolation, lifecycle statuses, historical
readability/immutability, false/true/equal/Unknown writes, stale/default/foreign
tokens and revision exhaustion.

Equal-write revision policy here is the accepted no-bump rule, **not** RES-019's
worksheet's illustrative always-increment convention. The tests do not establish
original-runtime parity.

Source-confirmed GF read-equality and explicit write observations come from a
reading of the original's functions at the cited addresses: GF query (0x4150D0),
GF action (0x415BA0), global-bit owner (0x444CF0, 0x444D20), evidence level F0
(not checked in the executable) except the unmasked write (0x444D20, ledger X10).
This owner and its fixtures are independently authored MIT work.
Identity/version/Unknown/bootstrap/bounds/receipt and equal-write
semantics are accepted ARC-SCRIPT-003 project safety policies, not observed
original version guarantees. Original live flags/defaults remain Unknown and are not
imported. No original payload, private machine paths or private ledgers are public.

Run the pinned SDK process-local [root checks](../workflow/toolchain.md), plus:

```sh
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~GlobalFlags"
python3 -B tools/research/dialog_state_probe.py --self-test
```

Mandatory headless checks include root locked restore/build/synthetic tests and
`dat_probe.py --self-test`. Local-only / CI_NOT_REQUIRED applies to this bounded
task; GUI/export/device/real-game execution are not required or claimed.
Independent exact-head FULL review, private PR publication and verified merge
receipt remain separate delivery stages; implementation is not whole delivery.

