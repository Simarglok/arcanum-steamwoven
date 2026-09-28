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

Core is Godot-independent. Each coordinator request creates one caller-owned,
idempotently disposable archive operation owning both seek-based `DatArchive`
objects. The coordinator serializes inspection, selected-frame decoding,
pre/post fingerprints, and per-load success/failure disposal before releasing
its gate, and records maximum observed archive concurrency.
It atomically publishes only after all records and deduplicated selected frames
succeed. The exact selected-frame identity is the source label, DAT ordinal,
flags, declared size, logical entry name, group, frame, and requested palette.
The cache preflights each distinct decoded frame against 4,096 keys and 256 MiB
of packed RGBA before retaining it. Camera actions never access archives or grow
that cache. After successful upload, the app retains only the safe source label
and scalar decoded-count/load-time HUD metrics: neither the completed load
result nor its decoded RGBA dictionary is scene-lifetime app state. The
scene-lifetime texture dictionary is the sole cache. Reload, failure, and exit
clear textures exactly once. The graphical self-test injects an upload failure,
tracks per-texture reload/failure/exit disposal, and deterministically inspects
the app field types to prove they cannot retain decoded texture data; it makes
no nondeterministic GC claim. Focused barrier tests prove per-load archive
lifetime, serialization, and delayed stale success/failure cannot mutate a
newer scene or HUD.

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
separate `CanvasLayer`. The fixed-screen ready HUD displays diagnostic mode,
aggregate counts, safe source label, current bounded zoom, decoded count,
explicit cache count, and load milliseconds; it refreshes after pan, zoom,
fit/reset, and resize. The initial view fits the complete diagnostic bounds
with 32-pixel margins. Zoom is bounded to 0.125–8.0 with nearest 1/1024 fit
rounding. Arrow/WASD pans, wheel and `+`/`-` zoom, and left drag pans. `R`
resets initial Fit, `F` recomputes Fit, and `L` reloads through the same
generation-aware load route. Resize recomputes Fit only while in
Fit mode; manual pan/zoom survives resize. F11 toggles normal fullscreen and
Escape restores windowed mode. The resizable default is 1280x800. There is no
controller behavior.

## Verification and evidence status

`--self-test` creates a public in-memory 82x42 asymmetric 8-bpp ART fixture, decodes
it through `ArtDecoder`, uploads it through the production RGBA8 texture path,
and constructs the canvas plan through the same `TerrainScenePlan` placement and
ordering path used for loaded terrain records. Its hand-authored pixels cover
transparent palette zero, row-major orientation, source crop, the one-pixel
anchor destination, 78x40 tile and 80x40 facade footprints, tile-only X flip,
unflipped facade command, borders, and separate layer/Y/X/ordinal winners. The independently stated geometry landmarks include all sector
corners and local `(30,32) -> (80,1240)` before the separate X anchor.
A non-headless Godot run reads the `SubViewport` pixels; the dummy headless
renderer cannot read a `ViewportTexture`, so its output explicitly records
`graphical readback NOT_VERIFIED` rather than claiming rendered pixels.

`--native-smoke` uses explicit original inputs, validates seven fixed
representative ordinals through isolated production canvases, then drives the production scene at
1280x800 and 1920x1080, injects keyboard/wheel/drag/reset/fit/F11/Escape events,
checks Camera2D readback, command geometry/cache count, HUD global coordinates,
fullscreen restoration where supported, manual-resize persistence, reload, and
100 input-driven pan/zoom/reset/resize cycles. It requires each viewport readback to have exactly the requested
dimensions and validates a terrain-texture landmark transformed through the
active camera; an opaque clear colour or HUD alone cannot pass. A display clamp
or unreadable terrain landmark is NOT_VERIFIED, never a size PASS. Fullscreen
which cannot transition is also NOT_VERIFIED; failed restoration is a failure.
With
`--native-smoke-screenshot PATH`, it writes filenames containing both requested
and actual dimensions. These game-graphics screenshots
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

## Reproducible local verification

Set `DOTNET=/path/to/canonical/dotnet`, `GODOT=/path/to/Godot`, and provide the
caller-selected `MODULE`, `RESOURCE`, and safe `SOURCE_ID` only in the local
shell. Do not place those paths in a manifest, commit, or issue.

    "$DOTNET" restore tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --locked-mode
    "$DOTNET" build tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --no-restore
    "$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build
    "$GODOT" --headless --path tools/terrain-viewer/Godot -- --self-test
    "$GODOT" --path tools/terrain-viewer/Godot -- --self-test
    "$GODOT" --path tools/terrain-viewer/Godot -- --native-smoke --module "$MODULE" --resource "$RESOURCE" --source-id "$SOURCE_ID"

After committing the source candidate, export it to the ignored
`artifacts/builds/ARC-RENDER-003/<commit>/ArcanumTerrainViewer.app`, run its
`--self-test` and `--native-smoke` with the same local arguments, then run
`codesign --verify --deep --strict --verbose=2` on that app. Record source and
export results, requested/actual dimensions, measured load milliseconds,
distinct texture count, checked RGBA bytes, maximum concurrency, and pre/post
fingerprint equality in the ignored schema-v1 manifest. `current.json` may be
updated only to that exact commit-bound directory.

For the macOS export, keep the SDK selection process-local. This avoids an
editor-configured alternate SDK without changing global Godot or .NET settings:

    SOURCE_HEAD=$(git rev-parse HEAD)
    BUILD_ROOT="artifacts/builds/ARC-RENDER-003/$SOURCE_HEAD"
    env -i HOME="$HOME" USER="$USER" TMPDIR="$TMPDIR" LANG="${LANG:-en_US.UTF-8}" \
      PATH="$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin" \
      DOTNET_ROOT="$HOME/.local/share/dotnet" DOTNET_HOST_PATH="$HOME/.local/share/dotnet/dotnet" \
      DOTNET_MULTILEVEL_LOOKUP=0 __GODOT_SHELL_ENV_SET=1 "$GODOT" --headless \
      --path tools/terrain-viewer/Godot --export-debug "macOS debug" "$BUILD_ROOT/ArcanumTerrainViewer.app"
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" --headless -- --self-test
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" -- --self-test
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" -- \
      --native-smoke --native-smoke-screenshot "$BUILD_ROOT/evidence/export-native-smoke.png" \
      --module "$MODULE" --resource "$RESOURCE" --source-id "$SOURCE_ID"
    codesign --verify --deep --strict --verbose=2 "$BUILD_ROOT/ArcanumTerrainViewer.app"
    python3 -m json.tool "$BUILD_ROOT/manifest.json"
    python3 -m json.tool artifacts/builds/ARC-RENDER-003/current.json

Before treating an export as usable, inspect its Godot export log for canonical
SDK `8.0.425` and no Homebrew SDK path, and verify `libhostfxr.dylib` exists in
both exported architecture payloads. Run source and exported native smoke with
separate ignored screenshot bases and record requested and actual readback
dimensions. A clamped display remains `NOT_VERIFIED`, not a 1920x1080 pass.

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
