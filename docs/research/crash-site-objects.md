# Crash Site selected-sector objects

## Scope, source identities, and reproduction

This is ARC-RES-007's bounded experiment on the one frozen base-sector entry
already selected by `crash-site-slice.md`. It is not a general object format,
a production loader, a runtime comparison, or an archive-precedence policy.
The local original inputs were opened read-only, were fingerprinted before and
after each run, and no original bytes, paths, names, text, or images are kept
in this repository.

| Input | Selector / identity | Use and limit |
| --- | --- | --- |
| Module DAT | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`; selected sector ordinal 463, compressed flag `0x00000002`, decoded length 47,291 | Exact selected base sector only; no `.dif`, PATCH, or loose-file policy |
| Explicit resource DAT | 147,991,122 bytes; SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` | One caller-selected source for five ART metadata checks; no winner/preference claim |
| Explicit prototype directory | Six referenced canonical prototype files; aggregate SHA-256 `00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6` over sorted `six-digit ID:file SHA-256` pairs | Required only where an instance lacks current-ART and/or object flags; directory names and payloads are not reported |
| Project code | `DatArchive`, `ArtDecoder`, and `Arcanum.CrashSiteProbe` | Bounded read-only diagnostic tooling, not reusable loading API |

Use the pinned SDK and explicit caller-owned inputs:

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- \
  --module <explicit-module> --prototype-root <explicit-prototype-directory> --resource <explicit-resource>
```

The probe requires the two recorded full file fingerprints, caps each decoded
sector, prototype, mapping, and selected ART entry at 1 MiB, verifies all
prototype files individually before/after use, and verifies both DAT files
before/after use. It outputs only aggregates and selected numeric metadata.

## Source map and evidence classes

| Claim | Source attribution | Measured or synthetic evidence | Status |
| --- | --- | --- | --- |
| Object-list envelope obtains the terminal count from end-of-stream, reads that many objects from the saved cursor, then compares the final count | object-list load `objlist_load` (0x4F1350) | Selected window `17140..47287` and final word `317`; delimiter consumes exactly 317 records | source-supported plus measured |
| Object-ID type A is prototype-addressable, and one serialized ID is 24 bytes | the object ID layout | All referenced inherited records used type-A IDs and exactly six canonical local prototypes resolved | source-supported plus measured |
| Common fields begin with current ART and location; `OBJ_F_FLAGS` field ID 19 serializes at zero-based common slot 18 | the common object field order | v119 field-mask parser retains current ART, location, and slot-18 object flags; test-only fixtures independently write distinct sentinels at literal raw slots 18 and 19 | source-supported plus measured parser scope and independently framed synthetic evidence |
| Object type `3` is scenery; a static object excludes dynamic flags after type eligibility | static test `object_is_static` (0x43D990); the `OF_DYNAMIC` flag bit | 317 parsed type-3 records all had an effective slot-18 object-flags value without `0x00002000`; direct and inherited fixtures independently write reversed static/dynamic sentinels at literal raw slots 18 and 19 | source-supported plus measured |
| Current ART is used by the object-list art precache | object-list art precache `objlist_precache_art` (0x4F1F00) | Instance override or prototype default resolved for all 317 records | source-supported plus measured |
| Scenery ART lookup key and path family derive from scenery category/number | path resolver `name_resolve_path` (0x41DAE0); the ART path builder (earlier source reading) | Five exact selected metadata chains resolved uniquely in the explicit resource archive | source-supported plus measured |
| ART type/frame/palette and physical-frame metadata | the ART header and frame functions (earlier source reading); project `ArtDecoder` | Five selected group/frame/palette values passed physical-table bounds and metadata inspection | source-supported plus measured |
| Coordinate rebasing happens after object read | object-list load `objlist_load` (0x4F1350) | Local source coordinates are reported below only; no global/runtime placement is claimed | source-supported; placement remains open |

The attributions above are an earlier source reading (provenance in git
history), cited by the executable address of each function; level F0 (not
checked in the executable). This note makes no clean-room or
runtime-parity claim.

## Record envelope: admitted fields and deliberate omissions

The probe accepts only object version `119` in this one already-framed window.
For each terminal-count-controlled record it reads: version; two 24-byte object
IDs; a concrete type in `0..17`; a signed field count; the type-specific valid
mask words; and exactly those mask-selected wire values necessary to continue.
It rejects an unknown version, invalid/reserved mask bit, mismatched field
count/popcount, invalid optional presence byte, malformed array header/bitset,
missing string terminator, truncation, trailing bytes after record 317, or an
unsupported type. These are delimiter and selected-field observations, not a
claim that every skipped field has a known semantic interpretation.

| Admitted field | Selection rule | Evidence-backed use | Not claimed |
| --- | --- | --- | --- |
| Prototype ID | First record ObjectID; required type-A only if an inherited value needs a local prototype | Exact reference to a canonical six-digit prototype file | Object identity beyond this resolution edge |
| Object type | Record type integer | All 317 values equal `3`; source attribution labels this scenery | A universal type catalog |
| Current ART | Common field index 0, direct when present; otherwise selected prototype default | Resolves 288 direct and 29 inherited values | That prototype and instance values normally coincide |
| Local location | Common optional field index 1 | Each selected handoff example carries source-local `x,y` | Global coordinate, offsets, baseline, hitbox, or visual anchor |
| Object flags | `OBJ_F_FLAGS` field ID 19, serialized as zero-based common slot 18; direct when present, otherwise selected prototype default | Checked only for `OF_DYNAMIC` in the static subset test; synthetic slot-18/slot-19 values differ | Meaning or handling of every other bit |
| Other masked fields | Wire-kind bounded skip | Safe traversal to the next record | Their names, meanings, or loader representation |

## Reproduced result and reconciliation

The predecessor framing result remains intact: the selected sector has zero
lights; its intermediate sections end at `17140`; its terminal count is at
`47287..47291`; and the object window is 30,147 bytes. The first object word is
v119.

| Account | Count | Basis |
| --- | ---: | --- |
| Terminal count / delimited records | 317 | Final LE count and complete no-trailing-byte v119 traversal |
| Type-3 records | 317 | Parsed record type, not an inference from the terminal count |
| Current-ART direct overrides | 288 | Present instance field 0 |
| Current-ART prototype defaults | 29 | Missing instance field 0, one validated matching prototype each |
| Object-flags direct overrides | 31 | Present instance slot 18 |
| Object-flags prototype defaults | 286 | Missing instance slot 18, one validated matching prototype each |
| Referenced prototype files | 6 | Unique canonical type-A references needed for current-ART and/or object-flags inheritance |
| Type-3 records passing the bounded static predicate | 317 | Type eligibility plus effective flags with `OF_DYNAMIC` clear |
| Unsupported or unresolved records in this frozen window | 0 | No record was silently skipped or widened to force the count |

“Static” here is the narrowly sourced `object_is_static` predicate, not a claim
about draw order, collision, navigation, gameplay, placement baseline, or
visual visibility.

## Current-ART / prototype / ART chain

The proved chain is deliberately explicit and fail-closed:

```text
v119 instance record
  -> current ART override when field 0 is present
  -> otherwise exactly one type-matching canonical prototype default
  -> packed ART fields: type=4, category, number, rotation group, frame, palette
  -> exact key in the explicit scenery mapping
  -> exactly one normalized logical ART entry in the explicit resource DAT
  -> ArtDecoder.Inspect physical group/frame bounds and frame metadata
