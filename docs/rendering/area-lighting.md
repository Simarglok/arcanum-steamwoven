# Crash Site day/night ambient and cave lighting (ARC-RENDER-010, ARC-RENDER-012)

Status: **implemented bounded component** for the `crash-site-area` diagnostic
viewer. It applies the hourly ambient scheme of the loaded area to drawn terrain,
bodies and the controlled actor by their indoor/outdoor channel (ART types without
an ambient channel stay unlit), driven by the
[WORLD-004 clock](../world/game-clock-runtime.md). It covers the ambient part of
[FR-LIGHT-003..007](../requirements/lighting-time.md); it is not a light graph,
scheduler, save system or original visual-parity claim.

Source analysis follows the [RES-018 contract](../world/time-lighting-contract.md)
(a source reading of the original's lighting and TIG functions, evidence level F0
unless the verification ledger lists them). The code is independently written. No original colour values, rule text or pixels are committed;
tests and logs use counts, scheme ids and SHA-256 digests only.

## What it does

| Requirement | Implemented subset | Not implemented / NOT_VERIFIED |
| --- | --- | --- |
| FR-LIGHT-003 hourly scheme | `LightSchemeParser` reads a scheme MES: 24 hour keys, six integers 0..255 per row in **outdoor RGB, indoor RGB** order, scheme id → `rules/<name>.mes` through `rules/lighting schemes.mes`. Every lighting MES file is read with the original grammar (`MesFile`, ARC-DATA-027): text outside braces is ignored (also a `//` comment, so a brace inside one starts a field), while a lone CR, a text over 1,999 bytes, a brace inside a field and a duplicate number are refused (`MesGrammar` / `LimitExceeded`; before, a lone CR counted as white space and texts up to 4,096 bytes were read). | The pinned Crash Site profiles still read the module archive only; the whole-map viewer reads through the content stack ([sources](#sources-and-load-phases-arc-data-027)). |
| FR-LIGHT-004 empty hours | Strict project policy from RES-018: all 24 keys present, hours 0 and 23 explicit (the shipped default scheme file states the same rule), each interior empty run filled per channel as `left + distance * trunc((right-left)/span)` from its own bracketing anchors. Missing key, edge gap, wrong channel count or range fail with a typed `LightingFailure`. | the source-read (F0) mutable running-colour behaviour for several separated gaps and reload is deliberately not reproduced. |
| FR-LIGHT-005 hourly application | `AmbientLightingState.Apply(hour, sector)` selects the pair for the whole game hour; the selection and its revision change only when the hour or the resolved scheme changes. The viewer re-applies after every clock change, so a resumed/jumped clock shows the current hour at once. | No relative 3,600,000 ms callback scheduler; no minute blending (none exists in the source either). |
| FR-LIGHT-006 indoor/outdoor surfaces | Per drawn ART type, following the original's palette callback (source reading, F0): tile/facade take **indoor** when their type bit is 0 (tile bit 8, facade bit 25) and **outdoor** otherwise; wall, critter, portal, scenery, container, roof, monster and unique NPC take outdoor; items only in ground disposition; interface, misc, light and eye-candy are unlit. The controlled actor is a critter (outdoor). The shader multiplies each item by its channel colour / 255. | Roof/roof-fade modes (no roofs in this core), local light blending, TIG palette/BPP rounding parity. |
| FR-LIGHT-007 map default and sector scheme | Map default from `maps/<dir>/mapinfo.txt` `LightScheme` (one positive value, no guessed fallback) via `rules/maplist.mes`; each **core** sector's scheme word at metadata +8, re-read from the module and bound to the snapshot's entry identity and payload SHA-256. Word 0 is the map-default sentinel. The active sector is the one holding the actor's committed cell; a change re-selects at the same hour. | All nine original core sectors use the sentinel, so a visible sector switch is proven synthetically only. Context sectors, other maps and map transitions are out of scope. |

Clock (ARC-WORLD-010): the driver reads and advances the clock of the viewer's one
[game session](../world/game-session-runtime.md), never a clock of its own. The session
is created per load generation whenever area lighting is loaded (with or without the
cave), with the clock profile of the module's `rules/gameinit.mes` (year key 4, hour
key 5, payload digest bound into the profile; `AreaAmbientDriver.ClockProfile`). Each
frame `AreaAmbientDriver.Tick` admits host time through
[`GameTimeFlow`](../world/game-clock-runtime.md#host-time-flow-arc-world-010) (8× the
admitted interval, so one game hour per 7.5 real minutes) and holds a `Menu` pause
while the diagnostic palette or a dialog is open, releasing only a Menu pause it holds
(a Menu pause already on the session when the driver binds, as after a restore, counts
as held; User or TurnBased pauses are left alone); resume has no catch-up. Time keeps
flowing while the window is unfocused or minimised: only a modal window pauses it. **Game time +1 hour** is an explicit wait (`ExplicitWait`) that also
works during that pause. After every clock change that crosses an hour, and after a
sector change, the selection is re-applied and recorded in the session
(`RecordLighting`), so a session capture carries the time and the scheme selected for
it (FR-LIGHT-012). The record is the selection, also in the cave (drawn unlit) and with
the diagnostic lighting toggle off; in the cave the exterior sector last selected stays
for the flow and the wait alike. Automated runs (self-test, native smoke) get no live flow; they drive
the same tick with literal host times. The F7/F8 quick-save slot
([ARC-WORLD-011](terrain-viewer.md#quick-save-and-quick-load-arc-world-011)) saves and
loads this clock and the applied scheme; on a quick load the driver rebinds to the
loaded session. Sleep/rest UI, turn-based combat, a scheduler and a save list are not
implemented; the 8× rate is a source reading not checked in the executable (F0), NOT_VERIFIED.

On the selected original module (opt-in real-data test): map 5000, map default
scheme 1, all nine core sector words 0, scheme 1 payload SHA-256
`ede40699a2e250d37b3748d255f7535561633c0bd8ec3de6229a157bedf07a0d` with 9
explicit hours and distinct indoor/outdoor channels, startup 1885 / 15:00
(`e098286005ba4c4a16701cd6cbcf74eca2bc8937d9f45c7d9ef6331000c7d670`). These are
the same identities as the RES-018/RES-020 research receipts.

## API

Namespace `Arcanum.Data.Lighting` (Godot-free):

- `AmbientColor`, `AmbientPair(Outdoor, Indoor)`, `LightSchemeTable` (24 detached read-only hours, explicit-hour flags, payload digest).
- `LightSchemeParser.TryParse(id, payload, out table)` → `LightingFailure`.
- `AreaLightingReader.Read(stack, snapshot)` (ARC-DATA-027) reads every file through the content stack ([sources](#sources-and-load-phases-arc-data-027)); its module archive must be the snapshot's. `AreaLightingReader.Read(module, snapshot)` is the pinned profiles' module-only read. Both → `AreaLightingReadResult(Failure, Source)`. ARC-DATA-035: through the stack, a window's generated core sector takes its scheme word from its terrain template (named by `AreaSnapshot.Window`, bound to the sector's payload digest; the module-only read refuses it, as pinned profiles generate none). Only a strictly published snapshot is accepted, and every core sector entry must live under the map directory whose `mapinfo.txt` supplies the default. Caps: 1 MiB per rule payload, 8,192 MES records, 256 core sectors (a whole interior map, ARC-RENDER-017; was 64), 32 schemes. For the module-only read the caller passes the module archive the snapshot was built from and re-verifies the module file afterwards (the viewer does).
- `AreaLightingSource`: module digest, map default, raw core sector words, referenced schemes, startup declaration; validating public constructor for synthetic inputs; `TryResolveScheme(sector)`.
- `AmbientLightingState.Apply(hour, sector, out changed)`.

Viewer Core (`tools/terrain-viewer/Core/AreaAmbient.cs`): `AreaAmbientPolicy`
(channel per ART id and per plan command), `AreaLightingPlan` (attached to
`AreaLoadResult.Lighting`), and `AreaAmbientDriver` (clock owner + state; `AdvanceHours`,
`Apply(sector)`, `Enabled`). Structural fixtures without lighting draw unlit.

## Sources and load phases (ARC-DATA-027)

The universal readers take each file from the sources the original reads it from, which
depends on when it reads it (`ContentLoadPhase`, checked in the 1.0.7.4 executable,
[ledger "Load phases"](../research/ledger/data.md#load-phases-arc-data-027)):

| File | Read by the original | Sources |
| --- | --- | --- |
| `rules/gameinit.mes` | at module load (module init, 0x4B9C30) | stack winner |
| `rules/maplist.mes` | at module load (map list load, 0x4125C0) | stack winner |
| `rules/lighting schemes.mes` | at module load (light scheme module load, 0x4A7C90) | stack winner |
| `rules/<scheme>.mes` | when the scheme is set (light scheme set, 0x4A7DD0) | stack winner |
| `maps/<dir>/mapinfo.txt` | when the map opens (map extension load, 0x4115D0) | stack winner |
| core sector files | with the map | the stack's winner of `maps/<dir>/<id>.sec`, which must be the module-archive member the snapshot read: none, or a member of another name, is `MapDirectoryMismatch` (as the module-only read), an override from another source or a changed member `SectorIdentityMismatch` |
| `art/light/light.mes` | at game start (light name init 0x4ED1E0, from Name's init) | first base source: a module copy is never read |
| `art/light/<name>.art` | when the light is drawn | stack winner |

The stack read requires the stack's module archive to be the snapshot's: its length is checked against the
snapshot's module identity and each sector is bound by member identity and payload digest, while the rule files are the
stack's own. The map viewer ([map mode](area-map-mode.md)) builds both from one stack and uses these readers; the pinned Crash Site
exterior and cave keep the module-archive and `arcanum2.dat` reads. On the installed
original both give the same result on all 80 interiors (opt-in
`LightingStackRealDataTests`): 76 lit with stationary lights, 2 not in the map list
(`MapNotCatalogued`), 2 with sectors older than the metadata section
(`SectorMetadataAbsent`). Since ARC-DATA-039 the two interiors that were unlit for an animated light ART and for
the no-shadow bit 0x40 are lit too (an animated ART draws its frame 0 until its animation is implemented, V7c of
[lights on all maps](../research/lights-on-all-maps.md)). Largest values against the caps: 79/256 core sectors, 3/32 schemes,
207/512 stationary lights, 9/32 light frames (ARC-DATA-029 raised the lights from 256 for 1.5 times
headroom, ARC-DATA-039 the frames from 16 for the object lights; [limits census](../world/limits-census.md)).

### Object lights (ARC-DATA-039)

`ObjectLightReader` (Godot-free, `Arcanum.Data.Lighting`) reads the object lights of a strict snapshot as the original
builds them at sector load ([ledger LT1, LT4](../research/ledger/world.md#lights-on-all-maps-arc-res-069)):

- For every core object with a location that is not in an inventory: the primary light of slots 12–14 (flags, ART,
  colour packed r << 16 | g << 8 | b) when its ART is not −1, and each overlay slot 0..3 of the arrays 15–17 whose
  ART is not −1, at the object's tile and offsets.
- The saved flag words may carry the original's runtime bits (animating 0x4, changed 0x80, the primary and
  overlay-slot markers), which are dropped. 0x20 (no animation) and 0x40 (no shadow) are kept as data.
- Each light records whether its object is switched off, whether it is nocturnal scenery and whether the tile under it
  is an outdoor tile (the ART flag of 0x5038C0: bit 8 of a tile, bit 25 of a facade). `BuiltAtLoad(light, hour)` is the
  original's load rule: nocturnal scenery on an outdoor tile only at night (hours 18..5). `FlagsAtLoad(light, hour)`
  gives a built light's flags: a nocturnal light is switched on with its object, which clears its saved OFF bit (LT13).
- The light ART frames come from the same table and phases as the stationary lights. An object light's frame is its
  saved animation frame.
- Fail-closed: an unknown flag bit, an ART that is not a light (type 9, rotation and low bits 0), a frame past the ART's
  last, an overlay array element outside slots 0..3 or of other than one word, and more than 8,192 lights per snapshot
  (project policy) are typed failures. All light ART frames of one read are capped at 64 MiB of RGBA.

On the installed original (opt-in `ObjectLightRealDataTests`), all 80 interiors and two main-map windows (the Crash Site,
Shrouded Hills) read without a failure: 6,809 object lights, at most 5,083 in one map (k'na tha), 109 of them nocturnal. The cap was checked on windows of
radius 1 only. Map mode draws them since ARC-RENDER-030 (below).

### Object lights in map mode (ARC-RENDER-030)

Map mode (whole maps and main-map windows) draws the object lights with the stationary lights; the pinned Crash Site
exterior and cave keep their stationary lights only.

- **At load** the viewer reads the object lights and builds two lightings: the night form (every object light) and,
  when a map has nocturnal scenery on outdoor tiles, the day form without it
  (`AreaStationaryLightSource.WithObjectLights`, ledger LT4/LT5). A built nocturnal light is switched on with its
  object (its saved OFF cleared, `FlagsAtLoad`); every other object light keeps its saved flags, so a saved OFF stays off.
- **Each hour** `AreaLocalLighting.AtHour` picks the form (day = hours 6..17), so the nocturnal lights go on at 18:00
  and off at 6:00 with the lattice the hour change rebuilds. The original's refresh slips A1 and A2 are not emulated:
  every nocturnal light switches (owner decision 2026-10-21, [rule modes](../world/rule-modes.md)). Nor is the refresh's skip of scenery
  with scenery flag 0x400 (LT5): such scenery switches with the hour too.
- **Drawing** is the stationary lights' composition: OFF gives nothing, DARK (the shade under trees and buildings, most
  object lights) subtracts. An object light shows its saved ART frame. Object lights that cannot be read, or whose
  frames or lattice exceed the caps, leave the map lit by its stationary lights alone (`objects=` on the load line,
  `AreaLightingPlan.ObjectLightsFailure`).
- **Scale.** Point samples look only at the lights whose rectangle meets a 256-pixel bucket, and each light adds into
  the lattice nodes its rectangle covers (the same sums as node by node, tested); the lattice cap is 2^22 nodes.
  K'na tha (5,083 object lights) builds its lighting in about 1 s at load; the Crash Site window holds 42. The shade
  lights are outdoor-tinted, so every hour change rebuilds the lattice and the body colours: about 100 ms on k'na tha,
  16 ms on caladon-castle main floor (`ViewMs` in the smoke), once per game hour.
- **Native smoke** (map stage): `ARC-RENDER-030 native-smoke object lights PASS` observes terrain pixels near object
  lights that are on and meet no stationary light (k'na tha, caladon-castle main floor, elven ruins and the Crash Site
  window: 3 of 3 each); it skips a map without such a light and fails when its object lights could not be read.

Not drawn: moving and carried lights (V7d), shadows.

### Animated lights (ARC-RENDER-031)

A light whose ART has more than one frame animates in map mode, stationary and object lights alike, unless it carries
0x20 or was saved while animating (0x4; the reader turns a saved 0x4 into 0x20, ledger LT6, LT7).

- **Pose.** `StationaryLightField.PosesAt(ms)` gives each animated light's pose after `ms` of animation time: one step
  every ⌊1000 / frames per second⌋ ms, to the next frame (wrapping) and by that frame's offset step, from the light's
  saved frame and offsets. `Point` and `Lattice` take the poses, so a posed light is drawn and sampled where it stands
  now, in source order with the others.
- **Time.** The viewer's light animation time starts at 0 whenever a map's lighting binds (a load, a transfer, a quick
  load), as the original starts a light's animation when it enters its sector list, and runs with real time unless the
  session clock is paused (a modal window); the original's animation clock rate is F0. A nocturnal light switched on at
  18:00 takes its phase from that time, where the original would start it at its insertion (F0). Each frame the viewer compares the poses
  (`AreaLocalLighting.AnimationKey`) and pushes a new view only when one changed; a map without animated lights does no
  work. Automated runs drive the time themselves.
- **Cost.** A changed pose rebuilds the whole lattice and every body colour of the map, so it is cheap only on maps with
  few lights; the installed module has one animated light, on void (228 lights, 10 to 15 ms per view). An ART whose
  frame steps do not sum to zero would walk the light away over time; the lattice cap is checked at load for frame 0.
- **Native smoke** (map stage): `ARC-RENDER-031 native-smoke light animation PASS` sets three animation times one
  step apart on void: frames 0, 1, 2 at 5 per second, the lattice changes at each step, and at the second and third a
  terrain pixel inside the posed light, where the pose changes the colour, reads back as the posed lattice predicts. It
  skips a map without an animated light that is on.

## Viewer controls

Following the [ARC-INPUT-001 route](../input/diagnostic-palette.md), diagnostics
live in the diagnostic palette (Ctrl+Shift+F9 / gamepad Start), never on original
game keys. In `crash-site-area` the palette has **Game time +1 hour** (a one-hour wait) and
**Ambient lighting on/off** (off shows unlit source pixels) plus a lighting status
line. The HUD shows the game time, DAY/NIGHT (`[06:00,18:00)`), whether time flows
(`time flows 8x`) or is paused (`PAUSED (dialog/palette/inventory open)`), the scheme id and
whether it came from the map default. Without loaded lighting (other WORLD modes)
both actions report "unavailable". No launch argument or key binding was added.

## Verification

- `tests/Arcanum.Data.Tests/Lighting/LightingTests.cs`: literal grammar, gap,
  edge, malformed, map-default, sentinel/override resolution, discrete hourly
  selection and reader fail-closed cases over a synthetic module; opt-in
  `RealData` test with the identities above (`ARC_AREA_*` inputs).
- `tests/Arcanum.TerrainViewer.Tests/AreaAmbientTests.cs`: ART-type channels,
  plan coverage, session-clock bootstrap/wait/day roll-over and sector change, 8×
  flow to the hour boundary with the scheme switch, Menu pause without catch-up and
  wait during it, a foreign clock profile refused, and capture → restore of time and
  applied scheme.
- Godot self-test `AssertAreaLightingAsync`: the production compositor with
  literal hand-computed pixels for outdoor, indoor, unlit, neutral and cleared
  ambient, and rejection of a command without a channel.
- `--native-smoke` in `crash-site-area`: the existing source-pixel gates run with
  lighting **off**, then a lighting phase checks lit terrain (candidates spread
  across the chart) and body pixels
  against `source × ambient / 255` (±2) at 15:00, 21:00, 06:00 and 12:00 on the
  original data. A lit sample counts only where the tint changes the source by more
  than the tolerance; an hour whose channel is near white (every component ≥ 240)
  and shows no such pixel is logged as near-neutral for that group, any darker hour
  must be observed, and the phase fails unless both terrain and bodies were
  observed tinted at least once. All sampled original terrain may be outdoor, so
  the indoor channel is proven by the synthetic self-test only.
  `ARC_AREA_LIGHTING_CAPTURE=<dir>` additionally saves one
  screenshot per checked hour (local evidence only, never committed).
- `--native-smoke` clock phase (ARC-WORLD-010), after the transition phase: the
  production tick at literal host times flows to the next hour boundary in exactly
  ⌈remaining/2,000⌉ frames of 250 ms and the recorded scheme follows; the open
  palette and an open dialog each hold the clock for 400 frames and resume with no
  catch-up; the palette's +1 hour waits during the pause; a capture is encoded,
  decoded and restored into a separate owner with the same time and applied scheme.
  It prints `ARC-WORLD-010 native-smoke clock PASS`.

Original night appearance, TIG multiply rounding, palette/BPP behaviour and
gameplay effects of darkness remain NOT_VERIFIED. Stationary sector lights are
covered below (ARC-RENDER-012); object/carried lights (FR-LIGHT-009), luminance
checks (FR-LIGHT-011) and a save list (FR-LIFECYCLE) are separate work; the
session snapshot carries the clock and the applied scheme, saved by the ARC-WORLD-011 quick-save slot.

## Cave ambient and stationary sector lights (ARC-RENDER-012)

Status: **implemented bounded component** for the same viewer. Every loaded map
(the exterior and the [ARC-WORLD-009 cave](terrain-viewer.md#crash-site-cave-and-return-arc-world-009))
now carries its own ambient source and its stationary sector lights, so the cave is
no longer drawn unlit. It covers the stationary part of FR-LIGHT-008, the
surface rule of FR-LIGHT-006 for lit bodies and walls, and the time behaviour of
stationary lights in FR-LIGHT-010. Source analysis follows the same RES-018
lineage (a source reading, F0 unless the ledger lists it, of the light read
(0x4DDD20), light rectangle (0x4DE290, 0x4DE390), light render (0x4DE900) and
light helpers (0x4D89E0, 0x4DA360, 0x4DC210), the sector light list load
(0x4F72C0, ledger Wilderness A1), the isometric tile draw and the TIG colour and
ART code); independently written.

| Requirement | Implemented subset | Not implemented / NOT_VERIFIED |
| --- | --- | --- |
| FR-LIGHT-007 per map | The cave reads its own map default (`maps/<cave dir>/mapinfo.txt`) and sector word through the ARC-RENDER-010 reader. A transfer, or a quick load onto the other map, rebinds the ambient driver to that map's source on the **same** session clock (the source must share the clock profile), so time is shared, the scheme follows the map and the session records the cave's scheme. | No fade; the hour is whatever the clock says on arrival. |
| FR-LIGHT-008 stationary lights | `StationaryLightReader` decodes the 12 raw words the strict snapshot retained per core sector: owner word 0 only (object lights refused), the raw location as the absolute world cell **inside its own sector** (an adopted project policy, NOT_VERIFIED: the decompiled sector-relative rebuild is marked TODO in the source and is not taken literally; a location outside the record's own sector is refused), signed screen offset, flags OFF/DARK/INDOOR/OUTDOOR and NO_SHADOW 0x40 (ARC-DATA-039; other bits refused), a plain light ART id (type 9, frame/rotation/flags 0) and the colour stored **r,b,g**. Tint, palette and padding words are bound through a record digest, not interpreted. The ART number resolves through `art/light/light.mes` in the terrain resource (Resource2); a one-group light ART of up to 32 frames is admitted (ARC-DATA-039; frame 0 is drawn, the others are kept with the ART's rate and per-frame offset steps). Pixels equal to palette index 0's colour (the ART colour key) contribute nothing. | Animating lights (V7c), drawing object/overlay/carried lights (FR-LIGHT-009; read since ARC-DATA-039, drawn from V7b), DIF/save overrides, neighbour-sector emitters of other maps. |
| Composition | `StationaryLightField`: a light covers its frame rectangle anchored like a body (tile + offset + (40,20) − hotspot). A contribution is the frame pixel, multiplied by the tint (`c·t/255`, truncated) unless the tint is white; INDOOR/OUTDOOR take the tint from the hour's pair (INDOOR first); OFF contributes nothing. **Terrain:** lighter and darker sums at the 40×20 screen lattice, each node composed as `max(min(ambient + lighter, 255) − darker, 0)` over the tile's own channel, interpolated bilinearly between the four surrounding nodes in the area shader (the source LERPs each 40×20 tile quadrant between its corner nodes). **Bodies:** the source's object colour, the ambient of the tile class under the body's cell plus the lights at its anchor, added or (DARK) subtracted in source order. **Walls:** the base line sampled at the sprite's two ends from lights on the wall's front side only (by tile coordinate), tile-class ambient for rotations 0/6 and outdoor for 2/4, drawn as a horizontal two-colour gradient (the source's hardware path). The actor is coloured like a body at its current placement. ART channels that ARC-RENDER-010 leaves unlit stay unlit. | TIG integer LERP and rounding (the shader interpolates floats), the software per-column wall array, the decompiled wall base-line shift (followed literally, NOT_VERIFIED), roof modes, shadows, light animation, original visual parity. |
| FR-LIGHT-010 stationary lights and time | Stationary sector lights are not time-gated in the source (the renderer checks only OFF). The lattice and body colours are recomposed at every hour change over the new ambient; INDOOR/OUTDOOR-flagged lights change colour with the hour (synthetic only: the four cave lights have flags 0). | Nocturnal scenery gating and object-light refresh (they belong to object lights, FR-LIGHT-009). |

Rule change for bodies: ARC-RENDER-010 tinted every lit body by its ART channel.
The body colour now uses the **tile class under the body** (the source's object
colour); for bodies on outdoor tiles with outdoor-type ART the two rules agree. On
the original data all 514 placed exterior core objects stand on outdoor-class cells
(all 36,864 exterior core terrain ids are outdoor class), so the exterior is
unchanged; in the cave all 177 placed objects stand on indoor-class cells.

On the original data (opt-in tests and native smoke): exterior map 5000 has **no**
stationary lights in its nine core sectors; cave map 5078 uses map-default scheme
**2** and has **4** stationary lights in sector 2080374816, flags 0. Three use
light ART 5 (`disk5x5`, 450×225, hotspot 225,112) and one ART 2 (`disk3x3`,
233×117, hotspot 115,57), both static; `light.mes` SHA-256
`a6c9f3df3ecdf6b7b6c522138f6c64376e3236c6d2ea4f7eae08aabd1f15606c`. Record 0's
digest equals the RES-018 receipt `11ad9c3e…`. The lit lattice is 28×29 nodes.
No colour values or pixels are committed.

API: `Arcanum.Data.Lighting.StationaryLight`, `StationaryLightFrame`,
`AreaStationaryLightSource` (`Create` for synthetic fixtures),
`StationaryLightReader` (`Read(stack, snapshot)` through the content stack, `Read(artResource, snapshot)` for the pinned profiles, `TryDecode`; caps 512 lights, 16 frames, 1024 px
frame side, ±16384 offset), `StationaryLightField` (`Point`, `Lattice`,
`TerrainPixel` reference, `Compose`); viewer Core `AreaLocalLighting` (tile
channels, body/wall/actor colours, `View`) attached as `AreaLightingPlan.Local`;
`AreaAmbientDriver.Rebind`. The area shader gains a light mode (constant, wall
gradient, terrain lattice); items without local light keep the constant ambient
and send no per-camera uniform.

Verification: `tests/Arcanum.Data.Tests/Lighting/StationaryLightTests.cs`
(literal wires, refusal cases, field arithmetic, reader over a synthetic DAT/ART,
opt-in `RealData` cave test); `tests/Arcanum.TerrainViewer.Tests/AreaLocalLightingTests.cs`
(body/wall/actor rules, view, driver rebind); Godot self-test
`AssertAreaLocalLightingAsync` (literal lattice and gradient pixels through the
production compositor); `--native-smoke` in `crash-site-area` with
`--area-cave-mobile`: after the transfer into the cave, map-default scheme 2 is
applied, terrain near lights and a lit body and a lit wall read back as
`source × composed light / 255` (±2) at pixels where the lights change the colour
beyond the plain ambient, at two hours, with the lattice unchanged between them
(`ARC-RENDER-012 native-smoke cave lighting PASS`).
