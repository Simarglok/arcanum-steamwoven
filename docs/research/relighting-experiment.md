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
   pass (one pass per light per item) was **not** measured here; the shader loop's cost is (it was measured afterwards: [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000): the same order, about a millisecond for 20 lights on the Mac).
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
| Frame time (mean of 90 frames, ms; real lights / +5 / +20 synthetic lights) | Last run, cave: Legacy 5.7, relit 5.7 / 7.0 / 7.8 (the 0.6 run: 5.7, 6.2 / 6.6 / 6.8); main map: Legacy 5.6, relit 5.6 / 5.6 / 6.0 (and 5.6, 5.6 / 5.6 / 5.6). Earlier runs gave Legacy 2.8 to 5.7 and relit 4.1 to 5.6. **Run-to-run noise (about a millisecond) is as large as the effect**, so the honest reading is: the relit term is not resolved from noise with the real lights (at most about 1.5 ms) and up to about 2 ms more for 20 extra lights in reach, on this Mac at 1280×800. Cost grows with covered pixels times lights in reach, not with sprite count (the lights are global uniforms). **Not measured on Steam Deck-class hardware**; the Deck's screen is the same 1280×800 and its GPU is much weaker, so a run there is needed before any go: the stand-in and an estimate are in [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000), the device run is [#2297](https://github.com/Simarglok/arcanum-steamwoven/issues/2297). |
| Memory of normals | Not stored in the viewer path (derived per pixel). As stored RGBA8 maps they would double each sprite frame: the visible sprite frames are 119 KB (main map, 9 frames) to 866 KB (cave, 15 frames) as normals; 24 real frames from the cave total 265,530 pixels, 1.06 MB. Two bytes per pixel (x and y, z rebuilt) would halve it. The census of all ART frames is not at hand here (the interface art alone is 31.75 Mpx, [HD interface art](../rendering/hd-interface-art.md)). |
| Generator speed | `experiments/relight-lab` `bench` on those 24 frames: 44.7 ms in a Debug build (168 ms per megapixel, JIT included); a Release batch will be several times faster. Deterministic: the same pixels and options give the same bytes (tests). |
| Critter flicker | **Not shown either way.** The walk run (a few cells, same walk once Legacy and once relit, one frame each, a 960×600 band round the start) changed the relit term by 0.0002 to 0.0012 (of full luminance) per frame on average against the Legacy picture's own 0.003 to 0.006, with single-pixel jumps up to 0.14 (cave) and 0.6 (main map); the jumps follow the carried light, which the original moves per committed cell, and the band is a scene measure, not a sprite one. The smoke's hero texture did not animate (the 15 hero frames it saw were one standing frame, so the lab's `shimmer` ratio over them is 0 by construction), so the walk cycle's own normals were not tested. By construction a normal is a function of the pixel's 3×3 neighbourhood, so an unchanged neighbourhood cannot flicker; palette dither between walk frames can, which the critter preset's 3×3 blur is meant to calm (the shader has no blur). Answered later with the walk frames of every facing: [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000) (no flicker beyond the animation's own change). |
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
the object lights, up to 263 in a window, are all scenery), and the AI host could not be built over these windows when this was written (a defect, [#2036](https://github.com/Simarglok/arcanum-steamwoven/issues/2036), fixed by ARC-WORLD-2036: the
window's inventory seed was refused with `GoldHandleMismatch` or `ListedObjectUnresolved`), so no NPC's worn light could be listed either; in the one Ashbury window that does seed (sector 505,979,
15 NPCs) none wears a light. The Ashbury wisps the owner remembers are therefore **not lit critters**: no critter holds a light item either (checked over the windows round the city). ARC-RES-2061 found them by
capturing the east and north-east of the city at night (`ARC_RELIGHT_AT=x,y;x,y` in the shots mode): a violet orb critter standing on the field east of the town (most likely prototype 28471,
seven of them in the central window, more north and west) that is **bright only in its own sprite and lights nothing**: in Legacy and in every relit mode the corn round it stays dark.
To make wisps light their surroundings the relit look would have to treat such critters as emitters (a colour from the sprite, a radius of our own), a deliberate difference from the original
that is the owner's call (HD variant). Whether they move cannot be seen yet: the AI host did not build over the Ashbury windows (#2036, since fixed). As a stand-in the capture uses the original's own moving-light mechanism (a carried light follows its creature, LT10, LT12): the hero wears an extra-large torch (cold white 244/255/255,
the colour a wisp-like light would have) and walks towards a cell eight away; Legacy draws that light too, so the comparison is fair. The relit look follows it frame by frame; the pool on the stones and the
hero's own highlights move with the light (two frames cannot show flicker; the check on real walk-cycle frames was made later, [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000)).

**Wisps (ARC-RENDER-2064).** By look there is one wisp species: art species 24 (`art/monster/wow/wowuwxaa.art`, 8 facings of 16 frames at 12 fps, a 40×40 px soft disc with a white-hot core and a slow pulse), in four palettes,
one prototype each: 28471 "Love Lights" (violet, disc tone 9957D3), 28472 "Will-o-the-Wisp" (amber B09949, the only one whose peak is not pure white), 28473 "Volar's Wisp" (cyan 57D3D3), 28474 "Death Lantern" (red E53B3A).
The range 28310..28471 of the monster file ends at the violet one; the other three are the next numbers (loose prototype files). Places (all NPC placements, none in the static sector data): 28471, 11 on the main map,
in the sectors (503,975), (504,974), (504,975), (504,977), (505,975), (505,976), east of Ashbury; 28472, 6, and 28474, 3, all in sector (1695,1317); 28473, 5, in (1551,758) and (1695,1316..1317); the Vormantown module's
multiplayer map holds 28472 ×7 and 28474 ×3. No wisp stands on an interior or dungeon map of the Arcanum module. No other creature looks like an orb of light (the air elemental is a dark cloud, the Araya a jellyfish with
tentacles). Created critters (summons, script action 52) are not scanned. With the relit look the four kinds now glow in their own tone (the area lighting document, "Wisp glow"); the open choice was the owner's: only in HD, Legacy unchanged.

**Numbers** (the owner's Mac, 1280×800, vsync off, mean of 90 frames, one run each; noise about a millisecond):

| City | Legacy | Relit night 0.6 | Relit add 0.6 | Lights in view (with the hero's torch) |
| --- | --- | --- | --- | --- |
| Tarant spot a (indoors, the densest cluster) | 7.5 ms | 9.0 ms | 9.0 ms | 14 |
| Ashbury spot a | 6.1 ms | 8.1 ms | 8.2 ms | 12 |

The relit term costs about 1.5 to 2 ms here (against up to 1.5 ms in the cave): the city's lights are large, so most pixels of the view are inside several of them, and the per-pixel
cost grows with covered pixels times lights in reach. Still a desktop-class number; the Steam Deck run remains open ([#2297](https://github.com/Simarglok/arcanum-steamwoven/issues/2297), the stand-in is in [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000)).
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
mode at about 1.2 to 1.6 is the one to tune, and the cost in a city is about 2 ms (to be checked on the Deck), so the option should be a switchable profile, not a default (settled differently: see the Verdict, it is a part of the HD option). Open: whether
the wisps' glow (done, ARC-RENDER-2064), and the AI-host-less city windows ([#2036](https://github.com/Simarglok/arcanum-steamwoven/issues/2036)).

## Walk cycle, a Steam Deck stand-in and Godot's own lights (ARC-RES-2000)

The three gaps the experiment left open ([#2000](https://github.com/Simarglok/arcanum-steamwoven/issues/2000)), measured by one more scene of the `relight` stage (`ARC_RELIGHT_SCENE=cycle`,
`TerrainViewerRelightCycleSmoke.cs`, reproduce below) on the main map at the Crash Site at the night hour 22 (hero `human-male`), in the shipped look: the night-weighted mode at strength 1.6.
Opt-in as before: nothing changes with the scene off.

**1. Walk cycle (AC1).** The hero's own Walk frames (ten frames in each of the eight facings, 80 frames, all different) were drawn one after the other through the production draw path with the hero standing in place
(the pose is forced, so the camera and the lights are still and only the art frame changes), once Legacy and once relit. The hero carries no torch in this part and two lamps stand beside it
(a warm one on its right, a cool one on its left, lit in the relit list only): see the finding below for why.

| Measure | Result |
| --- | --- |
| Lab `shimmer` over the 80 raw frames (the walk frames are cut to their own sizes, so the smoke puts each on a common canvas by its offset from the anchor first), `critter` preset (blur 1), looped, fixed light | Lambert-term change over luminance change: **mean 1.77, largest 2.21** (per facing 1.51 to 1.95); **mean normal change 12.2° between consecutive frames** (8.5° to 14.1°) |
| The same with the shader's own settings (`--blur 0`, the shader has no blur) | ratio mean 2.31, largest 2.84, mean normal change 16.6° |
| In the render, on the hero's own pixels (where a frame differs from the picture without the hero) | the relit term is 0.034 of full luminance on average (0.027 to 0.039 per facing); it changes by 0.012 to 0.019 per pixel from one frame to the next against 0.017 to 0.027 for the Legacy picture itself: **ratio 0.69 (0.63 to 0.71 per facing)**, largest single-pixel step 0.20 (0.15 to 0.20) |

Reading: the derived normals do change from frame to frame (about 12° on average, 16° without the blur), because the pose really changes (legs, cloth and the silhouette move; a normal is a function of
the 3×3 neighbourhood, so an unchanged neighbourhood cannot move). Compared with the sprite's own change the Lambert term moves 1.8 to 2.3 times as much per texel under one fixed light, which
is why the lab's number is above one; in the picture the relit term is multiplied by the texel, the light's falloff and the strength, and there it moves 0.69 times as much as the Legacy picture does, with
no pixel jumping by more than 0.2. So the relit look follows the animation and adds no flicker beyond it; the critter preset's blur calms the normal's change by about a quarter, and the shader has none
(a possible small follow-up if the owner sees the shimmer by eye; no change was made). Limits: one hero body, one lamp arrangement, per-pixel luminance steps rather than a viewing test; the composite
posted in the project thread shows both rows for two facings (Legacy, relit, and the relit term, labels in English).

**Finding (issue [#2298](https://github.com/Simarglok/arcanum-steamwoven/issues/2298)).** In the first version of the harness the hero wore its torch, as in the earlier smokes, and the relit term on the hero's own pixels was
exactly zero (the strip of relit minus Legacy was black on the hero and bright on the ground), while an NPC outside the pool took the full term. The likely cause is the night weighting, which reads the item's own
Legacy ambient, and a body's ambient is the lattice colour at its anchor, which includes the torch it carries. Not confirmed here; the harness avoids it with external lamps. **Confirmed and fixed by ARC-RENDER-2298:** with the relight smoke (the Crash Site cave at night, the night-weighted look at the look's strength, the same two lamps lit in the relit list only, the hero's pixels being those that differ from the picture with the hero hidden) the term on the hero's 6008 pixels was nonzero on 0 of them with the torch worn and on 5120 (mean 0.0185 of full luminance) without it; the cause is as suspected. The weight now reads the item's Legacy ambient without the carried lights (the relit list adds those itself), and the same capture gives 4969 lit pixels (mean 0.0177) with the torch and 5120 (0.0185) without; the Legacy captures are byte-identical before and after. A companion body drawn from another map's plan keeps the weight it had (the moving-body path does not split out its carried light).

**2. Frame times, with a stand-in for the Deck (AC2).** There is no Steam Deck at hand, so this is the stand-in the issue allows, with its limits stated. Machine: the owner's Mac (Apple M4 Pro, 20 GPU cores, Godot 4.7.2 .NET, OpenGL compatibility renderer), vsync off, exactly 1, 5 and 20
lights (the smoke lights only its own lights, ringed round the hero, no carried or map light), Legacy and relit, at the window sizes 1280×800 (1.02 Mpx), 1920×1200 (2.3 Mpx) and 2560×1314 (3.36 Mpx; the screen limits the height); the view grows with the window, so a larger
window is more pixels and more drawn world. Each number is the median of three rounds of 120 frames; "frame" is the smoke's own frame (as the first experiment timed), "bare" waits for the next frame only. The compatibility renderer has no GPU timer, so GPU time itself is not read.

| Window | Legacy frame / bare | Relit 1 light | Relit 5 lights | Relit 20 lights |
| --- | --- | --- | --- | --- |
| 1280×800 | 4.69 / 2.36 ms | 4.71 / 2.62 | 4.79 / 2.69 | 5.97 / 3.28 |
| 1920×1200 | 5.56 / 3.35 ms | 5.56 / 3.38 | 6.00 / 3.23 | 6.55 / 3.73 |
| 2560×1314 | 5.69 / 3.83 ms | 5.97 / 3.78 | 6.07 / 3.88 | 7.03 / 4.18 |

Across three runs of the final code and the three windows (nine cases each) the relit look adds, to the smoke's frame: **1 light: about 0 (−0.3 to +0.3 ms); 5 lights: 0.0 to 1.1 ms (median 0.4); 20 lights: 0.8 to 1.7 ms (median 1.1)**. Run-to-run noise
is about half a millisecond, as before. The cost does not grow with the window size in these numbers (it stays at about a millisecond for 20 lights from 1.0 to 3.4 Mpx): on this GPU the shader loop is small against the frame's CPU side,
so the Mac cannot resolve the pixel-proportional cost the Deck will have.

*Estimate for the Deck, not a measurement.* The Deck's GPU (RDNA 2, about 1.6 TFLOPS; a TFLOPS ratio between an Apple tile-based GPU and RDNA 2 is only indicative) is roughly five to six times weaker than this Mac's (about 9 TFLOPS) and its memory bandwidth about a third. If the whole millisecond for 20 lights
were GPU time, a Deck would take about 6 ms for it at 1280×800 (up to about 10 ms at the worst case seen), 2 to 3 ms for 5 lights and next to nothing for 1. The night cities hold 6 to 18 lights in view (the section above),
so a dense city could cost a Deck most of a 16.7 ms frame's spare time. That is a reason to run the scene on the device, not a result: the run is the human gate left open as issue [#2297](https://github.com/Simarglok/arcanum-steamwoven/issues/2297).

**3. Godot's own lights, same frames and lights (AC3).** The smoke builds a small Godot-native scene beside the viewer's draw (the viewer's canvas hidden while it is timed): the Legacy day frame of the viewer as one full-window
`CanvasTexture` (diffuse: the capture, normal: the lab generator's map of it, `ground` preset) under a `CanvasModulate` of the night/day ratio of a dark patch, up to 24 of the visible sprite frames (twelve in the runs) as `CanvasTexture`s with their own
generated normals (`object` and `critter` presets), and `PointLight2D` nodes at the very lights the shader loop used (same centre, radius, colour; a quadratic falloff texture like the shader's; light height 48; no shadows). Timed with 0, 1, 5 and 20 lights at the same windows.
Median rounds as above; added to the native scene's own frame:

| Added by | 1 light | 5 lights | 20 lights |
| --- | --- | --- | --- |
| Shader loop (relit look), frame | about 0 (−0.3 to +0.3) | 0.0 to 1.1 (median 0.4) | 0.8 to 1.7 (median 1.1) |
| `PointLight2D`, frame | −0.1 to +0.7 (median 0.2) | −0.1 to +0.9 (median 0.2) | −0.1 to +1.3 (median 1.0) |

The two are the same order of magnitude on this machine (about a millisecond for 20 lights), with noise as large as the difference, so **the departure from the handoff (a shader loop in the existing item shader instead of
`PointLight2D`) cost nothing measurable here**; the shader loop is not slower than the engine's own lights for this item count. Two honest limits: the native scene has one full-window item and twelve sprites against the viewer's hundreds
of drawn items, and Godot lights each lit item once per light in range, so its cost grows with items times lights while the shader loop's grows with covered pixels times lights (the lights are global uniforms): in a dense scene the native path
would be the heavier (by reasoning, not measured). And the native scene cannot keep the Legacy look: a lit sprite is multiplied by the engine's ambient, the Legacy lattice (per-pixel light masks) has no place in it, so the native picture in the composite is built from the *day* frame under a night ambient and shows a different look (the pools of the real lights replace the lattice; the same eight lights),
not Legacy plus a term. The normal maps for it were generated on the CPU by the lab's generator (0.3 to 1.0 s for a window-sized frame in a Debug build), the stored-map cost the first experiment only estimated.

**What changed in code** (none of it is on the player's path): the scene file above; `AreaRelight.UploadedLights` and `AreaRelight.OnlyExtra` (the smoke's exact 1, 5 and 20 lights; false everywhere else); the viewer project compiles the lab's `NormalMapGenerator.cs` by link (one source for the normal maps);
the lab's `shimmer` takes a directory of animation directories and `--loop` (`WalkCycleShimmer`, with tests).

## Verdict

**Owner decision 2026-11-15 ([#2001](https://github.com/Simarglok/arcanum-steamwoven/issues/2001), recorded in [decisions](../architecture/decisions.md), #2173): go for the night-weighted look under the HD option (HD = interface + fonts + relit lighting; no HD world sprites); no-go for daylight and for de-light.** The recommendation below is kept as the experiment's own wording.

**Conditional go, for a bounded "night and indoor" relit option; no-go for daylight and for de-light as built.** The experiment
shows the idea works on the real data and the real draw path with a small, switchable hook, Legacy untouched, and a cost within the noise to about a millisecond with the real lights on a desktop-class Mac. It does not show that the result is clearly better to the eye: that is the owner's call
from the composites, and a stronger statement would need the Steam Deck frame times and a real walk-cycle flicker check, both open.

The owner said go (night-weighted, under the HD option). Full coverage is these pieces, none large:

1. **Category tuning** (open, optional polish: [#2208](https://github.com/Simarglok/arcanum-steamwoven/issues/2208)): ground, wall, scenery, item, critter and roof gains and blur, per light type (torch, lantern,
   spell); tuning rounds with the owner's eye. The shader already knows the kind of each item.
2. **Light sources** (done, [#2207](https://github.com/Simarglok/arcanum-steamwoven/issues/2207), ARC-RENDER-2207): the relit list takes the map's sector and object lights of the shown hour (the night or day form of the
   scenery's lights, ARC-RENDER-030) where their animation poses stand (ARC-RENDER-031), and the carried lights (hero, critters, recalculated lights, and lit eye candy, ARC-MAGIC-1229); the radius cap and the view culling are
   decided: [relit look](../rendering/area-lighting.md#relit-look-light-sources-cap-and-culling-arc-render-2207).
3. **HD frames**: **dropped** (owner decision 2026-11-15, #2063, #2173): HD sprites of the world are not made and are out of the plan until the owner reopens it, so world frames stay 1x under the HD option; the N× path is dormant, built (gain and bevel stride) and covered by the lab twin only.
4. **Optional stored normals**: **deferred** (it needs HD world frames, which are out of the plan; owner decision 2026-11-15): only if per-pixel derivation proves too noisy or too slow on the Deck; the lab generator is the batch
   tool, about 168 ms per megapixel in Debug.
5. **Profile and settings**: done in another form: no separate profile; the relit look is a part of the HD option with no setting of its own (ARC-RENDER-2060, ARC-RENDER-2076, [#2174](https://github.com/Simarglok/arcanum-steamwoven/issues/2174) for the single launcher option).
6. **Deck run and flicker run**: the flicker run is done (a real animated walk cycle, no flicker beyond the animation's own change) and so is the Godot-native comparison (the same order of cost), both with a Mac stand-in for the frame times
   ([ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000)); the frame times at 1280×800 on Steam Deck-class hardware with 1, 5 and 20 lights are the one piece left, with an estimate in place
   (about 2 to 3 ms for 5 lights and about 6 ms for 20, to be checked: [#2297](https://github.com/Simarglok/arcanum-steamwoven/issues/2297)).

What this experiment did **not** do: use Godot `PointLight2D` / `CanvasTexture` in the game (departure 1 above; ARC-RES-2000 compared the two on one scene and found the same order of cost), test shadows from occluders (out of scope), touch the UI (it is unlit: the
hook lives only in the world item shader), or compare with Laigter (the free generator was not needed: the one-line Sobel in the
shader was good enough to judge the idea).

The owner's go / no-go ([#2001](https://github.com/Simarglok/arcanum-steamwoven/issues/2001)) is settled (2026-11-15): see the owner-decision paragraph at the top of the Verdict.

## Taken into the game (ARC-RENDER-2060)

The owner's decision of 2026-11-14 ("composition looks very promising, add it to the game under the HD graphics option") made the night-weighted variant at strength 1.6 a player setting: the
relit look as a part of the HD graphics option (on exactly with HD graphics, no setting of its own after ARC-RENDER-2076; [relit look](../rendering/area-lighting.md#relit-look-arc-render-2060)). De-light stays rejected. Frame time of the
shipped variant on the Mac, 1280x800: cave 3.4 to 4.7 ms, Tarant at night 5.9 to 8.7 ms, Ashbury 5.6 to 7.8 ms; the Steam Deck is still unmeasured ([#2297](https://github.com/Simarglok/arcanum-steamwoven/issues/2297); stand-in and estimate: [ARC-RES-2000](#walk-cycle-a-steam-deck-stand-in-and-godots-own-lights-arc-res-2000)).

## Reproduce

From the repository root, through the [launch queue](../workflow/toolchain.md#launch-queue-arc-dev-033):

    ./arcanum smoke map "crash site cave" --stages relight --env ARC_RELIGHT_CAPTURE=/absolute/dir
    ./arcanum smoke map Arcanum1-024-fixed --stages relight --env ARC_RELIGHT_CAPTURE=/absolute/dir

The night cities (ARC-RES-2007; PNGs named by `ARC_RELIGHT_CITY`):

    ./arcanum smoke map Arcanum1-024-fixed --stages relight --hero human-male --sector 969,1026 --env ARC_RELIGHT_SCENE=city --env ARC_RELIGHT_CITY=tarant --env ARC_RELIGHT_CAPTURE=/absolute/dir
    ./arcanum smoke map Arcanum1-024-fixed --stages relight --hero human-male --sector 504,975 --env ARC_RELIGHT_SCENE=city --env ARC_RELIGHT_CITY=ashbury --env ARC_RELIGHT_CAPTURE=/absolute/dir

The walk cycle, the frame times and the Godot-native comparison (ARC-RES-2000; `ARC_RELIGHT_SIZES` is the window sizes to measure, default `1280x800,1920x1200,2560x1600`; the PNGs, the hero's frames under `cycle-frames/f0..f7` and the scene's captures under `cycle/`, are derived from the player's art: outside git):

    ./arcanum smoke map Arcanum1-024-fixed --stages relight --hero human-male --env ARC_RELIGHT_SCENE=cycle --env ARC_RELIGHT_CAPTURE=/absolute/dir
    ./arcanum run --kind dotnet -- $HOME/.local/share/dotnet/dotnet experiments/relight-lab/bin/Debug/net8.0/Arcanum.Experiments.RelightLab.dll shimmer /absolute/dir/cycle-frames --kind critter --loop

The smoke logs `ARC-RES-2000 relight-cycle walk` (the render's numbers per facing) and `ARC-RES-2000 relight-cycle timing` (per window size: Legacy and relit with exactly 1, 5, 20 lights, and the native scene with 0, 1, 5, 20); the lab prints the shimmer of the frames. A run takes about 90 s.

Full-frame night shots for the owner (ARC-RES-2050): add `--env ARC_RELIGHT_SHOTS=1` (and optionally `--env ARC_RELIGHT_HOURS=20,23,3`) to the city commands. The stage then skips the walk and
the timing and captures up to three spots at least 700 px apart (each with at least four lit lights within 350 px) at every hour, in Legacy, night 1.0 and night 1.6
(`<city>-shot-<spot>-h<hour>-<variant>.png`, 1280×800). `ARC_RELIGHT_AT=x,y;x,y` (world tiles) stands near those tiles instead (the nearest standable cell within 2, 5 or 9 cells, else the window's start; spots `at1`, `at2`, ...; the window must still hold lit lights, except under `ARC_RELIGHT_WISP_SHOTS=1`; the wisps were found in the window centred on sector 505,975 with `32340,62389;32379,62519`). ARC-RENDER-2064: `--env ARC_RELIGHT_WISP_SHOTS=1` (with the shots mode and `ARC_RELIGHT_AT`) captures per spot Legacy with the wisp glow on and off, the relit look without and with the glow, and logs the pixel differences (Legacy on/off must be 0), the wisp lights in reach and the lights uploaded; it needs no lit light in the window (the amber and cyan wisps were captured in the window centred on sector 1694,1317 with `108500,84300;108515,84320`, the violet ones as above). In the first runs the ambient colour changed only slightly between the night hours 20, 23 and 3, so the hours differ less than the spots.

The PNGs under `ARC_RELIGHT_CAPTURE` are derived from the player's art: keep them outside git. The lab:
`experiments/relight-lab/README.md`.

