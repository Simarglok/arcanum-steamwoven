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
- **Main menu** (ARC-DEV-332, ARC-UI-139, [SN38](../research/audio-evidence.md#music-and-ambient-schemes-combat-music-save-and-map)): the menu starts no song of its own. The
  original's game init opens the shopping map (the start map without one) and puts the PC on it, so the menu plays what that map and its
  start sector say: the shop map has no default pair, its sector (0, 0) has 26/29, scheme 26 "Main Menu", which loops `arcanum.mp3` at
  every hour (scheme 29 loads nothing). The main theme loops the whole time from the start until a game begins or a save loads. The viewer
  plays it **without a game session or a scene** (ARC-UI-139; since ARC-DEV-387 the start map loads only for a new character, so the menu
  stands over neither): `StartMenuMusic` (`TerrainViewerMenuMusic.cs`) picks the map and sector from the map list (`MainMenuSound.Choose`),
  fills the two slots as the original's scheme request leaves them (`MainMenuSound.Choose(mapDefault, sector, loads)`: the sector's pair
  replaces the map default unless both ids are 0, an ambient equal to the music is dropped, a scheme without a list fills no slot) and
  ticks a scheme player of its own with those slots (no restore, no combat, the hour fixed at 12 because the menu has no game clock: the
  installed pair plays at every hour). The song starts when the main menu (root window Main) opens, before any scene; a scene published
  or torn down under the menu neither stops nor restarts it, and the scene's own pair is not played while the menu stands; it stops when the
  menu ends (a new game's map open, a save loaded, any close). The session's sound state is not touched. The in-play menu keeps the game's
  music. A **new game** is a map open of the start map: every sound stops, the map's default pair plays and then the arrival sector's pair
  (the Crash Site's 16/49); a load keeps the saved sound state as before. The log has `ARC-UI-139 menu-music map=... pair=... slots=...` and
  `ARC-UI-139 menu-music stopped`; the menu smoke prints `ARC-DEV-332 native-smoke menu-sound PASS ... voices=1` (the song is playing
  across the scene's attach) and `new-game-sound PASS`. Known limit: a transition song in another module's menu pair would play once without
  the restore bookkeeping the session keeps.
- **Quick load** (F8): `Restore` receives the saved actor's map default (`AreaQuickSave.Load` takes
  it), so the session's loaded state is the original's; only the scene's sound starts afresh.
- Songs play on the music channel at `music × song volume / 100`, ambient sounds on the effects
  channel at `effects × drawn volume / 100` with the drawn balance as a pan (one of nine panning
  buses per channel, steps of 0.25, a Steamwoven choice); the combat stinger and loop at the bare
  music volume. Ambient payloads are kept per module; songs are read when they start.
- The AREA status line shows `music/ambient: slots A/B[, combat], voices N`.
- The log has a line for every scheme song start (`ARC-UI-041 song start entry=N file=F window=S-E hour=H loop=L
  started=B`) and fade (`ARC-UI-041 song fade entry=N hour=H faded=B`, B false when no playing voice was left to
  fade), so a report of silence shows whether a song was due (ARC-RES-075).
- **Night silence is the original's** (ARC-RES-075, bug report of 2026-10-29: no music at Crash Site at 22:28). The
  Crash Site's arrival sector pair is 16/49 (map default 1/40). Schemes 16 and 1 hold one song each,
  `sound/music/wilderness.mp3` with window 05–19; scheme 49 holds only ambient sounds (birds by day; crickets 19–23
  and 0–6 and an owl hoot by night; howls at any hour and more of them around midnight); scheme 40 loops
  `forest_day.wav` 05–19 and `forest_night.wav` 19–0 and 0–5 (the reversed 19–0 entry plays only in the Steamwoven
  setting, SN14; the arrival sector's pair replaces 1/40 anyway). Between 20:00 and 04:59 no song of the pair is
  inside its window, so the original plays no music there, only the night ambient on the effects channel. SN14 is not
  involved: 05–19 is not reversed. All these files are found in the installed module folder, which the packaged game
  reads too (its log: 60 schemes, 0 issues).
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
  start together and stop at once. Hearing itself is not checked. An area without a session clock (since ARC-WORLD-057 only a map whose start clock cannot be read; a map
  drawn unlit has one) has no session and prints `ARC-UI-041 native-smoke schemes NONE` instead (ARC-UI-045).
