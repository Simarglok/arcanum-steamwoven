# ART: evidence-bounded one-frame decoder subset

## Scope and status

This document retains the R2 experimental record and now describes the bounded
implemented C# decoder of **one physical 8-bpp ART frame** into RGBA. It is not a
renderer, animator, writer, extractor, or viewer. Experimental observations remain
in [the ART evidence record](../research/art-evidence.md); CE-source-derived API
rules are separately attributed in [the CE specification record](../research/art-ce-spec.md).

Status vocabulary follows [the research method](../research/method.md):
reported, corroborated, experimentally verified, disputed, and unknown. The
R2 observations below deliberately did not assign rendering behaviour to
palette index 0, palette alpha, direction order, anchors, or timing. The later
implemented BGRX-to-RGBA, index-0, and row-order normalization rules are
CE-source-derived contract decisions, not retroactive claims about the R2 run.

## Observed layout

All offsets below are relative to an ART entry decoded into memory by the
bounded `Arcanum.Data` DAT reader. The selected corpus contains twelve entries:
six `item`-category static entries and six `critter`-category directional
entries. The category selector is local and no original entry names are
published.

| Region | Proposed representation | Status | Evidence / boundary |
| --- | --- | --- | --- |
| Header | 132 bytes, interpreted as 33 little-endian 32-bit words for the probe | experimentally verified for 12 samples | The probe reached the palette table, complete frame-header table, and exact payload end for every selected entry. The 132-byte size and word order are also reported by ArtConverter. |
| Header word 0 | Preserve as raw `uint32`; bit 0 selected one frame-header group for the six `item` entries and eight groups for the six `critter` entries | experimentally verified for the corpus; semantics unknown | The probe's count derived from this bit made every frame table and payload boundary exact. The semantic label “static” comes from the source route and local category selection; it is not a runtime direction-order claim. |
| Header words 1–2 | Preserve raw; the API exposes word 1 using the CE-source-derived `FramesPerSecond` label | unknown | The label is metadata only, not independent timing evidence and is not used to drive animation. |
| Header words 3–6 | Four raw palette selectors; non-zero word count controls the number of following 1,024-byte tables in this subset | experimentally verified for the corpus; selector semantics unknown | All selected `item` entries had one table; all selected `critter` entries had four. A zero/non-zero condition was sufficient to locate the next table. IDs, palette choice, and meaning are not established. |
| Header word 7 | Preserve raw; the API exposes it using the CE-source-derived `ActionFrame` label | unknown | The label is metadata only. No original-runtime or independent observational proof establishes an action/key-frame semantic. |
| Header word 8 | Per-group frame count | experimentally verified for the corpus | Combined with the observed bit-0 rule, it located every 28-byte frame header. |
| Header words 9–16 and 25–32 | Preserve raw | unknown | They were not needed to establish the frame boundaries. |
| Header words 17–24 | Eight raw per-group values | disputed / unknown | They equal observed payload-byte totals for all six directional samples, but not for any of the six selected static samples. Do not name or rely on them as data-size totals for a decoder. |
| Palette table | 256 × 4 raw bytes per present selector, immediately after header | experimentally verified for table size and placement | ArtConverter reports BGRA-shaped entries. Channel semantics and any conversion to RGBA are only reported, not independently verified. |
| Frame-header table | All frame headers precede payloads; each header is 28 bytes | experimentally verified for the corpus | Seven 32-bit words per frame made every selected entry consume exactly. |
| Frame-header words 0–2 | width, height, encoded payload byte count | experimentally verified for structural use | The probe validates positive dimensions, payload bounds, decoded pixel count, and exact consumption. |
| Frame-header words 3–6 | four raw 32-bit geometry values | reported | ArtConverter reads signed `c_x`, `c_y`, `d_x`, and `d_y`; the probe preserves their byte extent only. Coordinate origin, anchor, delta, and composition semantics are unknown. |
| Frame payload | Immediately follows all frame headers, in frame-table order | experimentally verified for the corpus | Sum of verified payload boundaries equals the selected entry length. |

The table describes a file-order subset, not a promise that the named source
fields have their reported engine semantics.

## Pixel encoding

For all 12 selected real-data samples, every frame used the bounded custom RLE
path: 294 frames in total, zero raw frames. The independently written probe
accepted a payload only when it produced exactly `width × height` indexed bytes
and consumed the frame payload exactly:

- a control byte has count `control & 0x7f`, which must be 1 through 127;
- high bit set: copy that many literal bytes;
- high bit clear: consume one byte and repeat it count times;
- neither input truncation nor output overrun is accepted.

This RLE rule is **experimentally verified for the selected corpus** because
the probe independently validates the source-reported encoding against real
byte boundaries. It remains a bounded observation, not evidence for every ART
variant. The source-reported raw alternative (`encoded_size == width × height`)
is exercised only by synthetic negative/positive tests here; it was not seen in
the 12 real samples and is not part of the proven real-data subset.

