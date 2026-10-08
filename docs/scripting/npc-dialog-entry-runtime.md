# Bounded NPC-dialog entry runtime

## Status and boundary

`Arcanum.Scripting.NpcDialog` is an independently authored, Godot-free net8.0
bridge in the existing Scripting assembly. It selects an effective SAP_DIALOG
attachment from a production `Arcanum.Data.Area.AreaSnapshot`, authenticates
explicit resources, prepares a bounded SCR graph and resolves a checked NPC
DLG entry using supplied versioned queries. It never starts
[DialogTransitionRuntime](dialog-transition-runtime.md), evaluates DLG eligibility
or executes entry effects. Entry selection is not a working full conversation.

The [entry contract](npc-dialog-entry-contract.md) and
[original evidence](../research/crash-site-npc-dialog-evidence.md) describe the
source-backed slice. Original executable/gameplay/display parity is NOT_VERIFIED.

## Public API and ownership

`NpcDialogEntryResolver.Prepare(AreaSnapshot, NpcDialogInvocation,
NpcDialogResourcePolicy, NpcDialogEntryLimits, CancellationToken)` returns
`NpcDialogPreparation`: distinct `Admission`, owned `Plan`, `Frontier` and `Error`.
Only `Admitted` provides a plan. No attachment, serialized script zero, denied
participants, unsupported event and non-null extra are separate admission outcomes.

`Resolve(NpcDialogEntryPlan, INpcDialogEntryQueries, NpcDialogEntryLimits,
CancellationToken)` returns `NpcDialogResolution` (`Resolved`, `Frontier`, `Error`),
a copied safe trace and, on success, `NpcDialogResolvedEntry`. The actual emitting
SCR line and checked positive NPC DLG node are separate typed values; the caller
cannot supply a DLG start node to this API.

World, map, NPC instance, prototype, PC, extra, event, script, SCR line, DLG node,
local flag, source version, world-state version and attachment-state version have
separate immutable identities. Owner epochs are nonempty GUIDs. Hash identities
are exact lowercase 64-hex SHA256; runtime maps are positive. Raw object/prototype
padding fingerprints are not semantic identifier equality. Prepare revalidates
default structs, selected unique row, map/module, source bindings, prototype,
v119/type16, schema and serialized/world placement facts. Render readiness is not
dialogue admission. This is not life/combat/host/exclusivity admission.

The merged Data reader supplies whole-field inheritance of SAP slot31. An absent
override inherits; explicit null/empty or an array omitting event9 never merges
prototype events back in. Serialized event9 script0 is `ZeroScript`. The owned
attachment records flags/counters/script, origin/presence and a semantic field
projection digest. That digest is NOT a raw serialized-field hash.

Plans contain only privately normalized instructions and owned metadata: no live
DAT entry, source handle or original DLG text/expressions escape. Results survive
input mutation and disposal. The caller retains stream ownership, must exclusively
lease readable/seekable streams during Prepare and must not mutate them concurrently.
Prepare preserves borrowed positions and leaves handles open on success and failure.
Before/after streamed full-source hashes detect observed drift, not an adversarial
atomic snapshot or a hostile world-owner attestation. Sources remain at most uint
archive size; authentication uses bounded chunks rather than archive-sized copies.

## Resource selection is PROJECT policy

`NpcDialogResourcePolicy.ExplicitSelectedArchives` is
`explicit-selected-archives-v1`. The caller names one Script source and one Dialog
source by opaque ID, full length/hash, and `NpcDialogSourceVersion` role bindings.
A single archive may serve both named roles; duplicate handles/IDs and arbitrary
precedence winners are disallowed. `NpcDialogEntryResolver.AreaRole(AreaInputKey)`
provides the binding role name for every snapshot input.

Strict raw SCR candidates have exact `scr\\` prefix, `.scr` suffix, printable ASCII,
terminal NUL and no embedded NUL, directory separator or colon in the basename.
The COMPLETE leading decimal run must be 1..99999; leading zeros are allowed.
Every eligible candidate is checked; invalid syntax fails, and exactly one selected
numeric match is required even across duplicate spellings/leading-zero collisions.
Derive DLG by replacing only folder and extension with `dlg`, preserving the exact
basename. Exactly one raw byte match in the explicitly named Dialog source is
required. No fuzzy name, smallest ID, DAT order or game-wide precedence is implied.
Stored/decoded caps are enforced before allocating selected payloads using the
existing bounded DAT and DLG readers.

## Static graph and query lifecycle

SCR framing is 64-byte header plus exactly count * 132 bytes; each line contains
three 44-byte components. Count > 0 and capacity >= count are distinct bounded
values. Code flags at header48 must be zero; unsupported representation is a
frontier. Description, saved pointer, inactive values/tags and initial header local
flags are opaque. Initial SCR flags never substitute for live attachment state.

Supported conditions: TRUE0; LOCAL_FLAG3 immediate tag3 bit0..31; FOLLOWING18
attachee tag1/triggerer tag0; JILTED39 attachee tag1. Supported actions:
DO_NOTHING0 advances; GOTO3 immediate valid SCR line; DIALOG4 immediate positive
existing NPC DLG node. Unknown operations/representations and CALL/return/default
are frontiers, not no-ops or generic VM execution.

Prepare iteratively validates the potential reachable slice until DIALOG or an
unsupported marker. Both arms of admitted variable predicates are checked before
ANY query/version call; known malformed operands or wrong/missing DLG roles in
untaken arms still fail statically. TRUE's false arm, inactive scratch and terminally
unreachable semantics remain opaque. An unsupported arm does not deny its valid
opposite arm. Depth bounds refer to the first-visit breadth-first prepared graph,
not exponential enumeration of every possible cyclic path; resolution is separately
step/cycle bounded.

