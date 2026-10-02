# Headless grid movement runtime

## Scope and provenance

`Arcanum.Simulation` is an independently authored, package-free `net8.0` library.
Its `Arcanum.Simulation.Movement` API implements the bounded project policies
[P1–P8](grid-movement-contract.md), not the CE evaluator or original executable.
The [source evidence](../research/grid-movement-evidence.md) remains research
context. No source code or worksheet evaluator was copied or mechanically
translated. Synthetic Open records below are policy fixtures, not real terrain
permissions. Real cardinal/diagonal admission, dynamic invalidation, UX and
original-runtime parity remain NOT_VERIFIED/out of scope. Data, Input, Scripting,
Presentation and Godot are not dependencies. No devices, callbacks, clock, file
access, animation FPS, rendering or route search participate.

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
