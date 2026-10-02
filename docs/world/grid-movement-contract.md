# Grid movement contract — bounded source specification

## Status and classifications

ARC-RES-013 specifies one logical step; it implements no production movement,
collision, navigation, input adapter, Godot component or simulation assembly.
The [evidence record](../research/grid-movement-evidence.md) contains the exact
CE pin, license, caller trace, source/policy differences and reproducible checks.
Original executable behavior remains **NOT_VERIFIED**. This document does not
make real cardinal or diagonal traversal ready.

Classification applies to every assertion in its labelled section/table row:

- **Original/source-confirmed (CE-source)**: directly inspected pinned CE text,
  not an original executable observation or independent corroboration.
- **Original/source-confirmed (historical original metadata)**: previously
  recorded identity-bound data observations; not rerun for this specification.
- **Project policy**: ratified P1–P8, including the owner's stopping rule; not
  inferred original behavior. All proposed API, equations, invariants, examples,
  action/facing choices and verification-fixture behavior below use this class.
- **Hypothesis**: a proposed explanation or future UX question, not admission.
- **Unknown**: insufficient source, data, context or runtime evidence.

## Source boundary

**Original/source-confirmed (CE-source)**: CE keeps a packed map location and
separate instance offsets. Its isometric `location_xy` projector and the actor
base `projected + offset + (40,20)` are not a normalized simulation chart.
`location_in_dir` enumerates eight neighboring map cells and rejects map-limit
violations. Normal `AG_ATTEMPT_MOVE` calls begin/update callbacks; update advances
ART frames, applies frame offsets to instance offsets, normalizes the location,
and separately checks subsequent progress/completion. Scheduler/stat inputs
modify the pause. See evidence S1–S9 for definitions **and** callers/conditions.

**Unknown**: original runtime interpolation, exact crossing/tie behavior,
release/cancellation, general turn scheduling and absolute compass-facing
semantics. CE's inspected ordinary update is discrete/frame-offset-driven,
not evidence for the linear law below. Its normal completion restores the
previous animation, not universally Stand. A separate source path explicitly
selects Stand, and cleanup has a local ART value without a writeback. Do not
promote either into a universal original Idle contract.

**Project policy**: WORLD-001 remains diagnostic free movement. Its neutral stop,
render-adapter floor, projected-pixel speed, screen octants and Idle 6/Walk 10
FPS are not this contract. [Actor placement](../research/actor-placement-evidence.md)
and [visual contract](../rendering/actor-visual-contract.md) remain conditional;
render hotspot/alpha, image size, camera and zoom never determine logical entry.
No raw ART FPS-to-gameplay-speed equivalence is admitted.

## P1–P3: chart, directed entry and committed ownership

**Project policy**. `CellCoord=(x,y)` consists of checked signed integers in a
caller-declared chart. `WorldAnchor=(u,v)` is finite continuous simulation space
in the same normalized units. `Center(C)=C`. Negative coordinates are legal
chart examples, not claims about CE packed locations or original map bounds.
The selected real-data profile still requires coordinates in its fixed sector;
chart validity alone is not traversal permission.

An interior cell extends one half unit each way around its center. This describes
geometry, not a global half-open ownership function. A settled actor owns its
committed center. For an admitted active step from `A` to `B=A+d`, each component
of `d` is -1, 0 or 1, and `d != (0,0)`. Cardinal means one nonzero component;
diagonal means two. Overflow, same-cell and non-neighbor endpoints reject.

With explicit frozen duration `D>0`, start `s`, and elapsed `t-s`:

```text
p = (t-s)/D, 0 <= p <= 1
anchor = A + p*d
entryTime = s + D/2
arrivalTime = s + D
```

Process boundaries exactly; never overshoot then classify release. `p<1/2`
keeps `committedCell=A`; `p>=1/2` commits `B` once. Directed equality belongs to
`B` in **both signs**. For a negative-X step from zero, `anchor=(-1/2,0)` already
owns `(-1,0)`. Generic `floor(anchor+1/2)` would disagree and is not authoritative.
For a diagonal, exact corner equality atomically enters **only B**, never either
shoulder; emit one atomic `DestinationEntered/CellCommitted` record. This is a
kinematic tie rule, not corner-cutting permission.

