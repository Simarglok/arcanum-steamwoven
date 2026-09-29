# Proposed actor visual contract

## Status

This is a **later typed contract**, not a production API. The bounded research
chain now identifies one direct, single-layer monster's source selector, both
actions, resource ordinals, group/flip bindings, palettes, frames, geometry,
and raw ART FPS; it remains `NOT READY` because an actor-specific world-anchor
delta is unknown, as recorded in [actor visual evidence](../research/actor-visual-evidence.md).
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
  anchorToTopLeftX:int, anchorToTopLeftY:int,
  sourceActionMarker:int?, evidence: EvidenceReference

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

The caller supplies an actor world anchor and an admitted action/facing. For an
accepted frame, top-left is `worldAnchor + anchorToTopLeft`. That delta is
valid only when actor-specific evidence establishes it; ART hotspot and offset
metadata alone does not establish it. `flipX` preserves the selected physical
mapping and is not a reason to mutate geometry in place.

Raw ART FPS is retained as metadata. The source move path can select Walk but
also derives the runtime pause from scheduler/stat inputs, so cadence authority
is `SourceScheduler`, not an FPS-derived walking speed. FPS does not define
position updates, collision, grid position, navigation, or locomotion. A caller
override is explicit; no frame rate inference is allowed.

## Support matrix

| Condition | Result |
| --- | --- |
| Direct or exactly-one matching prototype default, single layer, both actions, admitted facings/groups/palettes/frames, unique resource, established anchor and cadence | `Ready` |
| Missing/duplicate/mismatched prototype; invalid mask/envelope; unknown composition | `Unsupported` |
| Resource/action/facing mapping, palette, ART metadata, actor anchor, or cadence authority unknown | `Unsupported` |
| Bounded census ends without a complete non-humanoid candidate | `NotReady` with aggregate reasons |

## Implemented / not implemented / remaining work

Implemented: this documentation-only shape, fail-closed readiness semantics,
and a source-recorded bounded Direct/SingleLayer proof for one Monster action
pair. It is not a runtime API.

Not implemented: these types in `Arcanum.Data`, Presentation, Godot, a scene,
or gameplay; any MOB/PRO runtime parser; actor rendering; composite equipment;
source scheduler; movement; collision; pathfinding; archive precedence.

Remaining work: establish the actor-specific anchor-to-top-left evidence gate
listed in the evidence record, then implement a separately reviewed consumer
only for a complete contract.
