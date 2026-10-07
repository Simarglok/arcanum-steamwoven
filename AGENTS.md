# Arcanum Steamwoven — agent instructions

## Task authority

- Hermes Kanban board `arcanum-steamwoven` is authoritative for board-managed tasks and acceptance criteria. An explicit user instruction may also authorize bounded direct Codex work; it does not authorize creating or dispatching board tasks. Obsidian and repository roadmaps are knowledge/reference material, not task dispatch authority.
- For new board cards, follow the canonical [task naming policy](docs/workflow/task-naming.md) for titles, body fields, root ownership and idempotency. Native task IDs and dependency edges keep their separate meanings.
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Project development currently runs natively on macOS. Do not install Windows, run legacy executables, patch the installed game, or change shared Hermes profiles/settings unless the task explicitly covers it.

## Implementation and evidence

- Keep parsers and simulation independent of Godot. Use the pinned versions and process-local launch guidance in `docs/workflow/toolchain.md`; do not change shared installations implicitly.
- Prefer existing evidence; document unknown fields and link technical claims to versioned sources. Distinguish source claims, hypotheses and experiments.
- No mechanical copying from CE or CE-derived projects. Record provenance and applicable licenses before reuse; MIT labels alone do not prove independent lineage.
- Do not commit original/extracted game content, third-party toolkit archives, secrets or personal paths. Use synthetic public fixtures and opt-in local real-data checks. Keep the original installation unchanged.
- Validate offsets, sizes, decompression limits and untrusted archive paths. Do not infer gameplay/resource-precedence rules from a parser's convenience behavior.
- Run checks appropriate to changed behavior and all checks required by the accepted task. The repository has .NET tests and separate Godot/lab checks; see `docs/workflow/toolchain.md`. Documentation or source inspection alone is not runtime verification.

## GitHub identity

For this project, use the GitHub account `Simarglok`, not `conductor-hermes-bot`. Before GitHub operations, select it with `gh auth switch --hostname github.com --user Simarglok`, then verify `gh api user --jq .login` returns `Simarglok`. If an environment credential overrides the selected account, stop and resolve the mismatch without printing or copying secrets. This switch is shared CLI state, not repository-scoped; recheck identity before remote writes. It does not grant publication or merge authority.

## Delivery

For Hermes-managed work, use the installed universal profiles and `universal-development` skill described in `docs/workflow/hermes.md`. Direct Codex work follows its explicit user scope. Independent review targets the exact candidate. Report Implemented, Not implemented, Remaining work, checks and material uncertainties.

Publish a PR only when the accepted task authorizes it. For Hermes-managed tasks on board `arcanum-steamwoven`, read and follow `docs/workflow/merge-policy.md`: it grants `dev-reviewer` standing permission for a separate post-publication acceptance-and-merge stage after all its gates pass. Other roles and direct Codex work retain human merge. A narrower task restriction still applies. Never dispatch new work based solely on an Obsidian note or a proposed milestone.
