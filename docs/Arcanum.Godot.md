# Arcanum.Godot viewer

`Arcanum.Godot` is the ARC-RENDER-001 presentation boundary. It is a macOS developer viewer, not a game or animation runtime. It references `Arcanum.Data`; Data does not reference Godot.

## Toolchain

The checked-in project targets `net8.0`, uses `Godot.NET.Sdk` 4.7.2, and uses the Godot 4.7.2 .NET editor with matching 4.7.2 .NET export templates. The project uses the Compatibility (`gl_compatibility`) renderer because this bounded 2D viewer does not require Forward+ or Mobile features and Compatibility provides the broadest desktop coverage.

The toolchain is resolved from the shared user tool roots described by the local registry, not from `local/` in a checkout. Use the canonical executable paths for direct .NET commands and this POSIX-shell function for every Godot editor, build, self-test, synthetic-fixture, and export invocation:

```sh
DOTNET_ROOT="$HOME/.local/share/dotnet"
DOTNET="$DOTNET_ROOT/dotnet"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"

godot_canonical() {
  env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" \
    PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" "$GODOT" "$@"
}

"$DOTNET" restore --locked-mode
"$DOTNET" build --no-restore
godot_canonical --headless --path src/Arcanum.Godot --build-solutions --quit
godot_canonical --headless --path src/Arcanum.Godot -- --self-test
```

The `__GODOT_SHELL_ENV_SET=1` sentinel prevents Godot 4.7.2 editor mode from importing shell startup files that can replace the launch `PATH`. The editor build bridge must be run verbosely and must report SDK `8.0.425` from `$HOME/.local/share/dotnet/sdk/8.0.425` before an export is accepted. Its `Running:` build line must use `$HOME/.local/share/dotnet/dotnet`, with no `/opt/homebrew` build or publish line. A direct `"$DOTNET" build` alone is not evidence that the editor/export bridge selected the same SDK:

```sh
godot_canonical --verbose --headless --path src/Arcanum.Godot --editor --build-solutions --quit
```

If that command reports another `dotnet` executable or SDK, stop before export; do not replace a retained SDK, alter global shell startup files, use `TERM` as a workaround, or weaken macOS security to work around it. The local Kanban-only toolchain registry records observed non-owned installations and the current resolution state.

Godot C# prerequisites: https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html

Renderer selection: https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html

macOS export: https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_macos.html

## Viewer behavior and boundary

Open one DAT through the native file dialog. The viewer indexes only that archive and lists ART candidates whose raw names end in ASCII case-insensitive `.art\0`. Display names remove only the terminal NUL, render printable ASCII literally and every other byte as `\xHH`, and always include the original decimal ordinal. This preserves duplicate identity without guessing a code page or implementing archive precedence.

Only the selected `DatEntry` is read (bounded to 64 MiB), inspected once with `ArtDecoder.Inspect`, and retained only while it remains selected. `Inspect` reads checked headers and all frame metadata without eager RLE/RGBA decode. Physical groups are file group indices, not directions. The current frame is decoded from that retained byte array only when selection changes; it does not reread the DAT on every playback tick.

## Animation inspection

The mouse-accessible toolbar provides Play/Pause, Stop, previous/next Step, a frame selector, Loop/Play once, and exactly four speed choices: 0.25×, 0.5×, 1×, and 2×. Source FPS is displayed raw from the ART header. Playback is enabled only for source FPS 1..120 unless the explicit Manual FPS control is enabled and set to 1..120; the displayed effective FPS always identifies the selected override. These are inspector limits, not ART properties or evidence of original runtime timing.

The timeline consumes monotonic `_Process(delta)` elapsed time, not draw calls. Invalid deltas (negative, NaN, or infinity) do nothing; finite stalls use direct loop modulo or bounded play-once completion rather than sequential catch-up decoding. The final frame in play-once is visible for its entire interval. Focus loss pauses without autoresume. Resize and fullscreen preserve timeline state. Manual frame selection pauses; group/palette changes retain playing/paused state and an in-range frame while restarting the interval.

