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
(`logs/godot.log`, useful for a bug report). Deleting the folder starts
the game as on its first run. The game folder of Arcanum is only read.

## 6. Updating

The build does not update itself. A newer build is made from the current `main` with `./arcanum package mac` or
`./arcanum package win` on the developer's Mac; it replaces the app or the zip in `artifacts/packages/macos/` or
`artifacts/packages/windows/` (the one before moves to `previous/` there). On macOS copy the new app over the old one; on
Windows delete the old `Arcanum Steamwoven` folder and unpack the new zip in its place (do not unpack over it, or files of
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
around that area and puts you on the nearest walkable spot, together with the companions that follow you. A teleport takes
no game time and does not mark the area as discovered; areas you have really discovered stay marked in yellow. Normal
travel on the world map works as without the debug mode.

**Saves:** the debug mode does not change the rules or what a save holds, but a save made while it is on has `[DEBUG]`
before its location in the save's details (the line under the list in the save and load windows), so you can tell it apart. Such a save loads normally with the debug mode off.
