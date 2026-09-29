# Proposed actor visual contract

## Status

This is a **later typed contract**, not a production API. Its only current
research result is `NOT READY` as recorded in
[actor visual evidence](../research/actor-visual-evidence.md). A future caller
may render only a fully supported contract; every unknown is typed unsupported,
never filled by a default.

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
contract contains exactly Idle and Walk. `FacingSelector` is deliberately
opaque: no eight-direction or compass convention is invented.

## Render boundary

The caller supplies an actor world anchor and an admitted action/facing. For an
accepted frame, top-left is `worldAnchor + anchorToTopLeft`. That delta is
valid only when actor-specific evidence establishes it; ART hotspot and offset
metadata alone does not establish it. `flipX` preserves the selected physical
mapping and is not a reason to mutate geometry in place.

Raw ART FPS is retained as metadata. It does not define source scheduling,
movement speed, position updates, collision, grid position, navigation, or
locomotion. A caller override is explicit; no frame rate inference is allowed.

## Support matrix

| Condition | Result |
| --- | --- |
| Direct or exactly-one matching prototype default, single layer, both actions, admitted facings/groups/palettes/frames, unique resource, established anchor and cadence | `Ready` |
| Missing/duplicate/mismatched prototype; invalid mask/envelope; unknown composition | `Unsupported` |
| Resource/action/facing mapping, palette, ART metadata, actor anchor, or cadence authority unknown | `Unsupported` |
| Bounded census ends without a complete non-humanoid candidate | `NotReady` with aggregate reasons |

## Implemented / not implemented / remaining work

Implemented: this documentation-only shape and fail-closed readiness semantics.

Not implemented: these types in `Arcanum.Data`, Presentation, Godot, a scene,
or gameplay; any MOB/PRO runtime parser; actor rendering; composite equipment;
source scheduler; movement; collision; pathfinding; archive precedence.

Remaining work: establish the evidence gates listed in the evidence record,
then implement a separately reviewed consumer only for a complete contract.
