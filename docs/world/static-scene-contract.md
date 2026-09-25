# Static-scene contract: bounded Crash Site candidate

## Readiness verdict

**Not ready for implementation.** The selected map and one sector are
reproducible, and the coordinate/sector arithmetic plus a fixed tile-table
boundary are supported. Resource resolution for an actual terrain visual,
static object decoding, sprite frame selection, baseline/offset semantics, and
static draw ordering are not supported by evidence yet. A future loader or
Godot scene must fail closed for those missing links rather than inventing
fallbacks.

This is a contract proposal for a later, separate task. It is not a production
loader, renderer, Godot scene, or gameplay model.

## Inputs already bounded

| Input | Required selector | Result |
| --- | --- | --- |
| Module archive | caller-supplied read-only file matching the recorded size/SHA-256 | supports one exact sector entry |
| Start selector | sole module `START_MAP` record | `Arcanum1-024-fixed`, `(92958, 82592)` |
| Sector | packed ID `86570436012` under the CE-derived shift/packing rule | ordinal 463, compressed, 47,291 decoded bytes |
| Tile prefix | 4,096 words after bounded light section | aggregate only; no pathname claim |
| Object suffix | terminal count `317` | record boundaries/types unknown |

No archive-overrides policy is implied. The caller must supply an explicitly
chosen source set; this research does not select between module, PATCH, or
loose files.

## Candidate data model

A later Godot-independent loader may expose data no richer than the evidence
has earned:

```text
FrozenSlice
  mapName, sectorX, sectorY, sectorId, localOrigin, width=64, height=64
  tileWords[4096]                 // opaque until a bounded resolver is proven
  trailingObjectCount             // count only, no records

ProjectedPoint
  localX, localY, relativeScreenX, relativeScreenY
```

It must reject a source whose fingerprint, exact selected entry, compressed
flag, decoded byte bound, light count, fixed tile region, or terminal count
does not meet the recorded contract. It must not reinterpret opaque tile words
as resource paths or `trailingObjectCount` as a number of scenery objects.

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
and asset baseline policy explicitly. The formula is not evidence that raw ART
hotspots or a viewer canvas offset are world-placement offsets.

## Resource and placement gates

Before emitting a renderable terrain or scenery record, a future task must
prove this complete chain for each included class:

```text
selected sector word/object record
  -> bounded lookup or prototype rule
  -> exact archive/loose winner under an explicit policy
  -> ART entry
  -> ArtDecoder.Inspect metadata
  -> explicitly selected frame/palette and baseline transform
```

`DatArchive` and `ArtDecoder` are mandatory reuse points. No alternative DAT
or ART parser is authorized by this contract. The known ART decoder's validated
limits and palette/frame metadata are useful only after an exact resource path
is established.

## Ordering proposal, deliberately blocked

The following should become an implementation requirement only after evidence
for object fields and ART baselines exists:

1. render terrain at the relative coordinate;
2. order scenery using a documented baseline-derived sort key;
3. define deterministic ties by a stable source index; and
4. define walls/roofs only if they occur in the chosen slice and their resource
   and ordering rules are separately measured.

There is no accepted overlap example from the original slice. Any ordering
example before that point is synthetic and must not be reported as original
scene parity.

## Follow-on outlines (not Kanban authorization)

### Bounded C# loader research

Inputs: the frozen module input and an independently specified resource source
set. Outputs: rejected-or-opaque `FrozenSlice`, plus a small set of fully
proven terrain/scenery references. Acceptance: synthetic truncation/count/
missing-reference tests, selected real-data chain to ART, before/after input
fingerprint, and no game payload committed.

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
