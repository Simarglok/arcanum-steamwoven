# Arcanum Steamwoven alpha: install and first start

This is the instruction for a player of the packaged alpha build (ARC-DEV-047, Windows ARC-DEV-048). The build is a test build of an unfinished
reimplementation: it opens the launcher and the original main menu, and much of the game is not there yet
([component catalogue](../project/status.md)). It contains **no game content**: it reads the files of your own copy of
*Arcanum: Of Steamworks and Magick Obscura* (GOG release 1.0.7.4) at run time and never writes into that folder.

Platforms: **macOS** (Apple silicon and Intel, one universal `.app`) and **Windows 11 x64** (a folder with
`Arcanum Steamwoven.exe`, delivered as one zip).

Every key and mouse control of the game, grouped by purpose, is in the repository:
[Hotkeys](https://github.com/Simarglok/arcanum-steamwoven/blob/main/docs/player/hotkeys.md) (a web link, as this instruction is also copied
beside the build). The sections below name only the keys of their own steps.

## 1. Get the build

Both builds are made on the developer's Mac with `./arcanum package mac` and `./arcanum package win`, from `main` or from
a chosen commit. How to build them, where they land and which commit they come from is in the repository:
[Packaged builds](https://github.com/Simarglok/arcanum-steamwoven/blob/main/docs/workflow/packaging.md)
(a web link, as this instruction is also copied beside the build as its `README.md`, next to `BUILD.txt`).

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
- **C. Unpack the GOG offline installer** (`setup_arcanum_….exe` from your GOG library). The launcher does it for you:
  install [innoextract](https://constexpr.org/innoextract/) (`brew install innoextract`), press **Install from GOG
  installer…** on the launcher screen, pick the installer and then a new or empty folder; the launcher checks the installer,
  unpacks it, checks that `Arcanum.exe` is the 1.0.7.4 one and chooses the folder (Play stores it). By hand, the same is
  `innoextract --gog -m -d ~/Games/Arcanum setup_arcanum_….exe`, then choose the folder that holds `Arcanum.exe` (for the
  1.0.7.4 installer the files land in the folder root, not in `app`). Tried on 2026-11-04 on the 1.0.7.4 installer
  ([ARC-RES-087](../workflow/packaging.md)): the files unpack in seconds with `Arcanum.exe` in the folder
  root, game files byte-identical to a real installation. Such a folder has no `data/proto` (the original writes it at its own
  first start, which cannot run on a Mac): the first **Play** builds those files from your own `Arcanum.exe` (about half a minute,
  see below). The map's other first-start files are not built yet, so the first map may still refuse on such a folder.
- **Other wrappers** (Whisky, a Wineskin-style `.app`): choose the wrapper or its game folder with **Choose folder…**;
  the launcher looks inside a chosen `.app` for its `drive_c`.

On the first start the **launcher** screen opens before the main menu:

1. It shows the folder it found, or says that none was found. To use another one, press **Choose folder…** and pick the
   game folder. A folder that is not a game is refused with the reason in words (for example which file is missing).
2. Choose the **rules**: *Steamwoven* (the original game with the recorded fixes) or *Original* (the retail 1.0.7.4 rules,
   bugs included). They can be switched later in play under Options.
3. Press **Play**. The folder and the rules are remembered; the next start goes straight to the main menu. On a folder that
   the original game never started (for example one unpacked from the GOG installer) the first Play also prepares the game's
   prototype files from your own `Arcanum.exe`: the screen shows the progress (about half a minute, with a **Cancel** button)
   and keeps the result in the settings folder below, so later starts are not delayed. This needs the 1.0.7.4 `Arcanum.exe`;
   with another executable the screen says so. Your game folder is only read.

To open the launcher again, **hold Shift** while the game starts. **Reset settings** on the launcher forgets the stored
folder and rules. The launcher also has the **HD** (Off/On: the HD interface art and fonts and the relit night lighting), **HD pack**
and experimental **Generated interface** rows for an HD pack you have built or copied yourself (they are not in the
Options window); each choice is saved at once, and the pack source applies from the next start.

## 5. Where your settings and saves live

The build keeps its own folder, separate from any developer build:

    macOS:    ~/Library/Application Support/Arcanum Steamwoven Game/
    Windows:  %APPDATA%\Arcanum Steamwoven Game\      (C:\Users\<you>\AppData\Roaming\Arcanum Steamwoven Game)

It holds your settings (`arcanum.cfg`: the launcher settings, the options, Always Run, the volumes and the HD choices), the game data prepared from your `Arcanum.exe` (`prototypes/`, built once; it is
rebuilt by itself when the executable or the game's data change), the options, the saves (`saves/`) and Godot's log of the last runs
(`logs/godot.log`), and the bug reports that could not be written beside the game (`reports/`, section 9). Deleting the folder starts
the game as on its first run. The game folder of Arcanum is only read.

## 6. Updating

The build does not update itself. A newer build is made on the developer's Mac as section 1 says. On macOS
copy the new app over the old one; on Windows delete the old `Arcanum Steamwoven` folder and unpack the new zip in its place (do not unpack over it, or files of
the old build may stay). Settings and saves stay in the folder above. A newer build loads the saves of older builds (from the first packaged
build on); an older build refuses a save made by a newer one.

## 7. Debug mode

The debug mode is a testing aid for the alpha, **not part of the game**. It is off unless you turn it on. While it is on,
the world map (key **W**, also inside buildings and caves) lists every area of the world, known or not, and **Teleport**
moves you to the selected area at once. Without it the world map has no area list and you travel only by clicking the map,
as in the original game.

**Turn it on:** hold **Shift** while the game starts to open the launcher, tick **Debug mode** and press **Play**. The
choice is stored, so the next starts keep it until you untick it there (or press **Reset settings**). While it is on, the
line in the bottom right corner of the main menu ends in `DEBUG MODE`.

Advanced, for one start only: start the game with the argument `-- --debug-mode`. On macOS, in Terminal:
`"/Applications/Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven" -- --debug-mode` (adjust the path to where the
app is). On Windows, in a command prompt in the game folder: `"Arcanum Steamwoven.exe" -- --debug-mode`, or put
` -- --debug-mode` after the target of a shortcut to the `.exe`.

**Use it:** press **W** in play, select an area in the list on the right and press **Teleport**. The game loads the world
around that area and puts you on a walkable spot in it, together with the companions that follow you. A teleport takes
no game time and does not mark the area as discovered; areas you have really discovered stay marked in yellow. Normal
travel on the world map works as without the debug mode. The debug mode also shows the panel of diagnostic text at the top
of the window from the start (section 8), and a magenta frame and a yellow cross at the feet of your character (where the
game places it); without the debug mode they are not drawn.

**Saves:** the debug mode does not change the rules or what a save holds, but a save made while it is on has `[DEBUG]`
before its location in the save's details (the line under the list in the save and load windows), so you can tell it apart. Such a save loads normally with the debug mode off.

## 8. Showing and hiding the diagnostic panel

The panel of diagnostic text at the top of the window (the mode, the status and the diagnostic palette button) is a
development aid, so it is hidden unless the debug mode is on (section 7). Press **Ctrl+Shift+F10** to show it and again to
hide it (on a Mac keyboard where the F keys control brightness or volume, hold **fn** as well: **fn+Ctrl+Shift+F10**). It
works with or without the debug mode and lasts until the game closes: the next start hides the panel again (or shows it,
with the debug mode). If the game folder or an area fails to load while the panel is hidden, the failure line still appears
on its own at the top of the window. The diagnostic palette keeps its own key, **Ctrl+Shift+F9**, whether the panel is shown
or not.

## 9. Reporting a bug (Ctrl+B)

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
the source writes into its own user folder's `reports`. On a Mac, an app kept in Downloads, Desktop or Documents makes
macOS ask once, on the first Send, whether Arcanum Steamwoven may use that folder (again after a new build replaces the
app); if you refuse, the report goes to the user folder as above.

**What the folder holds.**

| File | Content |
| --- | --- |
| `report.md` | Your text; the build (commit and date); the time; the rules mode; the map; the hero's position (map, cell, facing, sector); the combat state (combat mode, turn-based round); the open windows; the game time; the platform. It also lists what is missing and why (for example no save in the main menu before a game). |
| `log.txt` | The last 120 seconds before the key press, one line each: UTC time, seconds before the key, `log`, `error` or `event`, text. |
| `screenshot.png` | The screen as it was when the key was pressed, before the report window opened. |
| `save/slot0000.json` (with `slot0000.meta.json` and `slot0000.png`) | The game saved at that moment the way F7 saves it, as a save of the ordinary save list. Unlike F7 it is also taken while a window is open, but a save holds the game, not the window: an open dialog, barter or map window is closed after loading it. There is no save in the main menu before a game, and none while a map change, a cast or a spell missile is under way; `report.md` then says why. |

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

**Archiving handled reports** (ARC-DEV-056). A report is read once: when it is handled, it moves out of the way so that
nobody returns to it.

- A report counts as handled once a fix thread has started for it (it has its task code) or the owner has called it not a
  bug.
- Move the handled report's whole folder, unchanged, into `archive/` inside the same `reports` folder (for example
  `/Applications/reports/archive/2026-10-28_17-43-30/`, or the user folder's `reports/archive/` for a report that went
  there). Never delete a report, and never move one that is not handled yet.
- Append one line per report to `reports/archive/INDEX.md` (create it with the first one):
  `- <report folder name> · <date handled, YYYY-MM-DD> · <task code, or "not a bug"> · <short summary>`.
- Reports to read are the dated folders directly in `reports/`; an agent asked to read the reports skips `archive/`
  unless it is asked for an archived one. The archive holds the same original content as the reports: it never goes
  into the repository either.

Not built yet: an upload or a GitHub issue from the window, a report from the launcher screen (before the main menu), and a
true pause of what runs on the wall clock while any window is open (NPC goal runs and flights in progress keep going under
the report window, as under the game's other windows; the screenshot, log and save are taken before it opens).
