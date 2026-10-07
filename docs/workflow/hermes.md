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

## Local references

The repository checkout may contain ignored `toolkit/`. Isolated worktrees do not inherit ignored archives. A research card must provide the permitted local toolkit/data paths explicitly when needed. Keep those machine-specific paths in local Kanban context, not committed files. Do not copy the game or full vault into each worktree.

## GitHub account

The user requires `Simarglok` for this project. Follow the selection and verification steps in `AGENTS.md` before GitHub operations. Do not use `conductor-hermes-bot` or grant it access as a workaround. `gh auth switch` changes shared CLI state, so do not assume the account remains selected across concurrent work in other projects. Never expose tokens.
