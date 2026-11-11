# The player's HD interface pack: storage, layout, build, update, backup (ARC-DEV-329, ARC-DEV-343, ARC-DEV-350, ARC-DEV-361, ARC-DEV-376)

One rule set for every agent (Claude, Codex) and the owner, for the **local** HD interface pack
`interface-redraw-4x`. The pack format is [`docs/formats/hd-pack.md`](../formats/hd-pack.md); the redraw
methods and results are [`docs/rendering/hd-interface-art.md`](../rendering/hd-interface-art.md). This page says
where each thing lives, which command may touch what, and how to change the installed pack without losing the
work of the other agent. The tool is [`tools/hd_pack/hd_pack.py`](../../tools/hd_pack/hd_pack.py) (standard
library only, tests `python3 -B -m unittest tools.hd_pack.test_hd_pack`).

## Hard rules

1. **No original or derived art in git**: no ART pixels, redraws, frames, sheets, screenshots, real manifests, plans
   with real file lists or receipts that name original files. Git holds this page, the tool, the importer and
   synthetic tests. A task that needs to show a result links a local path or describes it in words.
2. **Never replace the installed pack folder as a whole**, and never delete, move or re-create it. The installed
   pack holds work that no rebuild reproduces (the Codex repairs in the slices `character-hd114` and `interface-refinement02-soft`,
   `recipe-refinement02.json`, the unlisted `main-menu-16x9.png`). Change it only with `hd_pack.py apply`, `menu-art`, `relayout`, `refresh` or `rollback`.
3. **Every command that writes is run with `--dry-run` first**, the output is read, and only then the real run.
   A command given to the owner was run with `--dry-run` or `--help` first (launch-check rule).
4. **Build into staging, never into an installed pack or the iCloud copy.** Staging is
   `local/res095/pack-out/<id>/` (ignored by git). `hd_pack.py stage` refuses a target that is not a plain folder under a
   `local/**/pack-out/` path, and wipes (`--fresh`) only a folder that holds a pack manifest.
5. **Back up outside `hd-packs`.** The game discovers every folder inside `hd-packs/` as a pack; a backup there is
   a second pack. Backups live in `~/Library/Application Support/Arcanum Steamwoven Game/hd-pack-backups/`.
6. **One writer at a time.** `apply`, `menu-art`, `relayout`, `refresh`, `index`, `rollback` and `mirror` take the lock `hd-packs/.hd-pack.lock` next to
   the installed pack (a file there is not a pack); a lock of a dead process is taken over, one held by a live process
   means wait. Do not start a second agent on the same pack: check with `status` and the backup folder first.
7. If something is missing or the same failure repeats twice, stop and ask the owner with options. Do not edit a
   manifest by hand and do not copy files into the installed pack with `cp`/`rsync --delete`/Finder.

## Where things live

