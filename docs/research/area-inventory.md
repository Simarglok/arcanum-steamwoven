# Crash Site area inventory

The [whole-exterior traversal research contract](../world/area-traversal-contract.md)
reconciles this full census without visual filtering or mobile deduplication;
loaded/live readiness remains NOT_READY.

## Scope and evidence vocabulary

ARC-RES-015 expands the [frozen initial sector](crash-site-framing.md) into a
reproducible **research area**, not a production loader, scene or gameplay implementation.

- **Source fact** means an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited; not original execution.
- **Local observation** means a bounded read of the identified unchanged installation.
- **Project policy** means this research selection or diagnostic convention, not a stored engine boundary.
- **Unknown / NOT_VERIFIED** remains separate from parsed, resource-resolved and numerically placed.

Code was independently authored after inspecting specification sources; earlier
provenance is in git history. The preserved toolkit manifest has SHA-256
`491b84d31ff83fa346833965d246031c3c8a96fd91502d70e70cfb9dd939cc29`;
its community tools are context, not executed or translated here. See the
[research method](method.md), [framing](crash-site-framing.md),
[objects](crash-site-objects.md), [placement](scenery-placement.md) and
[future area contract](../world/area-contract.md).

## Identified inputs

Local observation, SHA-256 of entire read-only inputs; source order is explicit,
**not** a resource precedence rule. The five archives are module, resource1,
resource2, resource3, resource4 respectively. Only resource2 supplies the visual
chains below; the other resources participate in the bounded script search.

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| Module | 355938305 | `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| Resource1 | 447470816 | `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576` |
| Resource2 | 147991122 | `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` |
| Resource3 | 9605011 | `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499` |
| Resource4 | 447614 | `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c` |
| Exterior game mobile aggregate | 1823280 | `a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5` |

Referenced prototypes are identified individually by numeric ID, byte count and
hash in the opt-in report. Directory names and filenames are not authentication.
Do not substitute a historical prototype aggregate digest: prior probes used
different serialization conventions (raw hash bytes versus textual hashes).
The per-file identities are authoritative. No original bytes, text, pixels or
personal paths are included in this document.

## Exterior selector and boundary uncertainty

Local observation: catalogue record 5000 is the start map, runtime map 1, start
`(92958,82592)`. Map properties ordinal 8023 preserves numeric map extent
`(128000,128000)`; it does not define a Crash Site polygon. Area record 2 gives
origin `(92960,82592)` without an explicit radius. The game's default area-radius
behavior is not used as a census rectangle.

Project policy: survey **all 621 sector entries** of the start map, collect the
sector townmap metadata equal to 2, and select its cardinally connected component
containing sector `(1452,1290)`. This baseline has exactly nine members, a full
3×3 component. The probe rejects a changed count, duplicate coordinate or member
outside this component's observed coordinate set. A separate independently framed
621-entry survey confirmed membership, hashes and object-window boundaries.

Coordinates are integer world tiles. Sector ID is `x + y * 67108864`; sector
coordinates are world coordinates divided by 64. Each bounds tuple below is
`(minX,minY,maxX,maxY)` with **half-open** maximums. Every core row is included
because of its townmap=2 membership in the start-containing component, not because
objects happen to be visible. The core union is `[92864,93056) × [82496,82688)`.

| Core sector ID | Sector (x,y) | World bounds | Module ordinal | Static records | Tile / facade cells |
| --- | --- | --- | ---: | ---: | --- |
| 86503327147 | (1451,1289) | (92864,82496,92928,82560) | 458 | 4 | 413 / 3683 |
| 86503327148 | (1452,1289) | (92928,82496,92992,82560) | 459 | 7 | 1102 / 2994 |
| 86503327149 | (1453,1289) | (92992,82496,93056,82560) | 460 | 0 | 161 / 3935 |
| 86570436011 | (1451,1290) | (92864,82560,92928,82624) | 462 | 15 | 1221 / 2875 |
| 86570436012 | (1452,1290) | (92928,82560,92992,82624) | 463 | 317 | 3033 / 1063 |
| 86570436013 | (1453,1290) | (92992,82560,93056,82624) | 464 | 31 | 1021 / 3075 |
| 86637544875 | (1451,1291) | (92864,82624,92928,82688) | 466 | 0 | 686 / 3410 |
| 86637544876 | (1452,1291) | (92928,82624,92992,82688) | 467 | 51 | 2377 / 1719 |
| 86637544877 | (1453,1291) | (92992,82624,93056,82688) | 468 | 1 | 333 / 3763 |

Project policy: retain a one-sector Chebyshev context ring (including corners),
not another part of the core. Its 16 coordinates give 11 present and 5 missing
entries. Each present row below is included **only for boundary context**. Bounds
are derived by the same 64-tile rule; all have 4096 cells, zero terminal static
objects, zero lights and zero roof arrays.

| Context sector ID | (x,y) | Ordinal | Tile / facade cells |
| --- | --- | ---: | --- |
| 86436218282 | (1450,1288) | 455 | 4038 / 58 |
| 86436218283 | (1451,1288) | 456 | 3582 / 514 |
| 86503327146 | (1450,1289) | 457 | 3529 / 567 |
| 86503327150 | (1454,1289) | 461 | 4096 / 0 |
| 86570436014 | (1454,1290) | 465 | 4096 / 0 |
| 86637544878 | (1454,1291) | 469 | 4096 / 0 |
| 86704653738 | (1450,1292) | 470 | 2486 / 1610 |
| 86704653739 | (1451,1292) | 471 | 1579 / 2517 |
| 86704653740 | (1452,1292) | 472 | 1726 / 2370 |
| 86704653741 | (1453,1292) | 473 | 4096 / 0 |
| 86704653742 | (1454,1292) | 474 | 4096 / 0 |

Missing context coordinates are `(1452,1288)`, `(1453,1288)`, `(1454,1288)`,
`(1450,1290)`, `(1450,1291)`. Missing is not empty terrain and not a blocking
edge. No geometric gameplay ingress/egress or passable outer boundary has been
established by this census. The start point and source-level cave endpoints below
are identified; traversal to surrounding country remains NOT_VERIFIED.

**Owner question:** should the eventual exterior scene use this metadata-complete
core, expose the context ring as scenery, or extend along an independently surveyed
travel route? Townmap membership is not a stored named polygon or proof that all
player-traversable terrain lies inside the core. The entire map directory and all
map mobiles are expressly not equated with Crash Site.

## Reconciled layer and class census

Local observation; every sector was framed independently, not by copying the base
sector's offsets. Parsing reaches the terminal count and exact object-window EOF.
Resource-resolved means an identity-bound mapping/resource locator; it does not
mean a displayed sprite. Diagnostic placement means arithmetic only.

| Source/layer | Observed = parsed/classified | Resource result | Placement / remaining boundary |
| --- | ---: | --- | --- |
| Core terrain | 36864 = 10347 tile(class0) + 26517 facade(class11) | 36864 locators resolved; 0 issues | terrain metadata available; no area draw observation |
| Core sector objects | 426, all type3 scenery | 426 selected ART frames decoded; 0 unresolved/unsupported | 426 diagnostic rectangles; original scene placement/order/visibility NOT_VERIFIED |
| Core lights / roof arrays | 0 / 0 across all nine sectors | not applicable | absence in serialized layers, not an image inference |
| Core sector walls / portals | 0 type0 / 0 type1 | not applicable | source type census only |
| Context terrain | 45056 across 11 present sectors | 45056 locators resolved; 0 issues | context only; five missing sectors remain explicit |
| Context static/lights/roof | 0 / 0 / 0 | not applicable | not extrapolated outside the inspected ring |
| Exterior aggregate | 7399 complete v119 records | 88 core + 10 context + 7301 outside + 0 unknown locations | world-location partition, not map-name membership |
| Core mobile objects | 88, all parsed/classified with prototypes | 2 frame chains resolved + 86 unsupported visual branches; 0 unresolved | 2 diagnostic rectangles + 86 not-ready; no claim drawn |
| Context mobiles | 10 parsed/classified | 10 explicitly not resource-selected | outside core scope, retained rather than dropped |
| Outside mobiles | 7301 parsed/classified | not selected for area visuals | excluded by world location, retained in full correspondence |

The base regression remains **4096 terrain cells and 317 static records**, not an
area total. Core static current ART is inherited in 39 cases and direct in 387.
All 426 resolved static frames reference 32 distinct resource2 ART entries.
Vegetation is confirmed by packed scenery category plus the enum of the earlier source reading (F0): category1
large trees=13, category2 medium trees=25, category3 small trees=27, category5
plants=331. Remaining category counts are 6:5, 8:1, 9:3, 10:1, 11:2, 12:1,
13:8, 15:3, 16:4, 17:1, 20:1. Category20 alone does not prove an active connector.

| Core mobile type | Count | Visual readiness gap |
| --- | ---: | --- |
| 2 container | 2 | actual inherited-current-ART chains below |
| 5 weapon | 2 | class6 item disposition/state mapping not admitted |
| 7 armor | 3 | same class6 gap |
| 8 gold | 2 | same class6 gap |
| 9 food | 24 | same class6 gap |
| 13 written | 1 | same class6 gap; text/interaction not inspected |
| 14 generic item | 20 | same class6 gap |
| 16 NPC | 34 | classes2:11,12:13,13:10; appearance/state/direction/composition not admitted |

There are no type4 projectiles, type15 PCs, type17 traps, walls or portals in the
core mobile selection. NPC type is not a human/monster/species assertion. No
HumanFixed/chicken substitute is used. Inventory-flagged mobiles still belong to
the location census; those flags must be retained before deciding what to draw.
Passability, scripts, interactions, AI, animation and equipment composition are
NOT_VERIFIED, not consequences of resource resolution or sprite bounds.

### Normal-game mobile provenance

Source fact: the game distinguishes editor loose `.mob` loading from normal-game
aggregate loading and transforms a map selector into the game filename. Ordinary
ROT13 was not a successful selector; the observed game file was supplied explicitly
and authenticated by size/hash, not guessed from an editor path.

Local observation: a 16-byte leading GUID is followed by 7399 consecutively framed
v119 objects to exact EOF. The GUID is zero-valued and is **not authentication**.
The module has 7399 corresponding editor records (1823032 decoded bytes; largest
1660). Each editor record reaches its own EOF. An independent full-ID bijection
and field-wise content comparison found 3084 byte-equal pairs, 4315 byte-different
pairs, including 55 length differences. Zero unexplained differences remained.

Source-supported narrow research normalization excludes only runtime sparse-array
bitset handles and trailing zero bitset-capacity words. It preserves ID/header,
field presence, element width/count, ordered sparse keys and complete values;
interior zero words, absence versus present-empty, nonzero suffixes and all other
bytes remain significant. Present arrays with zero storage words fail the FILE
loader of the earlier source reading (F0) and are rejected; one word is admitted. This is not raw identity and
not a general tolerance rule. Original hashes remain distinct. The aggregate is
counted once; loose correspondents are validation evidence, not extra area objects.

## Additional visual chains

Local observation independently checked the instance/prototype field tuples,
selected mapping/resource, complete ART frame boundary, RLE/RGBA digest and numeric
rectangle. These are diagnostic conventions, not original-renderer parity.
All five examples use physical group0/frame0/requested/effective palette0 and ART
frame offsets `(0,0)`. Static ART and offsets are direct; flags are inherited for
indexes5/20 and direct for316. Container ART is inherited; offsets and flags direct.
Absent numeric fields inherit the actual prototype value, not a guessed zero.

| Source key | Prototype | World (x,y) | Current ART | Mapping/resource ordinals in resource2 | Offset | Frame W×H; hotspot | Diagnostic top-left |
| --- | ---: | --- | ---: | --- | --- | --- | --- |
| sector86570436012/index5 | 4030 | (92931,82562) | 1089995072 | 3691 / 3452 | (-21,2) | 127×92; (66,61) | (-127,41) |
| sector86570436012/index20 | 4030 | (92931,82567) | 1089995072 | 3691 / 3452 | (0,-3) | 127×92; (66,61) | (94,136) |
| sector86570436012/index316 | 4027 | (92986,82623) | 1081606272 | 3691 / 3906 | (-1,11) | 215×260; (129,255) | (70,2176) |
| aggregate/index4453 | 3055 | (92999,82591) | 1882193984 | 34 / 22 | (12,6) | 64×47; (30,32) | (-1618,2014) |
| aggregate/index6536 | 3067 | (92947,82594) | 1904214080 | 34 / 42 | (-12,0) | 37×53; (18,44) | (570,1016) |

| Resource ordinal | Entry bytes | ART SHA-256 | Selected RGBA SHA-256 |
| ---: | ---: | --- | --- |
| 3452 | 7481 | `b27bb449b1a3cdff8dbadd5134212f597e8380916cfeefe7f622ad2b77b5f946` | `3dd26526b5ab3667c2cc28084fa40f04ca26de84afdf16b7ca622669e3a0576f` |
| 3906 | 17490 | `3e818f7836792cc0196413338219892ea21c8dbd8f7a8dd731382cf08e1a4879` | `ba6828c128a929591aa1f4331817fd36558ca849282fec1b5fc094537ca23d4c` |
| 22 | 5853 | `b2b916a3e2e567ef71ca00604316960846fef706281ecfd4dfe942857578c3c9` | `d12ed81c802a4eb0086dae25d4d2aefdfb8624d06adc09302ba04334f35797f8` |
| 42 | 3904 | `1874c4e3634ec9a2aac3ae0fa83551ee5f03575aba384e8743c88220f5abd781` | `bbe666e8899f1e146f44ea919e5180e76187d9753470daf02e60f2ab5dc62d84` |

Effective flags are 4197424 for static5/20, 4196352 for316, 16432 for both containers.
The independent private oracle checked **all** selected object field/default and
admitted-frame chains, not only this sample. Static locations use low six tile bits
rebased by their containing sector even when persisted high bits are nonzero;
mobile locations stay world coordinates. Projection uses the existing diagnostic
40/20 convention relative to `(92928,82560)`, then subtracts hotspot.

## Bounded cave endpoints and composition

Source fact plus local observation: catalogue5078/runtime79 is a separate map,
not a same-map interior. The catalogue sequence5000..5078 is present. Its one
sector is ID2080374816, `(32,31)`, half-open bounds `(2048,1984,2112,2048)`, module
ordinal10236. An effective object script on exterior sector86570436011/index7,
prototype4047, world `(92910,82598)`, SAP1, script2793 has a literal action44 at
line0/true targeting catalogue5078 at `(2087,2018)`. Cave sector/index99,
prototype4047, world `(2093,2019)`, SAP1, script1532 has line0/true action44 targeting
catalogue5000 at `(92913,82598)`. The game converts these script map IDs by subtracting4999
(ledger [TR1 of map transitions](ledger/world.md#map-transitions-arc-data-025)).
These are **directed potential endpoints**, not an executed round trip; attached
object position and destination position intentionally differ.

| Cave source/layer | Observed = parsed/classified | Resource/placement result |
| --- | ---: | --- |
| Terrain | 4096 class0 cells | 4096 locators resolved, no terrain issues |
| Sector objects | 138 = 125 type0 walls + 13 type3 scenery | 13 decoded diagnostic frames;125 wall-class visual mapping/state gaps |
| Serialized lights | 4 | framed numeric records; lighting behavior unsupported |
| Roof arrays / portals | 0 / 0 | absent in this sector/type census |
| Module editor mobiles | 39 = type2:4,5:6,6:2,8:1,9:4,14:15,16:7 | 3 container frames resolved;36 unsupported (28 item,class12:6,class13:1,one class7 physical-selection gap) |
| Jump lists | exterior ordinal8022 and cave ordinal10276: each count0 | no `.jmp` connector; not proof that no cave exists |

Cave game-aggregate provenance is NOT_VERIFIED; the 39 editor objects are a
separately labelled observed composition, not authenticated normal-game contents.
No cave transition or complete cave renderer is implemented.

The finite topology search includes effective instance/prototype object SAPs,
sector attachments, tile attachments, core/context exterior mobiles and the 39
cave editor mobiles. It scans 183 nonzero attachment occurrences, 47 distinct
attached IDs and their immediate literal call targets, both true/false actions.
48 direct script variants require 14487 stored / 55344 decoded bytes; with the
one-hop targets,55 unique script variants/68860 decoded bytes were framed. Script1324
has two variants; neither is silently chosen. Gaps retained: one ambiguous-source
entry,26 object-focus/SAP calls,18 script-change occurrences,4 depth/root-cycle stops.
SAP is not a script ID. Computed values, activation conditions, object focus,
entry-line reachability, scheduled calls and deeper call chains remain unexecuted.

Minimal next research, only if needed for transitions: authenticate the cave's
normal-game counterpart; validate the two SAP activations/object-focus conditions
and their destination tiles; resolve script1324 against an explicit source policy.
Do not search all game interiors or infer reachability from a literal operand.
The first three are answered by [cave transition research](cave-transition-evidence.md)
(ARC-WORLD-009): the cave aggregate is verified at T0, both actions are
unconditional use-triggered teleports of the triggerer, and both arrival cells
are terrain-passable. Script1324 stays open; it is not on the cave path.

## Reproduction and verification boundary

Use the pinned SDK from [toolchain](../workflow/toolchain.md). Public fixtures are
synthetic; all opt-in output should remain in ignored task-owned evidence.
The original installation stays external and read-only.

```sh
"$DOTNET" restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --area-self-test
python3 -B tools/research/area_topology_probe.py --self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- \
  --area-inventory --module "$MODULE" --prototype-root "$PROTOTYPES" \
  --resource "$RESOURCE2" --mobile "$GAME_MOBILE" > "$PRIVATE_AREA_REPORT"
