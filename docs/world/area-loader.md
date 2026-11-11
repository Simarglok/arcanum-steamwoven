# Complete Crash Site exterior data loader

[Whole-exterior traversal research](area-traversal-contract.md)
consumes this immutable FILE-effective snapshot; visual completeness does not
close post-load registry or actor state and grants no movement permission.

## Scope and evidence boundary

`Arcanum.Data.Area` is a synchronous, Godot-independent, read-only loader for the
selected FILE-effective Crash Site exterior. It implements the data seams of the
[area research contract](area-contract.md), not an area renderer,
simulation, save writer or universal map loader. It reuses `DatArchive`,
`TerrainLoader` sector framing and `ArtDecoder`; its internal `ObjectWireReader`
is shared with the frozen static-object reader. Source-derived specifications and
their addresses are recorded in the
[inventory](../research/area-inventory.md) and research contract.

The authenticated version-one profile selects catalogue map 5000, runtime map 1,
start sector (1452,1290), and townmap 2. A complete metadata survey selects the
four-neighbor townmap component, then the surrounding eight-neighbor context ring;
it does not treat the whole module map as Crash Site. The current profile verifies:

| Domain | Required reconciliation |
| --- | ---: |
| Surveyed exterior sectors | 621 |
| Core sectors, X1451..1453 / Y1289..1291 | 9 |
| Context sectors present / absent | 11 / 5 |
| Core / context terrain cells | 36,864 / 45,056 |
| Core static objects | 426 |
| Normal-game aggregate records | 7,399 |
| Aggregate core / context / outside / unknown locations | 88 / 10 / 7,301 / 0 |
| Aggregate/editor correspondences | 7,399 |
| Required prototype definitions | 56 |
| Distinct selected terrain frame chains plus core object bodies | 2,444 |

All 88 core mobile bodies are retained, including 15 with the inventory-insertion
refusal disposition. A reference ready for decoding is not permission to insert or
draw that object. Context has metadata/terrain coverage, not complete context-object
visual readiness. Absent context sectors remain explicit absent records, never
zero terrain or collision blockers. Outside mobiles participate in reconciliation,
not area object publication. Lights, roofs, framing sections and terminal object
counts remain separate sector metadata. Active required composite references that
this profile cannot resolve fail completeness; absence in these originals does not
claim general overlay/underlay, wall or roof-rendering support.

