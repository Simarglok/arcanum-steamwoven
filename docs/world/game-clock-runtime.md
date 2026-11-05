# Bounded game clock runtime

Status: **Implemented bounded component**, ARC-WORLD-004. This is an independently
written, Godot-free C# authority in the existing `Arcanum.Simulation` assembly,
not a full world, scheduler, lighting system or original-runtime compatibility layer.
The task accepts `steamwoven-normalized-clock-v1` and only the bootstrap/query/
ordinary-advance/pause subset of [RES-020](game-clock-policy.md). Unselected P1–P3
remain **Research / Proposed**. Implementation is not independent review, PR
publication or verified merge delivery; those are separate exact-candidate gates.

## Evidence and deviations

The [versioned RES-020 policy][res020] and [RES-018 research][res018] distinguish
Source, Data-observed, Proposal and Unknown. Source is a reading of the original's
functions at the cited addresses, evidence level F0 (not checked in the executable)
unless the [verification ledger](../research/original-verification-ledger.md) lists
them; it is not independent retail evidence. This runtime and its synthetic fixtures
are independently authored MIT work.
Selected startup year1885/hour15 is retained research data evidence, not a newly
observed original-data or executable result. Fingerprints supplied to this API
are declarations for binding, not an attestation that a resource was validated.

Accepted departures in this component:

- Absolute, nonnegative signed64 game milliseconds from calendar day0 **midnight**,
  not raw source two-word wire, a host epoch or elapsed time since bootstrap.
- Hour0 starts at0, hour15 at54,000,000; explicit bounded startup replaces source
  missing-file/skipped-key/static/`atoi` behavior. There is no missing-key fallback.
- Midnight normalizes at equality. DayIndex = timestamp /86,400,000 and remainder
  is0..86,399,999. Ordinary calendar has30 days/month,12 months/year; it does not
  reproduce the swapped source current-day/month getters.
- Hour/minute/second are whole values; subsecond remainder remains in
  MillisecondOfDay. Year is checked **long**, never narrowed to int. IsDay is `[6,18)`.
- Explicit already-admitted game duration, not unconditional8× wall time, source
  5/250 admission, nested menu counters or animation advancement. The host-side
  admission adapter `GameTimeFlow` (ARC-WORLD-010, below) is separate from the authority.
- `ExplicitWait` (ARC-WORLD-010) is a deliberate wait that advances while paused and
  keeps the pause disposition; `OrdinaryAdvance` keeps the paused no-op.
- Explicit pause disposition, no replay on resume. Freeze is a clock no-op,
  not a promise that already-due callbacks elsewhere are suppressed.

Original epoch, signed/unsigned/`atoi` extremes, source overflow/subtraction,
network behavior, resource precedence, save/gameplay/night/visual parity remain
**Unknown / NOT_VERIFIED**. Literal runtime tests prove the accepted policy, not
retail parity. Movement `SimulationTime`/`StepDuration` remain separate and unchanged;
no conversion or integration with [movement](grid-movement-runtime.md) is accepted.

## Host time flow (ARC-WORLD-010)

