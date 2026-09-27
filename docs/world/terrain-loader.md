# Terrain loader

## Scope

`Arcanum.Data.Terrain` is a synchronous, Godot-free loader for the evidence-bounded 64 by 64 Crash Site-style sector framing. It accepts a caller-selected module `DatArchive` and exactly one caller-selected resource `DatArchive`; it does not select between archives, PATCH data, or loose files. It supports only `TerrainPathProfile.CeNonWindowsMacOs` (ASCII lowercasing and backslash-to-slash matching), tile and facade packed classes, and the framed base-sector prefix. Windows behavior, `.dif`, object semantics, lighting, navigation, scenes, renderer integration, normal-game facade policy, and visual parity are not implemented.

The module and resource archives remain caller-owned. They are seek-based and must not be used concurrently. The loader does not dispose either archive. Input paths belong only to the calling CLI; reusable API records contain a caller-provided non-path source ID and normalized logical entry names, not local archive paths.

## API

Use `TerrainLoader.Inspect(module, selector, source, options)` to receive exactly 4,096 ordinal outcomes. Successful outcomes form `ResolvedRecords`; failed cells form `Issues`, and never count as supported. `TerrainLoader.Load(...)` uses the same pipeline and throws `TerrainIncompleteException` with its report if any cell failed. `DecodeSelectedFrame(resourceArchive, record, options)` reselects the record's exact DAT ordinal and identity before calling `ArtDecoder.Decode`; it is the only on-demand RGBA path.

`TerrainGeometry.Place(kind, localX, localY, rawId, frameWidth, frameHeight, viewport)` is pure diagnostic geometry. It projects `(40 * (y - x), 20 * (y + x))`, applies the one-pixel X anchor adjustment, uses a 78 by 40 tile or 80 by 40 editor-facade footprint, crops against viewport then frame bounds, and returns null for a valid no-draw intersection. It retains raw tile flip `(rawId & 0xC1) == 0xC1`; facades do not flip in this diagnostic mode. ART hotspots and offsets are retained as metadata but not applied as placement.

The loader uses checked arithmetic and defaults to 1 MiB decoded sector/MES/ART entries, 1,024 lights, exactly 4,096 cells, 256 roof words, 4,096 scripts/object count/distinct ART metadata entries, finite MES limits, and bounded ART inspection (1 MiB input/encoded frame, 4,096 frames/dimension, 256 KiB table, 4 Mi pixels, 16 MiB RGBA). ART metadata is cached by resource DAT ordinal during an inspection; decoded ART payloads are not retained after metadata inspection. The opaque object window is an owned immutable byte copy with its range and terminal count; it is never interpreted or emitted by the CLI.

MES pathname values are fail-closed printable ASCII. For both tile and facade tables, a nonempty value contributes exactly its prefix before the first `/`; a value without `/` is preserved in full. The prefix is never trimmed, shortened, or case-folded before normal terrain pathname normalization.

Fatal structural, selection, limit, unsupported-profile, and identity failures use `TerrainException` subclasses with stable `TerrainErrorCode`. Per-cell failures use stable `TerrainIssueCode` values including unsupported class, invalid selectors, resource zero/many match, unsupported ART, invalid frame, and unavailable palette.

## `terrain-inspect` CLI

The minimal aggregate-only CLI fixes the documented Crash Site selector, entry metadata, and full immutable module/resource SHA-256 values; it requires explicit inputs:

    dotnet run --project tools/terrain-inspect/terrain-inspect.csproj -- \
      --module <module.dat> --resource <arcanum2.dat> --source-id <label> \
      [--expect-module-sha256 <sha256>] [--expect-resource-sha256 <sha256>]

`--help` exits 0. Invalid usage exits 2. A complete aggregate JSON report exits 0; a well-formed partial report exits 3; I/O, format, limit, integrity, and selection failures exit 1. Standard output is one deterministic aggregate JSON document containing schema version, fixed selector/framing, source ID, hashes, aggregate counts, issue totals, frame validation count, and normalization counts. A normalization recovery is counted only when the raw tile-derived path has zero candidates and the normalized lookup path resolves uniquely; it is not inferred merely because raw and lookup IDs differ. It never emits input paths, object bytes, MES values, full ART-name corpora, original payloads, or stack traces. Standard error contains only a safe exception type and terrain code where available.

## Evidence, provenance, and integration boundary

The fixed profile is documented by `docs/research/crash-site-{slice,framing,terrain-resolution,evidence}.md`; its framing and resource rules are attributed to the pinned CE source lineage `a7ff41b300ef712f0e7d088183a3d08957110cdd`, not copied implementation or independent corroboration. The opt-in real-data boundary is caller-supplied data only. It compares the documented full immutable module/resource fingerprints before and after and confirms the 4,096 aggregate (3,033 tile, 1,063 facade), zero issues, selected frames, and 457 recovered cells across 86 recovered lookup IDs without committing or logging original content.
