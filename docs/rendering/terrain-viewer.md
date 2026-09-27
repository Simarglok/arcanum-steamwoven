# Terrain viewer (ARC-RENDER-003)

`tools/terrain-viewer` is a standalone macOS Godot diagnostic viewer for exactly
one fixed Crash Site terrain sector. It is not a game renderer: 4,096 resolved
terrain records (3,033 tiles and 1,063 diagnostic facades) are terrain-only
coverage, not the full Crash Site and not original visual parity.

## Inputs and launch

The viewer has no picker, archive discovery, precedence, PATCH/dif/loose-file
policy, or extraction behavior. It requires caller-selected inputs and a safe,
non-path source label:

    "$HOME/.local/share/dotnet/dotnet" run --project tools/terrain-viewer/Godot -- \
      --module PATH --resource PATH --source-id LABEL

It validates the fixed Crash Site profile before and after its read: fixed
module/resource sizes and SHA-256 values, sector selector, all 4,096 outcomes,
zero issues, the 3033/1063 kind split, and the 457 recovered cells / 86
recovered lookup IDs. `--expect-module-sha256` and `--expect-resource-sha256`
are optional additional equality constraints, not replacements. Errors expose
only stable categories and aggregate counts; paths, entry names, exception text,
archive bytes, and sampled colours are deliberately not shown.

## Diagnostic rendering contract

Core is Godot-independent. One coordinator owns both caller-owned seek-based
`DatArchive` objects, serializes inspection, selected-frame decoding, pre/post
fingerprints, and disposal, and records maximum observed archive concurrency.
It atomically publishes only after all records and deduplicated selected frames
succeed. The exact selected-frame identity is the source label, DAT ordinal,
flags, declared size, logical entry name, group, frame, and requested palette.
The cache preflights each distinct decoded frame against 4,096 keys and 256 MiB
of packed RGBA before retaining it. Camera actions never access archives or grow
that cache. Reload, failure, and exit clear textures exactly once; failed partial
uploads are rolled back.

`TerrainGeometry.Place` is the only owner of projection, the one-pixel X anchor,
tile 78x40 / facade 80x40 footprint, crop, and raw tile flip. The adapter draws
only its returned integer source and destination rectangles with nearest,
straight RGBA8 and ordinary alpha blending. There is no Y flip, inferred facade
flip, ART hotspot/offset application, object-suffix interpretation, lighting,
shadows, grading, mipmaps, or HD treatment. Palette index zero remains
transparent. Paint order is explicitly **Editor diagnostic order**: tiles before
facades, then local Y, local X, and ordinal; later commands win. That is a
project diagnostic policy, not a claim about original facade/scenery occlusion.

## Controls and window behavior

The world canvas is viewport-independent under `Camera2D`; the status HUD is a
separate `CanvasLayer`. The initial view fits the complete diagnostic bounds
with 32-pixel margins. Zoom is bounded to 0.125–8.0 with nearest 1/1024 fit
rounding. Arrow/WASD pans, wheel and `+`/`-` zoom, and left drag pans. `R`
resets initial Fit and `F` recomputes Fit. Resize recomputes Fit only while in
Fit mode; manual pan/zoom survives resize. F11 toggles normal fullscreen and
Escape restores windowed mode. The resizable default is 1280x800. There is no
controller behavior.

## Verification and evidence status

`--self-test` creates a public in-memory asymmetric 8-bpp ART fixture, decodes
it through `ArtDecoder`, uploads it through the production RGBA8 texture path,
and constructs the canvas plan through the same `TerrainScenePlan` placement and
ordering path used for loaded terrain records. Its hand-authored landmarks cover
transparent palette zero, row-major orientation, source crop, the one-pixel
anchor destination, 78x40 tile and 80x40 facade footprints, tile-only X flip,
unflipped facade command, layer/Y/X/ordinal overlap winner, and border
transparency. The independently stated geometry landmarks include all sector
corners and local `(30,32) -> (80,1240)` before the separate X anchor.
A non-headless Godot run reads the `SubViewport` pixels; the dummy headless
renderer cannot read a `ViewportTexture`, so its output explicitly records
`graphical readback NOT_VERIFIED` rather than claiming rendered pixels.

`--native-smoke` uses explicit original inputs, drives the production scene at
1280x800 and 1920x1080, injects keyboard/wheel/drag/reset/fit/F11/Escape events,
checks camera state, command/cache count, fixed HUD coordinates, fullscreen
restoration, manual-resize persistence, and 100 input-driven no-growth reset
cycles. It requires each viewport readback to have exactly the requested
dimensions and validates a terrain-texture landmark transformed through the
active camera; an opaque clear colour or HUD alone cannot pass. A display clamp
or unreadable terrain landmark is a failed automated smoke, not a size PASS.
With
`--native-smoke-screenshot PATH`, it writes `PATH` with `-1280x800` and
`-1920x1080` inserted before the extension. These game-graphics screenshots
remain local and ignored.

The previously retained blank source/export PNGs are failed historical evidence,
not smoke passes. Current exact-head local build evidence lives below
`artifacts/builds/ARC-RENDER-003/<commit>/` with the app, `manifest.json`, and
an `evidence/` directory; `artifacts/builds/ARC-RENDER-003/current.json` points
to the exact commit-bound manifest/app. It is ignored and must not be committed
or attached. Evidence distinguishes synthetic checks, original automated checks,
original visual inspection, and physical/readability. Only the first two are
automatable here; local visual and physical/readability observations remain
explicitly NOT_VERIFIED unless a human records them.

## Provenance and limitations

This uses the project's independently authored Data APIs (`DatArchive`,
`TerrainLoader`, `TerrainGeometry`, and `ArtDecoder`). The documented diagnostic
placement conventions are attributed CE-derived specification evidence at the
pinned research reference; no CE code/runtime or mechanical translation is
included, and this makes no clean-room or independent-corroboration claim.
Original inputs, extracted frames, archive paths, screenshots, and toolkit
payloads remain local. The viewer does not implement objects, scenery, roofs,
collision, navigation, gameplay, dialogue, controller UX, archive precedence,
or original scene/visual parity.