`GameTimeFlow` turns monotonic, nonnegative host milliseconds into admitted game
milliseconds for `OrdinaryAdvance`, following the bounded source worksheet of
[RES-020](game-clock-policy.md#separate-time-domains-and-source-admission): the first
observation admits nothing; an interval under 5 ms admits nothing and keeps the
reference (short intervals accumulate); an admitted interval moves the reference and
is clamped to 250 ms, the excess discarded; game time gains 8 × the admitted interval
(one game hour per 7.5 real minutes at a steady frame rate). A host time that goes
backwards restarts the reference; `Reset()` forgets it. The 8× rate is a source
reading (F0), not an original-runtime measurement (**NOT_VERIFIED**). Pause stays the
clock's: the host keeps observing while paused and the clock ignores the ordinary
advance, so resume has no catch-up. Network gate counters, turn-based combat and
animation time are not modelled by `GameTimeFlow`. Tests: `tests/Arcanum.Simulation.Tests/Clock/TimeFlowTests.cs`.

## Frame clocks (ARC-DEV-121, MIG12 S2)

[K15](../architecture/clocks-frame.md) generalises the admission to the three clocks of the original's ping (TL14, TL15, TL26) as
pure rules; since S3 (below) the session is their one caller.
`GameTimeFlow.ObserveInterval` is the admitted real interval (the same 5 ms floor, reference-then-cut order, 250 ms cut, first and
backwards stamps as `Observe`, which is that interval times 8). `FrameClockKeeper` keeps the **real** stamp (starts at 1 ms) and the
**animation** stamp (starts at the start time) beside the game clock, whose authority stays `GameClockOwner`:
`Advance(hostMs, FrameFlags)` admits real `i` always, game `8i` unless paused or in a round, animation `8i` unless paused; a frozen
frame (message box, movie, slide show, fade, loading) observes no stamp and keeps the reference, so the next frame credits at most
250 ms. `Jump` adds a duration (0 is 1 ms) to the animation clock, ignoring pause and round, and reports the amount the
session adds to game time in the same call; `PlanJump` validates the same jump and changes nothing. `WorldMs` is the admitted animation time ÷ 8 (kept in 1/8 ms, jumps excluded), the run-local
time the viewer's tuned speeds will read. Refusals are typed (`FrameClockFailure`: negative host stamp, bad restored state, negative
duration, stamps within one admission of the 64-bit limit) and change nothing; the admission reference and world time are not saved.
Tests: `tests/Arcanum.Simulation.Tests/Clock/FrameClocksTests.cs`.

## The session's three clocks (ARC-DEV-122, MIG12 S3)

`GameSessionOwner` owns the three clocks: the game clock stays its `GameClockOwner` (token, revision, pause, calendar) and a
`FrameClockKeeper` beside it holds the real and animation stamps, which only the session moves. A new session starts real at 1 ms and
animation at the start time (TL15). `AdvanceFrame(hostMs, frozen)` admits one host stamp with the session's own flags (the game clock's
pause; a turn-based round stops game time only) and moves real, animation and world time; the game part of the admission is **handed
back**, not applied: the viewer's event pump steps it at each event deadline through `AdvanceGameTime(OrdinaryAdvance)` (S4 moves the
pump into `Arcanum.World` and steps the three clocks together). `JumpClocks(token, ms)` is a time jump (TL26): game and animation by the
same amount (0 is 1 ms), also while paused or in a round, world time untouched; the animation half is checked first, so a refusal
changes neither. Every `ExplicitWait` of `AdvanceGameTime` is that jump, so the world-map travel step, the round's second (TL38), the
change of place's timed advance and the palette's "+1 hour" move both clocks; an `OrdinaryAdvance` moves game time alone.
`QueryFrameClocks()` gives the participants' `FrameClocks`; `ResetFrameReference()` forgets the admission reference (the viewer's
`AreaAmbientDriver` calls it when it binds, so a new scene's first frame admits nothing, as the driver's own `GameTimeFlow` did; the
driver no longer owns one). The event queue is handed all three clocks (`GameEventClocks(real, game, animation)`) for every schedule
and pass; the 17 session kinds stay game-domain, so their deadlines are unchanged.

**Schema 70** saves the real and animation stamps in the `clock` section (`realMs` ≥ 1, `animationMs` ≥ 0) beside the game stamp; the
world milliseconds and the admission reference are run-local (a load credits nothing at its first frame). The step from schema 69 sets
real to 1 and animation to the saved game stamp: the two moved together in every older save except in a turn-based round, where the
animation clock was never advanced and the difference is not recoverable. Tests: `tests/Arcanum.Session.Tests/FrameClockStateTests.cs`,
the round's jump in `CombatRoundTests`, the driver over the session in `tests/Arcanum.TerrainViewer.Tests/AreaAmbientTests.cs`.

## API and caller ownership

Namespace `Arcanum.Simulation.Clock`:

