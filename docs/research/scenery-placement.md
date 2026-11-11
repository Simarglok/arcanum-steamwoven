# Crash Site scenery placement and overlap (ARC-RES-008)

## Scope and verdict

This is a bounded, read-only study of the five source-indexed static-scenery
examples admitted by [crash-site-objects.md](crash-site-objects.md). It adds no
production renderer, object loader, Godot scene, asset extraction, source
precedence rule, or original-runtime observation.

**Verdict:** the five examples are **READY for a numeric diagnostic
anchor/rectangle record**: source-local coordinates, direct instance offsets,
and selected ART frame hotspots produce deterministic positions without a
per-object visual nudge. A renderer can retain the ART offset metadata but must
not apply it to this placement rule. Diagnostic synthetic ordering is ready;
original normal-runtime object ordering, roof/wall occlusion, lighting, and
normal-runtime facade behavior remain **BLOCKED**.

## Inputs, provenance, and privacy boundary

The authorized original inputs were opened read-only. The bounded Crash Site
probe compared their full fingerprints before and after the run; all matched.
No original path, payload, text, image, decoded pixel, or binary artifact is
retained in Git.

| Input | Identity used by the read-only run | Result / limit |
| --- | --- | --- |
| Module DAT | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` | The frozen sector and all five records; no `.dif`, PATCH, or loose-file policy |
| Explicit resource DAT | 147,991,122 bytes; SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` | Exactly the five selected ART metadata chains; no archive winner claim |
| Referenced prototype directory | Six-file aggregate SHA-256 `00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6` | Used only for the already-reviewed current-ART/object-flag resolution edges |
| Earlier source reading | An earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited | No independent corroboration claim |

The direct-offset observation was added to the bounded research probe rather
than a production parser. Its synthetic coverage retains an independently
asserted signed direct-offset case. The original-data run again reconciled 317
v119 type-3 records, 288 direct/29 inherited current-ART values, 31 direct/286
inherited object flags, and 317 records passing the narrow static predicate.

## Evidence classes and coordinate chain

| Claim | Source / measurement | Status | Boundary |
| --- | --- | --- | --- |
| Isometric cell projection | `location_xy` (0x4B8680) | F0 reading | Camera origin is caller state, not a portable world origin |
| Sector objects are rebased after read | `objlist_load` (0x4F1350); selected local coordinates measured by the probe | F0 reading plus measured | No `.dif` behavior or runtime postprocessing claim |
| Object draw base includes object `offset_x/y` and `+40,+20` after `location_xy` | `object_draw` (0x43B390), `object_get_rect` (0x43DFD0); direct serialized slots 2/3 measured for the five records | F0 reading plus measured | Prototype-default behavior for offsets is not admitted |
| Main object rectangle subtracts frame hotspot | `object_get_rect` (0x43DFD0); selected ART metadata measured | F0 reading plus measured | Does not establish a scene camera or dirty-rectangle policy |
| Main object rectangle does not read frame `offset_x/y` | `object_get_rect` (0x43DFD0); selected raw ART offsets measured as `(0,0)` | F0 reading plus measured | This is distinct from special hover/overlay paths, which are not admitted |
| Flip changes source sampling, not the diagnostic destination rectangle | The ART library's blitter; synthetic asymmetric fixture | F0 reading plus synthetic policy check | No selected real object is claimed to enable a flip |
| Pixel index zero is transparent in the project ART decode contract | `ArtDecoder` and synthetic fixture | project decoder policy plus synthetic check | Not a measured original alpha/blending result |

For a selected record's local coordinate `(x,y)`, direct instance offset
`(ox,oy)`, and selected frame hotspot `(hx,hy)`, the source-derived relative
base cancels the mutable camera origin and the `location_xy` `-1` X term against
the object's subsequent `+40`:

```text
projected = (40 * (y - x), 20 * (y + x))
anchor    = projected + (ox, oy)
topLeft   = anchor - (hx, hy)
rect      = (topLeft.x, topLeft.y, frameWidth, frameHeight)
```

All arithmetic in the diagnostic probe is signed checked integer arithmetic.
The ART frame's `(offsetX,offsetY)` is retained alongside the result but is not
added to `anchor` or `topLeft`; it is a different metadata pair from the object
instance offsets. There is no manual correction term.

## Finite original-data anchor results

