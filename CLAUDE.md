# CLAUDE.md — project guide for Claude sessions

This file is for Claude. The shared agent rules live in `AGENTS.md`; this file
summarizes the project and points to them.

## 1. Roles and authority

- **Default role: developer.** Work on the task the owner gives you (a direct
  request, a Claude project thread, or a named `ARC-*` work item) and follow
  `AGENTS.md` (task authority, task readiness, implementation and evidence,
  GitHub identity, delivery), `docs/workflow/tasks.md` and
  `docs/workflow/toolchain.md`. Name threads, branches, PRs and commits by the
  task code ([`docs/workflow/task-naming.md`](docs/workflow/task-naming.md)):
  `ARC-TYPE-NNN` first, a Russian result in thread titles. Types: `RES`, `DATA`,
  `RENDER`, `WORLD`, `INPUT`, `GPAD`, `UI`, `SCRIPT`, `DEV`, `AI`, `COMBAT`,
  `SKILLS`, `MAGIC`, `TECHNOLOGY`. Check the number is unused (git history,
  open/merged PRs, branches, other threads) before taking it.
- **Language.** Talk to the owner in Russian; repository text (docs, code,
  commits, PR titles and bodies) stays in English.
- **Where work runs.** Work that builds, tests or runs code, Godot or original
  data runs on the owner's Mac (a Remote Control session in the owner's
  checkout). A docs-only task (only Markdown documentation changes) may run in
  a cloud clone, which has no .NET or Godot, and runs **no checks**: no root
  checks, no task checks; the PR says "docs-only, no checks run". The
  independent review still applies (`docs/workflow/tasks.md`).
