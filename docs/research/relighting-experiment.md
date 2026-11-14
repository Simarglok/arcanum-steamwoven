# 2D relighting experiment: normal-mapped sprites lit by point lights (ARC-RES-1978)

Status: **experiment, not a project decision, not part of Legacy.** The code is a switchable presentation hook in the `area`
viewer that is **off by default**; with it off the viewer draws what it drew before (checked pixel for pixel by the
`relight` smoke stage, below; the off path adds only a little per-item bookkeeping). Requirements: [FR-LIGHT-013](../requirements/lighting-time.md#fr-light-013) (a separate
Remastered visual profile) and [FR-PRODUCT-007](../requirements/content-product.md#fr-product-007) (late image improvements:
normal maps and the like). It reads or changes no geometry, clock, RNG or gameplay luminance. Origin: the owner's handoff of
2026-11-14 (a DLSS-style enhancement does not fit: it needs 3D scene data and motion vectors and a model that can only be taken from
other games; the deterministic, free 2D equivalent is dynamic relighting of the original sprites).

Question: on one small location, do the original sprites with a generated normal each, lit by dynamic lights (torches, a spell, a
carried light, day and night), give a visibly better scene without breaking the look of the original art, and what does it cost?
Result and recommendation: [Verdict](#verdict).

## What was built

| Piece | Where | What |
| --- | --- | --- |
| Relit look | `tools/terrain-viewer/Godot/AreaRelight.cs`, `AreaDrawItem.cs` | Two extra shader programs (normal blend, additive blend), built only when the relit look is first switched on; the three Legacy programs are never edited. Per pixel they derive a normal from the sprite's own luminance (3×3 Sobel, alpha aware) and add a normal-mapped diffuse term for the map's stationary lights and the carried lights, on top of the Legacy colour. The light list reaches every item through global shader uniforms (a 32×2 float texture and two vectors), so no per-item uniform changes with the lights. |
| Light sources | `TerrainCanvasArea.cs`, `TerrainViewerAreaLighting.cs`, `AreaLocalLighting.CarriedPlacements` | The sources are the ones the Legacy lattice already uses: the map's stationary lights of the shown hour and the carried lights (the hero's worn light, critters' lights), with their original colours and the emitter tint rule (INDOOR and OUTDOOR take the hour's ambient, LT15). OFF and DARK lights add nothing to the relit term. |
| Switch | environment `ARC_RELIGHT` = `off`, `add`, `night`, `delight` (first mode), `ARC_RELIGHT_STRENGTH`, `_DELIGHT`, `_HEIGHT`, `_GROUND`, `_OBJECT`, `_CRITTER` | No launch argument and no key was added (the launcher and input contracts stay as they are). `AreaRelight.SetMode` / `Cycle` switch at run time and `TerrainCanvasArea.RefreshRelight` redraws the shown items. |
| Generator | `experiments/relight-lab/` | A Godot-free C# counterpart of the shader's normal (same Sobel, bevel sign and gains; the shader has no blur and uses a continuous alpha in the bevel, so they are similar, not bit-exact) (height from luminance, optional blur, Sobel, alpha-aware silhouette bevel, three presets, OpenGL or image-space green), with MSTest tests and a command line (`normals`, `bench`). It is the reference for a later batch run over the ART frames. It lives in `experiments/`, not in `toolkit/` as the handoff said: `toolkit/` is git-ignored. |
| Smoke | stage `relight` (`TerrainViewerRelightSmoke.cs`) | `./arcanum smoke map "crash site cave" --stages relight` (and the same on the main map): Legacy checks, day and night captures in every mode, a walk with the carried torch (it probes directions for a hero walk of three to six cells, then walks 60 frames), frame times, memory. `ARC_RELIGHT_CAPTURE=/absolute/dir` keeps the PNG captures; they are derived from the player's art and stay out of git. |

Three modes answer the baked-light mitigations of the handoff (the shading baked into the original sprites is the main risk,
because a light added on top of it lights twice):

- **Add** (a): the relit term is added at a low strength (0.6) at any hour.
- **Night** (b): the same term, weighted by how dark the item's own ambient is (`1 − luminance of the Legacy tint`): daylight stays
  Legacy, a dark scene takes the full term.
