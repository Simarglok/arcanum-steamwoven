# Hermes project workflow

Project: **Arcanum Steamwoven**. Project/board slug: `arcanum-steamwoven`.

The **Hermes Kanban board is the sole task queue**. Repository documents govern implementation; Obsidian holds research and discussions. Neither creates, prioritizes or dispatches work automatically.

## Universal team

Reuse the installed profiles from [conductor-hermes](https://github.com/ConductorPlatform/conductor-hermes); do not copy a project-specific fleet or change global routing.

| Profile | Role | Package model / effort |
|---|---|---|
| dev-orchestrator | Route accepted work and continuations | gpt-5.6-sol / medium |
| dev-planner | Bounded research planning / diagnosis when needed | gpt-5.6-sol / high |
| dev-developer | Implement and verify | gpt-5.6-terra / high |
| dev-reviewer | Independent review of exact candidate | gpt-5.6-sol / high |

These are the currently inspected package settings, not permanent model requirements. Profiles are shared across boards; board creation does not create separate copies. Set named assignees explicitly; never rely on global/default fallback for this project.

Attach `universal-development` explicitly to each runnable task/continuation. A clear small task can skip Planner. Developer requests native review by dev-reviewer; requested changes return to the original Developer and candidate. A review pass is not a merge. The maintainer merges.

## Task contract

Use a Kanban card with: ID/title, context, goal, allowed scope, sources and provenance, dependencies, acceptance criteria, checks, documentation deliverables and publication authority. Record base/head/workspace and reviewer findings at handoff.

Start with one bounded DAT research pilot when the user accepts its card. Do not queue the entire product vision, launch workers, or populate tasks merely as a side effect of project setup.

## Local references

The repository checkout may contain ignored `toolkit/`. Isolated worktrees do not inherit ignored archives. A research card must provide the permitted local toolkit/data paths explicitly when needed. Keep those machine-specific paths in local Kanban context, not committed files. Do not copy the game or full vault into each worktree.

## GitHub account

The user requires `Simarglok` for this project. Follow the selection and verification steps in `AGENTS.md` before GitHub operations. Do not use `conductor-hermes-bot` or grant it access as a workaround. `gh auth switch` changes shared CLI state, so do not assume the account remains selected across concurrent work in other projects. Never expose tokens.
