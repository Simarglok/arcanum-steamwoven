# Arcanum Steamwoven — agent instructions

## Task authority

- Work starts only from an explicit owner request: a direct instruction, a Claude project thread or a named `ARC-*` work item. Obsidian and repository roadmaps are knowledge/reference material, not task authority. Full rules: [task workflow](docs/workflow/tasks.md).
- Every work item has a code under the [task naming policy](docs/workflow/task-naming.md); thread titles, branches, PR titles and commit subjects carry it first (PR titles and commits may put a short area prefix before it).
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Project development currently runs natively on macOS. Do not install Windows, run legacy executables, patch the installed game, or change shared tool profiles/settings unless the task explicitly covers it.

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
- Do not commit original/extracted game content, third-party toolkit archives, secrets or personal paths. Use synthetic public fixtures and opt-in local real-data checks. Keep the original installation unchanged.
- Validate offsets, sizes, decompression limits and untrusted archive paths. Do not infer gameplay/resource-precedence rules from a parser's convenience behavior.
- Launcher sync: a change that adds or changes a viewer mode's arguments or original inputs updates `tools/local_dev/launch.py` (`GAME_ROLES`, `MODE_ROLES`, `ROLE_FLAGS`), the root `./arcanum` launcher when it adds a mode, and [the launcher docs](docs/workflow/local-development.md#project-launcher) in the same PR; `tools/local_dev/test_launcher_sync.py` must pass.
- Run checks appropriate to changed behavior and all checks required by the accepted task. The repository has .NET tests and separate Godot/lab checks; see `docs/workflow/toolchain.md`. Documentation or source inspection alone is not runtime verification.

## GitHub identity

For this project, use the GitHub account `Simarglok`, not a bot or other account. Before GitHub operations, select it with `gh auth switch --hostname github.com --user Simarglok`, then verify `gh api user --jq .login` returns `Simarglok`. If an environment credential overrides the selected account, stop and resolve the mismatch without printing or copying secrets. This switch is shared CLI state, not repository-scoped; recheck identity before remote writes. It does not grant publication or merge authority.

## Delivery

Independent review targets the exact candidate diff before the PR is opened. Report Implemented, Not implemented, Remaining work, checks and material uncertainties.

Publish a PR only when the accepted task authorizes it. The owner merges unless the owner authorized the agent to merge for the task or as a standing project instruction; then follow [review, publication and merge](docs/workflow/tasks.md#review-publication-and-merge). A narrower task restriction still applies. Never start new work based solely on an Obsidian note or a proposed milestone.
