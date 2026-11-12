# Bounded production cardinal traversal assessment

See the separate [whole-exterior research contract](area-traversal-contract.md)
for FILE-effective/loaded-registry frontiers. No production profile expands here.

## WORLD003 bounded-adjacent policy amendment

Both factories additionally accept optional `CardinalTraversalAdmissionPolicy
policy = CardinalTraversalAdmissionPolicy.CardinalOnly`. The unchanged
default retains `BASE_SECTOR_1452_1290` and `DIAGONAL_RULE_UNPROVED`. WORLD selects
`BoundedAdjacent`/`BASE_SECTOR_1452_1290_ADJACENT_V1`; immutable snapshot, assessment,
step evidence and runtime binding must agree on that policy. Final terrain is
queried once; `Assessment.Legs` and `StepEvidence.Legs` retain the four object-only
XShoulder/XFinal/YShoulder/YFinal constraints for map diagonals. Each leg owns full
records, components, exact from/to and leg/original directions; each gap carries
its leg role. Parent records remain final-destination records rather than flattened
duplicates. Open needs complete nonblocking closure; unconditional Blocked retains
all unrelated gaps. No query-time I/O, mutable permit, shoulder terrain or multi-step
path is introduced. Both endpoints and shoulders stay in x4..8/y0..3, with the
same isolated context; path0x1000 remains Unknown and never falls back.

Authenticated original observations at spawn `(6,1)` distinguish diagonals toward
`(5,0)`/`(5,2)` (Blocked by shoulder scenery index1) from `(7,2)`/`(7,0)` (Open),
all four legs retaining317 records and zero gaps. Reachable `(7,2)` provides Open
for all eight neighbors. These are narrow structural/diagnostic facts, not whole
zone reachability or original-executable parity. The unchanged cardinal API and
historical evidence below do not themselves prove the new input/rendering policy.

## Status, source and limits