- **Delight** (c): the baked shading is first pulled partly (35 %) towards the local mean luminance, then the night-weighted term is added.

## Departures from the handoff

1. **The sprites are not `CanvasTexture` nodes with `PointLight2D` lights.** The viewer draws every terrain chunk, body and the hero
   through one custom canvas-item shader (`AreaDrawItem`) that resolves each pixel from nearest-neighbour index rows, applies the
   Legacy ambient and light lattice and the wall fade, and is `unshaded`. Godot's 2D lights only act on a lit item, and a lit item
   would also be multiplied by the engine's own ambient, so the Legacy colour (and with it the pixel-identical off state) could not be
   kept. The relit term is therefore computed inside that shader from the same light list. The frame-time cost of Godot's own light
   pass (one pass per light per item) was **not** measured here; the shader loop's cost is.
2. **No pre-baked normal textures in the viewer path.** The normal is derived per pixel from the frame texture the shader already
   samples (eight extra fetches, only where a light reaches). The memory cost of stored normal maps is reported as an estimate
   (a full-size RGBA8 texture per frame) and measured in the lab on real frames.
3. The generator is in `experiments/relight-lab/`, not `toolkit/` (git-ignored).
4. **No key or palette action for the A/B switch.** The mode switches at run time in code (`AreaRelight.SetMode` / `Cycle`,
   `RefreshRelight`; the smoke stage does it) and at launch by `ARC_RELIGHT`, but no diagnostic-palette action was added (the input
   contracts and their tests stay untouched); an interactive A/B in a running window is a small follow-up if the owner wants it.

## How the original lights (read first, as the project rules ask)

Source: the ledger rows [LT1–LT15](ledger/world.md#lights-on-all-maps-arc-res-069) (read in the executable's light code, addresses
there) and [FR-LIGHT-003…011](../requirements/lighting-time.md). In prose:

- A light is a 48-byte sector record or an object light (one primary and up to four overlay lights per object). It has a light ART
  frame (a radial colour mask), a colour, an offset, and flags: OFF, DARK (it subtracts), INDOOR and OUTDOOR (it takes the hour's
  indoor or outdoor ambient instead of its own colour), no-animation and no-shadow. Drawing and sampling gate only on OFF and DARK.
- The result is a colour, not a direction. The original sums the lights' masks into additive and subtractive buffers and tints what is
  drawn: terrain takes it per pixel from a 40×20 lattice (bilinear), a body takes one colour at its anchor, a wall two (its ends). There
  are no normals and no light direction in the original draw; the only directional thing is a shadow builder (up to four shadows of
  a light that is not off, dark or flagged no-shadow, LT14), which is not used here.
- Day and night is the hourly ambient scheme (24 keys, outdoor and indoor colour each, FR-LIGHT-003…006): tiles and facades take the
  indoor or outdoor colour by their type bit, critters, scenery, walls and roofs the outdoor one. Nocturnal scenery lights are switched
  on by hour (off for hours 6 to 17, LT4). An animated light steps its frame by a timed event (LT6).
- A creature's carried light is light ART 1 frame 0 with one colour per size (extra large 244/255/255, large 255/214/172, medium
  236/138/85, small 167/88/58), moved with the creature (LT10, LT12).

So the original's own relighting is a per-pixel colour tint without direction; this experiment adds the direction (a normal per
sprite pixel) and nothing else. The Legacy lattice and the carried-light lattice are untouched and still apply.

## Recon: how the viewer draws

Every terrain chunk, body, wall and the hero is one `AreaDrawItem` (a `Node2D` with an index-row mapping texture and one of three
shader programs: normal, additive, multiply). The ART frame texture is the item's `TEXTURE` (RGBA, palette already applied; HD frames
are the same shader at N×). The per-item inputs are `ambient`, `ambient_right`, a terrain lattice and the carried lattice; the lattice
space is the chart's screen space, which is also where the lights' rectangles are, so a light's centre and radius come straight from
its placement rectangle. The least intrusive place for a per-sprite normal is therefore this one shader.

