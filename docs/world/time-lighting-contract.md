# World time and lighting: bounded research contract

Status: **RESEARCH**, ARC-RES-018, 2026-10-07. This is a source/data contract and a
Godot-free inspector, **not a production clock, scheduler, light graph or renderer**.
Original executable, gameplay, visual and night parity are **NOT_VERIFIED**.
The [functional requirements](../requirements/lighting-time.md) remain proposals;
this research does not promote their coverage to Implemented. The later bounded
ambient implementation for the Crash Site viewer is
[ARC-RENDER-010](../rendering/area-lighting.md); it adopts this document's strict
gap policy and numeric map/sector resolution, not a general light graph.

## Evidence and authority

These classifications are deliberately distinct:

- **Source**: a reading of the original's functions (game and TIG) at the cited
  addresses, evidence level F0 (not checked in the executable) unless the
  [verification ledger](../research/original-verification-ledger.md) lists them.
  This is one reading, not independent original-runtime evidence. The inspector is
  independently authored.
- **Actual data**: bounded read-only observations of the explicitly selected
  original module, prototype files and authentic core-mobile aggregate. Identity,
  unique entry selection, byte offsets and field tuples were independently
  reconciled. No original tables, text, pixels or binary fixtures are published.
- **Proposal**: the strict research evaluator and future API/state shapes below.
  They are not accepted production compatibility policy or existing public APIs.
- **Unknown / NOT_READY**: a concrete evidence frontier, not a zero,
  fallback color, live handle, promise of parity or authorization to implement.

The [complete-area loader](crash-site-area-loader.md),
[area inventory](../research/crash-site-area-inventory.md),
[area contract](crash-site-area-contract.md) and
[grid movement runtime](grid-movement-runtime.md) are separate merged baselines.
The movement `SimulationTime` is an exact rational movement domain. No conversion
from it to original game hours, wall time or this proposed game clock is accepted.
The [terrain viewer](../rendering/terrain-viewer.md) is not modified by this work.
Unmerged rendering/input/script candidates are not research inputs.

## Clock wire, startup and ownership

**Source:** time kinds and wire distinguish real, game and animation
queues. DateTime wire contains unsigned 32-bit day and millisecond words over a
64-bit representation. It is not one little-endian absolute millisecond integer.
The reference date/epoch and original overflow behavior are not a production
policy. Preserve both raw words and the source/profile binding.

Startup (0x4B9C30) reads `rules/gameinit.mes`: key 4 is year, key 5 is hour.
The missing-file branch selects 1885 / 15, but a present resource with absent keys
is a different branch. Clock initialization (0x45AD10, 0x45AD20, 0x45AD70, 0x45AD80, 0x45AD90, 0x45AE20, 0x45AE80) sets the initial game
clock from its start parameters; static values alone do not establish module
startup. **Actual data:** the selected module has year **1885**, hour **15**.
Default noon scheme initialization is not clock startup.

**Source calendar and boundaries** (conversions (0x45A840, 0x45A870, 0x45A890, 0x45A8B0, 0x45A8D0, 0x45A8E0, 0x45A900, 0x45A920, 0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0, 0x45AAC0, 0x45AAE0, 0x45AB00, 0x45AB20, 0x45AB40)):

| Operation | Observed source rule / caveat |
| --- | --- |
| hour / minute | millisecond word / 3,600,000 modulo 24; / 60,000 modulo 60 |
| ordinary date getters | day word modulo 30 + 1; month = day / 30 modulo 12 + 1; year = start year + day / 360 |
| current day/month | formulas are exchanged relative to the ordinary getters; do not silently normalize the discrepancy |
| add duration | carry only when the millisecond sum is **strictly greater** than 86,400,000; equality is left in that word |
| zero delay | the delay constructor maps zero to 1, not a zero-duration event |
| subtract duration | the underflow path computes an odd value (a bug of the original, ledger row Clock subtraction); not reproduced as an accepted policy |

