# Crash Site area data/render contract (research policy)

## Status and scope

This is ARC-RES-015's proposed contract for the complete selected exterior research
area in the [canonical inventory](../research/area-inventory.md).
It is **not a production API implementation or dispatch instruction**. It does
not widen the frozen [terrain loader](terrain-loader.md),
[static-object loader](static-object-loader.md), actor profile or their admitted
five-object resource scope. A decoded frame is not evidence of a drawn, passable
or interactive object. Full exterior visual readiness is presently blocked by
86 core mobile visual gaps, plus unverified ordering/visibility semantics.

Labels below are **source fact**, **local observation**, **project policy** or
**unknown**. A source fact is a reading of the original's functions at the cited
addresses, evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists it; it
carries no original-executable parity claim.

## Source specification locators

Locators name the original's functions by address where one is recorded.

| Topic | Source fact / locator |
| --- | --- |
| Map catalogue and selector; editor versus normal-game mobiles; filename transform | the map functions: mobile load (0x410D10), filename transform (0x411830), map list load (0x4125C0) |
| Region attributes, not a named area polygon | the original's area attribute functions |
| Sector section order / independent terminal object count | the original's sector load, [framing evidence](../research/crash-site-framing.md) |
| Low tile bits and static rebase | the sector object list load (0x4F1350) |
| v119 fields/types and actual instance/prototype fallback | the object field layout, the array field fetch with prototype fallback (0x408BB0) and wire I/O |
| Sparse array serialization / fresh runtime handle | the sparse-array and object bitset FILE routines |
| Packed scenery categories and ART semantics | TIG ART id fields, the original's name functions, [ART subset](../formats/art.md) |
| Object flags, composition and placement | the object flag definitions, the object draw function (0x43B390) |
| Object ordering and roof stage | the sector object list ordering (0x4F20A0), the object draw function, the game draw loop (0x4046F0), the roof functions |
| SAP structures, actions, literal operands and potential teleport targets | the script action executor (0x447220), including its fade/teleport action |

## Proposed API seams

Names below describe responsibilities; they are not claims that these classes
already exist. Reuse existing Godot-independent Data/Presentation modules rather
than introducing one assembly per concept.

### 1. Area selection and immutable source plan

Project policy inputs:

- Explicit module/resource archive handles and size/hash expectations; explicit
  prototype directory provider; normal-game aggregate locator with expectation.
- Catalogue/runtime map identity, start-containing sector, townmap membership
  selector and boundary-context policy from the inventory, including missing cells.
- Caller-supplied checked allocation/read/cache limits, cancellation, and a source
  selection policy that preserves variants rather than inventing a global winner.

Output: copied deterministic sector keys, coordinates, half-open bounds,
core/context inclusion reason, source identities/ordinals and boundary issues.
Order by numeric sector ID then local index, never filesystem enumeration.
Full621 metadata survey precedes selecting the nine-member component. The core
is a project rectangle induced by this input's component, not an engine region.
A changed membership/identity produces an input-change error requiring new
research; do not stretch the rectangle or silently keep historical counts.

Unknown: verified player travel routes outside this component and the desired
owner-approved eventual scene extent. Context absence stays `MissingSource`, not
terrain zeroes or `Blocked` traversal. The selector is independent of camera,
zoom, viewport, actor draw state and file visibility.

### 2. Multi-sector data snapshot

Project policy outputs per source/record:

- Stable key `(input fingerprint, runtime map, source kind, sector ID if static,
  DAT ordinal or aggregate record offset/index, full object-ID fingerprint)`.
  A local `sourceIndex` alone cannot key two sectors or identify a changed file.
- Copied raw/effective numeric values, direct/inherited presence, object class,
  world tile coordinate and sector-local coordinate, field/schema issues, prototype
  reference/identity, current ART and relevant state/flags.
- Terrain tiles/facades, lights, roof array and terminal object count are separate
  layers. Report observed/parsed/classified counts before resource resolution.
- Editor correspondence is evidence attached to one aggregate object, not a second
  gameplay object. Preserve raw and narrow content fingerprints separately.

Source fact: static rebasing retains low six bits then adds the containing sector
origin. Mobiles retain the serialized world location. Local observation: the
aggregate's7399 records partition into88 core,10 context,7301 outside,0 unknown.
Unknown or negative/mobile-absent location never falls into the core by default.
Missing mandatory source, premature EOF, unsupported field schema or ambiguous
prototype cannot publish a full snapshot. A diagnostic partial report must retain
its exact failure offset, parsed-prefix count and unparsed suffix boundary.

