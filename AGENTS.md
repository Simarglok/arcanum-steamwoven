# Arcanum Steamwoven — agent instructions

## Task authority

- The accepted task and its acceptance criteria come from Hermes Kanban board `arcanum-steamwoven`. Obsidian and repository roadmaps are knowledge/reference material, not task dispatch authority.
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Project development currently runs natively on macOS. Do not install Windows, run legacy executables, patch the installed game, or change shared Hermes profiles/settings unless the task explicitly covers it.

## Implementation and evidence

- Keep parsers and simulation independent of Godot. Exact toolchain versions must be selected and documented by the first implementation task; none are implied by this scaffold.
- Prefer existing evidence; document unknown fields and link technical claims to versioned sources. Distinguish source claims, hypotheses and experiments.
- No mechanical copying from CE or CE-derived projects. Record provenance and applicable licenses before reuse; MIT labels alone do not prove independent lineage.
- Do not commit original/extracted game content, third-party toolkit archives, secrets or personal paths. Use synthetic public fixtures and opt-in local real-data checks. Keep the original installation unchanged.
- Validate offsets, sizes, decompression limits and untrusted archive paths. Do not infer gameplay/resource-precedence rules from a parser's convenience behavior.
- Run checks appropriate to changed behavior and all checks required by the accepted task. This docs-only foundation has no build/test suite; do not claim runtime verification.

## Delivery

Use the installed universal profiles and `universal-development` skill described in `docs/workflow/hermes.md`. Independent review targets the exact candidate. Report Implemented, Not implemented, Remaining work, checks and material uncertainties.

Publish a PR only when the accepted task authorizes it. Never merge; the user merges. Never dispatch new work based solely on an Obsidian note or a proposed milestone.
