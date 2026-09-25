# Crash Site evidence ledger

## Scope and provenance

This ledger records ARC-RES-003's one-sector research result. The user-owned
original data was read only. The repository contains no extracted game bytes,
asset names, previews, installation paths, credentials, or third-party source
copy.

| Source | Identifier | Use | Status / limit |
| --- | --- | --- | --- |
| Observed module archive | `Arcanum.dat`, 355,938,305 bytes, SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` | exact map and sector selectors | experimentally verified local input; not a retail-canonical or precedence claim |
| Project reader | `src/Arcanum.Data/DatArchive.cs` | bounded DAT opening and in-memory entry decode | independently authored project implementation |
| Project ART decoder | `src/Arcanum.Data/ArtDecoder.cs` | designated future ART metadata/frame decoder | no selected Crash Site ART path was established here |
| Pinned CE source | `a7ff41b300ef712f0e7d088183a3d08957110cdd` | sector sequence, coordinate packing, isometric relation, ART-ID context | one external lineage, not independent corroboration |
| Community catalogue | preserved toolkit metadata | Crash Site context | reported only; not byte-level map proof |

The CE revision was resolved by its full immutable commit ID before inspecting
these focused paths: `src/game/sector.c`, `src/game/sector_light_list.c`,
`src/game/sector_tile_list.c`, `src/game/sector_object_list.c`,
`src/game/location.c`, and `first_party/tig/src/art.c`.

## Reproduction commands

Use the canonical project SDK and pass a local, user-owned input explicitly.
The probe emits aggregate metadata only and does not write next to its input.

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore

dotnet run \
  --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj \
  --no-build -- --self-test

dotnet run \
  --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj \
  --no-build -- --module <explicit-module-path>
```

Use the project's configured .NET 8 SDK. The module path is supplied locally
and must not be committed or reported.

## Executed results

| Check | Result |
| --- | --- |
| Crash Site probe synthetic suite | PASS: truncation, negative/impossible count, exact tile-table boundary with no following terminal count, strict unexpected ART class, and negative terminal count are rejected; projection examples pass |
| Read-only selected-sector run | PASS: input fingerprint matched before and after; entry ordinal 463, flags `0x00000002`, decoded bytes 47,291; zero serialized lights; 4,096 tile-table words; 1,041 distinct words; 30,899 opaque object-payload bytes; terminal object count 317 |
| Existing DAT synthetic tests | PASS: `Arcanum.Data.Tests` exited 0 with the canonical SDK |
| Existing Python DAT probe synthetic suite | PASS: `python3 tools/research/dat_probe.py --self-test` |

The real-data aggregate art-type counts are recorded in `docs/formats/maps.md`.
They intentionally do not enumerate raw IDs or asset names.

## Probe boundary

`Arcanum.CrashSiteProbe` reuses `DatArchive`; it adds only a narrowly bounded
sector-prefix inspection. It caps the decoded sector at 1 MiB and validates
all arithmetic before a count-dependent region is read. It does not parse
light records, object records, MES, MOB, PRO, or ART data, and does not write
or export decoded bytes.

The strict synthetic mode rejects an unexpected non-tile high-nibble class.
The real probe records those classes rather than assuming the whole table has
one resource semantics; this is why it is evidence collection, not a loader.

## Narrowed object and terrain blockers

The real sector does establish the exact byte window that needs an object
schema: with zero lights, the 4,096-word tile table occupies decoded offsets
`4..16387`, the terminal count occupies `47287..47290`, and the intervening
`30,899` bytes contain the 317 serialized objects. These values are aggregate
measurements only; no object bytes, names, or identifiers are retained.

Pinned CE's `objlist_load` is a source-derived explanation of this boundary:
after the fixed tile list it first reads the final count, returns to the object
window, calls its object reader that many times, then reads the final count
again. Its `obj_read` first consumes a version and object identifier, selects
prototype versus instance layout from that identifier, and the instance layout
then consumes prototype/object identifiers, a validated type, a field-count,
a type-dependent override-bitset, and the payload for every set field. Some
field payloads are not fixed-width. This is one CE lineage, not independent
format confirmation.

Consequently, the exact minimum safe decoder subset still missing is not a
full object system: it is a bounded decoder for that record envelope plus only
the common **type**, **location**, **current ART ID**, and **prototype ID**
properties, including the type-dependent bitset and every preceding
variable-width property required to reach them. Without that delimiter schema,
a proposed offset could make any subsequent object's bytes appear to be a
scenery record. The selected-sector count and byte window prove the impact is
local to these 317 records; they do not classify any record as scenery.

Terrain is independently blocked at the equally small lookup boundary: the
fixed table contains both the CE-reported tile and facade high-nibble classes,
but this research has not established the type-specific ART-ID
number/variation decomposition and `ART ID -> selected resource path` rule for
either class. `DatArchive` can then locate a raw exact-name entry and
`ArtDecoder.Inspect` can inspect a bounded entry, but neither can supply that
missing resolver. No fallback path, archive winner, frame, baseline, or order
is inferred.

These two blockers mean neither a Godot-independent loader nor a Godot static
scene is ready. The bounded next evidence is therefore precisely the object
envelope/properties above and the two observed tile-class lookup rules—not a
full MOB/PRO parser, full ART-ID resolver, renderer, or gameplay model.

## Known limitations and resulting readiness

- The exact mapping from selected tile words to lookup table and ART path is
  unknown.
- No object record is safely delimited or classified, so the count 317 is not
  a scenery selection.
- No prototype inheritance, source precedence, frame/palette selection,
  baseline, occlusion, or scene comparison was measured.
- The CE location formula is source-derived and numeric synthetic checks verify
  only its relative arithmetic, not visual parity.

Accordingly, the C# loader and static Godot scene are both **blocked**, not
ready. Their bounded research gates are stated in
`docs/world/static-scene-contract.md`.
