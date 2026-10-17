# Arcanum Steamwoven — agent instructions

## Task authority

- Work starts only from an explicit owner request: a direct instruction, a Claude project thread or a named `ARC-*` work item. Obsidian and repository roadmaps are knowledge/reference material, not task authority. Full rules: [task workflow](docs/workflow/tasks.md).
- Every work item has a code under the [task naming policy](docs/workflow/task-naming.md); thread titles, branches, PR titles and commit subjects carry it first (PR titles and commits may put a short area prefix before it). The policy holds the type registry (`RES`, `DATA`, `RENDER`, `WORLD`, `INPUT`, `GPAD`, `UI`, `SCRIPT`, `DEV`, `AI`, `COMBAT`, `SKILLS`, `MAGIC`, `TECHNOLOGY`). Whoever allocates a number (the owner or an owner-authorized session) first checks that it is unused: git history, open and merged PRs, branches and codes reserved in the project.
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Project development currently runs natively on macOS: work that builds, tests or runs code, Godot or original data runs on the owner's Mac. A docs-only task (only Markdown documentation changes) may run in a cloud checkout without .NET or Godot and runs no checks ([docs-only rule](docs/workflow/tasks.md#review-publication-and-merge)). Do not install Windows, run legacy executables (single functions in the emulator of the [original executable check](docs/research/original-executable-check.md#function-emulation-oracle) are allowed), patch the installed game, or change shared tool profiles/settings unless the task explicitly covers it.
- The former Hermes task board and Codex workflow are retired: do not read `~/.hermes` or run `hermes`.

## Task readiness (Definition of Ready)

Check a new work item against the [Definition of Ready](docs/workflow/tasks.md#definition-of-ready) before work starts. A written task contract starts like this:

```text
Work-Item: ARC-TYPE-NNN
Result: <one mergeable result, one PR>
Components: <at most 3, comma-separated>
Human-Gates: none

AC1. <criterion an agent can check itself>
AC2. ...
```

- `Human-Gates:` is `none` or the codes of separate owner-validation work items (e.g. `ARC-INPUT-002`).
- At most 8 criteria `AC1`..`AC8`. A human, physical-device or native-GUI observation is never an `AC`: it goes into the separate owner-validation work item named in `Human-Gates`, which does not block merge.
- "Exhaustive", "certificate", "worst-case", "parity", "all possible" need an explicit bound (number, list of cases, limit) on the same line.
- The contract is fixed when work starts; only an explicit owner amendment changes it.
- When the work outgrows its contract or the same failure repeats twice, stop and ask the owner with options (accept the practical contract / narrow / stop / continue with a larger allowance) instead of looping.
- Evidence levels: L1 automatic (every correction), L2 native automatic (before review when required), L3 human (separate work item, never a gate). Keep evidence in the candidate branch or the PR, not only in ignored local directories.

## Implementation and evidence

- Keep parsers and simulation independent of Godot. Use the pinned versions and process-local launch guidance in `docs/workflow/toolchain.md`; do not change shared installations implicitly.
- Prefer existing evidence; document unknown fields and link technical claims to versioned sources. Distinguish source claims, hypotheses and experiments.
- No mechanical copying from CE or CE-derived projects. Record provenance and applicable licenses before reuse; MIT labels alone do not prove independent lineage.
- Original behaviour: when a task needs to know how the original game behaves, or a CE branch looks like a slip, check it in the original executable at the address CE gives ([original executable check](docs/research/original-executable-check.md)): a reading, or the function oracle once ARC-DEV-007 provides it. The install stays unchanged; record behaviour in prose and the verdict ("CE reconstruction slip" or "in the original"); never commit bytes, listings, decompiled code or the oracle's generated inputs and outputs. CE is being phased out (owner decision 2026-10-17, [plan](docs/research/original-oracle-research.md)): new work adds no CE reference beyond the function it checks, and only free tools with free licences are used.
- Do not commit original/extracted game content, third-party toolkit archives, secrets or personal paths. Nothing derived from original art enters git either: HD packs, upscaled frames, real manifests, model weights or adapters, screenshots and recordings stay local; an HD pack is built or imported on the player's device and never distributed ([HD pack format](docs/formats/hd-pack.md)). Use synthetic public fixtures and opt-in local real-data checks. Keep the original installation unchanged.
- Validate offsets, sizes, decompression limits and untrusted archive paths. Do not infer gameplay/resource-precedence rules from a parser's convenience behavior.
- Launcher sync: a change that adds or changes a viewer mode's arguments or original inputs updates `tools/local_dev/launch.py` (`GAME_ROLES`, `MODE_ROLES`, `ROLE_FLAGS`), the root `./arcanum` launcher when it adds a mode, and [the launcher docs](docs/workflow/local-development.md#project-launcher) in the same PR; `tools/local_dev/test_launcher_sync.py` must pass.
- Run checks appropriate to changed behavior and all checks required by the accepted task, plus the root checks (a docs-only change runs none of them; see [the docs-only rule](docs/workflow/tasks.md#review-publication-and-merge)):

  ```sh
  dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && dotnet test --no-build --filter "TestCategory!=RealData"
  python3 -B tools/research/dat_probe.py --self-test
  ```

  The root solution leaves out the terrain viewer and its tests, the `Arcanum.Input` project, research probes and labs, which have their own commands in `docs/workflow/toolchain.md`. Documentation or source inspection alone is not runtime verification.

## GitHub identity

For this project, use the GitHub account `Simarglok`, not a bot or other account. Before GitHub operations, select it with `gh auth switch --hostname github.com --user Simarglok`, then verify `gh api user --jq .login` returns `Simarglok`. If an environment credential overrides the selected account, stop and resolve the mismatch without printing or copying secrets. A cloud session without `gh` uses the GitHub tools it is given, which must act as `Simarglok`; check the account before the first remote write. This switch is shared CLI state, not repository-scoped; recheck identity before remote writes. It does not grant publication or merge authority.

## Delivery

Independent review targets the exact candidate diff before the PR is opened. Report Implemented, Not implemented, Remaining work, checks and material uncertainties.

Publish a PR only when the accepted task authorizes it. The agent merges its own PR once the acceptance criteria, task and root checks pass (none for a docs-only change) and the review is addressed, unless the owner explicitly asked to review it; follow [review, publication and merge](docs/workflow/tasks.md#review-publication-and-merge). A narrower task restriction still applies. Never start new work based solely on an Obsidian note or a proposed milestone.
