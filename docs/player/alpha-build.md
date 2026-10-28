# Arcanum Steamwoven alpha: install and first start

This is the instruction for a player of the packaged alpha build (ARC-DEV-047, Windows ARC-DEV-048). The build is a test build of an unfinished
reimplementation: it opens the launcher and the original main menu, and much of the game is not there yet
([component catalogue](../project/status.md)). It contains **no game content**: it reads the files of your own copy of
*Arcanum: Of Steamworks and Magick Obscura* (GOG release 1.0.7.4) at run time and never writes into that folder.

Platforms: **macOS** (Apple silicon and Intel, one universal `.app`) and **Windows 11 x64** (a folder with
`Arcanum Steamwoven.exe`, delivered as one zip).

## 1. Get the build

Both builds are made on the developer's Mac, `./arcanum package mac` and `./arcanum package win`
([packaging](../workflow/packaging.md)), and land in the project checkout:

    artifacts/packages/macos/Arcanum Steamwoven.app
    artifacts/packages/windows/Arcanum Steamwoven Windows x64.zip
    artifacts/packages/<macos|windows>/BUILD.txt   (the commit and date it was built from)
    artifacts/packages/<macos|windows>/README.md   (this instruction)

**macOS:** copy `Arcanum Steamwoven.app` wherever you like (for example `/Applications`). It is a self-contained app;
nothing else is installed.

**Windows:** copy the zip to the PC (a USB stick, a shared folder or a file transfer). Section 3 says how to unpack and start
it. The folder in it is self-contained too (the .NET runtime is inside); nothing is installed.

**Which build is this?** The launcher and the main menu show a line in the bottom right corner:
`Arcanum Steamwoven alpha · main 1a2b3c4d5e · 2026-10-28` — the first ten digits of the commit of `main` it was built from
and that commit's date. `BUILD.txt` has the full commit. A build started from the source instead says
`not a packaged build`.

## 2. First start on macOS: "Open Anyway"

The app is signed **ad-hoc** (no paid Apple developer account, no notarization). What happens on the first start depends
on how the app reached the Mac:

- **Built on this Mac, or copied from a local folder or disk:** it opens with a double click.
- **Downloaded, received by AirDrop, a messenger or mail** (macOS marks such files as from the internet): macOS refuses
  the first start with a message that it cannot verify the app or its developer. Then:
  1. Double-click the app once and close the message with **Done** (not "Move to Bin").
  2. Open **System Settings → Privacy & Security** and scroll down to **Security**. There is a line saying that
     "Arcanum Steamwoven" was blocked, with an **Open Anyway** button. Press it and confirm with your password or Touch ID.
  3. Start the app again and confirm in the last dialog (**Open** or **Open Anyway**). From then on it opens normally.
     The button in System Settings is shown for a while after the blocked start; if it is gone, start the app once more.

  On macOS 15 and later the old "right-click → Open" shortcut no longer skips this check; use System Settings as above.
  (Advanced, same effect: `xattr -dr com.apple.quarantine "/Applications/Arcanum Steamwoven.app"` in Terminal.)

These steps follow Apple's documentation; the build itself was started on the developer's Mac without the download mark.

## 3. First start on Windows 11: unzip, then "Run anyway"

The Windows build is **not signed** (a code-signing certificate costs money), so Windows does not know its publisher.

