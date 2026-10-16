# Arcanum.Data

`Arcanum.Data` is an independently written, evidence-bounded reader for the observed R1 `1TAD` DAT container subset and a bounded 8-bpp ART one-frame decoder. The DAT reader parses metadata incrementally and reads one selected payload into memory; it does not extract files, write archives or normalize names. Resource precedence across archives, PATCH files, the module and loose folders lives separately in `Arcanum.Data.Content` ([research and API](research/content-precedence.md)).

## Components and toolchain

The library also contains byte-preserving [DLG reading/reference validation](formats/dlg-reader.md), bounded [area ambient lighting schemes](rendering/area-lighting.md) (`Arcanum.Data.Lighting`), a fixed-source [terrain loader](world/terrain-loader.md), independent [terrain obstruction components](world/terrain-obstruction-components.md), a bounded [static-object loader](world/static-object-loader.md), the [complete Crash Site exterior data owner](world/crash-site-area-loader.md), and the read-only [T0 inventory view](world/area-inventory-view.md) over its snapshot (`Arcanum.Data.Inventory`). The area API supplies authenticated immutable multisector/mobile metadata and identity-bound selected-frame leases and frame batches, not rendering or gameplay. Its reservation accounting is not a total CLR heap/RSS bound. These APIs remain Godot-independent. The separate [dialog transition runtime](scripting/dialog-transition-runtime.md) belongs to `Arcanum.Scripting`; frame playback policy belongs to `Arcanum.Presentation`.

See the [component catalogue](project/status.md) for current coverage and [toolchain](workflow/toolchain.md) for pinned SDK/Godot versions, synthetic checks and opt-in input/filter requirements. `DAT_ROOT` alone does not supply terrain/static-object inputs; a broad `TestCategory=RealData` run requires every documented input.

## API and ownership

- `DatArchive.Open(string, DatReaderOptions?)` owns its file stream.
- `DatArchive.Open(Stream, DatReaderOptions?, bool leaveOpen = false)` requires a readable, seekable stream and owns it unless `leaveOpen` is true. Ownership also applies when opening fails. The caller position is not restored and must not use the stream concurrently with the archive.
- `Footer` and ordered `Entries` preserve raw bytes. `RawNameBytes` includes the terminal NUL. No decoding, lookup, case folding, or normalization is performed, so duplicates remain duplicates.
- `ReadEntry` accepts only exact flag `0x001` (plain) and `0x002` (zlib); all other values, including directory metadata `0x400`, are enumerated but rejected before payload access.
- `Dispose` is idempotent. Metadata remains readable after disposal; `ReadEntry` throws `ObjectDisposedException`.

### ART frame decoder

- `ArtDecoder.Decode(ReadOnlySpan<byte>, ArtFrameSelection, ArtDecoderOptions?)` accepts already-bounded ART bytes, zero-based physical group/frame-in-group, and palette slot 0–3. It supports only signed header `bpp == 8`.
- The result owns copies of the raw 132-byte header, present raw palette tables, selected 28-byte frame header, and tightly packed straight RGBA output. These are exposed as read-only lists; it never retains or mutates the input span.
- The decoder preserves raw header regions and signed geometry metadata, but does not implement packed IDs, animation, mirroring, placement, rendering, palette effects, extraction, or writing. Palette index 0 is transparent black; nonzero indices read file B/G/R/X as R/G/B/255, with X preserved in metadata but ignored for this conversion.
- Structural validation scans the whole frame table and payload sequence, including exact input consumption. RLE validation is deliberately limited to the selected frame so a malformed unselected RLE stream does not cause an otherwise selected physical frame to be decoded.
- `ArtFormatException` reports malformed or inconsistent bytes, `ArtUnsupportedFormatException` reports other bpp values, and `ArtLimitExceededException` reports policy caps. Selector and non-positive option values use `ArgumentOutOfRangeException`.

`DatFormatException` represents malformed structure or payload integrity errors; `DatUnsupportedFormatException` is the unsupported marker variant; `DatLimitExceededException` reports reader-policy caps; `DatUnsupportedEntryException` reports unreadable flags/directory metadata. Native `IOException` and argument/disposal exceptions remain unwrapped.

## Reader limits

Defaults are policy safeguards, not format facts: archive 4 GiB; index 64 MiB; entry count 1,000,000; each name 64 KiB; total names 64 MiB; stored entry 256 MiB; decoded entry 512 MiB. All must be positive and are checked before the corresponding allocation or payload read.

### ART decoder limits

`ArtDecoderOptions` exposes the following configurable safety policy defaults.
They bound untrusted input, checked size arithmetic, and allocations; none
describes a universal ART format maximum or a claim about the observed corpus.

| Option | Default | Safety rationale |
| --- | ---: | --- |
| `MaxInputBytes` | 64 MiB | Reject an already-bounded entry that is too large to retain and structurally scan in this API. |
| `MaxFrameCount` | 65,536 | Bound frame-header traversal and the internal metadata array before it is allocated. |
| `MaxFrameTableBytes` | 16 MiB | Independently bound the checked `frameCount × 28` table extent. |
| `MaxDimension` | 16,384 per axis | Reject extreme declared geometry before pixel-count multiplication. |
| `MaxFramePixels` | 16 Mi pixels | Bound decoded indexed-pixel work and the subsequent output allocation. |
| `MaxRgbaBytes` | 64 MiB | Bound checked `pixels × 4` straight-RGBA storage before allocation. |
| `MaxEncodedFrameBytes` | 64 MiB | Bound a selected encoded payload before slicing or decoding it. |

All values must be positive; invalid option values produce
`ArgumentOutOfRangeException`. Format inconsistency, unsupported bpp, and a
valid structure exceeding one of these policies remain distinct failure modes.

The supported structure and evidence boundary are in [formats/dat.md](formats/dat.md). Local real data are opt-in only and are never committed. The R1 evidence covers eight user-owned candidates but does not establish canonical bytes, other variants, flag semantics beyond the admitted exact values, filename semantics, or archive precedence.

ART layout and decoder boundary are in [formats/art.md](formats/art.md). The CE-source-derived rules used for the ART API are separately attributed in [research/art-ce-spec.md](research/art-ce-spec.md); they are not a CE dependency or a claim of pixel parity.

The DAT-specific opt-in check first and last verifies the documented size/SHA-256 manifest without writing inputs. It compares the parsed footer and all index metadata (including raw name and opaque fields) in memory with the existing Python R1 probe through a length-prefixed test adapter, then reads the five documented bounded payload ordinals. It publishes no source names, payload bytes, payload hashes, or local paths; a missing `DAT_ROOT` is an explicit failing/inconclusive invocation, not a pass.
