# CLAUDE.md — project guide for Claude sessions

This file is for Claude. The shared agent rules live in [`AGENTS.md`](AGENTS.md)
and the workflow detail in `docs/workflow/`; each line here is a rule in short,
and its link holds the detail.

## 1. Roles and rules

- **Default role: developer.** Work only on the task the owner gives you (a
  direct request, a Claude project thread or a named `ARC-*` work item) and
  follow `AGENTS.md`, [`docs/workflow/tasks.md`](docs/workflow/tasks.md) and
  [`docs/workflow/toolchain.md`](docs/workflow/toolchain.md).
- **One task code per thread**; the next code starts in a new thread
  ([one task per thread](docs/workflow/tasks.md#one-task-per-thread)).
- **Small context.** Search files first, then line ranges; no whole `cat`
  ([small search and read output](docs/workflow/tasks.md#small-search-and-read-output)).
  Batch independent tool calls in one response; update the checklist only when
  a step changes ([fewer steps](docs/workflow/tasks.md#fewer-steps)). Orient
  from the brief's entry files; a wide search goes to one Sonnet/Haiku Explore
  subagent returning at most 40 lines ([lean orientation](docs/workflow/tasks.md#lean-orientation)).
  Reviews run on Sonnet by default; Opus for a diff over about 800 lines or one
  that changes original-game mechanics, the save schema or threading
  ([review model](docs/workflow/tasks.md#review-model)). Per PR, run only the
  smoke stages the change touches or is directly related to, the Crash Site
  smoke only when related; the complete smoke run (every stage) runs only in
  the daily package build (`./arcanum daily`), when main changed since the last
  package ([smoke scope](docs/workflow/tasks.md#smoke-scope)). Run them with
  `./arcanum smoke pr --auto` (the zones the diff selects, printed with the
  reason; or `--stages S,... [--crash-site]`) and `./arcanum smoke complete`,
  never a scratch smoke script. Any PR that changes click or walk handling always
  runs `./arcanum smoke pr --stages map` and `./arcanum smoke self-test`, even
  when no other stage covers it ([smoke runner](docs/workflow/toolchain.md#smoke-runner-arc-dev-061)).
- **Naming** ([task naming](docs/workflow/task-naming.md)): `ARC-TYPE-NNN`
  first in threads, branches, PRs and commits, a Russian result in thread
  titles. Types: `RES`, `DATA`, `RENDER`, `WORLD`, `INPUT`, `GPAD`, `UI`,
  `SCRIPT`, `DEV`, `AI`, `COMBAT`, `SKILLS`, `MAGIC`, `TECHNOLOGY`. The
  coordinator checks the number is unused (git history, open/merged PRs,
  branches, other threads) before handing it out; the thread confirms with one
  check: remote branches and a PR search ([task code](docs/workflow/tasks.md#task-code)).
- **Language.** Talk to the owner in Russian; repository text (docs, code,
  commits, PR titles and bodies) stays in English.
- **Where work runs.** Work that builds, tests or runs code, Godot or original
  data runs on the owner's Mac (a Remote Control session in the owner's
  checkout). A docs-only task (only Markdown documentation changes) may run in
  a cloud clone without .NET or Godot and runs **no checks**; its PR says
  "docs-only, no checks run", and the independent review still applies.
- **Runs on the Mac.** Every Godot run, export and `dotnet` build/test goes
  through the FIFO queue `./arcanum run --kind window|headless|export|dotnet -- COMMAND`
  (`./arcanum queue` shows it; it replaces `lockf`)
  ([launch queue](docs/workflow/toolchain.md#launch-queue-arc-dev-033)). It is
  quiet: the whole output goes to a log file, stdout keeps PASS/FAIL and the
  first error ([quiet run output](docs/workflow/tasks.md#quiet-run-output)).
  Every Godot or launcher run must be seen to start the intended mode within a
  minute; a quiet `./arcanum run` checks it itself (start line, usage error),
  so run it blocking in one step; check any other run by hand
  ([checking that a run started](docs/workflow/toolchain.md#checking-that-a-run-started-arc-dev-029)).
- **Pull requests.** Every change goes through a PR from a branch (never push
  to `main`) as GitHub user `Simarglok`. A fresh subagent reviews the exact
  diff first. Once the acceptance criteria, task checks and root checks pass
  (none for docs-only) and the review is addressed, merge the PR yourself and
  report it, unless the owner explicitly asked to review it; then leave it open
  ([review, publication and merge](docs/workflow/tasks.md#review-publication-and-merge)).
- **Graphics changes** (owner rule 2026-11-11): post in the thread one
  composite image with labelled versions (before, after, any alternatives);
  forward it to the coordinator when the owner must decide; never commit the
  images ([graphics rule](docs/workflow/tasks.md#review-publication-and-merge)).
- **Stop and ask** with options when something you need is missing, a fix would
  change the task's contract, or the same failure repeats twice. Do not guess,
  mock or loop.
- **Secrets:** never open or print `.env*`, auth files, tokens or config values
  matching key/token/secret/password.
- **Retired Hermes.** The Hermes task board and the Codex workflow were removed
  on 2026-10-16 (PR #137): do not touch `~/.hermes` on the owner's Mac (old
  board, profiles, logs with secrets) and do not run `hermes`.

## 2. The project

Arcanum Steamwoven: independent MIT C#/Godot 4 reimplementation of *Arcanum: Of
Steamworks and Magick Obscura* that reads the player's original data.
Research-first, every component deliberately **bounded**; not a playable game
yet. Current state: the component catalogue
[`docs/project/status.md`](docs/project/status.md).

- Toolchain: .NET SDK 8.0.425 (`global.json`, no roll-forward), net8.0,
  `TreatWarningsAsErrors`, central package versions + lock files
  (`restore --locked-mode`), MSTest; Godot **4.7.2 .NET** through the
  `godot_canonical` wrapper ([toolchain](docs/workflow/toolchain.md)).
- Root checks:

  ```sh
  dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && dotnet test --no-build --filter "TestCategory!=RealData"
  python3 -B tools/research/dat_probe.py --self-test
  python3 -B tools/original-oracle/oracle.py --self-test
  ```

  The root solution leaves out the terrain viewer and its tests,
  `Arcanum.Input`, research probes and labs (own commands in the toolchain
  doc). Real-data tests are opt-in by env vars (`DAT_ROOT`, `ARCANUM_*`,
  `ARC_AREA_*`, `ARC_CAVE_*`, `ARC_NPC_*`) and categories (`RealData`,
  `DlgRealData`, `DlgRuntimeRealData`, `NpcDialogEntryRealData`)
  ([opt-in original data](docs/workflow/toolchain.md#opt-in-original-data)).
- Code: `src/` holds `Arcanum.Data`, `Arcanum.Simulation`,
  `Arcanum.Scripting`, `Arcanum.Presentation`, `Arcanum.Input`,
  `Arcanum.Session` (the one game-session owner), `Arcanum.World` (the Godot-free world runtime, GQ6; the record resolver first), `Arcanum.Executable` (the
  x86 interpreter of the player's `Arcanum.exe`) and `Arcanum.Godot`;
  Data/Simulation/Scripting/Session/World/Executable stay Godot-independent. `tools/` holds the
  terrain viewer (AREA and WORLD modes), `terrain-inspect`, research probes and
  the local export/lease/retention authority (`arcanum-local`, `local_dev`);
  labs live in `experiments/`.
- Launcher: `./arcanum` wraps `tools/arcanum-local`; `./arcanum package mac|win` builds the player's
  alpha from a fresh `origin/main` into `artifacts/packages/macos|windows` ([packaging](docs/workflow/packaging.md)). Never delete `artifacts/`
  or a build in it by hand, and never advise it: `./arcanum clean` and
  [build retention](docs/workflow/build-retention.md) say what may go. When a
  viewer mode gains or changes an argument or input, update
  `tools/local_dev/launch.py` (`GAME_ROLES`/`MODE_ROLES`/`ROLE_FLAGS`),
  `./arcanum` and the launcher docs in the same PR; `test_launcher_sync.py`
  catches drift.
- HD: a pack ([format](docs/formats/hd-pack.md)) is local, built from the
  player's own installation or imported from another of the player's devices,
  and never distributed. Build/import tooling ARC-DEV-003 is deferred by the
  owner. Storage, staging build, diff-only update, backup, rollback and the
  iCloud copy of the installed pack: [`docs/workflow/hd-pack.md`](docs/workflow/hd-pack.md)
  (`tools/hd_pack/hd_pack.py`, `--dry-run` first); never replace the installed
  pack folder or run `local/res095/tools/rebuild_hd.sh`.
- Hard rules: no original/extracted game content, nothing derived from it (HD
  packs and frames, screenshots, model weights), toolkit archives, secrets or
  personal paths in git (`toolkit/`, `artifacts/`, `local/`, `research-private/`
  are ignored); synthetic fixtures only; no source reconstruction is used or
  copied; record provenance.
- Style: dense, defensive, immutable records; explicit limits/options objects;
  typed failure results instead of exceptions at runtime boundaries; docs state
  scope and "NOT READY" boundaries precisely.

## 3. Truth hierarchy

1. **Code and git history** (prefer `origin/main`, §4).
2. Repo docs: the component catalogue `docs/project/status.md` and the coverage
   matrix `docs/requirements/coverage.md` (indexes; rows in their area folders),
   contracts under `docs/world`, `docs/scripting`, `docs/rendering`,
   `docs/formats`, research under `docs/research`, owner decisions in
   `docs/architecture/decisions.md`.
3. For how the **original game** behaves, the retail 1.0.7.4 `Arcanum.exe` is
   the only reference; no source reconstruction is used (owner decision
   2026-10-18, [plan](docs/research/original-oracle-research.md)). Read its code
   from the exports in `research-private/` first: search the decompiled C in
   `research-private/ghidra-export/c/` (`index.tsv`, `rg`; [Ghidra text
   export](docs/workflow/toolchain.md#ghidra-text-export-arc-dev-377)) and the
   listing, tables and byte scans beside it (index
   `research-private/ghidra-export/README.txt`; [what each
   holds](docs/workflow/toolchain.md#exe-exports-beyond-the-decompiled-c-arc-dev-379); the per-function `asm/`, `source-map/`, `tables/` and
   `names/` extras: [derived extras](docs/workflow/toolchain.md#exe-exports-derived-extras-arc-dev-385)), and go to the executable itself only for a
   function the exports lack, for a detail the
   decompiler's guesses leave open (operand width, signedness, wrap) or for run
   time behaviour (the emulator oracle, `./arcanum oracle`, ARC-DEV-007), per the
   [original executable check](docs/research/original-executable-check.md);
   behaviour in prose, no bytes, listings, decompiled text or oracle outputs in
   git; only free tools with free licences. Use it whenever a task needs original behaviour or
   an anomaly must be classified (reconstruction slip vs bug of the game, which
   decides the D8 "Original"/"Steamwoven" settings). Facts not yet read in the
   executable carry their address at level F0 and are re-read when their domain
   is implemented (owner decision 2026-10-20).
4. Obsidian vault "Arcanum Steamwoven" — Russian research/decision notes from
   2026-09-19..21. **Stale**: it predates all code. Use for intent/decisions
   (DEC-001..020, provenance policy), never for implementation status.

## 4. Repositories and git state

- Remote `https://github.com/Simarglok/arcanum-steamwoven`. The owner's local
  checkout often lags `origin/main`: fetch first and branch from `origin/main`,
  or read `git show origin/main:<path>`.
- `.worktrees/` and the `arcanum-steamwoven/t_<id>-arc-*` branches are evidence
  left from the retired task board: don't modify or delete them, except the one
  worktree your task explicitly names.

## 5. Where the rules live

- `AGENTS.md` — agent rules: task authority and readiness, implementation and
  evidence, GitHub identity, delivery.
- `docs/workflow/tasks.md` — task authority, Definition of Ready, token rules,
  functional requirements, evidence levels and storage, review and merge.
- `docs/workflow/task-naming.md` — codes, type registry, names.
- `docs/workflow/hd-pack.md` — the player's HD pack: where it lives, staging,
  diff-only update, backup, rollback, iCloud copy.
- `docs/workflow/toolchain.md` — versions, `godot_canonical`, launch check and
  queue, lab, export and oracle commands, opt-in real data.
- `docs/project/status.md` (catalogue) and `docs/requirements/coverage.md`
  (status of every functional requirement, checked when creating a task and
  updated in the PR that changes it); both, and the verification ledger, keep
  their full rows in area files
  ([area files](docs/workflow/tasks.md#area-files-of-the-catalogue-matrix-and-ledger)).
- `docs/architecture/decisions.md` (owner decisions), `docs/research/method.md`
  (research method and provenance),
  `docs/research/original-executable-check.md` (original behaviour).