`INpcDialogEntryQueries` exposes only `ReadVersions`, `Following`, `Jilted`,
`LocalFlag`. Semantic replies explicitly return KnownTrue/KnownFalse, Unknown,
Unavailable, Failed or Stale and the exact request context (participants,
attachment, line, ordinal). Version replies bind source generation/role hashes,
state owner epoch/revision and attachment-state epoch/revision. ReadVersions runs
before the first semantic call, after EVERY boolean reply (including nonvalues)
and before publication. All external calls consume the query budget. Unknown or
unavailable is a frontier; failed/stale/mismatched contexts or sanitized adapter
exceptions are errors, never default false. No earlier decision is cached across
independent Resolve attempts.

Cancellation is cooperative before/after ports and at hash/catalogue/graph/trace
boundaries; port-thrown cancellation is recognized when the supplied token is
cancelled. A synchronous blocking port cannot be forcibly interrupted: the host
must supply bounded ports. Repeated lines yield Cycle; advancing past count yields
NoEntry, never an invented node.

## Bounds and diagnostics

All positive caller limits may lower but cannot raise these PROJECT hard caps:

| Limit | Cap |
| --- | ---: |
| Script bytes / stored resource bytes | 1048576 each |
| Script entries / capacity | 4096 |
| Dialog bytes | 524288 |
| Dialog records / field bytes | 10000 / 4096 |
| Catalogue entries / individual name bytes | 1000000 / 65536 |
| Total name bytes / DAT index bytes | 67108864 each |
| Graph depth / resolution steps / external query calls | 128 each |
| Invocation depth | 1 |

Resolve uses the stricter preparation/current limits; lowered structural limits
are checked before ports. No capacity-sized allocation, nested SCR stack or heap,
RSS or wall-clock guarantee is claimed.

Frontiers/errors retain stage, code, invocation/source/state/attachment bindings
and line when known, with no original text, expressions, resource spelling, payload,
local path or arbitrary exception message. Codes distinguish missing/ambiguous or
invalid catalogue, WrongSource/SourceChanged/WrongContext, malformed container,
attachment or operand, WrongNodeRole/MissingNode, QueryUnknown/Unavailable/Failed/
Stale, unsupported condition/action/operand/callback/representation, NoEntry,
Cycle, Budget, Cancelled and InvalidLimits. Trace contains typed predicate/query
status/selected action and safe line/node IDs only. There are no command ports.

Unsupported DLG syntax reported by `DlgUnsupportedFormatException` is a
`Container` / `UnsupportedRepresentation` frontier, not malformed input. Actual
DLG structural failures remain `Container` / `MalformedContainer` errors, and
reader limits remain `Container` / `Budget` errors. All reject preparation without
a plan or external queries. The invocation retains participants, source/state/
attachment versions and caller line; no decoded SCR line is claimed for a DLG
container failure. Borrowed stream positions and ownership are preserved.

## Opt-in production checks

Public fixtures are invented and exercise the actual AreaDataOwner wire path,
then Prepare/Resolve with literal expected transcripts independent of production
constants. The original test requires a separate private independent selected-byte
oracle (identity, masks/rank, location, numeric catalogue, operands, true/false
terminals and NPC roles), plus the authenticated complete-area oracle.

Configure read-only placeholders locally: `ARC_AREA_MODULE`,
`ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_MOBILE`,
`ARC_AREA_PROTOTYPES`, `ARC_AREA_ORACLE`, `ARC_NPC_SCRIPTS`, `ARC_NPC_DIALOGS`,
`ARC_NPC_ORACLE`. Set `DAT_ROOT` separately for existing DAT/DLG regressions.
Then use the canonical SDK from [toolchain](../workflow/toolchain.md):

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter "TestCategory!=RealData"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "TestCategory=NpcDialogEntrySynthetic"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "TestCategory=NpcDialogEntryRealData&TestCategory=RealData"
```

The original check uses AreaOriginalValidator and all required prototypes. It
computes the investigated false entry and four independently role-validated true
terminals, and a concrete Following Unknown frontier with no later semantic calls.
EVERY supplied boolean, PC identity, epoch and revision is PROJECT diagnostic,
not reconstructed new-game/companion state. Full touched-file length/SHA256 is
compared before/after, including area resources/prototypes. Output is safe numeric
IDs/counts/fingerprints/status. Missing configuration/Inconclusive is NOT a required
original PASS. Successful data resolution is not original executable parity.

## Provenance and remaining capabilities

Framing/operand/whole-array/naming references are pinned through RES016's
[evidence crosswalk](../research/crash-site-npc-dialog-evidence.md). Prior art CE
pin `a7ff41b300ef712f0e7d088183a3d08957110cdd` uses Sustainable Use License 1.0,
NOT MIT. This bounded implementation is independently authored from documented
contracts and observations, not a mechanical CE evaluator translation or a
clean-room claim. Research probes are references, not production oracles.

GF/IN/RE effects/eligibility, full conversations, source-vs-project six-reply
selection/grouping, text decoding/role variants/substitution/media/UI, real state
services, global NPC lifecycle, save/patch policies, callback/return/writeback and
automatic first speech remain missing. Existing DLG LF/QU/RU one-edge ports are
unchanged and never started automatically. True data paths supported by this same
grammar do not establish true original gameplay state. No future product tasks are
authorized by this document.