Retain `stepFrom`, `stepTo`, outbound direction, duration and step identity after
commit. Commit is not arrival: the anchor can be between the committed cell's
entry boundary and center. `Settled` requires `anchor=Center(committedCell)`
exactly; epsilon proximity cannot manufacture arrival or another step.

## P4–P5: states, release, turns and renewed intent

**Project policy**. Actor intent is distinct from motion and animation. Camera
intent cannot activate it. Latest pending direction **replaces**, never queues.
An active segment freezes endpoints/timing; a mid-step direction change, including
an opposite direction without neutral, cannot bend, reverse or restart it.

| Current phase / input or boundary | Result and authority |
| --- | --- |
| Settled + active resolved neighbor + admitted permit/timing | Start outbound; freeze from/to/duration/context; Walk, outbound facing. |
| Settled + neutral or rejected traversal | Remain at committed center; Idle. No synthetic default Open. |
| OutboundBeforeEntry + neutral (`p<1/2`) | Emit StepCancelled once; latch Returning to A; no destination commit. |
| OutboundBeforeEntry + directed entry (including equality release) | Commit B atomically first; OutboundAfterEntry. |
| OutboundAfterEntry + neutral | Clear pending activation, finish unchanged to B center. No return to A. |
| Any outbound + active turn/held report | Latest intent changes if material; current endpoints, progress and duration do not. Held does not restart progress. |
| Returning + re-input | Update latest pending intent only; cannot reverse the cancellation latch. Finish return before a new step. |
| Returning or post-entry finish + re-input then neutral | Clear pending activation; settle without a next step. |
| Returning + repeated neutral | Idempotent: no new cancellation, no restarted return duration. |
| OutboundAfterEntry + repeated neutral | Idempotent: no duplicate commit or restarted finish. |
| Arrival at A after return / at B after outbound | CenterArrived; settle, then consider latest active intent only after the same-time input batch. |
| Settlement + continuously active actor intent | May start the next **separately assessed** step. No admission carried from the preceding segment. |
| Settlement after release, no new active intent | Idle; stale pre-release held state cannot start a step. |

Release at zero outbound distance settles immediately without displacement. If
press then release occur in one timestamp batch while initially Settled, no
step starts at all. If an already admitted segment has zero elapsed progress,
release cancels and settles with zero return duration; it does not teleport.
After any neutral, an explicit newer active intent is required for later start.
A report tagged older than the release is rejected, not revived by a camera or
render update. Intent remains active across a continuous hold unless released;
“renewed” does not require artificial per-step presses during that hold.

## P6–P7: time, continuous return, action and facing

**Project policy**. Use monotonic simulation timestamps and unique ordered event
keys `(timestamp,sequence)`, not render-frame samples. A request supplies all
input batches up to its inclusive time watermark; a batch at one timestamp may
not be split across frame requests. Later requests cannot append events to an
already closed timestamp. Initial setup/zero-time fixture state is distinct from
such a closed batch. Each internal boundary/input timestamp processes:

1. Advance existing motion; process entry/atomic commit, then center arrival.
2. Apply **all** actor-intent events at that timestamp in sequence. A neutral
   encountered before entry latches cancellation even if followed by re-input.
3. Only then consider a new start from a settled center.

Thus arrival cannot launch a stale-intent step before a same-time release. A
release earlier within a frame must be processed then, not at frame end. A long
interval visits every entry/arrival/input in order; identical complete batches
and assessment/timing inputs give identical states/events under frame partitions.
A future work budget must stop at a complete batch and report processed watermark,
remaining events and `unconsumedTime` explicitly, never clamp silently.

For release at `r`, `q=(r-s)/D<1/2`, return duration is `q*D`. At `r<=t<=r+q*D`:

```text
returnProgress = q - (t-r)/D
anchor = A + returnProgress*d
returnArrival = r + q*D
```

This preserves the exact release anchor and the same segment speed (also for a
diagonal; its length cancels in distance/speed). Clamp only at the mathematical
arrival endpoint, not arbitrarily per frame. Repeated release cannot reset `r`.
No cardinal/diagonal duration relation is assumed; each start supplies its own D.
The example's D=8 is an arbitrary synthetic time unit, not an original speed.

