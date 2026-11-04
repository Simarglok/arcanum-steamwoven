# Unpacking the GOG offline installer on macOS and Linux

Status: **Research**, work item ARC-RES-087 (2026-11-04). Docs-only: no code changes and no
repository checks run; the measurements below ran locally on the owner's Mac (arm64) with
`innoextract` 1.9 and one read-only run of `tools/research/Arcanum.ContentProbe` over the result.
It answers how big the task is to turn the GOG offline installer (`setup_arcanum_….exe`) into a
game folder without Windows, compares the ways to do it, and says how it fits the prototype
extraction slices E3/E4 ([prototype extraction](prototype-extraction.md#slices)).

Requirements (reference only, no coverage row changes):
[FR-CONTENT-001](../requirements/content-product.md#fr-content-001),
[FR-PRODUCT-005](../requirements/content-product.md#fr-product-005).

## Why it matters

Anyone can buy Arcanum on GOG and download the offline installer. Only a Windows player can run
it. On macOS and Linux the player today needs a CrossOver or Wine bottle, a Windows PC, or the
manual `innoextract` route in [the alpha instructions](../player/alpha-build.md). The goal here is
a launcher step that goes from "I have the installer" to "the launcher finds a complete game
folder" without a Windows installer run.

## What the installer is

Measured on the file the owner downloaded (`setup_arcanum_-_of_steamworks_and_magick_obscura_1.0.7.4_ddrawfix_(77721).exe`,
1,168,741,624 bytes, SHA-256 `6658219d…ae4e`):

| Fact | Value |
| --- | --- |
| Format | Inno Setup, setup data version **5.6.2, Unicode**, one language (`en-US`), **not password protected** |
| Parts | **One file.** No extra `.bin` slices; the whole 1.1 GiB of game data is inside the `.exe` (older or larger GOG titles do ship `-1.bin` parts, this one does not) |
| GOG data | game ID 1207658829; `goggame-1207658829.info`, `.script` (registry actions only: Sierra and Troika keys, all Windows-only), `.hashdb`, `.ico` |
| Contents | 197 listed entries: about 74 files of game and GOG metadata, about 120 installer temp images (`tmp/…jpg`, `slideshow.ini`) and `commonappdata/…/uninstall.dll` |
| Game files | `Arcanum.exe` (SHA-256 `9bd80511…8d52`, the 1.0.7.4 fingerprint the project already uses), `arcanum1–4.dat`, `tig.dat`, `modules/Arcanum.dat`, `Arcanum.PATCH0`, `Vormantown.dat`, movies, music, the `ddrawfix` Windows files (`ddraw.dll`, `DDrawCompat.ini`), manual and readme |
| Not in the installer | `data/proto` (all 1,423 prototype files), `arcanum.cfg`, saves: the original writes them at its first start ([PX1](prototype-extraction.md#how-the-original-builds-a-prototype)) |

## Does a known tool unpack it fully

Yes. `innoextract` 1.9 (Homebrew bottle, supports Inno Setup 1.2.10 to 6.0.5) did all of it:

- `innoextract -t` (verify checksums only) passed with exit code 0.
- `innoextract -q -d DIR setup.exe` wrote 197 files in **about 4 seconds** from an SSD (the
  compression method was not inspected).
- `innoextract --gog -m -d DIR setup.exe` (the command in the alpha instructions, plus
  `-m` which leaves out the temp images) wrote **74 files, 1.1 GB**. `Arcanum.exe` lands in the
  folder root; the `app/` subfolder holds only an icon and `webcache.zip`, so the instruction's
  "if the files land in an `app` subfolder" is not needed for this installer.
- One name pair is a collision by case (`DwarvenMusic.mp3` and `CombatMusic.MP3` are one stored
  file with two names); both names are written, no data is lost, and the folder works on a
  case-insensitive macOS volume.

### Comparison with the CrossOver installation

Compared read-only against the existing GOG installation inside a CrossOver bottle (`…/drive_c/GOG
Games/Arcanum`, installed on 2026-10-18), by SHA-256, leaving out the `._*` AppleDouble files that
the external volume adds:

- **All 70 files the two have in common are byte-identical, except `Eula.txt`** (the installer
  stores a 15.5 KiB file; the installation holds a 55.9 KiB one that the Windows installer wrote at
  install time, source not traced; a licence text, not game data). This covers every `.dat`, the module archive and its patch,
  `Arcanum.exe`, `tig.dat`, the movies and the music.
- Only in the unpacked tree: `__redist/ISI/scriptinterpreter.exe`, `app/`, `commonappdata/…` and
  the installer temp images; none is read by the game or by Steamwoven.
- Only in the CrossOver installation, and so written by the Windows installer or by the original
  game: `unins000.*`, the shortcut, two icons, `goglog.ini`, `arcanum.cfg`, `modules/Arcanum/Save/`,
  **`data/proto` with 1,423 files**, and 81 more files under `modules/Arcanum/maps` with names
  that look text-mangled (not examined; created by the original's first start, not by the
  installer).

### Does Steamwoven read the unpacked tree

`tools/research/Arcanum.ContentProbe --game-root <unpacked folder>` (through `./arcanum run --kind
dotnet`) exited 0 with no error line and listed the full content stack: module folder,
`arcanum.patch0`, `arcanum.dat`, base archives 4 to 1, the game folder and `tig.dat`; 94,597 keys,
109 overridden paths. So the content stack needs nothing from the Windows installer beyond the
files `innoextract` writes. This is not a start of the viewer or the map plan; the missing
`data/proto` still blocks the first map ([E3/E4](#fit-with-e3-and-e4)).

## Options

Sizes are rough developer days of one focused slice, level F0 (estimates, not measured).

| Option | What it is | Size | Licence and cost | Main risks |
| --- | --- | --- | --- | --- |
| **A. Call `innoextract`** | The launcher finds `innoextract` (PATH, Homebrew locations), runs `--gog -m -d TARGET setup.exe`, shows progress from its output and takes the folder with `Arcanum.exe`. If it is missing, the launcher shows the one install command. | **S: 1–2 days** (process start, progress, error and disk checks, tests with a stub tool, launcher text) | zlib licence (Homebrew lists `Zlib`): free to call and to redistribute. Needs the player to install it. | A step the player must do first; Linux package names vary |
| **A+. Bundle `innoextract`** | As A, plus a static universal macOS build (and a Linux x64 one) shipped inside the app. The player then needs nothing. | **M: +3–5 days** on top of A: reproducible static build against Boost and xz in CI, signing and notarising the nested binary (the packaging task does not sign yet), a third-party notice file | zlib: allows redistribution with the notice | Build and signing work; updating when Inno Setup 6.x installers appear |
| **B. Own C# reader in the launcher** | An Inno Setup 5.6.2 reader: setup header and data tables, chunk reader, LZMA or zlib, the call-instruction filter, checksums, file location table | **L: 2–3 weeks** for this one version family, with tests; more for other versions | No dependency; the format is documented by `innoextract`'s own zlib-licensed source | Largest cost and risk, no gain for the player; it needs a synthetic Inno installer as a test fixture (no original content in git) and breaks on a newer GOG re-pack until extended |
| **C. Guided manual step** | The launcher shows the two commands; the player runs them in a terminal | **XS: hours** (it is [already written](../player/alpha-build.md)) | None | A non-technical buyer cannot do it; no progress, no verification |

## Recommendation

**Do option A first, and make it A+ at packaging time.** Write it as one launcher slice: pick the
installer file, call `innoextract` (found on PATH or in the Homebrew locations; bundled copy first
once A+ lands), unpack into a folder the launcher owns, check the unpacked `Arcanum.exe` against the
known fingerprint, and hand the folder to the same source selection a CrossOver or Windows folder
goes through. Reasons: the tool is free, permissive, stable (1.9), fully correct on this very
installer (checksums verified, bytes equal to a real installation), and 4 seconds of work; an own
reader (B) costs weeks and gains nothing the player can see. Keep C as the fallback text when the
tool is missing. Do not write B unless GOG moves to an Inno version `innoextract` does not support.

Extra rules for the slice:

- Never write inside the installer's folder; unpack into a new empty folder, refuse a folder that
  is not empty, and check the free space first (about 1.1 GB for the folder plus the installer's
  own 1.2 GB if it must be copied).
- Pass the installer path as an argument, never through a shell string; show the tool's output in
  the run log, not game bytes in git.
- Verify with `-t` before writing, and treat a non-zero exit as a failed result with the first
  error line, as the launcher does elsewhere.
- The `.exe` may become a set (`-1.bin`) for other GOG titles; here it does not, and the slice
  need only handle the single file, with a clear refusal when `innoextract` reports missing parts.

## Fit with E3 and E4

An unpacked tree is exactly the "installation the original never ran" that
[prototype extraction](prototype-extraction.md#the-problem-and-the-owners-decision) describes: it
has everything but `data/proto`, and on macOS the original cannot be started to write it. So the
installer route is not complete on its own; it feeds the existing slices:

1. **Unpack** (this research's slice): installer → game folder. The `Arcanum.exe` it writes is the
   same 1.0.7.4 file (`9bd80511…8d52`) that E3 targets, so E3's fingerprint gate accepts it.
2. **E3 (ARC-DEV-068)** builds the 1,423 prototype files from that `Arcanum.exe` by interpretation.
3. **E4 (ARC-DEV-069)** adds the cache and the launcher's first-start build and progress; its
   fresh-installation path is the second half of the same player flow.

Player flow after all three: *pick installer → unpack → fingerprint check → build prototypes
(once, cached) → start*. The unpack step does not depend on E3; it can be built and tried
against a folder that still fails at `AREA_MAP_PLAN_MISSINGSOURCE`, and E3/E4 code is not touched.
The `Arcanum.exe` seen in the installer is not changed by the unpack, so there is no extra
verification debt for the executable interpreter.

## Not covered

- The viewer or the map plan on the unpacked tree (blocked by the missing `data/proto` until E3/E4).
- Other GOG installers, other languages, a Polish or Russian build, or Galaxy file parts.
- Windows and Linux machines (Linux was not run; `innoextract` is a common Linux package, the
  result is expected to be the same).
- The 81 text-mangled files in the CrossOver installation's `maps` folder.
