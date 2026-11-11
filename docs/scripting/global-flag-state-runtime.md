# Versioned global flag state runtime

## Status and boundary

`Arcanum.Scripting.GlobalFlags` implements ARC-SCRIPT-003's bounded, Godot-free,
synchronous in-memory GF owner and standalone typed GF comparison/write adapter.
It lives in the existing Scripting assembly, with no new package or runtime
dependency. Calls, input collections and all external mutations must be
serialized by the host; this is not thread-safe or distributed exactly-once.

This is **not** a DLG expression parser, `DialogSession` integration, SCR VM,
world/new-game bootstrap, save service, LF owner, inventory/IN, reaction/RE,
quest/QU, rumor/RU, UI or conversation implementation. Current
[DialogTransitionRuntime](dialog-transition-runtime.md) still rejects GF as
`UnsupportedOperation`, retains its serialized structural cap5 and its existing
LF/QU/RU grammar/limits. ARC-SCRIPT-005's separate
[conversation runtime](dialog-conversation-runtime.md) reads and writes GF in DLG conditions
and actions through this owner (Unknown is a frontier there). The
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

Transport structs deliberately permit `default`/invalid values. Every owner,
snapshot and adapter request boundary validates them (nonempty request/identity/
epoch, nonnegative revision/index). A valid FlagId0 is not confused with an
invalid empty GUID. No arbitrary opcode adds a domain member or creates an owner.

## Typed read, comparison and failure semantics

`GlobalFlagReadRequest(RequestId, Expected, Flag)` requires a complete expected
`GlobalFlagVersion(Identity, Epoch, Revision)`. The production
`IGlobalFlagOwner.Read` implementation is `GlobalFlagOwner.Read`:

| Result | Value / meaning |
| --- | --- |
| `Known` | Explicit bool true **or false**, reason `None`; current version equals expected. |
| `Unknown` | Null value, `MissingBootstrapValue`, admitted member with no known bit. |
| `Unavailable` | Null value, `OwnerDisposed`; standalone adapter also returns `OwnerUnavailable` for a missing port. |
| `Failed` | Null value, safe reason such as `WrongWorld`, `WrongModule`, `FlagNotAdmitted`, invalid request/version, or `OwnerFailed`. |
| `Stale` | Null value, `StaleEpoch` or `StaleRevision`; never a false bit. |

`GlobalFlagAdapter.Compare` admits a typed single-operation descriptor and
compares a **known** bit to its explicit boolean. It returns the bound read
result plus nullable `Matches`: only a known read gives true/false; every other
status gives null. No guessed boolean is passed through a failed gate. Reads,
comparisons, version inspection and snapshot capture never change any bit or
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
| `Indeterminate` | Only the adapter's external-port boundary: a write exception or malformed receipt cannot prove no effects. Stop and inspect authoritative state; do not replay automatically. |

At maximum revision, changed/Unknown-to-known writes reject with
`RevisionExhausted` atomically. Equal known writes still return `NoChange`.
The production owner validates and allocates its resulting receipt before the
commit; its commit has no callbacks, external calls or allocations. This is a
bounded per-write guarantee, not a multi-owner transaction or a promise of
recoverability from process death/out-of-memory.

Receipts contain only typed request identity, operation (`Write`), expected
version, admitted flag/bool where available, resulting owner version, disposition
and enum reason. Owner refusals return current version; adapter predispatch
refusals have no result version because no owner was called. Invalid descriptors
have no admitted flag or bool. There is no original text, raw expression,
exception object/message or file path. Receipts are returned, **not retained in
an unbounded history**; the host owns any bounded audit storage.

Request IDs are nonempty correlation identifiers, not idempotency tokens.
Repeated calls produce receipts; no distributed deduplication or exactly-once
claim is made. An old version after a changed write rejects; an equal write may
be repeated with the same version. A new valid version with the same request ID
can authorize another different write. Epoch/version admission does not prove
an entire dialogue entry is safe to replay.

## Standalone descriptor admission and external-port contract

`GlobalFlagOperationDescriptor` is **schema1**, opcode exactly two ASCII letters
`GF` (ASCII case-insensitive), and exactly two string operands. It represents
one operation, not a raw field, mnemonic-plus-arguments string, DLG source plan,
sequence or expression. `Compare` and `Write` have separate API entry points.

