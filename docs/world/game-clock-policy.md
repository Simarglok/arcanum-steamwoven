# Game clock policy and boundary contract

Status: **RESEARCH**, ARC-RES-020. Recommendation **Proposed** beyond the bounded
ARC-WORLD-004 subset described below, not full owner-ratified gameplay. This consolidates the clock part
of [RES-018](time-lighting-contract.md); it does not replace its lighting contract.
The shapes below are document-only, not existing public APIs. No running clock,
scheduler, save parser, ambient renderer or Legacy/HD integration is added.
Original executable, multiplayer, gameplay, night and visual parity remain
**NOT_VERIFIED**; those observations are not required for this bounded research.

### Bounded implementation boundary — ARC-WORLD-004

The accepted task implements only named normalized startup/calendar P1 and
bootstrap/query/ordinary AdvanceGameTime/SetPaused from P2 in the existing
Godot-free Simulation assembly; see [runtime API and tests](game-clock-runtime.md).
It adds opaque immutable per-owner reference identity to the complete token,
fixes generation at1, and specifies serialized calls and idempotent disposal.
Public long Year overflow is unreachable within supported int startup/long time;
the same production derivation has a separate internal arithmetic capacity probe.
This does not promote the remaining proposals or these research shapes to APIs:
raw/source admission/menu behavior, JumpTo, Restore, persistence/migration,
scheduler, P3 consumer, lighting/Legacy-HD, Movement conversion and host/UI
integration remain Research/Proposed or separately scoped Remaining work.
Fingerprints remain declarations, not provenance attestations; no original parity
or full gameplay acceptance is claimed. The worksheet remains table conformance,
not runtime proof. Exact-head review/publication/verified merge are separate gates.

## Evidence classes and provenance