| Entry | Contract |
| --- | --- |
| `GameClockOwner.Bootstrap(ProfileBinding)` | Explicit validation before readiness. Returns `ClockBootstrapResult`: Ready/None with Owner and initial Snapshot, or Rejected/typed failure with **both null**. No public owner constructor. |
| `QueryClockSnapshot()` | Full detached immutable value, structurally equal on repeated reads. No time/revision change or publication. |
| `AdvanceGameTime(ClockToken,long durationGameMs,ClockAdvanceReason)` | Reason `OrdinaryAdvance` or `ExplicitWait`; any other value is InvalidReason. Negative duration rejects even paused. Zero, or paused with `OrdinaryAdvance`, yields equal-state NoChange after request/token validation. `ExplicitWait` advances while paused and preserves the disposition. Otherwise stage complete normalized output and revision+1 once. |
| `SetPaused(ClockToken,PauseDisposition)` | Identical disposition NoChange; reason-only change is semantic. Resume changes disposition/revision only, never advances time. |
| `Dispose()` | Idempotent, no semantic revision/publication. Every later query/advance/pause throws `ObjectDisposedException`, even a malformed/no-op request. Returned history remains readable. |

There is one sealed owner per successful construction, **not** a singleton or scene
service. The caller must serialize **all** calls, including queries and disposal.
There are no locks, concurrent exactly-once guarantees, wall-clock reads, event
streams, callbacks, registry, filesystem/MES reader, Godot or Movement dependencies.
Bootstrap validates declarations; the caller separately validates startup provenance
before supplying them. There is no implicit startup parameter or time source.

`ProfileBinding` is a readonly record struct with all required constructor arguments:
`ClockSchemaVersion:int`, `PolicyId:string`, `SourceRevision:string`,
`SourceDataSha256:string`, `StartupResourceSha256:string`, `StartYear:int`,
`StartHour:int`. Support requires schema1, exact policy
`steamwoven-normalized-clock-v1`, exact source pin above, each fingerprint exactly
64 lowercase ASCII hexadecimal characters, StartYear0..2,147,483,647 and StartHour0..23.
No trim, case folding, aliases or profile migration. Unsupported schema is
InvalidSchema; other malformed/unsupported declarations are InvalidProfile.
Default/null fields never produce a ready owner. A typed int cannot encode an
absent hour; external parsers must fail before constructing declarations rather
than silently mapping absence to zero. Default profile fails schema validation.

`ClockOwnerIdentity` is an opaque sealed immutable reference with no public
constructor, mutable fields, owner backreference or equality override. Each
successful construction allocates a fresh identity; two same-profile owners at
generation1/revision0 are distinct **by reference**, without random collision
assumptions or a registry. Identity is process-local, not persistent, cryptographic
or a global world lifecycle. Immutable references in snapshots do not leak the owner.

`ClockToken` contains nullable OwnerIdentity (default is invalid), the **entire**
ProfileBinding, Generation and Revision. Every field must equal the current token,
including on no-ops. Null/default/foreign identity, altered profile or differing
counters produce StaleToken. Tokens are equality/freshness guards, not access
control: a copied/forged tuple that exactly matches the current tuple is accepted.
Generation is fixed1; Revision starts0 and increments exactly once per semantic
change, even multi-day advance. Neither counter is reset/imported through public APIs.

`ClockSnapshot` is a readonly record struct containing ClockSchemaVersion,
OwnerIdentity, ProfileBinding, GameTimestampMs, PauseDisposition, Generation,
Revision, DayIndex, MillisecondOfDay, Hour, Minute, Second, DayOfMonth, Month,
Year, IsDay and the exact computed Token. No mutable collections, lazy views,
handles or owner references escape. `with` creates a detached edited value, not
owner state. Old values survive mutations/disposal but cannot authorize stale writes.

`PauseDisposition(bool Paused,PauseReason Reason)` is valid only when false/None
or true/User, true/Menu, true/TurnBased. Unknown enum values and inconsistent
pairs reject InvalidReason. Default false/None is valid resume. Mutation returns
readonly `ClockMutationResult`: Changed|NoChange|Rejected, bounded ClockFailure
and the **complete** Snapshot. NoChange/Rejected preserve all state and the token.

