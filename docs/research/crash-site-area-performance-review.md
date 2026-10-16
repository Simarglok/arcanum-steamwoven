# Crash Site area viewer performance review (2026-10-16)

A measured pass over the `crash-site-area` viewer after the
[performance diagnosis](crash-site-area-viewer-performance.md) fixes (PRs #73–#90),
with the features merged since (day/night ambient lighting, the cave transition,
the passability overlay, NPC conversation). For each category it records what was
measured and rates the remaining optimizations by expected gain, size and risk.
It changes no code; every optimization below is a candidate, not a decision.

## Scope and method

- Development Mac (Apple M4 Pro, 14 cores, 180 Hz display, `gl_compatibility`),
  `origin/main` `cf691aa`, original data, isolated viewer preferences.
- **Load:** timestamped `load stage=` lines of an ordinary source launch, and the
  same `AreaLoadOperation.Execute` timed twice per configuration (Debug and
  Release) in a temporary test host. Source runs and the `macOS debug` export
  preset both run Debug assemblies. CPU attribution by macOS `sample` of the
  Release host (native frames only; managed frames are not symbolized). Another
  session's test host ran at the same time during part of the profiling, so the
  profiled run's stage times are only indicative; the timing table uses the
  clean runs.
- **Frames:** a temporary probe (not committed) drives the camera through twelve
  scripted phases of 4–6 s, uncapped (frame pacing off), and reports the frame
  time distribution, the canvas update time, Godot draw calls, texture and chunk
  counters and memory monitors. Two full runs; ranges below span both. Pan speed
  is 4 screen pixels per frame.
- **Developer checks:** wall time of each command on a warm build.
- The temporary probes are not part of the repository; rerunning requires
  equivalent local instrumentation.

## Summary by category

| Category | Measured now | Main remaining cost | Best candidate | Expected gain | Size / risk |
| --- | --- | --- | --- | --- | --- |
| Load | 22.1–22.8 s Debug, 11.6–13.4 s Release, single thread | strict build and frame copy (inflate, decode, SHA-256), about ten whole-archive authentication passes | Release assemblies for daily runs; reuse frames decoded in the strict build; share authentication | −45% (Release); −2–4 s each (reuse, sharing) | owner decision; medium / contract |
| Frame, overlay on | pan 10–33 ms, up to 2,508 draw calls | the overlay is redrawn every moving frame as thousands of separate polylines | redraw only when visible cells change; draw as one mesh | pan back to the 6–7 ms of the plain view | small–medium / low |
| Frame, plain view | pan 5–10 ms; worst frames 17–137 ms, highest on first visit of a zoom | canvas update on every moving frame (2–5 ms: scan of 37,363 commands, uploads, item updates); chunk composition on the main thread | spatial index for visibility; budgeted or background chunk composition | up to −2–4 ms per pan frame; spikes under ~20 ms | medium / low (exactness kept) |
| Idle and pacing | 58–61 FPS, 5–9% CPU | full redraw of an unchanged view | Godot low-processor mode when nothing changes | a few % CPU | small / low |
| Memory | RSS 277–620 MiB; GPU textures up to 266 MiB | 192 MiB chunk cache cap; 2.3 GB allocated per load | per-level or lower chunk cap; fewer load allocations | −50–100 MiB GPU; lower post-load peak | small / trade-off with spikes |
| Developer loop | root tests 12.9 s; smoke 59 s | one differential inflate test takes 10 s | smaller corpus or own category | −9 s per root test run | small / low |

## Load

| Stage | Debug (2 runs) | Release (2 runs) |
| --- | ---: | ---: |
| Open and first authentication | 0.6–1.0 s | 0.6–1.2 s |
| Strict snapshot (two more passes, sectors, decode) | 9.5–10.1 s | 4.3–5.7 s |
| Scene plan | 0.1 s | 0.04–0.07 s |
| Frame copy (1,975 frames, 25.9 MB RGBA) | 8.5–8.8 s | 3.6–3.7 s |
| Area steps, actor, lighting | 0.3–0.4 s | 0.3 s |
| `VerifyFile` ×3, cave owner, cave strict snapshot | 2.1 s | 1.9–2.1 s |
| Cave plan, frames (54), steps | 0.6–0.7 s | 0.6 s |
| **Total** | **22.1–22.8 s** | **11.6–13.4 s** |

The ordinary source launch logged `source-ready` 22.4 s after `load stage=begin`.
CPU time equals wall time: the load runs on one core of fourteen. Each load
allocates 2.3 GB (2,223 MiB) of managed memory and adds 14–16 gen-2 collections
(measured on the second run in a process); shortly after a load the managed heap
held 607–610 MiB, which fell to 119–159 MiB once collected.

The native profile of the Release frame copy has about half of the load thread's
samples in `AccelerateCrypto_SHA256_compress`; in the strict snapshot it is about
15%. The rest is managed code that the sampler cannot name; the earlier
diagnosis attributed it to inflate, the inflate projection and ART decode.

Where the time goes, from the code and a measured hash rate of about 2.5 GB/s
over the 961 MB of DAT inputs (inferred, not timed per call):

- **Repeated authentication.** The inputs are hashed in full about ten times per
  load: owner open, strict I0 and I7, the frame batch (twice per used resource),
  `VerifyFile` ×3, and again by the cave owner's open, I0 and I7 and its frame
  batch. The 2 s step between the lighting line and the cave snapshot is almost
  only these passes over files already authenticated in the same load.
- **Double decode.** Each selected frame entry is read, projected, inflated and
  decoded once in the strict build (to build its descriptor) and again in the
  frame copy.
- **Per-frame hashing.** Payload and RGBA SHA-256 for every frame in the copy.

| Option | Expected gain | Size | Notes |
| --- | --- | --- | --- |
| L-A Release assemblies for daily runs (a release export preset, or optimized Data/Core assemblies in Debug) | 22 → 12–13 s | small code, but export gates and presets | Owner approved on 2026-10-16; done, see the L-A result. |
| L-B Keep the RGBA decoded in the strict build and hand it to the frame copy | about half the copy: 4 s Debug, 1.5–2 s Release | medium | Changes the loader contract (strict build retains pixels for selected frames, about 26 MB); the copy keeps its batch authentication. |
| L-C Share authentication within one load (cave owner and `VerifyFile` reuse the main owner's verified identities while the files are held open) | 1.5–2 s in both configurations | medium | Needs an explicit argument for why a held-open, already hashed file cannot change, or a cheaper size/mtime guard; fail-closed contract change. |
| L-D Decode frames in parallel (14 cores) | copy 8.5 → about 3 s Debug, 3.6 → about 2 s Release, unless batch authentication is also split (estimate) | medium–large | The owner's budget and admission counters are sequential and deterministic today; parallel work must keep exact, reproducible accounting. |
| L-E Fewer allocations in the read path (pooled entry and inflate buffers) | lower GC time and post-load peak; time gain not measured | medium | Profile allocations first. |
| L-F Persistent local cache of derived frames keyed by source SHA-256 | repeat loads to a few seconds | large | Derived original content on disk (ignored `local/` only), invalidation and provenance rules; owner decision. |

## Frame time

| Phase (uncapped) | Avg | p95 | Max | Canvas update | Draw calls |
| --- | ---: | ---: | ---: | ---: | ---: |
| Idle, WORLD default (zoom 2) | 2.7–3.1 ms | 3.3–3.7 ms | 34–40 ms | 0 | 28–34 |
| Pan, zoom 2 | 5.1–7.6 ms | 7.4–8.3 ms | 17–23 ms | 2.2–4.6 ms | 33–35 |
| Pan, zoom 1 | 6.4–6.8 ms | 8.3–9.2 ms | 82–93 ms | 3.1–3.8 ms | 24–47 |
| Pan, zoom 0.5 | 5.6–5.9 ms | 7.6–9.7 ms | 40–66 ms | 2.4–2.6 ms | 67–71 |
| Pan, zoom 0.3 (full-resolution chunks) | 6.6–7.7 ms | 12.5–13.3 ms | 135–137 ms | 2.9–3.5 ms | 202–203 |
| Pan, zoom 0.25 | 9.8–10.2 ms | 13.3–13.6 ms | 29–44 ms | 4.7–4.9 ms | 339–354 |
| Pan, zoom 0.125 | 7.5–7.7 ms | 15.0–15.3 ms | 57–61 ms | 1.9–2.0 ms | 708–720 |
| Idle, zoom 0.125 | 4.5–4.8 ms | 4.9–5.6 ms | 10–14 ms | 0 | 725–745 |
| Idle, zoom 1, game hour +1 four times a second | 2.2–2.4 ms | 2.8 ms | 8–10 ms | 0.02 ms | 95–105 |
| Pan, zoom 1, overlay on | 10.0–13.3 ms | 12.1–15.3 ms | 19–37 ms | 2.2–3.5 ms | 458–573 |
| Pan, zoom 0.5, overlay on | 29.6–33.2 ms | 34–48 ms | 51–67 ms | 2.9–5.0 ms | 1,633–2,508 |
| Idle, zoom 1, overlay on | 3.6–5.6 ms | 4.2–6.1 ms | 32 ms | 0 | 339–1,122 |

These numbers come from a different probe than the earlier F3 table and are not
directly comparable with it. Rebuilding the WORLD status line costs 0.9–1.4 ms
each time; with the 10 Hz limit that is about one such frame per 100 ms. Changing
the game hour (relighting every item) is cheap.

Findings:

- **Passability overlay.** With the overlay on, a pan frame at zoom 0.5 takes
  30–33 ms (about 30 FPS) against 6 ms without it. `PassabilityOverlayLayer.Tick`
  queues a redraw whenever the view rectangle changes, that is on every moving
  frame, and `_Draw` emits each visible cell as its own polygon, polylines,
  hatch lines and arrows (2,739 cells and up to 2,508 draw calls). Even idle it
  costs up to 1,122 draw calls.
- **Canvas update.** `TerrainCanvas.UpdateAreaVisible` runs on every frame in
  which the camera moved: it filters all 37,363 commands (plus the upright
  order), composes missing chunks, uploads frames and updates the draw items.
  Even zoom 2 with about 200 visible commands spends 2–4.6 ms per pan frame in
  it. How much of that is the scan alone was not timed; it is inferred to be the
  part that does not shrink with the view.
- **Worst frames.** The maximum frame per pan phase was 17–23 ms at zoom 2,
  82–93 ms at zoom 1, 40–66 ms at zoom 0.5, 135–137 ms at the non-dyadic zoom 0.3
  (full-resolution chunks), 29–44 ms at zoom 0.25 and 57–61 ms at zoom 0.125.
  Chunks are composed on the main thread when first needed (5–259 per phase);
  attributing these spikes to composition is inferred from those counts.
- **Zoom 0.25 pan** is the slowest steady state without the overlay (10 ms,
  about 100 FPS, under the 180 Hz display): about half canvas update, the rest
  about 270 body items drawn one by one plus chunks.

| Option | Expected gain | Size | Notes |
| --- | --- | --- | --- |
| F-A Overlay: recompute and redraw only when the set of visible cells changes (with a margin around the view), and build the overlay as one mesh (or a few) instead of per-cell draw commands | pan with overlay close to the plain 6–7 ms; idle draw calls back to the plain view | small–medium | Display only; the existing overlay self-tests keep the cell classification. |
| F-B Spatial index (a coarse world grid of command indices) for the visibility scan | up to −2–4 ms per pan frame (upper bound: the canvas update also holds composition and uploads) | medium | Must keep the exact draw order: index only selects candidates, the order stays the plan order. |
| F-C Budgeted chunk composition: compose at most a few chunks per frame and draw the missing ones per command (already exact), or compose on a worker thread | worst frames from up to 137 ms to under about 20 ms (estimate) | medium | Per-command fallback is the existing exact path; a worker needs immutable inputs, which the plan and frames already are. |
| F-D Batch body items (one material, per-instance data) | zoom 0.25 pan toward 5–6 ms | large | Option C of the [draw-call research](crash-site-area-draw-calls.md); the nearest-sampling proof must be redone. |
| F-E Event-driven status line instead of 10 Hz rebuilds | about 1 ms every 100 ms | small | Low value now. |

## Idle and pacing

With frame pacing (PR #90) the idle view runs at 58–61 FPS once the cap has settled
(55–65 FPS in the first seconds) and 5–9% CPU (`ps`, this run), and an
active view at the display rate. The remaining idle cost is a full redraw of an
unchanged scene 60 times per second.

| Option | Expected gain | Size | Notes |
| --- | --- | --- | --- |
| I-A Godot low-processor mode (redraw only on change) while nothing animates | idle CPU toward 1% | small | Must stay off while the actor animation, a route or a conversation runs. |

## Memory

| Measurement | Value |
| --- | --- |
| Process RSS, idle after load | 362–533 MiB (three launches), 277–620 MiB during the scripted phases |
| Managed heap right after load / after collection | 607–610 MiB / 119–159 MiB |
| Managed allocation per load | 2.3 GB |
| CPU frame repository (area + cave) | 25.9 + 1.5 MB |
| GPU frame textures (visible + LRU) | 0.5–1.9 MiB in the scripted phases; at most 13 MB in the smoke sweep |
| Terrain chunk textures | up to 662–666 chunks, 188–190 MiB (cap 192 MiB) |
| Godot texture / video memory | 17–266 MiB / 29–282 MiB |

Terrain chunks dominate GPU memory once a few zoom levels have been visited.

| Option | Expected gain | Size | Notes |
| --- | --- | --- | --- |
| M-A Lower the chunk cap (for example 96 MiB) or budget it per level | −50–100 MiB GPU | small | More recomposition after zoom changes, so pair with F-C. |
| M-B Fewer load allocations (L-E) and a compacting collection after publication | lower RSS peak after load | small–medium | Measure RSS before and after. |

## Developer loop

Single wall-clock runs of each command on a warm build:

| Check | Wall time |
| --- | ---: |
| Root restore and build (warm) | 1.9 s |
| Root tests | 12.9 s |
| `dat_probe.py --self-test` | 0.1 s |
| Terrain viewer tests (151) | 8.0 s |
| Godot self-test, headless / GUI | 2.1 s / 8.3 s |
| Original-data native smoke (two loads, two sweeps, lighting, reload) | 59.1 s |

One test, `Fast_projection_matches_the_bit_serial_reference_on_valid_truncated_and_corrupted_streams`,
takes 10.0 of the Data project's 12.6 s.

| Option | Expected gain | Size | Notes |
| --- | --- | --- | --- |
| D-A Shrink the differential corpus of that test (or keep the full corpus in an opt-in category) | −9 s per root test run | small | Keep the edge-code and truncation cases. |

## F-A result: overlay redraw and batching

Implemented as described in the [overlay contract](../rendering/passability-overlay.md).
Same probe and machine, two runs each (before = the table above):

| Phase (uncapped) | Before | After | Draw calls before / after | Redraws per frame after |
| --- | ---: | ---: | ---: | ---: |
| Pan, zoom 1, overlay on | 10.0–13.3 ms | 5.1–7.8 ms | 458–573 / 43–119 | 0.05–0.11 |
| Pan, zoom 0.5, overlay on | 29.6–33.2 ms | 8.4–8.7 ms | 1,633–2,508 / 100–167 | about 0.2 |
| Idle, zoom 1, overlay on | 3.6–5.6 ms | 2.2–3.5 ms | 339–1,122 / 43–104 | 0 |

The remaining redraws during a pan come from newly assessed cells entering the view.

## L-A result: release export

The terrain endpoint now exports with the release template, and `Directory.Build.props`
optimizes every project built in Godot's ExportRelease configuration: a first release
export without that setting still shipped `Arcanum.Data` unoptimized and loaded in the
same 23.6–23.9 s as Debug. Same machine, original-data native smoke of a scratch export
at `42a76c8` plus the change, two runs:

| Measurement | Debug source run | Release export |
| --- | ---: | ---: |
| Load (`ElapsedMilliseconds`, first and reload generation) | 23.0–24.1 s | 12.2–13.1 s |
| Whole native smoke (two loads, sweeps, lighting, cave, reload) | 57–66 s | 36.8 s |

Exported self-tests (headless and GUI), `hostfxr_validate.py` and `codesign --verify`
pass on the release export. The ordinary `./arcanum area` launch gets this once a
release export is registered by the endpoint. `Arcanum.Input`'s locked restore against
its committed `packages.ExportDebug.<RID>` locks (the gamepad export graph) applies only
to ExportDebug, so in the release export it restores from its ordinary lock file like
the other terrain projects.

## F-B and F-C result: visibility index and parallel chunk composition

F-B is the coarse grid index described in the
[viewer contract](../rendering/crash-site-area-viewer.md). For F-C the per-command
fallback was dropped: a terrain command that spans a ready and a missing chunk would be
drawn over the ready chunk after it and break painter order there. Missing chunks are
instead composed in parallel on the CPU cores before upload. Same probe, two runs each,
on `0a7d1ab` plus the change (before = the frame table above, overlay phases after F-A):

| Phase (uncapped) | Avg before | Avg after | Max before | Max after | Canvas update after |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pan, zoom 2 | 5.1–7.6 ms | 2.3–2.5 ms | 17–23 ms | 17–25 ms | 0.2 ms |
| Pan, zoom 1 | 6.4–6.8 ms | 2.6 ms | 82–93 ms | 19–24 ms | 0.3 ms |
| Pan, zoom 0.5 | 5.6–5.9 ms | 3.5–4.7 ms | 40–66 ms | 27–39 ms | 0.8–1.1 ms |
| Pan, zoom 0.3 | 6.6–7.7 ms | 5.9–7.8 ms | 135–137 ms | 81–92 ms | 2.0–3.0 ms |
| Pan, zoom 0.25 | 9.8–10.2 ms | 6.1–6.9 ms | 29–44 ms | 32–38 ms | 2.2–2.4 ms |
| Pan, zoom 0.125 | 7.5–7.7 ms | 7.3–9.1 ms | 57–61 ms | 50–51 ms | 1.6–2.9 ms |
| Pan, zoom 1, overlay on | 5.1–7.8 ms | 3.7–4.3 ms | 19–50 ms | 28–36 ms | 0.4–0.5 ms |
| Pan, zoom 0.5, overlay on | 8.4–8.7 ms | 5.2–6.1 ms | 30–35 ms | 38–43 ms | 0.5–0.8 ms |

Steady pan frames at zoom 1–2 fall to about a third. The first visit of the
full-resolution zoom 0.3 still costs up to 81–92 ms: about 110–140 new 512 px chunks
are uploaded on the main thread after composition (not timed separately). When the
view spans more grid cells than half the occupied cells (the lowest zooms), the index
scans every command as before; at low zoom most of the remaining canvas update is the
per-item work, which grows with the visible count.

## L-B result: one read per ART entry

Instead of keeping decoded pixels from the strict build (the original L-B), the owner
now reads, inflates and payload-hashes each ART entry once per authenticated bracket
(strict build or frame batch) and shares it between the frames selected from it; see
the [loader contract](../world/crash-site-area-loader.md). The 1,975 selected frames
come from only 335 entries (218 MB of decoded entry bytes per load before, 4.7 MB
after), and the strict build's 2,444 visuals from 345. Same test-host method, two runs
each, at `d99cc8d` plus the change (before = the load table above):

| Stage | Debug before | Debug after | Release before | Release after |
| --- | ---: | ---: | ---: | ---: |
| Strict snapshot | 9.5–10.1 s | 1.9–2.0 s | 4.3–5.7 s | 1.2–1.9 s |
| Frame copy | 8.5–8.8 s | 1.1 s | 3.6–3.7 s | 0.7–0.8 s |
| **Whole load** | **22.1–22.8 s** | **7.0–8.4 s** | **11.6–13.4 s** | **5.3–6.6 s** |
| Managed allocation per load | 2.3 GB | 1.4 GB | 2.3 GB | 1.4 GB |

The largest remaining step is the 1.8–2.8 s between the lighting stage and the cave
snapshot: `VerifyFile` ×3 and the cave owner's own authentication passes over inputs
already authenticated in the same load (L-C, not done).

## D-A, I-A and M-A results

- **D-A done.** The differential inflate test now uses mostly small streams (two of 100–200 KB per
  compression level, the rest under 24 KB) with the same 2,160 comparisons and the same
  pass thresholds; it takes 1.7 s instead of 10.0 s.
- **I-A dropped.** With Godot's low-processor mode switched on while idle, the idle view
  still drew about 60 frames per second at the same CPU: something visible changes every
  frame, so there is nothing to skip until that source is found. Idle CPU measured
  11–24% in both runs on `f60dd31`, above the 5–9% measured on `cf691aa`; the cause was
  not investigated (other sessions were loading the machine at the same time).
- **M-A dropped.** The first visit of zoom 0.3 needs up to 141 full-resolution chunks
  (141 MiB) at once. A 128 MiB cap would make such views fall back to per-command
  terrain, so the 192 MiB cap stays.

## Recommended order

1. F-A overlay redraw and batching: the largest visible problem, small and display-only (done).
2. L-A Release assemblies for daily runs: halves the load (done, release export).
3. F-C chunk composition (done as parallel composition), then F-B spatial index (done):
   most of the per-pan CPU cost is gone; zoom 0.3 first-visit spikes remain.
4. L-B (done as one read per ART entry: Debug load 22 → 7–8 s) and L-C (share
   authentication, about 1.5–2 s more; not done).
5. D-A (done), I-A and M-A (dropped, see above).
6. Later, only if needed: L-D parallel decode, F-D body batching, L-F persistent
   cache.

## Not established

Frame times of the exported `.app` (its load is measured in the L-A result), real input (the probe moves the camera by script), GPU-side
timing beyond draw-call counts, other machines, and the cost of click routing and
NPC conversation (not probed; no frame spike was attributed to them). Quick save
and character stats, merged after the measured commit, were not measured. Gains in the
option tables are estimates from the measurements above, not measured results.