`direct offset` means serialized common slots 2 and 3 were present on the
instance. The selected frame's raw ART offsets are shown separately. Coordinates
and numeric IDs are the existing bounded handoff identifiers, not names or
payloads.

| Source index | Local | Direct offset | ART / frame metadata | Raw ART offset | Anchor | Diagnostic top-left / size |
| ---: | --- | --- | --- | --- | --- | --- |
| 0 | `(50,0)` | `(2,-15)` | `0x40280140`; `62x57`, hot `(34,39)` | `(0,0)` | `(-1998,985)` | `(-2032,946) / 62x57` |
| 1 | `(5,1)` | `(-3,14)` | `0x40700080`; `204x267`, hot `(113,262)` | `(0,0)` | `(-163,134)` | `(-276,-128) / 204x267` |
| 2 | `(6,1)` | `(-21,8)` | `0x40280140`; `62x57`, hot `(34,39)` | `(0,0)` | `(-221,148)` | `(-255,109) / 62x57` |
| 3 | `(6,1)` | `(19,10)` | `0x40280140`; `62x57`, hot `(34,39)` | `(0,0)` | `(-181,150)` | `(-215,111) / 62x57` |
| 4 | `(44,1)` | `(-4,-10)` | `0x40700080`; `204x267`, hot `(113,262)` | `(0,0)` | `(-1724,890)` | `(-1837,628) / 204x267` |

The source-index 2/3 pair is intentionally instructive: identical local
coordinates and ART metadata do not license a shared visual position; their
measured direct instance offsets produce different anchors. Conversely, no
inference is made for a record whose offset is absent or prototype-derived.

## Synthetic overlap comparison

`tools/research/Arcanum.StaticSceneryProbe` creates public in-memory 8-bpp ART
fixtures, decodes them through `Arcanum.Data.ArtDecoder`, and draws only into an
in-memory synthetic canvas. It neither opens original data nor writes an image.
The expected landmarks are hard-coded independently of the placement/raster
helpers.

| Case | Measured against | Result | Claim it supports |
| --- | --- | --- | --- |
| Anchor | authored local `(6,1)`, direct offset `(-21,8)` | `(-221,148)` | checked coordinate projection plus instance offset |
| Hotspot vs ART offset | 4x3 frame, hot `(1,2)`, ART offset `(9,-4)`, anchor `(100,50)` | top-left `(99,48)` | hotspot moves the rect; ART offset remains metadata |
| Transparency | asymmetric fixture with index-zero cells | background remains unchanged at two named transparent destinations | project diagnostic alpha policy |
| Horizontal flip | same fixture with an authored asymmetric three-pixel landmark | landmarks move from `(99,48)/(100,49)/(101,50)` to `(102,48)/(101,49)/(100,50)` while top-left remains `(99,48)` | optional diagnostic flip behavior only |
| Overlap and exact baseline tie | two opaque 3x3 fixtures at anchor `(20,20)`, source indices 7 and 9 | later source index 9 wins at `(21,20)` | explicit project tie policy, not original occlusion |

The diagnostic renderer policy sorts by `(anchor.y, anchor.x, sourceIndex)` in
ascending order and paints later commands over earlier opaque pixels. It is a
reproducible policy for future diagnostic work, not a recovered original sort
key. `sourceIndex` is the already-validated serialized record sequence and is
used only to make equal computed keys deterministic.

## Ordering and occlusion limits

The object pass of the earlier source reading visits sector/cell lists and assigns separate flat,
non-flat, shadow, overlay, and underlay queue bands before sorting queued blits
(`object_draw` 0x43B390, `object_enqueue_blit` 0x443440, `object_compare_blits`
0x443600; F0). That evidence
establishes neither a simple source-index order nor a static-scenery-only
occlusion rule for the selected retail records. The current probe does not
admit object `OF_FLAT`, render flags, underlays/overlays, scale, visibility,
wall flags, roof state, or postprocess state as placement/order inputs.

The terrain viewer's **Editor diagnostic order** (tiles then facades) is not a
scenery runtime rule. In particular, `gamelib_draw_game` versus editor facade
behavior remains outside this result. No normal-runtime facade ordering, wall
or roof occlusion, original object overlap reference, palette/lighting
behavior, transparency blend mode, or original rendered comparison was
observed. A local diagnostic render and an F0 source reading are not
original-game runtime observation.

## Diagnostic consumer rules

