# Proposed selected-sector passability contract

See [whole-exterior adjacent-step research](area-traversal-contract.md)
for eight-direction/source/state frontiers, not a universal occupancy verdict, and the
[closure research](../research/area-passability-closure.md) for the T0 rules that resolve them.

## Status

This is an evidence-bounded proposal for a future consumer of [selected-sector passability evidence](../research/sector-passability-evidence.md). It is **not implemented** as a production collision grid, navigation API, path finder, actor controller, renderer overlay, or gameplay rule.

ARC-RES-014 now separately implements a [research-only cardinal assessment](cardinal-traversal-contract.md)
with [admitted original-data rows](../research/cardinal-traversal-evidence.md) in one
explicit isolated PC/current-CRITTER-ART context and connected rectangle. It does
not change this general occupancy proposal or the public component contracts.

ARC-DATA-008 separately implements the [production cardinal assessment](cardinal-traversal-assessment.md)
for that exact fixed rectangle/context using owned complete evidence. This is not
a general occupancy grid, actor permission or runtime movement integration;
the proposal and historical Unknown frontier below remain unchanged.

It applies only to the immutable base-sector profile `(1452,1290)`, origin `(92928,82560)`, extent 64×64, module selector and identities recorded in the evidence document. A caller must supply the version/profile, base-input identity, coordinate, and the declared actor/context boundary. It does not choose archive, `.dif`, PATCH, or loose-file precedence.

## Result vocabulary

Both result kinds keep two independent dimensions:

```text
Coverage    = Supported | Unsupported | Unknown
Disposition = Open | Blocked | null
```

`Unknown` never means Open. `Supported + Open` is allowed only after every required component is admitted and nonblocking. `Supported + Blocked` requires an unconditional, non-overridable blocker in the declared context. `Unknown + null` preserves an in-profile missing, ambiguous, runtime-derived, or unverified dependency. `Unsupported + null` identifies a request outside this bounded profile.

Every result carries machine-readable reason and provenance codes, not an implied boolean. Suggested codes include:

```text
BASE_BLOCK_MASK_SET                 SOURCE_SERIALIZED_BASE
FACADE_WALKABILITY_BIT              SOURCE_SERIALIZED_BASE
TILE_NAME_RULE_UNAVAILABLE          SOURCE_ART_MES_NAME
STATIC_SPATIAL_CONTRIBUTION_UNKNOWN SOURCE_RUNTIME_CONTEXT
ACTOR_CAPABILITY_UNAVAILABLE        SOURCE_RUNTIME_CONTEXT
DIAGONAL_RULE_UNPROVED              SOURCE_GAP
CROSS_SECTOR_DATA_ABSENT            PROJECT_POLICY
PROFILE_OR_PRECEDENCE_UNSUPPORTED   PROJECT_POLICY
```

## `CellOccupancyAssessment`

A cell assessment is a coordinate-scoped evidence record, not a reusable grid cell:

```text
CellOccupancyAssessment
  profile: fixed base-sector identity
  local/world coordinate: required, 0..63 in the admitted sector
  components:
    serializedBlockMask: coverage + disposition + provenance
    tileFacadeName: coverage + disposition + provenance
    ordinaryStaticScenery: supportedness + applicability + disposition + provenance
    otherStaticAndDynamicObjects: coverage + disposition + provenance
    dynamicAndContext: coverage + disposition + provenance
  combined: coverage + disposition + reason/provenance codes
```

For this candidate, a set serialized block bit may report `Supported + Blocked` for that component because the original's traversal path tests it before later tile branches (source reading, F0). A clear bit does **not** report Open. Facade bit 0 is a raw-word component only; tile-class records additionally require an ART/MES/name rule. The named ordinary static-scenery destination-cell component is now separately evidenced by [ARC-RES-012](static-obstruction-contract.md): it can report its own supportedness/applicability/nullable Blocker-or-NoBlock result in the fixed context. It does not establish a general static-object component: dynamic objects, other states/types, visual geometry, source-cell/edge effects, and all unadmitted contributors remain unknown. `otherStaticAndDynamicObjects` therefore remains `Unknown + null`.

