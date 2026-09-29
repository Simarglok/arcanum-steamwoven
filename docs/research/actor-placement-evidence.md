# Actor world-anchor placement evidence

## Result and readiness

**Research result: source-derived placement relation identified; original-runtime
placement parity remains NOT READY.** This record closes the previously unknown
relationship for the bounded direct, single-layer monster Idle/Walk chain only
at the exact pinned `arcanum-ce` source level. It does not claim that the
original executable was observed, that the CE implementation is an independent
oracle, or that a production actor renderer is authorized.

The candidate remains the chain recorded by
[actor visual evidence](actor-visual-evidence.md): module ordinal 643, direct
current ART `0xc9001810`, Idle/Walk resource ordinals 4753/4754, and the eight
requested-facing to physical-group/flip bindings. Frame dimensions, hotspots,
and ART offsets remain frame-specific; Idle has 15 frames per group and Walk
has 8. No locomotion distance or movement speed is inferred from ART FPS.

## Evidence classes and provenance

| ID | Claim | Classification | Evidence and limit |
| --- | --- | --- | --- |
| AP-001 | The archived ArtConverter README identifies a converter only; it makes no actor-anchor, top-left, hotspot, or offset placement claim. | community description, silent | Local archived README, already catalogued in [ART evidence](art-evidence.md#source-matrix-and-lineage). |
| AP-002 | ArcNET describes `arcanum-ce` as a primary source for its object/art format work; it is therefore CE-derived and not independent placement corroboration. | community description, derivative | Local archived ArcNET README credits section; no ArcNET code was used or run. |
| AP-003 | The preserved WorldEd/ArtView references document tooling availability or unrelated editor/dialogue material, not an actor top-left observation. | community description, silent | Existing ART/dialogue provenance records; no archived executable was run. |
| AP-004 | The exact game draw route calls the generic object draw pass, which visits visible object nodes including NPCs. | pinned CE-derived rule | `src/game/gamelib.c:1703-1715`; `src/game/object.c:495-576`. The selected record is a type-16 NPC, so this is an actor-specific use of the generic object route, not scenery transfer. |
| AP-005 | The source location projector, instance offsets, frame hotspot, scaling/shrunk state, and current ART select the object rectangle before its queued blit. | pinned CE-derived rule | `src/game/location.c:135-143`; `src/game/object.c:764-912,1985-2101,4665-4791`. |
| AP-006 | ART frame lookup applies the admitted critter/monster/unique-NPC mirror mapping before returning hotspot and offset metadata; the final art blit horizontally samples reflected pixels. | pinned CE-derived rule | `first_party/tig/src/art.c:523-568,1197-1256,3078-3267`. |
| AP-007 | The recorded module/resource/prototype fingerprints, selected ordinal/ART/resources, all facing metadata, and sanitized reports are deterministic. | observed original bytes/metadata | Two opt-in read-only probe runs on the existing candidate produced byte-identical sanitized reports; both returned the documented exit 3 (`NOT_READY`). This is structural evidence, not a runtime placement observation. |
| AP-008 | A synthetic formula test or a CE source trace establishes original-game pixel placement. | not established | No original executable was run and no independent original-runtime measurement is recorded. |

The pinned source revision is
`a7ff41b300ef712f0e7d088183a3d08957110cdd`, acquired as public source text
into ignored task-local research storage and verified as that exact commit. It
is consulted as Sustainable Use License specification material only. No CE code,
comments, structures, binaries, dependencies, or original data were copied into
this repository; no CE, Windows, original, or archived-tool executable was run.

## Caller trace and coordinate relation

The relation below describes the pinned source route, not a new project API.
It is written in operation order to avoid silently importing the static-scenery
rule.

1. `gamelib_draw_game` invokes `object_draw`. For a visible object node,
   `object_draw` calls `location_xy` on the object location, then reads the
   object's instance `OFFSET_X` and `OFFSET_Y` fields and adds `(40,20)`.
   `object_get_rect` is the rectangle builder used by the normal object blit
   path.
2. In isometric view, `location_xy` projects packed map-cell coordinates
   `(mapX,mapY)` using the current camera/view-origin pixel pair `(originX,
   originY)`:

   ```text
   projected = (
     originX + 40 * (mapY - mapX - 1),
     originY + 20 * (mapY + mapX))
   base = projected + (instanceOffsetX, instanceOffsetY) + (40, 20)
   ```

   `mapX/mapY` are source map-cell coordinates, `originX/originY` are
   camera/view-origin pixels, and every remaining value in this section is a
   screen/viewport pixel. X increases right and Y increases down in the source
   rectangle/blit coordinates. The source has separate top-down behavior; this
   research admits the isometric route only.
3. The current ART ID supplies action, requested facing/rotation, and frame.
   The normal Idle/Walk animation path changes that current ID: Walk is selected
   by the movement path, and Stand resets frame zero when movement completes.
   The input action, facing, and frame are caller-controlled state, whereas the
   selected physical group, flip, dimensions, hotspot, and ART offset are
   data-derived from the selected ART/frame.
4. `tig_art_frame_data` resolves requested rotations 1..3 to physical groups
   7..5 when mirroring is enabled, transforms `hotX` to
   `width - hotX - 1`, and negates `offsetX`. Requested rotations 0 and 4..7
   retain their physical groups. This preserves the existing eight-facing
   mapping; it does not invent compass semantics.
5. `object_get_rect` reads the effective frame width, height, hotspot and the
   object scale/shrunk state. It scales hotspot and dimensions with the source's
   float-to-int truncation and halves them when shrunk. It then computes:

   ```text
   topLeft = base - effectiveHotspot
   rectangle = (topLeft, effectiveWidth, effectiveHeight)
   ```

   The returned ART `offsetX/offsetY` values are **not read** by this normal
   object-rectangle calculation. They remain frame metadata and may vary across
   Walk frames; neither zero nor any other placement default is inferred for
   them.
6. The normal object route queues the resulting source/destination rectangles
   and eventually calls the ART window blit. The art layer performs reflected
   pixel sampling for the mirrored requested facings; the rectangle itself uses
   the already mirrored effective hotspot. Clipping, dirty rectangles, blending,
   ordering, and scaling do not establish a different world anchor.

For the unscaled, non-shrunk supported case, the source-derived relation can be
expressed compactly as `topLeft = projected + instanceOffset + (40,20) -
effectiveHotspot`. The apparent `-1` in the X projector and the later `+40` are
both present in this actor draw route; they must be preserved together rather
than borrowed from scenery. Frame offset is deliberately absent from that
expression.

The source uses mixed 64-bit location projection and narrower rectangle
operations. A future independently authored consumer must use widened signed
intermediates and checked multiply/add/subtract and checked narrowing for every
projection, instance-offset, scale, and hotspot operation. It must return typed
unsupported rather than wrap when the selected location, offsets, scale/shrunk
state, frame, action, palette, or facing is absent, invalid, or outside the
bounded contract.

## Bounded action, facing, and state coverage

The existing probe validates both admitted actions and every requested-facing
binding against the fingerprinted selected inputs. It retains all frame geometry
rather than normalizing it:

| Input / transform | Idle and Walk disposition |
| --- | --- |
| Action and frame | Current ART action/frame are caller state; Stand/Idle is action 0 and Walk is action 1 in the recorded chain. Every per-group frame remains distinct. |
| Requested facing 0, 4, 5, 6, 7 | Direct physical groups 0, 4, 5, 6, 7; no horizontal reflection. |
| Requested facing 1, 2, 3 | Physical groups 7, 6, 5; mirrored pixel blit and transformed effective hotspot/offset metadata. |
| Instance offsets | Caller/object fields added after location projection. Their selected-record values are intentionally not printed by the privacy-safe probe. Missing/direct/inherited semantics are not promoted to zero by this research. |
| Frame hotspot | Effective data-derived subtraction from base. It varies by group/frame and is mirror-sensitive. |
| Frame offset | Retained metadata only in this normal path; no second placement translation is evidenced. |
| Camera/view origin | Caller-controlled projector state. Camera change moves projected/base/top-left together. |
| Scale/shrunk flags | Caller/object render state; source changes rectangle geometry before the hotspot subtraction. The selected-record parity of each state remains unobserved. |

The source's movement scheduler consults more than raw ART metadata for move
progress/pause behavior. ART FPS remains recorded metadata only and does not
establish world displacement, collision, or locomotion speed.

## Reproducibility and privacy

Run the existing standalone probe with the canonical SDK and caller-owned
read-only module/resource/prototype inputs. Its `actor-research-source-01`
report must retain the documented input fingerprints, selected module ordinal
643, direct ART `0xc9001810`, resource ordinals 4753/4754, eight facing/group/
flip bindings, and before/after input equality. This task ran it twice after a
locked restore, no-restore build, and synthetic self-test; the sanitized reports
were byte-identical and both exited 3 because runtime placement remains
`NOT_READY`.

Do not commit or report installation paths, entry names, original payloads,
pixels, screenshots, coordinates, or extracted content. The report contains
only existing aggregate fingerprints, ordinals, selectors, frame metadata, and
source-line references.

## Readiness boundary and minimum future observation

The relation is sufficient only as a **pinned-source-derived research rule**.
It is not sufficient to mark the proposed actor contract `Ready`, implement a
presentation consumer, or claim original-runtime parity.

A future authorized original-runtime observation must bind the same fingerprinted
module/resource/prototype inputs and record locally: actor map location,
camera/view origin, action, requested facing, physical group, flip state, frame
index, scale/shrunk state, instance offsets, and measured screen top-left. The
minimum discriminating set is:

1. one unchanged actor/camera/frame state rendered as an unmirrored physical
   group and through its mirrored requested-facing partner;
2. two identified Idle/Walk frames with unequal dimensions, hotspots, or frame
   offsets at unchanged actor/camera state; and
3. one controlled actor-location or camera-origin delta that distinguishes the
   projection sign and unit.

Compare those local measurements to the source-derived relation without a
manual nudge. Keep captures and paths local; publish only sanitized numeric
outcomes and immutable fingerprints after a separately authorized review.

## Implemented / not implemented / remaining work

Implemented (research): exact pinned-source caller trace; named coordinate
relation and operation order; actor-specific proof that the generic object route
uses location projection, instance offsets, effective hotspot, and the
admitted reflection path; classified evidence/provenance; and repeatable,
privacy-safe selected-chain checks.

Not implemented: a production actor API, parser expansion, renderer, Godot
scene, movement/collision/pathfinding behavior, scheduler, composite actor
support, runtime capture, or original-runtime parity.

Remaining work: the minimal independent original-runtime observation above,
then a separately scoped and reviewed decision on any future consumer.
