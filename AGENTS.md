# Arcanum Steamwoven — agent instructions

Each rule below is stated in short; the linked section of `docs/workflow/` or
`docs/research/` holds its detail and wins on wording.

## Task authority

- Work starts only from an explicit owner request: a direct instruction, a Claude project thread or a named `ARC-*` work item. Obsidian and repository roadmaps are reference material, not task authority ([task workflow](docs/workflow/tasks.md#task-authority)).
- Every work item has a code under the [task naming policy](docs/workflow/task-naming.md) (type registry there); thread titles, branches, PR titles and commit subjects carry it first. Whoever allocates a number first checks that it is unused: git history, open and merged PRs, branches and codes reserved in the project.
- One task code per thread and session: when its PR merges, goes to the owner for review or is cancelled, report, write the handoff and end ([one task per thread](docs/workflow/tasks.md#one-task-per-thread), owner decision 2026-10-21).
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Work that builds, tests or runs code, Godot or original data runs natively on the owner's Mac. A docs-only task may run in a cloud checkout without .NET or Godot and runs no checks ([docs-only rule](docs/workflow/tasks.md#review-publication-and-merge)). Do not install Windows, run legacy executables (single functions in the [function emulation oracle](docs/research/original-executable-check.md#function-emulation-oracle) are allowed), patch the installed game, or change shared tool profiles/settings unless the task explicitly covers it.
- The former Hermes task board and Codex workflow are retired: do not read `~/.hermes` or run `hermes`.

## Task readiness (Definition of Ready)

Check a new work item against the [Definition of Ready](docs/workflow/tasks.md#definition-of-ready) before work starts: one result, one PR, at most three components, a contract header (`Work-Item`, `Result`, `Components`, `Human-Gates`, `Requirements`) and criteria `AC1`..`AC8` an agent can check itself. A human, physical-device or native-GUI observation is never an `AC`: it goes into the separate owner-validation work item named in `Human-Gates`, which does not block merge. "Exhaustive", "certificate", "worst-case", "parity", "all possible" need an explicit bound on the same line. The contract is fixed when work starts; only an explicit owner amendment changes it. When the work outgrows its contract or the same failure repeats twice, stop and ask the owner with options instead of looping.

- `Requirements:` names the functional requirements the task serves from the [coverage matrix](docs/requirements/coverage.md); the PR that changes a requirement's coverage updates its row in the area file and the summary counts ([functional requirements](docs/workflow/tasks.md#functional-requirements)).
- [Evidence levels](docs/workflow/tasks.md#evidence-levels): L1 automatic (every correction), L2 native automatic (before review when required), L3 human (separate work item, never a gate). Keep evidence in the candidate branch or the PR, not only in ignored local directories.

## Implementation and evidence

- Small search and read output: files or counts first, then at most 20 lines cut to 200 characters; read ranges of at most 120 lines, never `cat` a whole source file or doc, grep a saved large output; grep only the needed row of the catalogue, matrix and ledger area files; give subagents the diff or exact ranges ([small search and read output](docs/workflow/tasks.md#small-search-and-read-output), [area files](docs/workflow/tasks.md#area-files-of-the-catalogue-matrix-and-ledger)).
- Few steps: independent tool calls in one response; update the progress checklist only when a step changes, together with other work; a coordinator sends a running lane only what needs action ([fewer steps](docs/workflow/tasks.md#fewer-steps)).
- Lean orientation: start from the entry files, types and docs the brief names; a search wider than a few files goes to one Explore subagent on Sonnet or Haiku that returns at most 40 lines (paths, ranges, answer) ([lean orientation](docs/workflow/tasks.md#lean-orientation)).
- Review model: the independent review runs on Sonnet; Opus for a diff over about 800 changed lines or one that changes original-game mechanics, the save schema or threading; executable readings stay on Opus ([review model](docs/workflow/tasks.md#review-model)).
- Smoke scope: per PR the touched smoke stages plus the Crash Site smoke; the full map smoke chain before every `./arcanum package` and once a day on main; a stage that also fails on clean main is named in the PR, not investigated ([smoke scope](docs/workflow/tasks.md#smoke-scope)).
- Task code: the coordinator checks the code is unused before handing it out; the thread confirms with one check (remote branches and a PR search); no checklist-only steps ([task code](docs/workflow/tasks.md#task-code)).
- Quiet run output: `./arcanum run` logs the whole output to a file and prints PASS or FAIL with the first error; check a run in its log with a narrow search; `dotnet` gets `-v q --nologo` and tests `--logger "console;verbosity=minimal"` ([quiet run output](docs/workflow/tasks.md#quiet-run-output)).
- Keep parsers and simulation independent of Godot. Use the pinned versions and process-local launch guidance in `docs/workflow/toolchain.md`; do not change shared installations implicitly.
- Prefer existing evidence; document unknown fields and link technical claims to versioned sources. Distinguish source claims, hypotheses and experiments.
- No mechanical copying from a reconstruction of the original's source or from projects derived from one; the project uses none (owner decision 2026-10-18). Record provenance and applicable licenses before reuse; MIT labels alone do not prove independent lineage.
- Original behaviour is checked in the original executable at the function's address ([original executable check](docs/research/original-executable-check.md)): a reading, or the function oracle (`./arcanum oracle`, ARC-DEV-007). A change to a function that has an oracle (`ORACLE_FUNCTIONS` in `tools/local_dev/launch.py`) runs `./arcanum oracle --function ADDR` on the owner's Mac, reports its result and commits its receipts to `docs/verification/receipts/`. A method implementing an original function carries `[OriginalFunction(address, scope)]` and its row in the [function registry](docs/verification/README.md); root tests keep the registry, attributes, receipts and rule-mode switches consistent and fail when a tracked file names the reconstruction. Record behaviour in prose with the verdict ("reconstruction slip" or "in the original"); never commit bytes, listings, decompiled code or oracle inputs and outputs. F0 facts keep their address; a task that implements or changes such a function reads it in the executable first (owner decision 2026-10-20). Only free tools with free licences.
- Original bugs ([rule modes](docs/world/rule-modes.md#what-is-a-mode-difference), owner rule 2026-10-28): a technical bug (slot leak, dead hotkey, crash) is fixed in both modes without a switch; a gameplay bug (behaviour that raises questions for a player) keeps the original behind an Original switch and is fixed in Steamwoven.
- Do not commit original/extracted game content, third-party toolkit archives, secrets or personal paths, or anything derived from original art (HD packs, upscaled frames, real manifests, model weights or adapters, screenshots, recordings); an HD pack is built or imported on the player's device and never distributed ([HD pack format](docs/formats/hd-pack.md)). Use synthetic public fixtures and opt-in local real-data checks. Keep the original installation unchanged.
- Validate offsets, sizes, decompression limits and untrusted archive paths. Do not infer gameplay/resource-precedence rules from a parser's convenience behavior.
- Never delete `artifacts/`, a build in it or a local home by hand, and never advise it as "rebuildable"; report and remove only with `./arcanum clean` ([which builds may be deleted](docs/workflow/build-retention.md), ARC-DEV-028).
- Launcher sync: a change to a viewer mode's arguments or original inputs updates `tools/local_dev/launch.py` (`GAME_ROLES`, `MODE_ROLES`, `ROLE_FLAGS`), the root `./arcanum` launcher when it adds a mode, and [the launcher docs](docs/workflow/local-development.md#project-launcher) in the same PR; `tools/local_dev/test_launcher_sync.py` must pass.
- Launch check: within the first minute confirm a Godot or launcher run started the intended mode, or stop it, fix the command and rerun; a run that did not start is not evidence, and a command given to the owner is first run or checked with `--dry-run` or `--help` ([checking that a run started](docs/workflow/toolchain.md#checking-that-a-run-started-arc-dev-029)).
- Launch queue: every Godot run, export and `dotnet` build or test goes through `./arcanum run --kind window|headless|export|dotnet -- COMMAND`, never two Godot runs at once and never the retired `lockf` rule ([launch queue](docs/workflow/toolchain.md#launch-queue-arc-dev-033), ARC-DEV-033).
- Run the checks for the changed behaviour, the accepted task's checks and the root checks (none for a docs-only change):

  ```sh
  dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && dotnet test --no-build --filter "TestCategory!=RealData"
  python3 -B tools/research/dat_probe.py --self-test
  python3 -B tools/original-oracle/oracle.py --self-test
  ```

  The oracle self-test installs its pinned Unicorn into an ignored local virtual environment on first use. The root solution leaves out the terrain viewer and its tests, `Arcanum.Input`, research probes and labs ([toolchain](docs/workflow/toolchain.md#root-solution-synthetic-checks)). Documentation or source inspection alone is not runtime verification.

## GitHub identity

Use the GitHub account `Simarglok`, not a bot or other account. Before GitHub operations, run `gh auth switch --hostname github.com --user Simarglok` and verify that `gh api user --jq .login` returns `Simarglok`. If an environment credential overrides the selected account, stop and resolve the mismatch without printing or copying secrets. A cloud session without `gh` uses the GitHub tools it is given, which must act as `Simarglok`; check the account before the first remote write. The switch is shared CLI state: recheck identity before remote writes. It does not grant publication or merge authority.

## Delivery

An independent review targets the exact candidate diff before the PR is opened. Publish a PR only when the accepted task authorizes it. The agent merges its own PR once the acceptance criteria, task and root checks pass (none for a docs-only change) and the review is addressed, unless the owner explicitly asked to review it ([review, publication and merge](docs/workflow/tasks.md#review-publication-and-merge)); a narrower task restriction still applies. Report Implemented, Not implemented, Remaining work, checks and material uncertainties. Never start new work based solely on an Obsidian note or a proposed milestone.
