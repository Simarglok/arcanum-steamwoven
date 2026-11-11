# Headless grid movement runtime

The [whole-exterior research proposal](area-traversal-contract.md)
does not change this runtime's frozen segment, commit or return behavior.

WORLD003's host now maps eight screen sectors to existing semantic map neighbors
under a separate bounded-adjacent Data policy. The Simulation contract below is
unchanged: one frozen segment, atomic diagonal destination commit, fixed diagnostic
duration supplied by its host, early return/equality finish and held renewal. Four
Data shoulder/object evidence legs are constraints, never runtime waypoints or
four permits. See [WORLD mapping and bounds](../rendering/terrain-viewer.md); the
default cardinal-only Data profile and original-executable limitations remain.

## Scope and provenance

`Arcanum.Simulation` is an independently authored, package-free `net8.0` library.
Its `Arcanum.Simulation.Movement` API implements the bounded project policies
P1–P8 (below), not the original's step evaluator.
The [source evidence](../research/grid-movement-evidence.md) remains research
context. No source code or worksheet evaluator was copied or mechanically
translated. Synthetic Open records below are policy fixtures, not real terrain
permissions. Real cardinal/diagonal admission, dynamic invalidation, UX and
original-runtime parity remain NOT_VERIFIED/out of scope. Data, Input, Scripting,
Presentation and Godot are not dependencies. No devices, callbacks, clock, file
access, animation FPS, rendering or route search participate.

## Source boundary

**Original/source-confirmed (source reading, F0, not checked in the executable;
see the [verification ledger](../research/original-verification-ledger.md))**: the
original keeps a packed map location and separate instance offsets; its isometric
location projector (0x4B8680) and the actor base `projected + offset + (40,20)` are
not a normalized simulation chart. The step-in-direction function (0x4B8FF0)
enumerates eight neighboring map cells and rejects map-limit violations. Normal
`AG_ATTEMPT_MOVE` calls begin/update callbacks; update advances ART frames,
applies frame offsets to instance offsets, normalizes the location, and checks
progress/completion separately. Scheduler/stat inputs modify the pause (see
[evidence](../research/grid-movement-evidence.md) S1-S9).

**Unknown**: original runtime interpolation, exact crossing/tie behavior,
release/cancellation, general turn scheduling and absolute compass-facing
semantics. The inspected ordinary update is discrete and frame-offset-driven, not
evidence for the linear law below. Normal completion restores the previous
animation, not universally Stand; a separate path selects Stand and cleanup has a
local ART value without writeback: neither is a universal original Idle contract.

**Project policy**: WORLD-001 stays diagnostic free movement; its neutral stop,
render-adapter floor, projected-pixel speed, screen octants and Idle 6/Walk 10 FPS
are not this contract. Render hotspot/alpha, image size, camera and zoom never
determine logical entry, and no raw ART FPS-to-speed equivalence is admitted. The
[actor placement](../research/actor-placement-evidence.md) and
[visual contract](../rendering/actor-visual-contract.md) stay conditional.

## Policies P1-P8 (project policy, not inferred original behavior)

**P1-P3 chart, directed entry, ownership.** `CellCoord=(x,y)` checked signed
integers in a caller-declared chart; `WorldAnchor=(u,v)` finite continuous
simulation space in the same units; `Center(C)=C`; negative coordinates are legal
chart examples, not original map bounds. A chart-valid cell is not traversal
permission. An interior cell extends half a unit each way; a settled actor owns
its committed center. An active step `A -> B=A+d` has each `d` component in
-1/0/1 and `d != (0,0)` (cardinal: one nonzero; diagonal: two); overflow,
same-cell and non-neighbor endpoints reject. With frozen duration `D>0`, start `s`:

```text
p = (t-s)/D, 0 <= p <= 1     anchor = A + p*d
entryTime = s + D/2          arrivalTime = s + D
```

`p<1/2` keeps `committedCell=A`; `p>=1/2` commits `B` once. Directed equality
belongs to `B` in both signs (generic `floor(anchor+1/2)` is not authoritative);
for a diagonal, exact corner equality enters only `B`, never a shoulder, in one
atomic `DestinationEntered/CellCommitted` record: a tie rule, not corner-cutting
permission. After commit retain `stepFrom/stepTo/direction/duration/step id`;
commit is not arrival; `Settled` needs `anchor=Center(committedCell)` exactly,
never by epsilon.

