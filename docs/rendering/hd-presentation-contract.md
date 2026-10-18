# HD presentation contract (ARC-RENDER-013)

Status: **contract and synthetic oracle tests, amended by ARC-DATA-014** (see
[the amendment](#amendment-arc-data-014-2026-10-16)) **and ARC-RENDER-016** (terrain chunks
and walls, [amendment](#amendment-arc-render-016-2026-10-17)). The pack format, reader, import and
device budget exist ([HD pack format](../formats/hd-pack.md)); the AREA/WORLD path for
characters and objects exists since ARC-RENDER-014
([viewer](terrain-viewer.md#hd-render-path-arc-render-014)); the setting, its persistence, the
toggle key and the timed commit exist since ARC-UI-006
([viewer](terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006)). The parts are ARC-DATA-014 (pack format and
loader), ARC-RENDER-014 (AREA/WORLD HD path for characters and objects), ARC-UI-006
(on-the-fly toggle), ARC-DEV-003 (pack build tool), ARC-RENDER-015 (before/after
review) and ARC-RENDER-016 (terrain). Each of them implements its part of this
contract and must not change it silently; a change to a rule below is a contract
amendment in this file.

Requirement: [FR-PRODUCT-004](../requirements/content-product.md#fr-product-004)
(Legacy and a future upscale share one world). Decision: [visual modes and early
graphics](../architecture/decisions.md). Exact sampling it extends:
[area viewport nearest sampling](area-nearest-sampling.md) (ARC-RENDER-007).

## Terms

- **Legacy frame**: one decoded original ART frame, `w × h` RGBA, with its frame
  hotspot, mirroring and palette, as the existing Data/AREA/WORLD paths load it.
- **HD frame**: an RGBA image of exactly `N·w × N·h` made outside the game from one
  Legacy frame. **N** is a positive integer scale declared by the pack. Accepted:
  `N = 4` (the default recipe) and `N = 2` (owner decisions 2026-10-16, recorded in
  [decisions](../architecture/decisions.md): first 2×, then "1 + 3" with 4×); the rules
  are written for any integer `N ≥ 2`.
- **Frame key**: the identity of a Legacy frame (resource entry, effective palette
  slot, physical group, frame, plus the hashes the existing `AreaFrameKey` carries).
  An HD frame is bound to exactly one frame key. A pack stores only the frame key's
  install-independent part (ART payload hash, view length, physical group, frame,
  effective palette) and checks the Legacy size and RGBA hash on each device
  ([amendment](#amendment-arc-data-014-2026-10-16)).
- **Visual profile**: `Legacy` or `Hd`, a presentation setting. It selects which
  texture draws a frame key; nothing else.
- **HD pack**: a local directory outside the repository with a manifest and HD frames,
  in a pack collection (the game's pack folder, or `local/hd-packs/<id>/` in a checkout,
  ignored by git) or in a folder the user chose. It is built by the owner on a device
  from a recipe, or built on one of their devices and imported on another, and is not
  distributed ([decisions](../architecture/decisions.md), HD pack). Pack
  frames, screenshots and recordings never enter git, PRs or attachments; tests use
  synthetic sprites and synthetic "HD" only.

## What HD may and may not change

| HD changes | HD never changes |
| --- | --- |
| The texture bound to a frame key | World destination rectangle, hotspot, anchor, `+40/+20` object offset, scale/shrink |
| The source crop in that texture (`× N`) | Mirroring decision, draw order, phase, same-cell ranks |
| GPU/CPU memory used by textures | Frame count, group, direction, FPS, action frame, animation timing |
| HUD counters of HD/fallback frames | Wall fade set and fade alphas, ambient/local light, tint |
| | Cell picking, passability, routes, wall check, Session, clock, snapshots, quick save |

## Exact sampling with a scaled source crop

The AREA path draws every item with the exact rule of
[area-nearest-sampling.md](area-nearest-sampling.md): for screen pixel `s`, viewport
scale `z`, translation `o`, destination `[d, d + n)` and source crop `[c, c + m)`,

    u = ((s + 1/2 - o) / z - d) * m / n

owns the pixel iff `0 <= u < m` and selects `c + floor(u)`, or `c + m - 1 - floor(u)`
when reflected.

**HD rule.** An HD draw of a Legacy draw `(o, z, d, n, c, m, reflected)` uses the
**same** `o, z, d, n` and reflection and the crop `(N·c, N·m)` in the HD frame, on each
axis independently. Nothing else in the rule changes: no epsilon, no interpolation,
no filtering, no mipmaps, no shader smoothing, no change of destination. The
production sampler (`AreaNearestAxis` and the index rows) needs no new mode: for
sampling, HD is only a different crop and texture. The only shader change HD needs is
the wall-fade column below.

**Consequences** (exact, for every screen pixel, every transform the Legacy rule
accepts and every integer `N ≥ 1`). Let `L` be the Legacy source index and `H` the HD
source index on one axis.

1. **Same coverage.** `H` is excluded iff `L` is excluded. The HD `u` is exactly `N`
   times the Legacy `u`, so `0 <= N·u < N·m` iff `0 <= u < m`.
2. **Same texel.** `floor(H / N) = L`, reflected or not. Unreflected:
   `H = N·c + floor(N·u)`, so `floor(H / N) = c + floor(u)`. Reflected: with
   `floor(N·u) = N·q + r`, `0 <= r < N`, `H = N·(c + m - 1 - q) + (N - 1 - r)`, so
   `floor(H / N) = c + m - 1 - q`, and `q = floor(u)`. Every HD screen pixel shows one
   of the `N × N` subtexels of the Legacy texel the same pixel shows in Legacy.
3. **Which subtexel.** The subtexel offset is `r = floor(N·u) mod N`, or `N − 1 − r`
   when reflected. What reaches the screen depends on the effective magnification
   `z·n/m`, not on the viewport zoom alone. With integer origin and destination at
   magnification 1 (zoom 1, `n = m`), `u` sits at a texel center, so `N = 2` always
   shows subtexel 1 (subtexel 0 when reflected): a point sample of the HD frame with no
   extra detail. At magnification 2 (zoom 2, or zoom 1 on a body drawn at twice its
   frame size) every subtexel reaches the screen (1:1 HD). Detail appears only above
   magnification 1; a box-filtered downsample would be a different rule and is out of
   this contract.
4. **Nearest-replicated HD is invisible.** If every HD texel equals its parent Legacy
   texel (a nearest `N×` upscale), the HD raster equals the Legacy raster pixel for
   pixel. This is the identity oracle for fallback and toggle tests.

Literal oracle values for `N = 2` (independent of GPU output and of the production
sampler; asserted in `HdScaledCropTests`):

| Case | Legacy indices | HD crop | HD indices |
| --- | --- | --- | --- |
| `o=100, z=1/2, d=0, n=8, c=2, m=8`, screen 99..104 | –, 3, 5, 7, 9, – | `c=4, m=16` | –, 6, 10, 14, 18, – |
| same, reflected | –, 8, 6, 4, 2, – | `c=4, m=16` | –, 17, 13, 9, 5, – |
| `o=0, z=1, d=0, n=m=4, c=0`, screen −1..4 | –, 0, 1, 2, 3, – | `c=0, m=8` | –, 1, 3, 5, 7, – |
| same, reflected | –, 3, 2, 1, 0, – | `c=0, m=8` | –, 6, 4, 2, 0, – |
| `o=0, z=2, d=0, n=m=4, c=0`, screen 0..7 | 0, 0, 1, 1, 2, 2, 3, 3 | `c=0, m=8` | 0, 1, 2, 3, 4, 5, 6, 7 |
| `o=0, z=1, d=0, n=3, c=0, m=6` (destination smaller than frame), screen 0..2 | 1, 3, 5 | `c=0, m=12` | 2, 6, 10 |

**Limits.** The index rows encode `index + 1` in 16 bits, so `AreaNearestAxis` refuses
`c + m > 65534`. An HD crop with `N·(c + m) > 65534` cannot be drawn; that frame
falls back to Legacy (`HdAxisLimit`, below), it never fails the view. At `N = 2` every
Legacy axis up to 32,767 fits, at `N = 4` up to 16,383.

**Index-row cache.** A row key includes the crop, so Legacy and HD rows of the same
item have different keys and both stay exact; an HD key keeps the integer-translation
invariance that lets panning reuse rows. Rows hold indices only, so two items with
equal keys may share a row whatever texture they sample. The row cache limits
(16 MiB, 8,192 rows, 8,192-pixel rows) do not change: row length depends on the
screen envelope, not on the texture.

## Anchors, mirroring, wall fade, light and alpha

- **Anchors and placement** come from Legacy metadata only: the plan computes
  `destination = projected + object offset + (40, 20) − hotspot · scale`, the wall
  hotspot shift and its `width − x − 2` mirror, the HumanFixed anchor and the WORLD
  actor anchor from the Legacy `w`, `h` and hotspot. The scene plans
  (`AreaScenePlan`, WORLD plans) never receive HD dimensions; HD is substituted when a
  texture is bound to an already planned draw. An HD frame carries no hotspot of its
  own.
- **Mirroring** is the Legacy decision (`Mirrored` xor the body flip). In AREA it is
  applied as discrete reflection inside the HD crop (consequence 2); in WORLD as the
  existing `-1` X transform around the unchanged destination.
- **Wall fade** (ARC-RENDER-011) is a function of the Legacy column. Today the shader
  takes the column straight from the sampled index `sx` and sets `fade_width` to the
  crop width (`AreaDrawItem`), which is correct for Legacy walls because they are
  drawn with crop start `c = 0`. Binding an HD crop without a shader change would give
  a gradient over `N·width`, which differs by up to half a Legacy step and is **not
  allowed**. ARC-RENDER-014 therefore adds the scale `N` and the crop start to the
  fade uniforms: the column is `floor((sx − N·c) / N)` (Legacy: `N = 1`), which equals
  the Legacy column `sx − c` (consequence 2), and `fade_width` stays the Legacy crop
  width. Every screen pixel then gets exactly the alpha it gets in Legacy. The fade
  set itself comes from the wall check, which never sees textures.
- **Light** (ARC-RENDER-010/012) is computed in world coordinates (lattice, ambient,
  wall gradient across the world destination) and is profile-independent.
- **Alpha** (as amended by ARC-DATA-014). The default HD alpha is **soft**: the edge is
  upscaled with the colour instead of copied from the nearest Legacy mask, so the
  silhouette loses its steps. It must pass the [soft mask check](../formats/hd-pack.md#binding-a-frame-on-this-device):
  alpha only within one Legacy texel of the Legacy silhouette, and mask IoU at least the
  recipe's minimum (≥ 0.9). Consequences 1 to 3 still hold (they concern the destination
  and the selected texel); per-pixel alpha coverage may differ from Legacy, inside the
  unchanged destination and within one Legacy texel of the edge. The Legacy hard edge
  (every HD texel takes its parent's alpha) remains a recipe option; with it the per-pixel
  coverage of HD equals Legacy, and the consequence-4 identity oracle applies to it. No game rule reads
  sprite alpha; any future sprite-pixel hit test reads the Legacy frame.

## WORLD path

The WORLD diagnostic draws with `DrawTextureRectRegion(texture, destination, source)`
and nearest texture filtering, not the exact AREA rule. For HD it binds the HD texture
and passes `source` scaled by `N` (`(N·x, N·y, N·w, N·h)`) with the same destination
and the same mirror transform. The source rectangle must come from the Legacy frame
size, not from the destination size (today's actor and static draws derive `source`
from the destination, which equals the frame only at scale 1). WORLD pixel selection
stays whatever the GPU nearest sampler does, as for Legacy; exactness is an AREA
property.

## Frame binding and per-frame fallback

For each draw, the presentation layer asks a frame source for the frame key under the
active profile and gets one of: the Legacy texture, an HD texture with its `N`, or a
typed refusal, in which case **that frame** draws Legacy. A refusal never fails the
view, the map load or the session, and never changes another frame. Fallback is per
frame key, so one animation may mix HD and Legacy frames when a pack is incomplete;
the pack report (ARC-DEV-003) lists such groups.

| Reason | Meaning |
| --- | --- |
| `NoPack` | Profile `Hd` but no valid pack is loaded |
| `PackRejected` | The pack's review status is `rejected` |
| `FrameMissing` | The pack has no entry for this frame key |
| `SourceMismatch` | The entry was built from different Legacy bytes (mod, other version): the pack knows the ART name only from other ART bytes, or the Legacy size or RGBA hash differs |
| `FrameRejected` | The entry's review status is `rejected` (added by ARC-DATA-014) |
| `Invalid` | File missing, unreadable or a link, size or hash differs, not the pack PNG profile, not exactly `N·w × N·h`, too large, or the alpha-mask check failed (ARC-DATA-014 reasons) |
| `HdAxisLimit` | `N·(c + m) > 65534` on an axis |
| `HdBudget` | The frame does not fit the HD budget for this submission |
| `Pending` | Not yet decoded/uploaded (preload in progress) |

`Legacy` profile never consults the pack. An `unreviewed` pack is used and the HUD
says so. The HUD shows, per submission, the count of frames drawn HD and the count of
fallback frames by reason. Validation details and the manifest belong to ARC-DATA-014;
this contract only fixes that every outcome other than a valid HD frame is a
per-frame Legacy draw with a reason.

## Budgets

HD textures live in their own caches, keyed by `(pack identity, N, frame key)` (pack
identity = id and the SHA-256 of its manifest), with their own budgets. Loading, replacing or unloading a pack releases every HD texture of
the previous pack, as a map change does. Legacy budgets and behaviour are unchanged. HD at scale `N` costs `N²` bytes
per Legacy byte, so the HD limits are `N²` times the Legacy ones they shadow (×4 at
`N = 2`, ×16 at `N = 4`). Since ARC-DATA-014 these are **ceilings**: a per-device budget
may lower each of them (amendment below).

| Domain | Legacy limit today | HD limit |
| --- | --- | --- |
| AREA visible GPU set | `AreaRepositoryPolicy.MaxFrames` 2,444 frames, `MaxCpuBytes` 81,853,096 B (since ARC-DATA-029 the map caps `MaxMapFrames` 16,384 and `MaxMapCpuBytes` 160 MiB, [limits census](../world/limits-census.md)) | the Legacy frame count of the profile, `N² · 81,853,096` B (the exterior's figure, unchanged) |
| AREA retained (left the view) | 512 frames, 32 MiB | 512 frames, `N² · 32` MiB |
| WORLD actor + static textures | key caps 184 + 5 (`MaxActorDistinctKeys`, `MaxStaticDistinctKeys`), bytes shared with terrain inside `TerrainCachePolicy.MaxRgbaBytes` 256 MiB | the same key caps; a separate HD byte limit of `N²/4 · 256` MiB (256 MiB at 2×, 1 GiB at 4×), not shared with the Legacy 256 MiB |
| Terrain chunks | `AreaChunkTextureCache` 192 MiB, 1,024 chunks | since ARC-RENDER-016 one HD byte limit for visible and kept chunks of `N² · 192` MiB, the same 192 chunks (4 MiB each at 2×, 16 MiB at 4×: 768 MiB / 3 GiB), and at most 1,024 kept chunks; separate from the AREA frame limits |

- There is no whole-area HD CPU repository: HD pixels are decoded only for the
  visible set, the preload queue and the retained set, and released with them.
- Admission is deterministic: in draw order, a visible frame that would exceed the HD
  limit gets `HdBudget` and draws Legacy. Exceeding an HD budget never throws.
- During a switch both sets are resident, so the peak is the Legacy visible set plus
  the HD visible set. Legacy textures are not released by a switch to HD; they become
  retained under the Legacy retain limit, which makes switching back cheap.
- These are bounded limits, not measured performance claims. ARC-RENDER-014 measures
  real usage on the Crash Site pack before readiness.

## Toggle protocol

The switch changes textures only and is safe at any moment: while walking, during a
light or clock change, during dialog, before or after a map change.

1. **Request.** A setting or the diagnostic key requests profile `P` with a new
   generation number. A request equal to the active profile with nothing pending is a
   no-op. A request during a pending switch cancels it (the newer generation wins);
   its partial work is dropped or kept as retained cache within budget.
2. **Persist.** The requested profile is saved at request time to
   `user://visual_profile_v1.json` (`{"schema":1,"profile":"Legacy"|"Hd"}`), following
   `DiagnosticZoomSettingsStore`: a missing, unreadable or unknown value loads as
   `Legacy`; a test path override isolates tests. A save failure is reported in the
   HUD and does not block the switch.
3. **Preload.** Off the main thread, decode and validate the frames of `P` for the
   current visible set (the frame keys of the latest submission); keys that become
   visible while preparing (animation, walking, panning) are added. GPU uploads run on
   the main thread in bounded batches per frame. The active profile keeps drawing
   until commit.
4. **Commit.** When every visible key is ready or refused, or after a bounded wait
   (default 500 ms, a named constant tuned in ARC-UI-006), the active profile changes
   between two submissions, so one frame draws every visible item with the new
   profile. Keys still `Pending` at a timed-out commit draw Legacy and switch
   individually when ready. Items that become visible later load lazily.
5. **Nothing else moves.** Commit does not touch the scene plan, camera, zoom, wall
   fade set, light, animation phase, Session or clock; it re-binds textures and
   crops. Index rows rebuild because the crop changed; that is expected.
6. **Release.** A map change, reload, pack change or exit cancels pending work, and every HD
   texture and staging buffer is released with its cache, under the same single-owner
   discipline as the [actor visual contract](actor-visual-contract.md). A switch to
   `Legacy` releases HD textures down to the HD retain limit.
7. **Startup.** The saved profile is applied before the first submission. With
   `Hd` and no pack, the view runs with every frame `NoPack` and says so in the HUD.

The profile is not part of the session snapshot or quick save; a save made in either
profile loads in either profile.

**Implementation (ARC-UI-006).** The rules above are implemented as written; these points
settle what they leave open, without changing a rule:

- The diagnostic key is `H` without modifiers while the world owns input; the setting is the
  palette action "Visual Legacy/HD (H)". Both request the other profile than the pending one,
  else the active one, so a second press during a pending switch is the cancel of step 1.
- A request equal to the profile already pending is a no-op, like one equal to the active
  profile with nothing pending: no new generation, the wait is not restarted.
- The bounded wait stays 500 ms (`VisualProfileSwitch.DefaultCommitWaitMilliseconds`): the
  first bind of the whole Crash Site view from a 4× pack took about 0.1 s (ARC-RENDER-014).
- A switch to `Legacy` has nothing to prepare (the Legacy visible set stays resident), so it
  commits in the next submission. A request when nothing is published commits at once.
- A map change during a pending switch drops its decodes (step 6) and keeps the request, which
  commits on the new map's view. While a reload or failure has cleared the scene nothing is
  visible, so a pending request commits at once; the next view's frames load lazily.
- Step 2's store is `VisualProfileSettingsStore` with the test override
  `VISUAL_PROFILE_TEST_PATH`; an automated run (self-test, native smoke) without it neither reads
  nor writes the file and starts `Legacy`. The ARC-RENDER-014 environment selection
  (`ARC_HD_PACK`, `ARC_VISUAL_PROFILE`) still overrides the saved profile for that run and is
  not saved.
- One pack is active: `ARC_HD_PACK`, else the first pack that opened and is not rejected in the
  game's pack folder (`user://hd-packs/`) and, in a source run, the checkout's `local/hd-packs/`;
  a rejected pack only when it is the only one (every frame `PackRejected`).

## Gameplay never reads HD

HD types and the pack loader live in the presentation layer and `Arcanum.Data`
(Godot-free pack reader). `Arcanum.Simulation`, `Arcanum.Scripting` and
`Arcanum.Session` do not reference the profile, the pack or HD dimensions. The
consumers below read Legacy data only, in both profiles:

- scene plans: destinations, hotspots, offsets, flips, order, ranks;
- cell picking, passability overlay, route source and step assessment;
- the wall check (fade set) and roof/wall rules;
- animation: frame counts, group, FPS, action frames, timing;
- light: ambient, lattice, object colour;
- Session snapshots and quick save.

ARC-UI-006 acceptance proves this end to end: the same scripted scenario (walk,
light change, dialog, map change) yields byte-identical Session snapshots under
`Legacy`, under `Hd`, and with switches in the middle, and each item keeps the same
world destination in both profiles.

## Verification

This PR: `tests/Arcanum.TerrainViewer.Tests/HdScaledCropTests.cs` checks, with an
independent exact rational oracle and the production `AreaNearestAxis`:
consequences 1 and 2 for `N = 2, 3, 4` on random transforms including fractional
binary origins, non-unit zooms, nonzero crops, destinations smaller or larger than the
frame and reflection; the literal table above; the axis limit; row-key separation and
pan invariance of HD row keys; the identity of a nearest-replicated `N = 2` frame in a
painter-order raster of overlapping sprites with holes; that HD subtexels stay inside
their Legacy texel and all reach the screen only above magnification 1 (zoom 1 and 2,
and a twice-size body at zoom 1); and that the wall-fade column
`floor((sx − N·c) / N)` with the Legacy width reproduces the Legacy fade alpha with
nonzero crop starts, while the rejected `N·width` gradient does not. The axis-limit
test checks the sampler refusal only; the `HdAxisLimit` fallback is ARC-RENDER-014.

ARC-DATA-014 added the synthetic pack, recipe, PNG, import and budget tests in
`tests/Arcanum.Data.Tests/Hd` ([format](../formats/hd-pack.md)). ARC-RENDER-014 added the
native HD draws ([viewer](terrain-viewer.md#hd-render-path-arc-render-014)):
`HdPresentationTests` (scaled crop, `HdAxisLimit`, the frame source with every per-frame
reason, draw-order budget admission, retention and upload batches), the self-tests
`ARC-RENDER-014 self-test AREA HD PASS` / `WORLD HD PASS` (production compositor with a
synthetic nearest 4× pack: the HD raster equals Legacy at magnifications 0.5 to 4, with
fractional origins and with wall fades, and `FrameMissing`, `NoPack`, `PackRejected`,
`FrameRejected`, `HdBudget` and `Invalid` each draw the Legacy raster; `HdAxisLimit`,
`SourceMismatch` and `Pending` are unit-tested)
and the native smoke `ARC-RENDER-014 native-smoke AREA HD identity PASS` / `WORLD HD
identity PASS` on the installed original with a nearest 4× pack in a temporary directory.
ARC-UI-006 added the toggle checks ([viewer](terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006)):
`VisualProfileSwitchTests` (codec, generations, settled or timed commit, cancel, pack choice), the
self-tests `ARC-UI-006 self-test toggle PASS` (AREA: preparing frames wholly Legacy, one commit frame
wholly HD, Legacy commit, cancel, bounded wait with frames pending, map change, `NoPack`),
`ARC-UI-006 self-test WORLD toggle PASS` and `ARC-UI-006 self-test app PASS`, and the native smoke `ARC-UI-006 native-smoke Session invariance
PASS`, which proves the acceptance above on the installed original: dialog, walk, one game hour and
the cave entry from one quick save give byte-identical Session snapshots and drawn destinations
under `Legacy`, under `Hd` and with switches before and during every step. Human review of real HD
frames is ARC-RENDER-015 and never a merge gate.

## Amendment ARC-DATA-014 (2026-10-16)

Owner decisions: packs are obtained by building on the device from a recipe or by
importing a pack built on another device; handhelds are Steam Deck class devices with at
least 16 GB of shared memory on SteamOS/Linux or Windows; after the measured plain
upscales were rejected ("1 + 3"), the default is 4× with a soft edge, 2× stays
supported, and the format must also hold a generative pack. This amendment changes the
rules above as follows; the [pack format](../formats/hd-pack.md) has the details.

1. **Scale.** `N ∈ {2, 4}` is accepted, 4 is the default recipe. The sampling rule,
   consequences, wall fade and anchors are unchanged; they were written for any `N`.
2. **Soft alpha by default** (Anchors, mirroring … Alpha, above), checked per frame
   against the device's Legacy alpha. The Legacy hard edge is a recipe option.
3. **Packs built elsewhere.** A pack is portable (PNG + JSON, relative OS-neutral
   paths, no user, OS or GPU path) and found in a pack collection or a chosen folder.
   Its entries carry only the install-independent part of the frame key; on each device
   a frame is bound only if this installation's Legacy frame has the recorded size and
   RGBA hash, otherwise it draws Legacy (`SourceMismatch`), so a pack built on a Mac
   works on a handheld for every frame whose Legacy bytes match. Import verifies the
   integrity of every file and copies the pack atomically; nothing is compared with
   reference HD hashes, because upscaler output is not shown to be bit-exact across GPUs
   (ARC-RES-033). A rebuild is a new pack identity; a copy is not.
4. **Reasons.** `FrameRejected` is added and `SourceMismatch`/`Invalid` are made exact
   (table above). All remain per-frame Legacy draws.
5. **Per-device budgets.** The HD limits of the Budgets table are ceilings. Each device
   has an HD budget (`user://hd_budget_v1.json`, read by `HdDeviceBudgetCodec`) that may
   lower any of them: AREA visible bytes, AREA retained frames and bytes, WORLD bytes,
   terrain bytes; 0 turns HD off in that domain. A missing or invalid file means the
   ceilings; a value above a ceiling is clamped; nothing raises a ceiling except an
   amendment here. Admission stays deterministic per submission (`HdBudget`), and Legacy
   budgets are untouched. Sizes for orientation (inference from the ARC-RES-033 census ×
   N²): the Crash Site's characters and objects are ~30 MiB at 4× and all its 1,975
   distinct exterior frames ~400 MiB; a 4× terrain chunk is 16 MiB, so the reserved
   terrain ceiling of 192 chunks is 3 GiB, and a 16 GB handheld would typically lower it
   (48 chunks = 768 MiB). Real usage is still ARC-RENDER-014/016's to measure.
6. **Terrain chunks** are a pack entry kind (512-pixel chunk, ≥ 32 px neighbour
   padding, keyed by chunk and the hashes of the composed tiles). They were reserved here
   and are bound since ARC-RENDER-016 ([amendment](#amendment-arc-render-016-2026-10-17)).
7. **Method-agnostic.** The contract and the pack never assume a method: networks,
   algorithms, generative models (base model, adapters such as LoRA, seed, prompt and
   parameters are recorded) and external pipelines produce the same pack, checked by
   what it contains. One pack is active at a time.

## Amendment ARC-RENDER-016 (2026-10-17)

Owner request (project discussion, 2026-10-17): the ground, rocks and everything else drawn
in the Crash Site should come from the HD pack too, not only characters and objects. This
amendment changes the rules above as follows; the [pack format](../formats/hd-pack.md#terrain-chunk-entries)
and the [viewer](terrain-viewer.md#hd-render-path-arc-render-014) have the details.

1. **Walls bind like any object.** An AREA body of ART class 1 is an HD candidate keyed by
   its frame key. Its wall fade already uses the Legacy column `floor((sx − N·c) / N)` over
   the Legacy width (ARC-RENDER-014), so a faded HD wall gets exactly its Legacy alpha; the
   wall check, picking, passability and the fade set never see HD.
2. **Terrain binds per composed chunk.** In the AREA, tiles and facades are drawn as
   composed 512-pixel chunks (`AreaTerrainChunks`, exact under the nearest rule). A level-0
   chunk (zoom 1 and above, and any zoom whose transform has no decimated level) is an HD
   candidate: its question is the chunk coordinates, the SHA-256 of its composed Legacy
   RGBA and the digest of its ordered tile list (`HdChunkKey`); the HD chunk is exactly
   `N·512 × N·512`, bound with the scaled crop `(0, 0, N·512, N·512)` to the unchanged
   world square, so consequence 4 holds: a nearest `N×` chunk draws the Legacy chunk raster
   pixel for pixel. Decimated chunks (levels 1..3, zoom below 1 with an integer origin),
   per-command terrain (a plan that does not admit chunks) and WORLD terrain stay Legacy and
   are not candidates.
3. **Binding a chunk.** Entries are matched by coordinates and then by composed Legacy
   frame and tile digest, so two maps may have entries at the same coordinates (separate
   slices). No entry at those coordinates is `FrameMissing`; entries there that all differ
   are `SourceMismatch`; the review mark, file, PNG, size and alpha checks are those of a
   frame (the alpha check is against the composed chunk's binary alpha). Each refusal is a
   per-chunk Legacy draw.
4. **Budget.** Chunks have their own residency under the terrain ceiling of the Budgets
   table (`HdDeviceBudget.TerrainBytes`, reserved by ARC-DATA-014): one byte limit for
   visible and kept chunks and at most 1,024 kept chunks (the Legacy chunk count cap); the AREA frame limits are not
   shared. Admission, retention, uploads, the toggle protocol and the release rules are
   those of frames. The two residencies share one frame source; each drops only its own
   decoded pixels.
5. **Unchanged.** Plans, destinations, chunk composition, light (a chunk keeps its single
   ambient channel), wall fade, picking, routes, the wall check and Session read Legacy
   data only; Legacy chunk textures stay resident under both profiles.

## Not ready / out of scope

- The runtime HD path for characters and objects (ARC-RENDER-014) and the setting, its
  persistence, the toggle key and the timed commit (ARC-UI-006) exist in the terrain viewer;
  the exported app gets them with its next export. There is no pack picker: one pack is
  chosen as above.
- Since ARC-RENDER-016 walls and level-0 terrain chunks bind too; decimated chunks (zoom
  below 1), per-command terrain, WORLD terrain, roofs, UI and cursors stay Legacy.
- No shader smoothing profile, VRAM compression, mipmaps or box-filtered downsample.
- Visual quality of any upscaler, AI determinism across GPUs and memory on real packs
  are ARC-RES-033 / ARC-RENDER-014 questions, not claims of this contract.
- Original-runtime pixel parity stays NOT_VERIFIED, as for Legacy.

Provenance: the rules are derived from this repository's own exact sampling, scene
plan and wall-fade code.