1. **Unpack the zip; do not start the game from inside it.** Right-click `Arcanum Steamwoven Windows x64.zip` →
   **Extract All…** and pick a folder you can write to, for example `C:\Games` or your Documents folder (not
   `C:\Program Files`, which needs administrator rights). Windows proposes a folder named after the zip, so the game
   ends up in `…\Arcanum Steamwoven Windows x64\Arcanum Steamwoven\` (remove the last part of the proposed path to skip the
   outer folder). The game folder `Arcanum Steamwoven` holds
   `Arcanum Steamwoven.exe`, `Arcanum Steamwoven.pck` and a `data_Arcanum.TerrainViewer_windows_x86_64` folder. Keep the
   three together: the `.exe` does not start without the other two beside it.
2. **Start `Arcanum Steamwoven.exe`.** If the zip came from the internet, a messenger or mail, Windows marks the files and
   **Microsoft Defender SmartScreen** stops the first start with a blue window "Windows protected your PC". Press
   **More info**, check that the app is `Arcanum Steamwoven.exe` with "Publisher: Unknown publisher", and press
   **Run anyway**. It is asked once for this file.
   (Same effect, before unpacking: right-click the zip → **Properties** → tick **Unblock** → **OK**; the unpacked files then
   carry no mark and SmartScreen does not ask.)
3. **Smart App Control.** On a PC where *Smart App Control* is on (Windows Security → App & browser control), Windows may
   block an unsigned app outright, with no "Run anyway" button. Smart App Control has no per-app exception; the build
   cannot start there unless you turn it off, which is your own decision for the whole PC.

These steps follow Microsoft's documentation; they have not yet been tried with this build on a Windows 11 PC.

## 4. Point the game at your Arcanum files

The game needs the folder of an installed GOG Arcanum: the folder that holds `Arcanum.exe`, `arcanum1.dat`,
`arcanum2.dat` and `modules/Arcanum.dat`.

**On Windows**, install Arcanum from GOG as usual (GOG Galaxy or the offline installer). The launcher looks, in this order,
at the install folder GOG records in the registry (the GOG game key of Arcanum), then at `C:\GOG Games\Arcanum` (the
offline installer's default) and `C:\Program Files (x86)\GOG Galaxy\Games\Arcanum` (GOG Galaxy's default). A game
installed elsewhere is found through the registry; if it is not, press **Choose folder…** and pick the folder that holds
`Arcanum.exe`. This detection has so far been tested only with synthetic folders, not on a Windows PC.

**On a Mac**, GOG sells Arcanum for Windows only, so use one of these:

- **A. Copy an installed game from Windows.** Install Arcanum from GOG on a Windows PC (GOG Galaxy or the offline
  installer; the default folder is `C:\GOG Games\Arcanum`) and copy that whole folder to the Mac as
  `/Applications/Arcanum`. The launcher finds it there (or in `~/Applications/Arcanum`) by itself.
- **B. CrossOver.** Install GOG Arcanum into a CrossOver bottle. The launcher looks into the bottles
  (`~/Library/Application Support/CrossOver/Bottles/*/drive_c/GOG Games/Arcanum`) by itself.
- **C. Unpack the GOG offline installer** (`setup_arcanum_….exe` from your GOG library) with
  [innoextract](https://constexpr.org/innoextract/): `brew install innoextract`, then
  `innoextract --gog -d ~/Games/Arcanum setup_arcanum_….exe`, and choose the folder that holds `Arcanum.exe` (if the
  files land in an `app` subfolder, that subfolder). This route has not been tried in the project yet.
- **Other wrappers** (Whisky, a Wineskin-style `.app`): choose the wrapper or its game folder with **Choose folder…**;
  the launcher looks inside a chosen `.app` for its `drive_c`.

On the first start the **launcher** screen opens before the main menu:

1. It shows the folder it found, or says that none was found. To use another one, press **Choose folder…** and pick the
   game folder. A folder that is not a game is refused with the reason in words (for example which file is missing).
2. Choose the **rules**: *Steamwoven* (the original game with the recorded fixes) or *Original* (the retail 1.0.7.4 rules,
   bugs included). They can be switched later in play under Options.
3. Press **Play**. The folder and the rules are remembered; the next start goes straight to the main menu.

To open the launcher again, **hold Shift** while the game starts. **Reset settings** on the launcher forgets the stored
folder and rules.

## 5. Where your settings and saves live

The build keeps its own folder, separate from any developer build:

    macOS:    ~/Library/Application Support/Arcanum Steamwoven Game/
    Windows:  %APPDATA%\Arcanum Steamwoven Game\      (C:\Users\<you>\AppData\Roaming\Arcanum Steamwoven Game)

It holds the launcher settings (`launcher_v1.json`), the options, the saves (`saves/`) and Godot's log of the last runs
(`logs/godot.log`), and the bug reports that could not be written beside the game (`reports/`, section 7). Deleting the folder starts
the game as on its first run. The game folder of Arcanum is only read.

## 6. Updating

The build does not update itself. A newer build is made from the current `main` with `./arcanum package mac` or
`./arcanum package win` on the developer's Mac; it replaces the app or the zip in `artifacts/packages/macos/` or
`artifacts/packages/windows/` (the one before moves to `previous/` there). On macOS copy the new app over the old one; on
Windows delete the old `Arcanum Steamwoven` folder and unpack the new zip in its place (do not unpack over it, or files of
the old build may stay). Settings and saves stay in the folder above. A newer build loads the saves of older builds (from the first packaged
build on); an older build refuses a save made by a newer one.

## 7. Reporting a bug (Ctrl+B)

Press **Ctrl+B** (the Control key, also on a Mac) whenever something looks wrong: in play, in the main menu, over any open
window, in either rules mode (*Original* or *Steamwoven*). No debug mode is needed. The game keeps the moment of the key
press first, then pauses and opens a small window with one text field:

- Write what you did and what happened, then press **Send** (or Ctrl+Enter). **Cancel** (or Escape) drops the report and
  nothing is written.
- After Send the window says where the report went; **Close** (or Escape) returns to the game.

The game always keeps the last 120 seconds of its log and of its own events (map, cell, combat and window changes, message
log entries, timed events) in memory, so a report holds the time before the key press. Nothing is uploaded; the report
stays on this computer.

**Where the report goes.** Next to where the game was started from, in a `reports` folder:

    macOS:    the folder that holds Arcanum Steamwoven.app, for example /Applications/reports/2026-10-28_17-43-30/
    Windows:  the game folder beside Arcanum Steamwoven.exe, for example C:\Games\Arcanum Steamwoven\reports\2026-10-28_17-43-30\

The folder is named after the local date and time of the key press (`_2`, `_3`… for a second report in the same second). If
that place cannot be written (a protected Windows folder such as `C:\Program Files`, a read-only disk, or a Mac app started
from a downloaded copy that macOS runs from a hidden read-only place), the report goes to the user folder of section 5
instead, `Arcanum Steamwoven Game/reports/YYYY-MM-DD_HH-MM-SS/`, and the window says so and why. (On a Mac, moving the app
to another folder once, or removing its quarantine mark as in section 2, lets reports go beside it.) A build started from
the source writes into its own user folder's `reports`.

**What the folder holds.**

| File | Content |
| --- | --- |
| `report.md` | Your text; the build (commit and date); the time; the rules mode; the map; the hero's position (map, cell, facing, sector); the combat state (combat mode, turn-based round); the open windows; the game time; the platform. It also lists what is missing and why (for example no save in the main menu before a game). |
| `log.txt` | The last 120 seconds before the key press, one line each: UTC time, seconds before the key, `log`, `error` or `event`, text. |
| `screenshot.png` | The screen as it was when the key was pressed, before the report window opened. |
| `save/slot0000.json` (with `slot0000.meta.json` and `slot0000.png`) | The game saved at that moment, exactly as F7 saves it, as a save of the ordinary save list. There is no save in the main menu before a game, and none in the moments F7 refuses (a map change or a cast in progress); `report.md` then says why. |

The folder contains pictures and texts of your own copy of Arcanum. Keep it to yourself and the developers; never publish it
or put it into the project's repository.

**Loading the report's save.** Copy the three `slot0000.*` files into the save folder of section 5 (`saves/map/` in the
packaged game), renaming all three to a slot number that is not taken there (for example `slot0042.json`,
`slot0042.meta.json`, `slot0042.png`), and choose it in **Load Game**. A save copied as `slot0000` over an existing
`slot0000` would replace that save.

**Asking an agent to read a report.** Give the agent the folder's full path and ask, for example:

> Read the bug report in `/Applications/reports/2026-10-28_17-43-30/`: `report.md` first, then `screenshot.png`, then
> `log.txt` from its end. Tell me what went wrong, which component it belongs to, and whether the save reproduces it.

The agent reads `report.md` for the player's words and the game state, looks at the screenshot, and reads `log.txt`
backwards from the key press (the `event` lines show what the game did, the `error` lines what failed). To reproduce, it
loads the save as above, or reads it with the save list in code (`GameSaveLibrary` on the report's `save` folder). It keeps
the report's contents (game texts, pictures, the save) out of commits, as for any original game content.

Not built yet: an upload or a GitHub issue from the window, a report from the launcher screen (before the main menu), and a
true pause of what runs on the wall clock while any window is open (NPC goal runs and flights in progress keep going under
the report window, as under the game's other windows; the screenshot, log and save are taken before it opens).