**P4-P5 states, release, turns, renewed intent.** Intent is distinct from motion
and animation; camera intent cannot activate it. The latest pending direction
replaces, never queues; an active segment freezes endpoints/timing, so a mid-step
direction change (even an opposite one) cannot bend, reverse or restart it.

| Phase / input | Result |
| --- | --- |
| Settled + active neighbor + admitted permit/timing | Start outbound; freeze from/to/duration/context; Walk, outbound facing |
| Settled + neutral or rejected traversal | Stay at committed center; Idle; no synthetic default Open |
| OutboundBeforeEntry (`p<1/2`) + neutral | StepCancelled once; latch Returning to A; no commit |
| OutboundBeforeEntry + directed entry (incl. equality release) | Commit B atomically first; OutboundAfterEntry |
| OutboundAfterEntry + neutral | Clear pending activation; finish unchanged to B center; no return |
| Outbound + turn/held report | Latest intent may change; endpoints, progress, duration do not; held does not restart progress |
| Returning + re-input | Update latest intent only; cannot unlatch; finish return before a new step |
| Returning/finish + re-input then neutral | Clear pending activation; settle without a next step |
| Repeated neutral (Returning or post-entry) | Idempotent: no second cancel/commit, no restarted duration |
| Arrival at A (return) / B (outbound) | CenterArrived; settle, then consider latest intent after the same-time input batch |
| Settlement + continuously active intent | May start the next separately assessed step; no admission carried over |
| Settlement after release, no new active intent | Idle; stale held state cannot start a step |

Release at zero distance settles immediately; press+release in one batch while
Settled starts nothing; an admitted segment with zero elapsed progress cancels
and settles with zero return duration. After any neutral a newer active intent
is required; a report older than the release is rejected. A continuous hold stays
active without artificial per-step presses.

**P6-P7 time, return, action, facing.** Monotonic simulation timestamps and
ordered `(timestamp,sequence)` keys, not frame samples; a request supplies all
batches up to its inclusive watermark and a timestamp batch is never split. Each
timestamp: advance motion (entry/atomic commit, then arrival); apply all intent
events in sequence (a neutral before entry latches cancellation even if followed
by re-input); only then consider a new start from a settled center, so arrival
cannot launch a stale step before a same-time release. A future work budget stops
at a complete batch and reports watermark, remaining events and `unconsumedTime`,
never clamping silently. For release at `r`, `q=(r-s)/D<1/2`: return duration
`q*D`, `returnProgress=q-(t-r)/D`, `anchor=A+returnProgress*d`,
`returnArrival=r+q*D` (same segment speed, diagonal included; clamp only at the
arrival endpoint; repeated release cannot reset `r`). No cardinal/diagonal
duration relation is assumed; each start supplies its own D. Walk denotes actual
moving segments (including return and finish with inactive intent); Idle is
settlement without an immediately admitted next step. Outbound facing follows
`d`, return facing `-d`, rest retains facing; Facing/ActionChanged only on
change; no positive-duration Idle slice at an immediate restart. Animation
cadence is supplied by presentation, never used for displacement.

Source reading S2/S3, map delta to original rotation (alias is project policy,
not compass; not WORLD `MapFacing` nor a physical ART group table):

| Delta | Rot | Alias | Delta | Rot | Alias |
| --- | ---: | --- | --- | ---: | --- |
| (-1,-1) | 0 | X-Y- | (+1,+1) | 4 | X+Y+ |
| (-1,0) | 1 | X- | (+1,0) | 5 | X+ |
| (-1,+1) | 2 | X-Y+ | (+1,-1) | 6 | X+Y- |
| (0,+1) | 3 | Y+ | (0,-1) | 7 | Y- |

Unknown: compass labels, all source/actor facing paths, the visual adapter
mapping. Hypothesis (future UX experiments only): short-tap comfort, return-facing
look, turn feel, analog effort-to-duration mapping.

