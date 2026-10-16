# HD presentation contract (ARC-RENDER-013)

Status: **contract and synthetic oracle tests only.** No HD pack format, loader,
renderer path, toggle or setting exists yet. They are ARC-DATA-014 (pack format and
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
  Legacy frame. **N** is a positive integer scale declared by the pack. The first
  slice accepts only `N = 2` (owner decision 2026-10-16); the rules are written for any
  integer `N ≥ 2` so a later 3× or 4× pack needs no new contract.
- **Frame key**: the identity of a Legacy frame (resource entry, effective palette
  slot, physical group, frame, plus the hashes the existing `AreaFrameKey` carries).
  An HD frame is bound to exactly one frame key.
- **Visual profile**: `Legacy` or `Hd`, a presentation setting. It selects which
  texture draws a frame key; nothing else.
- **HD pack**: a local directory outside the repository (`local/hd-packs/<id>/`,
  ignored by git) with a manifest and HD frames. It is built by the owner on their
  machine and is not distributed (option B of the 2026-10-16 legal review). Pack
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
production sampler (`AreaNearestAxis`, the index rows and the shader) needs no new
mode: HD is only a different crop and texture.

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
3. **Which subtexel.** The subtexel offset is `floor(N·u) mod N`. With integer origin
   and destination, `n = m` and zoom 1, `u` sits at a texel center, so `N = 2` always shows
   subtexel 1 (subtexel 0 when reflected): HD at zoom 1 is a point sample of the HD
   frame with no extra detail. At zoom 2 every subtexel reaches the screen (1:1 HD).
   Detail appears only above zoom 1; a box-filtered downsample would be a different
   rule and is out of this contract.
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
Legacy axis up to 32,767 fits.

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
- **Wall fade** (ARC-RENDER-011) is a function of the Legacy column. The shader
  computes the column from the sampled index `sx` (Legacy: `sx − c`, walls are drawn
  with `c = 0` today); on an HD draw it uses `floor((sx − N·c) / N)`, which equals the
  Legacy `sx − c` (consequence 2), and the Legacy crop width as `fade_width`, so every
  screen pixel gets exactly the alpha it gets in Legacy. A finer gradient over
  `N·width` would differ by up to half a Legacy step and is not allowed. The fade set
  itself comes from the wall check, which never sees textures.
- **Light** (ARC-RENDER-010/012) is computed in world coordinates (lattice, ambient,
  wall gradient across the world destination) and is profile-independent.
- **Alpha.** The default HD alpha is the parent Legacy alpha (nearest, hard edge);
  with it the per-pixel coverage of HD equals Legacy (consequences 2 and 4). A softer
  HD alpha is an opt-in pack option whose mask must still pass the ARC-DATA-014 mask
  check; with it, only pixels inside the unchanged destination may change coverage.
  No game rule reads sprite alpha; any future sprite-pixel hit test reads the Legacy
  frame.

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
| `SourceMismatch` | The entry was built from different Legacy bytes (mod, other version) |
| `Invalid` | Size is not exactly `N·w × N·h`, hash or alpha-mask check failed (ARC-DATA-014) |
| `HdAxisLimit` | `N·(c + m) > 65534` on an axis |
| `HdBudget` | The frame does not fit the HD budget for this submission |
| `Pending` | Not yet decoded/uploaded (preload in progress) |

`Legacy` profile never consults the pack. An `unreviewed` pack is used and the HUD
says so. The HUD shows, per submission, the count of frames drawn HD and the count of
fallback frames by reason. Validation details and the manifest belong to ARC-DATA-014;
this contract only fixes that every outcome other than a valid HD frame is a
per-frame Legacy draw with a reason.

## Budgets

HD textures live in their own caches, keyed by `(profile, frame key)`, with their own
budgets. Legacy budgets and behaviour are unchanged. HD at scale `N` costs `N²` bytes
per Legacy byte, so the HD limits are `N²` times the Legacy ones they shadow (×4 at
`N = 2`):

| Domain | Legacy limit today | HD limit |
| --- | --- | --- |
| AREA visible GPU set | `AreaRepositoryPolicy.MaxFrames` 2,444 frames, `MaxCpuBytes` 81,853,096 B | same frame count, `N² · 81,853,096` B |
| AREA retained (left the view) | 512 frames, 32 MiB | 512 frames, `N² · 32` MiB |
| WORLD actor + static textures | inside `TerrainCachePolicy.MaxRgbaBytes` 256 MiB | key caps of the actor/static domains, `N²` times their Legacy bytes, at most 256 MiB |
| Terrain chunks | `AreaChunkTextureCache` 192 MiB, 1,024 chunks | none until ARC-RENDER-016 (terrain stays Legacy) |

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
6. **Release.** A map change, reload or exit cancels pending work, and every HD
   texture and staging buffer is released with its cache, under the same single-owner
   discipline as the [actor visual contract](actor-visual-contract.md). A switch to
   `Legacy` releases HD textures down to the HD retain limit.
7. **Startup.** The saved profile is applied before the first submission. With
   `Hd` and no pack, the view runs with every frame `NoPack` and says so in the HUD.

The profile is not part of the session snapshot or quick save; a save made in either
profile loads in either profile.

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
pan invariance for HD crops; the identity of a nearest-replicated `N = 2` frame in a
painter-order raster of overlapping sprites with holes; that HD subtexels stay inside
their Legacy texel and reach the screen only above zoom 1; and that the wall fade
column taken from `floor(H / N)` reproduces the Legacy fade alpha.

Later tasks must add: native HD draws on real viewports (ARC-RENDER-014), the toggle
and Session-snapshot invariance (ARC-UI-006), pack validation fixtures
(ARC-DATA-014). Human review of real HD frames is ARC-RENDER-015 and never a merge
gate.

## Not ready / out of scope

- No runtime HD path, pack, loader, setting or key exists after this task.
- Terrain tiles, facades and terrain chunks stay Legacy until ARC-RENDER-016; walls,
  roofs, UI and cursors until a later stage.
- No shader smoothing profile, VRAM compression, mipmaps or box-filtered downsample.
- Visual quality of any upscaler, AI determinism across GPUs and memory on real packs
  are ARC-RES-032 / ARC-RENDER-014 questions, not claims of this contract.
- Original-runtime pixel parity stays NOT_VERIFIED, as for Legacy.

Provenance: the rules are derived from this repository's own exact sampling, scene
plan and wall-fade code; no CE code or structure is used.
