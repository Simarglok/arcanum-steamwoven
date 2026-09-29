# Static-scenery diagnostic contract (ARC-RES-008)

## Scope and status

This contract extends the finite static-object diagnostic established in
[crash-site-objects.md](../research/crash-site-objects.md). It permits a future,
Godot-independent **diagnostic** to place only its five named source-index
records using [scenery-placement.md](../research/scenery-placement.md).

It is not a production static-scene renderer, object loader, scene integration,
archive-precedence policy, normal-runtime observation, or parity claim. It
must not be used to treat the terrain viewer's Editor diagnostic facade order
as a scenery rule.

| Capability | Status | Contractual boundary |
| --- | --- | --- |
| Five finite object chains | READY | exact source-index, local coordinate, selected ART metadata, and direct instance offset must all match the recorded finite matrix |
| Diagnostic position and rectangle | READY | source-derived relative anchor plus hotspot-derived top-left; no visual nudge |
| ART frame offset retention | READY | retain as metadata, but do not translate this diagnostic rectangle with it |
| Synthetic transparent / flip pixels | READY as project diagnostic policy | public in-memory fixture only |
| Deterministic equal-key diagnostics | READY as project diagnostic policy | sort `(anchor.y, anchor.x, sourceIndex)` ascending; later opaque command wins |
| Original scene object ordering/overlap | BLOCKED | no selected original visual/reference pair establishes an original sort or occlusion key |
| Normal-runtime facades, walls, roofs, shadows, lighting and palette effects | BLOCKED | not measured and not implied by CE-source inspection or the terrain viewer |

A future caller must reject a record outside this exact evidence envelope. It
must not substitute prototype offsets, infer a missing field as zero, choose an
alternate ART/resource winner, or convert an unknown result into a plausible
image.

ARC-RENDER-004 consumes this envelope in the terrain viewer's explicit
`combined` diagnostic mode. The Data seam carries a path-free resource identity
(DAT ordinal, flags, declared decoded size and normalized logical-name hash) to
identity-bind selected-frame decoding. It is structural only:
`ValidateOriginalData` remains the sole authenticating operation. Selected
resource inspection reports five `Resolved` records and marks every other
record `OutsideResourceScope`; census-only inspection retains `NotEvaluated`.

## Required finite input record

For each admitted result, retain the following evidence—not a broader inferred
object model:

```text
StaticSceneryDiagnosticInput
  sourceIndex                       // validated serialized sequence identity
  localX, localY                    // selected sector-relative coordinates
  directInstanceOffsetX, directInstanceOffsetY
  currentAid and its recorded resolution provenance
  selected ART identity / group / frame / palette
  frame width, height, hotX, hotY, offsetX, offsetY
```

`directInstanceOffsetX/Y` are serialized instance values. This research does
not establish that absent offsets have a prototype default, nor that any other
object field is a placement offset. A caller cannot admit an object when either
direct offset is absent.

The resource input remains caller-selected and read-only. The narrow evidence
requires the recorded module, resource, and prototype aggregate identities in
`scenery-placement.md`; it makes no decision between module data, PATCH/dif,
or loose files. Do not persist original payloads, paths, extracted frames,
original screenshots, or decoded original pixels.

## Diagnostic coordinate and rectangle rule

The CE-source-derived relative projection is deliberately separated from a
future camera origin. For local coordinates `(x,y)`, direct instance offset
`(ox,oy)`, and selected frame hotspot `(hx,hy)`:

```text
projected = (40 * (y - x), 20 * (y + x))
anchor    = projected + (ox, oy)
topLeft   = anchor - (hx, hy)
rect      = (topLeft.x, topLeft.y, frameWidth, frameHeight)
```

Use signed checked integer arithmetic. The `location_xy` source expression has
an origin and a one-cell X term; in the selected object blit path, the
object-specific `+40,+20` compensates it. The displayed rule is the resulting
relative relationship only. Camera origin, zoom, rounding, clipping,
dirty-rectangle maintenance, and canvas baseline are deliberately caller
policy.

The ART frame's `(offsetX,offsetY)` is not the object instance `(ox,oy)`. For
this finite data set ART offsets measured `(0,0)`, but the diagnostic's public
fixture deliberately uses nonzero `(9,-4)` to prevent an accidental conflation.
The fixture proves only that this project's diagnostic rectangle subtracts the
hotspot without a second frame-offset translation; it does not establish
retail-game behavior for every ART use path.

## Raster policy permitted for the diagnostic

For a selected decoded frame, the diagnostic may draw ordinary straight RGBA
pixels into a caller-owned canvas with these explicit project policies:

1. palette index zero / alpha zero leaves the existing destination unchanged;
2. no crop has been established for these five records; draw the selected full
   frame only if it passes its documented dimensions and bounds;
3. an explicitly requested horizontal flip samples source X as
   `width - destinationX - 1`; it does not move the anchor or the destination
   rectangle;
4. no Y flip, rotation, scale, lighting, palette grading, shadow, overlay,
   underlay, roof, wall, or facade behavior is authorized;
5. an application-specific clipping rectangle may discard pixels, but must not
   change the recorded anchor/rectangle.

No admitted original record is claimed to request a flip. Flip is available
only so the public asymmetric fixture exercises a non-ambiguous future
renderer primitive.

## Ordering and overlap policy

Original static-object ordering is blocked. The CE source has multiple queue
bands and sorting inputs (including flat/non-flat paths and overlay/underlay
behavior), while this finite record does not admit all relevant inputs or an
original visual reference. Thus the following is a reproducibility policy,
not an original-game rule:

```text
sort key = (anchor.y ascending, anchor.x ascending, sourceIndex ascending)
paint    = later opaque source pixels overwrite earlier opaque source pixels
```

The `sourceIndex` is a tie-breaker only. It is not a z-order discovery, a
claim about serialized file order, or a substitute for an original occlusion
measurement. Implementations must label any output using this policy
"diagnostic" and report normal-runtime ordering as unverified.

## Acceptance gates for a future consumer

A future consumer can call itself compliant only if it:

1. verifies the selected immutable source identities before and after any
   original read and preserves no protected material;
2. validates all five source-to-ART chains and rejects any identity, selector,
   metadata, direct-offset, or frame mismatch;
3. uses the coordinate/rectangle rule above without per-object corrections;
4. keeps frame ART offsets distinct from instance offsets;
5. records that transparency, flip, and ordering are either the stated
   diagnostic policies or unknown—never retail-game recovery;
6. produces independent synthetic expectations for anchor, hotspot/offset,
   transparency, flip, overlap, and exact-key tie cases; and
7. makes no claim about normal-runtime facades, walls, roofs, occlusion,
   lighting, palette behavior, or original visual parity.

The current public reproduction is
`tools/research/Arcanum.StaticSceneryProbe --self-test`, which uses only
in-memory synthetic ART and an in-memory canvas. It supports the policy gates
but does not exercise original data. The finite original-coordinate evidence,
identities, measurement boundaries, and command sequence are retained in
`scenery-placement.md`.

## Provenance and limitations

The coordinate, object-anchor, frame-hotspot, and flip observations are
attributed to pinned `alexbatalov/arcanum-ce`
`a7ff41b300ef712f0e7d088183a3d08957110cdd`, primarily its location, object,
sector-object-list, and ART source files. That is a single source lineage and
is not independent corroboration. The implementation and documentation here
are independently written; no CE code is copied, translated, or represented as
a clean-room result. Applicable upstream licensing remains with that upstream
project.