The R2 probe retained pixel order only as one-dimensional palette indices in
file decoding order. ArtConverter's BMP path reverses rows for BMP output, but
that did not establish a renderer coordinate origin or alpha policy for the R2
observation. The implemented decoder contract below is separately
CE-source-derived and deliberately does not retroactively promote those rules
to R2 experiment facts.

## Implemented decoder contract

`Arcanum.Data.ArtDecoder.Decode` accepts one already-bounded ART entry byte
span and a zero-based physical group, frame-in-group, and palette slot. It
supports exactly this input and output contract:

- Input: a little-endian, signed-8-bpp ART entry with the observed 132-byte
  layout; palette slots 0–3 are selected independently of any packed `art_id`.
- Output: copied raw header and present palette tables, copied selected
  28-byte frame header and signed geometry words, selected/requested/effective
  group-frame-palette metadata, and tightly packed straight RGBA bytes.
- Present palette tables follow in increasing slot order and retain gaps. A
  requested absent slot falls back only to present slot 0; absence of both is a
  format error. For this CE-source-derived normalization, file B/G/R/X maps to
  `(R,G,B,255)` for indices 1–255, X remains in raw palette metadata, and
  index 0 is `(0,0,0,0)` regardless of its palette bytes.
- Pixels remain stored row-major: `x` increases first, then `y`, with byte
  offset `4 * (y * width + x)`. The decoder does not reverse rows for BMP.
- FPS and action-frame are preserved as signed metadata under CE-source-derived
  labels only; they do not establish or drive animation, timing, placement, or
  direction semantics.
- Admitted real-data structural subset: the observed 132-byte layout,
  zero/non-zero palette-table presence, bit-0-derived one/eight physical group
  sizing, selected RLE frames, and checked byte boundaries. The RGBA policy is
  synthetic-contract coverage plus bounded same-input local regression checks,
  not CE pixel parity or an independent pixel oracle.
- Required rejection conditions before allocation: entry shorter than 132
  bytes; non-positive or over-cap frame/group counts; absent palette/frame
  tables; dimension, multiplication, offset, or payload-size overflow; a
  payload outside the entry; zero/overrunning/truncated RLE control runs; RLE
  output not exactly equal to `width × height`; or trailing bytes after the
  declared full-frame payload sequence (unless the caller opts into the
  original's leniency, below).
- Research caps used by the evidence probe: decoded DAT entry at most 1 MiB,
  at most 1,024 frame headers, width/height at most 4,096 each, and at most
  4 MiB decoded pixels per frame. These are safety policy, not format facts.

A production decoder retains checked arithmetic and configurable reviewed caps.
It must not treat the exposed FPS/action labels as runtime semantics or silently
accept a different layout.

## Trailing bytes after the last frame (ARC-DATA-016)

437 of the 13,993 installed ART files (interface 326, eye candy 35, wall 33,
item 18, scenery 16, portal 8, monster 1) carry bytes after the last frame
payload: 1 byte in 44 files, 2–64 bytes in 134, more in 259. Every frame of
every one of them decodes to exactly `width × height` pixels. The pinned CE
reader (`first_party/tig/src/art.c`, `art_read_header` and the frame loop) reads
the header, palettes, frame table and payloads in order and never checks the
end of the file, so the tail is invisible to it.

`ArtDecoderOptions.AllowTrailingBytes` (off by default) accepts such a tail and
reports its length as `ArtMetadata.TrailingBytes`; a payload that runs past the
end of the file is still refused as truncated. Existing callers keep the strict
form; the universal loader opts in. An opt-in `RealData` test
(`ArtStackRealDataTests`) reads every installed ART file this way: 13,993
read, 437 with a tail, and exactly those 437 are refused by the strict form.

## Unsupported and unresolved

The following remain outside the supported subset:

- raw-pixel ART on real data, every unselected ART category, historical or
  patched variants, and any data entry above the probe cap;
- mapping of local category names to runtime types; category names only guided
  a deterministic sample choice;
- palette effects beyond the selected original palette, per-entry alpha,
  translucency, lighting, blending, or palette animation; the baseline BGRX
  conversion and transparent-black index 0 rule above are supported;
- renderer coordinate interpretation, direction order, mirroring, frame timing,
  action/key-frame semantics, and animation runtime behaviour; stored row-major
  RGBA order is supported but is not a renderer coordinate claim;
- meaning and coordinate system of the four frame geometry words;
- semantics of header words 1–2, 7, 9–16, and 25–32;
- the static-sample contradiction for header words 17–24;
- asset lookup precedence, loose-file/module/PATCH precedence, renderer
  integration, previews, extraction, and writing.

In particular, the location of animation timing is **unknown**: the ART source
route names a rate-like word, but no runtime or independent data experiment
establishes whether timing belongs to ART, an external table, or runtime code.
No Arcanum executable or CE application was run to resolve it.