**P8 pure API and invariants.** `AdvanceTo(previousState, targetTime, orderedInputs,
assessmentTimingRecords) -> MovementResult` is pure (no callbacks that mutate
objects, open doors or read engine state; independent of invocation count);
`QuerySnapshot` is read-only and never normalizes an anchor or commits a cell;
permits are explicit immutable records keyed to exact from/to, actor/context,
profile/fingerprints, epoch and start opportunity (full `Supported+Open` and
positive finite D, no bare boolean). Validation precedes mutation: invalid time,
late/duplicate/out-of-order keys, malformed direction/state, bad timing,
overflow or mismatched permit identity reject the whole request with the original
state. A well-formed coverage denial (`Unknown`, `Unsupported`, `Supported+Blocked`)
emits one step rejection and leaves motion untouched; held intent is never
retried on frame boundaries, and reassessment needs new explicit evidence. Legal
states: Settled anchor equals committed center with no segment; OutboundBeforeEntry
`0<=p<1/2`, committed=from; OutboundAfterEntry `1/2<=p<1`, committed=to; Returning
has the latched cancellation, committed=from, anchor between release anchor and
from, and becomes Settled at zero remainder. Per step id: at most one
StepStarted, one StepCancelled (only before entry), one atomic entry/commit and
one CenterArrived; cancellation and commit never both. New steps start only at a
settled center with active intent and their own admission; no shoulder ownership,
duplicate commit, unbounded input queue or intent inferred from Walk. Unknown and
out of scope: mutable obstruction invalidation during a step, dynamic-blocker
recovery, party/combat movement, route search; the admitted context is immutable
through outbound/return/finish and no teleport recovery is invented.

## Public API and values

- `MovementState.CreateSettled(ActorBinding, CellCoord, SimulationTime,
  NeighborDirection, MovementLimits? = null)` creates a sealed immutable state
  with an open initial timestamp, neutral intent, Idle, retained initial facing,
  no segment/material key, evidence revision0 and next opportunity/step/event
  ordinals1. Invalid initial values throw `ArgumentException`.
- `MovementRuntime.AdvanceTo(MovementState?, MovementRequest?)` returns an
  immutable `MovementResult`: `Completed` or `InvalidRequest`. It never mutates
  its inputs or calls external services. No partial/resume status exists.
- `MovementRuntime.QuerySnapshot(MovementState)` validates and returns a
  detached immutable view at the stored time. It does not extrapolate, normalize,
  emit events or create an opportunity. Invalid/null state throws.
- `CellCoord(int X,int Y)` is a signed integer center in the explicitly named
  chart. `WorldAnchor(ExactScalar U,V)` is continuous chart space, not pixels.
- `NeighborDirection` aliases are `XMinusYMinus`, `XMinus`, `XMinusYPlus`,
  `YPlus`, `XPlusYPlus`, `XPlus`, `XPlusYMinus`, `YMinus`. No compass or visual
  ART mapping is asserted. Neighbor coordinate addition is checked.
- `MovementPhase`: Settled, OutboundBeforeEntry, OutboundAfterEntry, Returning.
  `MovementAction`: Idle, Walk. Latest intent, material/latest input keys,
  committed cell, anchor, action and facing are separate state properties.
- `ActiveSegment` freezes its complete admitted `StepAssessmentTiming`, from/to,
  direction, start/duration, step/opportunity identities and optional release.
  Derived exact entry, outbound arrival and return end are read-only. The state
  stores no history queue; only the active segment and latest opportunity receipt.
- `InputBatch(SimulationTime, ReadOnlySpan<MovementInput>)` is one complete
  timestamp. `MovementInput.Set(sequence, ActorIntent.Toward(direction))`,
  `MovementInput.Set(sequence, ActorIntent.Neutral)` and
  `MovementInput.Reassess(sequence, evidenceRevision)` are the only commands.
- `MovementRequest(SimulationTime target, ReadOnlySpan<InputBatch>,
  ReadOnlySpan<StepAssessmentTiming>)` owns bounded copies. Batches/assessments
  own their nested copies; every child is immutable and wrappers reject mutation.
  No lazy IEnumerable constructors or public state restore/with API exist.

## Finite exact numeric domain

`ExactScalar.Create(Int128 numerator, Int128 denominator)` requires a positive
integer denominator and reduces canonically. Exact zero is0/1; the default
struct is invalid. `ExactScalar.Integer(Int128)` is the integer factory.
`SimulationTime.At(n)` / `At(n,d)` and `StepDuration.Of(n)` / `Of(n,d)` construct
wrappers; runtime validation requires nonnegative time and strictly positive
D. Explicit denominator0 is never treated as1. No floating convenience API,
NaN, infinity, epsilon or even-duration restriction exists.

Every reduced numerator must fit signed Int128 and every denominator positive
Int128. BCL BigInteger is used only as fixed-field bounded arithmetic scratch;
no arbitrary-precision caller payload or text parsing enters the runtime.
Comparison is exact. All required entry/arrival/return times and observed
anchors must be representable. Arithmetic, coordinates and nonnegative long
identities/counters are checked; overflow rejects the whole request. Since the
next ordinal must also fit, emitting/starting with an exhausted counter fails.

