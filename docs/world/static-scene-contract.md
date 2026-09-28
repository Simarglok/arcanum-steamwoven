# Static-scene contract: bounded Crash Site candidate

## Readiness verdicts

The selected map and one sector are reproducible. Corrected framing establishes
all base-stream sections through object window `17140..47287`, whose bounded
v119 delimiter reaches terminal count 317; the word `1` at offset `16388` is
the roof-empty discriminator, not a retail object version.

- **Fixed-source terrain-only loader contract: READY.** The narrow contract
  accepts the caller-supplied immutable module and explicit one-source resource
  archive, resolves the tile/facade-only prefix for all 4,096 records, and
  exposes selected-frame plus diagnostic placement evidence. The framed object
  suffix is opaque; this verdict neither adds object semantics nor infers
  general archive, PATCH, or loose-file precedence.
- **Terrain scene: NOT READY.** No production/Godot scene or renderer
  integration has been implemented. Lighting/palette treatment,
  camera/dirty-rectangle policy, normal-runtime facade behavior, and a measured
  presentation comparison remain unestablished.
- **Static-object diagnostic placement: READY, static scene: NOT READY.**
  ARC-RES-007 establishes the selected v119 object census, type-3/static
  predicate, current-ART/prototype resolution, and five bounded ART metadata
  examples in `docs/research/crash-site-objects.md` (with the future fail-closed
  model in `static-object-loader-contract.md`). ARC-RES-008 additionally
  establishes a finite direct-instance-offset plus frame-hotspot diagnostic
  rectangle for those five examples in `docs/research/scenery-placement.md` and
  `docs/rendering/static-scenery-contract.md`. It does not establish original
  baseline parity, occlusion, normal-runtime ordering, or a static scene.

This is a bounded research contract, not a production loader, renderer, Godot
scene, or gameplay model. A future broader loader or scene must fail closed
rather than inventing fallbacks.

## Inputs already bounded

| Input | Required selector | Result |
| --- | --- | --- |
| Module archive | caller-supplied read-only file matching the recorded size/SHA-256 | supports one exact sector entry |
| Start selector | sole module `START_MAP` record | `Arcanum1-024-fixed`, `(92958, 82592)` |
| Sector | packed ID `86570436012` under the CE-derived shift/packing rule | ordinal 463, compressed, 47,291 decoded bytes |
| Tile prefix | 4,096 words after bounded light section | tile/facade-only resolver and one-source ART/frame evidence for all 4,096 selected frames |
| Intermediate sections | roof then placeholder-controlled script, metadata/sound, and block sections | selected roof `empty=1`, placeholder `0xAA0004`; checked framing only |
| Object window / suffix | `17140..47287`, terminal count `317` | bounded v119 delimiter reaches the terminal count; ARC-RES-007 establishes a selected type-3/static predicate and current-ART/prototype chain, while ARC-RES-008 limits diagnostic placement to five direct-offset/hotspot chains; neither establishes original scene order |

No archive-overrides policy is implied. The caller must supply an explicitly
chosen source set; this research does not select between module, PATCH, or
loose files.

## Exact evidence still required before a renderable record

For this sector with zero light records, the fixed tile table ends at byte
16,388, but that following word is the roof `empty` discriminator. The enabled
intermediate sections end at `17140`; the terminal count starts at `47287`,
leaving a 30,147-byte framed window for 317 serialized objects. Pinned CE's
object-list reader uses the terminal count to walk that window as versioned
records. This establishes a **minimum boundary** only, not permission for a
general object parser:

1. retain the already proven framing and terminal-count consistency;
2. reuse the separately proven selected-v119 object/current-ART chain only
   within its explicit input contract;
3. validate every admitted reference with that chain's fail-closed rules; and
4. keep that result separate from a baseline or ordering claim.

The source paths establish why no constant byte offset is safe; the measured
window/count establish the bounded impact on this sector. They do not prove an
ART path, frame, placement baseline, or visual result. A future loader must
reject the selected subset if it cannot satisfy this envelope, rather than
choosing plausible offsets.

Terrain has a bounded but incomplete result. The real table contains the two
CE-attributed tile/facade classes; their type-specific decomposition, MES
callback lookup, exact one-source ART candidate, and selected physical frame
are documented for representative records. The future loader must still reject
all unsupported classes, missing/duplicate candidates, and invalid frame
selectors. It cannot infer archive precedence or claim this one-source result
is a complete terrain scene.

## Candidate data model

A later Godot-independent loader may expose data no richer than the evidence
has earned:

```text
FrozenSlice
  mapName, sectorX, sectorY, sectorId, localOrigin, width=64, height=64
  tileWords[4096]                 // raw evidence retained separately from resolution
  trailingObjectCount             // count only, no object semantics

TerrainResolution
  localX, localY, rawId, kind, lookupStatus
  sourceIdentity, artEntry, group, frame, palette, frameMetadata
  projectedAnchor, diagnosticPlacement // CE-derived footprint/crop/flip only

ProjectedPoint
  localX, localY, relativeScreenX, relativeScreenY
```