## Results

Runs: the owner's Mac (macOS, Godot 4.7.2 .NET, 1280×800 window, vsync off for the timings), pilot locations the Crash Site cave
(`crash site cave`: four original stationary lights, the hero wearing a large light) and the main map at the Crash Site (no stationary
lights, the hero's torch only); the hero's carried light is the "moving torch". Both through the smoke stage `relight`; the numbers
below are one run each (frame times move by about a millisecond between runs: read them as an order of magnitude).

| Check | Result |
| --- | --- |
| Legacy pixel-identical | **Yes, with the look off** (three captures of the same frame byte-identical, also after the look was switched on and off again: 0 pixels differ, both maps). With the look off no relit shader program is built and no relit uniform is set (by reading the code; the smoke compares pixels only). |
| Relit differs from Legacy | Cave (strength 0.6): about 133,000 of the 144,000 pixels in the 480×300 window round the hero differ in `add`, 123,000 in `night`; the look is a visible warm pool with direction on the floor and the rock. Main map at night: about 81,000 (`add`) and 72,000 (`night`). |
| Daylight (main map, hour 14) | `night` mode: 295 pixels differ at strength 0.6 (830 at 1.6): the Legacy daylight stays. `add` at 0.6: about 74,500 pixels differ (a faint warm spot round the torch). `delight`: about 131,700 (it flattens the shading everywhere, and the ground and the sprites visibly soften). |
| Cave at "day" | Same as night: the cave's own ambient does not follow the hour, so the night weighting is by the cave's darkness, not by the clock. |
| Frame time (mean of 90 frames, ms; real lights / +5 / +20 synthetic lights) | Last run, cave: Legacy 5.7, relit 5.7 / 7.0 / 7.8 (the 0.6 run: 5.7, 6.2 / 6.6 / 6.8); main map: Legacy 5.6, relit 5.6 / 5.6 / 6.0 (and 5.6, 5.6 / 5.6 / 5.6). Earlier runs gave Legacy 2.8 to 5.7 and relit 4.1 to 5.6. **Run-to-run noise (about a millisecond) is as large as the effect**, so the honest reading is: the relit term is not resolved from noise with the real lights (at most about 1.5 ms) and up to about 2 ms more for 20 extra lights in reach, on this Mac at 1280×800. Cost grows with covered pixels times lights in reach, not with sprite count (the lights are global uniforms). **Not measured on Steam Deck-class hardware**; the Deck's screen is the same 1280×800 and its GPU is much weaker, so a run there (and a GPU-side timing) is needed before any go ([#2000](https://github.com/Simarglok/arcanum-steamwoven/issues/2000)). |
| Memory of normals | Not stored in the viewer path (derived per pixel). As stored RGBA8 maps they would double each sprite frame: the visible sprite frames are 119 KB (main map, 9 frames) to 866 KB (cave, 15 frames) as normals; 24 real frames from the cave total 265,530 pixels, 1.06 MB. Two bytes per pixel (x and y, z rebuilt) would halve it. The census of all ART frames is not at hand here (the interface art alone is 31.75 Mpx, [HD interface art](../rendering/hd-interface-art.md)). |
| Generator speed | `experiments/relight-lab` `bench` on those 24 frames: 44.7 ms in a Debug build (168 ms per megapixel, JIT included); a Release batch will be several times faster. Deterministic: the same pixels and options give the same bytes (tests). |
| Critter flicker | **Not shown either way.** The walk run (a few cells, same walk once Legacy and once relit, one frame each, a 960×600 band round the start) changed the relit term by 0.0002 to 0.0012 (of full luminance) per frame on average against the Legacy picture's own 0.003 to 0.006, with single-pixel jumps up to 0.14 (cave) and 0.6 (main map); the jumps follow the carried light, which the original moves per committed cell, and the band is a scene measure, not a sprite one. The smoke's hero texture did not animate (the 15 hero frames it saw were one standing frame, so the lab's `shimmer` ratio over them is 0 by construction), so the walk cycle's own normals were not tested. By construction a normal is a function of the pixel's 3×3 neighbourhood, so an unchanged neighbourhood cannot flicker; palette dither between walk frames can, which the critter preset's 3×3 blur is meant to calm (the shader has no blur). Follow-up: [#2000](https://github.com/Simarglok/arcanum-steamwoven/issues/2000). |
| Memory of the look itself | Two shader programs, a 32×2 float texture, no per-item state. |

Note: no images are committed: the composites below were posted to the project thread (labels in English). They show, for the cave
at night and the main map at day and at night, Legacy against the three modes at strength 0.6 and 1.6.

What the pictures show, as a reading of the author, for the owner to judge:

- **Add** gives a believable directional pool round the torch and the cave's lamps: floor grain and rock ridges pick up light on the
  side facing the light, which the Legacy lattice (a flat colour tint) cannot do. At 0.6 it is subtle; at 1.6 it is clearly visible and
  starts to look like a spotlight painted on the ground, especially in daylight.
- **Night-weighted** (weighted by the item's own ambient) keeps daylight Legacy (exterior, hour 14: a few hundred pixels change) and in a dark scene looks nearly as strong as add; at night on the main map it is the best looking of the three, a clean pool with grain and no wash. It is the
  safest for "original art not degraded in daylight". In the cave, whose ambient is dim all day, it is only slightly weaker than add.
- **De-light** (35 % of the shading flattened, everywhere) visibly softens the art (the ground loses grain, a critter looks blurred) for
  little gain: not worth it as built. A proper de-light would need the baked light direction per sprite family, which this experiment does not have.
- The hero and critters take the term from their own pixels (silhouette bevel plus luminance slope); a 30×82 sprite gets a plausible
  normal (see the lab's `normals` output), but a sprite this small has little to bend.

## Night cities: Tarant and Ashbury (ARC-RES-2007)

Follow-up after the owner's answer to [#2001](https://github.com/Simarglok/arcanum-steamwoven/issues/2001) (continue the experiment on a night city with the original
lighting). Same hook, same three modes; new in this round: a **view culling rule** for the light list, and the smoke stage `relight` with
`ARC_RELIGHT_SCENE=city`. Everything stays opt-in (`ARC_RELIGHT`), Legacy untouched while it is off (the cave `relight` stage and the main-map `map` stage still pass).

**Where the cities are.** Tarant and Ashbury are not maps of their own: they are main-map windows (the interiors `tarant sewers`, `ashbury haunted castle` and the like are
other maps). The area table `mes/gamearea.mes` puts the markers at sector 972,1026 (Tarant) and 505,976 (Ashbury); the lit streets lie west of them, so the captures use the
windows `--sector 969,1026` (Tarant: 356 fixed lights at night, 301 lit) and `--sector 504,975` (Ashbury: 253 fixed lights at night, 170 lit, 83 dark). About three quarters
of the lit lights are large (a rectangle half-width of 220 to 240 pixels); 9 to 15 are outdoor-tinted. The fixed lights are the sector's stationary lights plus the object lights
of nocturnal scenery (the lamps, ARC-RENDER-030), which the Legacy lattice already uses.

**The light list.** The 32-light budget is not reached: in a 1280×800 view at zoom 1 between 6 and 18 lights reach the view (Tarant 13, 6 and 8 at the three captured spots,
Ashbury 11, 18 and 14). The rule is now: of the carried, map and synthetic lights keep those whose disk meets the shown view, and upload the 32 whose centre is nearest the view's centre
(`AreaRelight.SetView`, `InViewCount`); the earlier rule (carried first, then the map's lights nearest the first carried one) would have kept the first 32 of 300 in file order
in a city with no torch.

**Moving lights.** The saved data of the windows holds **no critter with a light** (a scan of 3×3-sector windows, centres every third sector from −9 to +9 round each marker, by `ObjectLightReader`: 0 critter lights;
the object lights, up to 263 in a window, are all scenery), and the AI host cannot be built over these windows (a defect, [#2036](https://github.com/Simarglok/arcanum-steamwoven/issues/2036): the
window's inventory seed is refused with `GoldHandleMismatch` or `ListedObjectUnresolved`), so no NPC's worn light could be listed either; in the one Ashbury window that does seed (sector 505,979,
15 NPCs) none wears a light. The Ashbury wisps the owner remembers are therefore **not lit critters**: no critter holds a light item either (checked over the windows round the city). ARC-RES-2061 found them by
capturing the east and north-east of the city at night (`ARC_RELIGHT_AT=x,y;x,y` in the shots mode): a violet orb critter standing on the field east of the town (most likely prototype 28471,
seven of them in the central window, more north and west) that is **bright only in its own sprite and lights nothing**: in Legacy and in every relit mode the corn round it stays dark.
To make wisps light their surroundings the relit look would have to treat such critters as emitters (a colour from the sprite, a radius of our own), a deliberate difference from the original
that is the owner's call (HD variant). Whether they move cannot be seen yet: the AI host does not build over the Ashbury windows (#2036). As a stand-in the capture uses the original's own moving-light mechanism (a carried light follows its creature, LT10, LT12): the hero wears an extra-large torch (cold white 244/255/255,
the colour a wisp-like light would have) and walks towards a cell eight away; Legacy draws that light too, so the comparison is fair. The relit look follows it frame by frame; the pool on the stones and the
hero's own highlights move with the light (two frames cannot show flicker; the check on real walk-cycle frames is still [#2000](https://github.com/Simarglok/arcanum-steamwoven/issues/2000)).

**Numbers** (the owner's Mac, 1280×800, vsync off, mean of 90 frames, one run each; noise about a millisecond):

| City | Legacy | Relit night 0.6 | Relit add 0.6 | Lights in view (with the hero's torch) |
| --- | --- | --- | --- | --- |
| Tarant spot a (indoors, the densest cluster) | 7.5 ms | 9.0 ms | 9.0 ms | 14 |
| Ashbury spot a | 6.1 ms | 8.1 ms | 8.2 ms | 12 |

The relit term costs about 1.5 to 2 ms here (against up to 1.5 ms in the cave): the city's lights are large, so most pixels of the view are inside several of them, and the per-pixel
cost grows with covered pixels times lights in reach. Still a desktop-class number; the Steam Deck run remains open ([#2000](https://github.com/Simarglok/arcanum-steamwoven/issues/2000)).
Pixels differing from Legacy at the three spots, of 1,024,000 (night 0.6): Tarant 823,000 / 481,000 / 468,000; Ashbury 836,000 / 872,000 / 357,000; that is the whole lit area, not
only a pool, because most city lights are 440 px wide (the difference is small per pixel).

**Reading** (the author's, for the owner to judge from the composites posted in the project thread):

- Night weighted at strength 1.6 is the clearest on the street: a lamp lights the cobbles and the wall beside it with the stone's own grain, from the side facing the lamp, where Legacy has
  a flat tint. At 0.6 (add or night) the effect is faint at this size.
- Indoors the candles and lanterns give a warm directional wash on the floor boards; strong at 1.6, a little patchy on large floor areas.
- Most Ashbury streets at night are close to black in Legacy (few lights), and relighting cannot add light where there is none: the look needs the light sources, not the normals.
- De-light 0.6 again only softens the art and is not worth keeping as built.
- The hero's torch pool in the walk frames is nicer relit: the stones round the hero show the direction of the light.

The 32-light rule has two known edges: with more than 32 lights in view (a zoomed-out view, a denser city) the hero's torch can be dropped when it is far from the view's centre, and
lights enter and leave the list as the view pans (a hard cut at the 32nd). Neither happens at zoom 1 in these cities. The strengths above (0.6, 1.6) are set by the smoke itself,
whatever `ARC_RELIGHT_STRENGTH` says.

**Updated recommendation.** Unchanged in kind (conditional go for a bounded night-and-indoor option, no-go for daylight and for de-light as built), with two additions: the night weighted
mode at about 1.2 to 1.6 is the one to tune, and the cost in a city is about 2 ms (to be checked on the Deck), so the option should be a switchable profile, not a default. Open: where
the Ashbury wisps come from, and the AI-host-less city windows ([#2036](https://github.com/Simarglok/arcanum-steamwoven/issues/2036)).

## Verdict

**Conditional go, for a bounded "night and indoor" relit option; no-go for daylight and for de-light as built.** The experiment
shows the idea works on the real data and the real draw path with a small, switchable hook, Legacy untouched, and a cost within the noise to about a millisecond with the real lights on a desktop-class Mac. It does not show that the result is clearly better to the eye: that is the owner's call
from the composites, and a stronger statement would need the Steam Deck frame times and a real walk-cycle flicker check, both open.

If the owner says go, full coverage is these pieces, none large:

1. **Category tuning** (the real work): ground, wall, scenery, item, critter and roof gains and blur, per light type (torch, lantern,
   spell); tuning rounds with the owner's eye. The shader already knows the kind of each item.
2. **Light sources**: the map's stationary lights and the carried lights are done; object lights of the main map's scenery
   (ARC-RENDER-030), lit eye candy (ARC-MAGIC-1229) and animated lights would feed the same list, the radius cap (32 lights) and a
   culling by view need a decision.
3. **HD frames**: the same shader runs on N× frames; the Sobel gain must scale with N; HD frames may carry their own normals later.
4. **Optional stored normals**: only if per-pixel derivation proves too noisy or too slow on the Deck; the lab generator is the batch
   tool, about 168 ms per megapixel in Debug.
5. **Profile and settings**: FR-LIGHT-013 asks for a separate Remastered profile: a setting, not an environment variable.
6. **Deck run and flicker run** (open now): frame times at 1280×800 on Steam Deck-class hardware with 1, 5 and 20 lights, and a
   real animated critter walk cycle.

What this experiment did **not** do: use Godot `PointLight2D` / `CanvasTexture` (departure 1 above: the engine's own light pass was
not measured, so the shader loop is not compared with it), test shadows from occluders (out of scope), touch the UI (it is unlit: the
hook lives only in the world item shader), or compare with Laigter (the free generator was not needed: the one-line Sobel in the
shader was good enough to judge the idea).

The owner's go / no-go is [#2001](https://github.com/Simarglok/arcanum-steamwoven/issues/2001).

## Reproduce

From the repository root, through the [launch queue](../workflow/toolchain.md#launch-queue-arc-dev-033):

    ./arcanum smoke map "crash site cave" --stages relight --env ARC_RELIGHT_CAPTURE=/absolute/dir
    ./arcanum smoke map Arcanum1-024-fixed --stages relight --env ARC_RELIGHT_CAPTURE=/absolute/dir

The night cities (ARC-RES-2007; PNGs named by `ARC_RELIGHT_CITY`):

    ./arcanum smoke map Arcanum1-024-fixed --stages relight --hero human-male --sector 969,1026 --env ARC_RELIGHT_SCENE=city --env ARC_RELIGHT_CITY=tarant --env ARC_RELIGHT_CAPTURE=/absolute/dir
    ./arcanum smoke map Arcanum1-024-fixed --stages relight --hero human-male --sector 504,975 --env ARC_RELIGHT_SCENE=city --env ARC_RELIGHT_CITY=ashbury --env ARC_RELIGHT_CAPTURE=/absolute/dir

Full-frame night shots for the owner (ARC-RES-2050): add `--env ARC_RELIGHT_SHOTS=1` (and optionally `--env ARC_RELIGHT_HOURS=20,23,3`) to the city commands. The stage then skips the walk and
the timing and captures up to three spots at least 700 px apart (each with at least four lit lights within 350 px) at every hour, in Legacy, night 1.0 and night 1.6
(`<city>-shot-<spot>-h<hour>-<variant>.png`, 1280×800). `ARC_RELIGHT_AT=x,y;x,y` (world tiles) stands at those tiles instead (spots `at1`, `at2`, ...; the wisps were found at sector 505,975 with `32340,62389;32379,62519`). In the first runs the ambient colour changed only slightly between the night hours 20, 23 and 3, so the hours differ less than the spots.

The PNGs under `ARC_RELIGHT_CAPTURE` are derived from the player's art: keep them outside git. The lab:
`experiments/relight-lab/README.md`.