**Source** means a reading of the original's functions listed under
[executable functions](#executable-functions) (evidence level F0 unless the ledger lists them).
Descriptions and synthetic worksheets here are independently authored, not copied
or mechanically translated implementations. Source inspection is not original
runtime observation. **Actual data** means the selected read-only startup identity
below, not a published resource. **Proposal** names a recommended future contract.
**Unknown** identifies an unresolved behavior; a checked integer does not resolve it.

Actual selected module: 355,938,305 bytes, SHA-256
`8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`.
Startup entry ordinal 14921, stored 281 / decoded 439 bytes, SHA-256
`e098286005ba4c4a16701cd6cbcf74eca2bc8937d9f45c7d9ef6331000c7d670`.
Independent retained DAT selection/hash evidence establishes year **1885**, hour
**15**. No original MES rows, text, tables or payload are included. This identity
is not a universal resource-precedence rule or proof that every module starts here.

## Source clock, startup and calendar

[Wire][wire] is two **uint32** words, `DayWord` and `MillisecondWord`, over a
64-bit representation; it is not one absolute millisecond integer. Retain both
unchanged with source/start identity. Raw equality differs from normalized equality.
The source hour is milliseconds / 3,600,000 modulo 24; minute is milliseconds /
60,000 modulo 60. [Ordinary date getters][calendar] use day % 30 + 1,
day / 30 % 12 + 1, StartYear + day / 360. Current day/month exchange the first
two formulas. At day 29 ordinary day/month is 30/1, current day/month is 1/30.
Do not quietly call the current getters an ordinary calendar.

[Startup][startup] in the non-editor branch distinguishes:

| Source condition | Observed result, not Proposed fallback |
| --- | --- |
| Resource absent | setters receive 1885 and 15 |
| Resource present, key 4 or 5 present | corresponding setter receives `atoi` result |
| Resource present, key absent | corresponding setter is skipped; retained parameter remains |
| First/static parameters | [static values][static-start] are 1885 and 36,000,000 ms (10h) |
| Initialization | [first initialization][init] constructs clocks using retained parameters |
| Negative year/hour supplied | setter skips it; this is not strict parse validation |
| Hour zero supplied | start parameter becomes zero; constructor gives clock word **1 ms** |
| Nonnegative hour above 23 | no upper 24h setter bound; signed multiplication risks remain Unknown |

Missing-key behavior is therefore lifecycle-dependent, not always 1885/10 or
missing-file 1885/15. Malformed `atoi`, signed conversions, overflow and the
reference epoch are not established compatibility rules. The [subtraction
underflow TODO][calendar] remains Unknown. No host DateTime or signed64 proposal
may be used to claim those original questions resolved.

Source addition carries only for a sum **> 86,400,000**, not equality:
`(0,86399999)+1=(0,86400000)`; `+2=(1,1)`. Equality can produce hour 0 while raw
day stays 0. The normalized proposal instead gives `(1,0)` and `(1,1)`.
Source unsigned wrap/underflow is not simulated by the bounded worksheets.

## Separate time domains and source admission

| Domain | Ownership / unit / prohibited inference |
| --- | --- |
| Source real | admitted ping milliseconds; not unrestricted host elapsed |
| Source game | conditional 8× admitted delta; not Movement time |
| Source animation | conditional 8× delta even in turn-based combat with open gate |
| Host elapsed | timestamp difference submitted to source admission; not clock authority |
| Proposed admitted game duration | explicit nonnegative game milliseconds already admitted by a future caller |
| Movement SimulationTime / StepDuration | [existing exact rational domain](grid-movement-runtime.md#finite-exact-numeric-domain), finite signed Int128 numerators/positive denominators; no accepted conversion to game hours |
| Display interpolation | renderer-owned presentation between observations, never authoritative advance |

Actual Movement definitions are `SimulationTime.At(n,d)`, `StepDuration.Of(n,d)`
in `src/Arcanum.Simulation/Movement/ExactScalar.cs`; `MovementRuntime.QuerySnapshot`
reads stored state and `AdvanceTo` validates complete explicit requests. Those
APIs are not game clocks. Legacy/HD must eventually share one world clock;
render frames, widgets and scenes must not create another authority.

The bounded **Source** worksheet `source-observation-v1` covers only
[non-network admission and nesting][admission]. Elapsed <5 rejects before updating
the host reference. Admitted pings update that reference **before** clamping >250;
excess is discarded, not a backlog. Real advances on every admitted ping. Counter
0 opens the game/animation gate in this pin ([network macro][network]); counter
1..30 closes it. Increment saturates at 30; decrement saturates at 0. Open gate
with turn-based combat freezes game only, not animation. Closed gate freezes both.

| Host delta | Gate / TB | Real, game, animation increments |
| --- | --- | --- |
| 4 | open / false | 0,0,0 |
| 5 | open / false | 5,40,40 |
| 6 | open / false | 6,48,48 |
| 249 | open / false | 249,1992,1992 |
| 250 | open / false | 250,2000,2000 |
| 251 | open / false | 250,2000,2000 |
| 5 | open / true | 5,0,40 |
| 5 | closed / either | 5,0,0 |

Host references 0→4(rejected, stays 0)→5(admits delta 5, ref 5)→256(admits
clamped delta 250, ref 256). Nested inc/inc/dec/dec is 0→1→2→1→0.
[Menu open][menu-open] increments only for non-OPTIONS; [menu close][menu-close]
decrements if positive. OPTIONS-open at 1 leaves 1; close can take 1→0. These
call sites do **not** prove every UI lifecycle is balanced. No menu service or
multiplayer adapter is implemented here.

[Due processing][admission] follows admission, outside game advancement. At frozen
game time 21,600,000 an already-due deadline 21,600,000 is still due on an admitted
ping; future deadline 21,600,001 remains future. A rejected <5 ping returns before
queue processing. Clock freeze does not establish global callback suppression.

## P1 — Proposed steamwoven-normalized-clock-v1

Recommend one named bounded policy, not a selectable source implementation.
`source-observation-v1` is solely a source worksheet identity.

`GameTimestampMs` is nonnegative signed64 milliseconds since selected module
calendar day 0 **midnight**, not elapsed since startup, host epoch or Movement ticks.
Selected hour 15 starts at **54,000,000**, not zero. Normalize by 86,400,000:
DayIndex = quotient; MillisecondOfDay = remainder 0..86,399,999; hour = quotient
by 3,600,000; minute/second = quotient by 60,000/1,000 modulo 60. Calendar:
DayOfMonth = DayIndex % 30 + 1; Month = DayIndex / 30 % 12 + 1;
Year = StartYear + DayIndex / 360, checked signed64. IsDay uses `[6,18)`
(the interval is [source-confirmed][day-predicate], its normalized use Proposed).

`ProfileBinding` is the exact immutable tuple:
`{ClockSchemaVersion:1, PolicyId:steamwoven-normalized-clock-v1,
SourceRevision:9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52,
SourceDataSha256, StartupResourceSha256, StartYear, StartHour}`. `SourceRevision` is the SHA-256 of the retail 1.0.7.4
`Arcanum.exe` whose clock functions were checked (`GameClockOwner.RetailExecutableSha256`, ARC-DEV-009); a profile written
before ARC-DEV-009 names an older revision and is refused (`InvalidProfile`), and a session snapshot written before it has
schema 15 and is refused as an unsupported schema (snapshot schema 16).
Fingerprints are lowercase 64-hex declarations for equality, not attestations;
startup provenance must separately be validated. StartYear is integral
0..2,147,483,647; StartHour 0..23. Require the exact supported tuple selected by
the future world owner; no aliases, first-win duplicates or profile migration.
Bootstrap requires explicit validated startup; absent/malformed keys fail readiness
atomically. Public fixtures use synthetic fingerprints, not resource bytes.

| Source behavior | Proposed departure / preservation | Reason | Independent check |
| --- | --- | --- | --- |
| Raw two-word identity | retain raw; derive separate timestamp | no lossy wire reinterpretation | raw-equality, raw-overday |
| Missing-file fallback vs skipped keys/static | explicit validated startup only | avoid hidden lifecycle-dependent defaults | start-missing-file, start-missing-key, boot-missing |
| Hour0 constructor 1ms; no upper bound | hour0 = 0ms; reject hour24 | normalized bounded startup | start-zero, boot-zero, boot-hour24 |
| Conditional 5/250/8× host admission | explicit admitted game duration only | separate host adapter from authority | admit-4..admit-251, advance-one |
| Nested/TB/menu gate | explicit pause disposition | stable future world contract, not source gate emulation | gate-nested, pause, pause-reason |
| Frozen game; due loop remains independent | paused ordinary advance no-op; no queue promise | no fabricated global scheduling semantics | paused-positive, due-equal |
| Resume uses host reference rules | resume changes disposition only, no host catch-up | deterministic explicit-duration ownership | resume |
| Delay0 maps to1 | duration0 is validated equal-state no-op | delay is not duration | delay-zero, zero, paused-zero |
| Unsigned subtraction TODO | negative advance rejected; explicit forward/backward JumpTo | rewind is deliberate, not underflow | negative, negative-paused, jump-forward, jump-backward |
| Strict carry leaves equality at day0 | normalized midnight increments day at equality | canonical timestamp | carry-equal, carry-after, boundary-midnight-at |
| Swapped current getters | ordinary 30-day/12-month/360-day calendar | consistent calendar metadata | calendar-0..calendar-361 |
| Source overflow/signed epoch unclear | checked finite time/year/revision/generation, atomic failure | explicit safety, not original parity | time-overflow, revision-overflow, generation-overflow |
| Source/start identity retained | immutable exact profile, reject foreign/stale binding | no silent world/profile mixing | foreign-profile, restore-profile, consumer-profile |
| No accepted restore generation protocol in helper | atomic same-profile restore always new generation | prevent stale reacquisition, independent of scene | restore-same, restore-different, restore-max-revision |
| Relative callbacks | pure supplied-hour metadata with full input key | deterministic detached consumer seam, not scheduler | cadence-repeat, consumer-repeat, consumer-resume |

These gameplay departures remain **Proposed for future owner acceptance**. Merging
this document does not ratify normalization, pause, rewind, admission or save rules.

## P2 — Document-only authority, snapshots and transitions

One future serialized Godot-free world authority owns:
`GameClockState {ClockSchemaVersion:1, ProfileBinding, GameTimestampMs,
PauseDisposition:{Paused:bool, Reason:None|User|Menu|TurnBased},
Generation:positive signed64, Revision:nonnegative signed64}`.
Paused=false iff Reason=None; true requires non-None. Initial generation1/revision0.
Profile is immutable in the retained authority lineage. Generation is not a scene,
widget or renderer lifetime and is **not globally unique across worlds**. A future
world owner must bind cross-world identity separately.

`ClockToken {ProfileBinding, Generation, Revision}` must equal the complete current
token for any command. `QueryClockSnapshot()` returns detached immutable copies of
all state plus the normalized fields above. No streams, leases, lazy enumerables,
mutable references or Godot handles escape. Repeated queries are structurally equal,
produce no events/revisions and remain readable after hypothetical owner disposal.
Historical readability does not authorize a stale write.

| Document-only operation | Required behavior |
| --- | --- |
| AdvanceGameTime(token,DurationGameMs,OrdinaryAdvance) | validate nonnegative signed64 duration first; paused or zero gives exact equal-state NoChange; otherwise checked addition |
| SetPaused(token,disposition) | same disposition NoChange; reason change is semantic even if still paused; resume has no elapsed replay |
| JumpTo(token,target,ExplicitJump) | nonnegative signed64 absolute target, either direction, allowed while paused; preserve disposition; equal target NoChange |
| Restore(token,{schema,profile,timestamp,pause}) | validate exact current token and supported same profile; never import generation/revision or original save bytes; always current generation+1, revision0, even identical time/pause |

Every ordinary semantic change stages **all** output, then increments revision
exactly once, including multi-day advance/jump. No per-skipped-hour events are
specified. Queries/no-ops still work at revision **9,223,372,036,854,775,807**;
changes fail RevisionExhausted. Restore can succeed at max revision if generation
has capacity. At max generation restore fails GenerationExhausted; ordinary changes
can still increment revision. No counter wrap/reuse, profile migration or scene reset.

`ClockMutationResult {Disposition:Changed|NoChange|Rejected,
Failure:None|InvalidSchema|InvalidProfile|InvalidState|InvalidDuration|InvalidReason|
StaleToken|TimeOverflow|RevisionExhausted|GenerationExhausted, Snapshot}`.
Validate shape/schema/profile/disposition/duration/reason, then exact-token equality,
then operation/no-op and checked output capacity. Invalid negative duration rejects
**even paused**, before shortcut. Malformed descriptors never become plausible defaults.
A reject or no-op returns the full previous detached snapshot and no publication or
consumer invalidation. Restore rejection leaves generation, revision and all previous
consumer output unchanged. The existing research Clock helper has **no accepted
restore authority protocol**; this matrix checks a declarative relation, not that helper.

State notation in the fixture is the complete descriptor tuple
`[schema,profileAlias,timestamp,pauseReason,generation,revision]`, with false/true
uniquely encoded by None/non-None. Tokens are `[profileAlias,generation,revision]`.
Alias A expands to a complete synthetic supported tuple; B differs in data identity
and is foreign. Derived snapshot values are uniquely fixed by timestamp/profile and
checked separately in boundary/calendar rows. NoChange/Rejected must preserve every
tuple field; Changed may differ only as specified. No runtime events are implemented.

Concrete P2 examples (all **Proposal**):

| Case | Before / operation | Complete after / result |
| --- | --- | --- |
| init | explicit 1885/15 profile | [1,A,54000000,None,1,0] |
| query, query-detached | [1,A,54000000,None,1,0], read twice | identical; no replacement |
| pause | [1,A,54000000,None,1,0], Menu | [1,A,54000000,Menu,1,1], Changed |
| paused-positive | [1,A,54000000,Menu,1,1], advance100 | identical, NoChange |
| resume | [1,A,54000000,Menu,1,1], None | [1,A,54000000,None,1,2], no catch-up |
| restore-same | [1,A,54000000,None,7,9], same descriptor | [1,A,54000000,None,8,0], Changed |
| restore-different | same before, restore timestamp0/User | [1,A,0,User,8,0], Changed |
| stale-generation | current g8/r0, token A/7/9 | full current retained, StaleToken |

The machine-readable fixture supplies literal complete outputs for all additional
cases, including reason-only pause, multi-day jumps, malformed state, foreign profile,
schema/token failures and finite exhaustion; it is the detailed matrix, not an API.
The invalid-schema/invalid-state rows deliberately submit invalid prior descriptors
to the research validator and retain those descriptors, not fabricated valid
snapshots. A future authority must reject such descriptors before bootstrap; with
an existing valid authority, any malformed request retains its valid current snapshot.

## P3 — Document-only pure hour consumer

`QueryHourMetadata(snapshot,expectedCurrentToken,ConsumerBinding)` returns detached
`{schema,profile,generation,revision,timestamp,hour,isDay,ConsumerBinding}` with no
colors, masks, renderer calls or scheduling. The supplied expected-current token from
the world owner must exactly match the snapshot token. Historical snapshots remain
readable, but stale inputs cannot replace current consumer metadata.

ConsumerBinding is an immutable supported scheme identity plus input version (bounded
nonempty identity, nonnegative signed64 version). The future owner selects supported
bindings; fixtures use `SYNTHETIC_SCHEME/1` and `/2`. Cache/publication key is the full
profile/generation/revision token **plus complete ConsumerBinding**, not hour alone.
Resume, same-hour semantic jump, restore and input-version change invalidate. Equal
jump/no-op does not. Repeated equal query returns equal complete metadata with no
extra publication. Validate ALL inputs before one complete replacement; invalid or
stale input retains the previous complete output, never a new clock plus old metadata.
Clock mutation and consumer publication are separate; no global callback ordering is
specified. Consumers requery after a Changed token; no hidden event bus is installed.

Source [ambient registration/reset][ambient-init] delay is1; [repeat and enable]
[ambient-callback] use relative3,600,000 and immediate current-hour application when
enabled. At21,600,001 the next relative deadline is **25,200,001**, not25,200,000.
This cadence differs from pure current-hour query; clock pause does not suppress
already-due events. General scheduler catch-up/order/cancellation is outside scope.
The source [delay API][delay-api] and [time-kind base selection][delay-base] establish
the relative clock base; [event-kind registration][event-kinds] selects game time
for ambient and animation time for Light, not a common renderer clock.

## Reproducible synthetic conformance inventory

[Fixture](../../tools/research/game_clock_policy_cases.json) schema1 contains only
synthetic Source/Proposal-tagged cases with requirement and source/P1–P3 references,
inputs and independently authored literal expected outputs. The
[standard-library verifier](../../tools/research/game_clock_policy_probe.py) verifies
required coverage, finite shapes and declarative arithmetic/transition relations.
It imports no production clock constants or original decoder, performs no extraction,
network, wall-time advancement or running authority. Its success is **table conformance**,
not production clock/restore/consumer behavior or original parity.

Fixture output layouts: normalized `[dayIndex,msOfDay,hour,minute,second,day,month,
year,isDay]`; calendar `[ordinaryDay,ordinaryMonth,year,currentDay,currentMonth]`;
raw `[retainedDay,retainedMs,normalizedDay,normalizedMs]`; carry `[rawDay,rawMs]`;
startup `[retainedYear,startParameterMs,constructedClockMs]`; bootstrap
`[Ready|Rejected,failure,[timestamp,generation,revision]|null]`; admission
`[realDelta,gameDelta,animationDelta,newHostReference]`; references is the ordered
list of those outputs; gate is the counter after each supplied call; delay
`[constructedDelay,deadline]`; due `[deadlineReached,eligibleOnAdmittedPing]`;
detached `[historicalDescriptorUnchanged,tokenStillCurrent]`; transitions use
`[disposition,failure,completeDescriptor]`; consumer uses
`[disposition,failure,[schema,profileAlias,generation,revision,timestamp,hour,isDay,
consumerBinding]|previous]`. NoChange means zero extra replacement/publication;
Changed means one complete hypothetical replacement, not an observed runtime event.
The entire raw worksheet row is tagged Proposal because its last two fields derive
P1 normalization; only unchanged two-word retention is a source fact.

| Required family / fixture IDs | Explicit oracle boundaries |
| --- | --- |
| boundary-dawn-before, boundary-dawn-at, boundary-dawn-after | 21599999/21600000/21600001 → hour5/6/6, day false/true/true |
| boundary-dusk-before, boundary-dusk-at, boundary-dusk-after | 64799999/64800000/64800001 → hour17/18/18, day true/false/false |
| boundary-midnight-before, boundary-midnight-at, boundary-midnight-after | 86399999/86400000/86400001 → day0/1/1, remainder86399999/0/1 |
| calendar-0, calendar-29, calendar-30, calendar-359, calendar-360, calendar-361 | ordinary (1,1,1885),(30,1,1885),(1,2,1885),(30,12,1885),(1,1,1886),(2,1,1886); current source pairs separately |
| admit-4, admit-5, admit-6, admit-249, admit-250, admit-251, admit-tb, admit-closed, admit-closed-tb, admit-reference | literal real/game/animation and reference preservation/clamp |
| gate-nested, gate-lower, gate-upper, gate-options, gate-menu | 0→1→2→1→0; saturation0/30; OPTIONS asymmetry |
| raw-equality, raw-overday, carry-equal, carry-after | raw preserved; strict equality versus normalized next day |
| start-missing-file, start-missing-key, start-static, start-zero, start-negative, start-upper, boot-missing, boot-zero, boot-hour24, init | actual selected startup separate from synthetic branch examples |
| delay-zero, cadence-repeat, due-equal, due-future | delay0→1 vs duration0; relative deadline and frozen due predicate |
| query, query-detached, zero, paused-zero, paused-positive, negative, negative-paused, advance-one, advance-multiday | equality, validation before no-op, explicit admitted duration |
| pause, pause-repeat, pause-reason, resume, jump-forward, jump-backward, jump-paused, jump-equal, jump-negative | semantic revisions once, no implicit host elapsed |
| time-edge, time-overflow, revision-noop, revision-overflow, generation-overflow, generation-advance | signed64 capacity and atomic retention |
| restore-same, restore-different, restore-profile, restore-schema, restore-invalid, restore-import-counters, restore-max-revision | same-profile descriptor/new generation; never import counters |
| foreign-profile, stale-generation, stale-revision, invalid-schema, invalid-state, invalid-reason, invalid-duration | complete token/shape validation |
| consumer-initial, consumer-repeat, consumer-resume, consumer-jump, consumer-restore, consumer-version, consumer-profile, consumer-generation, consumer-revision, consumer-invalid | full key and complete-output atomic failure |

Run from root with Python3.10+:

```sh
python3 -B tools/research/game_clock_policy_probe.py --self-test
```

Default reads only the fixed adjacent JSON and canonical document. `--self-test`
also exercises missing-case, changed-expectation and malformed-input rejection
through the same CLI entry with bounded temporary synthetic fixtures, not a PASS stub.
Caps:256 cases,256KiB JSON,512KiB total new public material, identities256 characters;
closed kinds, schema1, no eval or original rows. This isolated Python/doc change needs
these targeted checks, source-pin/link/table/scope/privacy/diff audits and independent
exact-candidate review, not a full lighting/toolkit/root C# rerun. Existing C# inspector
APIs are unchanged and unused by this verifier. Local-only / **CI_NOT_REQUIRED**;
no GUI/export/controller gate, without waiving mandatory local checks or review.
Execution logs and full acceptance/findings ledger stay private, not in Git.

## Unknowns and minimal future dependencies

- Original reference epoch, signed/unsigned and `atoi` extremes, subtraction TODO,
  overflow/underflow and source network behavior: **Unknown / NOT_VERIFIED**.
- Original executable speed, startup precedence across modules/patches, queue order,
  catch-up, sleep/travel/combat/save and night/visual parity: **NOT_VERIFIED**.
- Normalization, zero-hour bootstrap, boolean pause, rewind and restore are Proposed
  gameplay choices; document merge alone is not owner acceptance.
- Generation is finite authority-local identity. Cross-world uniqueness and persistent
  lifecycle require a separately accepted owner contract, never scene lifetime.
- Missing historical predecessor aggregate/test records are not reconstructed or
  upgraded to PASS; current bounded startup/source evidence stands separately.

Future order is a recommendation, **not dispatched work**: (1) owner ratifies named
startup/calendar/admission/pause/rewind choices; (2) one Godot-free authority implements
P2 with independent runtime atomicity/detachment/exhaustion tests; (3) validated
same-profile restoration and world identity; (4) P3 metadata consumer integration with
full-token/version invalidation; (5) separately accepted scheduler and lighting/resource
readiness, then Legacy rendering/visual evidence. Movement conversion, queues, original
save compatibility and gameplay systems need their own contracts. No working production
clock or lighting readiness is claimed by this research.

## Pinned source index

## Executable functions

The source references above name these functions of the retail 1.0.7.4 `Arcanum.exe` (evidence level F0 unless the
[verification ledger](../research/original-verification-ledger.md) lists them):

| Reference | Function address |
| --- | --- |
| wire | data layout or declaration (no function) |
| startup | 0x4B9C30 |
| static-start | data layout or declaration (no function) |
| init | 0x45AD10, 0x45AD20, 0x45AD70, 0x45AD80, 0x45AD90 |
| calendar | 0x45A840, 0x45A870, 0x45A890, 0x45A8B0, 0x45A8D0, 0x45A8E0, 0x45A900, 0x45A920, 0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0, 0x45AAC0, 0x45AAE0, 0x45AB00, 0x45AB20, 0x45AB40 |
| day-predicate | 0x45ACF0 |
| admission | 0x45B300, 0x45B320, 0x45B340, 0x45B360, 0x45B370 |
| menu-open | 0x541180 |
| menu-close | 0x5412E0 |
| network | data layout or declaration (no function) |
| ambient-init | 0x57D240, 0x57D2C0, 0x57D2D0 |
| ambient-callback | 0x57D620, 0x57D640, 0x57D650, 0x57D660, 0x57D6C0 |
| delay-api | 0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0 |
| delay-base | 0x45BA30 |
| event-kinds | data layout or declaration (no function) |

[wire]: #executable-functions
[startup]: #executable-functions
[static-start]: #executable-functions
[init]: #executable-functions
[calendar]: #executable-functions
[day-predicate]: #executable-functions
[admission]: #executable-functions
[menu-open]: #executable-functions
[menu-close]: #executable-functions
[network]: #executable-functions
[ambient-init]: #executable-functions
[ambient-callback]: #executable-functions
[delay-api]: #executable-functions
[delay-base]: #executable-functions
[event-kinds]: #executable-functions
