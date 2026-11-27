# Area viewport nearest sampling

ARC-RENDER-007 defines a deterministic presentation rule; it does not recover
retail Arcanum or AppleGL interpolated-UV tie behavior. Logical world positions,
source rectangles, layer order, actor simulation and camera zoom are unchanged.
This specification is frozen before the new native viewport conformance tests.

## Exact rule

For an axis-aligned positive viewport scale `z`, translation `o`, destination
`[d, d + n)` and source crop `[c, c + m)`, evaluate each integer screen pixel `s`
at its center `s + 1/2`. Treat the supplied Godot single-precision transform
components as their exact binary rational values, not rounded decimal values.
If existing viewport transform snapping is enabled, apply its existing
`ceil(o - 1/2)` translation policy first; never enable snapping for this rule.

Compute with exact rational arithmetic:

    u = ((s + 1/2 - o) / z - d) * m / n

The destination owns the pixel iff `0 <= u < m`. The source index is
`c + floor(u)`; reflection is applied AFTER discrete selection, giving
`c + m - 1 - floor(u)`. An exact boundary belongs to the higher unreflected
texel. The destination end is excluded. Apply the rule independently on X/Y.
There is no epsilon, neighboring-color permission, interpolation, uniform
support requirement, pixel-coordinate nudge or change in logical placement.

For example at `o=100, z=1/2, d=0, n=m=8, c=2`, screen centers
100.5/101.5/102.5/103.5 select indices 3/5/7/9, or 8/6/4/2 when reflected.
At `o=99.75, z=1/2`, center100.5 selects index3; at `o=100.5` it selects index2.
These literals and independently implemented rational oracles are not derived
from GPU output or the production sampler.

## Production mechanism and limits

Build two bounded CPU index rows per visible draw item's whole screen envelope
(padded by one pixel and not clamped to the viewport) from the rule above. Only an
axis longer than 8,192 pixels (a very large sprite at high zoom) is clamped to the
viewport as before; its rows stay exact but are not reused across pans.
Upload them as RGBA8 lookup metadata: zero encodes an excluded pixel; otherwise
R/G encode the 16-bit source index plus one. The shader reads the integer screen
pixel with `texelFetch` from the lookup and then the original retained ART frame.
It does not interpolate UVs or ask a proprietary nearest sampler to resolve ties.
A padded screen-covering primitive with explicit lookup exclusion prevents
backend triangle-edge conventions from changing the half-open ownership rule.
It is not additional source geometry or movement permission.

For row position `i` from the envelope start `e`, the rule above is an exact
affine function of `i`: `AreaNearestAxis.RowKey(e)` normalizes its integer
coefficients with the source crop and reflection into a key. Equal keys select
equal indices for every `i`, so one row texture serves every item and camera
position with that key. An integer camera translation (always the case with the
existing pixel snapping) moves the envelope by the same integer and leaves the key
unchanged, so panning reuses rows instead of rebuilding them; a sub-pixel or zoom
change yields new keys. The key is reused across such a translation without being
recomputed only when both origins are integral and the envelope moved by exactly the
same integer. Keys are
compared exactly, never by tolerance.

What moves with the camera is not an input of an item (ARC-RENDER-3416). All items of
a canvas read one small float texture, the view state, written once per submission: the
raster (canvas origin and zoom), the whole pixel at or before that origin and the place
of the carried lights' lattice. An item's screen start is counted from that whole pixel,
so across the whole-pixel translation above an item whose other inputs are unchanged
sends nothing to the engine and is not redrawn; the pixels it samples are the same. The
canvas also keeps each command's engine state (material, index row, world rectangle) with
the command: when the draw list shifts, the state moves to the node that draws the command
next, so a command that stays in the view is not prepared from nothing again. A standalone item
keeps and writes its own view state.

The carried lattice's two textures are larger than the lattice (ARC-RENDER-3427): each
side is a multiple of 16 nodes, grows when the lattice outgrows it and is given back when
it is more than four times the need. The lattice lies in their top left and the shader
reads only the nodes inside the bounds in the view state, so the drawn picture is the
same. A walking light changes the lattice's columns and rows every few frames; while each
change made new textures, every terrain item of the view sent them and was redrawn.

Rows live in one canvas-owned cache. Rows used by the current submission are never
evicted; rows of earlier views are reused or evicted least recently used first, down
to three quarters of the limits, when the row payload would exceed 16 MiB or the
cache would exceed 65,536 rows (8,192 before ARC-RENDER-032). A zoom that is not a power of two
has no reduced terrain chunks, so far out the terrain is drawn one item per tile and nearly every
visible item needs its own small row (about 100 to 200 bytes): a 2560 × 1440 view of a main-map
window at 0.1875 needed more than 8,192 rows with 1.1 MB of payload. A window has at most about
42,000 commands (36,864 core tiles and up to about 4,900 bodies, plus the covered-tile masks and
roofs of a submission), well under the cap. At the rows' size the payload budget no longer binds;
the row count does, and each row is its own texture, whose driver memory these limits do not count.
A submission that alone needs more fails explicitly: a whole map large enough, at a zoom that is not
a power of two on a large view, or one needing more than the chunk cache's 1,024 chunks or 192 MiB. A
standalone draw item keeps a private cache with only its current row. Shared ART
textures retain the existing identity/dedup cache. Rows stop being sampled on
hide/retirement and are disposed with the cache; maximum viewport axis is 8192,
maximum row length is 8192, maximum source axis is 65534. Unsupported rotated/sheared/nonpositive
transforms, oversized domains or exceeded budget fail explicitly rather than
fall back to interpolated sampling. These are bounded renderer limits, not
measured performance claims. Measure full-area update costs before readiness.

Both target admission and later-occluder alpha use this same production rule.
The covered-tile masks and roofs ([roofs](roofs.md)) are drawn outside the ordered command list,
so a pixel observation also rejects a pixel where a mask (for a terrain target) or a roof that is
not faded out at all four corners has nonzero source alpha (ARC-RENDER-036). A partly
faded roof counts wherever its source alpha is nonzero, and covers are read from the Legacy frames
(as the later occluders are), so an HD edge wider than the Legacy one is not covered.
Independent tests must additionally compute their own source indices and assert
literal pixels on actual main and sub viewports, sparse features/holes, opaque
occluders, full/nonzero crops, X/Y reflections, signed/parity/fractional positions,
0.5/1/2 zoom, pan/restore and texture republication. Color compositing remains
subject to the existing two-byte framebuffer conversion tolerance; source-index
selection itself has NO tolerance. Original-runtime pixel parity stays
NOT_VERIFIED.
