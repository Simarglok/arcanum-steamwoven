# Bounded cardinal traversal evidence contract (ARC-RES-014)

ARC-RES-017's [whole-exterior research proposal](area-traversal-contract.md)
retains this frozen profile separately; it does not widen cardinal admission.

## WORLD003 opt-in amendment (historical cardinal contract retained)

The research contract below remains the `CardinalOnly` default and historical
evidence. WORLD003 selects the separate production `BoundedAdjacent` policy,
profile `BASE_SECTOR_1452_1290_ADJACENT_V1`; sector, zone x4..8/y0..3, context,
guarded input identity and unavailable path0x1000 branch remain unchanged.
Final destination mask/facade/name are queried once. For a map diagonal, both
object-only shoulder paths must close: XShoulder, XFinal, YShoulder, YFinal.
Each retains exact endpoints, leg direction, original diagonal direction, full
record census and role-labelled gaps. Shoulder terrain is not a contributor.
One complete Open assessment binds one runtime key/half-second segment, not four
permits or intermediate commits. A known unconditional blocker retains all gaps;
unknown membership/flags/census cannot certify Open or invent a blocker.
See [production assessment](cardinal-traversal-assessment.md) and the independently
projected [screen mapping](../rendering/terrain-viewer.md). Default cardinal diagonal
Unknown tests and the historical research outputs are not reinterpreted as this
new profile. No dynamic walls/portals, executable parity or wider-zone admission.

## Status and scope

This is an implemented **research-only** assessment in the standalone
[CrashSiteProbe](../../tools/research/Arcanum.CrashSiteProbe/CardinalTraversalProbe.cs),
not a production traversal API, occupancy grid, route finder or actor controller.
[Evidence and immutable source references](../research/cardinal-traversal-evidence.md)
record an authenticated Open/Blocked pair and reversals. Native exact-head review,
publication and human merge are distinct later gates; this document is not a review
verdict. Original-executable parity on macOS is **NOT_VERIFIED**.

The existing [passability proposal](passability-contract.md),
[terrain components](terrain-obstruction-components.md) and
[ordinary-static components](static-obstruction-components.md) retain their public
boundaries. This evidence closes one deliberately isolated diagnostic context,
not all 4096 cells, all 91 unresolved tile-name components or installed gameplay.
No public production models/parsers, Godot, input, movement or route search change.

## Fixed inputs and project declarations

**Original structural facts**: base sector `(1452,1290)`, origin `(92928,82560)`,
64×64; selector `maps\\Arcanum1-024-fixed\\86570436012.sec`, ordinal463, flag2,
decoded47291. The module/resource/six prototype identities in the evidence document
are mandatory, authenticated before and after use. No PATCH, `.dif`, loose-file
precedence search, fallback or alternate resource profile is admitted. Tile names
come from the explicit resource with `NonWindowsMacOs` path normalization.

**Project policy**, not recovered original actor facts: one immutable context
`ISOLATED_PC_CRITTER_ART_NONPOLYMORPH`, non-null diagnostic actor kind `OBJ_TYPE_PC`
(15), current ART class `TIG_ART_TYPE_CRITTER` (2), spell flags0; ordinary ground,
non-editor, non-polymorph, no floating/passwall/special actor override. Supplemental
runtime registries are explicitly empty. The full selected serialized registry
is included, not the filtered sprite/scenery subset. Actor self is notionally at
the request's source only. No door/AI-open, scripts, savegame, combat or spawn state
is executed. These declarations do not recover a human/equipment model or describe
the WORLD rendered monster. Each directed query has the same immutable predicate
context; no actor is actually moved to a potentially blocked starting cell.

**Consequence of an earlier source reading** (F0, not checked in the executable), not a chosen numeric shortcut: the normal
adjacent animation helper starts traversal flags0, ORs polymorph/capability blocking
bits; non-polymorph + PC portal capability + current CRITTER ART window capability
contribute0, so the complete selected helper flags are0. Normal begin invocation is
in the adjacent-move begin function (0x42E9B0), under its begin/run gate `0x400`. Path flags0 select the current
path rotation (current index0 for this one adjacent diagnostic request), not the
ART-rotation branch. The helper's tile argument `a2=false` does not suppress tile
blocking. This is **not** normal route admission/cost or success of the entire
animation scheduler; all begin-animation work outside this predicate is unobserved.

