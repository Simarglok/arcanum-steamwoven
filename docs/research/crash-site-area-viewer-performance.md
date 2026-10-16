# Crash Site area viewer performance (diagnosis, 2026-10-15)

Measured diagnosis of the `crash-site-area` Terrain Viewer mode
([viewer contract](../rendering/crash-site-area-viewer.md), merged in PR #68) at
`origin/main` `9e7dfd6`. It records where load time and frame time go and ranks
suspected causes. It changes no code and is not a fix, a performance contract,
a budget or a regression gate. Numbers are single observations on one machine.

**Updates.** Cause L1 is addressed by the owner frame batch (see
[L1 result](#l1-result-frame-batch)); F1 and F5 by the bounded draw item pool,
actor-framed first publication and cached upright order, and F4 partly by the
cached N/A count (see [F1/F4/F5 result](#f1f4f5-result-pool-startup-hud-order)).
The other causes are unchanged.

## Scope and method

- Machine: Apple M4 Pro, macOS, Godot 4.7.2 .NET (`gl_compatibility`, OpenGL
  over Metal), .NET SDK 8.0.425, window 1280×800 on a 2560×1440 display.
- Inputs: the authenticated original profile (module, Resource1, Resource2,
  NormalGameMobile, 56 prototypes) from a local installation, page-cached after
  the first run. No original content or local path is recorded here.
- Load: an ordinary source launch (`godot_canonical --path tools/terrain-viewer/Godot`,
  Debug C# build as used by source runs and by the `--export-debug` preset) with
  wall-clock timestamps on the existing `ARC-RENDER-007 load stage=` lines and
  `--print-fps`; macOS `sample` of the process during frame copy and at idle.
- Load phases: a temporary, uncommitted MSTest probe in the viewer test project
  that calls the production `AreaOriginalValidator.OpenFiles`, `LoadStrict`,
  `AreaScenePlan.Create`, `AreaLoadOperation.CopyFrames`, traversal and actor
  steps with stopwatches, in Debug and Release, plus temporary timestamps at
  `AreaBudget.BeginPhase`.
- Frames: temporary, uncommitted Stopwatch counters in `TerrainCanvas.UpdateAreaVisible`,
  `AreaDrawItem`, `ProcessWorld` and the HUD refresh, with a scripted camera that
  moves the view centre horizontally every frame at fixed zoom (4, 16, 64 and 1
  world pixels per frame at zoom 1, 0.5, 0.25 and 2) and Godot `Performance`
  monitors (draw calls, objects, nodes). Two runs agreed except at zoom 2, where
  the first run's camera stopped at the envelope clamp; the second run is quoted.

## Load: 318 s from launch to SOURCE READY

| Stage (Debug source run) | Wall time | Share |
| --- | ---: | ---: |
| `OpenFiles` (one SHA-256 pass of every input, DAT indexes) | 1.0 s | 0.3% |
| `LoadStrict` (two more full SHA-256 passes + strict build) | 24.4 s | 7.7% |
| `AreaScenePlan.Create` (37,363 commands) | 0.1 s | — |
| `CopyFrames` (1,975 unique frames, 25,914,936 RGBA bytes) | 290.2 s | **91.2%** |
| Traversal validation, actor source, `VerifyFile` ×3 | 1.2 s | 0.4% |
| Main-thread publication (`PublishAreaReady`) | 1.3 s | 0.4% |

The load runs on a worker thread; the UI stays responsive (≈1,000 FPS while
loading). The load thread is CPU-bound: during `CopyFrames`, 85% of its samples
are in `AccelerateCrypto_SHA256_compress` and most of the rest in `pread`.

**Cause L1 — two whole-archive SHA-256 passes per selected frame (most of the 91% `CopyFrames` share).**
`AreaDataOwner.AcquireSelectedFrame` → `DecodeDescriptor`
([AreaAppearance.cs](../../src/Arcanum.Data/Area/AreaAppearance.cs)) calls
`Authenticate(identity)` before and after decoding every frame, and
`Authenticate` hashes the complete source archive. 1,956 frames come from
Resource2 (147,991,122 bytes) and 19 from Resource1 (447,470,816 bytes):
2 × (1,956 × 147,991,122 + 19 × 447,470,816) = 595.9 GB. The owner's own
`ReadActual` counter grows from 3,037,162,787 to 599,141,693,105 bytes
(+596.1 GB) across the copy; the 159,370,046-byte difference equals the growth
of `FrameStored`, i.e. the frame entries themselves. Independently measured
single-pass hash rates on this machine range from ≈1.9 GB/s (one Resource2 pass
in the probe) to ≈2.9 GB/s (an I0 pass over all inputs), which puts these
passes at ≈205–310 s, i.e. most or all of the 290 s phase; the sampler agrees
(85% SHA-256 compression plus `pread`). The remaining per-frame work (entry read
and payload hash, ART decode, pixel hash, linear `Visuals.SingleOrDefault`
descriptor lookup) was not timed separately.
The whole-load cost therefore scales with *frames × archive size*, not with the
25.9 MB actually retained.

**Cause L2 — Debug JIT for the strict build (≈6% of load, ≈4× slower than Release).**
Same probe, same inputs:

| Phase | Debug | Release |
| --- | ---: | ---: |
| `OpenFiles` | 0.73 s | 0.77 s |
| `LoadStrict` total | 20.6–23.9 s | 5.9–6.2 s |
| ↳ I0 / I7 authentication pass (each) | 0.33 s | 0.34 s |
| ↳ I1 selection | 0.46 s | 0.41 s |
| ↳ I3 sectors (terrain records, statics, decode and pixel hash of selected visuals) | 18.9 s | 4.9 s |
| ↳ I4 / I5 aggregate and correspondence | 0.56 s | 0.20 s |
| `CopyFrames` | 260 s | 240 s |

Both Release probe runs of `LoadStrict` ran alone; the full Release and Debug
probes' `CopyFrames` overlapped a concurrent viewer load, so those two copy
times are only indicative. Source runs and the `macOS debug` export preset both
run Debug assemblies
(optimizer disabled). I3 holds per-byte loops (for example `HashPixels` copies
through the `IReadOnlyList<byte>` indexer) that are sensitive to that; which of
I3's sub-steps dominates was inferred from code, not measured.

**Minor load items.** Each frame is decoded and pixel-hashed twice (once in I3
to build the descriptor, again in `CopyFrames`). Every input is fully hashed
three times by the owner (Open, I0, I7: ≈1 s total) and three archives once more
by `VerifyFile` (0.5 s); `CardinalTraversalInspector` reopens and validates its
own sources (0.7–0.8 s).

## Frame time

Debug source run, 1280×800, measured per scripted phase. "Visible sprites" is
`AreaVisibleCount` (area commands, excluding the controlled actor) averaged over
recomputes; draw calls are averaged over all frames of the phase, so the panning
zoom 0.25 row (a recompute on 48 of 94 frames) is not directly comparable.

| Phase | Visible sprites | Avg frame | FPS | `UpdateAreaVisible` per frame | Draw calls |
| --- | ---: | ---: | ---: | ---: | ---: |
| Idle, WORLD follow, persisted zoom 1.27 (code default 2) | 474 | 5.7 ms | 176 | ≈0 (cached) | 486 |
| Pan, zoom 2 | 158 | 33.8 ms | 30 | 26.4 ms | 168 |
| Pan, zoom 1 | 752 | 47.1 ms | 21 | 35.5 ms | 762 |
| Pan, zoom 0.5 | 2,857 | 90.7 ms | 11 | 62.8 ms | 2,867 |
| Pan, zoom 0.25 | 9,342 | 85.5 ms | 12 | 81.3 ms | 8,264 |
| Idle, zoom 0.25 | 7,119 | 35.1 ms | 28 | 0 | 7,129 |

`UpdateAreaVisible` breakdown while panning at zoom 1 (per frame): hidden-node
loop 18.5 ms, per-item mapping rebuild ≈13.4 ms (752 rebuilds), upright sort
3.0 ms, texture upload 0.5 ms; plus `AreaDrawItem._Draw` 1.0 ms and HUD 2.1 ms.

**Cause F1 — 32,340 permanent `AreaDrawItem` nodes (≈19 ms per camera change).**
`PublishAreaReady` calls `SetAreaPlan`, which Fits the envelope, clamped at the
minimum zoom 0.125, then publishes. The first `UpdateAreaVisible` sees 32,339 of
37,363 area commands (plus the actor), takes 1,233 ms on the main thread and
grows `_areaItems` to 32,340 nodes; the view then resets to the actor (474 visible), but the pool never
shrinks (`ObjectNodeCount` stays ≈32,400). Every later recompute walks all
hidden items and calls `Hide`, clears the texture and `ReleaseMapping`
(a `SetShaderParameter` engine call) on each: ≈0.6 µs × 31,600 items. At zoom 2
this loop is 73% of the per-frame update. The same Fit-sized submission recurs
whenever Fit is chosen.

**Cause F2 — every visible sprite rebuilds its sampling texture on any camera
move (≈15–26 µs per sprite, including show/redraw).** `AreaDrawItem.PrepareMapping` caches on a state
that includes the full canvas transform, so any pan or zoom invalidates every
visible item: it allocates a `byte[]`, creates a Godot `Image` and creates or
updates an `ImageTexture`, and sets three shader parameters. Rebuilds per frame
equal the visible count (752 at zoom 1, 2,858 at zoom 0.5). Together with the
per-recompute LINQ arrays this allocates ≈100 MB/s while panning (gen0/gen1/gen2
collections 105/53/2 in 8 s at zoom 1); maximum frame times reach 80–146 ms.

**Cause F3 — one draw call per sprite (dominates zoomed-out views).** Each
`AreaDrawItem` has its own `ShaderMaterial` and per-item uniforms/mapping
texture, so Godot cannot batch: draw calls equal visible sprites + ≈10. With no
area update at all, zoom 0.25 idle costs 35 ms per frame for 7,129 draw calls
(≈4.6 µs each on the compatibility renderer after the 2 ms HUD); at the initial
Fit view (≈32,000 sprites) this extrapolates to ≈150 ms per frame (not measured).

**Cause F4 — HUD status rebuilt every frame (≈2 ms).** In WORLD/area mode
`ProcessWorld` → `RefreshReadyStatus` → `RefreshAreaStatus` formats the long
status line (including a LINQ count over the coverage rows) and assigns the
label text on every frame, idle or not. `ProcessWorld` takes 2.2 ms of the
5.7 ms idle frame; the status refresh alone measured ≈2.1 ms per frame.
`PublishWorldSelection` → `PublishArea` also runs about twice per frame; it is
cheap (cached state) but repeats the visibility state check.

**Cause F5 — full upright re-sort per recompute (≈3 ms).** `UprightOrder`
re-sorts all phase-4 bodies plus the actor marker with a seven-key `OrderBy`
on every camera change, independent of what is visible.

**Cause F6 — visible-only GPU residency causes upload churn (minor).**
`UploadAreaVisible` disposes every texture that left the view, so panning back
re-uploads: 229 textures created in 8 s at zoom 1, 934 at zoom 0.25
(0.5–2.5 ms per frame). The CPU repository makes this cheap but not free.

The idle loop is uncapped in these runs (≈176 FPS, ≈85% CPU of one process
with no input), so the viewer keeps a core busy while idle.

## L1 result: frame batch

The owner's `BeginFrameBatch` now authenticates each used source once before
its first frame and once at `Complete`, instead of twice per frame (contract in the
[loader](../world/crash-site-area-loader.md)). Same machine and method, at
`origin/main` `8def8c2` plus the change:

| Measurement | Before | After |
| --- | ---: | ---: |
| Debug source launch to SOURCE READY | 318 s | 49.9 s |
| `CopyFrames`, Debug source run | 290.2 s | 22.2 s |
| `CopyFrames`, probe Debug / Release | 260 / 240 s | 19.7 / 5.2 s |
| Owner `ReadActual` growth during the copy | 596.1 GB | 1.35 GB |
| Native `crash-site-area` smoke (two loads) | not timed (two ≈5 min loads) | 109 s |

The remaining copy time is per-frame entry read and payload hash, ART decode and
pixel hash, about four times slower in Debug; the linear descriptor lookup costs
0.2 s for all 2,444 keys. It shares its code with I3, so it belongs to cause L2.
The load now also includes the whole-core step stage (0.2 s) merged after the
diagnosis.

## F1/F4/F5 result: pool, startup, HUD, order

- The first area publication now happens after the camera is placed on the actor,
  not in the Fit view at minimum zoom: 191 visible items in 24 ms instead of 32,339
  in 1,170 ms. Godot nodes after load: about 250 instead of 32,400.
- `UpdateAreaVisible` hides only the items shown by the previous submission.
  Every frame, still or moving, frees at most 1,024 hidden items until at most
  max(256, 2 × shown) items remain. An explicit Fit still submits the whole
  envelope (about 32,000 items, about 1.2 s when last measured); only startup no
  longer does.
- The status line no longer counts N/A over all 37,363 coverage rows every frame
  (status build 1.9 ms → 1.0–1.3 ms). It is still formatted and assigned every
  frame; updating it only on change remains open.
- `AreaUprightOrder` sorts bodies once per plan and inserts the actor marker by
  binary search; a test checks it is identical to `UprightOrder`.

Same method, isolated preferences (zoom 2 default), two runs at `origin/main`
`2758c4f` plus the change:

| Phase | Before | After |
| --- | ---: | ---: |
| Idle, WORLD follow, zoom 2 | 3.9 ms | 2.2–3.4 ms |
| Pan, zoom 2 | 35.2 ms | 9.0–10.1 ms |
| Pan, zoom 1 | 46.4 ms | 21.7–27.1 ms |
| Pan, zoom 0.5 | 88.3 ms | 65.5–70.7 ms |
| Pan, zoom 0.25 | 80.1 ms | 66.8–67.0 ms |
| Idle, zoom 0.25, where the pan stopped | 33.0 ms | 37.6–40.4 ms |

Zooming out now creates draw items on demand, so the first frame after a large
zoom-out (to 0.25) costs up to about 140 ms; zooming back in frees the surplus over
the following frames (worst observed frame 72 ms instead of 139 ms). Idle zoom 0.25 is bound by
draw calls (cause F3, ≈4.7 µs each) and did not improve; the pan ended on a busier
view (≈8,070 draw calls after, 7,125 before). Per-sprite mapping rebuilds (F2) are now
the largest panning cost.

## Ranked suspected causes and fix directions

Directions are suggestions for separately scoped work; none is implemented or
accepted here, and each must keep the existing identity, budget and fail-closed
contracts that the code currently enforces.

1. **L1, most of 290 s of 318 s. Done by the frame batch above.** This was current contract, not an accident:
   the [loader](../world/crash-site-area-loader.md) states that frame
   acquisition reauthenticates the source before and after decoding. Changing it
   needs a deliberate contract revision that answers what whole-archive rehashing
   guarantees beyond the existing per-entry payload hash check in
   `DecodeDescriptor` and, for path-bound inputs only, `AreaPathStream`'s
   path/inode/size/time check (stream-only inputs bind bytes, not a path). For
   example, authenticate once per owner generation and rely on those checks per
   frame. Estimated load without the per-frame passes: ≈25–30 s Debug,
   ≈7–10 s Release, assuming the untimed per-frame remainder stays small.
2. **F1 + F2, most of the 26–81 ms `UpdateAreaVisible` per camera-moving frame. F1 done for startup and pool size (an explicit Fit still submits the whole envelope); F2 remains.** Create items for visible
   commands only (no Fit-sized pool, or shrink it) and touch only items whose
   state changed; make the nearest-sampling mapping independent of pan (for
   example derive it per zoom level, or compute indices in the shader from
   uniforms) so panning does not rebuild per-sprite textures.
3. **F3, 35 ms+ at zoom ≤ 0.25.** Reduce draw calls: shared material and
   batched or atlased submission, or pre-composited terrain chunks for the
   immutable terrain layer (36,864 of 37,363 commands are terrain).
4. **L2, ≈18 s of load in I3 plus ≈15 s of the remaining frame copy.** Run
   original-data loads from optimized assemblies (Release core libraries, or an
   optimized export), or remove per-byte interface loops in decode/pixel hashing.
5. **F4 (≈2 ms every frame) and F5 (≈3 ms per camera change). F5 done; F4 partly (N/A count cached, the line is still rebuilt every frame).** Update the HUD
   only on change; cache the upright order and patch only the actor marker.
6. **F6 and minor load items.** Keep a bounded LRU of GPU textures instead of
   visible-only; avoid decoding frames twice; cap the idle frame rate.

## Not established

Original-runtime performance, other machines, the exported `.app` (expected to
match the Debug source run, not measured), Release frame times, GPU-side timing
beyond draw-call counts, and memory bounds. The temporary probes are not part of
the repository; rerunning this diagnosis requires re-creating equivalent local
instrumentation.