The cave is a second pinned profile (see [Crash Site cave profile](#crash-site-cave-profile)).
Executing the recorded connector, PATCH/dif/loose-file precedence,
lighting, time, navigation, interaction, AI and original runtime/pixel parity remain
unimplemented. The snapshot reports `RendererExecutionVerified == false` and
`GameplayVerified == false`. The independent frozen 4,096-terrain/317-record sector
regression is separate and unchanged; this API does not widen the five-only static
resource API or the bounded traversal profile.

## Crash Site cave profile

ARC-WORLD-009 adds `AreaOriginalValidator.OpenCave(inputs, options)` and
`OpenCaveFiles(module, resource1, resource2, caveMobile, prototypes, options)`
for the cave, catalogue map 5078 / runtime map 79, under the pinned profile
`arc-world-009-cave-file-effective-source-v1` (`CaveProfile`). It uses the same
owner, budget, identity and frame contract as the exterior. Facts and sources are
in the [cave transition research](../research/cave-transition-evidence.md).

| Domain | Required reconciliation |
| --- | ---: |
| Surveyed cave sectors | 1 |
| Core sector (32,31), townmap 141, 4 serialized lights | 1 |
| Context sectors present / absent | 0 / 8 |
| Core terrain cells | 4,096 |
| Core static objects (125 walls, 13 scenery) | 138 |
| Normal-game aggregate records, all core | 39 |
| Aggregate/editor correspondences | 39 |
| Required prototype definitions | 30 |
| Distinct terrain frame chains plus core object bodies | 194 |

The NormalGameMobile role is the cave's game-generated object file (6,698 bytes,
SHA-256 `aa29827d…8318`), never the exterior one. The profile binds all ten mapping
roles, including the two wall tables.

**Walls.** Object type 0 is admitted only when a plan binds both `WallStructure`
and `WallName`; otherwise it stays `UnsupportedVersionOrSchema`, as before. A wall
selector (art class 1) chooses one file: the structure row for its wall number gives
an interior name (rotations 0, 1, 6, 7) or an exterior name (2–5), matched by its
first three characters to exactly one wall-name row; then the piece code, damage
letter `u` and the variation digit. Damaged walls (selector bits 0x480), unknown
pieces, other flag bits (1–3, 6), missing rows and ambiguous names fail. Structure
rows are positional in the source, so a table whose keys below 1000 are not exactly
0..n−1 is refused. The visual is physical group 0 of a single-rotation ART (an
eight-rotation wall ART fails), frame 0, requested palette from bits 4–5;
`NominalFacing` keeps the rotation and `Mirrored` keeps the flip bit (bit 0). For a
wall, `Mirrored` means that flip bit, whose hotspot rule (width − hotX − 2) differs
from the critter rotation alias. The loader does not move hotspots: drawing
a wall (hotspot adjustment for rotations 0, 1, 6, 7 and the flip) and wall edge
blocking are the next ARC-WORLD-009 steps.

**ART rules.** Since ARC-DATA-036 the cave profile is built by the rules of every plan
([one rule set](whole-map-plans.md#one-rule-set-arc-data-036)); its former pinned
exceptions are those rules: four contained items whose ground ART has trailing bytes
after its last frame (one NUL for entry 919, the bytes `02 00` for entries 955 and 956)
are read from the ART's own extent (`original-art-extent-v1`), and one container whose
single-rotation ART is selected with rotation 1 is drawn from group 0
(`original-single-group-rotation-v1`). The cave profile equals the cave's whole-map plan
in every visual (opt-in test).

**Endpoint.** The snapshot publishes the profile's expected endpoint after the owner checks
it against the cave's objects (present static core object, id, prototype and use script):
cave sector 2080374816 index 99, prototype 4047, use slot 1, script 1532 → catalogue 5000 /
runtime 1, arrival (92913,82598). The exterior snapshot keeps its endpoint into the cave.
Both are the one use transition the maps' transition data give in each core (ARC-DATA-036,
opt-in test). Neither is executed by Data.

## API and explicit sources

- `AreaInput(key, stream, leaveOpen)` transfers an explicit readable, seekable
  stream. Keys and stream instances must be distinct in `AreaInputSet`.
- `AreaInput.FromFile(key, path)` opens a read-only path-bound adapter. On macOS
  and Linux it binds handle/path device, inode, size and modification/change times
  using the .NET 8 `System.Native` file-status ABI. On Windows (ARC-DEV-049), where
  that ABI does not exist, the handle's read-only share mode makes the system refuse
  every other writer, rename and delete of the file itself while it is open, and the
  adapter compares the handle's and the path's size and last write time, with
  managed calls only. A path that resolves elsewhere later (a retargeted junction
  or link in a parent folder, a changed drive mapping, a remount) is bound only by
  that size and write time; the bytes are still read and authenticated through the
  handle. A timestamp change by another program fails the load conservatively
  (`InputChangedDuringRead`). Another system is
  refused (`PlatformNotSupportedException`). Stream-only inputs bind bytes, not a
  filesystem path. The Windows binding is tested on macOS (its managed calls are
  the same there) and has not yet run on Windows.
- `AreaDataOwner.Open(inputs, plan, limits)` authenticates every input against the
  explicit length/SHA-256 plan and creates owned archive indexes. Constructor
  `AreaSourcePlan` values produce `structural-file-effective-v1`, **not** proof of
  original authenticity. Maps, mapping entry identities/payload hashes and optional
  exact ART bindings are explicit; arbitrary resource precedence is never inferred.
- `AreaOriginalValidator.Open(inputs, options)` instead uses the pinned original
  profile `arc-data-009-complete-file-effective-source-v1`. It requires Module,
  Resource1, Resource2, NormalGameMobile and all 56 numbered Prototype inputs.
  Whole-file identities, mapping identities and required reconciliation are fixed
  in `OriginalProfile`; callers cannot turn a synthetic plan into that profile.
- `Inspect(token)` returns `AreaInspection`. Structural/resource/budget failures
  yield concrete issues; an appearance failure may retain a diagnostic candidate
  snapshot. Only `IsComplete` means a snapshot with zero issues. Diagnostic
  candidates are never published or eligible for frame acquisition.
- `LoadStrict(token)` uses the same path and throws `AreaIncompleteException`
  with the report unless complete. `PublishedSnapshot` changes only after a complete
  build and the final authentication pass.
- Authentication passes (ARC-RENDER-009). The first `Inspect` after `Open` uses the
  open-time authentication as its opening one: I0 then re-checks only every input's
  path binding and length, and I7 hashes every input after the build as before, so
  that build is bracketed by the `Open` and I7 hashes. Later builds hash at I0 and I7.
  A whole-set pass (I0, I7, a frame batch's `Complete`) hashes the inputs of at least
  `AreaLoaderOptions.ParallelAuthenticationMinBytes` (default 1 MiB; 0 makes every input
  eligible) in parallel when two or more take part; smaller inputs keep the sequential pass.
  The smaller inputs are checked first, one by one; then the large inputs' scratch
  admission and path/length checks run in plan order, their reads run in parallel, and
  their verdicts, position resets and closing path checks follow in plan order, so the
  first failing large input in plan order is reported. During the parallel reads only the
  read counters and the first budget rejection change (synchronized); a read rejection
  there names the pass, not the input. After a failed pass the read counters depend on
  how far the other inputs got, and a pass holds one 69,632-byte scratch per large input
  at once. A stream passed to the owner may be read from a worker thread and must not call
  back into the owner while it is read: the owner's public getters take its lock, which
  the pass holds (`CurrentPhase` is an unsynchronized internal phase for tests).
- Verified-file memo (ARC-DEV-374, O5a of the [performance diagnosis](../research/performance-diagnosis-2026-11.md); owner decision
  2026-11-11, option A). The authentication rule above is "every input is hashed in full at every pass". For a file of at least
  1 MiB (`VerifiedFileMemo.MinBytes`: the archives and large loose inputs; prototypes, patches and small resources keep the
  full pass every time) the rule is now: **the first check of that file in the process hashes it in full**; every later check
  of the same path (the map plan's identity, `Open`, I0, I7, a frame batch's `Complete`, the viewer's `VerifyFiles`; in the same
  load or a later one) reuses that hash while the file's stamp is unchanged. The stamp is the device, inode, size and the
  modification and change times to the nanosecond on macOS and Linux (a file rewritten with its modification time put back still
  moves the change time), and the size and last write time on Windows. A changed stamp is a first check again: the file is
  hashed in full. A hash is remembered only when the stamp is the same before and after the pass that computed it. The memo
  keeps a path, a stamp, a length and a hash, never bytes, and is not a trust root: a pinned plan still compares the memoized
  hash with its pinned hash, so a file that is not the pinned file is refused by the first check and by every later one. A
  path-bound input (`AreaInput.FromFile`) still has its handle/path binding and length checked on every pass; only the read of
  its bytes is skipped, so the read counters of a later load are lower than the first's, and a stream-only input is always
  hashed. The effect on I7: a load's build is still bracketed by its binding checks, but the bytes of a file already hashed in
  this process are not read again at I7, so a rewrite of an archive in place during a load is seen only through its stamp: it is
  caught unless it leaves the size, the modification time and the change time all as they were (a user-space program cannot
  restore the change time), and on Windows unless it leaves the size and last write time as they were. A file rewritten in the same filesystem
  timestamp tick as the pass that hashed it keeps its stamp (the "racy timestamp" case of any stamp cache); the tick is a
  nanosecond on APFS and 100 ns on NTFS, so it is a known limit and not a guard. The legacy terrain/actor modes of the
  viewer (`TerrainLoadCoordinator`, not the AREA load) keep their own full fingerprints and do not use the memo. Memo state is process-wide and lives until the process ends; it is not saved.
- `AreaOriginalValidator.Validate(inputs, token)` is metadata-only convenience:
  its owner is disposed before returning. Use `Open` when frame access is needed.

A caller that has already assembled the exact inputs can use:

```csharp
using var owner = AreaOriginalValidator.Open(inputs);
AreaSnapshot snapshot = owner.LoadStrict(cancellationToken);
AreaVisualRecord selected = snapshot.Visuals[0];
using var lease = owner.AcquireSelectedFrame(snapshot, selected.Key, cancellationToken);
byte[] independentRgba = lease.CopyRgba(); // explicitly charged caller-owned copy
```

Many frames are copied through one batch instead:

```csharp
using var batch = owner.BeginFrameBatch(snapshot, cancellationToken);
var copies = keys.Select(key => batch.CopySelectedFrame(key, cancellationToken)).ToArray();
batch.Complete(cancellationToken); // discard every copy unless this returns
```

Build the input set inside an exception-safe scope: `FromFile` opens immediately,
so the caller must close already-created inputs if constructing later inputs fails
before `Open`. Once `Open` is called, it honors each `leaveOpen` on both success and
failure. While owned, streams must not be externally sought, mutated or read.
The owner serializes its methods; it does not make external stream use safe.

## Identity, values, frame access and lifetime

Sector coordinates are nonnegative 26-bit values with checked pack/unpack and
64-cell origins. Static serialized locations rebase their low six bits into the
source sector; mobiles retain direct signed world locations. Missing/negative
mobile locations fail, never default into the core. Keys include source SHA-256,
runtime map, kind, sector where applicable, record index/offset/length and full
object-ID fingerprint; a local source index is not a global key.

Snapshots own immutable copied numeric metadata and fingerprints, not stream views,
original filenames, text, encoded ART or pixels. Direct fields preserve presence
and origin; effective values use authenticated actual prototype defaults. Explicit
zero, null and sparse-empty values override absence. FILE sparse masks/bitsets,
population, exact record advancement and instance/editor bijection are validated;
there is no scan-to-next-version recovery or unchecked native handle restoration.

Appearance resolution covers the admitted tile/facade and class4/7 scenery/container,
class6 ground-item, class2/12/13 NPC and, where a plan binds both wall tables, class1
wall source rules (see the cave profile). Object types 2, 3, 5–14 and 16 are
admitted bodies, plus type 0 under the same wall condition and type 1 where a plan binds
the portal names (types 10–12 and portals since ARC-DATA-036, as on whole maps); ammo
(type 6) was added with the cave, which carries two. Since ARC-DATA-036 the profile has no exceptions of
its own: it is built by the rules of every plan ([one rule set](whole-map-plans.md#one-rule-set-arc-data-036)).
Four item records whose ART entry (1,419 bytes) ends with one NUL after its last frame
are read from the ART's 1,418-byte extent (`original-art-extent-v1`), and one unique NPC
asking its single-rotation ART for facing 5 is drawn from group 0
(`original-single-group-rotation-v1`), as before under pinned per-object policies; the
independent oracle still matches every entry, view and pixel. Raw selectors, full
payload identity, view length and policy remain in descriptors. Ambiguous sources,
names the mapping cannot build and unsupported state/physical selection remain failures;
a resolved name no resource holds is drawn from `art/badart.art` when the resources hold
it (`original-badart-v1`), else it fails. Palette fallback is the existing decoder's explicit present-slot
policy, exposed as requested and effective palette, not arbitrary asset fallback.

`AreaVisualRecord` retains group/frame/palette, mirroring, signed hotspot/ART/object
offsets, scale, sizes and RGBA digest. It supplies diagnostic placement inputs,
not a draw rectangle, collision shape or original ordering claim. The diagnostic
anchor remains `40*(dy-dx)+offsetX`, `20*(dx+dy)+offsetY` followed by hotspot subtraction;
a renderer must separately ratify visibility, scale/mirror and overlap behavior.
The authenticated exterior-to-cave endpoint is numeric source evidence only,
not verified activation, normal-game cave provenance or transition readiness.

`AcquireSelectedFrame` accepts only this owner's current publication/generation and
an exact visual key. It reauthenticates the source before/after decoding and checks
entry/payload/geometry/palette/pixel identities. At most one revocable lease is live.
Loading while leased, a second lease, foreign or superseded snapshots fail.
`ReadPixelByte` and `CopyRgba` fail after lease/owner disposal; no raw pixel view
escapes. An explicit copy survives independently. Disposal is idempotent, revokes
leases and closes transferred streams. Previously returned metadata remains valid;
it cannot be used to reacquire frames after replacement or disposal. There is no
persistent encoded/decoded frame cache between operations of the pinned exterior and of any load that
passes no resident stores; the map path passes them ([resident frames and visuals](#resident-frames-and-visuals-across-map-loads-arc-dev-375)).

Inside one authenticated bracket, a strict build (between the I0 and I7 passes) or one
frame batch (between a source's first authentication and `Complete`), an ART entry is
read, inflated and payload-hashed once and then shared by every frame selected from it
(ARC-RENDER-009; Crash Site: 1,975 frames from 335 entries, 4.7 MB of decoded entry
bytes instead of 218 MB). The bracket's own authentication already covers every read in
it, so a second read of the same entry adds no assurance. Each frame still makes its
own frame-domain admission, ART table inspection, decode and geometry, palette and
pixel identity checks. This entry memo is owner-private, never escapes, is limited to
16 MiB, is held in the retained domain only while a quarter of that domain stays free
for mandatory admissions after the hold (otherwise the entry is simply read again), and
is dropped when its bracket ends or faults, or when the batch or the owner is disposed.
A batch serves memoized bytes only after their payload hash matched the publication: a
mismatch faults the batch and drops the memo.
Usage counters therefore record fewer reads and inflate allocations than one read per
frame; the cumulative frame-domain admissions are unchanged.

`BeginFrameBatch` copies many frames of the same current publication under the
same single slot: a lease, a second batch or a load fails while a batch is open,
and foreign or superseded snapshots fail. Each source is whole-input authenticated
inside the admitted frame that first uses it, and `Complete` authenticates every used
source once more (one more F phase visit). Every frame keeps the entry, payload,
geometry, palette and pixel identity checks and the lease's frame-domain
(requests, stored, decoded, RGBA, encoded) and copy charges; authentication scratch
and reads are charged once per source and side instead of per frame. A source that
differs from its identity when `Complete` runs is reported there, so a caller must
discard all copies unless `Complete` returns; a change made and reverted between two
authentications is seen only if a per-frame check reads it (with the entry memo, only
the first frame of an entry reads it). Any failed copy or
`Complete`, including cancellation, faults the batch; faulted, completed, disposed or
revoked batches refuse further use. The batch therefore guarantees what per-frame
reauthentication did for the batch as a whole, not for each frame separately; for the
Crash Site profile it reads about 1.2 GB of authentication instead of about 596 GB.

## Resident frames and visuals across map loads (ARC-DEV-375)

Status: **implemented by ARC-DEV-375** (design written before the code; measurements at the end of this section). O5c of the [load time diagnosis](../research/performance-diagnosis-2026-11.md); the owner
approved keeping the decoded repository between loads of the same map). The rule "no persistent encoded/decoded frame cache between
operations" above stays for the pinned exterior (the Crash Site smoke, `ExecuteExterior`) and for every load that passes no resident
store. The map path (`AreaMapLoadOperation`, whole interiors and the main map's windows) passes two.

**Why.** A window move, a cave return and a cross-map quick load each build a new owner and read the same ART again: the strict build
reads, inflates, hashes and decodes every distinct frame selection to record its pixel digest (`MakeVisual`), and the frame copy then reads and
decodes each of them a second time (`DecodeDescriptor`), behind two whole-archive authentication passes of the batch. On the main map that is
about 2,000 distinct frames (25 MiB) per window, most of them the same in the next window. The diagnosis measured the strict snapshot at 1.3 s
and the frame copy at 1.2 s of a 6.4-14.9 s worker (packaged, Apple M4 Pro); this task measures them again beside the new numbers (below).

**What is kept.** Two process-resident, content-keyed stores. Neither holds a file handle, an owner or a snapshot.

| Store | Where | Key | Value | Replaces |
| --- | --- | --- | --- | --- |
| Visual memo (`AreaResidentVisuals`) | `Arcanum.Data` | the owner's build-local `_sharedFrames` key (entry identity, group, frame, palette, single-rotation class flag, fallback policy) plus the entry's source `Length` and `Sha256` from the plan's identities | the `AreaVisualRecord` template (geometry, payload and pixel digests, policy) | the entry read, inflate, payload hash, decode and pixel hash of a frame selection already built in this process |
| Frame store (`AreaResidentFrames`) | `Arcanum.World` | `AreaFrameKey` (entry, palette, group, frame, policy, payload digest, view length, pixel digest) | `DecodedAreaFrame` (RGBA, shared, immutable by convention: no consumer writes it) | the frame copy's decode, and its batch when every frame hits |

**Authentication rules are unchanged where they bind.** The strict build still authenticates every input at `Open` and again at I7;
O5a ([DEV-374](../research/performance-diagnosis-2026-11.md)) is the separate decision that may reduce those passes. A hit is served inside that
bracket and is keyed by the identity the bracket authenticates, so a changed file gets another key and misses (a file changed during the read is still
refused at I7). What a hit skips is only re-deriving, in the same process, a value that was derived from the same authenticated bytes:

- a **visual record** is inserted only after its build's I7 pass succeeded (it is staged in the owner and committed there; a failed or cancelled build
  commits nothing), so every record in the memo was derived inside a fully authenticated bracket;
- a **frame** is inserted only after its batch's `Complete` succeeded, so it passed the entry, payload, geometry, palette and pixel checks and the batch's
  authentication; a faulted or cancelled batch inserts nothing;
- a frame hit is accepted only when its width, height and RGBA length equal the descriptor's, and the pixel digest in the key is the descriptor's;
- an all-hit copy opens no batch and so adds no second authentication pass beyond the build's own (the batch's passes authenticate the sources the
  batch read from; this load read none); it still refuses a snapshot its owner has not published, as the batch's own check does;
- the native smoke stage `resources` (a reload on a fresh owner with fresh counters, the retired generation collectable) empties both stores before its
  reloads, so it keeps proving that contract; the real-data plan test that counts a cold build's frame requests empties the memo per map.

The deviation from the contract above is therefore stated: a frame served from the store is not decoded again in this load. Its pixels are the decoder's output
for bytes the same process already authenticated; the key carries the pixel digest the strict snapshot of this load published, and the decoder is deterministic.
Nothing is written to disk, so a stale store cannot outlive the process.

**Budget accounting.** A hit charges none of the owner's frame-domain counters (requests, stored, decoded, RGBA, encoded) and none of the
copy charges, as nothing was read or decoded. The copy's pre-admission still tests the frames that must be read. The load result's `FinalUsage` on a warm load
is therefore lower than on a cold one; no limit gets tighter. The per-load repository cap (16,384 frames, 160 MiB) still applies to the `Frames` of the result.

**Memory.** Frame store: at most 256 MiB of RGBA and 32,768 frames, least recently used first, by bytes; every frame a load uses is touched, so the
window just loaded is the newest. The frames also live in the load result (the result's own 160 MiB cap), so the store's cost is the frames of earlier windows
that are not in the current one: bounded by 256 MiB. Visual memo: at most 65,536 records, least recently used first (about 0.5 KiB each, about 32 MiB).
Together under 0.3 GiB resident on top of the load, against 24 GB of RAM on the development Mac. A Windows or smaller machine keeps the same caps; the caps are
constants of the stores, not settings. The viewer's GPU caches (the 512-frame/32 MiB allowance and the terrain chunk cap) are separate and unchanged: a new
load still releases every area frame texture, so equal keys never reuse another load's textures, only its CPU pixels.

**Invalidation.** None is needed for correctness: keys carry content digests (and the source identity for the memo). The stores shrink by their caps only;
`Clear()` empties them (tests, and a viewer that wants the memory back). A different game folder or an edited archive yields other keys and the old entries age out.
A different data-rules profile (Original or Steamwoven) changes map data, not ART selections, and shares both stores.

**Threads.** The load worker reads and inserts under one lock per store (a load runs one worker at a time; an overlapped reload's cancelled first generation
inserts nothing that was not committed). The main thread only reads the immutable frames of its own result.

**Not kept, and why.** The strict snapshot of a plan, its scene plan and its step evidence. A window move loads another window (other sector files), so the
whole snapshot differs; the part that was expensive and is shared, the ART work, is what the two stores keep. A reload of the *same* window (a cave return to
the same arrival, a quick load in place) could keep the snapshot too, but doing so skips the build's authentication, which is the O5a decision; a follow-up
(snapshot keyed by plan and input identities) belongs after O5a. The map's transitions are O5b ([DEV-368](../research/performance-diagnosis-2026-11.md)).

**Measured (ARC-DEV-375).** One `perf` smoke (Debug source, `--sector 1409,1314`, Mac under load: load average 15-24, so only ratios inside one run mean anything),
the same build with `ARC_AREA_RESIDENT=0` (before) and without it (after). Worker of the **return to the main map** after a trip to a temple interior (the cave-return case),
milliseconds: `read.strict` 3,120 -> 3,950, `read.copy` **1,809 -> 469**, and the copy's allocation 103.5 MiB -> 1.9 MiB; frames served 1,617 of 1,623 (6 read), visual records served
2,459 of 2,466. The unchanged stages of the same leg (plan, transitions, open, verify) were 1.6x slower in the second run (3.2 s -> 5.2 s together), so the second run was the busier one; scaled by
that factor the worker's read stage fell from about 5.1 s to about 2.8 s (an estimate, not a measurement). The strict build still takes seconds with every visual served: its remaining cost is not the ART
decode, so it is the next thing to profile (the closing whole-input authentication, the per-object appearance resolution; not measured here). The first load of a process is unchanged (nothing is resident), the
temple's first visit read 76 of 242 frames (the main map's frames were in the store).

**Relation to residency (K1).** This is art residency in the loader, not the sector cache of mobile objects ([residency](../architecture/residency.md)):
the two share no state, and the object residency's fold and heartbeat rules are untouched.

## Resource contract: bounds, reservations, observations

Input-derived lengths, cardinality, arithmetic and admission checks precede the
controlled expensive work. Failures do not remove required records to fit a cap.
DAT admission projects compressed work before inflate, including repeated dynamic
Huffman tables; payload length alone is not a bound on decompressor work. Failed
and cancelled attempts retain cumulative work charges while dropping temporary
ownership. Limits are per owner lifetime; replacing a snapshot does not refund
reads, decoding, allocation reservations or frame requests.

Fixed boundary policies include 512 MiB/archive, 64 MiB/index/total names,
131,072 entries/archive, 4,096 bytes/name, 16 MiB stored/decompressed DAT payload
(1 MiB before ARC-DATA-036 for the pinned profiles),
prototype and record/string bounds, 128 fields, 262,144 sparse elements, 8,192 MES
keys, checked section/count/range framing and the shared bounded ART decoder
(4,096 frames/dimension, 256 KiB frame table, 4 Mi pixels, 16 MiB RGBA).
The component reservation limit can reject a frame below those individual decoder
limits. `Open` checks at most four non-prototype sources plus `MaxPrototypes`,
one binding per mapping role (ten roles) and `MaxVisuals` explicit ART bindings before authentication.
Caller-created source plans/input sets are outside the owner's allocation accounting;
callers must not construct unlimited enumerables and expect `Open` to undo that work.

Current configurable `AreaLoaderOptions` defaults are policy, not format maxima:

| Domain | Default |
| --- | ---: |
| Survey / present sectors / aggregate records | 621 / 20 / 7,399 |
| Aggregate bytes / prototypes / retained objects / visuals | 1,823,280 / 56 / 524 / 2,444 |
| `MaxManagedBytes` cumulative reservation | 4,454,350,848 |
| `MaxRetainedBytes` reference-ownership reservation | 370,147,328 |
| `MaxBuildPhaseBytes` per-phase reservation | 1,406,140,416 |
| `MaxFrameOperationBytes` component/copy reservation | 2,097,152 |
| `MaxReadBytes` cumulative requested wrapped-stream bytes | 193,343,722,807,071,426 |
| `MaxFrameRequests` | 4,890 |
| Stored / decoded frame-request bytes | 329,489,402 / 449,899,684 |
| RGBA / encoded selected-frame bytes | 81,853,096 / 9,383,648 |

The generous read cap is a finite adversarial/short-read admission policy, not a
performance promise or intended disk traffic. Defaults support two complete loads
and two further selected-frame requests for this profile, not unbounded reloads.

`Usage.ManagedReserved` and `PhaseReserved` are conservative loader-controlled
buffer/collection graph charges, **not measured CLR allocations**. `RetainedReserved`
tracks scopes/reference ownership (including fixed persistent capacity), not live
heap or bytes reclaimed by GC. Releasing a scope changes ownership, not cumulative
work. `ReadReserved` counts requested bytes, including retries/failed terminal reads;
`ReadActual` counts returned bytes, not physical disk I/O. Frame byte counters charge
requests and selected output domains; `ConsumerCopyBytes` counts explicit copies,
whose allocation also consumes cumulative/component admission. `PhaseHistory`
reports visits/cumulative/high-water reservations for O(open), I0(authentication),
I1(selection), I2(prototypes/mappings), I3(sectors), I4(aggregate), I5(correspondence),
I6(snapshot), I7(final authentication), F(frame), C(copy). `FirstBudgetRejection`
retains the first counter-domain/callsite rejection without partially committing
that reservation. Cardinality failures may instead be direct `AreaIssue` failures.

This contract does not certify every runtime allocation, JIT/type-loader/native
cache or exception-stack byte, total process RSS, GC reclamation timing, or caller
retention. Cold/warm allocation observations and post-disposal checks are sampled
runtime regressions, not mathematical worst-case heap certificates.

## Errors and verification

Issues distinguish missing/ambiguous sources, input identity versus mid-read change,
malformed records, unsupported version/schema/ART state/physical selection,
unknown location, duplicate identity, invalid selection, budget, lease and snapshot
mismatch. Offsets/source indexes are retained when the failing seam has them.
Cancellation propagates `OperationCanceledException`, not success or an empty report.
Argument misuse/disposal retain standard .NET exceptions. A failed/cancelled load
leaves the previous publication untouched and unwinds temporary ownership.

Independent public synthetic tests exercise production `Open`/`Inspect`/`LoadStrict`
and frame access: adjacent edges/keys, FILE masks/defaults/sparse arrays, aggregate
EOF/identity/correspondence, malformed/overflow/cap+1, source changes/path replacement,
missing/ambiguous resources, physical selectors, palette/geometry, immutable copies,
phase cancellation and lease/batch/disposal. Run with the pinned [toolchain](../workflow/toolchain.md):

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~.Area.&TestCategory!=RealData'
```

The cave's opt-in check (`FullyQualifiedName~AreaCaveOriginalTests&TestCategory=RealData`)
uses `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`,
`ARC_AREA_PROTOTYPES` and `ARC_CAVE_MOBILE`, the cave's game-generated object file.

For the opt-in full-original check, set `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`,
`ARC_AREA_RESOURCE2`, `ARC_AREA_MOBILE`, `ARC_AREA_PROTOTYPES` and `ARC_AREA_ORACLE`
to explicit local read-only inputs. The oracle is a retained independently computed
numeric manifest with SHA-256
`bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60`, not generated from
production constants during the check. Its original-data details remain private;
no game payloads or machine paths belong in Git. Missing inputs are not a pass.

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~AreaOriginalTests&TestCategory=RealData' \
  --logger 'console;verbosity=detailed' --logger trx
```

The check compares every published occurrence and all 2,444 visual chains against
the independent manifest, repeats a deterministic complete load, exercises selected
frame copies and verifies transferred handles close while metadata survives.
It emits sampled cold-open/cold-load/warm-load/allocation/GC observations. GC live
bytes can include test-retained snapshots/oracle/serialized strings; they are not
owner-only retained heap. Root, affected standalone consumers and separate frozen
terrain/static/traversal regressions remain required. No graphical/export check is
implied by this data-only API or these observations.
