# Ordinary static-scenery obstruction contract

## Status and boundary

This is a proposed research contract for the component
`ordinary-static-scenery/base-sector/cardinal-normal-critter`, evidenced by
[ARC-RES-012](../research/static-obstruction-evidence.md). It is not a
production API, collision grid, `IsWalkable` predicate, route finder, actor
controller, renderer rule, or claim of original-runtime parity.

The only supported profile is the immutable 64x64 Crash Site base sector with
the recorded module/resource/prototype identities. Context is normal,
non-editor traversal by one ordinary ground critter taking one adjacent
cardinal step on CE's non-recursive odd-rotation path with traversal flags
exactly zero. The component reads a destination-cell object contribution; it
does not decide the step, source cell, edge, or a combined cell result.

## Result vocabulary

Every result retains these independent dimensions:

```text
Supportedness = Supported | Unknown | Unsupported
Applicability = Applicable | NotApplicable | Unknown
Disposition   = Blocker | NoBlock | null
Provenance    = [archived-report | pinned-ce-source |
                 observed-serialized-instance | prototype-default |
                 synthetic-check | identity-bound-original-data-aggregate]
```

`Disposition` is legal only with `Supported + Applicable`. `Unknown`,
`Unsupported`, and `NotApplicable` carry null disposition. In particular,
Unknown never means NoBlock.

A result includes stable reason codes and its applicable provenance entries.
For example, a normal direct slot-18 result can carry
`OBSERVED_SERIALIZED_INSTANCE` and `PINNED_CE_SOURCE`; an inherited result also
carries `PROTOTYPE_DEFAULT`. The WorldEd report is corroborating archived
report provenance, not original-runtime provenance.

## Per-record assessment

1. The record must be v119 type-3 scenery. Other object types are
   `Unsupported + Unknown + null` for this named component.
2. Effective common-object flags are direct serialized zero-based slot 18, or,
   only when direct flags are absent, exactly one matching type-A prototype
   default. Missing, duplicate, or type-mismatched prototype resolution is
   `Unknown + Unknown + null`.
3. `OF_DYNAMIC` (`0x00002000`) must be clear. The bit is a subset selector,
   not a collision rule. A dynamic record is Unsupported here.
4. The direct serialized location must be present and decode to local X and Y
   each within `0..63`. Absence, malformed representation, checked coordinate
   overflow, or out-of-range values are `Unknown + Unknown + null`; no default
   coordinate or inherited prototype location exists.
5. `OF_INVENTORY` (`0x00001000`) yields `Supported + NotApplicable + null`.
   It is not a NoBlock shortcut.
6. Under the stated normal non-editor context, `OF_DESTROYED` (`0x00000001`) or
   `OF_OFF` (`0x00000002`) yields `Supported + NotApplicable + null`, also not
   NoBlock.
7. Remaining applicable records use `OF_NO_BLOCK` (`0x00000400`): set means
   `Supported + Applicable + NoBlock`; clear means
   `Supported + Applicable + Blocker`.

ART identity, object/ART offsets, hotspot, dimensions, alpha, visual bounds,
`OF_SHOOT_THROUGH`, sight/cover bits, and the adjacent slot-19 spell flags are
not inputs. The self-test contains a distinct slot-19 sentinel to prevent
accidental substitution for slot 18.

## Spatial registration and aggregation

An admitted record registers only in the exact destination cell decoded from
its direct serialized location. The contract does not infer source-cell
registration, direction, visual extent, neighboring cells, renderer footprint,
or edge behavior.

For a given destination cell within this component:

- any admitted `Blocker` contributor gives `Supported + Applicable + Blocker`;
- when all known admitted contributors are `NoBlock`, including an empty
  admitted subset, the component result is `Supported + Applicable + NoBlock`;
- an unresolved candidate at that known destination preserves
  `Unknown + Unknown + null` unless an admitted Blocker already wins.

This is a component aggregation rule only. It must not be used to convert an
otherwise unresolved terrain/object/actor/edge decision into an Open cell or a
walkable step.

## Explicit exclusions

The following requests are Unsupported for this component and retain null
disposition: projectile, sound, skip-objects, ignore-critters, wall, portal,
critter, diagonal, same-cell, non-adjacent, and cross-sector traversal. The
contract excludes portal/window special cases, traps, spells, combat, mutable
state, generalized diagonal handling, and all visual geometry. Those are not
silently treated as NoBlock.

## Composition boundary

A future passability consumer must retain the static-component result alongside
serialized block-mask, facade, tile-name, dynamic/state, actor, and edge
components. It may not return a full `Supported + Open`/walkable answer from
this component. The full selected-sector result remains Unknown until those
independent dependencies and precedence policy are admitted.

The contract neither selects archive, DIF, PATCH, nor loose-file precedence;
loads no neighbor sector; and does not establish original executable behavior.
See the [selected-sector passability contract](passability-contract.md) for
those retained gaps.
