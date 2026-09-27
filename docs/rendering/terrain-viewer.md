# Terrain viewer (ARC-RENDER-003)

`tools/terrain-viewer` is a standalone macOS Godot diagnostic viewer for exactly
one fixed Crash Site terrain sector.  It is not a game renderer and its 4,096
commands are terrain-only diagnostic coverage, not the full Crash Site and not
original visual parity.

Run it only with explicit inputs:

    "$HOME/.local/share/dotnet/dotnet" run --project tools/terrain-viewer/Godot -- --module PATH --resource PATH --source-id LABEL

The viewer always requires the documented fixed sizes and SHA-256 fingerprints;
the optional expected hashes are additional equality checks.  `source-id` is a
safe label, not a path.  Errors expose stable category and aggregate counts,
not paths, archive names, decoded bytes, or exception details.

The Core coordinator is the only archive owner and serializes opening,
inspection, selected-frame decode, post-read hashing, and disposal.  It builds
an atomic scene only after all 4,096 outcomes and deduplicated selected frames
are available within the 4,096-key / 256 MiB RGBA budget.  Camera movement,
resize, fit, redraw, and HUD never read archives or decode assets.

Rendering preserves `TerrainGeometry.Place` source/destination/flip results.
It uses straight RGBA8 and nearest filtering.  Its paint order is explicitly
`Editor diagnostic order`: tiles before diagnostic facades, then local Y, local
X, and ordinal.  This makes no claim about original scenery or facade occlusion.

`--self-test` exercises the production RGBA8 upload path with a transparent,
asymmetric synthetic row-major fixture.  Real-data, native-display, fullscreen,
and physical readability checks remain opt-in local evidence; headless success
does not establish graphical or visual parity.

For local native smoke evidence only, `--native-smoke` drives the production
canvas through both required window sizes and input/fullscreen events after a
complete explicit-input load.  Its optional `--native-smoke-screenshot PATH`
must target ignored local evidence; it is never a viewer feature or source-data
export mechanism.