ARC-DATA-008 implements the owned production API below in `Arcanum.Data.Traversal`,
independently authored against the bounded specification on this page
(the former research contract, retired; its live facts are below)
and [attributed research evidence](../research/cardinal-traversal-evidence.md).
Merged delivery: [PR #38](https://github.com/Simarglok/arcanum-steamwoven/pull/38)
implements the default cardinal API; [PR #44](https://github.com/Simarglok/arcanum-steamwoven/pull/44)
adds the bounded-adjacent amendment and WORLD003 consumer. Current audited scope
is recorded in the [component catalogue](../project/status.md). Historical
experiment details below retain their own source/context; this status update
is not a fresh runtime check.

Claims from an earlier source reading, cited by executable address, are evidence level
F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them.
No research implementation was ported, no clean-room or original parity claim.
Original executable behavior remains **NOT_VERIFIED**, an accepted non-goal.
General occupancy (see the [traversal research contract](area-traversal-contract.md)) remains NOT READY;
all historical 91 unresolved name classifications remain unchanged.

Both factories admit only profile `BASE_SECTOR_1452_1290`, logical base sector
`maps\\Arcanum1-024-fixed\\86570436012.sec`, sector `(1452,1290)`, packed ID
`86570436012`, origin `(92928,82560)`, 64×64. Query endpoints must BOTH be in
local rectangle **x=4..8, y=0..3**. Reading the full census grants no admission
elsewhere. No neighbor, `.dif`, PATCH or loose-file precedence is inferred.
Explicit selected `art\\tile\\tilename.mes` uses `NonWindowsMacOs`, and must
come from the same caller resource archive. Missing source/selection stays Unknown;
there is no suppression option for required destination name evidence.

Fixed diagnostic context: `ISOLATED_PC_CRITTER_ART_NONPOLYMORPH`, actor kind15,
current ART class2, spell flags0, path flags0, nonpolymorph, noneditor,
OrdinaryGround, zero supplemental registry count, actor notionally at source.
Derived traversal flags0 are source-proved OUTPUT, not a caller shortcut.
This is neither the rendered WORLD actor nor a recovered runtime actor.

## Fixed inputs and project declarations

**Original structural facts**: base sector `(1452,1290)`, origin `(92928,82560)`,
64×64; selector `maps\\Arcanum1-024-fixed\\86570436012.sec`, ordinal 463, flag 2,
decoded 47291. The module, resource and six prototype identities in the
[evidence document](../research/cardinal-traversal-evidence.md) are mandatory and
authenticated before and after use. No PATCH, `.dif`, loose-file precedence search,
fallback or alternate resource profile is admitted.

**Project policy**, not recovered original actor facts: one immutable context
`ISOLATED_PC_CRITTER_ART_NONPOLYMORPH` (diagnostic actor kind `OBJ_TYPE_PC` 15,
current ART class `TIG_ART_TYPE_CRITTER` 2, spell flags 0; ordinary ground,
non-editor, non-polymorph, no floating/passwall/special override). Supplemental
runtime registries are explicitly empty; the full selected serialized registry is
included, not a filtered sprite/scenery subset. The actor is notionally at the
request's source only. No door/AI-open, script, savegame, combat or spawn state is
executed. These declarations recover no human/equipment model and do not describe
the rendered WORLD monster. Each directed query has the same immutable predicate
context; no actor is moved to a potentially blocked start.

**Consequence of an earlier source reading** (F0), not a numeric shortcut: the
normal adjacent animation helper starts traversal flags 0 and ORs polymorph and
capability blocking bits; non-polymorph plus PC portal capability plus current
CRITTER ART window capability contribute 0, so the helper flags are 0 (adjacent
move begin 0x42E9B0, begin/run gate `0x400`). Path flags 0 select the current
path rotation, not the ART-rotation branch; the helper's tile argument `a2=false`
does not suppress tile blocking. This is not normal route admission/cost or success
of the animation scheduler.

**Project selection policy**: connected rectangle local x4..8/y0..3; `(6,1)->(5,1)`
and `(6,1)->(7,1)` plus reverses were admitted. The assessor holds no whitelist of
permissive cells. Leaving the rectangle is unsupported; y0 never authorizes a
neighbor sector.

## Complete contributor proof and ordered components

All relevant evidence is evaluated before composition, even when an unconditional
blocker exists. Synthetic and original modes call the same `Assess` seam; the
original mode projects existing terrain inspections and checks full loader and
ordinary-static agreement without changing their contracts. Per-contributor
classification:

| Component / classification | Required evidence and meaning |
| --- | --- |
| ACTOR / project tuple + source helpers | Exact kind 15, ART class 2, non-polymorph-derived flags 0; no blanket flags default |
| FROM_TO MASK/FACADE/NAME / source-inapplicable | Source tile data retained numerically but not queried by the helper; a blocking source tile does not reject departure (not proof of start reachability) |
| TO_MASK / serialized fact | Destination ordinal, word index/bit/word; a set bit is an unconditional blocker in this context |
| TO_FACADE / packed fact | Class 11 uses bit 0: clear blocks, set is nonblocking; non-facade NotApplicable; no visual extent inference |
| TO_NAME / selected resource fact | Bounded category/material name rule with reason/provenance; missing/ambiguous entry, mapping or value keeps a gap |
| REGISTRATION and EXTENT / source proof | Load normalizes the location and inserts one node at the head of the tile index list (0x4D7090); exact-cell queries read that head; visual offsets affect ordering, not neighbor admission; inventory cannot be inserted; OFF/DESTROYED are query-excluded (non-editor); no runtime registration change is performed |
| CENSUS / original structural fact | Terminal count equals all unique source-index records; every type, strict unmasked world location, flags and provenance surveyed before cell lookup; a filtered subset or `LocationCoverageComplete` alone is insufficient |
| RECORD_n / original + source rule | Each record in source-index order; unlocated/outside/unresolved flags/other types/dynamic facts create gaps even if hidden by a subset; direct slot 18 overrides the prototype, else a resolved inherited default is required; OFF/DESTROYED/inventory are explicit exclusions, not NO_BLOCK; at the destination, flags 0 with NO_BLOCK `0x400` clear is an unconditional Blocked, set is nonblocking; same-cell multiplicity never collapsed |
| SOURCE_EDGES / serialized absence | The admitted census has no wall/portal type, so the source mask has zero contributors; no guessed orientation, portal state, passwall or AI-open call |
| DESTINATION_OTHER_TYPES / serialized absence | The census holds only scenery type 3; destination mask `0x3801F` (wall/portal/container/scenery/projectile/PC/NPC/trap) other branches are absent in these inputs, not globally |
| SUPPLEMENTAL_REGISTRY / project policy | Explicit zero registries; no claim about the installed game, savegame, mobiles, spawns or mutable state |
| ACTOR_SELF / project policy + source | Actor at the source only; the PC is excluded from the source mask and differs from the destination; this cannot hide another destination critter |
| ROUTE_TRAP_FIRE_WINDOW / source-inapplicable | The normal route function checks spotted traps, fire and window indicator; the adjacent helper does not; a serialized trap is still accounted under the destination mask; no trap/fire absence fabricated |
| SCRIPT_EXECUTION / source-inapplicable + no-execution | No script callback in the helper/tile/exact-cell branch; 8 tile scripts and 12 sector-script bytes are structural counts, not evidence of zero scripts |

Source proof is tied to the named pin and the selected loaded base registry. Runtime
sector-lock failure or limits, state mutation, unavailable registrations/extents or
extra runtime inputs are never permissive defaults; missing registration/extent
creates a gap, and a scenery blocker cannot be certified unconditional until its
membership is proven. Output prints component and census numeric identifiers
(suppressing only repetitive off-destination scenery rows) but never original MES
text, assets, bytes or local paths; re-querying reverses, return/finish or rejected
requests does not mutate the snapshot or context (not a movement simulation).

## Actual API and ownership

```csharp
CardinalTraversalSnapshot CardinalTraversalInspector.InspectSector(
    StaticObjectSource source, StaticObjectPrototypeSource prototypes,
    string moduleSourceId, string resourceSourceId, string prototypeSourceId,
    TerrainSectorSelector selector, TerrainTileNameSource? tileNameSource,
    long evidenceEpoch, TerrainLoaderOptions? terrainOptions = null,
    StaticObjectLoaderOptions? staticOptions = null);
CardinalTraversalSnapshot CardinalTraversalInspector.ValidateOriginalData(
    string modulePath, string resourcePath, string prototypeDirectory,
    long evidenceEpoch, TerrainLoaderOptions? terrainOptions = null,
    StaticObjectLoaderOptions? staticOptions = null);
CardinalTraversalAssessment CardinalTraversalSnapshot.Assess(CardinalTraversalRequest request);
```

`StaticObject*` types are in `Arcanum.Data.StaticObjects`; `Terrain*` types are
in `Arcanum.Data.Terrain`. Context constructor:
`(string contextId,int actorKind,int currentArtClass,uint spellFlags,uint pathFlags,bool polymorph,bool editor,CardinalTraversalActorState actorState,int supplementalRegistryCount)`.
Request constructor:
`(string profileId,CardinalTraversalInputIdentity inputIdentity,long evidenceEpoch,int sectorX,int sectorY,CardinalTraversalCell from,CardinalTraversalCell to,CardinalTraversalDirection direction,CardinalTraversalContext context)`.
Use the successful snapshot's immutable identity and DeclaredContext explicitly.
Cell is a readonly local `(int X,int Y)` value, never silently world/screen coordinates.
Direction map deltas: 0=(-1,-1),1=(-1,0),2=(-1,+1),3=(0,+1),4=(+1,+1),
5=(+1,0),6=(+1,-1),7=(0,-1), not compass or physical ART group aliases.

Snapshot/identity/assessment/component/census/record/gap/provenance/step evidence
are sealed, get-only, internally constructed. Only Context and Request have public
value constructors (plus readonly Cell). No public report/permission-array factory,
attestation, `IsWalkable`, mutable epoch or Godot/Simulation dependency. Numeric
inputs are typed get-only Code/long Value rows, not object dictionaries.

Structural inspection uses existing DAT/sector/MES/v119/default readers and the
SAME assessor as original inspection. It records ACTUAL selector entry metadata,
not assumed original ordinal463/flag2/size47291. `Kind=Structural` and
`OriginalIdentity=null`; it never authenticates originals. The original factory
extends the existing loader's authenticated guard through the LAST terrain/MES,
full census, static projection and digest assembly read, then performs final full
module/resource/six prototype hash checks BEFORE returning. It does not validate
and unguardedly reopen. Original identities and loader aggregate are deeply owned.

Synthetic guard negatives call the same concrete final fingerprint verifier used
by this original path, around an actual structural snapshot factory/Build. An
unchanged control and independent late mutations of module, resource and each of
six required synthetic prototypes observe factory reads before final hash reads;
changed inputs must throw sanitized InputChanged/Fingerprint and return no snapshot.
No test-authored verification callback, public authentication bypass, original-game
mutation or platform-specific file-sharing assumption is used. Structural fixtures
remain Structural, never authenticated originals.

The snapshot owns bounded numeric terrain words/mask/name results and all terminal
records, no caller archive/source/options, raw name text, filesystem paths, ART
payloads or lazy source retention. Queries do no I/O, survive disposal/source
mutation and do not dispose caller archives. Existing terrain/static PUBLIC APIs
keep their separate component-only semantics. Common direct slot1 location and
slot18 flags for all delimited types come from the SAME existing parser; non3
EffectiveFlags remains null and unadmitted. Slot19 is NOT obstruction flags.
Direct0 overrides defaults. Strict signed unmasked locations are used, never the
old low-six-bit convenience coordinates. Visual offsets/ART absence cannot invent
membership or footprint. Selected original ART authentication remains unchanged.

Existing TerrainLoaderOptions and StaticObjectLoaderOptions bound source bytes,
MES, fields, prototypes and terminal records (4096 maximum). No new dependencies
or alternate DAT/PRO/MOB parser. Storage is bounded by4096 cells and capped records;
record lookup is indexed once, records are not duplicated per cell. Original files
are streamed read-only, not copied or retained as evidence assets.

`evidenceEpoch` is a nonnegative immutable long generation label allocated by the
integration owner, not a query counter/time/global monotonic service or Simulation
EvidenceRevision. Assess/reverse/return/finish never advances it. Deep identity
comparison allows equivalent reconstructed evidence; changed evidence or epoch
rejects. Context/epoch are separate from the selected evidence digest.

## Admission, failures and composition

Whole Coverage=Supported iff Disposition exists (Open or Blocked).
Unknown/Unsupported always have null disposition. Component Supported+Applicable
has disposition; Supported+NotApplicable has null; missing facts have Unknown/null.
Open for ONE component is not whole-step permission.

1. Validate profile, deep identity, epoch, sector, safe coordinate bounds/zone,
   relation/direction and COMPLETE context before any blocker shortcut.
2. In-zone matching diagonals are Unknown/null with DIAGONAL_RULE_UNPROVED.
   Otherwise matching path0x1000 is Unknown/null with PATH_BRANCH_UNAVAILABLE;
   both gaps survive when both apply. No shoulder/normal fallback composition.
3. Evaluate Actor; source Mask/Facade/Name; destination Mask/Facade/Name;
   Registration; Extent; Census; each record in source-index order; SourceEdges;
   DestinationOtherTypes; SupplementalRegistry; ActorSelf; RouteTrapFireWindow;
   ScriptExecution. Records are a separate full ordered collection, not collapsed.
4. Source tile rows retain numbers but are NotApplicable/null: the selected helper
   does not reject departure because the source has a blocking tile/scenery.
5. Terminal/census count, unique contiguous indices, strict EVERY-record locations,
   admitted type3/resolved flags/no dynamic branch and static/census agreement close
   exact-cell registration/extent/type absence. Missing/default/dynamic/non3/location
   facts remain separate ordered gaps, even for off-destination records. Inventory,
   destroyed and off exclusions are explicit only after required admission;
   dynamic cannot hide behind an exclusion.
6. A destination tile mask/facade/name blocker independently yields Supported/Blocked
   after request admission, retaining all gaps. A scenery blocker does so ONLY with
   full census/membership closure. Thus tile blockage survives hidden portal
   uncertainty; scenery alone with that uncertainty is Unknown. With full census,
   scenery Blocked may retain an unrelated name gap. No-gap nonblocking proof is Open.

All components, records, gaps, numeric inputs and provenance remain available even
when a blocker wins. `CompleteCoverage = Coverage==Supported && Gaps.Count==0`;
complete Blocked still prohibits moving. ScriptExecution is NotApplicable, not a
claim of zero scripts: originals retain8 tile scripts and12 sector-script bytes.

| Failure family | Classification |
|---|---|
| Null/lexical IDs/negative epoch or count/options shape | Argument-family construction failure, no assessment; IDs are [A-Za-z0-9_.-],1..128 |
| Authentication/input missing/changed, malformed archive/sector/record/selected MES, cap | CardinalTraversalInvocationException; no snapshot or successful Unknown |
| Well-shaped profile/identity/epoch/sector/context mismatch, zone/sector escape, extreme local coordinates, same/nonadjacent, invalid numeric Direction/ActorState, relation mismatch | Successful Unsupported/null BEFORE blockers; invalid coordinates have null world/ordinal |
| In-zone diagonal / exactly otherwise admitted path0x1000 | Unknown/null with both applicable branch gaps |
| Missing mask/name source/entry/mapping/material/value; non3/unresolved flags/location/dynamic/uncertain census | Unknown/null unless an independent admitted unconditional blocker applies; all gaps retained |

Invocation exception message is constant `CARDINAL_INVOCATION_FAILED`, no private
inner exception. Code values: InputUnavailable0,FingerprintMismatch1,InputChanged2,
InvalidSelection3,MalformedSource4,LimitExceeded5. Stage values:
Selection0,Terrain1,Records2,Prototypes3,Fingerprint4. Optional SourceIndex/Offset
are safe numeric diagnostics. Lexical/semantic caller denials never echo private paths.

## Canonical evidence encoding V1 (project policy, not original wire format)

SHA256 of the following EXACT byte stream, lower-case64 hex text. Integer encodings
are little-endian signed int32 (coords/counts/indices/type/A-ID), int64 (packed
sector/location/file length), uint32 (raw words/flags/decoded sizes). A nullable
field/tuple/list starts with presence byte0/1, then its encoding when present.
Strings: uint32 ASCII byte length + exact bytes, no newline/NUL. Lists: uint32
item count then stated order. Boolean: byte0/1. Enum: one byte, explicit codes.

SelectedEvidenceSha256 field order:

1. String `CARDINAL_EVIDENCE_V1`; input-kind byte (Structural0,AuthenticatedOriginal1);
   module/resource/prototype source-ID strings.
2. Sector X,Y,packedId,originX,Y,width,height,entryOrdinal,flags,decodedSize;
   path-profile byte0 (NonWindowsMacOs).
3. Nullable selected-name-entry TUPLE (ordinal,flags,decodedSize).
4. Nullable original-identity TUPLE: module length/SHA string; resource length/SHA;
   ascending prototype list (A-ID,length,SHA); loader aggregate SHA string.
5. Tile word list in ordinal order; nullable block-word LIST in word order.
6. Name-result list in ordinal order. EACH row: nullable int32 CategoryBits,
   NameCategoryIndex,MaterialOrdinal,MesKey (in that order); coverage byte;
   applicability byte; nullable disposition byte; reason string; source-ID string;
   nullable entry ordinal. Missing name table produces explicit unresolved rows,
   not an absent list. CategoryBits3 is NOT NameCategoryIndex0; selected keys follow
   sorted sparse MES ordinal, never category base+material. Non-class0 numeric
   category/material/key are unavailable, not invented.
7. Terminal int32 count; source-index ordered census list. EACH row: index,type;
   nullable A-ID,packed DIRECT location,strict worldX,Y,localX,Y,DirectFlags,
   EffectiveFlags; flags-source byte; location-status byte.
8. Tile-script int32 count; sector-script int32 byte count.

Coverage codes Supported0,Unknown1,Unsupported2; applicability Applicable0,
NotApplicable1,Unknown2; disposition Open0,Blocked1. Name component unsupported
in-profile coverage is encoded Unknown1. FlagsSource follows existing
StaticObstructionFlagsSource: Unresolved0,Direct1,Prototype2. LocationStatus:
InRange0,Absent1,OutOfRange2,Overflow3.

ContextFingerprint: string `CARDINAL_CONTEXT_V1`,contextId string,actorKind int32,
currentArtClass int32,spellFlags uint32,pathFlags uint32,polymorph byte,editor byte,
actor-state byte (OrdinaryGround0,Floating1,Passwall2,SpecialOverride3),supplemental
int32 count. Invalid numeric actor enums reach Unsupported; their diagnostic byte
is unchecked conversion, so a digest is NEVER a credential or replacement for
complete semantic comparison. Neither digest includes epoch. Selected digest also
excludes context, raw name/path text and ART visual metadata.

These safe-projection digests are not original file authentication or permission.
They do NOT replace the loader's ID:SHA aggregate
`00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6`
or the historical research ID:length:SHA aggregate
`684e04f305cef5e0c89caabee852728b33bfb85d4a7e6b2f509505815c080342`.
Tests encode V1 independently from literal byte fixtures, including direct/absent
flags and sparse numeric key distinction.

## Executed production examples and reproduction

New opt-in CardinalTraversalOriginalDataTests calls production ValidateOriginalData
and Assess and independently hashes FULL module/resource/six individual prototypes
before construction and after ALL queries. Missing variables FAIL, not skip.
All normal original rows have all317/terminal317/type3=317/strict locations317,
direct31/inherited286,293 distinct cells,22 multi-record cells,max4,zero gaps.

| From → To (local y1) | Direction | Ordinals | Production result | Destination records |
|---|---:|---|---|---|
| (6,1)→(5,1) | 1 |70→69|Supported/Blocked|1: direct4196352/ref4027 (NOT a seventh default input)|
| (5,1)→(6,1) | 5 |69→70|Supported/Open|BOTH2/3: inherited4197424/default4030|
| (6,1)→(7,1) | 5 |70→71|Supported/Open|exact full-registry absence|
| (7,1)→(6,1) | 1 |71→70|Supported/Open|BOTH2/3 again|

World x=92933/92934/92935,y82561; destination raw70592/79809/111553,
mask word2,bits5/6/7,value0,class0,CategoryBits3,NameCategoryIndex0,
material0,key0,selected name entry5353. Sources/prototypes/individual identities
remain those in the [merged evidence](../research/cardinal-traversal-evidence.md),
with no assets/names retained in reports. This tests original DATA, not gameplay.

Use canonical SDK8.0.425 as in [toolchain](../workflow/toolchain.md). Configure
ARCANUM_STATIC_MODULE, ARCANUM_STATIC_RESOURCE, ARCANUM_STATIC_PROTOTYPES locally
and the same module/resource as ARCANUM_TERRAIN_MODULE/RESOURCE; no paths in Git.

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter 'TestCategory!=RealData'
python3 -B tools/research/dat_probe.py --self-test
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build --filter 'FullyQualifiedName~CardinalTraversal&TestCategory!=RealData'
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build --filter 'FullyQualifiedName~CardinalTraversal&TestCategory=RealData' --logger 'console;verbosity=detailed'
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build --filter 'FullyQualifiedName~StaticObjects&TestCategory=RealData'
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build --filter 'FullyQualifiedName~TerrainLoaderTests&TestCategory=RealData'
```

Local-only stage, CI NOT_REQUIRED by accepted policy; no GUI/export gate is
substituted by these headless checks. Exact-candidate logs/ledger stay task-local
and ignored; they are not embedded in assets or committed original data.

## WORLD003 binding and remaining work

StepEvidence shares immutable FULL Components/RecordAssessments/Gaps, carries
ProfileId,deep InputIdentity/SelectedEvidenceSha256,epoch,Context/ContextFingerprint,
Sector,From/To local and admitted world/ordinal,Direction,Coverage,Disposition,
ReasonCode and bounded provenance-ID summary. It is evidence, not a permit.
WORLD003 must accept the authenticated ORIGINAL binding, exact endpoints/direction,
actor association/capabilities/context/profile/epoch, then supply its OWN
opportunity/start/material-key/revision/positive duration contract. Diagnostic PC
context is not the rendered monster by convenience. Map enums deliberately;
Data epoch is not Simulation EvidenceRevision. Simulation summary caps cannot
justify discarding full317 records or gaps as a silent Open. See
[grid runtime](grid-movement-runtime.md) and [traversal research contract](area-traversal-contract.md).

Implemented: production assessment/factories, bounded existing-reader projections,
complete owned evidence, synthetic and read-only original-data gates. Not implemented:
movement, GUI, actor controller, route search, diagonals/doors/dynamics/new registries,
original executable parity or general occupancy. Remaining: independent native
exact-head review, separately authorized private publication, observed HUMAN merge
before WORLD003 release, and WORLD003's independently admitted adapter/runtime.
Review is not publication; publication is not merge.
