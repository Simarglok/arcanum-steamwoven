# Music and ambient in the viewer (ARC-UI-041)

Status: **Implemented bounded component**, task 5 of the
[audio plan](../research/audio-evidence.md#task-plan). The AREA viewer (exterior, cave and map
mode) plays the session's music and ambient schemes ([session state](../world/session-sound-runtime.md))
as the original does, for any map of any module.

## Scheme player (Godot-free)

`Arcanum.Presentation.Audio.SchemePlayer` turns the session's two slots (the schemes' sounds from
the [catalogue](sound-catalog.md)), the restore flag, combat music, the game hour and real time
into commands for a host ([audio study](../research/audio-evidence.md) SN12–SN17, checked in the
executable):

- A slot that changes fades its loop songs over 25 ms and leaves its one-shot songs playing; a new
  scheme starts its anchor and transition songs at once.
- A tick runs once more than 250 ms of real time have passed and walks each slot in list order:
  an ambient sound inside its hour window fires when a draw 0–999 is below its per-mille
  frequency, at a volume and a balance drawn below their maximum (the maximum itself is never
  drawn), and then the rest of that slot waits for the next tick; a loop song starts inside its
  window and fades outside it; a transition song that ended while a restore is pending reports it
  to the session.
- Hour windows are inclusive. A reversed window (20 installed night entries such as `19-0`) never
  plays in the **Original** rule setting and runs past midnight in **Steamwoven**
  (`SoundRuleProfile.HourWindowsNeverWrap`, ledger SN14, [rule modes](../world/rule-modes.md#registry)).
- Combat music: when the session's combat-start counter moves, a stinger 1–6 and the combat loop
  start together; when the flag clears both stop at once. A loaded flag replays no stinger.
- Draws come from an audio generator (`IAudioRandom`), never the game's: ambience never shifts a
  game roll (a project choice; the original uses its game generator).

## Viewer integration

- **Map open** (area start, cave entry and return, a map-mode transfer): the viewer reads the map's
  `SoundScheme` pair through the universal map catalogue (map list → `WorldCatalog`), calls
  `EnterMapSound`, stops every sound of the scene and starts a fresh player; the arrival sector
  applies.
- **Sector**: when the PC's committed cell enters another sector, its sound record (read once per
  sector through `WorldCatalog.ReadSector`) goes to `EnterSectorSound`; a 0/0 record changes
  nothing.
- **Quick load** (F8): `Restore` receives the saved actor's map default (`AreaQuickSave.Load` takes
  it), so the session's loaded state is the original's; only the scene's sound starts afresh.
- Songs play on the music channel at `music × song volume / 100`, ambient sounds on the effects
  channel at `effects × drawn volume / 100` with the drawn balance as a pan (one of nine panning
  buses per channel, steps of 0.25, a Steamwoven choice); the combat stinger and loop at the bare
  music volume. Ambient payloads are kept per module; songs are read when they start.
- The AREA status line shows `music/ambient: slots A/B[, combat], voices N`.
- Known limits: only the pair the session holds when a frame begins reaches the player, so the once-songs of a pair
  replaced within the same frame (a map default immediately replaced by the arrival sector's pair) never start; the
  original's order there is not recorded. A loop song whose start failed is not retried until its window or slot
  changes. A first entry into a sector parses that sector once on the main thread (cached), and songs open their
  payload when they start; ambient payloads are cached per module (bounded by the module's ambient files).

## Checks

- `tests/Arcanum.Presentation.Tests/SchemePlayerTests.cs`: once-songs at load, loops by window and
  tick, the 250 ms rule, ambient chance, draws and one ambient per slot per tick, per-slot ambients,
  reversed windows in both settings, slot changes, the transition report, combat start and stop.
- Native smoke on the original installation (map mode `fal'kins maze`; the same stage is chained into the exterior
  AREA smoke, which was not run because its first pixel stage fails on main for unrelated reasons): `ARC-UI-041 native-smoke
  schemes PASS`: the session holds the map default (15/39 at 15:00), its day loop song plays; a
  requested scheme with an ambient sound at this hour fires within the first ticks with draws
  pinned to their minimum (the map's pair is requested again afterwards); the combat stinger and loop
  start together and stop at once. Hearing itself is not checked.
