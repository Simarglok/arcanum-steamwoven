# HD redraw kit scripts (ARC-DEV-341)

The scripts that build the owner-local HD interface redraw kit and turn the redrawn sheets into an importer plan.
They are code only: the original frames, sheets, results, plans and packs they read and write live in the ignored
`local/` of the **main checkout** (`hd_paths.py` finds it from any worktree; override with `ARC_MAIN_CHECKOUT`). No
original or derived art belongs in git ([hard rules](../../../CLAUDE.md)); the workflow is
[`docs/workflow/hd-pack.md`](../../../docs/workflow/hd-pack.md).

| Script | What it does | Reads | Writes (all under `local/res095/`) |
| --- | --- | --- | --- |
| `build_res095.py [--no-4x]` | groups every interface art into ChatGPT sheets, prompts, manifests, 1x/4x frames | `local/res094/{frames,census.json,kinds.json,u3_*}` | `groups/`, `common/`, `.build_state.json` |
| `fit_res095.py [--jobs N] [SHEET_ID ...]` | registers each sheet result to its source sheet, cuts the cells | `groups/*/{manifest.json,sheets,results}` | `ready/cells/`, `ready/fit_log/` |
| `pack_res095.py [--min-ncc 0.5]` | stitches tiled arts, QA table, review sheets, importer plan | `ready/cells`, `ready/fit_log`, `.build_state.json` | `ready/{frames,review,qa.json,plan.json}` |
| `hdlib.py`, `metrics.py` | shared image helpers (from RES-094) | `local/res094/frames` | nothing |
| `hd_paths.py` | the `local/` roots | | |

Run every script with the res094 virtual environment (numpy, scipy, Pillow, scikit-image; Real-ESRGAN is needed only
by the optional `hdlib.resr_batch`) and from any directory:

```sh
local/res094/venv/bin/python tools/hd_pack/res095/fit_res095.py --jobs 12   # only when sheets or results changed
local/res094/venv/bin/python tools/hd_pack/res095/pack_res095.py            # writes ready/plan.json and ready/frames
python3 -B tools/hd_pack/hd_pack.py stage --plan local/res095/ready/plan.json --staging local/res095/pack-out/interface-redraw-4x --fresh --dry-run
```

Left out on purpose: `rebuild_hd.sh`, `full_hd.sh`, `apply_update.sh`, `merge_update.py` (superseded by
`hd_pack.py stage` / `plan` / `apply`; `rebuild_hd.sh` deletes its target) and `docs_res095.py` (writes the Russian
README/INDEX of the local kit). They stay in `local/res095/tools/` as history.

Tests (synthetic, no art): `python3 -B -m unittest tools.hd_pack.res095.test_hd_paths`.
