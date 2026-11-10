# The player's HD interface pack: storage, build, update, backup (ARC-DEV-329)

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
   pack holds work that no rebuild reproduces (`character-hd114/`, `refinement02/`, `recipe-refinement02.json`, the
   unlisted `main-menu-16x9.png`). Change it only with `hd_pack.py apply`, `menu-art` or `rollback`.
3. **Every command that writes is run with `--dry-run` first**, the output is read, and only then the real run.
   A command given to the owner was run with `--dry-run` or `--help` first (launch-check rule).
4. **Build into staging, never into an installed pack or a Drive folder.** Staging is
   `local/res095/pack-out/<id>/` (ignored by git). `hd_pack.py stage` refuses a target that is not a plain folder under a
   `local/**/pack-out/` path, and wipes (`--fresh`) only a folder that holds a pack manifest.
5. **Back up outside `hd-packs`.** The game discovers every folder inside `hd-packs/` as a pack; a backup there is
   a second pack. Backups live in `~/Library/Application Support/Arcanum Steamwoven Game/hd-pack-backups/`.
6. **One writer at a time.** `apply`, `menu-art`, `rollback` and `mirror` take the lock `hd-packs/.hd-pack.lock` next to
   the installed pack (a file there is not a pack); a lock of a dead process is taken over, one held by a live process
   means wait. Do not start a second agent on the same pack: check with `status` and the backup folder first.
7. If something is missing or the same failure repeats twice, stop and ask the owner with options. Do not edit a
   manifest by hand and do not copy files into the installed pack with `cp`/`rsync --delete`/Finder.

## Where things live