Each operand has1..32 characters and contains only ASCII digits. Leading zeros
are accepted; signs (including `-0`/`+1`), whitespace, punctuation, combinators,
extra arguments, controls, non-ASCII and unsupported schemas/opcodes fail before
any owner read/write. The decimal flag fits Int32 and must belong to the binding;
the decimal bool is exactly numeric0 or1. Overflow is checked before multiplication
can overflow. Limits are fixed; callers cannot enlarge them through descriptors.
This stricter transport admission is project policy, not the original setter's permissiveness (unmasked writes, ledger X10)
or a change to the existing DLG parser.

`GlobalFlagAdapter(binding, owner)` requires the owner's **exact validated binding
instance**. Passing a different owner's metadata is a programmer error
(`ArgumentException`); passing no owner is an explicit unavailable capability.
Adapter preparation validates structure, operands, identity/domain and epoch
before dispatch; dynamic revision/lifecycle is checked by the owner. Missing
owner after otherwise valid preparation refuses without a call.

The extension port `IGlobalFlagOwner` must be bounded, serialized, read-only on
reads, no-effect on `Rejected`, and truthful about effects and versions. The
adapter checks result request/flag/version/enum/value shape: malformed read
results become `Failed/InvalidOwnerResult`, malformed write receipts become
`Indeterminate/InvalidOwnerResult`. External read exceptions become
`Failed/OwnerFailed`; external write exceptions become `Indeterminate/OwnerFailed`.
No exception text is exposed. The adapter cannot verify whether an arbitrary
external port lied about state; this production owner, not untrusted callbacks,
is the tested authoritative implementation. A blocked port cannot be interrupted
by these synchronous APIs. The adapter stores no owner registry or receipt list.

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
    var adapter = new GlobalFlagAdapter(owner.Binding, owner);
    var descriptor = new GlobalFlagOperationDescriptor(1, "GF", new[] { "3", "1" });
    var before = owner.CaptureSnapshot();
    var receipt = adapter.Write(new GlobalFlagRequestId(Guid.NewGuid()), before.Version, descriptor);
    var condition = adapter.Compare(new GlobalFlagRequestId(Guid.NewGuid()), owner.Version, descriptor);
    // receipt: Applied/revision1; condition: Known(true), Matches=true.
    // before.Read at before.Version still returns Known(false) for3;9 remains Unknown.
}
```

## Verification and provenance

Focused [production-path tests](../../tests/Arcanum.Scripting.Tests/GlobalFlags/)
use literal whole-state, version and receipt expectations and an independently
computed state-machine oracle. Counting ports forward to the **real owner**, not
unconditionally Applied mocks. Tests cover bootstrap rejection/bounds, identity/
module/world isolation, lifecycle statuses, historical readability/immutability,
false/true/equal/Unknown writes, stale/default/foreign tokens, revision exhaustion,
ASCII/arity/schema/domain/overflow admission-before-calls and corrupted ports.
Frontier fixtures go through the actual `DlgReader` and `DialogTransitionRuntime`.
Existing LF/QU/RU/NPC tests are retained unchanged.

C01 and C02 cover standalone post-write comparison and both explicit writes.
C09 covers **GF-only** ordered writes3/4/5/6/8, not IN/QU/RU/RE, offers or traversal.
C16 covers only the first-three-GF checkpoint/historical snapshot, not IN
rejection/recovery/session replay protection. Equal-write revision policy here
is the accepted no-bump rule, **not** RES-019's worksheet's illustrative
always-increment convention. Neither these tests nor that worksheet establish
whole Virgil-trace PASS or original-runtime parity.

Source-confirmed GF read-equality and explicit write observations come from a
reading of the original's functions at the cited addresses: GF query (0x4150D0),
GF action (0x415BA0), global-bit owner (0x444CF0, 0x444D20), evidence level F0
(not checked in the executable) except the unmasked write (0x444D20, ledger X10).
This owner/adapters/fixtures are independently authored MIT work.
Identity/version/Unknown/bootstrap/bounds/receipt/strict admission and equal-write
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