Snapshot values are immutable copied metadata. Archive owners serialize reads
against seek-based streams and dispose handles only after all selected reads;
consumers do not retain spans into mutable I/O buffers. An interrupted or failed
load leaves the prior published snapshot untouched. No data writer is proposed.

### 3. Identity-bound visual resolution and selected-frame access

Project policy input is an effective object plus explicit archive mapping profile,
not an arbitrary asset name or an actor substitute. Output preserves:

- Effective current ART and instance/prototype provenance; signed offsets, scale,
  rotation/state, render/spell flags and applicable overlay/underlay arrays.
- Mapping identity/ordinal, numeric category/number, resource archive/entry hash,
  physical group/frame/requested and effective palette, geometry and RGBA digest.
- Separate `ResourceReady`, `DiagnosticPlacementReady`, `RenderSemanticsReady`,
  `PassabilityReady`, `GameplayReady`, each with concrete reason/unknown state.

Local observation: scenery(class4) and container(class7) branches have bounded
chains. Core426 statics and2 mobile containers decode;86 mobiles require class6
item/state or class2/12/13 NPC appearance/direction/composition research. The cave
additionally exposes wall-class gaps and a class7 packed group/frame that is
outside the physical frame table. Do not modulo/clamp that selector or fabricate
a frame: retain `UnsupportedPhysicalSelection` with packed values and resource
identity. These are mandatory blockers for a future strict full-area visual API,
not blockers to reporting this research inventory honestly.

Source fact: missing numeric/array fields read actual prototype defaults;
explicit zero/null overrides differ from absence. Local observation: most core
statics use direct offsets but one pair is inherited;39 inherit current ART.
Prototype defaults must be authenticated and tested, not replaced by zero.
Unsupported ART branches return a specific class/state/schema reason. Missing or
ambiguous mappings return an explicit issue; no arbitrary fallback asset or
DAT/PATCH/loose precedence is introduced.

Selected-frame byte access remains Godot-free and lifetime-owned. Metadata/public
snapshots never serialize original pixels, text, resource filenames or local paths.
Local consumers may decode the user's external data under explicit caps; they must
not package it into a repository, exported app or public evidence.

### 4. Ordering, flags and placement

Source fact (F0): the original inserts flat objects before non-flat objects in a tile list,
handles scenery `UNDER_ALL` and wall/portal relations, then compares derived
ordering values. The object draw function (0x43B390) traverses visible sectors/tile lists, checks roof
coverage/type visibility/render flags and builds underlay/body/overlay paths.
The game pipeline draws tiles, objects and a distinct later roof stage. This is
not equivalent to sorting every sprite by diagnostic projectedY/sourceIndex.

Local observation, core statics:4 flat,343 no-block,0 don't-draw,0 invisible,
0 inventory,0 overlay/underlay flags. Combined core statics+mobiles include60 flat
and15 inventory flags, with no overlay/underlay flags. No absence in these inputs
removes the future API's need to preserve those arrays and branches. Scale100 is
observed for all core426 static objects; it is not a universal default.

Project policy: keep terrain, flat/non-flat objects, actor/scenery overlap,
overlay/underlay and roof stages separable with deterministic source-key tie
breaking for diagnostics only. A future renderer must independently ratify the
source-based ordering/visibility plan before claiming original scene behavior.
Original-renderer composition, overlap, scaled/mirrored rectangles and roof fading
are unknown. There is no area renderer or graphical gate in this research task.

The diagnostic projection from world tile deltas to the base-sector origin is
`anchorX = 40*(dy-dx)+offsetX`, `anchorY = 20*(dx+dy)+offsetY`, followed by hotspot
subtraction. This reuses existing [diagnostic placement](../research/scenery-placement.md),
not the original's blit rectangle. Sprite dimensions/rectangles are not collision
shapes. Passability requires its separate bounded traversal contract and coverage;
interactive readiness requires scripts/state, not a successful frame decode.

### 5. Budgets, cache and failure policy

Local observation sizing is finite and input-bound, not format limits:

| Read domain | Evidence-derived baseline |
| --- | --- |
| Exterior metadata survey | 621 sectors,24892578 decoded bytes in total, largest127177; process entries sequentially |
| Selected exterior core/context plus cave sector payloads | 21 sector payloads,410425 decoded bytes total |
| Exterior mobiles | game stream1823280 bytes,7399 records; editor correspondents1823032 bytes,largest1660 |
| Script direct search | 47 root IDs,48 variants,14487 stored /55344 decoded bytes |
| Script including immediate literal targets | 55 unique variant reads,68860 decoded bytes; no recursive VM |