The combined cell assessment is therefore `Unknown + null` in the present candidate, even when a component contains a useful blocking fact. Component visibility prevents a future consumer from silently treating a clear/unknown component as Open.

## `StepTraversalAssessment`

A step assessment is directional and distinct from occupancy:

```text
StepTraversalAssessment
  from, to: required fixed-sector coordinates
  relation: adjacent cardinal | adjacent diagonal
  actor/context: required declared capability/state/flags profile
  fromCell, toCell, edgeAndCorner, objectTraversal: component assessments
  combined: coverage + disposition + reason/provenance codes
```

Same-cell and non-adjacent requests are `Unsupported + null`; they are not route-search requests. A cardinal step remains `Unknown + null` until its source/destination terrain components, other relevant object contributors, actor capability/context, and source-proven edge semantics are admitted. The ARC-RES-012 ordinary-static component is destination-cell-only and must not be promoted into source/edge semantics. Diagonal steps are `Unknown + null` in-profile because the original has recursive shoulder/object behavior (source reading, F0) but this contract has not established all required corner/cross-cell semantics. It must not invent a diagonal corner policy.

A step that leaves the 64×64 sector is `Unsupported + null` with `CROSS_SECTOR_DATA_ABSENT`. The proposal deliberately does not load or infer a neighbor. Other sector IDs, source versions, profiles, `.dif`/PATCH/loose precedence, generalized dynamic objects, and unsupported actor/state categories are likewise `Unsupported + null`.

`PATH_FLAG_0x1000` is an unavailable source branch, not a fallback: the source implementation is explicitly incomplete. A future consumer must reject/mark unknown that context rather than select a default behavior.

## Composition rule

The future implementation must evaluate every admitted component and retain all component results. It may short-circuit the **combined** result to `Supported + Blocked` only if a blocker is source-proven unconditional under the entire declared context. It may return `Supported + Open` only if all terrain, facade/name, static-object, edge/diagonal, actor, and state dependencies are admitted and nonblocking. Otherwise, in-profile uncertainty remains `Unknown + null`.

This policy deliberately does not translate a source-level traversal cost into an occupancy boolean. The original's object traversal routine (0x43FD70, 0x43FE00) distinguishes source/destination lists, wall/portal orientation, object flags/state, actor portal/window capability, traps/fire, and directed rotation. Those are contract inputs or declared unknowns, not convenience defaults.

## Static-subset readiness gate

A static combined result may become implementation-ready only when all of the following are true:

1. the exact base input and coordinate profile are fingerprinted and fixed;
2. block-mask shape, ordinal mapping, bit order, and polarity are proven;
3. each explicit tile/facade/name rule is resolved from admitted source data;
4. every relevant static, dynamic, and stateful object contributor is completely enumerated and its spatial contribution is proven beyond the ARC-RES-012 ordinary-static subset;
5. every direct/prototype field needed by traversal is resolved to an exact serialized slot/default;
6. actor/context and state inputs are declared with no omitted dynamic override; and
7. the cardinal edge and any admitted diagonal/corner rule are source-proven.

If any condition fails, component facts can be useful, but combined occupancy and step traversal remain unknown. The original general proposal fails conditions 3 through 7. It does not promote the 317-record census or the ARC-RES-012 static subset into total collision semantics. The separate ARC-RES-014 research profile closes these dependencies only for its authenticated inputs, full contributor census and declared cardinal context; other states, diagonals, precedence and production admission remain unproved.

## Non-goals

This proposal does not authorize changes to `TerrainLoader`, `StaticObjectLoader`, their public models, the viewer, Godot, the gamepad lab, simulation/navigation APIs, or the fixed local pathfinding study. It introduces no collision data structure, navmesh, route search, actor/controller, visual overlay, generalized object parser, generalized map/version support, or original-runtime compatibility assertion.
