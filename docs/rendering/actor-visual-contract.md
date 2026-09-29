# Proposed actor visual contract

## Status

This is a **later typed contract**, not a production API. The bounded research
chain now identifies one direct, single-layer monster's source selector, both
actions, resource ordinals, group/flip bindings, palettes, frames, geometry,
and raw ART FPS. The exact pinned-source actor route now has a documented,
state-dependent placement relation in
[actor placement evidence](../research/actor-placement-evidence.md), while this
contract remains `NOT READY` because original-runtime placement has not been
observed.
A future caller may render only a fully supported contract; every unknown is
typed unsupported, never filled by a default.

## Types

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
state. The pinned CE source relation is `topLeft = location_xy(mapCell) +
instanceOffset + (40,20) - effectiveHotspot`, after the source's mirror,
scale, and shrunk transforms; see the [placement evidence](../research/actor-placement-evidence.md)
for domains and operation order. ART frame offsets remain metadata and are not
added by this normal object rectangle. `flipX` preserves the selected physical
mapping and changes the effective hotspot before the rectangle is formed.

This is a source-derived documentation shape, not a runtime API or a readiness
claim. A future consumer must use checked widened arithmetic and make every
missing/unsupported input typed unsupported; it must not treat absent instance
offsets, camera state, scale, or frame data as zero/default.

Raw ART FPS is retained as metadata. The source move path can select Walk but
also derives the runtime pause from scheduler/stat inputs, so cadence authority
is `SourceScheduler`, not an FPS-derived walking speed. FPS does not define
position updates, collision, grid position, navigation, or locomotion. A caller
override is explicit; no frame rate inference is allowed.

## Support matrix

| Condition | Result |
| --- | --- |
| Direct or exactly-one matching prototype default, single layer, both actions, admitted facings/groups/palettes/frames, unique resource, checked caller inputs, pinned-source rule **and** independent original-runtime observation | `Ready` |
| Missing/duplicate/mismatched prototype; invalid mask/envelope; unknown composition | `Unsupported` |
| Resource/action/facing mapping, palette, ART metadata, caller input, or cadence authority unknown | `Unsupported` |
| Only the pinned-source placement relation is available; original-runtime observation/parity is absent | `NotReady` |
| Bounded census ends without a complete non-humanoid candidate | `NotReady` with aggregate reasons |

## Implemented / not implemented / remaining work

Implemented: this documentation-only shape, fail-closed readiness semantics,
and a source-recorded bounded Direct/SingleLayer proof for one Monster action
pair. It is not a runtime API.

Not implemented: these types in `Arcanum.Data`, Presentation, Godot, a scene,
or gameplay; any MOB/PRO runtime parser; actor rendering; composite equipment;
source scheduler; movement; collision; pathfinding; archive precedence.

Remaining work: obtain the minimal independent original-runtime placement
observation listed in the [placement evidence](../research/actor-placement-evidence.md),
then implement a separately reviewed consumer only for a complete contract.
