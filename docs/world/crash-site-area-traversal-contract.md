# Whole-exterior Crash Site adjacent-step research contract

Status: Research / Proposed. `AREA_TRAVERSAL_RESEARCH_V1` is a research format,
not a public Data API, movement permit, route search or executable admission.
Whole-area loaded/live readiness is **NOT_READY**. A visual polygon does not
establish physical permission. Original executable, graphical and controller
parity are NOT_VERIFIED. Existing [cardinal assessment](cardinal-traversal-assessment.md),
[cardinal contract](cardinal-traversal-contract.md), [grid contract](grid-movement-contract.md),
[grid runtime](grid-movement-runtime.md) and [passability](passability-contract.md)
retain their narrower policies without reinterpretation.

## Inputs and scope

Use the authenticated [DATA009 owner](crash-site-area-loader.md) and its
[area contract](crash-site-area-contract.md), reconciled against the independent
[RES015 inventory](../research/crash-site-area-inventory.md). The supplied source
plan remains FILE-effective; no save/patch/dynamic-source precedence is inferred.
The approved nine core sectors are 1451..1453 by 1289..1291, with a one-sector
context ring. Present context is evidence, not movement admission. Missing
context is not an empty loaded sector. Cave geometry/transition activation is
excluded.

The independent census is 621 surveyed sectors, nine core, eleven present and
five missing context; 36864 core and 45056 context terrain cells; 426 statics;
7399 aggregate mobiles (88 core, ten context, 7301 outside, zero unknown),
7399 editor correspondences (not additional occurrences), 56 prototypes and
2444 selected visual chains. The original experiment must reconcile individual
sector, terrain, object, direct/effective field and visual identities, not just
these totals. Null object IDs and coincident objects are distinct occurrences.
Offsets/frame bounds/alpha never replace exact-tile registration.

## Selected source and applicability

