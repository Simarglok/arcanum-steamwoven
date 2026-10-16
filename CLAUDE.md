# CLAUDE.md — project guide for Claude sessions

This file is for Claude. Codex/Hermes workers read `AGENTS.md` and `.hermes.md`;
they do not load this file, and nothing here changes their contracts.

## 1. Roles and authority

- **Default role: developer.** Work on the task the owner gives you (a direct
  request, a Claude project thread, or a named `ARC-*` work item) and follow
  `AGENTS.md` (task authority, card readiness, implementation and evidence,
  GitHub identity, delivery) and `docs/workflow/toolchain.md`.
- **Other roles are assigned explicitly.** The Hermes reliability role (diagnose
  why the Hermes board stalls, configure Hermes via `conductor-hermes`) lives in
  [`docs/hermes/claude-reliability-role.md`](docs/hermes/claude-reliability-role.md);
  load it only when the owner assigns that role.
- **Hermes state is off limits for developer sessions.** Do not touch
  `~/.hermes` (board, profiles, plugins, cron, logs with secrets) and do not run
  `hermes`. Work done outside Hermes is reconciled on the board by the owner or the
  reliability role.
- **Pull requests.** Every change goes through a PR from a branch (never push to
  `main`), as GitHub user `Simarglok`. Get an independent review from a fresh
  subagent on the exact diff first. The owner merges developer PRs (human merge for
  non-Hermes work) unless the owner says otherwise for the task.
- **Stop and ask** when something you need is missing, a fix would change the
  task's contract, or the same failure repeats twice; offer options. Do not guess,
  mock or loop.
- **Secrets:** never open or print `.env*`, auth files, tokens or config values
  matching key/token/secret/password.

## 2. The project

Arcanum Steamwoven: independent MIT C#/Godot 4 reimplementation of *Arcanum: Of
Steamworks and Magick Obscura* that reads the player's original data.
Research-first, every component deliberately **bounded** (fixed Crash Site
sector/area profiles, diagnostic viewers). Not a playable game yet.

- Toolchain: .NET SDK 8.0.425 (`global.json`, no roll-forward), net8.0,
  `TreatWarningsAsErrors`, central package versions + lock files
  (`restore --locked-mode`), MSTest. Godot **4.7.2 .NET**, `godot_canonical`
  wrapper and paths in `docs/workflow/toolchain.md`.
- Root checks: `dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && dotnet test --no-build --filter "TestCategory!=RealData"` and `python3 -B tools/research/dat_probe.py --self-test`.
  Root sln excludes terrain viewer, research probes and labs (own commands).
  Real-data tests are opt-in via env vars (`DAT_ROOT`, `ARCANUM_TERRAIN_*`, `ARC_AREA_*`) and categories `RealData`, `DlgRealData`, `DlgRuntimeRealData`, `NpcDialogEntryRealData`.
- Assemblies (`src/`): `Arcanum.Data` (DAT `1TAD` reader, ART decoder, DLG,
  terrain, static objects, cardinal traversal, complete Crash Site `Area`
  owner), `Arcanum.Simulation` (exact grid `Movement`; `Clock` on origin/main),
  `Arcanum.Scripting` (`Dialog` transition; `NpcDialog`, `GlobalFlags` on
  origin/main), `Arcanum.Presentation` (animation timeline), `Arcanum.Input`
  (+ `Arcanum.Input.Godot` adapter source), `Arcanum.Godot` (ART viewer).
  Data/Simulation/Scripting must stay Godot-independent.
- Tools: `tools/terrain-viewer` (Core + Godot; the integrated "WORLD"
  diagnostic), `tools/terrain-inspect`, `tools/research/*` probes,
  `tools/arcanum-local` + `tools/local_dev` (local export/lease/retention
  authority — heavy, fail-closed). Labs: `experiments/{gamepad,display-ui,audio}-lab`.
- Launcher: `./arcanum setup|area|world|status` wraps `tools/arcanum-local`. When a
  viewer mode gains or changes an argument or input, update `tools/local_dev/launch.py`
  (`GAME_ROLES`/`MODE_ROLES`/`ROLE_FLAGS`), `./arcanum` and the launcher docs in the
  same PR; `test_launcher_sync.py` catches drift.
- Hard rules: no original/extracted game content, toolkit archives, secrets or
  personal paths in git (`toolkit/`, `artifacts/`, `local/`, `research-private/`
  are ignored); synthetic fixtures only; no mechanical copying/translation from CE;
  record provenance; GitHub account `Simarglok` only; human merge for non-Hermes work.
- Style: dense, defensive, immutable records; explicit limits/options objects;
  typed failure results instead of exceptions at runtime boundaries; docs state
  scope and "NOT READY" boundaries precisely.

## 3. Truth hierarchy

1. **Code and git history** (prefer `origin/main` — see §4); for board state, the live Hermes DB.
2. Repo docs: `docs/project/status.md` (component catalogue, audited), contracts
   under `docs/world`, `docs/scripting`, `docs/rendering`, `docs/formats`.
3. Obsidian vault "Arcanum Steamwoven" — Russian research/decision notes from
   2026-09-19..21. **Stale**: it predates all code. Use for intent/decisions
   (DEC-001..020, provenance policy), never for implementation status.

## 4. Repositories and git state

- Remote `https://github.com/Simarglok/arcanum-steamwoven`. The owner's local
  checkout may lag `origin/main` (on 2026-10-13 it sat at `11e8099`/PR #46 vs
  `1f3c036`/PR #58). For current code use a fresh cloud clone, or
  `git show origin/main:<path>` locally (only as fresh as the last fetch).
- `.worktrees/` (~70 Hermes task worktrees, ignored via `.gitignore`)
  and many `arcanum-steamwoven/t_<id>-arc-*` branches belong to Hermes runs.
  Don't modify or delete them; they are evidence — except the one worktree your
  task explicitly names (e.g. ARC-GPAD-005: `.worktrees/t_6346b4d1`). Unmerged
  in-flight branches include ARC-DATA-010 and ARC-GPAD-005.
- The CE research clone (owner's Mac; `~/mnt/arcanum-ce` in bridged sessions) = `alexbatalov/arcanum-ce` at the exact pinned research
  commit `a7ff41b300ef712f0e7d088183a3d08957110cdd` (cited ~1,200× in docs).
  C, Sustainable Use License: read to understand original mechanics
  (`src/game/*.c`, `src/ui/*.c`); never copy code or structure into this repo.

## 5. Where the rules live

- `AGENTS.md` — agent rules, card readiness (Definition of Ready), evidence levels.
- `docs/workflow/toolchain.md` — exact SDK/Godot versions, `godot_canonical`,
  lab and export commands.
- `docs/workflow/hermes.md`, `merge-policy.md`, `task-naming.md`, `readiness.json`
  — Hermes board workflow (for reading cards and for the reliability role).
- `docs/project/status.md` — component catalogue; `docs/requirements/coverage.md`.
- `docs/hermes/` — Hermes failure analysis and remediation history.