Project policy: the existing1MiB per-entry/record cap remains unchanged; the
1823280-byte aggregate is a separate stream budget, not permission to widen DAT
or record decoding caps. Minimum instance envelope70 yields a byte-derived
maximum attempt count `(streamBytes-16)/70`. Each record must advance by its exact
validated length. Counts,widths,bitsets,presence,EOF and checked products validate
before allocating; no scan-for-next119 recovery or unlimited recursion.

Cache policy is optional and owned by the local consumer. **Zero persistent
payload cache** is valid: read/inspect/dispose one entry and one frame at a time.
Measured geometry for terrain in core/context/cave, resolved exterior object
frames and cave static frames gives327 distinct ART entries,3730743 encoded bytes,
27417852 RGBA bytes for all their physical frames,largest entry194480 and largest
single-frame RGBA542640. This is a conservative finite reference working set for
those branches only, not a total-game or complete-area-mobile cache estimate.
Additional cave mobile frames and currently unsupported mobile branches must be
charged separately; texture/GPU duplication, metadata and allocator overhead are
not included in the RGBA figure.

A bounded cache key must include archive fingerprint+entry identity+group/frame+
effective palette+normalization version, not ART ID alone. Charge checked encoded
and decoded bytes separately, share identical immutable frames across objects,
and enforce a caller-selected budget before admission. A consumer keeping all
reference frames needs at least the measured byte charge **plus** its own observed
overhead; a one-frame consumer needs the selected-frame charge, not327 full assets.
No arbitrary universal MiB ceiling is inferred from these observations. Return
`BudgetExceeded` or evict unleased frames deterministically; never evict an active
lease, drop required objects, delete shared caches or call memory failure a pass.
Unsupported mobile visual branches mean a complete eventual scene budget remains
unknown and must be measured when those branches are implemented.

Specific failure outcomes must distinguish `InputIdentityMismatch`,
`InputChangedDuringRead`, `MissingSource`, `AmbiguousSource`, `MalformedRecord`,
`UnsupportedVersionOrSchema`, `UnsupportedArtClassOrState`,
`UnsupportedPhysicalSelection`, `UnknownLocation`, `BudgetExceeded`, and
`Cancelled`. Unknown source semantics are not malformed bytes; structural failure
is not an empty layer. Detailed diagnostics stay private; public summaries retain
numeric identities, counts, fingerprints and reasons only.

### 6. Connector and cave boundary

Source fact/local observation establishes potential directed exterior→catalogue5078
and cave→catalogue5000 literal endpoints as recorded in the inventory. Effective
SAP attachments plus both branch actions and one-hop literal calls were checked.
Conditions, object-focus calls, script changes, scheduled activation and full
reachability are unknown; no transition API/VM is implemented. Cave39 editor
mobiles are a labelled source observation, not verified normal-game aggregate
provenance. Before executing a future connector, ratify catalogue/runtime ID
conversion, source policy, activation, exact arrival tile and destination-source
identity. Empty jump lists cannot erase script-driven endpoints.
[Cave transition research](../research/cave-transition-evidence.md) (ARC-WORLD-009)
now answers activation (use of the trigger scenery, unconditional), focus
(the triggerer), ID conversion (catalogue − 4999), both arrival tiles and the
cave's normal-game aggregate identity. The connector is still not executed.

## Dependency map and next acceptance boundaries

Project policy, not authorization to create work:

1. Owner-approved inventory/extent → multi-sector selection, immutable coordinates,
   source reconciliation and resource-provider work. Preserve4096/317 base regression.
2. That data snapshot + class6/2/12/13 and wall/state research → complete mandatory
   area visual readiness. Existing five-only production profiles remain explicit.
3. Identity-bound frames/fields + ratified flags/order/overlap plan + measured budget
   → eventual full exterior renderer and graphical verification. Do not use a
   human/chicken diagnostic profile in place of actual NPC resources.
4. Separate traversal and interaction contracts → passability/gameplay. Neither
   follows from ART rectangles. Cave transition depends on authenticated cave
   normal-game data and verified SAP activation, not literal-operand presence alone.

Implementation tests should independently encode neighboring-sector keys and
half-open boundaries; direct/inherited zero/null defaults; malformed masks/counts/
bitsets and EOF; full-ID bijection without shared normalizer constants; missing/
ambiguous mappings; invalid packed physical-frame selectors; snapshot cancellation/
lifetime determinism; cache key/lease/budget behavior. Opt-in originals must repeat
input identities before/after and reconcile every source/type/readiness partition.
Research synthetic tests and private independently computed selector/wire/ART
oracles are described in the inventory. Native exact-head independent review and
owner approval remain separate from local probe results. Publication is a private
PR after review; merging remains a human action.