```

A missing/duplicated prototype or resource entry, unlocatable ID, mismatching
prototype type/ID, invalid mapping, unsafe relative filename, non-scenery ART
type, or out-of-table group/frame aborts rather than selecting a fallback.
Resource matching uses the documented macOS normalization
(lowercase ASCII and slash normalization) within the one explicit archive only.
No archive, PATCH, or loose-file precedence follows from that observation.

## ARC-RES-008 handoff: five finite source examples

These are source-indexed examples rather than names or images. `local` is the
serialized sector-local coordinate retained by the probe; `hotspot` is ART
frame metadata, **not** an established world placement offset. All selected
ART values use group 0, frame 0, palette 0, and were resolved from the exact
explicit resource source above.

| Source index | Prototype A ID | Local | Current-ART source | ART ID | Category / number | Frame metadata (`width x height`, hotspot) |
| ---: | ---: | --- | --- | --- | --- | --- |
| 0 | 4030 | `(50,0)` | instance | `0x40280140` | `5 / 5` | `62 x 57`, `(34,39)` |
| 1 | 4027 | `(5,1)` | instance | `0x40700080` | `2 / 14` | `204 x 267`, `(113,262)` |
| 2 | 4030 | `(6,1)` | instance | `0x40280140` | `5 / 5` | `62 x 57`, `(34,39)` |
| 3 | 4030 | `(6,1)` | instance | `0x40280140` | `5 / 5` | `62 x 57`, `(34,39)` |
| 4 | 4027 | `(44,1)` | instance | `0x40700080` | `2 / 14` | `204 x 267`, `(113,262)` |

ARC-RES-008 may use these finite numeric chains as anchors only. It must not
infer a baseline from the hotspot, derive overlap/occlusion from source index,
or claim a rendered position without separately measured anchor and ordering
evidence.

## Synthetic coverage and remaining limits

`--self-test` covers valid and truncated framing, light/count/placeholder
bounds, all placeholder branches, v119 and unknown-version rejection,
unsupported type rejection, mask/count/reserved-bit validation, optional and
array wire corruption, canonical prototype missing/duplicate/malformed/type/ID
and boundary cases, direct-versus-inherited current-ART and object-flags
precedence (including independently written distinct sentinels at literal raw
serialized slot 18 and adjacent slot 19, with reversed `OF_DYNAMIC` cases), and synthetic resource mapping missing/ambiguous rejection
plus selected ART metadata retention. The ART decoder's own existing suite
remains its independent structure/limit coverage.

Unknown or excluded: `.dif` behavior; other object versions; all skipped-field
semantics; runtime temporary IDs and postprocessing; original object names;
prototype/resource precedence outside explicit inputs; ART pixel decoding,
palette availability, visual baseline, offsets, scale, mirroring, collision,
lighting, occlusion, ordering, and scene parity. The next task is therefore
ready for bounded anchor/overlap research, not a production static scene.