The day predicate (0x45ACF0) uses `[06:00,18:00)`. Tests distinguish
21,599,999 / 21,600,000 / 21,600,001 ms and 64,799,999 / 64,800,000 / 64,800,001 ms;
the normalized proposal also distinguishes 86,399,999 / 86,400,000 / 86,400,001.
The source strict-carry worksheet and normalized proposal are separate results.
Checked arithmetic rejects overflow; it does not simulate C unsigned wrap.

**Source admission:** the time-event ping and its gate (0x45B300, 0x45B320, 0x45B340, 0x45B360, 0x45B370) ignore host elapsed
less than 5 ms, clamp above 250 ms, always advance the real clock on an admitted
ping, and conditionally advance game/animation clocks by 8 times elapsed. In the
non-network-client branch, a closed gate freezes both; turn-based combat freezes
game time but not admitted animation time. The network-client branch has an
additional admission limit. This is **not** unconditional wall-clock speed or an
FPS rule. The bounded source worksheet tests the non-network branch only.
The nested counter is incremented/decremented by the
menu caller (0x541180), menu close (0x5412E0) and clock initialization.
The network test in this branch was read as always false; that is not
proof of the corresponding original network behavior (F0).

The due-event loop (0x45B300, 0x45B320, 0x45B340, 0x45B360, 0x45B370) is outside the game-time advancement branch.
An already-due event can still be admitted while game time does not advance.
Therefore “clock frozen” does not prove globally paused callback delivery.
General queue ordering, catch-up, rest/sleep, combat, travel and save restoration
remain dependencies, not implemented systems.

### Hour application is not next-hour alignment

**Source:** UI registration/reset (0x57D240, 0x57D2C0, 0x57D2D0) registers the ambient callback
and schedules initial/reset delay **1**. The event is game-time, while Light uses
animation time (event-kind table).
Ambient enable/callback/reschedule (0x57D620, 0x57D640, 0x57D650, 0x57D660, 0x57D6C0) reads the current game hour,
applies it if application is enabled, clears the typed event and schedules delay
**3,600,000**. Enabling/rescheduling also immediately applies the current hour;
disabling changes application state, not the callback scheduling path.
Delay construction (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0) and its base selection (0x45BA30) use the current
clock for that event kind. This is relative scheduling, not next-hour alignment.
For example, scheduling at 21,600,001 gives deadline 25,200,001, not 25,200,000.
No global pause suppression or original callback cadence is claimed.

**Proposal:** a future pure query reflects its supplied actual hour immediately,
including after resume/jump, without scheduling duplicate events. Whether a future
production compatibility profile preserves relative callbacks is a ratification
question; this inspector does not answer it by installing a scheduler.

## Map, sector and numeric scheme resolution

**Source:** mapinfo loading (0x4115D0) reads `LightScheme`; the absent-extension
branch simply returns. Do not invent a per-map fallback from that branch.
Scheme definitions define sector sentinel **0** and default
scheme **1**. Scheme resource resolution (0x4A7DA0, 0x4A7DC0, 0x4A7DD0) resolves the numeric
catalogue key in `rules/Lighting Schemes.mes` to the named rules MES resource.
Resource index order is not scheme identity and establishes no DAT/PATCH/loose
precedence rule.

Sector metadata (0x4D1530) at version/tag `>= 0xAA0003` comprises six
32-bit words: townmap, aptitude adjustment, scheme, then three sound words.
The existing project framing retains 24 bytes; scheme is at metadata offset +8,
not a guessed enum/ordinal. Absence of that metadata differs from explicit 0.
The strict inspector rejects absent/negative scheme metadata rather than
manufacturing an original default rule.

Active-PC sector notification (0x4F2330) resolves sentinel 0 to the current
map default and applies a changed scheme at the current game hour. This is an
**active-sector ambient selection** seam, not proof that each simultaneously
visible neighbour gets a different ambient scheme. Neighbour emitter support is
a different responsibility.

**Actual bounded selection:** exterior catalogue **5000**, runtime **1**, start
sector `(1452,1290)`, townmap **2**. Nine core sectors at X1451..1453 / Y1289..1291
and eleven present context sectors were read within the 5x5 X1450..1454 /
Y1288..1292 selection; five selected context entries are absent. All twenty
present exterior records have scheme **0**, resolving map default **1**.
The start and adjacent core sector therefore resolve the same numeric resource.
No nonzero serialized override was found **in this selection**.