**Project selection policy**: connected rectangle local x4..8/y0..3; proposed
`(6,1)->(5,1)` and `(6,1)->(7,1)` plus reverses were fully admitted here. No whitelist
of permissive cells exists in the assessor. If those four requests do not supply
both a gap-free Open and Blocked, deterministically enumerate all directed cardinal
pairs inside that rectangle (row-major, directions1/3/5/7), then at most one explained
alternative x42..46/y0..3 near scenery index4. No third patch/full-sector search.
Leaving either rectangle is unsupported; y0 never authorizes a neighbor sector.
The measured first-zone selectors are frozen in the evidence table.

## Owned request and result

The internal immutable request carries profile ID, full input identity, context ID,
sector X/Y, from/to local cells, direction and the immutable actor/context value.
Coordinates are checked before ordinal/world arithmetic. Signed arithmetic is widened
for neighbor tests; overflow/out-of-range input cannot wrap into the sector. Snapshot
terrain and **all** records are copied into read-only arrays, with immutable rows.
No archive, source buffer, caller list or mutable context is retained.

```text
Coverage    = Supported | Unknown | Unsupported
Disposition = Open | Blocked | null
Component   = ID, Applicability(Applicable|NotApplicable|Unknown), Coverage,
              nullable Disposition, numeric inputs, reason, provenance
Assessment  = Coverage, nullable Disposition, reason, ordered components, gaps
```

This internal shape is not a new public API/schema. Rows are evidence about an
individual directed query. Component Open means nonblocking **within that component**,
not whole-step admission. NotApplicable/null remains explicit; it is not permission.

Request/profile/input/context/sector/zone/actor validation happens before component
composition. Other profile/input/sector, unsupported actor/state, same/nonadjacent
cells, direction mismatch and sector/zone escape return `Unsupported/null`.
In-profile adjacent diagonals/corners and `PATH_FLAG_0x1000` return `Unknown/null`
with explicit gap. That path flag is an unavailable source branch, not permission
to select the normal fallback. Other nonzero path flags are outside this context.

Fingerprint or malformed source failures fail the invocation with nonzero status
and sanitized `CARDINAL_INVOCATION_FAILED`; they are not successful Unknown evidence.
No absent original file can produce an admitted row.

## Complete contributor proof and ordered components

All relevant evidence is evaluated before composition, even if an unconditional
blocker exists. The original mode projects existing production terrain inspections
and checks full loader/ordinary-static inspection agreement; it does not change their
contracts. Synthetic and original modes call the **same** research `Assess` seam.

