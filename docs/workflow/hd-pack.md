# The player's HD interface pack: storage, layout, build, update, backup (ARC-DEV-329, ARC-DEV-343, ARC-DEV-350, ARC-DEV-361, ARC-DEV-376)

One rule set for every agent (Claude, Codex) and the owner, for the **local** HD interface pack
`interface-redraw-4x`. The pack format is [`docs/formats/hd-pack.md`](../formats/hd-pack.md); the redraw
methods and results are [`docs/rendering/hd-interface-art.md`](../rendering/hd-interface-art.md). This page says
where each thing lives, which command may touch what, and how to change the installed pack without losing the
work of the other agent. The tool is [`tools/hd_pack/hd_pack.py`](../../tools/hd_pack/hd_pack.py) (standard
library only, tests `python3 -B -m unittest tools.hd_pack.test_hd_pack`).

## What the pack holds in the product (owner decision 2026-11-15, #2173)

The HD option is the HD interface (art and fonts, this pack) plus the relit lighting. The pack holds interface art
and fonts only: HD sprites of characters, objects and terrain are not made and are out of the plan until the owner
reopens it, and the build/import tooling ARC-DEV-003 stays deferred ([decision](../architecture/decisions.md)).

## Hard rules

1. **No original or derived art in git**: no ART pixels, redraws, frames, sheets, screenshots, real manifests, plans
   with real file lists or receipts that name original files. Git holds this page, the tool, the importer and
   synthetic tests. A task that needs to show a result links a local path or describes it in words.
2. **Never replace the installed pack folder as a whole**, and never delete, move or re-create it. The installed
   pack holds work that no rebuild reproduces (the Codex repairs in the slices `character-hd114` and `interface-refinement02-soft`,
   `recipe-refinement02.json`, an old unlisted `main-menu-16x9.png`, which the game no longer reads, ARC-UI-3003). Change it only with `hd_pack.py apply`, `relayout`, `refresh` or `rollback`.
3. **Every command that writes is run with `--dry-run` first**, the output is read, and only then the real run.
   A command given to the owner was run with `--dry-run` or `--help` first (launch-check rule).
4. **Build into staging, never into an installed pack or the iCloud copy.** Staging is
   `local/res095/pack-out/<id>/` (ignored by git). `hd_pack.py stage` refuses a target that is not a plain folder under a
   `local/**/pack-out/` path, and wipes (`--fresh`) only a folder that holds a pack manifest.
5. **Back up outside `hd-packs`.** The game discovers every folder inside `hd-packs/` as a pack; a backup there is
   a second pack. Backups live in `~/Library/Application Support/Arcanum Steamwoven Game/hd-pack-backups/`.