Supplementary catalogue **5078**, runtime **79**, sector **2080374816** `(32,31)`,
module ordinal **10236**, has default **2**, sector sentinel **0**, townmap **141**
and 4,096 class-0 cells. It demonstrates a different **map default**, not a
nonzero serialized sector override. It is **cave-only editor/source data**, not
normal-game cave authenticity, playable cave loading or a map transition.
Synthetic default/override/changed-default/absent-metadata tests establish only
the research resolution policy. Wider original override acquisition is NOT_READY
outside this bounded selection; no synthetic override is presented as actual data.

## Hourly channels and parsing frontier

**Source:** hour grammar and gap loop (0x4A8060) has 24 hour keys and six
components in **outdoor RGB, indoor RGB** order, despite the struct's member
order. Hour application (0x4A7EB0) chooses integer hourly values and calls
the light colour setter (0x4D8560), which refreshes
palettes and, when enabled, enumerates loaded sectors and invalidates.
This is not continuous minute blending.

Important source caveats: the initial values of the parse's local colors are
not checked in the executable (F0); the allocated table is zeroed at initialization, not cleared
on every parse (initialization (0x4A7C80, 0x4A7C90)). There is no cyclic edge fill.
Missing and empty keys enter the gap branch. The gap loop mutates its running
colors; later separated gaps can use those mutated values. Empty leading/trailing
slots and reload can therefore not be generalized to cyclic, zero or immutable
anchor interpolation. Source behavior is recorded, not mechanically translated.

**Strict research proposal, actually exercised:** all 24 keys must be present;
each nonempty row has exactly six integral channels in 0..255; endpoints 0/23
must be explicit. Interior empty runs use their independently stored bracketing
anchors and integer step truncation toward zero:
`left + distance * trunc((right-left)/span)` per component. A new parse gets a
fresh table. Invalid/missing/edge states fail, never guessed zero/previous color.
This is deliberately not an assertion of the original's multi-gap/reload result.
MES selection accepts brace pairs, whitespace, semicolon/line comments and
bounded trailing 1..12 AM/PM hour annotations found on the selected resource;
it is a research subset, not a universal MES implementation.

**Actual data:** numeric scheme **1** is module ordinal **14925**, payload SHA-256
`ede40699a2e250d37b3748d255f7535561633c0bd8ec3de6229a157bedf07a0d`;
it has 9 explicit / 15 empty hourly rows. Scheme **2**, ordinal **14926**, SHA-256
`a8b3b8968e0b8f21b46b36560b2cbe9b69f4f14654254c7b6e9261764e75da57`,
has 2 explicit / 22 empty rows. Neither selection has missing keys or empty
endpoints. Both passed all 24 strict queries and have distinct indoor/outdoor
channels. These are shape/count/hash observations, **not published color tables**
or original-runtime interpolation parity.

## Serialized emitters, object defaults and gating

### Stationary wire versus a usable light

Wire declaration, read (0x4DDD20) and the flag definitions give
a 48-byte serialized record (not a native pointer-sized struct):

| Offset / width | Meaning established by source; retained frontier |
| --- | --- |
| 0 / 8 | owner-like word; not a persistent GUID or usable object handle |
| 8 / 8 | raw location; stationary coordinate conversion remains unknown |
| 16,20 / 4 each | signed X/Y offsets |
| 24 / 4 | flags, including unknown bits |
| 28 / 4 | ART ID, not radius/intensity/direction |
| 32,33,34 / 1 each | **r,b,g**, not r,g,b |
| 35 / 1 | uninterpreted padding |
| 36 / 4 | serialized tint word; runtime rebuild differs from raw retention |
| 40 / 4 | serialized palette-like word, discarded on read; not a live palette |
| 44 / 4 | uninterpreted padding |

The existing Data owner retains twelve raw words, not typed live emitters.
Stationary placement (0x4F72C0) leaves its coordinate conversion unknown (F0);
no packed-location interpretation is promoted to parity. Unexplained padding or
palette words are preserved/hash-bound, not rejected merely for being nonzero.