| Component / classification | Required evidence and meaning |
| --- | --- |
| ACTOR / project tuple + source helpers | Exact kind15/current ART class2/non-polymorph-derived flags0; no blanket flags default. |
| FROM_TO_MASK/FACADE/NAME / source-inapplicable | Source tile data retained numerically but not queried by selected adjacent helper. A blocking source tile does not reject departure. This is not proof of starting-cell reachability. |
| TO_MASK / serialized fact | Destination ordinal, word index/bit/word; set bit is unconditional blocker under this context. |
| TO_FACADE / packed fact | Facade class11 uses bit0; bit clear blocks, bit set is nonblocking. Non-facade is NotApplicable. No visual extent inference. |
| TO_NAME / selected resource fact | Existing bounded category/material name rule with reason/provenance. Missing/ambiguous entry, mapping or value retains gap; no all91 resolution. |
| REGISTRATION and EXTENT / source proof | The original's load assigns normalized location, inserts one node in the list head of the location's tile index (0x4D7090); exact-cell list queries that head. Visual offsets affect ordering, not neighboring admission. Inventory cannot be inserted; OFF/DESTROYED are query-excluded in non-editor context. Runtime registration changes are not performed. |
| CENSUS / original structural fact | Complete terminal count equals all unique source-index records. Survey every type, strict **unmasked** world location, flags and provenance before cell lookup. A filtered subset or `LocationCoverageComplete` alone is insufficient. |
| RECORD_n / original + source rule | Each record is considered in source-index order. Unlocated/outside-sector/unresolved flags/other types/dynamic facts create gaps even if hidden by a component subset. Direct slot18 overrides prototype; otherwise a resolved inherited default is required. OFF/DESTROYED/inventory are explicit exclusions, not NO_BLOCK. Non-destination scenery is not in source wall/portal mask. At destination, normal flags0 and NO_BLOCK bit0x400 clear imply unconditional Blocked; set implies component nonblocking. Same-cell multiplicity is never collapsed. |
| SOURCE_EDGES / source-proven serialized absence | Full admitted census contains no wall/portal type, so source mask3 has zero contributors. No guessed wall orientation, portal state, passwall or AI-open call. |
| DESTINATION_OTHER_TYPES / source-proven serialized absence | Full admitted census contains only scenery type3; destination mask0x3801F includes wall/portal/container/scenery/projectile/PC/NPC/trap. All non-scenery branches absent in these serialized inputs, not globally absent in the game. |
| SUPPLEMENTAL_REGISTRY / project diagnostic policy | Explicit zero supplemental registries, not a claim about installed game, savegame, map mobiles, spawns or mutable state. |
| ACTOR_SELF / project policy + source applicability | Actor notionally at source only; PC excluded from source mask3 and different from adjacent destination. This convention cannot hide another destination critter. |
| ROUTE_TRAP_FIRE_WINDOW / source-inapplicable | Normal route function separately checks spotted traps/fire/window indicator. Selected adjacent helper does not call those extra checks; any serialized trap still must be accounted under destination mask. No separate trap/fire absence fabricated. |
| SCRIPT_EXECUTION / source-inapplicable + project no-execution | No script callback in selected helper/tile/exact-cell scenery branch; eight tile scripts and 12 sector-script bytes are retained structural counts, not evidence of zero scripts or game safety. Other script-driven states are excluded inputs, not silently removed required contributors. |

Source proof is tied to the named pin and selected loaded base registry. Runtime
sector-lock failure/limits, state mutation, unavailable registrations/extents or
additional runtime inputs cannot be treated as permissive defaults. In particular,
missing registration/extent creates a gap and a scenery blocker cannot be certified
unconditional until its membership is proven.

## Composition, ownership and limitations

`Supported/Open` requires **all** component applicability/coverage resolved, no
coverage gaps and no blockers. Never infer it from summed component NoBlock or
an empty sprite subset. A supported applicable whole-context non-overridable blocker
may yield `Supported/Blocked` with unrelated coverage gaps **retained**. Examples
include destination mask, facade or admitted normal destination scenery. Otherwise
missing in-profile facts yield `Unknown/null`. AC3 measured samples require complete
admission and zero gaps, not this Blocked shortcut.

The assessor does not short-circuit row retention. Output prints component and census
numeric identifiers, suppressing only repetitive off-destination scenery rows; all
full record rows remain in the owned assessment and retained count is printed.
It prints no original MES text/assets/bytes or local paths. Re-querying reverses,
return/finish and rejected/Blocked requests does not mutate/advance snapshot or
context. This ownership check is not a movement simulation or permit lifetime API.

A destination scenery Blocked component is not certified as a whole-context
unconditional blocker if the full source-type census is missing or contains an
unadmitted portal/AI callback that could precede it. Such uncertainty stays Unknown;
tile blockers precede object callbacks and retain their independent unconditional
proof. Same-seam negative cases distinguish these two orders.

Diagonals/corners, dynamic doors/AI, actor engine, mutable permits, all-name resolution,
neighbor sectors and executable compatibility remain Unknown/Unsupported as above.
[Grid movement readiness](grid-movement-contract.md#real-traversal-readiness-frontier)
may cite this narrow evidence, but kinematics still cannot grant a real movement
permit without its own admitted integration/runtime contract.
