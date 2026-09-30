# Static-obstruction evidence (ARC-RES-012)

## Scope and status

This is a bounded research record for the named component
`ordinary-static-scenery/base-sector/cardinal-normal-critter`. It is not total
object collision, `IsWalkable`, a production collision grid, navigation,
actor movement, renderer geometry, or original-runtime validation.

The context is one immutable Crash Site 64x64 base sector: a normal,
non-editor ordinary ground critter makes one adjacent cardinal step on the
pinned CE non-recursive odd-rotation path, with traversal flags exactly zero.
The component considers only a type-3 scenery contribution registered in the
step destination cell. Projectile, sound, skip-objects, ignore-critters,
wall/window/portal branches, critters, traps, spells, combat, mutable state,
diagonals, same-cell/non-adjacent requests, and cross-sector traversal are
outside this component.

This evidence has source-supported and identity-bound original-data aggregate
status. CE is one source lineage, not independent confirmation or a proof of
the original executable. The original installation was read only; this record
contains no local path, original payload, text, image, raw object bytes, names,
or full grid.

## Provenance and source trace

| Provenance kind | Immutable identity / trace | Role and limit |
| --- | --- | --- |
| Archived report | WorldEd manual archive, SHA-256 `049faa6956bab507f99d6174ce14b2b4feb49a49b0dcf7b69bec3203a3df72cb`; lines 850-858, 878-884, and 1034-1036 | Corroborates the reported “No Block” scenery/editor concept and a blocking tile where placed. It is not runtime proof. |
| Pinned source | [`alexbatalov/arcanum-ce` `a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd) | One attributed source lineage; Sustainable Use License applies. No code was copied or mechanically translated. |
| Serialized instance | Fixed selected-sector v119 records parsed by `StaticObjectLoader.Inspect` | Direct location and direct slot-18 flags are observed only in the authenticated input. |
| Prototype default | Exactly one matching type-A prototype when direct slot 18 is absent | Resolves only the missing object-flags default; it never supplies location. |
| Synthetic check | `Arcanum.CrashSiteProbe --self-test` | Independently asserted boundary and aggregation cases, not an original-data oracle. |
| Original-data aggregate | Fixed module/resource/six-prototype identities verified before and after the passability run | Safe counts and capped numeric rows only. |

The source trace is intentionally narrow:

- `src/game/obj.h:10-32` numbers `OBJ_F_FLAGS` as enum 19, whose serialized
  common-object position is zero-based slot 18. Slot 19 is spell flags and is
  deliberately not an input here.
- `src/game/obj_flags.h:11-24` supplies `OF_DESTROYED` `0x00000001`, `OF_OFF`
  `0x00000002`, `OF_NO_BLOCK` `0x00000400`, `OF_INVENTORY` `0x00001000`, and
  `OF_DYNAMIC` `0x00002000`.
- `src/game/object.c:1779-1788` supplies the static predicate used only as a
  subset selector, not a collision rule.
- `src/game/sector_object_list.c:115-185` rebases loaded locations and inserts
  sector objects; `src/game/obj.c:1189-1205` is handle postprocessing, not a
  footprint calculation.
- `src/game/sector_object_list.c:532-624,652-676` rejects inventory objects
  and uses the location-derived tile list. Offsets affect list order, not cell
  membership or footprint.
- `src/game/object.c:3304-3394` is the exact-cell list query and the normal
  non-editor DESTROYED/OFF filtering.
- `src/game/object.c:2978-3027,3152-3282` supplies the ordinary destination
  object branch, its `OF_NO_BLOCK` handling, and the excluded diagonal/wall/
  portal/context branches. `src/game/path.c:239-280` and
  `src/game/anim.c:4737-4763` identify the caller/traversal context.

No ART, offset, hotspot, sprite dimension, pixel alpha, visual rectangle,
`OF_SHOOT_THROUGH`, sight/cover flag, or adjacent slot-19 spell flag is an
input to the named component.

## Admitted record rule

For a v119 type-3 record, effective object flags come from direct serialized
slot 18, otherwise exactly one matching type-A prototype default. `OF_DYNAMIC`
must be clear; this selects the ordinary immobile subset and does not by itself
establish blocking. The direct serialized location must be present and decode
to a coordinate within the fixed local `0..63` extent. Missing, malformed, or
out-of-range location remains Unknown; no zero coordinate or prototype location
is invented.

`OF_INVENTORY` gives `Supported + NotApplicable + null`, because the source
insertion path rejects it from the sector cell list. In normal non-editor
context, `OF_DESTROYED` or `OF_OFF` likewise gives `Supported + NotApplicable +
null`. For the remaining applicable records, `OF_NO_BLOCK` set gives `NoBlock`;
clear gives `Blocker`. Type mismatch, dynamic scenery, and all excluded
traversal contexts remain Unsupported rather than becoming NoBlock.

A record is registered only at the exact destination cell decoded from its
direct serialized location. This evidence does not infer source-cell
membership, edge blocking, neighbors, visual extent, renderer footprint, or
same-cell position from offsets/hotspots.

## Fixed-profile aggregate run

The read-only `--passability` run used the selected base sector and its
pre-existing module/resource/six-prototype identity gates. The terminal record
count was 317; every identity check before and after use passed.

| Aggregate | Observed value |
| --- | ---: |
| Resolved slot-18 flags / type-3 records | 317 / 317 |
| Direct / prototype object flags | 31 / 286 |
| Direct locations present / in local range | 317 / 317 |
| Dynamic / inventory / OFF / DESTROYED exclusions | 0 / 0 / 0 / 0 |
| Applicable Blocker / NoBlock | 58 / 259 |
| Unknown records | 0 |
| Distinct occupied destination cells | 293 |
| Same-cell groups / maximum multiplicity | 22 / 4 |

The probe emits exactly six rows, in this fixed order: first direct Blocker,
first inherited Blocker, first direct NoBlock, first inherited NoBlock, first
same-cell group with at least two contributors containing a Blocker, and first
same-cell all-NoBlock group with at least two contributors.
Only source index, local coordinate, direct-vs-prototype source, disposition,
and contributor count are emitted:

| Source index | Local x/y | Flags source | Disposition | Contributors |
| ---: | --- | --- | --- | ---: |
| 1 | 5 / 1 | direct | Blocker | 1 |
| 7 | 38 / 2 | prototype | Blocker | 1 |
| 166 | 19 / 34 | direct | NoBlock | 1 |
| 0 | 50 / 0 | prototype | NoBlock | 1 |
| 15 | 59 / 4 | mixed | Blocker | 2 |
| 2 | 6 / 1 | prototype | NoBlock | 2 |

These aggregates do not show that NoBlock means a walkable cell or step. They
only establish this named ordinary-static-scenery component in the fixed
profile.

## Synthetic matrix and reproducibility

`--self-test` independently asserts direct and prototype Blocker/NoBlock,
direct slot-18 override over an opposite matching prototype default through the
existing resolver and assessment paths, slot-19 sentinel non-effect,
unresolved flags from missing/duplicate/type-mismatched prototype edges,
missing/malformed/out-of-range and checked-overflow locations, type/DYNAMIC/
INVENTORY/OFF/DESTROYED distinctions, every excluded traversal context,
source-to-destination mapping, same-cell blocker/no-block/unresolved/empty
aggregation, row selection that skips singleton groups, selects each eligible
multi-contributor category, and returns the zero/none row when absent, terminal
317 contributor multiplicity, and the pre-existing
framing, field-mask/count, array/string-cap, slot-18/19, prototype, and mask
checks. The assertions use authored expected outcomes rather than a
self-comparison oracle.

Reproduce with the repository-pinned SDK and explicit local inputs; do not put
those inputs or paths in Git:

```text
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --passability --module <module> --prototype-root <prototypes> --resource <resource>
```

## Remaining limits

This result does not choose DIF/PATCH/loose-file precedence and does not
resolve terrain-name rules, dynamic/state changes, actor capability, wall or
portal behavior, diagonal/edge handling, cross-sector traversal, or original
runtime behavior. Its `NoBlock` outcome is not a total occupancy or full
passability outcome. See the [static component contract](../world/static-obstruction-contract.md)
and [selected-sector passability evidence](sector-passability-evidence.md).
