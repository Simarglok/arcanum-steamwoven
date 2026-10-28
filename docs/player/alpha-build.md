# Arcanum Steamwoven alpha: install and first start

This is the instruction for a player of the packaged alpha build (ARC-DEV-047). The build is a test build of an unfinished
reimplementation: it opens the launcher and the original main menu, and much of the game is not there yet
([component catalogue](../project/status.md)). It contains **no game content**: it reads the files of your own copy of
*Arcanum: Of Steamworks and Magick Obscura* (GOG release 1.0.7.4) at run time and never writes into that folder.

Platforms: **macOS** (Apple silicon and Intel, one universal `.app`). The Windows 11 x64 build is a separate, later step.

## 1. Get the build

The build is made on the developer's Mac with `./arcanum package mac` ([packaging](../workflow/packaging.md)) and lands in
the project checkout:

    artifacts/packages/macos/Arcanum Steamwoven.app
    artifacts/packages/macos/BUILD.txt      (the commit and date it was built from)
    artifacts/packages/macos/README.md      (this instruction)

Copy `Arcanum Steamwoven.app` wherever you like (for example `/Applications`). It is a self-contained app; nothing else is
installed.

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

## 3. Point the game at your Arcanum files

The game needs the folder of an installed GOG Arcanum: the folder that holds `Arcanum.exe`, `arcanum1.dat`,
`arcanum2.dat` and `modules/Arcanum.dat`. GOG sells Arcanum for Windows only, so on a Mac use one of these:

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

## 4. Where your settings and saves live

The build keeps its own folder, separate from any developer build:

    ~/Library/Application Support/Arcanum Steamwoven Game/

It holds the launcher settings (`launcher_v1.json`), the options, the saves (`saves/`) and Godot's log of the last runs
(`logs/godot.log`, useful for a bug report). Deleting the folder starts
the game as on its first run. The game folder of Arcanum is only read.

## 5. Updating

The build does not update itself. A newer build is made from the current `main` with `./arcanum package mac` on the
developer's Mac; it replaces `artifacts/packages/macos/Arcanum Steamwoven.app` (the one before moves to
`artifacts/packages/macos/previous/`). Copy the new app over the old one. Settings and saves stay in the folder above; an
older build may refuse a save made by a newer one.
