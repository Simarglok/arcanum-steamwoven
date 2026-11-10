# ARC-DEV-341 — HD build scripts in git

Work-Item: ARC-DEV-341
Result: The owner-local HD redraw scripts (`build_res095.py`, `fit_res095.py`, `pack_res095.py` and their image helpers) are in git under `tools/hd_pack/res095/`, with no personal path, so threads and worktrees can change and review them.
Components: `tools/hd_pack/res095`, HD pack workflow doc
Human-Gates: none
Requirements: FR-PRODUCT-004 (serves the HD upscale path; no coverage row changes)

AC1. The three scripts plus `hdlib.py` and `metrics.py` live in `tools/hd_pack/res095/`; behaviour is unchanged (only paths, usage lines and the self-copy and `tools/` folder creation of `build_res095.py` in `local/` changed). `fit_res095.py` is the pre-ARC-DEV-340 version, so the guard fix is a normal diff on top.
AC2. Paths come from `hd_paths.py` (the main checkout's ignored `local/`, found from any worktree, `ARC_MAIN_CHECKOUT` overrides); no `/Users/` path or account name in any file.
AC3. `docs/workflow/hd-pack.md` names the new location; the superseded local scripts stay local.
AC4. No original or derived art, plan, manifest or sheet enters git.