`ViewerSession` owns immutable archive/entry and full frame-target generations. A target contains archive generation, entry generation, ordinal, target generation, group, frame, and palette; every semantic target change, including automatic timeline advance, mints a new target generation. `ViewerWorkCoordinator` is the sole background queue: it serializes DAT open, entry read plus `Inspect`, and frame decode as one active operation plus one replaceable latest pending operation. Opening a DAT or selecting an entry clears the prior snapshot before awaiting I/O, so old bytes cannot acquire a new generation. Completion is rechecked against the full target before every texture, canvas, status, metadata, failed-target, or pause effect. Images and textures are created, updated, and disposed only by the `ViewerApp` main-thread publication sink; geometry is validated before texture creation and an uncommitted replacement is disposed on every error path. A decode failure pauses and records a recoverable status; that same failed target is not retried every tick, while a new valid selection can recover.

Raw hotspot alignment is a diagnostic projection: frame top-left is `(-HotX, -HotY)` around a common anchor. Signed hotspot values are supported; `OffsetX` and `OffsetY` remain displayed metadata and do not accumulate movement. Group bounds determine Fit so different frame dimensions do not pump zoom. This is not a claim of original game placement, semantic direction, mirroring, or action-frame behavior.

Decoder output is uploaded as packed `Image.Format.Rgba8` bytes with no channel swap, premultiplication, row flip, lighting, tint, grading, placement or mirroring. The canvas draws an alpha checkerboard, preserves aspect ratio, defaults to nearest 1:1, and supports bounded zoom, fit/reset and drag pan. Resizing and fullscreen do not change a selected archive, entry or selector state.

Archive lifetime belongs to the coordinator: replacement and close logically invalidate pending work, then dispose the prior archive only after its current worker operation has left the serialized pipeline. Cancellation is logical because bounded synchronous parser work is not preempted; obsolete completions are side-effect-free. UI error text deliberately does not include a selected file path.

## Automated and manual checks

The `--self-test` path creates a deterministic in-memory DAT containing duplicate ART names with distinct ordinals, a non-ART entry, plain and zlib ART payloads, one static and one eight-physical-group ART, raw and RLE frames, palette gaps/fallback, a slot-0-absent ART, malformed and unsupported ARTs, and a generated over-cap DAT entry. Its unequal-frame animation includes positive and negative signed hotspots plus a fixture-authored common-anchor colour marker. Barrier-controlled `ViewerSession`/`ViewerWorkCoordinator` tests prove that an active A completion or error becomes side-effect-free after automatic B/C target advancement, only C publishes through the main-thread sink, entry commit occurs once, and observed read/inspect/decode concurrency is one. They also cover static/paused zero uploads, the slot-0-absent recovery, bounded entry rejection, decoder bytes, the real Godot `Image` / `ImageTexture` RGBA8 round trip, dimensions, top-left row-major orientation, and `FrameCanvas` nearest/zoom/fit calculation/reset state. It does not substitute for interactive graphical evidence.

Generate the same synthetic DAT outside the checkout for the file-picker smoke without committing it:

```sh
godot_canonical --headless --path src/Arcanum.Godot -- --write-synthetic-dat /path/to/synthetic-viewer.dat
```

For an opt-in local data check, keep the game data unchanged and supply `DAT_ROOT` only to the existing Data test command:

```sh
DAT_ROOT=/path/to/Arcanum "$DOTNET" test --no-build --filter "TestCategory=RealData"
```

Never put the path, game assets, extracted bytes or screenshots in Git. Interactive smoke remains required: launch the exported app outside the editor, open a synthetic DAT through the native picker, select duplicate ordinals, switch group/frame/palette including the slot-0-absent recovery, use 1:1/zoom/fit/pan, resize twice, enter/exit fullscreen, attempt malformed and over-cap input, then reopen a valid sample. A headless build is not a UI smoke.

## Local export

`src/Arcanum.Godot/export_presets.cfg` holds the macOS debug preset. Export only into an ignored configured build root (for example `${ARCANUM_BUILD_ROOT:-artifacts/builds/ARC-RENDER-001/<candidate-id>}`), never into source control:

```sh
godot_canonical --headless --path src/Arcanum.Godot --export-debug "macOS debug" "$ARCANUM_BUILD_ROOT/ArcanumSteamwovenViewer.app"
```

Use only the matching templates. A local development export may be ad-hoc signed by Godot if the preset requires it; it is not notarized or publicly distributable. If Gatekeeper or macOS blocks launch, record the failure without weakening security.