Carried from the retired static-scenery diagnostic contract. A diagnostic
consumer places only the five named source-index records, may not treat the
terrain viewer's Editor diagnostic facade order as a scenery rule, and is not a
production renderer, archive-precedence policy or parity claim.

Admission: a record is admitted only inside the exact finite evidence envelope
(source index, local coordinate, selected ART identity/group/frame/palette,
frame width/height/hot/offset, current-ART provenance and **both** direct
instance offsets). It must not substitute prototype offsets, infer a missing
offset as zero, pick another ART/resource winner or turn an unknown result into
a plausible image. Absent offsets have no established prototype default. The
resource input stays caller-selected and read-only; no original payload, path,
frame or screenshot is persisted. The terrain viewer's `combined` mode
(ARC-RENDER-004) consumes this envelope through a path-free resource identity
(DAT ordinal, flags, declared decoded size, normalized-name hash); selected
inspection reports five `Resolved` records and every other record
`OutsideResourceScope`.

Rectangle: with signed checked integer arithmetic, `anchor = (40*(y-x), 20*(y+x))
+ (ox,oy)` and `topLeft = anchor - (hx,hy)`. Camera origin, zoom, rounding,
clipping and canvas baseline are caller policy. The frame's `(offsetX,offsetY)`
is not the instance `(ox,oy)`; the fixture uses a nonzero `(9,-4)` to keep them
apart.

Raster policy (project policy, not recovered behavior): palette index zero /
alpha zero leaves the destination unchanged; no crop is established, so draw the
selected full frame inside its documented bounds; an explicit horizontal flip
samples source X as `width - destinationX - 1` without moving the anchor or
rectangle (no admitted record requests one); no Y flip, rotation, scale,
lighting, grading, shadow, overlay, underlay, roof, wall or facade behavior is
authorized; an application clip may discard pixels but not change the recorded
anchor/rectangle.

Ordering: sort key `(anchor.y, anchor.x, sourceIndex)` ascending, later opaque
pixels overwrite earlier ones. This is a reproducibility policy, **not an
original rule**; `sourceIndex` is a tie-breaker only (not a z-order discovery or
serialized-order claim). Output using it is labelled "diagnostic" and
normal-runtime ordering is reported unverified.

Gates for a compliant consumer: verify immutable source identities before and
after any original read; validate all five source-to-ART chains and reject any
identity/selector/metadata/offset/frame mismatch; apply the rule without
per-object corrections; keep ART offsets distinct from instance offsets; label
transparency, flip and ordering as the stated policies or unknown; supply
independent synthetic expectations for anchor, hotspot/offset, transparency,
flip, overlap and exact-key ties; claim nothing about normal-runtime facades,
walls, roofs, occlusion, lighting, palette or visual parity.

## Reproduction

Use the project-pinned SDK where available. The current local focused evidence
used installed SDK 8.0.131 from a scratch working directory because pinned SDK
8.0.425 is unavailable; no repository or global SDK configuration was changed.

```text
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- \
  --module <bounded-module> --prototype-root <bounded-prototype-directory> --resource <explicit-resource>

dotnet restore tools/research/Arcanum.StaticSceneryProbe/Arcanum.StaticSceneryProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.StaticSceneryProbe/Arcanum.StaticSceneryProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.StaticSceneryProbe/Arcanum.StaticSceneryProbe.csproj --no-build -- --self-test
```

The first original-data command requires the recorded identities and performs
before/after fingerprints; it must not be pointed at a substitute input. The
second command is entirely synthetic.

## Future renderer readiness

| Requirement | Verdict | Reason |
| --- | --- | --- |
| Five-record diagnostic anchors/rectangles | READY | finite coordinates, direct instance offsets, selected ART hotspot/dimensions, and checked formula |
| Preserve ART offsets as metadata | READY | measured and deliberately not conflated with placement |
| Diagnostic transparent/flip draw | READY as project policy | synthetic decoded fixtures and independent landmark expectations |
| Deterministic static-scene ties | READY as project policy | `(anchor.y, anchor.x, sourceIndex)` is explicit and tested |
| Original static object order or occlusion | BLOCKED | no original overlap/order reference and complex source queue inputs remain unadmitted |
| Normal-runtime facades, walls, or roofs | BLOCKED | no selected normal-runtime evidence; editor facade order is explicitly insufficient |
| Original visual parity, lighting, palette effects | BLOCKED | no runtime capture or measured original rendering comparison |