Walk denotes actual moving segments, including return and finish with inactive
intent; Idle denotes settlement without an immediately admitted next step.
Outbound facing follows `d`; return facing follows `-d`; retain facing at rest.
Emit FacingChanged/ActionChanged only on changes. At an immediate settlement/new
start there is no positive-duration Idle slice. A zero-length cancelled start
has no positive-duration Walk. Animation cadence is independently supplied by
presentation/action policy, never used to calculate displacement here.

**Original/source-confirmed (CE-source)**: S2/S3 relate eight neighbor rotations
to map deltas and current-ART rotation in the inspected begin path:

| Map delta | CE rotation | Project alias (policy, not compass) |
| --- | ---: | --- |
| (-1,-1) | 0 | X−Y− |
| (-1,0) | 1 | X− |
| (-1,+1) | 2 | X−Y+ |
| (0,+1) | 3 | Y+ |
| (+1,+1) | 4 | X+Y+ |
| (+1,0) | 5 | X+ |
| (+1,-1) | 6 | X+Y− |
| (0,-1) | 7 | Y− |

**Unknown**: compass labels, all source/actor facing paths and project visual
adapter mapping. The table is not WORLD diagnostic `MapFacing`, nor a physical
ART group table: mirrored requested facings remain separately bound in the
visual evidence. **Hypothesis**: short-tap comfort, return-facing appearance,
turn feel and analog effort-to-duration mapping warrant future UX experiments;
none has been proved comfortable or faithful here.

## P8: pure proposed API and invariants

**Project policy; specification only, no exported production types**.

| Proposed value / operation | Explicit boundary |
| --- | --- |
| CellCoord / WorldAnchor / NeighborDirection | Checked integers / finite normalized anchor / already resolved nonzero neighbor; no raw stick or screen-vector quantization. |
| SetActorIntent | `(timestamp,sequence,active,direction?)`; active requires a valid neighbor, neutral carries no direction. Timestamped input is immutable request data, not a mutating hidden callback. |
| MovementState | Current watermark, phase, committedCell, anchor, stepId/from/to/start/D, entryCommitted, return release/progress/arrival, latest intent/key, facing/action; inactive segment fields not used to infer movement. |
| Step permit and timing inputs | Explicit immutable assessment records keyed to exact prospective from/to, actor/capabilities/context, profile/fingerprints, evidence epoch and start opportunity; full Supported+Open and positive finite D. No bare boolean/default admission. |
| AdvanceTo | Pure `(previousState,targetTime,orderedInputs,assessmentTimingRecords)` -> MovementResult. All relevant records supplied, no callbacks that mutate object state, open doors, read engine state or depend on number of invocations. |
| MovementResult | State, ordered events, structured request/step rejection with reason/provenance, processed watermark and unconsumedTime (zero for completed unbudgeted requests). |
| QuerySnapshot | Pure read-only state view; never normalizes an anchor or commits a cell. |

Validation precedes mutation of state/events: nonfinite/negative/nonmonotonic
time, late/duplicate/out-of-order event keys, malformed direction/state,
nonpositive/nonfinite timing, coordinate overflow or mismatched permit identity
reject the entire invalid request with the original state and no motion events.
Coverage rejection of a well-formed candidate (`Unknown`, `Unsupported`,
`Supported+Blocked`) emits a step rejection at a start opportunity; motion fields
remain settled/unmodified. Accepted intent/clock bookkeeping is distinct from
movement admission. Do not repeatedly retry a denied held intent on arbitrary
render/frame boundaries; start opportunities are explicit material actor intent
at settlement or actual center arrival. Reassessment needs new explicit evidence,
not a silent default or hidden retry loop.

Legal-state invariants:

- Settled anchor equals committed center; no active segment.
- OutboundBeforeEntry has `0<=p<1/2`, committed=from, entryCommitted=false.
- OutboundAfterEntry has `1/2<=p<1`, committed=to, entryCommitted=true.
- Returning has latched cancellation, committed=from, no entry commit, anchor
  on the original segment between release anchor and from. At zero remainder
  it immediately becomes Settled.
- For each stepId, at most one StepStarted, one StepCancelled (only before entry),
  one atomic DestinationEntered/CellCommitted, and one CenterArrived. Cancellation
  and destination commit cannot both occur on the same step.
- New steps start only at a settled center with active intent and their own
  identity/context-bound admission. No shoulder ownership, duplicate commit,
  unbounded input queue, intent inferred from Walk or mutation by QuerySnapshot.