Prior art: CE Sustainable Use License v1.0, pin
[`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd).
This independently authored probe consumes existing project readers; it does
not copy/translate CE code. Versioned bounded references below describe source
claims, not original executable observations. Unread callees remain frontiers.

| ID | Pinned file and actually inspected range | Consequence |
| --- | --- | --- |
| S1 | [anim.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4776) 4737–4776,10454–10498 | Selected normal-ground `sub_425760`: final terrain with false caller suppression, then object constraints; opportunity depends on begin-move caller flags. |
| S2 | src/game/critter.c 1988–2032; src/game/ai.c 3725–3769 | PC portal capability; window capability from current ART; nonpolymorph human diagnostic is declared, not a recovered PC. Portal attempt needs script/lock state. |
| S3 | src/game/location.c 350–459; src/game/tile.c 130–219; src/game/tile_block.c 311–355 | Eight semantic deltas; final mask/facade/name, once. Failed sector/resource lookup never establishes Open here. |
| S4 | src/game/object.c 2962–3283 | Source wall/portal list; destination type mask `0x3801F` admits 0..4,15..17, excludes ordinary item types 5..14. Critters need current HP, not NO_BLOCK. |
| S5 | src/game/object.c 2997–3026 | Four ordered object constraints for diagonals, preserving original direction; no shoulder terrain substitution. |
| S6 | src/game/object.c 253–284,3304–3394 | Noneditor destroyed/off filter, exact tile lists; requested masks force sector loading. DONTDRAW differs from exclusion. |
| S7 | src/game/sector_object_list.c 115–186,458–504,532–641; src/game/tile.c 134–143 | Static low-six-bit rebase, inventory insertion refusal, mobile fold; ordering is not sprite extent. |
| S8 | src/game/object.c 1755–1799,4151–4240; src/game/ai.c 3048–3107; src/game/map.c 1360–1579; src/game/sector.c 1690–1774 | Dynamic/static predicates and fold hooks; save/diff/load state may alter registry. |
| S9 | src/game/critter.c 608–647; src/game/object.c 1632–1677 | HP = adjusted/derived/effect maximum minus damage; absent effects are not zero. |
| S10 | src/game/sector.c 844–1008,1390–1519; src/game/map.c 1360–1579 | Loaded neighbor/cache/source policy is not a copied immutable FILE set. |
| S11 | [path.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L70-L171) 70–171,239–340 | PATH_FLAG_0x1000 selects `sub_420E30` when the earlier attempt fails; that unclosed branch is unavailable, never normal-ground fallback. |
| S12 | src/game/obj.c 1189–1205; src/game/wallcheck.c 933–939 | Postprocess resolves ID handles; `sub_438530` dirties local-PC wallcheck. These bounded definitions do not close all fold hooks/stat/effect/registry state. |
| S13 | [art.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L17-L27) 17–27,761–802,1647–1710; src/game/portal.c 130–201; src/game/obj_flags.h 1–80 | Rotation shift11, wall piece shift14 and named flags/art accessors; synthetic exact-edge cases, no sprite-bound inference. |

Other table file/range pairs are relative to the linked immutable source pin,
not the current branch. Bounds name inspected ranges, not proof of every callee.

Semantic direction codes are `XMinusYMinus`=0, `XMinus`=1,
`XMinusYPlus`=2, `YPlus`=3, `XPlusYPlus`=4, `XPlus`=5,
`XPlusYMinus`=6, `YMinus`=7. They are not screen/ART/camera directions.
For diagonal D, constraints are source→CCW shoulder, CCW shoulder→final,
source→CW shoulder, CW shoulder→final, with D preserved as original direction.
Source and destination edge-wall orientation differ by four. Window pieces
10/13/14/17/18/19 block diagonals even for a jumping actor; opening pieces
22/25/26/29/30/31/32 do not. Solid wall passwall and portal damage/open/script
facts require their own evidence. The experiment does not implement doors.

## Coverage and readiness

| Contributor | Evidence and applicability | Minimal remaining dependency |
| --- | --- | --- |
| Mask/name/facade | Actual `TerrainObstructionInspector.InspectSector` then report `GetCell`; copied components, selected entry and payload bound | Missing mask/MES/material remains Unknown; NoBlock is component-only |
| Static scenery/container/projectile/trap | All occurrences and flags retained; type-mask branch and exact rebased cell | Positive complete post-load list/registry closure before an object blocker or Open |
| Dynamic scenery/container | FILE location/flags only, never no-AI absence | Fold membership, source overlays and relevant lifecycle hooks |
| NPC/PC/creature | Destination critter branch, all records retained | Registry plus current HP with stat/effect/load closure; no dead-ART/NO_BLOCK proxy |
| Ordinary item 5..14 | Destination component NotApplicable under S4; direct/inherited/inventory facts retained | Does not prove other callers, erase an occurrence or establish whole Open |
| Walls/portals/windows | Absent only in the serialized approved census; synthetic orientation cases | Current ART, spell/passwall/portal/lock/script/AI facts when introduced |
| Roof/lights/decorative layers | Inventory evidence, no consumed extent predicate in this selected caller | No blanket claim of irrelevance to every caller |

`FileEffectiveInitial` means persisted data plus inheritance. `DeclaredDiagnosticOverlay`
means individually `ProjectDeclared` facts; actor kind15/current ART class2,
flags0/nonpolymorph/ground follow WORLD003's named diagnostic, not a recovered
PC or declared absent/dead NPCs. `LoadedStateObserved` is reserved for actual
independent loaded-state observation and is not emitted by this experiment.
Each numeric fact is SourceConfirmed, InputObserved, ProjectDeclared or Unknown.

Validation of binding, actor, map, shape, adjacency, direction, checked arithmetic
and scope precedes blocker shortcuts. Malformed/authentication/cap/cancellation
failures are sanitized invocation errors; no partial authenticated report.
Same-cell/nonneighbor/outside relations are Unsupported/null. Missing neighbor,
registry, state or source is Unknown/null and NOT_READY. NotApplicable is only a
component applicability with null disposition. Open requires positive closure of
all applicable dependencies. An unconditional **final terrain** blocker can be
Supported/Blocked while retaining unrelated gaps and NOT_READY; an object blocker
cannot bypass list registration closure. No boolean/truthy conversion is offered.

## Canonical positional encoding

The following encoding is specified before its implementation. SHA256 over compact
UTF-8 JSON **arrays** only, no BOM/whitespace/object members. Strings are printable
ASCII, escaping only quote and backslash. Tokens are null, true/false or base-ten
integers (no exponent/float/negative zero). Enum codes are the explicit canonical
names below, not integer ordinals. Unsigned ART/entry values and sparse words are
uint32; scalar/world/object-offset/input-length/generation numbers are int64;
map/type/slot/index/object-length/view/frame/section values are int32. Persisted
ART/flags/spell scalar slots retain signed int32 representation before checked
bit interpretation. Null differs from zero and `[]`.
Presence and origin precede value; absent slots remain absent (never default zero).

Stable codes: role `Module/Resource1/Resource2/NormalGameMobile/Prototype`;
scope `Core/Context/Outside/Unknown`; kind `Terrain/Static/Mobile`;
layer `Body/Foreground/Background/Underlay`; presence `Absent/Null/Value/Empty/Array`;
origin `Instance/Prototype`; occurrence disposition `BodyEligibleConditionally`,
`ExteriorSectorInsertionRefusedByInventory`, `BodySuppressedBySerializedState`,
`ContextOnly`, `MissingMandatoryFact`. Coverage is `Supported/Unknown/Unsupported`;
applicability `Applicable/NotApplicable/Unknown`; terrain component disposition
`NoBlock/Blocked/null` (never whole-step Open), result `Open/Blocked/null`.
Input kind is `Structural/AuthenticatedOriginal`; provenance and reason codes
are the exact copied public component strings, not inferred enum ordinals.
Requested scope is `CoreWithContextEvidence` or the explicit
`Path0x1000Unavailable` branch. Other branches are invocation errors.

Notation `[...]` is an array, `records[]` a nested array of record arrays:

* Input: `[role,number,length,sha256]`, sorted by role ASCII then number.
* Entry: `[role,number,ordinal,flags,stored,decoded,offset,nameSha256]` or null.
* Sector: `[map,packed,sourceSha256,scope,present,ordinal,townmap,lightCount,roof,
  placeholder,terminal,sections[],lights[],entry,payloadSha256]`; section
  `[start,length]`, light array of signed int32. Sorted map/packed/source.
* Object key: `[kind,map,sectorOrNull,index,offset,sourceSha256,ownIdSha256,length]`.
  Sort these fields in this order, sector null first; strings ordinal ASCII.
* Field: `[slot,presence,origin,numberOrNull,elements[],fingerprintOrNull]`;
  sparse element `[key,words[]]`, ascending unique keys; fields ascending slot.
* Occurrence: `[key,version,type,prototypeNumber,parentSha256,prototypeOwnSha256,
  rawSha256,semanticSha256,scope,serializedLocation,worldX,worldY,disposition,
  directFields[],effectiveFields[],schemaReady,placementReady,layersReady]`.
* Terrain: `[map,sector,sourceSha256,index,localX,localY,worldX,worldY,rawArt,lookupArt,
  scope,selectedVisualKey]`, sorted sector/index. Visual key
  `[kind,map,sector,sourceSha256,index,ownIdSha256,layer,sparseIndex,objectKeyOrNull]`.
* Visual: `[key,rawArt,lookupArt,entry,payloadSha256,viewLength,nominalFacing,
  sourceGroup,physicalGroup,frame,requestedPalette,effectivePalette,mirrored,width,
  height,hotX,hotY,artOffsetX,artOffsetY,objectOffsetX,objectOffsetY,scale,encodedBytes,
  rgbaBytes,rgbaSha256,policy]`, sorted canonical key encoding.
* Component: `[cell,kind,coverage,applicability,dispositionOrNull,reason,
  provenanceKind,sourceId,ordinalOrNull,classification,rawArtOrNull]`; cell
  `[map,x,y]`; order Mask,Facade,Name. Raw ART comes from the owner, never a
  second parser; mask words/name text remain encapsulated by the component API.
* Reconciliation: `[survey,coreSectors,contextPresent,contextMissing,coreTerrain,
  contextTerrain,statics,aggregate,coreMobiles,contextMobiles,outsideMobiles,
  unknownMobiles,editorCorrespondence,prototypes,visuals]`.
* Selected evidence: `["AREA_TRAVERSAL_RESEARCH_V1","SELECTED_EVIDENCE",1,
  ruleId,sourcePin,inputKind,catalogueMap,runtimeMap,profile,inputs[],sectors[],
  occurrences[],terrain[],visuals[],reconciliation,components[],nameBinding,authorizedScope[]]`.
  Name binding `[entry,payloadSha256]` or null. Components sort cell x/y then kind.
  Authorized scope is `[packedSector,scope]` sorted by packed sector, unique
  Core/Context entries. This explicit geometric selection is independent of
  supplied sector metadata: omitted and Present=false admitted neighbors both
  retain missing-sector gaps, not outside-scope results. Original scope comes
  from the approved owner selection; synthetic scope is ProjectDeclared.
* Actor context: `["AREA_TRAVERSAL_RESEARCH_V1","ACTOR_CONTEXT",1,caller,chart,
  actorId,kind,currentArt,spellFlags,ground,stateMode,classification,profile,
  registryPolicy,stateToken,registryClosed]`. Different state or actor has a different digest.
  `registryClosed` is an explicit ProjectDeclared structural-fixture boolean,
  never true for AuthenticatedOriginal. There is no unbound closure override.
* Binding (report/query): `[schema,ruleId,sourcePin,inputKind,map,profile,
  selectedEvidenceDigest,actorContextDigest,generation]`. Generation is explicit
  caller-owned integer, not time/randomness and not part of selected input digest.
* Query: `[binding,sourceCell,destinationCell,direction,requestedScope]`.
* Result: `[query,coverage,dispositionOrNull,reason,completeCoverage,readiness,
  preconditions[],components[],recordRows[],legs[],gaps[]]`.
* Record row: `[role,objectKey,coverage,applicability,dispositionOrNull,reason,
  classification]`; all matching occurrences retained including exclusions.
* Leg: `[role,sourceCell,destinationCell,direction,originalDirection,recordRows[],gaps[]]`
  in the four semantic order above (cardinal has one `Adjacent` leg).
* Gap: `[code,cellOrNull,objectKeyOrNull,slotOrNull,dependency,nextPrerequisite,classification]`.
  Repeated role-specific gaps are retained, not collapsed into a boolean.

The human-readable report may use JSON objects. Digests NEVER hash that renderer.
Literal minimal payload fixtures, computed independently with Python hashlib,
are embedded in the self-test and documented with their digests after execution.

The ACTOR_CONTEXT fixture is exactly:

```json
["AREA_TRAVERSAL_RESEARCH_V1","ACTOR_CONTEXT",1,"sub_425760","WORLD_CELL_CENTERS_V1","HUMAN_FIXED_DIAGNOSTIC",15,536870912,0,true,"DeclaredDiagnosticOverlay","ProjectDeclared","fixture","NONEDITOR_EXACT_TILE","state-1",false]
```

SHA256: `9dfa1a228beb1f04a35bf03da67f70e135a2ed0365f57517a8b8eeaeab68cce7`.
The empty structural SELECTED_EVIDENCE **encoding-only** fixture is exactly:

```json
["AREA_TRAVERSAL_RESEARCH_V1","SELECTED_EVIDENCE",1,"S1_NORMAL_GROUND_FILE_FRONTIER_V1","a7ff41b300ef712f0e7d088183a3d08957110cdd","Structural",1,1,"fixture",[],[],[],[],[],[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],[],null,[]]
```

SHA256: `5491cd236bfd9a2e8424643da97f4928e30cce2d90ee1bb027bf4b31bfee5ade`.
It is not a valid original area/registration declaration. Field payload
`[18,"Value","Instance",0,[],null]` hashes to
`df80171b2be70e88bf4c1446cb75eaa8ee9bb47dff745bd12fae482196e91f77`;
`[30,"Array","Prototype",null,[[2,[0,4294967295]],[9,[1,2]]],null]` hashes to
`24c4b5764e9b24c48a0d663d2abbd521767f732b81a00d477792bd8d13ca2934`.
Independent computation: Python standard-library `json.dumps(value,
separators=(',',':'),ensure_ascii=True)` then `hashlib.sha256(payload.encode('utf-8'))`.
Do not hash the newline in these rendered code blocks. The real synthetic owner
also exercises selected-evidence sorting and neighbor-state digest invalidation.

## Lifetime and invalidation

Reports own immutable copies of fields, sparse words and collections, no leased
stream/frame/mutable-state reference or lazy I/O. A query is I/O-free. Evidence
construction recomputes selected evidence from those exact owned copies of
the full snapshot, component rows, name binding and authorized scope, requiring
equality with the supplied binding. Actor/context digest includes structural
registry closure. Reusing old bindings with changed evidence is rejected;
detached historical queries continue to assess only their immutable old evidence.
Missing/partial/invalid placement is a construction error, never an omitted
occurrence. Static source-sector/map/scope/source identity and low-bit rebased
location must agree; mobiles use their serialized world location. A located
schema-unready occurrence retains an explicit completeness gap; visual-layer
readiness is retained but is not a traversal exclusion predicate.
Source owner disposal cannot rewrite the historical report. New neighbor/source/state,
location/flags/HP/capability/chart/profile/rule changes invalidate derived binding;
a stale generation/context/digest is an invocation error. Identical old inputs
are historical evidence, not permission for new state. Runtime segment
freeze/commit/return behavior is unchanged.

The extra terrain reads use separate read-only archive handles, not externally
used owner leases. Match each selected sector entry, payload, source identity,
origin and copied raw terrain against the owner snapshot. Guard every original
input and oracle before and after ALL extra reads and before authenticated output.
No partial authenticated output on late mutation. Structural fixtures remain
Structural even if a caller supplies original-looking numbers.

## Experiment commands and delivery boundary

Build with the [canonical toolchain](../workflow/toolchain.md). The research CLI
requires every input explicitly, in this order; placeholders are not defaults:

```sh
"$DOTNET" restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --area-traversal-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --area-traversal-research \
  --module MODULE --resource1 RESOURCE1 --resource2 RESOURCE2 --mobile MOBILE \
  --prototype-root PROTOTYPES --oracle ORACLE
```

One serialized whole-area load, existing caps unchanged; no pixels/payload/text or
private paths printed in research output. Original identities, exact commands,
head/run/time and logs remain private. Frozen production cardinal and WORLD003
BoundedAdjacent checks run separately: they are NOT area-profile permission.
The experiment attempts approved x/y borders, shared corner, missing context,
same/nonneighbor/outside/direction/path cases and actual container/NPC/item cells.
Research completion does not imply loaded/live readiness.

## Observed research evidence and minimal frontier

The actual authenticated experiment reconciled the above census and retained
524 occurrence contributors, including 426 static scenery, 88 core and ten
context mobiles. Core mobiles were type2:2, type5:2, type7:3, type8:2, type9:24,
type13:1, type14:20, type16:34; context had type7:1 and type16:9.
The identity-bound component census contains mask Unknown12288/NoBlock69624/
Blocked8; facade NotApplicable47767/NoBlock176/Blocked33977; name Unknown37539/
NotApplicable34153/NoBlock10228. All original inputs passed the extended final
identity guard. These are FILE/input facts, not loaded-engine parity.

| Actual attempted pair (world coordinates) | Research result / missing prerequisite |
| --- | --- |
| (92991,82561)→(92992,82561), reverse | Unknown/null, NOT_READY; observe both post-load exact-tile registries (S6–S8/S10). No serialized object at either cell does not prove a complete loaded list. |
| (92934,82623)→(92934,82624), reverse | Unknown/null, NOT_READY; same registry frontier. |
| (92991,82623)→(92992,82624), reverse | Unknown/null, NOT_READY; all four ordered legs retained, two role-specific object rows and registry frontier. Final terrain inspected once. |
| Container destination (92999,82591) | Unknown/null, NOT_READY; selected record retained, registration/fold membership unobserved. |
| NPC destination (92968,82606) | Unknown/null, NOT_READY; current HP/derived-stat/effect closure (S9), plus registry. NO_BLOCK never substitutes for dead state. |
| Item destinations (93004,82591),(92957,82594),(92966,82604),(92934,82587),(92939,82609),(92961,82594) | Item component NotApplicable under S4; whole result Unknown/null because registry and, where coincident, NPC/state remain unresolved. All occurrences retained. |
| Core→missing context (92928,82496)→(92928,82495) | Unknown/null; neighbor sector, components and leg/list evidence missing. |
| Same-cell, nonneighbor, direction mismatch, outside | Unsupported/null, not a cardinal fallback. |
| PATH_FLAG_0x1000 request | Unknown/null, named unavailable caller branch; no S1 evaluation. |

Area-profile queries at frozen cells also remain Unknown/null due to registration
frontiers; they do not override the separate frozen 317-record policy. Its required
opt-in regression observes local6,1→5,1 Blocked/reverse Open and6,1→7,1 Open/reverse
Open, zero gaps. BoundedAdjacent's separate diagonal5,0/5,2 Blocked and7,0/7,2 Open
remain narrow-profile checks, not whole-exterior permission. Structural research
self-tests cover eight literal deltas, reverse asymmetry, windows/four diagonal
legs, missing dependencies, multiplicity, field inheritance/presence, original
flag masks, width/error checks, actual owner/terrain components, late mutation,
immutable lifetime and independent digest fixtures. Production owner malformed,
EOF/schema/cap/cancellation tests supply the complementary reader matrix.

Minimal future dependencies (descriptions, NOT task launches): close explicit
neighbor/list/load/HP/state frontiers; separately authorize immutable production
assessment and invalidation; separately authorize Simulation mapping/segment
integration; then separately validate presentation/control/admission behavior.
Affected requirement references, with full implementation status unchanged:
[FR-NAV-002](../requirements/world-travel-runtime.md#fr-nav-002),
[FR-NAV-003](../requirements/world-travel-runtime.md#fr-nav-003),
[FR-WORLD-002](../requirements/world.md#fr-world-002),
[FR-WORLD-003](../requirements/world.md#fr-world-003),
[FR-WORLD-004](../requirements/world.md#fr-world-004),
[FR-SCENE-012](../requirements/scene.md#fr-scene-012),
[FR-SCENE-013](../requirements/scene.md#fr-scene-013),
[FR-SCENE-014](../requirements/scene.md#fr-scene-014),
[FR-SCENE-016](../requirements/scene.md#fr-scene-016).
