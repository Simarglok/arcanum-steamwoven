# Audio: requirements, original check and task plan

Status: **Research**, work item ARC-RES-052 (2026-10-17). Requirements re-read from
[FR-MEDIA-003/004](../requirements/ui-media-lifecycle.md#fr-media-003),
[FR-UI-006](../requirements/ui-media-lifecycle.md#fr-ui-006),
[FR-LIFECYCLE-004](../requirements/ui-media-lifecycle.md#fr-lifecycle-004) and the [audio contract of ARC-RES-022](../audio/resources-and-playback-contract.md);
behaviour read in CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd` and checked
in the retail `Arcanum.exe` 1.0.7.4 (SHA-256 `9bd80511…8d52`) by the
[original executable check](original-executable-check.md); data counted on the owner's installed
original (GOG 1.0.7.4) through the [content stack](content-precedence.md). No code changes; the
tasks at the end implement it.

## Scope

Steamwoven plays no sound yet: `src` reads no sound table, the SCR admission refuses the three
sound actions, and the only audio code is the synthetic [audio lab](../audio/audio-lab.md)
(ARC-UI-002). ARC-RES-022 described the original's audio from CE alone and proposed reading
it from one explicitly selected archive. This study:

- re-states the audio requirements as testable items for the whole game (AU1–AU14);
- checks every audio behaviour those items rest on in the original executable (rows SN1–SN33);
- counts the installed sound data over all 81 maps of the module through the content stack;
- corrects ARC-RES-022 where the executable or the whole-world rule disagrees;
- splits full in-game audio into ordered, PR-sized tasks.

One rule governs all of it (owner rule, 2026-10-17): audio is **universal**. Every table,
scheme, map default, sector record, voice and music file is read through the content stack for
any map of any module; nothing is keyed to a location, a map name or a single archive.

## Requirements (AU1–AU14)

Each item is what the game must do; the rows it rests on are in the next section, the task that
delivers it in the last one. "Original" means the 1.0.7.4 behaviour that the Original rule
setting keeps; differences the Steamwoven setting may make are under
[switch candidates](#switch-candidates-and-steamwoven-choices).

| ID | Requirement | Rests on | Phase | Task |
| --- | --- | --- | --- | --- |
| AU1 | One sound catalogue over the content stack: SFX id → file through the index tables and the module's user table; music and ambient schemes; positional parameters; speech paths; fixed music files. Missing ids and files are typed results, never another sound. | SN1–SN3, SN21–SN22, census | Pre-alpha | DATA-040 |
| AU2 | Every installed sound file opens: WAV PCM 8/16-bit, mono/stereo, 8–44.1 kHz; MP3 MPEG-1 Layer III 44.1 kHz. Large music streams; nothing is extracted to disk. | census | Pre-alpha | DATA-041 |
| AU3 | Three channels with the original options: Effects, Voice and Music, each 0–10 (default 5), applied live, stored in user preferences outside saves. Ambient sounds use the Effects volume, scheme songs the Music volume. | SN8–SN9, SN14, SN16 | Pre-alpha | UI-040, UI-044 |
| AU4 | Music and ambient follow the world: the map default on every map open, a sector's pair when the local PC enters another sector (a 0/0 sector changes nothing), and script requests; two slots, already active schemes keep playing, a slot no longer requested fades out. | SN10–SN11, SN19–SN20, SN28, SN32 | Pre-alpha | WORLD-040, UI-041 |
| AU5 | Schemes play by the game hour: loop songs inside their hour window, ambient one-shots by per-mille chance every 250 ms with random volume and balance, at most one ambient per slot per tick. | SN12–SN15, SN33 | Pre-alpha | UI-041 |
| AU6 | Combat music: an NPC entering combat mode starts a random combat stinger and the combat loop together; the world schemes stop and resume (the latest request) when the PC leaves combat mode. | SN17 | Pre-alpha | WORLD-040, UI-041 |
| AU7 | Positional effects: listener at the view centre, linear attenuation and pan from the positional parameters, per-size volume for small/medium scenery, live update on scroll and on a source's move. | SN4–SN7 | Pre-alpha | UI-042 |
| AU8 | Event sounds of the world: footsteps (surface, armour, four variants), critter barks (critically hit, dying, fidget, attacking, alerted), weapons (use, hit, critical, miss, out of ammo), items (pickup, drop), containers and portals (open, close, locked), busting and looping scenery. | SN23–SN27 | Pre-alpha | WORLD-041, UI-042 |
| AU9 | Script sounds: `play_sound` (non-positional), `play_sound_on` (on an object), `play_sound_scheme` (scheme pair), and the sound operand of teleports and fades. | SN28–SN29 | Pre-alpha | SCRIPT-040 |
| AU10 | Dialogue voice: an NPC line with a voice plays it once on the voice channel, chosen by the PC's gender with female → male fallback; a new voiced line or the PC's choice stops it; a missing voice never delays or changes the dialogue. | SN21–SN22 | Pre-alpha | UI-043 |
| AU11 | Interface sounds: fixed ids for windows (open, close, page), messages (level up, bless, curse, poison), quest completion, inventory container and item actions. | SN31 | Pre-alpha | UI-044 |
| AU12 | Saved sound state: the scheme pair, a pending restore and the combat pair travel with the game save; a load resumes the schemes from their start. User volumes are never in a save. | SN18 | Pre-alpha | WORLD-040 |
| AU13 | Robustness: no device, a missing file, an unknown id or a full voice budget never changes game state or blocks input; the game runs silent with a status. | SN1, SN6, SN12 | Pre-alpha | every task |
| AU14 | Later surfaces: world-map travel music, story slides with voice, movie sound tracks, spell and eye-candy sounds, AI alert barks, floating-line voice. | SN26, SN29–SN30 | Later | [later](#later-not-in-this-plan) |

## What the original does

Read on 2026-10-17 by six readers in three domains: for each domain a first reader and a second
reader that saw only the questions (method step 6). Where the two differ the row says so and
names the reading that settles it. Every direct branch of the functions read stays inside
`.text`; none reaches the GOG `.IIDKing` section. Addresses are retail virtual addresses. The
verdicts follow the [ledger's](original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings)
meaning: **Original** (CE agrees, intended or harmless), **CE slip** (CE differs; both settings
follow the original), **Original bug** (CE agrees, unintended; switch candidate).

Evidence level ([F0–F4](original-executable-check.md#evidence-levels-for-a-function-f0f4)): **F2**
(two independent readings) for every row except SN21 (F1, read once by this task); SN32 is a
caller list checked in the executable by the reviewer. The readers checked operand widths, signed compares and rounding where the result
depends on them (SN4 64-bit signed division and the rounded single-precision distance, SN8 and
SN13 integer truncation, SN14 the absolute tick difference).

### Resources, settings and positional sound

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SN1 | `gsound_resolve_path` (0x41A940) | The SFX tables are searched in array order and the first table holding the id wins; only on a miss the module's `snd_user.mes` is searched. A hit gives `sound\` plus the entry text; a miss gives an empty path and an error, and the play request then plays nothing. | Original |
| SN2 | `gsound_init` (0x41AA30), `gsound_mod_load` (0x41B0A0) | Loads `sound\snd_00index.mes` (init fails when it is empty) and every table it names, in ascending entry number (MES entries are sorted); then `SchemeIndex.mes`, `SchemeList.mes` and `soundparams.mes`. `snd_user.mes` is loaded per module. The listener starts at screen point (400, 300); CE's HRP centring is a CE addition. Settings `effects volume`, `voice volume`, `music volume` default to 5. | Original |
| SN3 | `gsound_init` (0x41AA30), soundparams block | Keys 1–4: large-size min/max radius (defaults 150/800) and min/max pan distance (150/400). Keys 10/11/12: small min radius, max radius, **max volume %** (defaults 50, 150, 40). Keys 20/21/22: medium (50, 400, 70). Keys 30/31/32: extra-large (50, 1500, 100). Large max volume is 100. Without the file, small, medium and extra-large stay 0 and are silent. | CE slip (CE writes key 12 into the medium volume, which key 22 then overwrites) |
| SN4 | `gsound_calc_positional_params` (0x41B420) | Distance is `sqrt(dx² + (2·dy)²)` in origin-relative screen coordinates, rounded to nearest. Volume is 127 inside the min radius, 0 beyond the max radius, linear between. Pan is 0–127 (centre 64), linear between the min and max pan distance, left and right. The result is multiplied by the size's max volume % (small 40, medium 70, large and extra-large 100). | CE slip (CE never applies the per-size volume; CE truncates the distance, at most one unit) |
| SN5 | `gsound_size` (0x41B980), `gsound_range` (0x41B9E0) | Only scenery has a size other than large: scenery flag 0x40 small, 0x80 medium, 0x100 extra-large. Range in tiles is the size's max radius / 40 (defaults 3, 10, 20, 37). | Original |
| SN6 | `gsound_play_sfx_func` (0x41B2E0) through `gsound_play_sfx_on_obj` (0x41B930) | No distance or visibility cut-off: a source beyond the max radius still starts, at volume 0. A sound on an object takes the object's location once and does not follow it unless the caller moves the handle. All requests are refused while sound is locked. The first positional sound fixes the origin of the coordinates. Volume at start is `volume × effects / 100`. | Original |
| SN7 | `gsound_listener_set` (0x41C6D0), `scroll_by` (0x40E630), `scroll_origin_changed` (0x40E910), `gsound_move` (0x41C780) | Scrolling puts the listener on the tile at the centre of the view; every live positional sound is then recalculated. Changing the effects volume moves the listener to the local PC instead. | Original (the second is a quirk) |
| SN8 | volume get/changed (0x41C8A0–0x41C980), `adjust_volume` (0x41C9D0) | Effects and music: `setting × 127 / 10 × 80 / 100` (5 → 50, 10 → 101); voice: `setting × 127 / 10` (10 → 127). A slider change sets every live sound of that type to the flat channel volume, so per-song volume is lost until the next scheme load. Recalculation scales by /127 where the first play scaled by /100, so a positional sound gets about 21 % quieter at its first recalculation. | Original (quirks) |
| SN9 | options control table (0x5CCD78), Audio tab handlers (0x589960–0x589A00) | The Audio tab has exactly three bars, Effects, Voice and Music, 0–10. There is no speech switch and no combat-music switch; "Combat Taunts" sits on the display and text tab. | Original |
| SN10 | `gsound_lock` (0x41C610), `gsound_unlock` (0x41C660), caller `gmovie_play_path` (0x40DEE0) | While a movie plays: scheme loops stop at once, every new sound request is refused; one-shots and combat music keep playing. Unlock reloads the saved scheme pair. | Original |

### Music and ambient schemes, combat music, save and map

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SN11 | `gsound_play_scheme` (0x41BD50), `gsound_scheme_is_active` (0x41BD10) | Refused while locked (dropped, not deferred). Equal ids: ambient becomes 0. While combat music plays, the pair only becomes the restore target. Otherwise ids already active keep playing, a slot no longer requested is stopped (loops fade 25 ms) and the new ids load. 0 means "leave this slot empty": (X, 0) silences the ambient, (0, 0) both. | Original |
| SN12 | `gsound_scheme_load` (0x41BE20) | The SchemeIndex entry `#N` gives the first SchemeList number; N…N+99 are scanned, gaps allowed, at most 25 sounds kept. An index entry without `#` (such as "No Sound") loads nothing: a silent slot. `/anchor` and `/over` songs start at once. A 26th entry makes the original dereference a null pointer. | Original; the 26th entry is an **original bug** (crash; no installed scheme has more than 25) |
| SN13 | `gsound_scheme_parse_entry` (0x41BF70), `gsound_scheme_parse_param` (0x41C290) | Line lowercased; file name up to the first space; options found as substrings. `/over` wins over `/loop` (a transition, never a loop); `/loop` reads `/time`; ambient entries read `/freq`, `/time`, `/bal`, `/scatter` (scatter overrides volume and balance). Defaults: frequency 5, hours 0–23, volume 100/100, balance 50/50. Percent → `127 × v / 100`, clamped to 0–127; a max below its min is raised to it. | CE slips (CE makes `/over /loop` both; CE clamps a balance-left above 127 to 0, the original to 127) |
| SN14 | `gsound_ping` (0x41B2A0), `gsound_update` (0x41BAF0) | A tick runs when more than 250 ms passed. The hour is the game clock's. Hour windows are inclusive and never wrap: a reversed window never plays. An ambient fires when `random(0, 999) < freq` (per mille per tick); volume and balance are drawn below their maximum, which is never reached. After one ambient fires, the rest of that slot waits for the next tick. Loop songs start or fade (25 ms) at the window edges; a finished `/over` song restores the pair it replaced. All draws use the shared game random generator. | CE slip (CE keeps scanning after an ambient fires); the never-wrapping window is an **original bug** in effect (20 installed entries never play, see census) |
| SN15 | `gsound_scheme_stop` (0x41BA20), `gsound_stop_all` (0x41C340), `gsound_scheme_reset` (0x41AFB0) | Stopping a scheme fades only its loops; one-shots play out. `gsound_stop_all` also stops every sound (effects, voice, combat music). | Original |
| SN16 | `gsound_play_voice` (0x41C370), `gsound_play_music_once` (0x41C3D0), `gsound_play_music_indefinitely` (0x41C450) | Voice at the voice volume, music at the music volume, streamed, not positional; refused while locked. Scheme songs start at `music × raw volume / 100`, so a `/vol:100` song is 1.27× a fixed music file. | Original |
| SN17 | `gsound_start_combat_music` (0x41C4D0), `gsound_stop_combat_music` (0x41C5A0), caller `combat_critter_toggle_combat_mode` (0x4B3F80) | Starts when an **NPC** enters combat mode, real-time or turn-based; saves the slot ids, fades the schemes (25 ms), then starts `sound\music\combat N.mp3` (N = 1–6) once and `sound\music\combatmusic.mp3` looping **at the same time**. One on/off flag, no nesting. Stops only when the **local PC** leaves combat mode: both cut without fade, then the latest pair requested during combat plays. | Original (CE's "stinger followed by the loop" comment is wrong; the code plays both at once) |
| SN18 | `gsound_save` (0x41B1D0), `gsound_load` (0x41B0F0) | Saves eight integers: the two slot ids, the pending-restore flag, the two restore ids, the combat flag, the two pre-combat ids; not the lock or handles. Load calls `play_scheme` with the slot ids, so songs restart from the beginning. A save made during combat stores 0/0 slots; on load the combat flag defers that pair over the pre-combat ids, combat stays silent and leaving it restores silence. | Original; the combat save is an **original bug** (one reader counted seven values; the second reading and a third check of the function agree on eight) |
| SN19 | `map_open_in_game` (0x40FC70), `map_close` (0x4106D0), `map_load_extension` (0x4115D0) | A map change stops every sound without fade and resets sound state (combat flag included); then the new map's `mapinfo.txt` `SoundScheme: music, ambient` is applied as written on every map open. No line: nothing plays until a sector or script asks. | Original |
| SN20 | `objlist_notify_sector_changed` (0x4F2330), caller (0x444270) | Only when the **local PC** is placed in another sector (walk, teleport, drop): the sector's pair is applied unless both ids are 0. A sector with one id 0 silences that slot. | Original |

### Dialogue voice, event sounds, scripts and interface

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SN21 | speech id (0x4189C0) | `(script number & 0x7FFF) << 16 | (number in the NPC line's condition field & 0xFFFF)`; −1 when either is missing. | Original (read once by this task) |
| SN22 | `dialog_ui_speech_start` (0x5688D0), `dialog_ui_speech_stop` (0x5689B0), `dialog_ui_process_option` (0x567E30) | −1 returns before stopping, so an unvoiced line leaves the previous voice playing. Otherwise the previous voice stops, then `sound\speech\%05d\v%d_%c.mp3` with the **PC's** gender (male `m`, female `f`); a missing female file falls back to male, never the reverse; missing → silence, text unchanged. One voice at a time, not positional. Stopped when the PC picks an option and at shutdown; closing the dialog does not stop it. | Original |
| SN23 | `sfx_item_sound` (0x4F0BF0) | Base is the item's sound field (`OBJ_F_SOUND_EFFECT`). Use +0, miss +3 (no base: 4010), hit +4 or +5 at random, critical +6, out of ammo +7. Pickup/drop: 5950/5960 + material class (heavy metal counts as light at weight ≤ 2000; gold 5958/5968). Hit without a resolvable base: `7000 + 20 × weapon class + 3 × target class + random(0, 1)`, the target's armour standing in for the target, no weapon = flesh. | Original |
| SN24 | `sfx_critter_sound` (0x4F0ED0) | Critter sound field + type: 0 critically hit, 1 dying, 2 gruesome dying, 3 fidget, 4 attacking, 5 alerted, 6 agitated (never requested). Footsteps: plate armour art 2900 on any surface; otherwise the tile's sound class (from the tile-name rules) gives dirt 2904, sand 2912, snow 2916, stone 2920, water 2928, wood 2932, chain armour +4 on dirt, stone and wood; then +random(0, 3). Nothing plays while the **local PC** is switched off (world-map travel). | CE slip (CE tests the off flag on the sounding critter) |
| SN25 | `sfx_container_sound` (0x4F0FD0), `sfx_portal_sound` (0x4F1010), `sfx_misc_sound` (0x4F1050) | Containers and portals: sound field + 0 open, +1 close, +2 locked. Misc (walls, scenery, projectiles, traps, weapons): busting +0 (weapon +1), destroying +1 (weapon +2), both only when the file resolves; animating +2. | Original |
| SN26 | footsteps in the path-move update (0x4305D0); `AGbeginAnimOpenDoor` (0x42D080), `AGbeginAnimCloseDoor` (0x42D1C0), `AGbeginAnimDying` (0x42D570), `AGbeginAnimAttack` (0x432700), `AGbeginAnimLoopAnim` (0x42DA50), eye candy (0x431550, 0x431960) | Footsteps on frames **3 and 8** of a walk or run animation of a non-monstrous critter (fixed numbers, not the ART action frame; sneaking and knock-back are silent); no visibility test. Door open/close/locked on the door; pickup on the picker (local PC only); dying (fall-down → type 1, else 2); fidget on frame 1; attacking bark on 1 attack in 4; the AI's alerted bark plays once at the attack approach. Looping scenery sounds play forever while the PC is within range and stop beyond it. Looping eye-candy sounds start when no handle exists (flag bit 0x80). | Original for the events; CE slips: looping scenery and eye-candy sounds loop in the original (CE plays once), CE tests flag 0x80000000, inverts the handle test and swaps two goal fields. The readers differed on the stored bark: CE shows it is the AI's alerted sound (`ai.c` sets it, the anim goal plays it) |
| SN27 | `sub_4B2F60`, `sub_4B3170`, `combat_process_melee_attack` (0x4B3770) | Use (or out of ammo) when the attack starts, then critical (critter "critically hit" on the target and weapon critical on the attacker), hit or miss on the attacker; all positional, once. | Original |
| SN28 | `script_execute_action` (0x447220) cases, `mp_gsound_play_sfx` (0x4EECB0), `mp_gsound_play_sfx_on_obj` (0x4EED80), `mp_gsound_play_scheme` (0x4EEE00) | `play_sound`: non-positional, once. `play_sound_on`: positional on the object, once. `play_sound_scheme`: `gsound_play_scheme(music, ambient)` unchanged. | Original |
| SN29 | `teleport_process` (0x4D3460), fade actions in `script_execute_action` | The teleport sound plays on the teleported object at its departure point, after the fade-out and before the move. A script fade with a movie passes the sound to the movie as its sound track; without a movie it plays non-positionally. | Original (CE omits multiplayer-only gating and tests the movie id as signed; no single-player effect) |
| SN30 | `slide_ui_start` (0x569600), `slide_ui_do_slide` (0x569770), `wmap_ui_state_set` (0x5615D0) | Slides: every sound stops (25 ms), `sound\music\DwarvenMusic.mp3` loops, each slide plays `sound\speech\slide\<file>` and ends when it finishes or on a key or click; no minimum time. World-map travel: every sound stops, `sound\music\arcanum.mp3` loops until travel ends. | Original (CE's three-second minimum per slide is a CE change) |
| SN31 | `quest_state_set_internal` (0x4C4E60), interface callers, hot-key equip (0x550000) | Quest completed: 3028, non-positional. Windows: open 3008, close 3009, page 3010, switch 3011; messages: bless 3002, cursed 3003, exclamation 3004, level up 3005, poisoned 3006; inventory: containers and items by their formulas, 3007, bad trade 8158; generic click 0; schematic combine 3018 + discipline. All non-positional. Equipping from a hot key into an empty slot plays the slot number (1000-range) as a sound id. | Original; the hot-key id is an **original bug** |
| SN32 | `gsound_play_scheme` callers | Requests come only from the map default, sector changes, `play_sound_scheme`, unlock, load, `/over` completion and the end of combat (plus a multiplayer relay). | Original (the executable's callers of 0x41BD50 are exactly these) |
| SN33 | `random_between` (0x4CEF00) | Ambient chances, volume and balance draws, footstep variants and the combat stinger all advance the shared game random generator. | Original |

The second readings agree on every behaviour above except the three noted (SN18 value count,
SN26 bark source and eye-candy flag bit), each settled by a third reading: of the function in the executable for SN18 and the flag bit, of CE's `ai.c` for the bark source.
Readers also saw two unreferenced helpers in `gsound` with no CE counterpart; nothing calls
them.

## Installed data (GOG 1.0.7.4, module Arcanum)

Counted with a scratch runner over `ContentStack` and `WorldCatalog` on `origin/main`; no
payload or table text is reproduced.

- **Keys under `sound/`**: 3,426 winners. 706 WAV effects at the root (703 from one base
  archive, 3 from another), 11 MES tables, 26 MP3 music files in the **module folder**
  (`modules/Arcanum/sound/music`, loose), 2,678 MP3 speech files in the **module archive**,
  plus four `.snd` files and one source-control file in `tig.dat` that the game does not load.
  `snd_critter.mes` has two copies; the winner is the base archive in search order.
- **SFX tables**: six named by `snd_00index.mes` plus `snd_user.mes` (one dummy record).
  834 distinct ids, no id in two tables, 805 empty records; 27 ids name a file that is not
  installed (they play nothing in the original). 527 of the 706 root WAV files are named by a
  table; the rest are only reachable by an item, critter or object formula (SN23–SN25), by a
  script id, or not at all.
- **Schemes**: 60 index entries, 58 with `#`: 26 music, 30 ambient, 2 reserved; "None" (0) and
  "No Sound" (29) have no `#` and load nothing. 153 list entries, at most 25 in any scheme
  (none reaches the SN12 crash); options used: `/time` 110, `/loop` 85, `/freq` 67, `/vol` 62,
  `/scatter` 32, `/bal` 27; **no `/over` or `/anchor`** entry exists. **20 entries have a
  reversed hour window** (mostly night loops written `19-0`, also `22-0` and `24-4`); under
  SN14 none of them ever plays. 3 entries name a WAV that is not installed.
- **Map defaults**: 74 of 81 maps have a `SoundScheme` line (21 distinct ids); scheme 29 ("No
  Sound") is the ambient of most interiors.
- **Sector records**: 1,680 sector files over 81 maps, all read; 26 have no sound record (old
  sector version), all 1,654 others have flags 0. 410 sectors carry a non-zero pair, on 6 maps:
  the world map (380 of its 621 sectors, 73 pairs), K'na Tha, both Tarant sewer levels, the
  shop map and the wolf cave. Every map and sector id is a SchemeIndex entry (29 "No Sound"
  loads nothing; the reserved 27 and 28 are used by the sewers); music and ambient slots hold
  either kind (slots are not typed).
- **Formats**: WAV all PCM (format tag 1): mono 16-bit at 8,000 / 11,025 / 22,050 / 44,100 Hz,
  mono 8-bit at 11,025 / 44,100 Hz, stereo 16-bit at 22,050 / 44,100 Hz; 2 files carry a
  `smpl` chunk. MP3 all MPEG-1 Layer III 44.1 kHz: music stereo, speech mono (12 stereo).
- **Speech**: 2,678 files in 99 folders; 2,630 follow `NNNNN/vL_g.mp3` (2,513 male, 117
  female, every female line has a male file), 48 are the slide voices under `speech/slide/`.

## Corrections to ARC-RES-022

- **Source policy.** ARC-RES-022 resolved audio from one explicitly selected archive or root.
  The game resolves every sound name through the normal mount order, and the data spreads over
  three source kinds (base archives for effects and tables, the module archive for speech and
  the user table, the module folder for music). Audio uses the content stack; the
  selected-source helpers stay a research probe only.
- **soundparams key 12** is the small size's volume (SN3); there is no key 12/22 overwrite.
- **Per-size volume** is applied (SN4); CE's omission is not the original.
- **Balance-left clamp** is to 127 (SN13), and `/over` beats `/loop`.
- **One ambient per slot per tick** (SN14), not every eligible entry.
- **Combat music** starts stinger and loop together, from an NPC entering combat (SN17).
- **Voice gender** is the PC's, and closing the dialog does not stop the voice (SN22).
- **Overnight windows**: they exist in the data (20 entries), so "unsupported" is not an
  option; the parser accepts them and the player applies the rule setting (next section).
- The proposed mixer (Master + Music/Voice/SFX/Ambient) was an extension; the original has
  three channels and ambient follows Effects (SN8, SN14). A separate Ambient slider stays a
  Steamwoven option, not the default.

## Switch candidates and Steamwoven choices

Owner decision D8 gives every original bug a switch candidate; each slice records its fix
under D2 for the owner. Proposed, not decided:

| Row | Original | Proposed Steamwoven setting |
| --- | --- | --- |
| SN14 | A reversed hour window never plays (20 night entries silent). | Wrap past midnight, so `19-0` plays from 19:00 to 00:59. |
| SN18 | A save made during combat loses the pre-combat schemes. | Save the pre-combat pair as the slot ids, so a load resumes correctly. |
| SN31 | Hot-key equip into an empty slot plays the slot number as a sound. | No sound (or the generic click). |
| SN12 | A 26th scheme entry crashes. | Both settings refuse the scheme with a typed issue (owner rule: Original never reproduces a crash). |

Quirks kept in both settings unless the owner asks otherwise: the effects-volume change that
moves the listener to the PC (SN7), the flattening of song volume on a slider move and the
/100 → /127 recalculation drop (SN8), the voice that outlives a closed dialog (SN22).

Two choices replace original mechanisms without changing what is heard (proposed; the owner
confirms them in the first task that needs them):

- **Randomness.** The original draws ambient chances, volume and balance, footstep variants
  and the combat stinger from the game generator (SN33). Proposed: these purely audible draws
  use a presentation generator, so ambience never shifts a game roll. Draws that already belong
  to a rule stay in the session generator for seeded parity, first of all the 1-in-4 attack
  bark (`AttackSoundSides` of [real-time combat](../world/real-time-combat-runtime.md)).
- **Failure.** A missing device, file or id plays nothing and reports it; no audio state is
  authoritative except the scheme state of AU12, which belongs to the session.

## Architecture

- **Data** (`Arcanum.Data.Audio`, Godot-free): the catalogue of AU1 and the payload admission
  of AU2 over `ContentStack`; the map default is already `WorldMapInfo`, the sector pair is
  words 4 and 5 (0-based) of `WorldSector.MetadataAndSound`.
- **Session** (`Arcanum.Session`): the authoritative scheme state of AU4/AU6/AU12 (two desired
  ids, restore pair, combat flag and pair, movie lock), driven by map open, the local PC's
  sector change, script requests and combat mode; in the snapshot.
- **Rules** (Simulation/Session, Godot-free): sound-id rules of SN23–SN26 producing immutable
  sound cues (id, object or location, loops) on the session's output, like other presentation
  events; scripts emit the same cues.
- **Presentation rules** (`Arcanum.Presentation`, Godot-free, tested headless and later by the
  function oracle): the scheme player (hour windows, 250 ms ticks, one ambient per slot, fades,
  presentation random), positional mixing (listener, attenuation, pan, per-size volume) and the
  volume formulas of SN8.
- **Godot backend**: one device backend shared by the viewers (grown from the audio lab) that
  decodes and plays what the rules decide, plus the viewer hosts for voice, interface sounds and
  the volume preferences. Presentation reads Session and cues, never writes game state.

## Task plan

The codes are **proposed**: under owner decision D6 a code is allocated when its task is created,
when its creator checks it is still free (else the next free number is taken). A 040 block per
type keeps the audio series together and away from the numbers other threads allocate one after
another (DATA-025/026, WORLD-031/032, COMBAT-005 and RES-051 were taken while this study ran). Each task is one PR; the checks are the task's own
plus the root checks; viewer tasks follow the launcher rule (a new argument updates
`tools/local_dev/launch.py`, `./arcanum` and its docs in the same PR).

| Order | Code | Result | Depends on | Scope and done when |
| --- | --- | --- | --- | --- |
| 1 | ARC-DATA-040 | Sound catalogue over the content stack | — | `Arcanum.Data.Audio`: SFX resolver (SN1–SN2), scheme catalogue with the original grammar (SN12–SN13, typed refusal past 25), positional profile (SN3), speech candidates (SN21–SN22), combat and fixed music names, sector pair accessor. Real-data test reproduces the census (834 ids, 27 missing files, 58 schemes, 153 entries, 20 reversed windows, every map and sector id a SchemeIndex entry). |
| 2 | ARC-DATA-041 | Every installed sound opens | DATA-040 | WAV PCM and MP3 header admission for the classes of the census, bounded member reads, streamed loose music, typed refusals for anything else. Real-data test admits all 3,410 sound files. |
| 3 | ARC-UI-040 | Sound in the viewer: backend and volumes | DATA-040, DATA-041 | Audio backend from the lab in the AREA viewer: Effects/Voice/Music buses with the SN8 volumes, preferences file outside saves (0–10, default 5), silent mode without a device; smoke plays interface ids by number. |
| 4 | ARC-WORLD-040 | Music and ambient state in the session | DATA-040 | Session scheme owner: map default on open, local PC sector change (0/0 skipped), script request, combat start by NPC and end by PC with restore, movie lock; snapshot schema bump; SN18 switch. Headless tests over synthetic schemes. |
| 5 | ARC-UI-041 | Music and ambient play in the viewer | UI-040, WORLD-040 | Scheme player rules in `Arcanum.Presentation` and their host in AREA and map mode: 250 ms ticks on the session hour, loop windows, ambient chances with presentation random, one ambient per slot per tick, 25 ms fades, combat stinger and loop, cut on map change; SN14 switch. Smoke: Crash Site day/night and the cave. |
| 6 | ARC-WORLD-041 | Sound cues of world events | DATA-040 | Godot-free rules SN23–SN26 and SN27 (item, critter, footstep by tile sound class and armour, container, portal, misc, combat) and the session cue output; prototype and object sound fields through the stack; local-PC off rule. Headless tests. |
| 7 | ARC-UI-042 | Positional effects in the viewer | UI-040, WORLD-041 | Positional rules in `Arcanum.Presentation` (SN4–SN7: listener at the view centre, attenuation and pan with per-size volume, recalculation on scroll) and their host; plays footsteps, doors, pickup, combat and looping scenery cues. |
| 8 | ARC-UI-043 | Dialogue voice | UI-040, DATA-040 | Speech id per NPC line in the conversation (SN21), voice by PC gender with fallback, start/replace/stop as SN22, voice bus; missing voice changes nothing. Smoke: Virgil's lines. |
| 9 | ARC-SCRIPT-040 | Script sounds | WORLD-040, WORLD-041 | Admit `play_sound`, `play_sound_on`, `play_sound_scheme` in the SCR interpreter, output through the session's cue and scheme ports; teleport and fade sound operand when the teleport action is admitted. |
| 10 | ARC-UI-044 | Interface sounds and audio options | UI-040 | SN31 ids on the existing windows (inventory and containers, character sheet, conversation, level up, quest complete) and the three sliders in the viewer settings, applied live (SN8). |

Order 1 → 2 → 3 is the trunk; after it, 4 → 5, 6 → 7 and 8 can run in parallel threads with
rebases (WORLD-040 and WORLD-041 both touch the session, the UI tasks the viewer host), then 9
and 10. Each task updates
[FR-MEDIA-003/004](../requirements/ui-media-lifecycle.md#fr-media-003) coverage and
[status](../project/status.md).

### Later (not in this plan)

World-map travel music (needs the world-map screen), story slides with voice (FR-MEDIA-002),
movie sound tracks (FR-MEDIA-001), spell and eye-candy sounds (with the magic slices), AI
alerted barks (with the AI slices), and voice of floating lines and combat taunts (with the
text-float layer). Each reuses the catalogue, backend and cue output above.

## Not checked

- No row is F3: the [function oracle](original-executable-check.md#function-emulation-oracle)
  (ARC-DEV-007) is not merged. The formulas of SN3–SN4, SN8, SN13–SN14 and SN23–SN24 are the
  first candidates for it in the tasks that implement them.
- Nothing was heard: these are static readings and data counts. How often a branch runs, the
  exact Miles mixing curve, latency and the timing of fades in the running game need an
  observation in the running original.
- Prototype sound fields were not counted (which items, critters and objects carry a base and
  whether `base + offset` names an installed file); ARC-WORLD-041 counts them.
- The tile-name sound classes were not tabulated; ARC-WORLD-041 reads them with the tile-name
  rules the step evidence already loads.
- The `play_sound` ids of the 146 script uses ([SCR census](scr-census.md#action-types-d)) were not resolved against the catalogue;
  ARC-SCRIPT-040 does it.
- Multiplayer branches present in the executable and missing in CE were noted, not read.
