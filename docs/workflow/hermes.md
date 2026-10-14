# Hermes project workflow

Project: **Arcanum Steamwoven**. Project/board slug: `arcanum-steamwoven`.

The **Hermes Kanban board is the authoritative queue for Hermes-managed work**. Explicit user instructions may authorize bounded direct Codex work without a Hermes card; this does not alter or dispatch board tasks. Repository documents govern implementation; Obsidian holds research and discussions. Neither creates, prioritizes or dispatches work automatically.

## Universal team

Reuse the installed profiles from [conductor-hermes](https://github.com/ConductorPlatform/conductor-hermes); do not copy a project-specific fleet or change global routing.

Operational role definitions, model routing and effort settings belong to conductor-hermes. Inspect its current installed package for each managed run; this repository does not retain a model snapshot or a project-specific fleet. Profiles are shared across boards. Set named assignees explicitly; never rely on global/default fallback for this project.

Attach `universal-development` explicitly to each runnable task/continuation. A clear small task can skip Planner. Developer requests native review by dev-reviewer; requested changes return to the original Developer and candidate. A review pass is not a merge. For Hermes-managed tasks, [the project merge policy](merge-policy.md) authorizes `dev-reviewer` to perform a separate post-publication acceptance-and-merge stage when all scope, independent PASS, check, identity and PR-head gates pass. This standing permission applies only to this project; narrower task contracts and existing human gates still apply. Other roles and direct Codex work retain human merge.

## Task contract

Use a Kanban card with: ID/title, context, goal, allowed scope, sources and provenance, dependencies, acceptance criteria, checks, documentation deliverables and publication authority. Record base/head/workspace and reviewer findings at handoff.

For every new board card, follow the [canonical task naming policy](task-naming.md) for the human title, machine-readable body fields, root ownership and idempotency key. Keep native task IDs stable and distinguish root ownership from dependency parents.

Continue from current accepted board work and the [merged component catalogue](../project/status.md). Do not queue the entire product vision, launch workers, or populate tasks merely as a side effect of project setup.

## Definition of Ready and proof budget

Owner decision 2026-10-14 (see `docs/hermes/decomposition-analysis-2026-10-14.md`): work stalls when one work item carries several results, human or device observations, or unbounded proofs. A root card is ready only when it follows these rules; the machine-readable policy is [readiness.json](readiness.json) and the board guard enforces it when cards are created through Hermes.

1. **One result, one PR, at most three components.** After the three identity lines the root body names them:

   ```text
   Result: <one mergeable result>
   Components: <at most 3, comma-separated>
   Human-Gates: none | ARC-TYPE-NNN[, ...]
   Proof-Budget: cards=8 runs=20 diag=2      (optional; these are the defaults)
   ```

2. **Only criteria an agent can check.** Acceptance criteria are labelled `AC1` ... `AC8` (at most eight). A human, physical-device or native-GUI observation is not an acceptance criterion of an agent work item: put it into the separate owner-validation work item named in `Human-Gates`. That work item never blocks merge. Saying that there is *no* such gate is fine.
3. **Every proof is finite.** "Exhaustive", "certificate", "worst-case", "parity", "all possible" and similar are allowed only with an explicit bound on the same line: a number, a list of cases, a limit or a reference.
4. **The contract is fixed at the first run.** Only an explicit owner amendment recorded on the root changes acceptance; comments that propose new criteria do not.
5. **Proof budget.** A work item may use at most 8 proof cards (INTAKE, PREFLIGHT, PLAN, IMPL, FIX, DIAG), 20 runs (rate-limited and reclaimed runs do not count), and 2 DIAG cards, unless the root's `Proof-Budget:` or the owner's `/arc budget` says otherwise. At the limit the guard refuses new proof cards (`PROOF_BUDGET_EXCEEDED`); the worker blocks with `needs_input` and the owner chooses: accept the practical contract, narrow it, stop, or raise the budget. Review, publish, merge and close stages are never blocked.
6. **Size.** A root body is at most 12,000 characters and a proof-stage body at most 8,000. A stage references the root instead of copying its contract; long evidence goes into attachments or files.

Roots created before 2026-10-15 are exempt from rules 1-3 and from the root-size part of rule 6 at stage creation; their new proof stages still obey the stage-size limit and count against the budget. Roots made outside Hermes (CLI, dashboard, Codex) are checked when their first stage is created (`ROOT_NOT_READY`), so check a new root against this list before creating it.

## Evidence levels

| Level | Who and when | Evidence |
| --- | --- | --- |
| L1 automatic | agent, every correction | synthetic tests, headless checks, frame readback from inside the application, programmatic input |
| L2 native automatic | agent, before review when the work item requires it | the same build in a real window on the host with a scripted observer (frames, input log, clean exit); a logged-in GUI session is a precondition checked before the run. Verified 2026-10-14: in-application frame readback works from a launchd job without Screen Recording permission. Always launch through `godot_canonical` (`docs/workflow/toolchain.md`): without `DOTNET_ROOT` the mono build shows a modal .NET error dialog |
| L3 human | owner, once per work item or milestone, in batches | short owner checklist in a separate owner-validation work item; never gates agent stages or merge |

If an older contract still demands L3 inside an agent stage, the agent records that gate as NOT_VERIFIED and asks the owner once instead of looping.

## Evidence storage

Evidence that must survive goes into the candidate branch (when it is allowed in git) or into card attachments, never only into ignored `local/`, `research-private/` or a workspace that can be removed. For original-data results that cannot be committed, attach paths and digests.

## Local references

The repository checkout may contain ignored `toolkit/`. Isolated worktrees do not inherit ignored archives. A research card must provide the permitted local toolkit/data paths explicitly when needed. Keep those machine-specific paths in local Kanban context, not committed files. Do not copy the game or full vault into each worktree.

## GitHub account

The user requires `Simarglok` for this project. Follow the selection and verification steps in `AGENTS.md` before GitHub operations. Do not use `conductor-hermes-bot` or grant it access as a workaround. `gh auth switch` changes shared CLI state, so do not assume the account remains selected across concurrent work in other projects. Never expose tokens.