| What | Where | In git | Notes |
| --- | --- | --- | --- |
| Code, format, this page, synthetic tests | repository | yes | `tools/hd-interface-import`, `tools/hd_pack`, `docs/` |
| Original art frames, ChatGPT sheets and results, fit/pack outputs, plans, handoffs | `local/res094/`, `local/res095/` (group folders, `ready/`, `character_repair/`, `topbar*/`, `combat119/`, `arc-ui-*/`) | **no** (`/local/` is ignored) | the work area; the local pipeline scripts are in `local/res095/tools/` (`fit_res095.py`, `pack_res095.py`, `build_res095.py`; owner-local, not in git) |
| Staging build | `local/res095/pack-out/interface-redraw-4x/` | no | rebuilt from `ready/plan.json`; may be wiped by `stage --fresh` |
| **Installed pack the game reads** | `~/Library/Application Support/Arcanum Steamwoven Game/hd-packs/interface-redraw-4x/` | no | `manifest.json`, `recipe*.json`, `interface/`, `character-hd114/`, `refinement02/`, `main-menu-16x9.png` (outside the manifest) |
| Backups of every change | `~/Library/Application Support/Arcanum Steamwoven Game/hd-pack-backups/<TASK>-<old manifest sha12>/` | no | the old manifest, the replaced files, `RECEIPT.json` |
| Google Drive copy | `~/Library/CloudStorage/GoogleDrive-<account>/My Drive/Steamwoven/hd_pack/` (reached as the symlinks `local/hd_pack` and `local/hd-packs`) | no | cross-device store; a **mirror of the installed pack**, never a build target ([below](#google-drive-copy)) |
| Cold sources: the player's own installation | `/Applications/Arcanum/modules/Arcanum.dat` | no | the importer reads it, nothing writes it |

`local/hd-packs` is **not a local folder**: it is a symlink into Drive. A path under it reaches the cloud folder,
so any write there is a cloud write, and macOS may answer `Operation not permitted` to a process that was not
granted Drive access (the Claude app, a Remote Control session, a Codex worktree). That is a permission, not a
broken pack: run the command from a terminal that has the access, or use the installed folder as the source of truth.

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

The sheet fit and stitching are the owner-local scripts; the importer is in git:

```sh
local/res094/venv/bin/python local/res095/tools/fit_res095.py --jobs 12      # only when sheets or results changed
local/res094/venv/bin/python local/res095/tools/pack_res095.py               # writes ready/plan.json and ready/frames
python3 -B tools/hd_pack/hd_pack.py stage --plan local/res095/ready/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh --dry-run
python3 -B tools/hd_pack/hd_pack.py stage --plan local/res095/ready/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh
```

`stage` runs the importer through `./arcanum run --kind dotnet` (the queue and its quiet log apply). **Do not run
`local/res095/tools/rebuild_hd.sh`**: it deletes its target folder, and its target is now the Drive link. Do not
point `hd-interface-import --pack` at the installed pack or a Drive folder either: the importer rewrites the
manifest of an existing pack as a new identity and has not been verified against the Codex extras.

The importer's lock is on by default; a plan item with `"lock": false` skips the shading/colour lock
([format](../formats/hd-pack.md#interface-art-arc-dev-242), ARC-UI-111/117). `fit_res095.py`'s guard can still mix
the original in; check the frame that reached the pack (RGB/alpha along the outline, anchors, states), not only the fit.

### Update the installed pack (diff-only)

The update replaces only the `interface/` entries whose content differs from the staging build, matched by the
portable key (art hash, group, frame, palette). Entries and files of the other folders (`character-hd114`,
`refinement02`) and every file outside the manifest stay.

```sh
python3 -B tools/hd_pack/hd_pack.py plan  --staged local/res095/pack-out/interface-redraw-4x
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-redraw-4x --task ARC-UI-NNN --dry-run
python3 -B tools/hd_pack/hd_pack.py apply --staged local/res095/pack-out/interface-redraw-4x --task ARC-UI-NNN
python3 -B tools/hd_pack/hd_pack.py verify --deep
```

Read the plan: the number of replaced entries must match what the task changed (a handful for a repair, never
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

### Google Drive copy

The Drive copy lets the owner carry the pack to another device. It follows the installed pack, one way:

```sh
python3 -B tools/hd_pack/hd_pack.py mirror --dst ~/Library/CloudStorage/GoogleDrive-<account>/"My Drive"/Steamwoven/hd_pack/interface-redraw-4x --dry-run
python3 -B tools/hd_pack/hd_pack.py mirror --dst <the same folder>
```

`mirror` refuses a damaged source, a destination inside the game's folder or holding symbolic links, copies missing or differing files (a listed file is compared by the manifests' hash and size, so Drive files
are not re-read; `--deep` hashes every file in both folders), writes `manifest.json` last, and **never deletes**: files only in the destination are
listed and kept. After it, `status --installed <Drive folder> --allow-symlink` (from a terminal with Drive access)
shows the same manifest sha as the installed pack. Whether a second device has synced is a separate check on that
device. Importing the copy on another device is `HdPackImport` (the format page), never a copy over a pack.

## Known hazards

- **Drive symlinks** (`local/hd_pack`, `local/hd-packs`): see above; the tool refuses a symlinked installed pack
  unless `--allow-symlink`, and refuses any symlink inside a pack.
- **Superseded local scripts.** `local/res095/tools/rebuild_hd.sh` (now stubbed to refuse), `apply_update.sh`, `merge_update.py` and `full_hd.sh`
  are replaced by `stage`, `plan` and `apply`; they stay in `local/` as history only.
- **Unlisted files.** `main-menu-16x9.png` and recipe files outside the entries are not copied by the importer or by
  `HdPackImport`; a whole-folder replace silently loses them.
- **Same file name, new content.** A local repair may keep a file's name while its content changes (the installed
  `combat_button-f0-…` differs from the same-named staged file by hash), so the name suffix is not the content hash.
  `apply` and `mirror` compare manifest hashes (`apply` overwrites and keeps the old copy in the backup); a copy or sync by name and size
  would not notice.
- **Two agents.** Codex repairs (`character-hd114`, `refinement02`) are in the installed pack only. Read
  `status` before writing and name the manifest sha you started from in the report.
- **Plans and receipts are local.** A real plan lists original file names; keep it in `local/`, and keep
  `hd-pack-backups` receipts out of the repository.

## Checklist for a thread that changes the pack

1. `status` and `verify --deep`: record the manifest sha before.
2. Build or repair into staging (or prepare the menu PNG); never into the installed pack.
3. `plan` and `apply --dry-run`: the replaced entries are exactly the task's.
4. `apply` (or `menu-art`); `verify --deep`; the new manifest sha and the backup folder name go into the report.
5. Start the game/viewer, check the changed frames once, and (for a rendering task) the task's smoke stage.
6. `mirror --dry-run`, then `mirror` to Drive when the owner uses it, from a terminal with Drive access.
7. Handoff: what was changed, the backup folder, the manifest sha before and after, what stays local.