6. **One writer at a time.** `apply`, `relayout`, `refresh`, `index`, `rollback` and `mirror` take the lock `hd-packs/.hd-pack.lock` next to
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
| **Installed pack the packaged game reads** (since ARC-DEV-350 only when the iCloud copy is not used: the *HD pack* setting [chooses](#hd-pack-source-arc-dev-350); the viewer of `./arcanum area` reads another folder) | `~/Library/Application Support/Arcanum Steamwoven Game/hd-packs/interface-redraw-4x/` | no | `manifest.json`, `recipe*.json`, `layout-map.tsv`, `INDEX.md`, `index.tsv`, `interface/<screen>/<kind>/` (the [layout](#pack-layout-arc-dev-376); before ARC-DEV-376 the folders `interface/`, `character-hd114/`, `refinement02/`), `main-menu-16x9.png` (outside the manifest; ignored by the game since ARC-UI-3003) |
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
  manifest.json  recipe-*.json      as before (the recipes, and an old menu PNG the game ignores, stay unlisted at the root)
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
  copy); an interrupted `relayout` or `rollback` is finished by running `rollback <backup>` again, before any other command on the pack. The three-way `--base` check is unchanged and still protects a local repair.

## Optimized textures (ARC-DEV-376)

The PNGs stay the pack's only content: you edit a PNG, `refresh` it into the manifest, and the game does the rest. For the draw path the game keeps a
**device-local cache** of ready textures, `user://hd-cache/<pack id>/<sha[0..2]>/<sha256 of the PNG>.mip` (in the packaged game
`~/Library/Application Support/Arcanum Steamwoven Game/hd-cache/interface-redraw-4x/`). It is outside the pack, so the pack stays portable and neither
`mirror` nor iCloud carries it, and it can be deleted at any time (the next start rebuilds it).

- **What an entry is:** the decoded 4× RGBA8 frame with its whole mip chain (Godot's own `Image.GenerateMipmaps`, so the texture is the one the draw
  built itself before), about 4/3 of the raw frame (about 2 GB for the whole interface pack). Lossless: no VRAM compression in this version.
- **When it is made:** a few seconds after an HD pack is attached with the *HD* setting on, a background worker (two threads, below normal
  priority) converts every interface PNG of the pack that has no entry yet; it logs one line `ARC-DEV-376 hd cache pack=... entries=... present=...
  converted=... failed=... removed=... size=...MiB ms=...`. Entries are named by the PNG's hash, so an edited PNG (after `refresh`) has no entry and is
  converted again, and after every finished pass the entries no PNG of the pack names any more are removed (a PNG that fails to convert does not stop that).
  The worker threads are stopped when the game exits. An automated run (smoke, self-test) reads the cache but
  never converts (`ARC_HD_CACHE=convert` lets it, e.g. to check the conversion on the real pack); `ARC_HD_CACHE=0` turns the cache off (read and write);
  `ARC_HD_CACHE_DIR=<folder>` names another cache folder.
- **What the draw saves:** with an entry the frame is bound without reading the PNG, hashing it, decoding it or generating mipmaps; the pack's own checks
  (key, Legacy frame, review marks, size, alpha rule) still run on the cached level 0 (`HdPack.CheckPrepared`), and the pack file must still exist with the
  manifest's byte size (one stat; it is not read, so a PNG edited without `refresh` is noticed by `verify`, not by this draw). A frame without an entry, a damaged entry
  and the premultiplied and grey variants (bitmap-font canvases, grey spell icons) take the earlier path, so the cache can never make a frame fail.
- **Remembered alpha passes (ARC-DEV-382):** the pack's alpha rule (`HdAlphaCheck`) is a per-texel pass over the whole 4× frame, and on a warm bind it was the
  main cost left (measured on the installed pack with the frames' own sizes: about 75 ms for one 800 × 600 backdrop, 140 ms for the inventory frames, 270 ms
  for the character sheet's, 300 ms for the logbook's, 560 ms for the world map's, on a loaded Mac, so read them as ratios; the Legacy-pixel SHA-256 next to it is
  1-2% of that and the 4× copy 3-4%, so neither is worth weakening). The result is a pure function of the PNG, the Legacy frame, the policy and the rule's
  version (`HdAlphaCheck.Version`: bump it with any change of the rules), so the first successful bind of a frame appends a 24-byte record to
  `alpha-ok.v1` in the cache folder and every later bind of that frame (this run, the next start) skips the pass. Only passes are kept; the key, Legacy
  hash (still computed), size and file checks still run on every bind; `RemoveUnreferenced` drops the records of PNGs no longer in the pack, and deleting the
  cache folder (or the file) only costs one pass per frame again. VRAM-compressed textures were studied and **not adopted** (owner decision 2026-11-11: the lossless chain stays; the
  measurements are below).
- Format and code: `HdOptimizedCache` (Godot-free store, header `ARCM` version 1, format 1) and `HdOptimizedStore` (viewer).
- **VRAM compression study (ARC-DEV-382, 2026-11-11; the lossless chain stays).** Real frames of the installed pack (a 940 x 1300 panel, the 3200 x 636 HUD
  bar, a 360 x 224 icon, a 44 x 76 coverage glyph, a 168 x 168 indicator) were compressed on an Apple M4 Pro (GL on Metal): BC3 and BC7 shrink the
  chain to about a quarter (BC3 on a plain alpha channel to an eighth), but **upload time does not improve** (lossless 0.2-3 ms, BC7 0.3-16 ms per
  frame); BC3 is soft on edges (PSNR 34-45 dB, alpha off by up to 12), BC7 is close (41-52 dB, alpha within 2) but encodes in seconds for a wide
  frame; ASTC failed on the GL path and ETC2 does not fit desktop GL. If memory ever becomes the limit: compress only in the device cache (never in the
  pack), BC7 via `Image.Compress(Bptc)` after `GenerateMipmaps` in the existing background converter, run the alpha rule on the lossless PNG and bind a
  compressed texture only for a frame with a recorded pass, keep large panels and backdrops first and small frames, bitmap-font glyphs and indicators
  lossless, fall back to lossless when the renderer does not report the format compressed, and show the owner a labelled before/after composite first.

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

### Portraits (ARC-UI-2865)

The portrait redraws (the Codex deliveries of #2864 for the 48 hero portraits at 512 and 256, and of #2884 for 30 NPC portraits at 256) enter the installed pack as entries of kind
`portrait` ([format](../formats/hd-pack.md#portrait-entries-arc-ui-2865)), built into their own staging pack and added with `apply --add-new`, like the traced fonts. The deliveries stay under `local/hd-portraits/`
(never in git). Every command is run with `--dry-run` first and the game closed for the install:

```sh
python3 -B tools/hd_pack/hd_pack.py portraits-plan --hero local/hd-portraits/ARC-UI-2864-deliveries/delivery-portraits --npc local/hd-portraits/ARC-UI-2884-deliveries/delivery-npc-portraits --out local/hd-portraits/ARC-UI-2865/portraits-plan.json --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-portraits/ARC-UI-2865/portraits-plan.json --staging local/res095/pack-out/interface-portraits --fresh
python3 -B tools/hd_pack/hd_pack.py verify --installed local/res095/pack-out/interface-portraits --deep
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-portraits --add-new
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-portraits --add-new --no-base-check --task ARC-UI-2865 --dry-run
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-portraits --add-new --no-base-check --task ARC-UI-2865
python3 -B tools/hd_pack/hd_pack.py verify --deep && python3 -B tools/hd_pack/hd_pack.py mirror --dry-run
```

- `portraits-plan` checks every file it uses against the delivery's `SHA256SUMS.txt` and its PNG header (8-bit, 512 for `large/<id>_b.png`, 256 for `small/<id>.png` and an NPC `<id>.png`) and pairs each large image with its small one. Hero images map to `portrait/<id>_b.bmp` and `portrait/<id>.bmp`, NPC images to `portrait/<id>.bmp` (an NPC has no large bitmap). It writes only a plan file under a checkout's `local/`.
- `stage` runs the importer: for each item it reads the original bitmap from the game (the content stack's winner) and refuses a redraw that is not exactly 4× its size. Portraits are taken as they are (no fit, no lock) and written opaque.
- `--no-base-check` is right here because the update only adds entries (`plan` shows `0 ... differ and would be replaced`); a later re-delivery of one portrait is a replacement and goes in with `--base` as usual (the
  `staged-manifest.json` of this apply is the base). A staged portrait needs no row in `layout-map.tsv`: it is placed at `interface/portraits/heroes/` (stems starting `npc`: `interface/portraits/npcs/`) as `portrait-<stem>-f000.png` and `apply` appends its row.
- The device cache (`hd-cache`) converts the portraits too (about 80 MB more), which it does by itself a few seconds after the pack attaches.
- A portrait whose bitmap changed (another game version, a mod) draws the BMP without a message beyond the pack's usual per-frame refusal; nothing else of the pack depends on portraits.

### World-map pictures (ARC-RENDER-3079)

The HD world map (the 64 tiles and `Map_Zoomed`, upscaled without a generator, [#3065](https://github.com/Simarglok/arcanum-steamwoven/issues/3065)) enters the installed pack as entries of kind
`worldmap` and `map-zoomed` ([format](../formats/hd-pack.md#world-map-entries-arc-render-3079)), built into their own staging pack and added with `apply --add-new`, like the portraits. The pictures stay
under `local/` (never in git). The pictures come from `python3 -B tools/hd_pack/world_map_upscale.py blend` and `cut` ([recipe](../rendering/hd-world-town-map.md#world-map-one-picture-non-generative-upscale-ac3); installed 2026-11-24, ARC-RENDER-3065, backup `ARC-UI-3065-<sha12>`). Dry run first at every step:

```sh
python3 -B tools/hd_pack/hd_pack.py maps-plan --maps local/hd-maps/ARC-RENDER-3065/pictures --out local/hd-maps/ARC-RENDER-3065/maps-plan.json --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-maps/ARC-RENDER-3065/maps-plan.json --staging local/res095/pack-out/interface-maps --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-maps/ARC-RENDER-3065/maps-plan.json --staging local/res095/pack-out/interface-maps --fresh
python3 -B tools/hd_pack/hd_pack.py verify --installed local/res095/pack-out/interface-maps --deep
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-maps --add-new
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-maps --add-new --no-base-check --task ARC-RENDER-3065 --dry-run
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-maps --add-new --no-base-check --task ARC-RENDER-3065
```

- `maps-plan` takes a folder of `<base>001.png` .. `<base>064.png` (the 64 tiles, `250 × scale` square, default scale 4 = 1000) and `map_zoomed.png` (`365 × scale`, 1460 at 4×; `--zoomed-name` for another `ZoomedName`),
  checks every file's PNG header (8-bit RGB or RGBA, not interlaced, the exact size) and the folder's `SHA256SUMS.txt` when there is one, and refuses a gap in the 64 tiles unless `--partial`. It writes only a plan file under a checkout's `local/`.
- `stage` runs the importer: for each item it reads the original bitmap from the game (the content stack's winner), takes the entry kind from its size (250 × 250: `worldmap`, 365 × 365: `map-zoomed`) and refuses a picture that is not exactly the pack scale times that size. The pictures are taken as they are (no fit, no lock) and written opaque.
- A staged map bitmap needs no row in `layout-map.tsv`: it is placed at `interface/world-map/maps/` as `<kind>-<stem>-f000.png` and `apply` appends its row. `--no-base-check` is right for the first add (only new entries); a later new picture of one tile is a replacement and goes in with `--base` as usual.
- The device cache (`hd-cache`) converts these entries too, which it does by itself a few seconds after the pack attaches. A bitmap whose original changed (another game version, a mod) draws the BMP without a message beyond the pack's usual per-frame refusal; without the HD option or without entries the window draws the original pixels as before.

### Town-map tiles (ARC-RENDER-3106)

The HD town maps (the tiles of a town rendered by our engine at scale 1.0, [render path](../rendering/hd-world-town-map.md#render-path-arc-render-3063)) enter the installed pack as entries of kind `townmap`
([format](../formats/hd-pack.md#town-map-entries-arc-render-3106)), **one town per plan**, built into their own staging pack and added with `apply --add-new`, like the portraits and the world map. The tiles stay under `local/`
(never in git). `TOWN` is the town's folder name in `rules/townmap.mes` (`Ashbury`), `TILES` the render's `tiles-1.0` folder, `N` the town's tile count (`.tmi` tiles that exist; `town_render.py` prints it). Dry run first at every step:

```sh
python3 -B tools/hd_pack/hd_pack.py townmap-plan --tiles TILES --town TOWN --expect N --out local/hd-maps/ARC-RENDER-3106/TOWN/plan.json --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-maps/ARC-RENDER-3106/TOWN/plan.json --staging local/res095/pack-out/interface-townmaps-TOWN --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/hd-maps/ARC-RENDER-3106/TOWN/plan.json --staging local/res095/pack-out/interface-townmaps-TOWN --fresh
python3 -B tools/hd_pack/hd_pack.py verify --installed local/res095/pack-out/interface-townmaps-TOWN --deep
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-townmaps-TOWN --add-new
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-townmaps-TOWN --add-new --no-base-check --task ARC-RENDER-3106 --dry-run
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-townmaps-TOWN --add-new --no-base-check --task ARC-RENDER-3106
```

(the paths are the main checkout's `local/`, as in the sections above; the plan's image paths are relative to it.)

- `townmap-plan` takes the folder of one town's tiles (`<Town>NNNNNN.png`, tile 000000 included), checks every file's PNG header (8-bit RGB or RGBA, not interlaced, exactly `64 × scale` by `32 × scale`: 256 × 128 at the 4× pack) and the folder's
  `SHA256SUMS.txt` when there is one, and refuses a count other than `--expect` unless `--partial`. The default scale is 4 (render scale 1.0); a render at 0.5 is made for a 2× pack, never for the installed 4× pack. It writes only a plan file under a checkout's `local/`.
- `stage` runs the importer: it reads each original tile from the game (the content stack's winner), refuses a tile that is not 64 × 32 and a picture that is not exactly the pack scale times that size, and writes the pictures as they are, opaque. It holds one decoded picture at a time (a town has thousands).
  Ashbury (2,991 tiles) stages in about 90 s into about 88 MB.
- The installed 4× pack grows by about 30 KB per tile (Ashbury: 88 MB; all 125 towns: about 1.9 GB). The device cache does **not** convert these entries.
- Without the HD setting, without an entry for a tile, or for a tile whose original changed (another game version, a mod) the window draws the original BMP, tile by tile.
- To look before touching the installed pack: copy the pack to a folder under `local/` (never inside `hd-packs/`), `apply` into the copy with `--backup-root` under `local/`, and start a smoke with `ARC_HD_PACK=<copy>` and `ARC_AREA_WORLDMAP_HD_VERIFY=generated`
  (`./arcanum smoke map Arcanum1-024-fixed --stages worldmap --sector 503,976 --hero human-male`, the town part of the stage runs in a town-map sector).

### Back up and roll back

Every `apply` writes `<backup-root>/<TASK>-<old manifest sha12>/` with
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

### Which main-menu PNG the game loads (ARC-DEV-343, retired)

None. Until ARC-UI-3003 the viewer read `main-menu-16x9.png` at the root of the active pack and logged an `ARC-DEV-343 main menu backdrop ...` line; the code (`AreaMenuArtView.WidescreenBackground`, `HdPack.ReadMainMenuBackdropReport`) and the log line are gone, and the table-scene menus draw the pack's 4× art (Steamwoven layout) or the original's whole-factor plate (generated interface off). Which *pack* is active is still chosen as described under [HD pack source](#hd-pack-source-arc-dev-350); the startup lines `ARC-RENDER-014 visual profile=... pack=<id> ...` and `ARC-UI-006 ... pack-source=...` say which pack opened.

### HD pack source (ARC-DEV-350)

The game chooses its HD pack at startup from the **HD pack** line of the launcher screen (after *HD*; ARC-UI-143 moved it there from the options window's Game page, ARC-UI-146), saved as the `hd pack` (`auto`, `game`, `custom`) and `hd pack path` lines of `arcanum.cfg` (a missing line reads as Auto). A change
applies at the **next start**, and the launcher screen's note says at once what that start will read:

| Setting | Chain (first pack that opens, is not `rejected` and is whole wins) |
| --- | --- |
| **Auto** (default) | the iCloud pack folder (`~/Library/Mobile Documents/iCloud~md~obsidian/Documents/Arcanum Steamwoven/hd_pack`, **macOS only and only when that folder exists**, so Windows and other devices simply have no default), then the game folder `user://hd-packs/`, then (editor binary only) the checkout's `local/hd-packs/` (the same folder by its symlink, counted once) |
| **Game** | the game folder `user://hd-packs/` alone: the installed copy |
| **Custom** | the folder chosen in the system folder dialog (a folder that holds `manifest.json` is the pack itself, any other is a folder of packs), then the game folder; entering Custom always opens the dialog, cancelling keeps the saved folder |

*Whole* is a stat of every manifest file and the recipe (an unlisted `main-menu-16x9.png` does not count): the file is there, has the manifest's byte
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

**Fallback to Legacy graphics and the problem box.** When the player asked for HD (the *HD* setting is on, or a developer override asks for the HD profile)
and the pack cannot be used, the run draws the original (Legacy) graphics: no pack found or none whole (every candidate's reason is listed), the
pack named by `ARC_HD_PACK` did not open, or the pack is marked rejected. The settings are not changed, so a repaired pack brings HD back at the
next start. The reason goes to the log (`ARC-DEV-350 hd pack problem: ...`, no path) and to a box over the menu with **Continue** (goes on with
Legacy graphics; Enter and Escape do the same) and **Close game** (ends the program; the log says which was pressed, `ARC-DEV-350 hd pack
problem box: ...`). A pack that is incomplete but entirely on this device is still used and the box says that its missing frames draw Legacy. A
skipped iCloud copy whose installed fallback is whole is not an error: it is in the log line, the HUD and the options note only. An automated
run opens no box.

### The second device and the iCloud copy (ARC-DEV-343, ARC-DEV-349)

What `mirror` does for unlisted files such as an old `main-menu-16x9.png` (ignored by the game since ARC-UI-3003) and `recipe-refinement02.json` (code and `test_hd_pack.py`, not a live cloud run): they are not in the manifest, but
`mirror` copies **every** file of the source pack except `.DS_Store`, so they
travel with it; an unlisted file is compared by SHA-256 on every run (a listed one by the manifest's hash and size), so a
changed one is noticed and copied. Run `mirror` after every `apply`, or the iCloud copy keeps the old picture.

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
- An **editor-binary run** there (`Godot --path`, rare) reads the checkout's `local/hd-packs/` (the iCloud symlink) in place, but only when its own `user://hd-packs/` has no usable pack (item 2 above) and *HD* is on.
- **There is no command that installs the iCloud copy as a pack.** `HdPackImport` (the format page) is a library function used by
  tests only; it copies the manifest, one recipe and the listed entries and **drops every unlisted file, so it never brings
  an old `main-menu-16x9.png`**. `hd_pack.py mirror` refuses a destination inside the game's folder. The way left is a hand copy of the
  whole `hd_pack/interface-redraw-4x` into an empty `hd-packs/` of the second device (a first install only; then `status` and `verify --deep`
  there, and never over an existing pack). A follow-up task would add a checked install command; until then do not tell the owner the
  second device "syncs through iCloud" without saying that this hand copy is meant.
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
- The staging pack holds only the fonts (id `interface-fonts`). `ARC_HD_PACK=<that folder>` with the *HD* option on shows the HD text with every other
  frame Legacy, which is the way to look at the fonts without touching the installed pack.
- The importer checks each frame against the `masked` rule (no ink farther than one Legacy texel from the Legacy glyph) and refuses the whole build (nothing is
  written) when one frame fails. On 2026-11-11 the five fonts of the first trace (arts 27, 171, 229, 327, 840) built as 1113 entries in one run (a local observation).
- `fonts-plan` overwrites only a plan it wrote (every item a `coverage` item), never the pipeline's `ready/plan.json`, and writes only under a checkout's `local/`.
- Rolling back is `rollback` of the `apply` backup, as for any update.
- **Replacing the installed fonts (ARC-UI-145).** A new trace of the same fonts (other `hd_font` parameters) changes the *bytes* of keys that are already installed, so
  the update is not `--add-new`: run `plan --staged <staging> --base INSTALLED` without `--add-new`; it must say `1106 interface entries differ` (the five fonts: 1113 entries, 7 of them
  byte-identical under the new trace; a local dry run of 2026-11-11, `staged-only entries: 0`) and no CONFLICT, then `apply` the same way. The viewer draws the glyphs bilinear without mipmaps since ARC-UI-145 (the interface frames stay trilinear).
- **The nine Codex fonts (ARC-UI-1202, 2026-11-15, owner-run Codex delivery; the pack and its receipts stay local).** Each font arrives as its own staging pack with a
  base manifest (the `staged-manifest.json` of the ARC-UI-145 backup), so the updates are independent and run one after another, each with its own
  `apply --dry-run` against the *current* installed pack and the same base. Stage A replaced existing keys (27, 229, 171, 327: 181, 184, 177, 185 entries
  differ, no CONFLICT); stage B added new keys with `--add-new --map layout-additions.tsv` (28, 230, 301, 371, 483: 223 entries each, 0 replaced). The
  installed manifest went from 024dec70aa9a to 175a19a0fed1 (2255 to 3370 entries, `interface/fonts` 1113 to 2228); every step ended with `verify --deep` PASS.
  The viewer needs no per-font support: a font ART read through `AreaInterfaceArt.Load` carries its HD key and `AreaArtFontFace` asks the pack for each glyph, so any
  font ART in the pack is drawn. A generated font keeps the original's frame sizes and advances; where the generator was unsure (all of Garmond9's extended frames 161..255
  and about 40 other frames per font) the frame is the earlier trace, named per frame in the delivery's `origins.json`. Rollback is the `ARC-UI-1202-*` backups, newest first.
- **Ten reading-window fonts (ARC-UI-2309, 2026-11-15, traced locally, no Codex).** Fonts 480 (Courier10, trace at cap 0.5 and alpha-max 1.0), 497, 490, 492, 485, 484, 815, 478, 479 and 37 were traced by [`tools/hd_font`](hd-font.md#method-and-limits)
  into one folder (it needs the tool's `receipt.json`; copy it from the trace run), `fonts-plan` made 2229 frames in 10 fonts, `stage` built `local/res095/pack-out/interface-fonts-reading-2309`, and the new keys went in with
  `apply --add-new --map layout-additions.tsv` (one row per ART: `<name>font<TAB>fonts<TAB>fonts<TAB>1`, e.g. `courier10font`). The plan said `0 interface entries differ`, `staged-only entries: 2229`. A **candidate pack**
  (a `cp -R` of the installed pack, `apply --installed <copy> --backup-root <outside the copy>`, `verify --deep` PASS) lets the viewer draw the result before the real update:
  `./arcanum smoke pr --stages map --env ARC_AREA_WINDOW_ART=hd --env ARC_HD_PACK=<pack> --env ARC_AREA_WINDOW_CAPTURE=<prefix> --env ARC_AREA_READING_SIZE=2560x1440` (about 65 s, a capture of each reading window; `ARC_AREA_WINDOW_ART=legacy`
  without a pack gives the original-look reference) saves the windows for a labelled before/after composite (local, never in git). Applied on 2026-11-15 with the owner's go: manifest 175a19a0fed1 to a39a9dfef966 (3370 to 5599 entries, `interface/fonts` 2228 to 4457), `verify --deep` PASS, `mirror` 2233 files; the backup is `ARC-UI-2309-175a19a0fed1`, so the next base is its `staged-manifest.json`. Garmond6 and Garmond8 were not in that update; they followed in ARC-UI-2329 (next bullet).
- **Garmond8 and Garmond6 (ARC-UI-2329, 2026-11-16, owner-run Codex delivery, generated like the nine Codex fonts).** Each font is its own staging pack (62 generated Latin letters and digits, the other 161 frames the cap 0.5 trace; 223 keys each) with the saved base manifest of the ARC-UI-2309 backup. Dry-run on the current pack: `0 interface entries differ`, `staged-only entries: 223`, no CONFLICT. Applied one after the other with the owner's go, game closed: `apply --add-new --map layout-additions.tsv`, manifest a39a9dfef966 to a31b8f3f9c32 (Garmond8, 482, backup `ARC-UI-2329-a39a9dfef966`) to 9292e4afa87d (Garmond6, 481, backup `ARC-UI-2329-a31b8f3f9c32`), 5599 to 6045 entries (`interface/fonts` 4457 to 4903), `verify --deep` PASS after each, `mirror` 450 files. The next base of this line is the `staged-manifest.json` of `ARC-UI-2329-a31b8f3f9c32`. Rollback is these two backups, newest first. The newspaper masthead (482) and the telegram form lines (481) are the reading-window callers (ledger WR8, WR20); the owner's brief said some masthead/legend text already uses Garmond9, which is unverified here, so installing these frames changes only the callers that still ask for 481/482. Garmond6 stays tiny at 1x and its spacing follows the original advances.

## Kit-assembled frames in the pack (ARC-UI-3077)

The frames of the Steamwoven kit assembly tool ([`assemble.py`](../ui/steamwoven-kit-assembly.md), TRD [pipeline](../ui/steamwoven-interface-trd.md#generation-pipeline) row 6b) are
`<art>-fNNN.png` files plus `<art>.assembly.json`. `tools/steamwoven_ui/pack_plan.py` (row 6c) turns them into the importer plan, so nobody writes a plan or a mask by hand.
All of it is local: the plan names original ART files and the frames are derived from them, so the tool refuses an output folder inside the checkout other than `local/`.
A worktree has its own empty `local/`: give the commands the **main checkout's** `local/` paths. Run every command with `--dry-run` first where it has one:

```sh
python3 -B tools/steamwoven_ui/assemble.py LAYOUT.json --out local/steamwoven-ui/assembled/<batch>        # the frames and their records
python3 -B tools/steamwoven_ui/pack_plan.py local/steamwoven-ui/assembled/<batch> --dry-run               # the art map, nothing written
python3 -B tools/steamwoven_ui/pack_plan.py local/steamwoven-ui/assembled/<batch>                         # plan.json and masks/ next to the frames
python3 -B tools/hd_pack/hd_pack.py stage --plan local/steamwoven-ui/assembled/<batch>/plan.json --staging local/res095/pack-out/interface-redraw-4x --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/steamwoven-ui/assembled/<batch>/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-redraw-4x --base INSTALLED
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-redraw-4x --base INSTALLED --task ARC-XXX-NNN --dry-run
```

`INSTALLED` is the installed pack folder, as for the fonts above. The real `apply` (then `verify --deep`, then `mirror`) is the owner's to allow, with the game closed.

- **What it writes.** Per assembled frame one `ready` item (`"lock": false`: an assembled frame is final, the importer's shading lock would pull it back to the original's tone) whose tile
  names the ART file as `interface.mes` gives it (`Schematics_Button.ART`); image and mask paths are relative to the plan file. A frame assembled in the `masked` alpha mode (a plate
  that must not keep the original frame's rim and corners) also gets an `alphaMask`: the frame's own alpha as an 8-bit grey PNG of the HD frame's size in `masks/` (the importer takes
  such a mask as the alpha itself, cut to the Legacy-opaque texels, and writes the entry as `"alpha": "masked"`, [format](../formats/hd-pack.md#interface-art-arc-dev-242)). A `legacySmooth`
  frame gets none: the importer's own alpha for a frame with no mask is that rule. A `legacy` frame (the exact original alpha, which the importer does not keep without a mask) and a
  record written before the record carried `alphaMode` get masks; `--masks always|never` overrides. The importer **cuts** every HD mask to the texels the Legacy frame leaves
  opaque and refuses one that leaves nothing opaque, and the tool does not read the Legacy frame: a plate part outside the silhouette (the one-texel reach the `masked` gate allows)
  is cleared at `stage`, and a mask refused there is reported there.
- **The art map.** The tool prints `art -> ART file` for every art (`470 -> Combat_Button.ART (3 frames of 156x156, alpha masks; name from census)`). A number is looked up in the census
  (`docs/ui/steamwoven-interface-census-arts.tsv`) and the frame count and size are checked against its row; another identifier is taken as the file's own name; `--art-name 471=Schematics_Button.ART`
  names an art the census does not. It refuses two arts naming one ART file, different scales in one plan, a frame that differs from its record, and a frame with nothing visible to mask and a `--art-name` for an art that has no record. The census row's frame count and size are checked under `--art-name` too. Files are written whole (a temporary file moved over the old one), the plan last.
- `--pack-id` (default `interface-redraw-4x`) is the staging pack's id. The plan is replaced only when the existing `plan.json` is an importer plan.
- Frames the staging build replaces in the installed pack show in `plan` as differing entries; frames the installed pack lacks need `--add-new`. The plan must name exactly the frames the assembly wrote.
- Checks: `python3 -B -m unittest tools.steamwoven_ui.test_pack_plan` (synthetic frames only; also in the batched `tools/local_dev/run_tests.py`).

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
   The dialog box, the menu art views and the pre-made browser had the same fault (round Accept and Cancel buttons with a square behind them, ARC-UI-144).
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
- **Unlisted files.** Recipe files outside the entries (and an ignored old `main-menu-16x9.png`) are not copied by the importer or by
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
2. Build or repair into staging; never into the installed pack.
3. `plan` and `apply --dry-run` with `--base`: the replaced entries are exactly the task's and there is no unexpected CONFLICT (a dry run that finds conflicts prints the plan and exits 2).
4. `apply`; `verify --deep`; the new manifest sha and the backup folder name go into the report.
5. Start the game/viewer, check the changed frames once, and (for a rendering task) the task's smoke stage.
6. `mirror --dry-run`, then `mirror` to the iCloud copy when the owner uses it.
7. Handoff: what was changed, the backup folder, the manifest sha before and after, what stays local.
