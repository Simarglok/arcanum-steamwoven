# Bink movies: the movie list, the callers and the decoder options

Status: **Research**, work item ARC-RES-1108 (issue #1108, plan slice
[E7-r](../project/missing-systems-plan.md#e7-media-endings-and-credits), 2026-11-12). It unblocks
ARC-RENDER-1170 (issue #1170, E7-a: movie player, movie steps, View Intro). Docs only: no code, no decoder, no
video file or frame is in git.

Requirements: [FR-MEDIA-001](../requirements/ui-media-lifecycle.md#fr-media-001) (starting and playing movies);
FR-MEDIA-002 is E7-b and is not touched. The owner allowed an external LGPL dependency for the Bink read on
2026-11-10 ([decisions](../architecture/decisions.md), row "Missing systems work order"); the scope of the movie
player is still the owner's (issue #1170 carries `owner-decision`), so section 6 names the choice.

## Evidence discipline

- **Data (D)**: the installed retail 1.0.7.4 game (GOG layout). The numbers below are container header fields
  read from the ten `.bik` files and the 8 rows of `movies.mes`, and the operands of script lines read from the
  script files. Nothing but numbers and the file names the original itself uses is written here; no text, frame or
  sound of a movie.
- **Exe (E)**: the retail `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read in the private Ghidra export
  ([toolchain](../workflow/toolchain.md#ghidra-text-export-arc-dev-377)): the functions 0x531530, 0x5317D0,
  0x40DEE0, 0x40DE70, 0x404810, 0x5445F0, the movie branches of 0x4D3460 and of the script action switch in 0x447220
  (case 0x67), the Bink import list and `binkw32.dll`'s header. Level F2 for the rows of section 3.
- **Web (W)**: FFmpeg's `libavcodec/bink.c` and `libavformat/bink.c` on its master branch, read on 2026-11-12 for the
  licence header, the accepted revisions and the audio flags; the status of Bink 2 from FFmpeg's issue tracker (a
  search, not a source read).
- **Not done:** no movie was decoded (no `ffmpeg` on the owner's Mac, and none was installed for this study), so
  "FFmpeg plays these files" rests on the header match of section 2 and on the decoder's documented revision list,
  not on a run. The licence reading is an engineering reading, not legal advice.

## 1. The movie list

`modules/Arcanum/movies/movies.mes` is the movie table: eight numbered rows, number 0 reserved ("the empty
non-movie to scripts"). The exe builds the path `movies\<name>` from the row and hands it to the content layer
(0x52F410), which resolves it like any resource. The two logo movies are not in the table: the exe names them by
path. This installation holds the files loose (`modules/Arcanum/movies/` and `data/TIGCache/movies/`; the logos are
also inside `arcanum3.dat`). **Ten files, 167 MB** ([data census](data-census.md) says 18 movies and 351 MB; those
figures were not reproduced here, the installation has ten).

| No. | File | Size (MB) | Frame size | Frames at fps | Length (s) | Audio tracks (ids) | Who plays it |
| ---: | --- | ---: | --- | --- | ---: | --- | --- |
| 1 | `00069.bik` | 27.3 | 720×346 | 1,460 at 15 | 97 | 1 (0) | View Intro; new-game start (first movie) |
| 2 | `51169.bik` | 5.4 | 800×400 | 302 at 15 | 20 | 1 (0) | scripts 1105 (line 89), 2863, 3061 |
| 3 | `91568.bik` | 6.5 | 800×400 | 363 at 15 | 24 | 1 (0) | scripts 1105 (line 92), 2862 |
| 4 | `02112.bik` | 41.2 | 800×400 | 2,100 at 15 | 140 | **2 (0, 1)** | script 1093 with track 1, script 1109 with the default track |
| 5 | `G0021.bik` | 8.2 | 800×400 | 414 at 15 | 28 | 1 (0) | scripts 2862, 2863, 3061 |
| 6 | `A0021.bik` | 43.1 | 800×400 | 2,432 at 15 | 162 | 1 (0) | script 1105 (line 101) |
| 7 | `01138.bik` | 26.8 | 720×346 | 1,371 at 15 | 91 | 1 (0) | View Intro; new-game start (second movie) |
| 8 | `50000.bik` | 3.9 | 800×600 | 171 at 15 | 11 | 1 (0) | start-up, before the first menu |
| — | `SierraLogo.bik` | 2.2 | 800×400 | 201 at 27.8 | 7 | 1 (0) | start-up (module init), by path |
| — | `TroikaLogo.bik` | 2.8 | 800×400 | 221 at 15 | 15 | 1 (0) | start-up (module init), by path |

(A comment in `movies.mes` labels number 2 as one character's death; treat it as a label of the file. What each
movie shows was not looked at.)

Header facts shared by all ten files: container magic `BIK` with revision letter **`i`** (a late Bink 1
revision), no alpha flag, one video stream, audio **44.1 kHz stereo** with the track flag word 0xE000 (stereo bit set,
the DCT bit clear, so the RDFT audio codec in FFmpeg's terms). The player library the exe loads, `binkw32.dll`
(263,168 bytes), carries a PE time stamp of April 2000 and exports the Bink 1 API; this matches the Bink 1 era.
Movie 4 is the only multi-track file: its two tracks carry the ids 0 and 1.

## 2. Where the movies are called

All calls go through one player; there is no other video path in the exe.

| Id | Caller | What it asks for |
| --- | --- | --- |
| MV1 | start-up 0x401000, right after the default module has loaded and before the first menu loop | movie 8, flags 8, default track. The condition that shows it was not read in this study ([FR-MEDIA-001](../requirements/ui-media-lifecycle.md#fr-media-001) says the same). |
| MV2 | game-module init 0x4020F0 through 0x404810 | the two logo movies by path (Sierra, then Troika), flags 0 |
| MV3 | menu "View Intro" 0x5445F0 (the single player window 10) | movie 1, then movie 7, flags 0, then the menu returns to window 5 |
| MV4 | teleport process 0x4D3460 (the [change of place](../architecture/change-of-place.md) CP3) | the request's first movie (flag 0x1, number in the request) and second movie (flag 0x40), flags 0, default track; skipped in a network host game. The new game's request (flag word 0x45) carries movies 1 and 7. |
| MV5 | script action 103 (`fade`, operands: seconds, sound, movie, a flag) in 0x447220 case 0x67; the whole action (fade, time advance and movie) is skipped in a network host game, like MV4 | fade-out, time advance when seconds are positive, then **movie by number** (the `sound` operand, when positive, is the audio track id; when the movie is 0 the same operand plays a sound instead), then the last operand picks how the screen is brought back (not read further) |
| MV6 | script action 102 (`fade_and_teleport`) | passes its movie operand to the teleport request as the first movie (CP9) |

The shared steps of the player (the 0x40DEE0 wrapper and 0x531530 itself):

1. The wrapper resolves the path (0x52F410), **locks the sound system** (0x41C610: scheme loops stop and new sound
   requests are refused, [audio SN10](audio-evidence.md)), calls the player, unlocks (0x41C660). Caller flags 4 / 1 / 2 / 8 become
   player flags 1 (not skippable) / 2 (clear before) / 4 (leave the screen blank after) / 8 (do not restore the display); the shipped
   callers use only 0 and 8, so **every movie is skippable**.
2. The player takes the DirectDraw surface type, passes the audio track (only when the caller gave one,
   `BinkSetSoundTrack`), opens the file (`BinkOpen`), clears the screen, then loops: `BinkWait`, `BinkDoFrame`,
   `BinkCopyToBuffer` into the display surface (0x5317D0, which restores a lost surface), `BinkNextFrame`, and pumps
   the message queue. **A key press ends the movie** (the event kind read is 5 with its down flag; that a mouse button also maps to it
   was not checked); the player then waits for the key release, closes the movie and
   restores the screen. It returns a small error code on failure (a null path, an unusable surface type, an open
   failure); every failure just returns, nothing hangs or aborts. The pause and time clocks stand still for the whole
   movie ([TL25](../architecture/original-time-loop.md)).
3. Only the Bink calls `BinkOpen`, `BinkClose`, `BinkSetSoundTrack`, `BinkDDSurfaceType`, `BinkWait`,
   `BinkDoFrame`, `BinkCopyToBuffer`, `BinkNextFrame` (and `BinkOpenMiles`, `BinkSetSoundSystem`: the binder 0x5314F0 is the last entry of the engine-library init table that 0x51F130 runs at start-up, so movie audio plays through the Miles sound system, which is why the sound lock around a movie matters) are imported: a plain file-in, frames-and-audio-out use.

### Scripts

Read from the 2,399 installed script files (the winners of the content stack; the attachment of each script to an
object, sector or dialog was **not** resolved here, see section 7).

- **Action 103 (`fade`), 21 lines in 13 scripts.** 11 lines in **6 scripts carry a movie** (the operand is a literal
  number in every case): script 1093 (movie 4 on track 1), 1105 (movies 2, 3, 6), 1109 (movie 4, default track),
  2862 (movies 3, 5), 2863 (movies 2, 5), 3061 (movies 2, 5). The file names of these scripts suggest story
  movies. The other 10 lines are fades without a movie.
- **Action 102 (`fade_and_teleport`), 29 lines in 14 scripts: the movie operand is 0 in every one.** Shipped
  scripts never start a movie through a teleport. The only movies of the change-of-place plan are therefore those
  of the new game start (1 and 7).
- **Maps** carry no movie field: no map list, `mapinfo.txt` or start-location file names a movie. The new game's
  movies are fixed in the exe (MV4).
- The credits and ending *slides* are not movies: the scripts 997 (end slides) and 999 (credits) contain no fade
  with a movie ([FR-MEDIA-002](../requirements/ui-media-lifecycle.md#fr-media-002), slice E7-b).
- Steamwoven today: the change-of-place plan carries the movie steps and the viewer skips them with the line
  "NOT READY: no Bink playback" ([ARC-DEV-204](../architecture/change-of-place.md)); the scripts' action 103 is not in
  any admission set (the run stops there); the menu's intro button is hidden with a reason; no start-up movie plays.

## 3. Registry rows of this reading

The rows of [`functions.csv`](../verification/functions.csv) for 0x531530, 0x5317D0 and 0x5445F0 point here at level
F2, and the wrappers 0x40DE70, 0x40DEE0 and the logo launcher 0x404810 gained rows (all `out of scope` or
`not implemented`: no method of ours implements them).

| Function | Reading |
| --- | --- |
| 0x40DE70 | movie by number: asks the module's movie table (the handle at 0x59F02C) for the number's row, builds `movies\<row>` and calls 0x40DEE0. Number not in the table: nothing happens. |
| 0x40DEE0 | movie by path: resolve, lock sound, run the player (flag map above), unlock. A path that does not resolve plays nothing and does not lock. |
| 0x404810 | the two logo movies, by fixed path, called from the game-module init. |
| 0x531530 | the player loop (steps 2 and 3 above). |
| 0x5317D0 | one frame: wait for the frame to be due, decode, copy to the surface, advance; returns 0 at the last frame. |
| 0x5445F0 | View Intro: movies 1 and 7, then back to window 5. |

## 4. Decoder options and licences

What a decoder must give (from sections 1 and 2): Bink 1 revision `i` video (up to 800×600, 15 fps, up to 162 s,
8 files plus logos); Bink audio (RDFT, 44.1 kHz stereo) with **a track selectable by id** (movie 4); frames in
step with the audio; a key skip; a failed open that leaves the game running.

| Option | Licence and obligations | What it takes | Verdict |
| --- | --- | --- | --- |
| **O1. FFmpeg as an external program** (`ffmpeg` found on the player's machine, run as a child process: raw frames on one pipe, 16-bit PCM of the chosen audio track on another) | FFmpeg's Bink demuxer and decoder are LGPL-2.1-or-later (headers read, W). A separate program the player installs (Homebrew, a Windows build) is not linked; Steamwoven ships no FFmpeg code. A build with `--enable-gpl` is GPL as a whole, which does not matter for a separate process. | A locator (`./arcanum setup` records an optional input: path, version, build line), a movie service in the viewer (spawn, read frames into a texture, PCM into a stream player, kill on skip), a no-FFmpeg fallback. Frame size, frame rate and the track are all read from the header or from `ffprobe`. | **Recommended first.** Cheapest, no ABI coupling, licence-clean, nothing derived lands on disk. |
| **O2. FFmpeg libraries loaded at run time** (`libavformat`, `libavcodec`, `libswscale`, `libswresample` through P/Invoke) | LGPL-2.1+ and dynamic: allowed when the libraries stay replaceable; if a package ships them it carries their licence text and a source offer. | An in-repo binding for a pinned FFmpeg major version (the C structs change between majors) and the same viewer service as O1, in process. | Later, only if packages must carry an LGPL build (owner decision, below). |
| **O3. An own MIT decoder in C#** (Bink 1 video plus RDFT audio) | Clean-room is possible from the public format notes; **porting FFmpeg's decoder makes the port LGPL** (the ScummVM port is GPL). | Roughly 2,000 lines (an estimate) plus an FFT, test fixtures we cannot generate (no free Bink encoder exists, so the only test input is the player's own file). | Not recommended: size L, high risk, little gain over O1. |
| **O4. Transcode once to Theora/Vorbis and use Godot's own player** | Godot plays Ogg Theora natively (to be checked on the pinned 4.7.2). The transcode is a derived copy of the original video: local only, never in git or a package, like the HD pack. | The O1 locator plus an import step (a few hundred MB per transcode, lossy to lossy) and the two audio tracks of movie 4 as separate files. | Second choice to O1 if live decoding proves too slow or hard to keep in step; worse for quality and disk. |
| **O5. The original `binkw32.dll`** | proprietary RAD library, in the player's installation, not redistributable | 32-bit Windows only; cannot be loaded into the 64-bit game process. | Out. |
| **O6. No video** (stills or a fade only) | none | the movies have no stills in the data and a still would be a derived frame; the audio needs the decoder as well | The status quo. Valid only as the **fallback** when no decoder is found. |

Other notes:

- **Bink 2** (`KB2…`) is a different, later format that FFmpeg does not decode (its demuxer reports it as not
  implemented; long-standing tracker tickets, status after 2022 not confirmed). It does not matter here: all
  ten files are Bink 1 (`BIKi`).
- **Patents**: not assessed. Bink 1 dates from 1999 to 2000; FFmpeg has shipped its independent decoder since 2009.
  This is the owner's call if the question ever matters.
- **The decoder match**: FFmpeg's demuxer accepts `BIK` followed by `b f g h i k` and the decoder has branches for
  the revisions up to `k`, so `i` is covered; its audio selector picks RDFT when the DCT bit 0x1000 is clear
  and stereo from bit 0x2000, which is what these headers set; and it exposes each header audio track as its own
  stream, which is what movie 4's two tracks need.
- **A synthetic test file is impossible**: there is no free Bink 1 encoder, so a unit test cannot feed a `.bik`.
  Tests of the player drive its decoder interface with a synthetic raw-frame and PCM stream (the O1 pipe contract);
  only an opt-in real-data run uses the installed files.

## 5. What the player must reproduce

- Every movie is skippable by a key press; the game and
  the session clock stand still for its whole length (frozen frame, [TL25](../architecture/original-time-loop.md), the same
  host rule as the death slide, ARC-DEV-126).
- The sound system is locked for the length of a movie and unlocked after it
  ([`SoundDomain.Lock`/`Unlock`](../world/session-sound-runtime.md) exist; "no movie runtime calls it yet").
- A failed open or a missing file plays nothing and returns control, never a black screen or a hang.
- The audio track operand of action 103 selects a header track by id; 0 or absent means the default track.
- Display: the frame sizes (720×346, 800×400, 800×600) are drawn on a game screen of at least 800×600; the exe
  hands the frame to the display surface and the placement and scaling were not read (the viewer's own window size
  is a choice; E7-a reads 0x5317D0's destination rectangle if it must match).

## 6. Recommendation and the owner's choice

Recommended scope for #1170 (E7-a), in this order, each slice its own task:

1. **Decoder service (O1).** Optional input `ffmpeg` in `./arcanum setup`, a movie service behind an interface
   (`IMoviePlayer`: open by number or path and track, frames, PCM, skip), the missing-decoder fallback that keeps
   today's "not shown" behaviour (log line, no black screen), tests over the synthetic pipe contract.
2. **The callers that exist in the shipped data:** View Intro (movies 1 and 7), the new game's first and second
   movie steps, script action 103 (admitted with its time advance and track operand), the start-up movie 8 and the
   two logos behind a setting that defaults off in the viewer (the exe has no switch; the show condition of movie 8
   is still to read).
3. **Bundling** (O2 or shipping an LGPL FFmpeg build in the packages): only if the owner wants movies for players
   without their own FFmpeg.

Owner's choices (they are put to the owner through the coordinator, not decided here): **(a)** whether movies are
in scope at all or the fallback (O6) stays; **(b)** if in scope, O1 for the alpha (recommended) or also a bundled
LGPL build; **(c)** whether the start-up logos and movie 8 play in the viewer.

## 7. Open items and limits

- **Attachment of the six movie scripts** (to which NPC, item or sector they are attached, and so which map reaches
  them) was not resolved; E7-a resolves it with the attachment walk of the
  [script census](scr-census.md) before it admits action 103.
- **The show condition of start-up movie 8** and the placement of the frame on the surface were not read.
- **No decode was run.** The first thing E7-a does with an installed `ffmpeg` is probe all ten files
  (`ffprobe` streams, frame counts against the headers above) and decode movie 4's second track.
- The movie contents (what each shows, which script is which ending) were not looked at; the labels above come from
  file names, script names and the table comment.
