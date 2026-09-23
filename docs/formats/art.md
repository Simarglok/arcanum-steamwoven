# ART: evidence-bounded one-frame decoder subset

## Scope and status

This document retains the R2 experimental record and now describes the bounded
implemented C# decoder of **one physical 8-bpp ART frame** into RGBA. It is not a
renderer, animator, writer, extractor, or viewer. Experimental observations remain
in [the ART evidence record](../research/art-evidence.md); CE-source-derived API
rules are separately attributed in [the CE specification record](../research/art-ce-spec.md).

Status vocabulary follows [the research method](../research/method.md):
reported, corroborated, experimentally verified, disputed, and unknown. The
The R2 observations below deliberately did not assign rendering behaviour to
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
| Header words 1–2 | Preserve raw | unknown | ArcNET labels them rate and rotation count, but that is not independent evidence and was not used by the probe. |
| Header words 3–6 | Four raw palette selectors; non-zero word count controls the number of following 1,024-byte tables in this subset | experimentally verified for the corpus; selector semantics unknown | All selected `item` entries had one table; all selected `critter` entries had four. A zero/non-zero condition was sufficient to locate the next table. IDs, palette choice, and meaning are not established. |
| Header word 7 | Preserve raw | unknown | A source calls it an action/key frame. No original-runtime or independent observational proof exists. |
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

Pixel order is retained as one-dimensional palette indices in file decoding
order. ArtConverter's BMP path reverses rows for BMP output, but that does not
establish a renderer coordinate origin. No ART palette index is assigned alpha
or transparency in this specification.

## Explicit later decoder contract

A later C# task may implement **only after separate authorization** this input
and output contract:

- Input: one already-bounded ART entry byte span from `Arcanum.Data`, and a
  local selector identifying one frame-table group and one frame index.
- Output: raw 132-byte header (or all raw words), raw present palette tables,
  the selected 28-byte frame header (including its four opaque geometry words),
  and exactly `width × height` decoded palette-index bytes.
- Admitted real-data subset: the observed 132-byte layout, zero/non-zero
  palette-table presence, bit-0-derived one/eight group table sizing, selected
  RLE frames, and checked byte boundaries. It must not claim timing, direction
  orientation/order, palette selection/channel conversion, transparency, or
  anchor semantics.
- Required rejection conditions before allocation: entry shorter than 132
  bytes; non-positive or over-cap frame/group counts; absent palette/frame
  tables; dimension, multiplication, offset, or payload-size overflow; a
  payload outside the entry; zero/overrunning/truncated RLE control runs; RLE
  output not exactly equal to `width × height`; or trailing bytes after the
  declared full-frame payload sequence.
- Research caps used by the evidence probe: decoded DAT entry at most 1 MiB,
  at most 1,024 frame headers, width/height at most 4,096 each, and at most
  4 MiB decoded pixels per frame. These are safety policy, not format facts.

A production decoder must retain checked arithmetic and configure its own
reviewed caps. It must not use an unverified header word as FPS/action-frame,
or silently accept a different layout.

## Unsupported and unresolved

The following remain outside the supported subset:

- raw-pixel ART on real data, every unselected ART category, historical or
  patched variants, and any data entry above the probe cap;
- mapping of local category names to runtime types; category names only guided
  a deterministic sample choice;
- palette slot meaning, palette channel conversion, alpha and transparency
  (including index 0);
- row orientation, direction order, mirroring, frame timing, action/key frame,
  and animation runtime behaviour;
- meaning and coordinate system of the four frame geometry words;
- semantics of header words 1–2, 7, 9–16, and 25–32;
- the static-sample contradiction for header words 17–24;
- asset lookup precedence, loose-file/module/PATCH precedence, renderer
  integration, previews, extraction, and writing.

In particular, the location of animation timing is **unknown**: the ART source
route names a rate-like word, but no runtime or independent data experiment
establishes whether timing belongs to ART, an external table, or runtime code.
No Arcanum executable or CE application was run to resolve it.
