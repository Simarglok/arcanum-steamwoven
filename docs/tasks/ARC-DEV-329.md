# ARC-DEV-329 — one canonical HD pack workflow and safe tool

Work-Item: ARC-DEV-329
Result: One agent-neutral page and one dry-run-first tool for storing, staging, updating, backing up, rolling back and mirroring the installed HD interface pack.
Components: HD pack workflow doc, `tools/hd_pack`
Human-Gates: none
Requirements: FR-PRODUCT-004 (serves the HD upscale path; no coverage row changes)

AC1. `docs/workflow/hd-pack.md` states what lives in git, in ignored `local/`, in the installed pack, in backups and on Google Drive, and is linked from `CLAUDE.md`, `AGENTS.md`, the format page and the toolchain page.
AC2. `tools/hd_pack/hd_pack.py` updates the installed pack by manifest diff only, keeps every other entry and unlisted file, backs up outside `hd-packs`, writes the manifest last and rolls back; every writing command has `--dry-run`.
AC3. Synthetic tests (`python3 -B -m unittest tools.hd_pack.test_hd_pack`) cover plan, apply, rollback, refusals (symlink, corrupt staged file, backup inside the collection, live lock), menu art, mirror and stage.
AC4. A `--dry-run` against the real installed pack and the real staging build lists the expected diff and changes nothing.
AC5. No original or derived art, real manifest or personal path enters git. Root checks pass.