| What | Where | In git | Notes |
| --- | --- | --- | --- |
| Code, format, this page, synthetic tests | repository | yes | `tools/hd-interface-import`, `tools/hd_pack`, `docs/` |
| Original art frames, ChatGPT sheets and results, fit/pack outputs, plans, handoffs | `local/res094/`, `local/res095/` (group folders, `ready/`, `character_repair/`, `topbar*/`, `combat119/`, `arc-ui-*/`) | **no** (`/local/` is ignored) | the work area; the pipeline scripts are in git: [`tools/hd_pack/res095/`](../../tools/hd_pack/res095/README.md) (`fit_res095.py`, `pack_res095.py`, `build_res095.py`; ARC-DEV-341) and read and write only the main checkout's `local/res094`, `local/res095` |
| Staging build | `local/res095/pack-out/interface-redraw-4x/` | no | rebuilt from `ready/plan.json`; may be wiped by `stage --fresh` |
| **Installed pack the packaged game reads** (since ARC-DEV-350 only when the iCloud copy is not used: the *HD pack* setting [chooses](#hd-pack-source-arc-dev-350); the viewer of `./arcanum area` reads another folder, [below](#which-main-menu-png-the-game-loads-arc-dev-343)) | `~/Library/Application Support/Arcanum Steamwoven Game/hd-packs/interface-redraw-4x/` | no | `manifest.json`, `recipe*.json`, `layout-map.tsv`, `INDEX.md`, `index.tsv`, `interface/<screen>/<kind>/` (the [layout](#pack-layout-arc-dev-376); before ARC-DEV-376 the folders `interface/`, `character-hd114/`, `refinement02/`), `main-menu-16x9.png` (outside the manifest) |
| Traced interface fonts (ARC-DEV-355) | `local/hd-font/out/<art>-<name>/` (frames, `font.json`, `receipt.json`) | **no** | local 4× coverage glyph frames from the player's own fonts, written by [`tools/hd_font`](hd-font.md); built into a **separate staging pack** `local/res095/pack-out/interface-fonts/` and added to the installed pack only by `apply --add-new` ([below](#traced-fonts-in-the-pack-arc-dev-361)), never by a rebuild of the pack |
| Backups of every change | `~/Library/Application Support/Arcanum Steamwoven Game/hd-pack-backups/<TASK>-<old manifest sha12>/` | no | the old manifest, the replaced files, `RECEIPT.json` |
| iCloud copy (ARC-DEV-349) | `~/Library/Mobile Documents/iCloud~md~obsidian/Documents/Arcanum Steamwoven/hd_pack/` (reached as the one symlink `local/hd-packs`; the folder keeps its own name `hd_pack`, the pack is `hd_pack/interface-redraw-4x`) | no | cross-device store inside the owner's Obsidian vault; a **mirror of the installed pack**, never a build target ([below](#icloud-copy-arc-dev-349)). It replaced the Google Drive copy (`~/Library/CloudStorage/GoogleDrive-<account>/My Drive/Steamwoven/hd_pack/`), which stays untouched, is no longer updated and is deleted only with the owner's say-so |
| Cold sources: the player's own installation | `/Applications/Arcanum/modules/Arcanum.dat` | no | the importer reads it, nothing writes it |

`local/hd-packs` is **not a local folder**: it is a symlink into the Obsidian vault in iCloud. A path under it reaches the
iCloud folder, so a write there syncs to the owner's other devices and to the vault, and macOS may keep a file online-only
(*Optimize Mac Storage*): hashing a placeholder downloads it, and a sync still running looks like a short or missing file.
Unlike Google Drive (`Operation not permitted` for the Claude app, a Remote Control session and a Codex worktree), the iCloud
folder was readable and writable from a Remote Control session when it was set up (ARC-DEV-349); if a path answers
`Operation not permitted` anyway, that is a permission, not a broken pack: run the command from a terminal that has the access,
or use the installed folder as the source of truth.

## Pack layout (ARC-DEV-376)

The reader puts no meaning on a path ([format](../formats/hd-pack.md#pack-directory)), so the layout is a convention of this tool. Since
ARC-DEV-376 a pack that holds a `layout-map.tsv` at its root is laid out by it:

```text
<pack>/
  manifest.json  recipe-*.json  main-menu-16x9.png      as before (the menu PNG and recipes stay unlisted at the root)
  layout-map.tsv       art, screen, kind, folder: where every ART of the pack lives (the file the owner edits)
  INDEX.md             screens > kinds > ARTs with frame counts and sizes (generated, for reading)
  index.tsv            one line per frame: screen, kind, art, frame, path, Legacy and HD size, slice, alpha, review, sha12 (generated)
  interface/<screen>/<kind>/<art>-f<NNN>.png        one frame of an ART
  interface/<screen>/<kind>/<art>/f<NNN>.png        an ART whose map row has folder = 1 (many frames: bitmap fonts, animations)
```

`<screen>` is the window or area where the art is drawn (`character-sheet`, `inventory`, `hud-bar`, `logbook`, `world-map`, `schematics`, `menus`,
`widgets` for the buttons shared by several windows, `fonts`, `cursors-indicators` and so on); `<kind>` is what it is (`buttons`, `icons`, `tabs`,
`gauges`, `panels` for windows, plates and everything else, `spells`, `schematics`, `fonts`, `cursors`, `indicators`). `-g<G>` and `-p<P>` follow the
frame number when the group or palette is not 0. The file name has **no content hash**, so an edited PNG keeps its name; `refresh` (below) brings
the manifest up to date. The folders `character-hd114` and `refinement02` of the earlier layout are gone: where a frame came from stays in the
manifest's `slice`, and the layout does not depend on it. The map, the index and the layout stay local; no art name list is in git.

- **`relayout`** moves every entry to its place by the map (also the first migration and any later change of the map). `--map FILE` gives the map
  (columns `art screen kind folder`; a `frames` column in place of `folder` means folder = more than 8 frames; further columns are ignored), by default the
  pack's own `layout-map.tsv` is used. It hard-links (or copies) each file to its new path, writes the map, the index and the manifest, then removes the
  old paths and the folders they leave empty; the PNG bytes and so every frame hash stay. Backup `<TASK>-<old manifest sha12>/` holds the old manifest and a
  receipt with every move, `rollback <that folder>` puts everything back byte for byte. It refuses a pack that is already damaged (deep check), an ART
  the map lacks, two entries on one path and a target that is an unlisted file. Run `--dry-run` first, with the game closed, on the installed pack
  and then on the iCloud copy (`--installed <copy> --allow-symlink`), then `mirror --dry-run` must show nothing to copy.
- **`index`** rewrites `INDEX.md` and `index.tsv` from the manifest (`apply`, `relayout` and `refresh` do it themselves).
- **`refresh`** takes PNGs you edited in place into the manifest: every listed file whose size or SHA-256 changed gets the new values after a header
  check (8-bit RGBA, not interlaced, exactly the entry's HD size). The backup keeps only the old manifest; the edited PNG is the new truth. Whether the
  alpha passes the entry's rule is still the game's check at draw time (a refused frame draws Legacy and is logged).
- **`status`** counts entries per `interface/<screen>`.
- **`plan` and `apply` on a laid-out pack:** a replaced entry stays at its installed path, a new one (`--add-new`) is placed by the map, so the
  staging build keeps its importer file names (`interface/<art>-f<N>-<hash>.png`) and is never laid out itself. An ART the map lacks stops the update
  with its name: add its row to `layout-map.tsv`, or pass `--map FILE` with the missing rows, which `apply` merges into the pack's map (a rollback restores the old map).
  What may be replaced is chosen by the entry's `slice` (`interface` by default, `--slices character-hd114,interface-refinement02-soft` for the Codex
  repairs) instead of its folder (`--folders` is only for a pack still in the old layout).
- **The map file** is the owner's: `relayout` without `--map` and `apply` never rewrite it (`apply --map` only appends rows for ARTs the pack's map lacks, the pack's
  own rows win); `relayout --map FILE` writes its normalised four-column copy into the pack. A `refresh` cannot be rolled back (the edited PNGs are the only
  copy); an interrupted `relayout` or `rollback` is finished by running `rollback <backup>` again. The three-way `--base` check is unchanged and still protects a local repair.

## Optimized textures (ARC-DEV-376)

The PNGs stay the pack's only content: you edit a PNG, `refresh` it into the manifest, and the game does the rest. For the draw path the game keeps a
**device-local cache** of ready textures, `user://hd-cache/<pack id>/<sha[0..2]>/<sha256 of the PNG>.mip` (in the packaged game
`~/Library/Application Support/Arcanum Steamwoven Game/hd-cache/interface-redraw-4x/`). It is outside the pack, so the pack stays portable and neither
`mirror` nor iCloud carries it, and it can be deleted at any time (the next start rebuilds it).

- **What an entry is:** the decoded 4× RGBA8 frame with its whole mip chain (Godot's own `Image.GenerateMipmaps`, so the texture is the one the draw
  built itself before), about 4/3 of the raw frame (about 2 GB for the whole interface pack). Lossless: no VRAM compression in this version.
- **When it is made:** a few seconds after an HD pack is attached with the *HD interface* setting on, a background worker (two threads, below normal
  priority) converts every interface PNG of the pack that has no entry yet; it logs one line `ARC-DEV-376 hd cache pack=... entries=... present=...
  converted=... failed=... removed=... size=...MiB ms=...`. Entries are named by the PNG's hash, so an edited PNG (after `refresh`) has no entry and is
  converted again, and after every finished pass the entries no PNG of the pack names any more are removed (a PNG that fails to convert does not stop that).
  The worker threads are stopped when the game exits. An automated run (smoke, self-test) reads the cache but
  never converts; `ARC_HD_CACHE=0` turns the cache off (read and write).
- **What the draw saves:** with an entry the frame is bound without reading the PNG, hashing it, decoding it or generating mipmaps; the pack's own checks
  (key, Legacy frame, review marks, size, alpha rule) still run on the cached level 0 (`HdPack.CheckPrepared`), and the pack file must still exist with the
  manifest's byte size (one stat; it is not read, so a PNG edited without `refresh` is noticed by `verify`, not by this draw). A frame without an entry, a damaged entry
  and the premultiplied and grey variants (bitmap-font canvases, grey spell icons) take the earlier path, so the cache can never make a frame fail.
- Format and code: `HdOptimizedCache` (Godot-free store, header `ARCM` version 1, format 1) and `HdOptimizedStore` (viewer).

## Daily tasks

All commands run from the checkout root; add `--help` to any subcommand for its arguments.

### What is installed

```sh
python3 -B tools/hd_pack/hd_pack.py status            # manifest sha, entries per folder, unlisted files, size check
python3 -B tools/hd_pack/hd_pack.py verify --deep     # every manifest file with its SHA-256 (about 700 MB, a few seconds)
```

`status` and `verify` exit 1 on a missing file, a wrong size/hash or a symbolic link inside the pack (the game
refuses links inside a pack). Record the printed manifest sha in the task report.

### Build a staging pack (whole pipeline, rare)

The sheet fit and stitching scripts ([`tools/hd_pack/res095`](../../tools/hd_pack/res095/README.md), ARC-DEV-341) and the importer are in git; the data they use is local:

```sh
local/res094/venv/bin/python tools/hd_pack/res095/fit_res095.py --jobs 12      # only when sheets or results changed
local/res094/venv/bin/python tools/hd_pack/res095/pack_res095.py               # writes ready/plan.json and ready/frames
python3 -B tools/hd_pack/hd_pack.py stage --plan local/res095/ready/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/res095/ready/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh
```

`stage` runs the importer through `./arcanum run --kind dotnet` (the queue and its quiet log apply). **Do not run
`local/res095/tools/rebuild_hd.sh`**: it deletes its target folder, and its target is now the iCloud link. Do not
point `hd-interface-import --pack` at the installed pack or the iCloud copy either: the importer rewrites the
manifest of an existing pack as a new identity and has not been verified against the Codex extras.

The importer's lock is on by default; a plan item with `"lock": false` skips the shading/colour lock
([format](../formats/hd-pack.md#interface-art-arc-dev-242), ARC-UI-111/117). `fit_res095.py`'s edge guard mixes the
original back in near the cell border and the alpha silhouette (on one checked sheet it touched 1% to 97% of a frame's opaque texels), so a `lock:false` redraw must be fitted without it:
pass `--no-guard` (or set `"lock": false` on the sheet in its group manifest); the default stays guard on. `fit_log/<sheet>.json`
records `guard` per sheet and `guarded` per cell (0.0 when skipped). Still check the frame that reached the pack (RGB/alpha
along the outline, anchors, states), not only the fit.

### Update the installed pack (diff-only)

The update replaces only the `interface/` entries whose content differs from the staging build, matched by the
portable key (art hash, group, frame, palette). Entries and files of the other folders (`character-hd114`,
`refinement02`) and every file outside the manifest stay.

**The base (three-way check).** The installed pack is the staging build **plus local repairs** (Combat centring, IntTop,
the left buttons, Phantasm, slots), and a repair often keeps the file name. A staging build rebuilt from the old
plan therefore differs from the installed pack exactly where a repair was made, and a plain "replace what differs" would
revert it (on 2026-11-10 all 16 differing entries were such repairs). `plan` and `apply` take `--base`, the manifest the
staging line was last synchronised with, and replace an entry only when the installed file is still the base's.
An entry whose installed file is not the base's is a **CONFLICT**: the update is refused (`--conflicts refuse`, the
default), or applied without those entries (`--conflicts skip`), or reverting them on purpose (`--conflicts overwrite`).
Without `--base`, `plan` prints a warning and `apply` refuses (`--no-base-check` only when the installed entries are
known untouched). Where the base comes from: every `apply` writes `staged-manifest.json` into its backup folder, which is
the `--base` of the next update from the same staging line; for the first one use the manifest of the oldest backup before
the repairs (`hd-pack-backups/ARC-UI-114-a480266bf0ca/manifest.json`) or the manifest of the staging build the pack was
installed from. The oldest backup is the manifest before the first update, so entries that first update changed show up
as CONFLICT too (safe, only noisy; use the latest `staged-manifest.json` once there is one). The `staged-manifest.json`
of a *repair* apply (below) is a copy of the installed pack and must not become the base of the main staging line.

```sh
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-redraw-4x --base BASE
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-redraw-4x --base BASE --task ARC-UI-NNN --dry-run
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-redraw-4x --base BASE --task ARC-UI-NNN
python3 -B tools/hd_pack/hd_pack.py verify --deep
```

BASE is a manifest.json or a pack folder. To land a **repair** (a frame changed on its own), do not rebuild the whole pack:
make a staging folder that holds the repaired PNG and a copy of the installed manifest in which only that entry's file,
bytes and SHA-256 changed, and apply it with `--base <the installed pack>`, so the diff is exactly the repaired entries.
A repair of an entry that lives in an extra folder (`character-hd114`, `refinement02`: the Codex repairs) is invisible to `plan`
and `apply` by default, since only `interface/` entries are replaceable; name the folder with `--folders character-hd114` (a
comma list, plain folder names only), keep the repaired file in the same folder, and the plan then lists exactly those entries
(ARC-DEV-345; the other folders and every unlisted file still stay). In a [laid-out pack](#pack-layout-arc-dev-376) the folder is not the Codex marker any
more: name the entry's slice with `--slices` and keep the repaired file at the entry's installed path in the staging folder.

Read the plan: no CONFLICT line you did not expect, and the number of replaced entries must match what the task changed (a handful for a repair, never
hundreds by accident). `apply` checks every staged file against its manifest entry before touching anything, backs
up the old manifest and old files, copies the new files, writes the manifest **last**, removes an old file only when
no manifest entry still names it, then re-checks that nothing outside the plan changed (size and mtime of every other
file) and the hashes of what it wrote. It refuses a pack that is already damaged, and a target file name that exists in
the pack but is not in its manifest (move such a file away first). A staged-only entry is ignored unless `--add-new`. After it: restart the
game or viewer, and look at the changed frames in the game (the owner or an L2 native check, not the tool, decides
that the art is right).

### Replace the main-menu art

`main-menu-16x9.png` sits at the pack root outside the manifest, so `apply` never touches it and the importer never
copies it ([contract](../formats/hd-pack.md#local-widescreen-main-menu-art)). Install it separately:

```sh
python3 -B tools/hd_pack/hd_pack.py menu-art --image PATH/main-menu-16x9.png --task ARC-UI-NNN --dry-run
python3 -B tools/hd_pack/hd_pack.py menu-art --image PATH/main-menu-16x9.png --task ARC-UI-NNN
```

The header must be 8-bit RGB/RGBA, not interlaced, at most 3840 x 2160 and 40 MiB (opacity and registration stay the
viewer's check). Keep the chosen source next to its prompt under `local/`, not in git.

### Back up and roll back

Every `apply` writes `<backup-root>/<TASK>-<old manifest sha12>/` (and `menu-art` `<TASK>-menu-<old menu sha12>/`) with
the old manifest, the replaced files and `RECEIPT.json` (hashes and file names, no pixels in the receipt). Rolling back:

```sh
python3 -B tools/hd_pack/hd_pack.py rollback ~/Library/"Application Support"/"Arcanum Steamwoven Game"/hd-pack-backups/ARC-UI-NNN-<sha12> --dry-run
python3 -B tools/hd_pack/hd_pack.py rollback <the same folder>
```

`rollback` restores the manifest last and refuses when the installed manifest is no longer the one that update
produced (a later update followed): roll back newest first. An `apply` that was interrupted after copying files (receipt
`failed`/`started`, manifest still the old one) is rolled back the same way; the tool prints the command. The older backups
(`ARC-UI-114-…` .. `ARC-UI-120-…`) are whole-pack copies of about 680 MB each (some 6.8 GB together) with no receipt:
restore from one only by hand and only with the owner. Do not delete backups to save space; the owner decides
(`./arcanum clean` covers `artifacts/` only). New backups are diff-only and small.

### iCloud copy (ARC-DEV-349)

The iCloud copy lets the owner carry the pack to another device. It follows the installed pack, one way, and `mirror --dst`
defaults to it (`~/Library/Mobile Documents/iCloud~md~obsidian/Documents/Arcanum Steamwoven/hd_pack/interface-redraw-4x`):

```sh
python3 -B tools/hd_pack/hd_pack.py mirror --dry-run
python3 -B tools/hd_pack/hd_pack.py mirror
```

The first copy (2026-11-10) was made from the installed pack, not from the Google Drive folder (a Claude session cannot read it):
1145 files, `verify --deep` on the copy PASS, manifest 478fbb920b80. Anything else the Drive folder holds was not moved; the owner
copies it by hand if wanted.

`mirror` refuses a damaged source, a destination inside the game's folder or holding symbolic links, copies missing or differing files (a listed file is compared by the manifests' hash and size, so cloud files
are not re-read; `--deep` hashes every file in both folders), writes `manifest.json` last, and **never deletes**: files only in the destination are
listed and kept. After it, `status --installed <the iCloud copy> --allow-symlink`
shows the same manifest sha as the installed pack. Whether a second device has synced is a separate check on that
device. Importing the copy on another device is `HdPackImport` (the format page), never a copy over a pack.

### Which main-menu PNG the game loads (ARC-DEV-343)

The menu picture is the file `main-menu-16x9.png` at the **root of the one active HD pack**; nothing else in the pack or
elsewhere is a menu picture. Read in the code (`AreaMenuArtView.WidescreenBackground`, `HdPack.ReadMainMenuBackdropReport`,
`TerrainViewerApp.ConfigureVisualProfile`), checked on the owner's Mac on 2026-11-10:

1. **It is used only when all of these hold:** the window is the main menu, the *HD interface* game setting is on
   (`hdInterface` in `game_settings_v1.json`, independent of the Legacy/HD world profile), an HD pack is active and not
   `rejected`, and the file is an opaque 16:9 PNG of at most 3840 x 2160. Anything else leaves the original menu art from
   the game data, without a message. An automated run (smoke, self-test) looks for no pack folder and opens one only when `ARC_HD_PACK` names it, so a smoke shows the HD menu only with that variable.
2. **The active pack** is `ARC_HD_PACK` when set; otherwise the one the **HD pack** setting of the options window chooses
   ([below](#hd-pack-source-arc-dev-350), ARC-DEV-350): by default (Auto) the iCloud pack folder when it is on this device and the pack in it
   is whole, else the first pack that opens and is not `rejected` in the collection `user://hd-packs/` (the installed copy), then, **only for a
   run of the Godot editor binary** (`Godot --path`, feature `editor`), the checkout's `local/hd-packs/` (the same iCloud folder by its symlink).
   Before ARC-DEV-350 the game's own folder won over the iCloud copy and the packaged game never read the iCloud folder.
3. **`user://` is not the same folder for the packaged game and for the source viewer.** The packaged game (feature
   `steamwoven_package`) uses `~/Library/Application Support/Arcanum Steamwoven Game/`: there `hd-packs/interface-redraw-4x/`
   (the pack this page maintains) holds the menu PNG, and `game_settings_v1.json` says `hdInterface: true`. The viewer that
   `./arcanum area` and `map` start (an export that is "not a packaged build") uses
   `~/Library/Application Support/Godot/app_userdata/Arcanum Terrain Viewer/`: on 2026-11-10 its `hd-packs/` held only the old
   sprite pack `crash-site-full-4x` (no menu file) and `hd-packs-alt/` two older packs, and it has no `game_settings_v1.json`,
   so *HD interface* is off there and the original menu art shows even though the installed pack has a new PNG. To see the
   installed PNG in that viewer, start it with `ARC_HD_PACK` set to the installed pack and turn the *HD interface* option on.
   Do not delete or edit those old folders to "fix" it; the owner decides. The two real runs of the packaged game on
   2026-11-09/10 (`logs/godot*.log` in the game folder) logged `pack=interface-redraw-4x ... pack-source=1 of 1 found` and
   `interface HD enabled=True`, so that is where the menu PNG came from, but the log of those builds did not name the file.
4. **The log names the file** (added by ARC-DEV-343; the log has no path, only the outcome): when the main menu first draws
   with the HD interface on, the viewer prints once per pack

   ```
   ARC-DEV-343 main menu backdrop file=main-menu-16x9.png status=Used sha12=<12 hex> size=1664x936 pack=<id> manifest-sha12=<12 hex> pack-from=game-folder
   ```

   `status` is `Used`, or why it was refused (`NoFile` also covers an unreadable, symbolic-link, empty or oversized file, `PackRejected`, `BadHeader`, `WrongSize`, `BadImage`, `NotOpaque`);
   `pack-from` is `game-folder` when the pack lies inside `user://hd-packs/` (also when `ARC_HD_PACK` points there) and `other-folder` otherwise. Compare `sha12` with the first
   12 digits of `shasum -a 256 <pack>/main-menu-16x9.png`; the `menu-art` backup receipt names the same hashes. The older
   startup lines `ARC-RENDER-014 visual profile=... pack=<id> ...` and `ARC-UI-006 ... pack-source=...` say which pack opened.

### HD pack source (ARC-DEV-350)

The game chooses its HD pack at startup from the **HD pack** line of the options window (Game page, after *HD interface*; main menu and in-play
menu), saved as `hdPack` (`auto`, `game`, `custom`) and `hdPackPath` in `game_settings_v1.json` (file version 4; older files read as Auto). A change
applies at the **next start**, and the window's note says at once what that start will read:

| Setting | Chain (first pack that opens, is not `rejected` and is whole wins) |
| --- | --- |
| **Auto** (default) | the iCloud pack folder (`~/Library/Mobile Documents/iCloud~md~obsidian/Documents/Arcanum Steamwoven/hd_pack`, **macOS only and only when that folder exists**, so Windows and other devices simply have no default), then the game folder `user://hd-packs/`, then (editor binary only) the checkout's `local/hd-packs/` (the same folder by its symlink, counted once) |
| **Game** | the game folder `user://hd-packs/` alone: the installed copy |
| **Custom** | the folder chosen in the system folder dialog (a folder that holds `manifest.json` is the pack itself, any other is a folder of packs), then the game folder; entering Custom always opens the dialog, cancelling keeps the saved folder |

*Whole* is a stat of every manifest file, the recipe and `main-menu-16x9.png` (when the folder has it): the file is there, has the manifest's byte
size, and is **not online-only**. The manifest and every `*.json` of the pack folder are checked the same way **before** the pack is opened (opening
reads and hashes them), so an evicted manifest is skipped without a download; every pack of a folder is tried, not only the first. A file the cloud provider keeps only in the cloud (macOS *Optimize Mac Storage*: `dataless` in `ls -lO`, read once
per folder, or an old `.name.icloud` placeholder) counts as online-only, because reading it would make the game wait for a download in the middle of a
frame. In a cloud folder (`Mobile Documents`, `CloudStorage`) a download state that cannot be read counts as not whole (fail closed); in a local
folder it counts as here. Apart from the manifest and the recipe of a pack that passed the stat, nothing is hashed or read at startup (a frame is
still checked against its manifest hash when it is drawn; `hd_pack.py verify --deep` is the full check). A pack that is not whole is skipped with its reason (`3 of 1145 files are online-only in the cloud folder`, `N files are missing`, `N files
have another size than the manifest (a sync may still be running)`, a manifest that does not open) and the next source is tried, so a half-synced
iCloud copy never costs the player the installed one. When no source is whole, a pack that is incomplete but entirely on this device is still used
(missing frames draw Legacy); one with online-only files never is (no pack, HD draws Legacy).

The startup logs one line with no path: `ARC-DEV-350 hd pack source mode=auto chosen=iCloud copy found=1 files=1146 local=1146 online-only=0 missing=0
wrong-size=0 skipped=[...]`, and the HUD's pack line says the source (`iCloud copy, interface-redraw-4x`) and any fallback. An automated run (smoke,
self-test) still searches no pack folder; `ARC_HD_PACK` still overrides everything.

**Fallback to Legacy graphics and the problem box.** When the player asked for HD (the HD graphics profile or the *HD interface* setting is on)
and the pack cannot be used, the run draws the original (Legacy) graphics: no pack found or none whole (every candidate's reason is listed), the
pack named by `ARC_HD_PACK` did not open, or the pack is marked rejected. The settings are not changed, so a repaired pack brings HD back at the
next start. The reason goes to the log (`ARC-DEV-350 hd pack problem: ...`, no path) and to a box over the menu with **Continue** (goes on with
Legacy graphics; Enter and Escape do the same) and **Close game** (ends the program; the log says which was pressed, `ARC-DEV-350 hd pack
problem box: ...`). A pack that is incomplete but entirely on this device is still used and the box says that its missing frames draw Legacy. A
skipped iCloud copy whose installed fallback is whole is not an error: it is in the log line, the HUD and the options note only. An automated
run opens no box.

### The second device and the iCloud copy (ARC-DEV-343, ARC-DEV-349)

What `mirror` does for the menu PNG (code and `test_hd_pack.py`, not a live cloud run): the file is not in the manifest, but
`mirror` copies **every** file of the source pack except `.DS_Store`, so `main-menu-16x9.png` and `recipe-refinement02.json`
travel with it; an unlisted file is compared by SHA-256 on every run (a listed one by the manifest's hash and size), so a
`menu-art` change is noticed and copied. Run `mirror` after every `apply` and every `menu-art`, or the iCloud copy keeps the old picture.

The owner confirmed (2026-11-10) that the two symlinks that then existed, `local/hd_pack` and `local/hd-packs`, led to the **same**
Google Drive folder; ARC-DEV-346 removed `local/hd_pack`, so `local/hd-packs` is the only name, and ARC-DEV-349 (the owner's decision,
2026-11-10: Drive is inconvenient, a Claude session has no access to it) repointed it to the iCloud folder
`~/Library/Mobile Documents/iCloud~md~obsidian/Documents/Arcanum Steamwoven/hd_pack`. `local/hd-packs` is that folder itself: the
pack collection an editor-binary run scans is its `hd_pack/`, every folder in it counts as a pack (a backup or a second copy put there
is a second pack), and the mirror target is `hd_pack/interface-redraw-4x` inside it.

What the iCloud copy does on another device:

- A **packaged game** there reads, since ARC-DEV-350, the iCloud copy when the HD pack setting is Auto, the folder exists on that device and the pack in
  it is whole (see [HD pack source](#hd-pack-source-arc-dev-350)); otherwise its own `Arcanum Steamwoven Game/hd-packs/`. On Windows or without the
  iCloud folder it reads only its own folder, or the folder a Custom setting names.
- An **editor-binary run** there (`Godot --path`, rare) reads the checkout's `local/hd-packs/` (the iCloud symlink) in place, menu PNG
  included, but only when its own `user://hd-packs/` has no usable pack (item 2 above) and *HD interface* is on.
- **There is no command that installs the iCloud copy as a pack.** `HdPackImport` (the format page) is a library function used by
  tests only; it copies the manifest, one recipe and the listed entries and **drops every unlisted file, so it never brings
  `main-menu-16x9.png`**. `hd_pack.py mirror` refuses a destination inside the game's folder. The ways left are a hand copy of the
  whole `hd_pack/interface-redraw-4x` into an empty `hd-packs/` of the second device (a first install only; then `status` and `verify --deep`
  there, and never over an existing pack), or `menu-art --image <iCloud copy>/main-menu-16x9.png` on that device for the picture alone,
  once the sprites are in place. A follow-up task would add a checked install command; until then do not tell the owner the
  second device "syncs through iCloud" without naming which of these is meant.
- iCloud may keep files online-only; hashing a placeholder downloads it, and a sync still running looks like a short or missing file.
  Whether the second device has finished is read there with `status --installed <its folder> --allow-symlink` (same manifest sha as
  the installed pack) and `verify --deep`.

The iCloud copy was verified from a Remote Control session after the first copy (`status`, `verify --deep`). Whether another device has
synced it is checked on that device.

## Traced fonts in the pack (ARC-DEV-361)

The 4× coverage frames of the interface fonts ([`tools/hd_font`](hd-font.md), ARC-DEV-355) go into the pack as `interface` entries in
the `masked` alpha mode, the format [described here](../formats/hd-pack.md#interface-art-arc-dev-242) (no format change). The font entries are **new** keys of the
installed pack (the font ARTs are in no interface redraw plan), so the update is `apply --add-new` and replaces nothing. Run all from the checkout root:

```sh
python3 -B tools/hd_pack/hd_pack.py fonts-plan --fonts local/hd-font/out --out local/hd-font/fonts-plan.json --dry-run   # reads every frame, checks its hash, prints the counts
python3 -B tools/hd_pack/hd_pack.py fonts-plan --fonts local/hd-font/out --out local/hd-font/fonts-plan.json
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-font/fonts-plan.json --staging local/res095/pack-out/interface-fonts --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-font/fonts-plan.json --staging local/res095/pack-out/interface-fonts --fresh
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-fonts --base INSTALLED --add-new
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-fonts --base INSTALLED --add-new --task ARC-XXX-NNN --dry-run
```

A worktree has its own empty `local/`: give the commands the **main checkout's** `local/` paths. `INSTALLED` is the installed pack folder (the base
for "nothing here is replaced"): the plan must say `0 interface entries differ ... staged-only entries: N (added)`; any replaced entry or CONFLICT is
not a font change, stop and look. The real `apply` (then `verify --deep`, then `mirror`) is the owner's to allow, with the game closed, as for any pack change.

- `fonts-plan` takes only grey frames with traced ink: the control-character placeholders, a glyph whose trace vanished and the coloured frame 0
  of a font are left out and draw their Legacy frame. A frame whose PNG no longer matches the SHA-256 in `font.json` stops the command (trace the font again).
  The plan is a local file under `local/` (it names original ART files), never in git.
- The staging pack holds only the fonts (id `interface-fonts`). `ARC_HD_PACK=<that folder>` with the *HD interface* option on shows the HD text with every other
  frame Legacy, which is the way to look at the fonts without touching the installed pack.
- The importer checks each frame against the `masked` rule (no ink farther than one Legacy texel from the Legacy glyph) and refuses the whole build (nothing is
  written) when one frame fails. On 2026-11-11 the five fonts of the first trace (arts 27, 171, 229, 327, 840) built as 1113 entries in one run (a local observation).
- `fonts-plan` overwrites only a plan it wrote (every item a `coverage` item), never the pipeline's `ready/plan.json`, and writes only under a checkout's `local/`.
- Rolling back is `rollback` of the `apply` backup, as for any update.

## Producing and repairing art (who does what)

Generation is the owner's (or Codex's, in the owner's account): no Claude thread has an image model, API or local model
(ARC-RES-053), and a thread must not drive the owner's ChatGPT account without an explicit request. What a thread can do is
everything before and after generation. The facts below are from the owner-local handoffs in `local/res095/` and the
merged PRs #872-#887; they are the working method, not a format.

| Step | Who | How |
| --- | --- | --- |
| Extract originals, build sheets, prompts and briefs | thread (scripts) | `tools/hd_pack/res095/build_res095.py` (in git; venv `local/res094/venv`) and `docs_res095.py` (owner-local): 815 ARTs, 8,734 frames, 204 sheets in 20 groups; one sheet per request, frames as RGBA in grey cells, 1x and 4x copies |
| Generate a sheet | **owner / Codex** | one request per sheet with the sheet, `common/style_refs/ref_wood.png`, `ref_brass_frame_grid.png` and a style anchor, the sheet's `prompts/<sheet>.txt` and the STYLE LOCK block of `common/STYLE_GUIDE_RES094.md` (wood about #382c25, brass about #9b7a41, black about #020202; no invented ornaments; any output size). Moderation refusals and quota stops are the owner's; the blocked sheets are listed in `COLLECTION_STATUS.md` |
| Accept or reject | **owner** | by eye, 30 seconds per file, checklist in the style guide section 4; an imperfect result may be kept as final (owner decision 2026-11-08); a direction (the thin closed-slot outline, the full redrawn main menu) is the owner's choice |
| Save the result | owner or thread | `groups/<group>/results/<sheet>.png` plus a line in `results/SOURCE.txt` (prompt, date, tool); keep the failed attempts |
| Register, stitch, QA, plan | thread | `tools/hd_pack/res095/fit_res095.py` (scale and offset by NCC of blurred luma, cell cut, edge guard, skipped with `--no-guard` or a sheet's `"lock": false`), `pack_res095.py` (same folder; stitching, `ready/qa.json`, `ready/plan.json`, `ready/aliases.json`; frames the fit could not register are left out and listed) |
| Spot repairs | thread, owner confirms taste | alpha masks and `ellipse` for round buttons, overrides on top of the common fit, a shift of one logical pixel, darkening of closed slots, states derived from frame 0 plus the Legacy difference, `"lock": false` for frames that keep the redraw's tone; each repair keeps a `diagnosis.png` and a `HANDOFF.md` under `local/` |
| Import into staging | thread | `hd_pack.py stage` ([above](#build-a-staging-pack-whole-pipeline-rare)) |
| Install | thread, **after the owner allows it and with the game closed** | `plan`, `apply --dry-run`, `apply` with a base, `verify --deep` |
| Show the result in the game | thread | native check below; the owner judges the look |

A new main-menu picture is made as a full-scene outpaint of a 1920 x 1080 guide with side bands masked, then cropped to
1664 x 936 without touching the remaining pixels; a soft composite with the original pasted in the middle was rejected by
the owner (2026-11-10). Keep guide, mask, prompt and result under `local/`.

## Diagnosing a graphics defect

Reproduce on a clean `origin/main` build, then separate the layers before changing anything:

1. Take the original (Legacy), the current HD and a small pilot at the same scale, and save the layers separately
   (the plate under a button, each state frame) as `diagnosis.png`, before and after in one scale.
2. Follow one frame down the chain: result of the generation, fit/guard output, ready frame, plan item, written PNG and
   manifest entry in the **installed** pack, `ResolveSprite` (does it resolve to the HD frame and not fall back), and last
   the native composition. A fit score (NCC) does not catch icon, alpha or composition errors; check RGB and alpha along the
   outline, anchors, states and cancel.
3. Known causes so far: HD frames drawn through the premultiplied font path added RGB from transparent texels (#878, #881:
   premultiply before mipmaps); the shading lock pulled a redraw back to the original's tone (#872, #882: `"lock": false`);
   a button drew no Down frame for a click shorter than a frame (#883, #884, #886: minimum Down time 80 ms from first draw);
   a Legacy alpha that kept a plate's corners round a redrawn button (#875: alpha mask).
   The world map window drew HD frames on premultiplied canvases with straight-alpha textures, which baked a rectangle behind two
   buttons (ARC-UI-126: every texture on a premultiplied canvas is the premultiplied variant).
4. Native evidence: `./arcanum smoke pr --stages bar --main-menu` (or the stage the window belongs to) with
   `ARC_HD_PACK=<pack dir>`, `ARC_AREA_BAR_HD_VERIFY=1` and `ARC_AREA_BAR_CAPTURE=<dir>` for the readback; a capture that
   needs a temporary patch is made in a separate checkout and the patch is removed afterwards. Captures are original-derived
   and stay under `local/`.
5. Compare the installed pack before and after an install file by file: only the intended PNGs and the manifest differ.

Roles: the owner decides direction and acceptance and runs generation; Codex and Claude threads diagnose, repair locally,
stage, install with the owner's permission and verify. The independent review of an HD diff is a fresh subagent as for any
PR; the owner's 2026-11-08 instruction for ARC-UI-114 to use only Codex sub-agents applied to that task.

## Known hazards

- **Stale staging reverts repairs.** See the base paragraph above; never `apply --no-base-check` to the real pack
  with a staging build older than the last repair.

- **A repaint over a Legacy hole is rejected silently.** The alpha check allows an HD frame to be opaque only within one Legacy texel of the Legacy
  silhouette (also for `"alpha": "masked"`). A frame that fills a Legacy-clear area installs and verifies (the manifest and hashes are fine) but fails
  `AlphaMismatch` when the game opens it, and the game draws that frame as Legacy. Look at a native shot of the frame after every install
  (ARC-UI-126, 2026-11-11, rolled back).

- **iCloud symlink** (`local/hd-packs`, the only name; the old second link `local/hd_pack` was removed by ARC-DEV-346 and nothing may name it): see above; the tool refuses a symlinked installed pack
  unless `--allow-symlink`, and refuses any symlink inside a pack.
- **Superseded local scripts.** `local/res095/tools/rebuild_hd.sh` (now stubbed to refuse), `apply_update.sh`, `merge_update.py` and `full_hd.sh`
  are replaced by `stage`, `plan` and `apply`; they stay in `local/` as history only.
- **Unlisted files.** `main-menu-16x9.png` and recipe files outside the entries are not copied by the importer or by
  `HdPackImport`; a whole-folder replace silently loses them.
- **Same file name, new content.** A local repair may keep a file's name while its content changes (the installed
  `combat_button-f0-…` differs from the same-named staged file by hash), so the name suffix is not the content hash.
  `apply` and `mirror` compare manifest hashes (`apply` overwrites and keeps the old copy in the backup); a copy or sync by name and size
  would not notice.
- **Two agents.** Codex repairs (the slices `character-hd114` and `interface-refinement02-soft`) are in the installed pack only. Read
  `status` before writing and name the manifest sha you started from in the report.
- **Button frame shifted against the plate baked behind it.** A redrawn round button (`SkilMinusBut` 625 on the character
  sheet's skills page) can keep its RGB a few HD pixels off the ring that is baked into the page's plate, and a sheet gutter
  can stay as a 2-pixel grey column at its edge (ARC-DEV-345). Measure the ring centre of the frame against the plate at the
  runtime position (`AreaCharacterSheetPagesLayout`), then re-register the RGB only: the Legacy-smooth alpha stays, so the
  slice and the alpha policy do not change. Replace the three state frames together, with the same shift.
- **Plans and receipts are local.** A real plan lists original file names; keep it in `local/`, and keep
  `hd-pack-backups` receipts out of the repository.

## Checklist for a thread that changes the pack

1. `status` and `verify --deep`: record the manifest sha before.
2. Build or repair into staging (or prepare the menu PNG); never into the installed pack.
3. `plan` and `apply --dry-run` with `--base`: the replaced entries are exactly the task's and there is no unexpected CONFLICT (a dry run that finds conflicts prints the plan and exits 2).
4. `apply` (or `menu-art`); `verify --deep`; the new manifest sha and the backup folder name go into the report.
5. Start the game/viewer, check the changed frames once, and (for a rendering task) the task's smoke stage.
6. `mirror --dry-run`, then `mirror` to the iCloud copy when the owner uses it.
7. Handoff: what was changed, the backup folder, the manifest sha before and after, what stays local.
