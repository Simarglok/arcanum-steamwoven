# Proposed actor visual contract

## Status

This is a **bounded typed contract**, not a general actor API. The bounded research
chain identifies one direct, single-layer monster's source selector, both
actions, resource ordinals, group/flip bindings, palettes, frames, geometry,
and raw ART FPS. The exact pinned-source actor route now has a documented,
state-dependent placement relation in
[actor placement evidence](../research/actor-placement-evidence.md), while this
contract remains `NOT READY` because original-runtime placement has not been
observed.
The historical standalone terrain-viewer actor preview consumes this exact profile through a
separate caller-selected, fixed-authenticated actor archive. Every unknown is
typed unsupported, never filled by a default; this does not change runtime
readiness or authorize a generalized `Arcanum.Data` actor interface.

## Types

ARC-RENDER-006's WORLD consumer uses the distinct closed HumanFixed profile
below and the same checked normal placement. Sector-local free motion and
screen-octant facing numbers remain **project diagnostic policies**,
not source compass/scheduler/speed or original movement/collision claims.
See [WORLD policy](terrain-viewer.md#integrated-world-diagnostic-policy).
The historical original-runtime `NOT READY` conclusion above is unchanged.

```text
ActorSelector
  safeSourceId, moduleFingerprint(length, sha256), moduleDatOrdinal,
  objectType, prototypeAid?

EffectiveActorVisualId
  packedSelector:uint32,
  source: Direct | PrototypeDefault,
  composition: SingleLayer | CompositeUnsupported | Unknown

ActorVisualContract
  selector: ActorSelector,
  effectiveVisualId: EffectiveActorVisualId,
  idle: ActorActionVisual, walk: ActorActionVisual,
  evidence: EvidenceReference[], readiness: Ready | Unsupported | NotReady

ActorActionVisual
  action: Idle | Walk,
  sourceActionCode:uint32,
  resourceFingerprint(length, sha256), resourceDatOrdinal,
  packedSelector:uint32, cadence: ActorCadence,
  facings: FacingBinding[]

FacingSelector
  opaqueSourceValue:uint32

FacingBinding
  selector: FacingSelector, physicalGroup:int, flipX:bool,
  requestedPalette:int, effectivePalette:int,
  frames: ActorFrameVisual[]

ActorFrameVisual
  sequenceIndex:int, physicalFrameIndex:int,
  width:int, height:int, hotX:int, hotY:int, offsetX:int, offsetY:int,
  sourceActionMarker:int?, evidence: EvidenceReference

SourceDerivedActorPlacement
  isometricMapCellX:int64, isometricMapCellY:int64,
  cameraOriginX:int64, cameraOriginY:int64,
  instanceOffsetX:int, instanceOffsetY:int,
  renderFlags:uint32, placementPath: ArtFrameHotspot | CachedRenderUnsupported | Unknown,
  effectiveHotX:int, effectiveHotY:int,
  scalePercent:int, shrunk:bool,
  topLeftX:int, topLeftY:int, classification: PinnedCeDerived

ActorCadence
  rawArtFramesPerSecond:int,
  sourceScheduledInterval:int?,
  authority: ArtMetadata | SourceScheduler | CallerOverride | Unknown

EvidenceReference
  claimId, immutableSource, location, classification,
  confidence, limitation
```

`source` distinguishes a field-0 direct value from an exactly-one valid
prototype default. `composition` must remain `CompositeUnsupported` or
`Unknown` unless evidence proves the selected actor is single-layer. The
current selected source chain is `Direct` and `SingleLayer`; that does not
generalize to other actor records. The contract contains exactly Idle and Walk.
`FacingSelector` is deliberately opaque: the current eight numeric values are
a bounded admitted set with an explicit group/flip table, not an invented
compass convention.

## Render boundary

For the admitted isometric source route, the caller supplies map cell,
camera/view origin, direct instance offsets, action/facing/frame, and render
state. The pinned CE hotspot relation is admitted only when
`ORF_08000000` is clear: `topLeft = location_xy(mapCell) + instanceOffset +
(40,20) - effectiveHotspot`, after the source's mirror, binary-float scale,
cast-to-int truncation toward zero, and (when shrunk) signed `/ 2` truncation
toward zero transforms; see the [placement evidence](../research/actor-placement-evidence.md)
for domains and operation order. With `ORF_08000000` set, the source instead
uses cached `RENDER_X/Y/WIDTH/HEIGHT` and returns before current-ART frame
lookup/hotspot placement. The selected metadata evidence does not establish
that flag, so `CachedRenderUnsupported` and `Unknown` are fail-closed states;
they must not be represented by a hotspot default. ART frame offsets remain
metadata and are not added by the normal object rectangle. `flipX` preserves
the selected physical mapping and changes the effective hotspot before the
rectangle is formed.

This is a source-derived documentation shape, not a runtime API or a readiness
claim. A future consumer must use checked widened arithmetic and make every
missing/unsupported input typed unsupported; it must not treat absent instance
offsets, camera state, scale, or frame data as zero/default.

Raw ART FPS is retained as metadata. The source move path can select Walk but
also derives the runtime pause from scheduler/stat inputs, so cadence authority
is `SourceScheduler`, not an FPS-derived walking speed. FPS does not define
position updates, collision, grid position, navigation, or locomotion. A caller
override is explicit; no frame rate inference is allowed.

## Closed HumanFixed WORLD profile (ARC-RENDER-006)

WORLD selects one complete fixed human layer, not a reusable character engine.
The mapping-only source is type2/body0 female fixed underwear/armor0, shield0,
weapon0, layer0/order0, requested=effective palette0. It has no proven human
NPC/module association and exposes no race, appearance, equipment or compositing
selection. Historical standalone actor preview remains MonsterRegression.

| Dimension | Bounded evidence / production contract | Not established |
|---|---|---|
| Source | Authenticated existing actor archive; Idle selector `0x20000000` ordinal2667, Walk `0x20000040` ordinal2668 | NPC identity, original player appearance |
| ART metadata | Idle 8 physical groups × 1 frame, raw6 FPS, marker0; Walk 8 × 10, raw17 FPS, marker9; full 88-row numeric metadata commitment | Art markers as logical actions; original scheduler |
| Logical actions / cadence | Idle0/Walk1; positive raw6/17 rates drive diagnostic clock; action reset, same-action facing retains phase | Original animation/motion-speed parity; fallback cadence |
| Facing | Project octants clockwise up..up-left 0..7; source facings opaque 0..7; both action group tables `[0,7,6,5,4,5,6,7]`, flips `[false,true,true,true,false,false,false,false]` | Original compass meanings |
| Geometry | Per-frame signed hotspots/unequal dimensions; mirrored hotX=`width-hotX-1`; no ART offset added in normal uncached scale100 placement | Normalized-size sprites, HD replacement assets |
| Anchor | Single world foot anchor Project(position)+(0,20) (WORLD; the AREA chart uses +(40,20), the diamond centre, since ARC-RENDER-011); baseline Idle rect(-9,-55,28,77), Walk(-14,-53,27,75) | Sprite bounds as collision or gameplay footprint |
| Coverage | Exactly 55 reachable keys (Idle5/Walk50), 457676 packed RGBA bytes (36384/421292); physical 88 rows are not 88 cache entries | OS/native allocation from algebraic byte sums |
| Presentation | Original raster size, nearest filtering; source/export actual main-viewport comparisons must independently cover all admitted directions/frames and reject hidden/wrong-facing controls | Headless or texture-only comparisons as GUI passes |

The profile is closed and immutable in Core. Source admission authenticates
original archive identity plus all physical numeric metadata before retaining
the reachable catalogue. Checked unique-key accounting precedes RGBA retention;
human coverage cannot inherit the legacy184-key ceiling as a result invariant.
WORLD's aggregate remains256 MiB, at most4096 terrain keys and five statics;
its selected union ceiling is4156, whereas MonsterRegression retains4285.
An ordinary app holds uploaded textures and immutable metadata, not decoded
RGBA arrays. Cancellation, stale generations, upload rollback, reload and exit
must preserve single ownership; byte sums alone do not prove that lifecycle.

The independent research packet separates source-semantic evidence, physical
rows, reachable-key enumeration, independent wire decode, local attributed
reference-image observation and unknowns. Chaotic Evil specification provenance
is pinned revision `a7ff41b300ef712f0e7d088183a3d08957110cdd`, Sustainable Use
License1.0; no CE implementation is copied/translated/executed. ArcNET is not
the independent oracle. Original assets/pixels and local paths stay ignored/local
and must not enter commits, PRs or public attachments.

For later HD work preserve this same world anchor and separately defined world
footprint. Do not move the world position to compensate for a replacement
image's dimensions. This task adds documentation only, no HD assets or system.

## Support matrix

| Condition | Result |
| --- | --- |
| Direct or exactly-one matching prototype default, single layer, both actions, admitted facings/groups/palettes/frames, unique resource, checked caller inputs including known `ORF_08000000` clear state, pinned-source rule **and** independent original-runtime observation | `Ready` |
| Missing/duplicate/mismatched prototype; invalid mask/envelope; unknown composition | `Unsupported` |
| Cached-render state (`ORF_08000000` set), or unknown render flags | `Unsupported` |
| Resource/action/facing mapping, palette, ART metadata, caller input, or cadence authority unknown | `Unsupported` |
| Only the pinned-source placement relation is available; original-runtime observation/parity is absent | `NotReady` |
| Bounded census ends without a complete non-humanoid candidate | `NotReady` with aggregate reasons |

## Implemented / not implemented / remaining work

Implemented: the fixed-profile terrain-viewer diagnostic consumes this bounded
shape with an explicit caller anchor/state, all admitted frames, and raw-FPS
preview controls. It remains a diagnostic presentation subset, not a runtime
API or placement-parity result.

Not implemented: these types in `Arcanum.Data`; a general actor parser;
composite equipment; source scheduler; movement; collision; pathfinding;
archive precedence; or gameplay.

Owner authorization now permits a separately bounded fixed-profile diagnostic
consumer that labels all anchor/render state as caller policy and preserves
`NOT READY` original-runtime parity. It is not a general actor API or a
replacement for the independent original-runtime placement observation.

Remaining work: obtain the minimal independent original-runtime placement
observation listed in the [placement evidence](../research/actor-placement-evidence.md)
before any compatibility/readiness claim or expanded consumer.