## Validation, atomicity and capacity

Order is deterministic, with disposed-check first:

1. Advance rejects negative duration (InvalidDuration), then unknown reason
   (InvalidReason). Pause validates the complete disposition (InvalidReason).
2. Compare exact current token (StaleToken), before zero/paused/identical shortcuts.
3. A validated no-op returns the complete unchanged snapshot without capacity checks.
4. Advance checks time addition (TimeOverflow), invokes the same complete calendar
   derivation (CalendarOverflow), then checks revision capacity (RevisionExhausted).
   Pause checks revision capacity for a changed disposition.
5. Construct the complete immutable next snapshot and replace stored state **once**.

Every typed failure leaves the complete previous snapshot intact. Queries and
valid no-ops work at time/revision9,223,372,036,854,775,807; changes never wrap.
The closed vocabulary is None, InvalidSchema, InvalidProfile, InvalidState,
InvalidDuration, InvalidReason, StaleToken, TimeOverflow, CalendarOverflow,
RevisionExhausted. InvalidSchema/Profile are construction failures; InvalidState
is internal descriptor/domain rejection. A malformed string duration cannot enter
the typed long API: this component implements no JSON request parser.

Public supported inputs cannot overflow signed64 **Year**: at maximal StartYear
and timestamp its maximum is2,444,016,955. At longMax timestamp the literal fields
are day106,751,991,167/remainder25,975,807/hour7/minute12/second55/date18/month10.
No supported public owner calendar-overflow/atomic-retention PASS is claimed.
The production internal `ClockCalendar.TryDerive` uses checked long arithmetic;
friend tests exercise **that same function** with extended arithmetic operand
startYear=longMax: timestamp31,103,999,999 yields yearlongMax, while31,104,000,000
reports CalendarOverflow with no derived output. This is a pure capacity probe,
**not** an accepted public profile or retained-owner domain. Reachable time/revision
failures assert full owner retention through the actual mutation paths.

Existing friend-assembly access permits validated internal `ClockDescriptor` /
`GameClockOwner.FromDescriptor` construction of near-capacity time/revision. This
shares profile validation and derivation with Bootstrap, enforces nonnegative
time/revision, generation1 and valid pause, and allocates fresh identity. It has
no bypass flag, reused identity, substitute owner or public test setter. It is not
Restore, persistence or a way to extend public startup bounds.

## Independent runtime tests and research mapping

[Clock tests](../../tests/Arcanum.Simulation.Tests/Clock/) call production APIs with
synthetic fingerprints. Complete expected snapshots/fields, counters, disposition
and failure are literal independently authored or separately computed expectations;
no production calendar helper/constants or Python worksheet evaluator supplies
expected public behavior. Tests also assert complete post-call state and histories.
The shared-calendar capacity probe is deliberately classified separately above.

