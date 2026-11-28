# The start-up loading picture (ARC-UI-3501)

Issue [#3501](https://github.com/Simarglok/arcanum-steamwoven/issues/3501). What the original 1.0.7.4 executable shows
between the two logo movies and movie 8, read in the Ghidra exports (decompiled C, the listing for the unsigned division,
the retry limit, the stored value and the window rectangle) at level F2; nothing was run in the oracle. The start-up order
around it is in [Bink movies](bink-movies.md#2-where-the-movies-are-called).

## Ledger

| Id | Where | Fact |
| --- | --- | --- |
| SL1 | game-module init 0x4020F0, 0x404830 | The picture is drawn right after the two logo movies (0x404810), in the game only (not the world editor). The subsystem init loop, the UI init, the default-module load and the UI data load follow, then movie 8 and the main menu; nothing between presents the window again, so the picture stays through the loading. It is drawn at no other time. |
| SL2 | 0x404830, 0x52F760, 0x530FD0 | The files are the matches of `art\splash\*.bmp` over every source mounted at that point, a name once, in the order of their names without letter case. The installed game has three, `splash1.bmp` to `splash3.bmp` (all in `arcanum2.dat`). With no file nothing is drawn and nothing is written. |
| SL3 | 0x404830, 0x438C80, 0x438D10, 0x438CE0 | The choice is not random. The configuration line `splash` (default 0) is read as an integer; the file is index `(splash + i) mod count`, unsigned, for `i` = 0, 1, 2: the next file is tried when one cannot be drawn. Then `splash` takes the index of the last file tried plus one, not wrapped. |
| SL4 | 0x4024D0, 0x438C20 | The configuration is written to disk only at the game's exit, so a run that ends without it shows the same picture next time. |
| SL5 | 0x404830, 0x5239D0, 0x557810, 0x51EA10 | The bitmap is copied, without scaling, fade or drawn text, into the 800 × 400 play-view window at (0, 41) of the 800 × 600 screen, at its left edge; a bottom-up file is centred in the window's height, a top-down one starts at its top. Only uncompressed 8- or 24-bit bitmaps that fit are taken. The rest of the screen stays the black the movie player left. The text "Loading Arcanum..." is part of each picture. (With `-fullscreen` the window is the whole screen; the shipped default is not.) |
| SL6 | 0x404830, 0x401000 | No minimum time, no wait and no key: movie 8 replaces it when the loading ends. No command-line switch or configuration line turns it off. |

SL5 agrees pixel for pixel with the owner's three screenshots of the original (rows 41 to 440, everything else black;
mean channel difference under 1).

## In Steamwoven

`Arcanum.World.AreaStartupPicture` follows SL2, SL3 and SL5; the viewer puts the picture on the movie screen between the
second logo and movie 8 ([terrain viewer](../rendering/terrain-viewer.md)) and writes `splash` into its own
`arcanum.cfg` when it closes (SL4). Differences:

- **Time (SL6).** This build has loaded before the first logo, so the picture stays a fixed 3 seconds
  (`ViewerStartupMovies.PictureMilliseconds`) instead of the loading time; keys do not end it.
- **Skipping.** The project's own option that skips the start-up movies
  ([ARC-UI-3453](https://github.com/Simarglok/arcanum-steamwoven/issues/3453)) skips the picture too, and `splash` then
  stays.
- **HD.** With HD interface art on, the pack's 16:9 redraw of the same picture is drawn instead
  ([HD pack](../formats/hd-pack.md#start-up-loading-pictures)).

## Registry rows

0x404830 is in the [function registry](../verification/functions.csv).