**Actual data:** zero serialized stationary records in all twenty selected
exterior sectors; this says nothing universal about object, neighbour or DIF
emitters. All **four** supplementary cave wire records were projected and
independently reconciled, including signed offsets and the r/b/g order via a
projection digest. Their flags are 0: additive/custom-tint disposition, no actual
OFF/DARK/indoor/outdoor example. Record 0 at offset 4 has SHA-256
`11ad9c3e3406e6b5f9d301aca67eaa063ddd7dae282c1250b40cb46f83919b8e`;
records 1..3 begin at 52, 100, 148. Unknown padding/palette words are nonzero in
three records. Their runtime placement/mask was NOT_READY here; [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012)
later adopted an in-sector absolute-location policy and draws them. No pixel
acquisition, mask export or visual parity is asserted by this research.

### Primary, overlay and carried descriptions are separate

Object fields distinguish serialized light flags/ART/color and
three overlay arrays from runtime primary/overlay handles. The research FILE
projection uses primary slots **12/13/14**, overlay slots **15/16/17**, preserving
instance absent/null/empty/explicit-zero versus prototype defaults. Only the
private probe projects prototype array spans; public Data APIs/owners are unchanged.
Missing array contents are not fabricated handles or successful rendering.

**Actual data:** all **426 core statics** and **88 authentic core mobiles** were
inspected with **54 referenced prototypes**. The original aggregate was explicitly
located and fingerprinted; loose editor mobs were not substituted. A private
independent offset/length/hash witness selected the 88 core records; this is not
a new full 7,399-record `AreaOriginal` validation or whole-game emitter census.
Module/prototype/aggregate identities were unchanged before/after inspection.

There are **42** static effective non-invalid primary descriptions, **4** direct
and the remainder inherited. Representative direct: start sector 86570436012,
static index 127, byte offset 29229 / length 131, prototype 4041, record SHA-256
`c3c6d67468f6df94d87fc3269fb407aed1b849232685dd4b4d923b12ea814af8`.
Representative inherited: sector 86503327147, index 0, offset 17212 / length 95,
prototype 4026, SHA-256
`44074b4d20e61f1e6a145d5e70242ec84f2886b2151451069f9371fa8b0c1643`.
These establish **description presence**, not activity: primary ART/mask, state,
coordinate and object-lifecycle binding remain **NOT_READY**. The inherited
representative has light flags 18, but that alone does not prove a runtime dark
light was active or visible.

No effective non-invalid primary descriptions were found in the 88 selected
mobiles; no effective non-invalid overlay entries in either selected object
family. Overlay presence/default provenance was inspected, not inferred from
zero sector lights. A representative actual non-invalid overlay is therefore
**NOT_READY outside the selected input**, not universally absent from the game.
Actor-carried/equipped light lifecycle, inventory/equipment state and attachments
remain separate dependencies, not implemented by this inspector.

**Proven source dispositions:** OFF (1) excludes contribution; DARK (2) selects
subtraction otherwise addition. INDOOR (8) / OUTDOOR (16) choose **emitter tint
sources**, not spatial indoor-only/outdoor-only exclusion; the tint helper (0x4DE390)
prioritizes INDOOR if both are set. With neither flag set it retains custom color. Unknown bits
remain Unknown. Synthetic literal words test these branches even when the actual
supplement has none.

