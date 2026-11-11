# Arcanum.Godot viewer

`Arcanum.Godot` is the macOS ART frame and animation inspector introduced by ARC-RENDER-001 and extended by ARC-RENDER-002. It uses `Arcanum.Data` for decoding and `Arcanum.Presentation` for playback policy; both remain independent of Godot. It is a developer viewer, not an integrated game or gameplay animation runtime.

## Toolchain

The checked-in project targets `net8.0`, uses `Godot.NET.Sdk` 4.7.2, and uses the Godot 4.7.2 .NET editor with matching 4.7.2 .NET export templates. The project uses the Compatibility (`gl_compatibility`) renderer because this bounded 2D viewer does not require Forward+ or Mobile features and Compatibility provides the broadest desktop coverage.

Use the [shared toolchain](../../workflow/toolchain.md) for pinned SDK versions, canonical process-local paths and the `godot_canonical` function. The existing viewer-specific entry points are:

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
godot_canonical --headless --path src/Arcanum.Godot --build-solutions --quit
godot_canonical --headless --path src/Arcanum.Godot -- --self-test
godot_canonical --verbose --headless --path src/Arcanum.Godot --editor --build-solutions --quit
```

Before export, verify the verbose editor bridge selected the canonical SDK/executable as described in the shared toolchain. Keep the following viewer-specific export and evidence requirements.

The bounded Terrain diagnostic export is invoked through `tools/terrain-viewer/Godot/tools/validate-macos.sh`, not a bare editor export. After `ARCANUM_LOCAL_ROOT_ID` names the configured ignored artifact root, it holds the exact candidate export lease from export through source/exported self-tests, deep/strict codesign, immutable v2 evidence/inventory manifest validation, local `exported` registry registration, and the atomic v2 current pointer. This remains local-only and never accepts, publishes, or merges the candidate; a failed command leaves registry/current state unchanged.

## Viewer behavior and boundary

Open one DAT through the native file dialog. The viewer indexes only that archive and lists ART candidates whose raw names end in ASCII case-insensitive `.art\0`. Display names remove only the terminal NUL, render printable ASCII literally and every other byte as `\xHH`, and always include the original decimal ordinal. This preserves duplicate identity without guessing a code page or implementing archive precedence.

Only the selected `DatEntry` is read (bounded to 64 MiB), inspected once with `ArtDecoder.Inspect`, and retained only while it remains selected. `Inspect` reads checked headers and all frame metadata without eager RLE/RGBA decode. Physical groups are file group indices, not directions. A replacement or failed selection clears prior metadata, selector ranges, texture, and timeline before awaiting I/O, so old FPS/frame values cannot remain playable. The current frame is decoded from retained selected bytes only when a target changes; it does not reread the DAT on every playback tick.

## Animation inspection

The mouse-accessible toolbar provides Play/Pause, Stop, previous/next Step, a frame selector, Loop/Play once, and exactly four speed choices: 0.25×, 0.5×, 1×, and 2×. Source FPS is displayed raw from the ART header. Playback is enabled only for source FPS 1..120 unless the explicit Manual FPS control is enabled and set to 1..120; the displayed effective FPS always identifies the selected override. These are inspector limits, not ART properties or evidence of original runtime timing.

The timeline consumes monotonic `_Process(delta)` elapsed time, not draw calls. Invalid deltas (negative, NaN, or infinity) do nothing; finite stalls use direct loop modulo or bounded play-once completion rather than sequential catch-up decoding. The final frame in play-once is visible for its entire interval. Focus loss pauses without autoresume. Resize and fullscreen preserve timeline state. Manual frame selection pauses; group/palette changes retain playing/paused state and an in-range frame while restarting the interval.

`ViewerSession` owns immutable archive/entry and full frame-target generations. A target contains archive generation, entry generation, ordinal, target generation, group, frame, and palette; every semantic target change, including automatic timeline advance, mints a new target generation. `ViewerWorkCoordinator` is the sole background queue: it serializes DAT open, entry read plus `Inspect`, and frame decode as one active operation plus one replaceable latest pending operation. Opening a DAT or selecting an entry clears the prior snapshot before awaiting I/O, so old bytes cannot acquire a new generation. Completion is rechecked against the full target before every texture, canvas, status, metadata, failed-target, or pause effect. Current main-thread publication failures use the same exact-target recoverable-error latch as worker failures; `ViewerApp` validates geometry before resource/canvas mutation and disposes an uncommitted replacement on every error path. A failed target pauses and records one recoverable status without tick retry; a new target or valid selection can recover.

Raw hotspot alignment is a diagnostic projection: frame top-left is `(-HotX, -HotY)` around a common anchor. Signed hotspot values are supported; `OffsetX` and `OffsetY` remain displayed metadata and do not accumulate movement. Group bounds determine Fit so different frame dimensions do not pump zoom. This is not a claim of original game placement, semantic direction, mirroring, or action-frame behavior.

Decoder output is uploaded as packed `Image.Format.Rgba8` bytes with no channel swap, premultiplication, row flip, lighting, tint, grading, placement or mirroring. The canvas draws an alpha checkerboard, preserves aspect ratio, defaults to nearest 1:1, and supports bounded zoom, fit/reset and drag pan. Resizing and fullscreen do not change a selected archive, entry or selector state.

Archive lifetime belongs to the coordinator: replacement and close logically invalidate pending work, then dispose the prior archive only after its current worker operation has left the serialized pipeline. Cancellation is logical because bounded synchronous parser work is not preempted; obsolete completions are side-effect-free. UI error text deliberately does not include a selected file path.

## Automated and manual checks

The `--self-test` path creates a deterministic in-memory DAT containing duplicate ART names with distinct ordinals, a non-ART entry, plain and zlib ART payloads, one static and one eight-physical-group ART, raw and RLE frames, palette gaps/fallback, a slot-0-absent ART, malformed and unsupported ARTs, and a generated over-cap DAT entry. Its unequal-frame animation includes positive and negative signed hotspots plus a fixture-authored common-anchor colour marker. Barrier-controlled `ViewerSession`/`ViewerWorkCoordinator` tests record exact targets and prove that active A completion or error is side-effect-free after automatic B/C advancement, only C publishes on the drain thread, and observed open/read-inspect/decode concurrency is one. The production publication seam executes the same rollback-safe `FrameCanvas`/texture transaction as `ViewerApp`: the injected post-canvas hook is inside the rollback boundary, and assertions prove exact prior texture/layout/hotspot/pan/zoom/fit restoration plus exactly-once disposal/latch/status. It records independent manual frame, group, palette and Stop effects, splits real active Read/Inspect archive replacement (including usable replacement B) from delayed close disposal, and proves paused multi-frame ticks submit no requests, completions, resources, uploads, or publications. The extracted production selector/metadata/rate-control adapter is exercised for valid-invalid-valid entry and DAT recovery: no selection shows `No frame`, zero selector ranges/values, source FPS 0 and blocked effective FPS; a valid replacement restores inspected controls including palette slots 0–3. Static zero uploads, the slot-0-absent recovery, bounded entry rejection, decoder bytes, the real Godot `Image` / `ImageTexture` RGBA8 round trip, dimensions, top-left row-major orientation, and `FrameCanvas` nearest/zoom/fit calculation/reset state remain covered. It does not substitute for interactive graphical evidence.

Generate the same synthetic DAT outside the checkout for the file-picker smoke without committing it:

```sh
godot_canonical --headless --path src/Arcanum.Godot -- --write-synthetic-dat /path/to/synthetic-viewer.dat
```

For an opt-in local data check, keep the game data unchanged and supply `DAT_ROOT` only to the existing Data test command:

```sh
DAT_ROOT=/path/to/Arcanum "$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build --filter "FullyQualifiedName~DatArchiveTests&TestCategory=RealData"
```

Never put the path, game assets, extracted bytes or screenshots in Git. Interactive smoke remains required: launch the exported app outside the editor, open a synthetic DAT through the native picker, select duplicate ordinals, switch group/frame/palette including the slot-0-absent recovery, use 1:1/zoom/fit/pan, resize twice, enter/exit fullscreen, attempt malformed and over-cap input, then reopen a valid sample. A headless build is not a UI smoke.

## HD before/after review (ARC-RENDER-015)

The viewer reviews any HD pack in the [ARC-DATA-014 format](../../formats/hd-pack.md) against the
opened DAT: network, generative, algorithm or external method, 2× or 4×, soft or Legacy
alpha. It is how the owner rates the output of a method (for example ARC-RES-038) and
records approve/reject marks; nothing here draws the game (that is ARC-RENDER-014).

- **Open HD pack…** opens one pack directory. The summary line shows id, scale, method
  kind, tool and model labels, alpha mode and the review counts. The right-hand list holds
  the pack's sprite frames in manifest order (`✓` approved, `✗` rejected, `·` unreviewed;
  at most 2,000 rows, N/P reach the rest; **Unreviewed only** filters it). Selecting a row
  finds the ART in the open DAT by the name hash, checks that the DAT's bytes are the
  pack's (the next DAT entry of that name is tried; another DAT, a mod or another game
  version is reported), and shows the frame's group, frame and palette.
- Every frame the viewer shows, including during playback, is looked up in the pack
  (ART payload hash, view, group, frame, effective palette; a shorter view of the same
  bytes is decoded from its own view) on a background task; at most one lookup runs and
  only the latest pending one follows. Built HD textures are cached (256 frames, 1 GiB),
  so an animation plays in HD once each frame was seen. The HD texture has the Legacy
  frame's size override: it takes exactly the Legacy frame's place and shows its detail
  from zoom N upward.
- Views: **Legacy**, **HD**, **Flip** (alternates original then HD every *Flip every*
  seconds, 0.1–60) and **Split** (original left, HD right; Fit fits one frame, so zoom out
  once for both). The caption on the canvas
  names the side shown, the pack frame and its mark, and why a frame has no HD:
  not in the pack, other ART bytes, a palette slot that falls back, an undecodable view,
  or the pack's refusal (for example `SourceMismatch` or an invalid file). A frame whose
  recipe alpha check fails is still shown, marked "alpha check failed: the game would draw
  Legacy". Review marks never hide a frame here, so rejected frames can be looked at again.
- Marks: **A** approve, **R** reject, **U** unmark the pack frame on screen (with
  **Next after mark**, the next frame follows; with **Unreviewed only**, the next
  unreviewed one). Each mark is written at once into the pack's `manifest.json`
  (`HdPackReview.Save`): only `review` values change, the rewritten manifest is parsed
  and checked first and then replaces the old one by an atomic rename inside the pack;
  a manifest changed on disk since the pack was opened, or one that is a link, is never
  written. A marked pack has a new identity (manifest hash); frame files are untouched.
  Every mark rewrites the whole manifest, which suits review slices, not a whole-game
  manifest.
- Other keys: **N/P** (Page Down/Up) next/previous pack frame, **H** Legacy/HD, **F** flip,
  **S** split, **F12** capture, **F10** presentation (only the canvas and a short caption,
  for the demo). Letter and page keys do nothing while a text or number field has focus;
  F10 and F12 always work. A pack frame whose DAT entry or frame fails to load tries the
  next DAT entry of that name.

### Local captures and the demo clip

**F12** writes `<pack id>-e<entry>-<UTC time>-window.png` (the window) and, when the frame
has HD, `…-pair.png` (Legacy replicated N× nearest, a gap of 4·N pixels, the HD frame;
transparent background, HD resolution). File names carry no path. The folder is
`--hd-capture-dir`, otherwise `local/hd-review/` of the checkout when run from source,
otherwise `user://hd-review` in an exported viewer. Run from source, a capture folder inside
the checkout must be under its ignored `local/` (a capture is refused otherwise). Captures, clips and packs are
original-derived pixels: they stay local and never go into git, a PR or an attachment.