Outbound interpolation always recomputes from frozen absolute start and D,
never adds frame deltas. Entry is exactly s+D/2, owns the destination in either
sign, and a diagonal commits only its destination atomically. Early neutral
latches return; return arrival is exactly r+(r-s), not a new full D. Equality
release commits first and finishes outbound. Arrival explicitly sets the exact
committed center and removes the segment. Continuous re-input cannot unlatch
return or bend active motion. Latest direction replaces rather than queues.

Partition equality applies only when both full and partition requests fit the
caps and all requested observations/arithmetic fit this finite domain. An extra
partition at an unrepresentable fraction may fail even if a coarser observation
succeeds. This is explicit capacity/representation behavior, not rounding.

## Complete timestamp batches and event order

The factory's initial time is open for setup. A successful request closes its
inclusive target and asserts all input through that target is present. Later
input times must be strictly greater than the closed watermark, even with newer
sequence numbers. Empty equal-time requests are idempotent (no events/retry).
Batches are strictly time-ordered, nonempty, unsplit; sequences within each batch
are strictly increasing nonnegative longs. Input after target, duplicate/late
keys, unknown direction/command, or malformed active/neutral shape reject.

At each input or boundary timestamp the ordering is:

1. Existing motion: one `CellCommitted` then `CenterArrived` where due.
2. All inputs in sequence: early neutral produces `StepCancelled`, followed by
   positive-distance reverse `FacingChanged`. A zero-progress admitted internal
   test fixture produces Cancel then Arrive with no reverse facing.
3. After the whole batch, at most one `StepStarted` or `StepRejected` opportunity,
   followed by outbound `FacingChanged` only if changed.
4. One final coalesced `ActionChanged` only if changed. Arrival plus immediate
   admitted start keeps Walk; no artificial Idle/Walk toggle.

All events carry exact time/global checked ordinal/full admission key and
applicable assessment/reason/provenance. Facing is frozen during outbound/return;
latest input alone cannot rotate it. Denied starts do not rotate; rest retains
facing. Initial settled press+release in one batch emits no events. The internal
friend-test setup is checked by the same production validator; it is not a
public restore API or permission to append a closed timestamp.

State validation also checks transition/receipt consistency: before-entry
outbound requires active intent; a neutral post-entry finish must record its
material release at or after entry; returning material input cannot predate the
latched release. Active evidence revision equals the frozen admitted revision.
An Admitted receipt consumed its proposed step, whereas Denied/MissingAssessment
receipts did not. Receipt material keys cannot exceed the current material key;
unchanged material retains its admitted/attempted direction. Settled ownership
must match the last denied source or an admitted source/destination. Nonzero
revision requires a timestamped input key. Invalid descriptors are refused, not
normalized; these checks do not introduce a public restore or history API.

## Explicit admission, opportunities and ownership

`ActorBinding` contains exact ordinal, nonempty bounded identities: ChartId,
ActorId, ContextId, CapabilitiesProfileId, CapabilitiesFingerprint,
AssessmentProfileId, InputFingerprint and nonnegative EvidenceEpoch. Opaque
fingerprints are declarations, not a security attestation.

`AdmissionKey` includes that entire binding plus From, To, Direction, StartTime,
OpportunityId, ProposedStepId, MaterialIntentKey(time,sequence), EvidenceRevision.
`StepAssessmentTiming` adds Coverage, nullable Disposition, optional explicit D,
and nonempty reason/provenance arrays. Only canonical `Supported+Open` with valid
D admits. `Supported+Blocked`, `Unknown+null`, `Unsupported+null` are distinct
well-formed denials. Any supplied D must be positive, including on a denial.
All supplied records, including unused records, are structurally validated.
Foreign bindings, replay/stale identities, duplicate opportunity/start times,
future horizon records and malformed combinations reject atomically.

An opportunity occurs only at a settled material intent change with final active
intent, an actual arrival with final active intent, or explicit settled-only
reassessment with a strictly newer revision. Same-time causes coalesce after all
inputs. Identical held reports, repeated neutral and empty calls are not causes.
Neutral/repress is material. Reassessment during motion is invalid, not dynamic
invalidation. Epoch/binding never changes in-place; admitted evidence is frozen.