| RES-020 fixture ID/family | Production C# test (class.method) / adaptation |
| --- | --- |
| boot-zero, init | `BootstrapOwnershipTests.ExplicitStartup` (0/15 plus minyear/maxyear/hour23) |
| boot-missing, boot-hour24, invalid-schema | `MalformedProfilesFailBeforeReadinessWithoutNormalization`; missing declarations/default/null fail before readiness, no malformed prior owner query |
| boundary-dawn-before/at/after, boundary-dusk-before/at/after, boundary-midnight-before/at/after | `CalendarBoundaryTests.NormalizedBoundaryThroughOrdinaryAdvance`, nine literal full-state rows through ordinary Advance from0 |
| calendar-0/29/30/359/360/361, advance-multiday | `OrdinaryCalendarAndMultiDayAdvance`, ordinary fields only; no source swapped-current accessor |
| advance-one | `AdvanceOneMillisecondCrossesMidnightOnce`, real mutation from86,399,999 to86,400,000 |
| query, query-detached | `BootstrapOwnershipTests.QueriesAreEqualDetachedAndIndependentOwnersHaveDistinctIdentity` |
| zero, paused-zero, paused-positive, negative, negative-paused, pause, pause-repeat, pause-reason | `MutationTests.ZeroPauseReasonResumeAndExplicitAdvanceHaveExactRevisions` |
| resume | `ResumeDirectlyFromMenuHasNoCatchup` and the longer reason sequence |
| foreign-profile, stale-generation, stale-revision | `AllTokenFieldsGuardChangesAndNoopsEvenWhilePaused`; generation adapted to fixed1; all profile fields/default/foreign/forged identity/counters on changed and no-op requests |
| invalid-state | `ValidatedFriendDescriptorsCannotImportUnsupportedStateOrIdentity` rejects before ready; `MalformedRequestPrecedencePreservesCompleteValidState` retains valid existing owner, not invalid research descriptor parity |
| invalid-reason, invalid-duration | `MalformedRequestPrecedencePreservesCompleteValidState`, unknown enum/inconsistent pause/negative long; JSON string duration is unrepresentable in this API |
| time-edge, time-overflow | `TimeCapacityAcceptsEdgeRejectsOverflowAndAllowsNoops` |
| revision-noop, revision-overflow | `RevisionCapacityStagesCompleteChangeThenRejectsWithoutPartialState`; both Advance and Pause, identical/reason-only/resume at exhaustion |
| additional (absent from worksheet) | `MaximalPublicProfileCanReachLongMaxTimeWithoutNarrowingYear`, `SharedProductionCalendarReportsSignedYearCapacityWithoutClaimingPublicReachability`, `AtBothCapacitiesValidationAndTimeFailurePrecedeRevisionFailure`, `DisposalIsIdempotentAndHistoriesRemainReadable`, `PublicSurfaceIsBoundedAndValueTypesAreReadonly` |

Research-only raw/carry/startup/admission/gate/menu/delay/due rows are **outside**
this runtime; source facts are not copied into a source clock. Jump/Restore/
generation-overflow/generation-advance (huge generation) and all consumer rows are
also **outside**, not fake production PASS or failed mandatory component acceptance.
The [96-case worksheet](../../tools/research/game_clock_policy_cases.json) and its
[self-test](../../tools/research/game_clock_policy_probe.py) still establish only
**table conformance** and include these unimplemented proposals.

Run from root using the [pinned toolchain](../workflow/toolchain.md):

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test tests/Arcanum.Simulation.Tests/Arcanum.Simulation.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Arcanum.Simulation.Tests.Clock"
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter "TestCategory!=RealData"
python3 -B tools/research/dat_probe.py --self-test
python3 -B tools/research/game_clock_policy_probe.py --self-test
```

Mandatory local checks and exact-head independent review remain required.
Local-only / **CI_NOT_REQUIRED** does not waive GitHub-required delivery gates.
All component checks are headless; GUI/export/device/original-executable observation
is not required here. Run-bound logs/TRX/full acceptance-findings ledger remain
private and untracked, outside sealed bundles. The ledger records every requirement,
production seam, independent oracle, command/head/workspace/run/time/result,
finding disposition and downstream obligation; no worksheet or stale log replaces it.

## Not implemented / Remaining work

No JumpTo, Restore, persistence/save parsing, generation migration, scheduler,
callbacks, P3 consumer, lighting/Legacy-HD, movement conversion/integration,
UI/input, realtime host adapter or full world/game lifecycle. Those require separate
accepted contracts and are not dispatched by this component. Full gameplay and
original-runtime parity remain NOT_VERIFIED. Independent review, authorized private
PR, post-publication acceptance, policy-authorized Reviewer or observed owner merge,
and verified head/merge-ancestry receipt are separate delivery stages; component
implementation/test evidence is not whole delivery completion.

[res020]: https://github.com/Simarglok/arcanum-steamwoven/blob/e860d16657feeb2abe9d8da69789eeb9254c6c06/docs/world/game-clock-policy.md
[res018]: https://github.com/Simarglok/arcanum-steamwoven/blob/b9a9eb8c290b086d698c0bc33bd963b7f828de13/docs/world/time-lighting-contract.md
