# Crash Site day/night ambient lighting (ARC-RENDER-010)

Status: **implemented bounded component** for the `crash-site-area` diagnostic
viewer. It applies the hourly ambient scheme of the loaded area to drawn terrain,
bodies and the controlled actor by their indoor/outdoor channel (ART types without
an ambient channel stay unlit), driven by the
[WORLD-004 clock](../world/game-clock-runtime.md). It covers the ambient part of
[FR-LIGHT-003..007](../requirements/lighting-time.md); it is not a light graph,
scheduler, save system or original visual-parity claim.

Source analysis follows the [RES-018 contract](../world/time-lighting-contract.md)
(CE/TIG at `a7ff41b300ef712f0e7d088183a3d08957110cdd`, Sustainable Use License,
one prior-art lineage). The code is independently written; no CE code was copied
or translated. No original colour values, rule text or pixels are committed;
tests and logs use counts, scheme ids and SHA-256 digests only.

## What it does

| Requirement | Implemented subset | Not implemented / NOT_VERIFIED |
| --- | --- | --- |
| FR-LIGHT-003 hourly scheme | `LightSchemeParser` reads a scheme MES: 24 hour keys, six integers 0..255 per row in **outdoor RGB, indoor RGB** order, scheme id → `rules/<name>.mes` through `rules/lighting schemes.mes`. | Universal MES grammar; DAT/PATCH/loose precedence (module archive only). |
| FR-LIGHT-004 empty hours | Strict project policy from RES-018: all 24 keys present, hours 0 and 23 explicit (the shipped default scheme file states the same rule), each interior empty run filled per channel as `left + distance * trunc((right-left)/span)` from its own bracketing anchors. Missing key, edge gap, wrong channel count or range fail with a typed `LightingFailure`. | CE's mutable running-colour behaviour for several separated gaps and reload is deliberately not reproduced. |
| FR-LIGHT-005 hourly application | `AmbientLightingState.Apply(hour, sector)` selects the pair for the whole game hour; the selection and its revision change only when the hour or the resolved scheme changes. The viewer re-applies after every clock change, so a resumed/jumped clock shows the current hour at once. | No relative 3,600,000 ms callback scheduler; no minute blending (none exists in the source either). |
| FR-LIGHT-006 indoor/outdoor surfaces | Per drawn ART type, following the CE palette callback: tile/facade take **indoor** when their type bit is 0 (tile bit 8, facade bit 25) and **outdoor** otherwise; wall, critter, portal, scenery, container, roof, monster and unique NPC take outdoor; items only in ground disposition; interface, misc, light and eye-candy are unlit. The controlled actor is a critter (outdoor). The shader multiplies each item by its channel colour / 255. | Roof/roof-fade modes (no roofs in this core), local light blending, TIG palette/BPP rounding parity. |
| FR-LIGHT-007 map default and sector scheme | Map default from `maps/<dir>/mapinfo.txt` `LightScheme` (one positive value, no guessed fallback) via `rules/maplist.mes`; each **core** sector's scheme word at metadata +8, re-read from the module and bound to the snapshot's entry identity and payload SHA-256. Word 0 is the map-default sentinel. The active sector is the one holding the actor's committed cell; a change re-selects at the same hour. | All nine original core sectors use the sentinel, so a visible sector switch is proven synthetically only. Context sectors, other maps and map transitions are out of scope. |

Clock (ARC-WORLD-010): the driver reads and advances the clock of the viewer's one
[game session](../world/game-session-runtime.md), never a clock of its own. The session
is created per load generation whenever area lighting is loaded (with or without the
cave), with the clock profile of the module's `rules/gameinit.mes` (year key 4, hour
key 5, payload digest bound into the profile; `AreaAmbientDriver.ClockProfile`). Each
frame `AreaAmbientDriver.Tick` admits host time through
[`GameTimeFlow`](../world/game-clock-runtime.md#host-time-flow-arc-world-010) (8× the
admitted interval, so one game hour per 7.5 real minutes) and holds a `Menu` pause
while the diagnostic palette or a dialog is open, releasing only a pause it set; resume
has no catch-up. **Game time +1 hour** is an explicit wait (`ExplicitWait`) that also
works during that pause. After every clock change that crosses an hour, and after a
sector change, the selection is re-applied and recorded in the session
(`RecordLighting`), so a session capture carries the time and the scheme shown for it
(FR-LIGHT-012). Automated runs (self-test, native smoke) get no live flow; they drive
the same tick with literal host times. Sleep/rest UI, turn-based combat, a scheduler
and a save file are not implemented; the 8× rate is a CE source observation, NOT_VERIFIED.

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
- `AreaLightingReader.Read(module, snapshot)` → `AreaLightingReadResult(Failure, Source)`. Only a strictly published snapshot is accepted, and every core sector entry must live under the map directory whose `mapinfo.txt` supplies the default. Caps: 1 MiB per rule payload, 8,192 MES records, 64 core sectors, 32 schemes. The caller passes the module archive the snapshot was built from and re-verifies the module file afterwards (the viewer does).
- `AreaLightingSource`: module digest, map default, raw core sector words, referenced schemes, startup declaration; validating public constructor for synthetic inputs; `TryResolveScheme(sector)`.
- `AmbientLightingState.Apply(hour, sector, out changed)`.

Viewer Core (`tools/terrain-viewer/Core/AreaAmbient.cs`): `AreaAmbientPolicy`
(channel per ART id and per plan command), `AreaLightingPlan` (attached to
`AreaLoadResult.Lighting`), and `AreaAmbientDriver` (clock owner + state; `AdvanceHours`,
`Apply(sector)`, `Enabled`). Structural fixtures without lighting draw unlit.

## Viewer controls

Following the [ARC-INPUT-001 route](../input/diagnostic-palette.md), diagnostics
live in the diagnostic palette (Ctrl+Shift+F9 / gamepad Start), never on original
game keys. In `crash-site-area` the palette has **Game time +1 hour** (a one-hour wait) and
**Ambient lighting on/off** (off shows unlit source pixels) plus a lighting status
line. The HUD shows the game time, DAY/NIGHT (`[06:00,18:00)`), whether time flows
(`time flows 8x`) or is paused (`PAUSED (dialog/palette open)`), the scheme id and
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
gameplay effects of darkness remain NOT_VERIFIED. Light sources (FR-LIGHT-008..010),
luminance checks (FR-LIGHT-011) and a save file (FR-LIFECYCLE, ARC-WORLD-011) are
separate work; the session snapshot already carries the clock and the applied scheme.