The event record includes timestamp, step identity, endpoints and provenance.
Atomic entry/commit is one indivisible record; action/facing changes are ordered
at their actual motion transition, not fabricated elapsed animation slices.
**Unknown/out of scope**: mutable obstruction/permit invalidation during a step,
recovery from dynamic blockers, party/combat movement and general route search.
The admitted context is immutable throughout outbound/return/finish. This does
not authorize walking through a newly appearing blocker or inventing teleport
recovery; a future task needs a separate dynamic contract.

## Executed synthetic examples

**Project policy**. The [worksheet](../../tools/research/grid_movement_probe.py)
uses exact fractions with independent literal anchors and event transcripts.
No CE/source/data constants are imported. All rows below are synthetic;
`Open` is a fixture permit, **not** the real-data diagonal result.

From A=(0,0), D=8, start=0. Each row's three anchors are at release t=2/4/6
(p=1/4,1/2,3/4), respectively. At t=2 return reaches A at t=4 with no commit.
At t=4 or t=6 B has one commit at t=4 and finish reaches B at t=8.

| B | Anchor t=2 | Anchor t=4 (directed destination owns) | Anchor t=6 |
| --- | --- | --- | --- |
| (1,0) | (1/4,0) | (1/2,0) | (3/4,0) |
| (-1,0) | (-1/4,0) | (-1/2,0) | (-3/4,0) |
| (0,1) | (0,1/4) | (0,1/2) | (0,3/4) |
| (0,-1) | (0,-1/4) | (0,-1/2) | (0,-3/4) |
| (1,1) | (1/4,1/4) | (1/2,1/2) | (3/4,3/4) |
| (1,-1) | (1/4,-1/4) | (1/2,-1/2) | (3/4,-3/4) |
| (-1,1) | (-1/4,1/4) | (-1/2,1/2) | (-3/4,3/4) |
| (-1,-1) | (-1/4,-1/4) | (-1/2,-1/2) | (-3/4,-3/4) |

Negative chart origin A=(-2,-3), d=(1,1), release t=4: anchor=(-3/2,-5/2),
committed=(-1,-2), one diagonal commit, no shoulder. Cardinal release t=2 at
anchor=(1/4,0) returns for two time units: t=3 anchor=(1/8,0), t=4 anchor=(0,0).
This evaluates return-duration arithmetic rather than assuming a full new D.

Timeline shorthand: Start / Commit / Arrive / Cancel; source step starts X+ at 0
unless stated. The worksheet checks full literal event lists, not only final cells.

| Inputs / condition | Expected timeline and final state |
| --- | --- |
| Press then neutral in batch at 0 | No Start, no motion; settled A. |
| Neutral at progress zero on an already admitted fixture segment | Cancel/Arrive at 0, no commit/displacement. |
| Held X+ at 1; neutral at 2 and 3 | One Start0, Cancel2, Arrive4 A; repeated input/release cannot restart. |
| Neutral at entry 4 | Commit4 B before neutral; Arrive8 B; no next step. |
| Neutral at 6 | Commit4 B, Arrive8 B; finish unchanged. |
| Neutral exactly at arrival 8 | Arrive8 B then neutral; no stale Start8. |
| Active Y+ at 2, active X− at 3 (no neutral), neutral16 | Original Commit4/Arrive8 (1,0); latest X− starts8, Commit12/Arrive16 (0,0); no queued Y+ step. |
| Neutral2, Y+ re-input3, neutral12 | Cancel2, Arrive4 A, Y+ Start4, Commit8/Arrive12 (0,1); re-input cannot undo return. |
| Neutral2, Y+3 then neutral3 | Cancel2/Arrive4 A; no next step. |
| Neutral4, Y+6, neutral16 | Commit4, Arrive8 (1,0), Y+ Start8, Commit12/Arrive16 (1,1). |
| Neutral6, Y+7 then neutral7 | Finish Arrive8 (1,0); no next step. |
| At t=2: neutral then Y+ | Cancel2 remains latched; Arrive4 A then Y+ Start4. |
| At t=2: Y+ then neutral | Cancel2; Arrive4 A, no next step. |
| Continuous X+, neutral24; one long interval | Commit4/12/20, Arrive8/16/24; starts0/8/16, no Start24; settled (3,0). |
| Same complete input batches split at integer timestamps | Same complete motion transcript/state as the long request, including earlier intra-frame neutral. |
| Invalid target/events | Atomic invalid-request rejection; no partial clock/intent/motion changes. |
| Real-context diagonal Unknown (represented by separate rejection fixture) | Step rejection, no Start/Commit/anchor change; not the synthetic Open diagonal above. |