It must reject a source whose fingerprint, exact selected entry, compressed
flag, decoded byte bound, light count, fixed tile region, intermediate-section
framing, object-window boundary, or terminal count does not meet the recorded
contract. It must not reinterpret unresolved words as resource paths or
`trailingObjectCount` as a number of scenery objects.

## Coordinates and projection

The pinned CE `location_xy` is a source-derived isometric relationship:

```text
screenX = originX + 40 * (worldY - worldX - 1)
screenY = originY + 20 * (worldY + worldX)
```

For sector-relative coordinates, the constant and caller-controlled origin
cancel when comparing positions. The candidate projection is therefore:

```text
relativeX = 40 * (localY - localX)
relativeY = 20 * (localY + localX)
```

The probe's synthetic checks cover these numerical examples:

| Local position | Relative point | Evidence |
| --- | --- | --- |
| `(0, 0)` | `(0, 0)` | CE-derived formula; synthetic checked |
| `(1, 0)` | `(-40, 20)` | CE-derived formula; synthetic checked |
| `(0, 1)` | `(40, 20)` | CE-derived formula; synthetic checked |
| start offset `(30, 32)` | `(80, 1240)` | CE-derived formula; synthetic checked |

A static scene must choose `originX`, `originY`, camera anchoring, rounding,
and asset baseline policy explicitly. ARC-RES-008 has a separate five-record
diagnostic where a direct instance offset plus ART hotspot selects a rectangle;
it is not evidence for unadmitted records, a viewer canvas offset, or original
static-scene parity.

## Resource and placement gates

Before emitting a terrain diagnostic record, a future task must validate this
complete chain for each included record; it must retain an explicit error record
for every failed lookup. The one-source experiment proves the marked
tile/facade steps only for its representatives, not the policy or completeness
of a general source set. Scenery begins at the separately bounded object envelope;
ARC-RES-008 admits only five direct-offset/hotspot diagnostic placements. A
scene still needs original ordering/occlusion evidence:

```text
selected sector word/object record
  -> supported tile/facade resolver OR validated object/prototype rule
  -> exact archive/loose winner under an explicit policy
  -> ART entry
  -> ArtDecoder.Inspect metadata
  -> type-specific selected frame/palette
  -> bounded diagnostic terrain placement OR five-record static-scenery diagnostic placement
  -> independently validated scene baseline/order for any scene claim
```

`DatArchive` and `ArtDecoder` are mandatory reuse points. No alternative DAT
or ART parser is authorized by this contract. The known ART decoder's validated
limits and palette/frame metadata are useful only after exact lookup succeeds;
an unsupported type, duplicate/missing candidate, or failed frame bound
produces no substitute image.

## Ordering proposal, deliberately blocked

The following should become an implementation requirement only after evidence
for ART baselines and object overlap exists:

1. render terrain at the relative coordinate;
2. order scenery using a documented baseline-derived sort key;
3. define deterministic ties by a stable source index; and
4. define walls/roofs only if they occur in the chosen slice and their resource
   and ordering rules are separately measured.

There is no accepted overlap/order example from the original slice: original
visual baseline and original overlap/order reference remain unproven.
ARC-RES-007 has safely delimited and classified the 317 type-3 records, and
ARC-RES-008 has five direct-offset/hotspot diagnostic placements plus a
synthetic stable-tie policy. Neither establishes their original visual overlap.
A future decoder must retain the validated record sequence as a deterministic
tie key only when its policy says so; that is a reproducibility requirement,
not an evidence-backed occlusion rule. Any ordering example before a selected,
validated pair is synthetic and must not be reported as original scene parity.

## Follow-on outlines (not Kanban authorization)

### Bounded C# loader research

Inputs: the frozen module input, an explicitly ordered immutable source set,
and the selected sector/framing contract. It must validate the fingerprint,
light/table/intermediate-section/object-window bounds, use x-major local
ordinals, accept only tile/facade classes, apply the documented lookup/frame
rules, and return either `TerrainResolution` or a specific failure. Outputs:
`FrozenSlice`, diagnostics for resolved and failed terrain words, and projected
anchors only—no renderer baseline, static scenery, or fallback image. Acceptance:
synthetic truncation/count/lookup/frame failures, coordinate examples, selected
read-only real-data chains, source fingerprints before/after, and no game
payload committed.

### Static Godot scene research

Inputs: a reviewed loader output with exact ART metadata and approved palette
handling. Outputs: a non-playable diagnostic scene with explicit anchor,
baseline, and deterministic order. Acceptance: synthetic placement examples
and a measured comparison target; no gameplay, lighting simulation, or
unverified scene-parity claim.

## Source lineage

The coordinate relationship and sector boundary are derived from pinned
`arcanum-ce` `a7ff41b300ef712f0e7d088183a3d08957110cdd`, notably
`src/game/location.c`, `src/game/sector.c`, and its sector list modules. CE is
one lineage rather than independent confirmation. This contract is independently
written and does not copy CE code or grant a license conclusion.