python3 -B tools/research/area_topology_probe.py --inventory "$PRIVATE_AREA_REPORT" \
  --archive "$MODULE" --archive "$RESOURCE1" --archive "$RESOURCE2" \
  --archive "$RESOURCE3" --archive "$RESOURCE4" > "$PRIVATE_TOPOLOGY_REPORT"
```

The area probe authenticates module/resource2/aggregate before and after; every
prototype is fingerprinted before/after its bounded read. The topology probe
fingerprints all explicit archives before/after, preserves variant identities,
and reads only attached scripts and one-hop literal targets. Its output is a
private occurrence inventory, not a public dump of original script content.

Synthetic tests exercise literal record framing/EOF/truncation/caps, sparse handle
and zero-capacity equivalence with negative key/value/ID/presence cases, high-bit
static rebasing, half-open mobile boundaries and script/default inheritance.
Topology fixtures distinguish action operands from header/condition sentinels,
both branches, computed endpoints, one-hop calls and SAP/object-focus gaps.
Independent private probes frame all7399 pairs using the field declarations of the earlier source reading (F0),
not candidate schemas, prove the full621 selector and check selected field/default,
ART/RGBA and numeric placement results. Independent native review remains a
separate acceptance step. No unchanged GUI/lab rebuild, Godot export or original
executable observation is required for this data/source research.