## Real traversal readiness frontier

Classifications in this table are explicit; “required next” is **Project policy**
for future bounded work, not authorization to implement it now. Result vocabulary
and precedence remain those of the [passability contract](passability-contract.md).

| Dependency / current result | Classification and evidence | Required next admission |
| --- | --- | --- |
| Terrain tile-name component: 91 Unknown/null | Original/source-confirmed (historical original metadata), [current component evidence](../research/terrain-obstruction-components-evidence.md). Different stage from historical 3033 Unknown in [sector evidence](../research/sector-passability-evidence.md). Neither aggregate rerun here. | Resolve only the entries required by a chosen bounded step; retain any unresolved component as Unknown. No need to resolve all 91 for this task. |
| Mask/facade/name NoBlock | Original/source-confirmed (historical component evidence), [component API](terrain-obstruction-components.md). | Never promote component NoBlock into total Open; retain applicability/null dimensions. |
| Cardinal source/destination terrain, directional edges/walls/portals | [ARC-RES-014 bounded research context](cardinal-traversal-contract.md) has admitted Open/Blocked rows and reversals; production/runtime real steps remain unverified. CE-source dependencies S8. | Retain exact full contributor/source/context proof; do not generalize diagnostic admission to other actors, states or runtime movement. |
| Diagonal shoulders/corner/recursive object traversal | Unknown; CE-source recursive shoulder path S8, not complete diagonal admission. | Prove both shoulder/edge/corner contributions and relevant tile/object contexts; kinematic corner equality supplies no permission. |
| Ordinary static-scenery subset | Original/source-confirmed (historical metadata/source), [static contract](static-obstruction-contract.md). Destination-only cardinal-normal-critter/base/flags-zero component. | Do not infer source registration, edge extent or general object membership. Its Blocker-before-Unknown aggregation is component-only. |
| Other static types, critters, doors and dynamic state | Unknown; source/destination lists, portal state/AI-open, critter death and capabilities in S8. | Fully enumerate relevant object contributions and exact serialized/prototype/runtime inputs; no actor capability or spell-state default. |
| Actor/context, traps/fire, floating/shrunk/special movement | Unknown outside fixed future profile; CE-source caller S3/S8. | Declare actor state/capabilities/flags and all overrides; source callbacks are not pure admission oracles. |
| Cross-sector, other profiles, PATCH/DIF/loose precedence | Unsupported/null under selected-sector contract. | Separately authorize neighbor inputs, version/precedence and fingerprints; do not load/infer a neighbor here. |
| PATH_FLAG_0x1000 | Unknown/unavailable source branch S9. | Reject that context; do not select the normal fallback as replacement. |
| Dynamic permit invalidation/recovery | Unknown; outside P8 fixed-context specification. | Separate dynamic contract and independent observations before expansion. |

**Project policy**: only fully admitted `Supported+Open` from every required
terrain/object/actor/edge/context dependency can permit a real step. Unconditional
whole-context blocking may report `Supported+Blocked` only under the existing
composition rule; in-profile unresolved combined steps remain `Unknown+null`.
The [bounded cardinal research evidence](../research/cardinal-traversal-evidence.md)
now closes one declared isolated context, not real movement integration. Other
combined cardinal contexts and diagonals remain Unknown; cross-sector is
Unsupported. Geometry can be specified/tested synthetically now without moving
any original-data actor. The future minimal readiness unit is one fingerprinted,
immutable, fully assessed cardinal segment and its return/finish path—not full
pathfinding, a collision grid or a claim that all 4096 cells are walkable.

## Implemented / not implemented / remaining work

**Project policy/status**: implemented as research documentation and an exercised
synthetic worksheet only. Not implemented: production types/controller, traversal,
source scheduler, gameplay speed, Godot integration or navigation. Remaining:
independent exact-candidate source/table review; separately authorized publication;
future bounded admission/runtime/UX observations. No build/export/CI is required
for this documentation-only stage; review is not publication or owner merge.
