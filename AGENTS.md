# Arcanum Steamwoven — agent instructions

## Task authority

- Hermes Kanban board `arcanum-steamwoven` is authoritative for board-managed tasks and acceptance criteria. An explicit user instruction may also authorize bounded direct Codex work; it does not authorize creating or dispatching board tasks. Obsidian and repository roadmaps are knowledge/reference material, not task dispatch authority.
- For new board cards, follow the canonical [task naming policy](docs/workflow/task-naming.md) for titles, body fields, root ownership and idempotency. Native task IDs and dependency edges keep their separate meanings.
- Confirm task ID, scope, assigned workspace and existing changes before editing. Work in the task's isolated checkout and preserve unrelated work.
- Read `docs/architecture/decisions.md`, `docs/research/method.md` and the relevant task references. Do not expand a bounded task into a full engine.
- Project development currently runs natively on macOS. Do not install Windows, run legacy executables, patch the installed game, or change shared Hermes profiles/settings unless the task explicitly covers it.

## Card readiness (Definition of Ready)

Every new root card on board `arcanum-steamwoven` must be ready before work starts; the board guard refuses stages under a root that is not ready and proof stages over budget. Full rules: [docs/workflow/hermes.md](docs/workflow/hermes.md#definition-of-ready-and-proof-budget); policy: `docs/workflow/readiness.json`. A root body (at most 12,000 characters) starts like this:

```text
Work-Item: ARC-TYPE-NNN
Card-Code: ARC-TYPE-NNN
Root-Task: SELF
Result: <one mergeable result, one PR>
Components: <at most 3, comma-separated>
Human-Gates: none
Proof-Budget: cards=8 runs=20 diag=2

AC1. <criterion an agent can check itself>
AC2. ...
```

- `Human-Gates:` is `none` or the codes of separate owner-validation work items (e.g. `ARC-INPUT-002`); `Proof-Budget:` is optional (the line above shows the defaults).
- At most 8 criteria `AC1`..`AC8`. A human, physical-device or native-GUI observation is never an `AC`: it goes into the separate owner-validation work item named in `Human-Gates`, which does not block merge.
- "Exhaustive", "certificate", "worst-case", "parity", "all possible" need an explicit bound (number, list of cases, limit) on the same line.
- The contract is fixed at the first run; only an explicit owner amendment on the root changes it.
- Budget per work item: 8 proof cards (INTAKE, PREFLIGHT, PLAN, IMPL, FIX, DIAG), 20 runs (rate-limited/reclaimed runs excluded), 2 DIAG. A proof-stage body is at most 8,000 characters and references the root instead of copying it. Review, publish, merge and close stages are never blocked.
- Guard messages: `READINESS_INVALID` about your own new card — fix it. `ROOT_NOT_READY` or `PROOF_BUDGET_EXCEEDED` — do not work around it: block with `needs_input` and ask the owner (accept the practical contract / narrow / stop / raise the budget with `/arc budget`).
- Evidence levels: L1 automatic (every correction), L2 native automatic (before review when required), L3 human (separate work item, never a gate). Keep evidence in the candidate branch or card attachments, not only in ignored local directories.

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