Sector load/fold (0x4F7940) refuses inventory world insertion and applies nocturnal
scenery gating: tile class 0 is ungated by day; nonzero tiles disable during
`[6,18)` and enable at night. Hour refresh (0x4DDD90) additionally excludes
respawning scenery, updates only a changed state and has **no equivalent inventory
refusal in that function**. These seams must not be flattened into one rule or a
universal time-sensitive-object policy. The helper's load/refresh decision tables
are bounded source worksheets, not object-state mutation.
Primary/overlay creation/update (0x4D9590): primary creation adds an extra runtime
flag (0x8000000); overlay creation uses the overlay flag array plus the slot bit, and
overlay update ORs the overlay flags without it. The executable reading of ARC-RES-069
([LT1](../research/original-verification-ledger.md#lights-on-all-maps-arc-res-069))
corrects the earlier source reading, which had overlay creation take the primary flags.
Object lights, nocturnal switching, carried and animated lights are read there
([lights on all maps](../research/lights-on-all-maps.md)).

## Renderer boundary, layers and neighbours

**Source:** tile rendering selects indoor for tile class 0, outdoor for
nonzero, with constant/local-sampling paths. Roof rendering (0x439140) uses outdoor
tint in the hardware path but flags 0 in the software path. The
palette callback (0x4DE0B0) selects indoor/outdoor for tile/facade class;
wall/critter/portal/scenery/container/roof/monster/unique-NPC use outdoor;
ground items use outdoor, other item dispositions and interface/misc/light do not.
This callback is not a complete universal render policy or proof all callers/modes
use it identically. Do not replace these seams with a whole-screen tint.

Footprint (0x4DE290) derives from ART frame dimensions/hotspot, location and
signed offsets (with source screen anchoring +40/+20), not radial attenuation.
Sampling (0x4D89E0, 0x4D9240) consults neighbour sector lists and ART color-key pixels;
composition (0x4DE900) builds separate lighter/darker buffers. Three-by-three
map prefetch is **not** a proven universal light radius. Missing required neighbour
coverage is Unknown/NOT_READY, not an empty dark sector. Synthetic full source
keys deduplicate contributors, reject conflicting descriptions and demonstrate
cross-seam rectangular support, not actual stationary-coordinate parity.

TIG multiplication/add/subtraction and its
multiplication table are platform/BPP-dependent, read from the
original's TIG functions (F0). The synthetic helper uses test-owned 8-bit scalar masks:
transparent -> no contribution, `mask*tint/255` truncation, saturating add/sub.
It proves only its stated research arithmetic. Final palettes, BPP/blit order,
actual ART masks, roof modes, grayscale/gameplay luminance (unknown), Godot
shaders and original visual parity remain **NOT_VERIFIED / NOT_READY**.

## Proposed Godot-free state and detached snapshot

The names below are **document-only proposals**, not public symbols or accepted
production defaults. The helper evaluates supplied bounded inputs only; it has
no wall-clock read, running service, general event queue or production mutation.

| Proposed shape / operation | Units, ownership and contract |
| --- | --- |
| `OriginalClockWire` | unsigned raw day/millisecond words; source/start/profile identity; raw equality is preserved separately from normalized time |
| `GameClockState` | proposed nonnegative checked 64-bit admitted-game-millisecond timestamp, clock-profile/start identity, explicit pause/admission disposition, checked revision; normalized day/hour policy remains ratifiable |
| `AdvanceGameTime(state,duration,reason)` | single future authority-facing admission operation; explicit nonnegative game duration, not host elapsed or movement ticks; pause/zero is idempotent, overflow fails atomically |
| `ResolveScheme(map,sector,bindings)` | numeric identity and raw presence; sentinel/default versus explicit override; missing/ambiguous/unsupported is an error, not first-win |
| `QueryAmbient(profile,time,resolution)` | pure hourly outdoor/indoor pair under a named compatibility policy; same supplied state returns equal detached values without duplicate events |
| `LightState` | future authoritative description keyed by map/source generation/sector-record or object identity+overlay ordinal; direct/default provenance, chart location, signed offset units, validated ART/frame/mask and supported activity; no radius/intensity/pointer/Godot handle |
| `LightingSnapshot` | detached immutable numeric metadata: schema/profile/source version, clock revision/time/hour/day, active map/sector/scheme, ambient pair, bounded descriptions and readiness; object/frame/membership/selection/restoration generations |

**Shared coordinates:** Legacy and HD consume the **same** authoritative chart
and clock/light state, using the area/grid world-cell convention (64 cells per
sector). They may have different presentation scaling/materials, never separate
clocks or emitter simulation. Wire locations, cell coordinates and projected
screen-pixel offsets are distinct units. A future source-profile transform must
prove stationary conversion and hotspot anchoring; the synthetic rectangular
worksheet does not establish that transform.

**Invalidation proposal:** resolved scheme/input version, applied hour, map/active
sector, object activity/membership/location/offset, ART frame/mask/tint revision
and restore generation form the key. Hour, resume, jump, default change, movement,
light OFF/tint/animation change, neighbour coverage or restored state invalidates
affected output. Repeated queries without semantic change do not mutate versions.
Failure/cancellation publishes no partial new snapshot and retains the previous
complete snapshot in a future owner. The research CLI publishes only one complete
receipt after admission/identity checks; it is not that future owner.

**Lifetime:** future provider owns source streams/frames/caches; no stream/pixel
lease escapes into authoritative state or renderer metadata. Detached read-only
copies survive owner disposal; new source generation prevents stale reacquisition.
The helper's returned hourly/overlay metadata are detached/read-only and tested.
Overlay ABSENT, NULL, EMPTY, VALUES and inherited effective results all reject
mutation. Test-owned FILE records exercise prototype/instance array presence and
override masks through the actual research parser before default resolution.
Texture/pixel caches are renderer-owned. No scene lifetime or GC heap guarantee is
asserted by these caps.

## Inspector, limits and verification

Implemented research entry points in
[CrashSiteProbe](../../tools/research/Arcanum.CrashSiteProbe/TimeLightingProbe.cs),
with [literal self-tests](../../tools/research/Arcanum.CrashSiteProbe/TimeLightingSelfTest.cs):

```sh
"$DOTNET" restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --time-lighting-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- \
  --time-lighting --module "$ARC_AREA_MODULE" --prototype-root "$ARC_AREA_PROTOTYPES" \
  --mobile "$ARC_AREA_MOBILE" --mobile-selection "$TIME_LIGHTING_CORE_SELECTION"
```

Use the [pinned toolchain](../workflow/toolchain.md). Original arguments are opt-in
and read-only; never substitute unspecified local paths. The optional paired
mobile arguments require a private `time-lighting-core-selection-v1` JSON witness
with `Schema`, `SourceSha256`, and exactly 88 `Records` of `Offset`, `Length`,
`Sha256`, obtained from an independent authenticated core census. Each selected
record must hash-match, parse to exact EOF, be nonoverlapping and locate within
core. This is an explicitly witness-dependent **selection**, not an independent
whole-aggregate completeness certificate. Without it mobile readiness is
NOT_READY with that input/oracle dependency. Missing/mismatched supplied inputs
fail, not silently degrade to editor sources or an `AreaOriginal` PASS.

**Proposed bounded research policy, enforced by this inspector:**

| Admission | Limit |
| --- | --- |
| archive | 512 MiB; streamed full-file hash before any payload expansion |
| index / total names | 64 MiB each |
| entry count / one name | 131,072 / 4,096 bytes |
| one stored / decoded payload | 1 MiB each |
| selected sectors / referenced schemes | 32 each |
| MES pairs | 8,192 per resource |
| stationary records | 1,024 per selected sector, admitted before framing |
| retained descriptions | 4,096 across stationary and inspected objects, charged before retention (including no-light descriptions) |
| referenced prototypes | 128 read-only files, each bounded by payload cap |

These are safety policy, not game maxima or a heap certificate. Cap+1, negative,
checked multiplication/addition overflow and incomplete required coverage reject;
no silent dropping to fit. DAT caps delegate to the existing `DatArchive` guards;
the new tests bind exact policy values plus admission boundaries, while existing
Data/DAT tests exercise reader limits. Resource tokens are bounded relative names;
no archive extraction destination or executable path is constructed.
Selected-sector and referenced-scheme tests share the inspector's admission
seams: 32 is accepted, 33 rejects before payload expansion or new ID retention;
rejection leaves the admitted count/set unchanged. Repeated scheme IDs consume
no additional slot, including at the cap.

Read-only handles remain bound to path device/inode, size, modification/change
identity and streamed SHA-256 before/after use (supported macOS/Linux .NET 8 ABI).
Selected entry/record/prototype digests are retained. Replacement with the same
content is not accepted as unchanged input. Output contains role/IDs/counts/
fingerprints/status only; all original colors/text/pixels remain transient.
Safe error codes never emit a payload, arbitrary exception message or local path.
The CLI has no partial stdout receipt on rejection/cancellation. Linux adapter
behavior is not independently executed here; actual checks ran on macOS.

| Independent synthetic matrix | Exercised seam, not a claim of original parity |
| --- | --- |
| T1 | raw wire, millisecond hour/day boundaries, strict source carry vs normalization, month/year/current accessor quirks, overflow |
| T2 | pause/resume/zero/query idempotence, explicit multi-day jump, source elapsed gate/turn-based distinctions, relative deadline and already-due distinction |
| T3 | numeric default/sentinel/override/changed-default, adjacent queries, distinct channel order, unique/missing/case-ambiguous resources and unsafe tokens |
| T4 | consecutive/separated positive/negative nondivisible gaps, fresh reload, missing/edge/all-empty keys, malformed channels/hour/MES |
| T5 | independently authored 48-byte wire with unequal r/b/g and padding, truncation/count/cap/overflow, absent/null/empty/zero/default overlay provenance |
| T6 | literal OFF/DARK/tint/both flags and distinct scenery/inventory/respawning load versus hour-refresh decision tables |
| T7 | neighbour full-key dedup/order/conflicts/coverage, seam-crossing support, distinct layer applicability and synthetic quantized/saturating composition |
| T8 | cap equality/+1/negative/overflow, cancellation/failure publication, changed/replaced/disposed input, immutable detached metadata |

Actual original checks compare the **production research helper** against an
independent Python DAT/framing/struct oracle and independent field tuples rebound
to current hashes; expected results do not call the C# evaluator. Synthetic
wire/clock/gap literals were separately computed/checked. Required regression
checks remain locked root restore/build/non-RealData tests, DAT/grid/topology
self-tests and existing CrashSiteProbe self/area tests. Data/public consumers are
unchanged, so this work does not add a full visual-area launch as a clock gate.
Local-only / CI_NOT_REQUIRED; GUI/export NOT_REQUIRED for this Godot-free research.
Neither designation waives independent review or future GitHub delivery gates.
Execution logs, TRX, full ledger and input paths remain private, outside commits.

## Readiness and dependency-aware next steps

| Capability | Research result / next dependency |
| --- | --- |
| clock/start/calendar/admission | source chain and actual selected startup established; production profile/calendar/pause/save policy requires ratification |
| numeric default/hour channels | actual selection inspected and strict helper tested; original multi-gap/reload/runtime parity NOT_VERIFIED |
| nonzero actual sector override | none in the bounded selection; outside-selection acquisition NOT_READY; synthetic semantics only |
| stationary cave | four complete wire projections; coordinate conversion unknown + ART/mask/state binding NOT_READY; cave normal-game provenance not established |
| exterior primary objects | 42 non-invalid descriptions, direct/default provenance inspected; live activity/render-guard/state/ART masks NOT_READY |
| overlay / carried/equipment | no non-invalid selected overlays/mobile primaries; actual representative beyond this scope and lifecycle integration NOT_READY |
| neighbours/layer/composition | source seams + synthetic policy established; actual support/masks/BPP/blit/mode integration NOT_READY |
| luminance/gameplay/original visuals | NOT_VERIFIED; needs separately accepted original/runtime/renderer evidence |

Future implementation order is a **proposal, not dispatched milestones**:

1. Ratify clock compatibility/start/calendar/admission/restoration policy, keeping
   movement time distinct. Acceptance: literal before/at/after boundaries, pause,
   resume, zero/jump/overflow and restore generation across all consumers.
2. Prove source selection/schema and typed read-only primary/overlay/stationary
   projection, including coordinate/mask/activity. Acceptance: independent actual
   identities/default provenance; unsupported required input fails readiness.
3. Introduce one Godot-free authority plus detached shared Legacy/HD snapshot.
   Acceptance: version/lifetime/invalidation/dedup/coverage and atomic publication,
   without renderer-side time advancement or a guessed 3x3 light radius.
4. Separately scope Legacy palette/mask/layer integration and mode-specific roof
   behavior. Acceptance: actual supported source and exported visual evidence;
   synthetic scalar composition alone cannot satisfy it. HD artistic relighting,
   AI, weather, travel/rest/combat/equipment/healing/save implementation remain
   separate dependencies and are not authorized by this research.