- **Check every launch.** Within a minute of starting a Godot or launcher run,
  confirm it started the intended mode, or stop it and fix the command
  ([checking that a run started](docs/workflow/toolchain.md#checking-that-a-run-started-arc-dev-029)).
- **The retired Hermes setup is off limits.** The Hermes task board and the
  Codex workflow were removed on 2026-10-16 (PR #137); do not touch `~/.hermes`
  on the owner's Mac (old board, profiles, logs with secrets) and do not run
  `hermes`.
- **Pull requests.** Every change goes through a PR from a branch (never push to
  `main`), as GitHub user `Simarglok`. Get an independent review from a fresh
  subagent on the exact diff first. Once the acceptance criteria, task checks
  and root checks pass (none for docs-only) and the review is addressed, merge
  the PR yourself and report it, unless the owner explicitly asked to review it;
  then leave it open for the owner (`docs/workflow/tasks.md`).
- **Stop and ask** when something you need is missing, a fix would change the
  task's contract, or the same failure repeats twice; offer options. Do not guess,
  mock or loop.
- **Secrets:** never open or print `.env*`, auth files, tokens or config values
  matching key/token/secret/password.

## 2. The project

Arcanum Steamwoven: independent MIT C#/Godot 4 reimplementation of *Arcanum: Of
Steamworks and Magick Obscura* that reads the player's original data.
Research-first, every component deliberately **bounded**. Not a playable game
yet. On `origin/main` (2026-10-16, through PR #157): the AREA viewer walks the
whole Crash Site exterior and cave (click-to-move, hover, inventory and loot,
NPC conversation, Virgil follows, day/night, quick save, diagnostic palette,
optional local HD pack); the data layer catalogues all 81 maps and opens every
interior through the area owner (52 of 80 complete; no viewer mode yet); headless
rules cover the game session, character stats, starting hero and one attack
resolution. Combat, magic, skills, NPC AI and the SCR dispatcher are researched
under `docs/research`. Details: `docs/project/status.md`.

- Toolchain: .NET SDK 8.0.425 (`global.json`, no roll-forward), net8.0,
  `TreatWarningsAsErrors`, central package versions + lock files
  (`restore --locked-mode`), MSTest. Godot **4.7.2 .NET**, `godot_canonical`
  wrapper and paths in `docs/workflow/toolchain.md`.
- Root checks: `dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && dotnet test --no-build --filter "TestCategory!=RealData"` and `python3 -B tools/research/dat_probe.py --self-test` and `python3 -B tools/original-oracle/oracle.py --self-test`.
  Root sln excludes the terrain viewer and its tests, `Arcanum.Input`, research
  probes and labs (own commands in `docs/workflow/toolchain.md`).
  Real-data tests are opt-in via env vars (`DAT_ROOT`, `ARCANUM_*`, `ARC_AREA_*`,
  `ARC_CAVE_*`, `ARC_NPC_*`) and categories `RealData`, `DlgRealData`,
  `DlgRuntimeRealData`, `NpcDialogEntryRealData`.
- Assemblies (`src/`): `Arcanum.Data` (DAT `1TAD` reader, resource precedence
  `Content`, MES `Messages`, ART, DLG `Dialogue`, terrain, static objects,
  universal map catalogue `Maps`, `Area` owner with Crash Site and whole-map
  plans, step evidence `Traversal`, T0 `Inventory`, `Lighting`, pre-made hero
  `Characters`, item/critter `Combat` fields, `Hd` pack/recipe reader, `Audio` sound catalogue, object sound fields and tile sound classes), `Arcanum.Simulation`
  (exact grid `Movement`, click-to-move `Navigation`, `Clock`, `Character`
  stats, `Party`, `Combat` attack resolver and life rules, `Equipment` wear rules, `Sound` event cue rules), `Arcanum.Scripting` (`Dialog`
  transition and conversation, `NpcDialog` entry, `GlobalFlags`),
  `Arcanum.Presentation` (animation timeline), `Arcanum.Input` (+
  `Arcanum.Input.Godot` adapter source), `Arcanum.Session` (one game-session
  owner: clock, GF, actor placement, map changes, story, party, inventory,
  starting hero, randomness, life state, event queue with life timers, NPC reaction, turn-based round, equipment,
  durable effects with blessings and curses, experience and levels, auto-level schemes, combat mode, music and ambient scheme state, quelled rumors, the hero's skills, the spell book, snapshot schema 22), `Arcanum.Godot` (ART viewer and HD
  before/after review). Data/Simulation/Scripting/Session must stay
  Godot-independent.
- Tools: `tools/terrain-viewer` (Core + Godot; ordinary modes `crash-site-area`
  (AREA) and `world-diagnostic` (WORLD), plus older diagnostic modes), `tools/terrain-inspect`, `tools/research/*` probes,
  `tools/arcanum-local` + `tools/local_dev` (local export/lease/retention
  authority — heavy, fail-closed). Labs: `experiments/{gamepad,display-ui,audio}-lab`.
- Launcher: `./arcanum setup|area|world|map|status|clean` wraps `tools/arcanum-local`. Never delete `artifacts/`
  or a build in it by hand, and never advise it: `./arcanum clean` and `docs/workflow/build-retention.md` say what may go. When a
  viewer mode gains or changes an argument or input, update `tools/local_dev/launch.py`
  (`GAME_ROLES`/`MODE_ROLES`/`ROLE_FLAGS`), `./arcanum` and the launcher docs in the
  same PR; `test_launcher_sync.py` catches drift.
- HD: a pack (PNG + JSON, `docs/formats/hd-pack.md`) is local, built from the
  player's own installation or imported from another of the player's devices,
  and never distributed; the AREA/WORLD viewers draw characters and objects from it with
  per-frame Legacy fallback, and the AREA also walls and level-0 terrain
  chunks since ARC-RENDER-016 (PR #206). Build/import tooling is ARC-DEV-003, deferred by the
  owner (not started).
- Hard rules: no original/extracted game content, nothing derived from it (HD
  packs and frames, screenshots, model weights), toolkit archives, secrets or
  personal paths in git (`toolkit/`, `artifacts/`, `local/`, `research-private/`
  are ignored); synthetic fixtures only; no source reconstruction is used or copied;
  record provenance; GitHub account `Simarglok` only; agent merges unless the owner explicitly asked to review.
- Style: dense, defensive, immutable records; explicit limits/options objects;
  typed failure results instead of exceptions at runtime boundaries; docs state
  scope and "NOT READY" boundaries precisely.

## 3. Truth hierarchy

1. **Code and git history** (prefer `origin/main` — see §4).
2. Repo docs: `docs/project/status.md` (component catalogue, updated by each
   task), `docs/requirements/coverage.md`, contracts under `docs/world`,
   `docs/scripting`, `docs/rendering`, `docs/formats`, research under
   `docs/research`, owner decisions in `docs/architecture/decisions.md`.
3. For how the **original game** behaves, the retail executable is the only
   reference; no source reconstruction is used (owner decision 2026-10-18,
   [plan](docs/research/original-oracle-research.md)). Facts not yet read in the
   executable carry their address at level F0 and are re-read when their domain
   is implemented (owner decision 2026-10-20).
   Check behaviour in the original 1.0.7.4 `Arcanum.exe`
   ([original executable check](docs/research/original-executable-check.md)):
   read it with `llvm-objdump` or headless Ghidra, or run a single function in the
   emulator oracle (allowed by the owner; `./arcanum oracle`, ARC-DEV-007);
   behaviour in prose, no bytes, listings or oracle outputs in git. Use it in every
   part of the game whenever a task needs original behaviour or an anomaly must be
   classified (reconstruction slip vs bug of the game, which decides the D8
   "Original"/"Steamwoven" settings). Only free tools with free licences.
4. Obsidian vault "Arcanum Steamwoven" — Russian research/decision notes from
   2026-09-19..21. **Stale**: it predates all code. Use for intent/decisions
   (DEC-001..020, provenance policy), never for implementation status.

## 4. Repositories and git state

- Remote `https://github.com/Simarglok/arcanum-steamwoven`. Many tasks merge a
  day, so the owner's local checkout often lags `origin/main`: fetch first and
  branch from `origin/main`, or read `git show origin/main:<path>` (only as
  fresh as the last fetch).
- `.worktrees/` (about 70 task worktrees as of 2026-10-13, ignored via `.gitignore`) and many
  `arcanum-steamwoven/t_<id>-arc-*` branches are left from the retired task board.
  Don't modify or delete them; they are evidence — except the one worktree your
  task explicitly names (e.g. ARC-GPAD-005: `.worktrees/t_6346b4d1`). ARC-GPAD-005
  (`arcanum-steamwoven/t_6346b4d1-arc-gpad-005-gamepad-lab`) merged as PR #67 on
  2026-10-14; the owner deferred its controller check (L3) to the first
  playable game build (2026-10-20).
  ARC-DATA-010 has no branch on the remote and is still todo.

## 5. Where the rules live

- `AGENTS.md` — agent rules, task readiness (Definition of Ready), evidence levels.
- `docs/workflow/tasks.md` — task authority, Definition of Ready, evidence levels
  and storage, review and merge rules.
- `docs/workflow/task-naming.md` — `ARC-TYPE-NNN` codes, type registry, thread,
  branch, PR and commit naming.
- `docs/workflow/toolchain.md` — exact SDK/Godot versions, `godot_canonical`,
  lab and export commands.
- `docs/project/status.md` — component catalogue; `docs/requirements/coverage.md` — status of
  every functional requirement, checked when creating a task and updated in the PR that
  changes it (`docs/workflow/tasks.md#functional-requirements`).
- `docs/architecture/decisions.md` — owner decisions (graphics, HD pack, controls,
  platforms); `docs/research/method.md` — research method and provenance;
  `docs/research/original-executable-check.md` — checking behaviour in the
  original executable.
