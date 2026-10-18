# ART decoder specification record

## Purpose and provenance boundary

ARC-DATA-002 independently implemented the bounded public `Arcanum.Data`
8-bpp ART one-physical-frame decoder. Its contract comes from an earlier source
reading (provenance in git history) of the ART functions (header and frame
handling, palette selection, unflipped source addressing, the frame-data lookup
and the header reader) and the colour-index lookup; level F0 (not checked in the
executable). The project makes no claim of measured pixel
parity. ArcNET is not an independent confirmation.

## Contract adopted for this bounded decoder

The decoder accepts only an already-bounded little-endian entry whose 132-byte
header reports signed `bpp == 8`. It preserves flags, signed FPS/action fields,
palette-presence words, and all three eight-word opaque header regions. Bit 0
selects one physical group when set and eight when clear; other flag bits stay
uninterpreted. The signed frame count per physical group must be positive.

Nonzero palette-presence words mean that a 1,024-byte table follows in slot
order; they are not offsets. A requested present palette wins, otherwise only
slot 0 is a fallback. The decoder returns four nullable slots so gaps remain
observable. Frame headers are group-major then frame-major and contain signed
positive width, height, and encoded length plus signed raw geometry metadata.
Payload boundaries come only from sequential encoded lengths after the complete
frame-header table, never from header words 68–99.

Equal encoded length and `width * height` selects raw indices. Otherwise the
bounded RLE stream uses a 1–127 low-seven-bit count, literal runs when high bit
is set, and repeated-byte runs otherwise. The selected stream must consume its
payload and produce exactly the frame's pixel count. Whole-entry table/payload
structure is validated, while unselected RLE is intentionally not decoded.

Output is straight RGBA in stored row-major order. Index 0 is `(0,0,0,0)`;
indices 1–255 map file B/G/R/X to `(R,G,B,255)`, preserving X only in the raw
palette metadata. This does not implement blending, palette animation,
lighting, mirroring, timing, placement, or any renderer behavior.

## Relationship to R2 evidence

The 12-entry R2 experiment in [art-evidence.md](art-evidence.md) remains
experimental history: it established bounded layout and RLE observations but
did not independently establish channel conversion, transparency, or row
orientation. The production decoder's synthetic tests independently state
expected RGBA values. Opt-in local data checks are structural/regression
coverage, not an external pixel oracle or comparison with another decoder.