Startup options (after `--`; paths must be absolute, Godot runs in the project directory):
`--dat FILE`, `--hd-pack DIR`, `--hd-entry INDEX`, `--hd-view legacy|hd|flip|split`,
`--hd-flip-seconds S` (0.1–60), `--hd-capture-dir DIR`, `--presentation`, and
`--hd-demo SECONDS` (0.5–600 per frame and at least twice the flip period; walks the
approved frames, or every frame not rejected when none is approved, in presentation and
flip view, then quits; `--hd-demo-capture` also captures each frame once while HD is
shown). An invalid or missing value, a pack or DAT that does not open in a demo, or a demo
that could show no frame exits with code 1 instead of leaving a recording running. A clip with on-the-fly switching is recorded with
Godot's Movie Maker, which works from source and in the exported viewer:

```sh
godot_canonical --path src/Arcanum.Godot --write-movie "$PWD/local/hd-review/demo.avi" --fixed-fps 30 -- \
  --dat /path/to/arcanum1.dat --hd-pack "$PWD/local/hd-packs/<pack>" --hd-demo 4 --hd-flip-seconds 1 \
  --hd-demo-capture --hd-capture-dir "$PWD/local/hd-review"
```

Movie Maker writes MJPEG AVI (or a PNG sequence when the path ends in `.png`); the
recording runs at the fixed frame rate regardless of wall time. Without `--hd-demo` the
same command records a manual session, with H/F/S switching live. Convert the AVI
locally for players that do not read MJPEG.

The self-test builds a synthetic 2× pack in a temporary directory from the synthetic
DAT's static ART and checks the lookup by ART hash, stale-result suppression, the cached
main-thread answer, the flip clock, the HD canvas layer (a new Legacy frame never shows the
previous HD frame, a failed publication restores it), how a lookup result ends locating a
pack frame (only the target frame on screen concludes it), the pair image at 2× and 4×
and its file name, the capture folder rule, and a manifest mark. The manifest writer, review binding and index have synthetic MSTest
coverage in `tests/Arcanum.Data.Tests/Hd/HdReviewTests.cs`.

## Local export

`src/Arcanum.Godot/export_presets.cfg` holds the macOS debug preset. Export only into an ignored configured build root (for example `${ARCANUM_BUILD_ROOT:-artifacts/builds/ARC-RENDER-001/<candidate-id>}`), never into source control:

```sh
godot_canonical --headless --path src/Arcanum.Godot --export-debug "macOS debug" "$ARCANUM_BUILD_ROOT/ArcanumSteamwovenViewer.app"
```

Use only the matching templates. A local development export may be ad-hoc signed by Godot if the preset requires it; it is not notarized or publicly distributable. If Gatekeeper or macOS blocks launch, record the failure without weakening security.