Each opportunity consumes an opportunity ID; only admitted starts consume a step
ID. Encountered record correlation uses opportunity ID or exact start timestamp,
then compares every key field. A mismatch is InvalidRequest, not Missing/Blocked.
No record produces one `StepRejected` with `MissingAssessment` and
`PROJECT_POLICY` provenance, consumes the opportunity, but no record/step and no
motion. Denial preserves its assessment/reason/provenance. Neither retries on
frames. `ConsumedAssessmentKeys` includes admitted and denied records.
Well-formed unencountered same-binding prospective records are explicitly returned
in `UnusedAssessments`; no queue or reuse after their timestamp closes. Partition
callers retain records beyond their horizon, rather than supplying them early.

## Atomic rejection and caps

Hard per-request maxima (immutable `MovementLimits` may only lower these):
4096 complete batches,4096 total input entries,1024 supplied assessments,1024
encountered opportunities,8192 output events. Each identity/reason/provenance
string is at most256 characters; reason and provenance arrays at most16 each.
Structural size limits are checked before defensive copying; constructor-size
violations throw `ArgumentOutOfRangeException` and no request is created.
Session-limit/semantic failures return structured InvalidRequest.

Traversal uses input timestamps and segment boundaries, not time ticks. Work
includes a carried segment and is bounded by at most two entry/arrival transitions
per supplied assessment plus the carried segment. Finite permits followed by a
missing opportunity terminate held admission even for tiny D and huge target.
Events/opportunity/counter limits are checked before publishing anything.

Every transition is staged privately. ANY invalid input/state/record, encountered
identity mismatch, late semantic failure, arithmetic overflow or cap exhaustion
discards ALL hypothetical clock/intent/motion/rejection/action/facing/events and
consumed keys. InvalidRequest returns the exact previous reference, no events,
zero processed batches, no consumed keys and all request assessments unused.
Processed watermark/closed flag remain previous (unavailable for null/absent raw
descriptor); `UnconsumedTime` is exact target-minus-previous if valid and
representable, otherwise null, never a fabricated0. Completed reaches target,
closes it, consumes every batch and has UnconsumedTime0. A long request exceeding
a lowered event/opportunity limit may reject while smaller complete partitions
succeed: this is capacity rejection, not partial success. Correct a full request
or partition from the same previous state; no resume cursor is returned.

## Tested synthetic use

The following exact use is exercised by `RuntimeDocumentationTests`:

```csharp
using Arcanum.Simulation.Movement;
var binding = new ActorBinding("SYNTHETIC_CHART", "ACTOR", "FIXTURE_CONTEXT",
    "CAP", "CAP_FP", "PROFILE", "INPUT_FP", 7);
var state = MovementState.CreateSettled(binding, new CellCoord(0, 0),
    SimulationTime.At(0), NeighborDirection.XPlus);
var key = new AdmissionKey(binding, new CellCoord(0, 0), new CellCoord(1, 0),
    NeighborDirection.XPlus, SimulationTime.At(0), 1, 1,
    new InputKey(SimulationTime.At(0), 0), 0);
var assessment = new StepAssessmentTiming(key, AssessmentCoverage.Supported,
    AssessmentDisposition.Open, StepDuration.Of(8),
    ["SYNTHETIC_OPEN"], ["SYNTHETIC_POLICY"]);
var request = new MovementRequest(SimulationTime.At(3),
    [new InputBatch(SimulationTime.At(0),
        [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))]),
     new InputBatch(SimulationTime.At(2),
        [MovementInput.Set(1, ActorIntent.Neutral)])], [assessment]);
var result = MovementRuntime.AdvanceTo(state, request);
var snapshot = MovementRuntime.QuerySnapshot(result.State!);
// Completed; Returning, committed(0,0), anchor(1/8,0), facing XMinus, Walk.
// Events: Start0, Idle->Walk0, Cancel2, XPlus->XMinus2; closed watermark3.
```

Run from root with the [canonical toolchain](../workflow/toolchain.md):

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter "TestCategory!=RealData"
"$DOTNET" test tests/Arcanum.Simulation.Tests/Arcanum.Simulation.Tests.csproj --no-build
python3 -B tools/research/dat_probe.py --self-test
```

Local-only; CI, Godot GUI/export and original-data experiments are not required
for this bounded stage. The existing Godot root project still builds. M1–M6 tests
exercise production APIs/validator, literal signed anchors/full ordered events,
query/partition identity, permit mutations, late atomic rollback, numeric/counter
failure, immutable ownership and cap controls. The worksheet is supplemental
only, never the production oracle. Independent exact-head review, authorized PR
and human merge remain separate delivery gates.
