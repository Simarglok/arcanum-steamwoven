# Static-object loader contract: bounded Crash Site candidate

> Status update (2026-09-28): This historical proposed contract has a bounded [implementation](static-object-loader.md). Its future-tense text preserves the research boundary; it is not the current API specification. [Scenery placement research](../research/scenery-placement.md) and the [rendering contract](../rendering/static-scenery-contract.md) refine diagnostic placement without establishing original scene parity. See [current status](../project/status.md).

## Status and boundary

This is a proposed, fail-closed contract for a future data-only loader. It is
based only on the reviewed evidence in `docs/research/crash-site-objects.md`
and the frozen-sector framing contract. It authorizes no production loader,
Godot integration, renderer, asset extraction, source-precedence policy, or
scene-parity claim.

**Readiness:** the v119 selected-sector object diagnostic is READY for the
narrow records below when the caller supplies the exact immutable module,
prototype directory, and one explicit resource archive. Static-scene placement,
baseline, overlap/occlusion, ordering, and rendering are NOT READY.

## Required caller inputs

A future caller must explicitly provide all inputs; it must not search for a
winner or silently substitute one.

| Input | Required validation | Failure behavior |
| --- | --- | --- |
| Module | Recorded full size/SHA-256; exact raw sector selector; compressed flag; 1 MiB decoded cap; framing and terminal count | no report or data record |
| Prototype directory | Every referenced inherited type-A ID maps to exactly one canonical six-digit `.pro` file; each selected file is capped and unchanged during the read | unresolved record; strict mode fails whole inspection |
| Resource archive | Caller-selected full size/SHA-256; one exact mapping and one normalized ART entry within that archive | unresolved record; strict mode fails whole inspection |
| Object version | Exactly v119 | unsupported record; no heuristic continuation |
| Art selector | ART type 4; mapping key; group/frame within physical table | unresolved record; no fallback ART, frame, or palette |

The fixed sector remains the entry and framing recorded in
`crash-site-framing.md`. `.dif`, patches, loose files, different sector
versions, and unpinned sources are outside this contract.

## Minimal data model

Names, raw payloads, original paths, and decoded pixels are intentionally not
returned. Identifiers below are numeric source IDs or caller-supplied opaque
source labels.

```text
StaticObjectInspection
  sourceId                    // caller-owned non-path label
  moduleFingerprint
  resourceFingerprint
  selector                    // fixed sector identity and object-window range
  terminalObjectCount
  records[]                   // exactly terminalObjectCount outcomes

StaticObjectRecord
  sourceIndex                 // stable serialized order only
  status                      // resolved | unsupported-version | unresolved-reference | invalid-record
  objectType                  // only when parsed
  prototypeAid                // type-A numeric ID only when used/available
  localX, localY              // serialized local coordinates only when present
  currentArtId                // packed numeric value only when resolved
  currentArtSource            // instance | prototype
  effectiveDynamic            // only tested predicate
  artType, sceneryCategory, artNumber
  rotationGroup, frame, palette
  frameMetadata               // width, height, hotspot only
  reasonCode                  // stable failure category; never original text/path
```

A `resolved` record requires all fields in the resolution chain below. A record
with an absent local location, unsupported object type/version, invalid wire
form, missing/ambiguous prototype or resource reference, unsafe mapping value,
or invalid ART group/frame is not made renderable. An inspection may return all
outcomes for diagnostics; a strict load must reject if any outcome is not
`resolved`.

## Proven override/default precedence

Only two field defaults are admitted by evidence:

```text
currentArt = instance.currentArt when field 0 is present
           = exactly-one validated matching prototype.currentArt otherwise

effectiveFlags = instance.flags when CE OBJ_F_FLAGS enum ID 19 is present at serialized slot 18
               = exactly-one validated matching prototype.flags otherwise
```

The matching prototype must have the same serialized type and exact ObjectID;
only canonical type-A IDs in the six-digit range are locatable. Duplicate,
missing, malformed, type-mismatched, or ID-mismatched files fail closed. No
other prototype field is inherited by this contract, and no conclusion is made
about defaults for skipped fields.

The bounded static predicate is:

```text
objectType == 3 && (effectiveFlags & 0x00002000) == 0
```

This mirrors the cited source's type eligibility plus `OF_DYNAMIC` check. It
does not establish visual visibility, collision, passability, interaction,
height, flatness semantics, occlusion, or ordering.

## Resolution support matrix

| Stage | Observed selected support | Required validation | Unsupported / failure result |
| --- | --- | --- | --- |
| Sector framing | 317 v119 records in `17140..47287` | Exact terminal count and no trailing record bytes | reject input |
| Record envelope | types `0..17` delimited synthetically; real slice type 3 only | version, object IDs, field count/masks, bounded optional/array strings | `invalid-record` |
| Type classification | all real records type 3 | no type promotion or inferred aliases | `unsupported-type` |
| Location | local coordinates present in the five handoff records | optional field must be present for a coordinate record | `unresolved-location` |
| Current ART | 288 direct, 29 inherited real values | direct field or exactly-one valid prototype default | `unresolved-reference` |
| Flags for static predicate | 31 direct / 286 inherited effective instance/prototype object flags from serialized slot 18 (CE enum ID 19) | direct slot or exactly-one valid prototype default; do not substitute adjacent slot 19 spell flags | `unresolved-reference` |
| Prototype file selection | six real files used | one canonical numeric candidate; unchanged fingerprint | `unresolved-reference` |
| Scenery mapping | five real selected examples | one mapping key and safe relative filename | `unresolved-resource` |
| ART entry | five real selected examples | one normalized candidate in explicit resource archive, 1 MiB cap | `unresolved-resource` |
| ART metadata | five real selected examples | type/category/number, group/frame bounds, inspected metadata | `invalid-art-selector` |
| Placement and ordering | none | separate evidence required | not part of this result |

The real slice supports a complete record census and current-ART/default chain,
but only the five listed source examples have an ART metadata chain in this
research. A future implementation must not broaden that five-example resource
claim into a whole-art corpus claim without new evidence.

## Explicit non-goals and safety rules

- Never infer source precedence across DATs, patches, loose files, or different
  roots. The resource archive is an explicit caller input.
- Never emit archive paths, mapping text, asset names, pixels, or raw object
  bytes in diagnostics.
- Never use hotspot, frame dimensions, source index, or local location as an
  unproven visual anchor, sort key, baseline, or occlusion value.
- Never continue after malformed/truncated data merely to reach the terminal
  count; the record must be rejected.
- Do not reuse this diagnostic contract for gameplay, navigation, collision,
  item/container/critter loading, scripts, or runtime postprocessing.

## Evidence lineage

The byte-level observation and synthetic tests are in
`Arcanum.CrashSiteProbe`; the bounded result is recorded in
`crash-site-objects.md`. Source attributions are limited to `arcanum-ce`
`a7ff41b300ef712f0e7d088183a3d08957110cdd`, principally
`src/game/sector_object_list.c`, `src/game/object.c`, `src/game/obj.h`,
`src/game/obj_id.h`, `src/game/obj_flags.h`, `src/game/name.c`, and
`first_party/tig/src/art.c`. That is one lineage, not independent validation
or permission to copy source code.