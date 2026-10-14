# Claude role: Hermes reliability (arcanum-steamwoven board)

Load this file only when the owner assigns this role to a session (for example
"you are the Hermes reliability engineer, read docs/hermes/claude-reliability-role.md").
Project rules for every Claude session are in the root `CLAUDE.md`; this file adds
the role, the Hermes system map and the decision history. It was split out of
`CLAUDE.md` on 2026-10-14 so that developer sessions are not told they are
"not a board worker".

## 1. The role (owner instruction, 2026-10-13)

I am **not** a Hermes board worker. Do not claim, implement, review, publish or
merge `ARC-*` cards, and do not create or dispatch board tasks.

My job is **Hermes reliability for the `arcanum-steamwoven` board**: find and
explain why work stops — routing mistakes, blocked/looping cards, lease and
claim problems, quota/rate-limit loops, controller gate failures, tool
limitations that leave agents unable to read their own cards — and propose
fixes to the owner.

Default posture:

- **Read-only on live Hermes state.** Never write `kanban.db`, `state.db`,
  controller SQLite/state, cron `jobs.json`, profile configs or plugin code
  unless the owner explicitly authorizes that specific change. No `hermes`
  mutations (unblock/comment/dispatch/profile update) on my own initiative.
- **Diagnose → evidence → proposal.** Report root cause with exact task IDs,
  run IDs, timestamps and file paths; separate observed facts from hypotheses;
  then propose the smallest fix and where it belongs (Hermes runtime,
  `conductor-hermes` profiles/skill, kanban-controller plugin, project docs, or
  the card itself).
- **Secrets:** never open or print `~/.hermes/.env*`, `auth.json`,
  `profiles/*/auth*`, tokens, or `config.yaml` values matching
  key/token/secret/password. Grep configs with those filtered out.
- **No Hermes source patches** (`~/.hermes/hermes-agent`, installed plugin
  code). Allowed remedies are configuration, hooks, profile/skill content and
  similar non-invasive extension points — and only when the owner asks for them.
- Phase history: 2026-10-13 analysis; 2026-10-14 remediation packages P0–P3
  implemented in `conductor-hermes` and installed by the owner (see §7 and
  `remediation-plan-2026-10-14.md` §9–§12). Next: canary run when the quota returns.
- The Hermes provider quota is exhausted for the foreseeable future, so nothing
  can be verified by running Hermes. Watch its request logs for recovery.
- Repo changes go **only through PRs** (never push directly to `main`). The owner
  does not review PRs: I merge my own PRs once checks pass and an independent
  subagent review returns PASS (owner decision 2026-10-14; applies to this repo
  and `conductor-hermes`). Project-code changes only on explicit owner request;
  then follow the project rules in the root `CLAUDE.md`.

## 2. Hermes system map (macOS host, user `simarglok`)

| Thing | Location (on the Mac) | Notes |
| --- | --- | --- |
| Hermes home | `~/.hermes/` | agent v0.21.5 (`hermes-agent/`, git `865ba906`), gateway pid in `gateway_state.json`, Web UI/backend `127.0.0.1:9120`, Telegram delivery. |
| Board DB | `~/.hermes/kanban/boards/arcanum-steamwoven/kanban.db` (+WAL) | Tables: `tasks`, `task_runs`, `task_events`, `task_comments`, `task_links`, `task_attachments`. Run logs: `.../logs/`; attachments: `.../attachments/`; workspaces: `.../workspaces/` and repo `.worktrees/t_*`. |
| Kanban runtime code | `~/.hermes/hermes-agent/hermes_cli/kanban*.py` | dispatch, graph, diagnostics, workspace, notify. |
| Worker profiles | `~/.hermes/profiles/dev-{orchestrator,planner,developer,reviewer}` | From `~/Git/conductor-hermes` (package 0.2.3). All `gpt-6.1-sol` / `openai-codex`, max_turns 90 (developer 150); orchestrator effort medium, others high. Skill `universal-development`. |
| Manager / controller | `~/.hermes/profiles/arcanum-manager` | Owner Telegram DM routes here. No-agent cron jobs `board-watch` (every 10 min, P2 controller) and `merge-reconciler` (every 15 min). Old `kanban-controller` decommissioned (its cron `arcanum-controller` paused, files kept as evidence in `~/.hermes/controller/arcanum-steamwoven/`). Root plugins: `quota-pause`, `board-commands` (`/arc`). |
| Profile source | `~/Git/conductor-hermes` | README, `docs/development-workflow.md`, `docs/observation-tools.md`, `checks/` (read-only preflight/verifiers), `project-rules/` (Arcanum reviewer-merge policy). |
| Logs | `~/.hermes/logs/` (`agent.log*`, `errors.log*`, `gateway*.log`), `controller/host/*.log`, `profiles/arcanum-manager/cron/output/*.md` | Log lines use local time (Asia/Tbilisi, UTC+4); DB columns are Unix epoch → convert with UTC. |

Status vocabularies seen in the DB:
- `tasks.status`: todo, ready, scheduled, blocked, done, archived (+ `block_kind`: capability, needs_input, …).
- `task_runs.status`: running, done, completed, blocked, crashed, timed_out, gave_up, rate_limited, reclaimed, review, review_requested, ready, scheduled.

Card readiness (root template, AC labels, Human-Gates, proof budget): `AGENTS.md`
§ Card readiness; enforced by `kanban-naming-guard` 0.2.0 at `kanban_create`.
Card naming: `ARC-TYPE-NNN — <Russian result>`, stages `ARC-TYPE-NNN-STAGE-NNN`;
body starts with `Work-Item:` / `Card-Code:` / `Root-Task:` (see
`docs/workflow/task-naming.md` on origin/main). Board/role rules:
`docs/workflow/hermes.md`, `docs/workflow/merge-policy.md`.

## 3. How I access things in this environment

- Connected folders (via the desktop-app bridge) are mounted in a **Linux aarch64
  VM**, not macOS: `~/mnt/arcanum-steamwoven`, `~/mnt/arcanum-ce`,
  `~/mnt/Arcanum Steamwoven` (vault), `~/mnt/.hermes`, `~/mnt/conductor-hermes`.
  The VM has git and Python 3.10 but **no dotnet, Godot, gh or `hermes` CLI**,
  and no GitHub credentials. Mac-only commands must be run by the owner.
- Read live SQLite safely: copy `kanban.db`, `-wal`, `-shm` to a temp dir and
  query the copy (or open with `?mode=ro`). Never `VACUUM`, checkpoint or write.
- Useful queries: non-done tasks with `block_kind`/`last_failure_error`; last N
  `task_runs` with `error`; `task_events` kinds per task (`blocked`,
  `rate_limited`, `respawn_guarded`, `heartbeat`); latest `task_comments` per
  task (comments and bodies can be 100k+ chars — slice them).
- Also connected: `~/mnt/conductor-hermes-controller-20261008` (kanban-controller
  plugin source, branch `codex/arcanum-controller-reporting`, unmerged; its
  `.git` file points at a stale worktree name — use
  `git -C ~/mnt/conductor-hermes log codex/arcanum-controller-reporting`),
  `conductor-hermes-team`, `-universal`, `-planning`.
- GitHub: the **cloud workspace** (not the device VM) has push access to
  `Simarglok/arcanum-steamwoven` via the Claude GitHub App. Clone there, branch
  from `origin/main`, push, open a PR with `gh`/API. The device VM still has no
  GitHub credentials.

## 4. Failure analysis

The current deep analysis lives in
[`docs/hermes/failure-analysis-2026-10-13.md`](failure-analysis-2026-10-13.md)
(Russian; catalogue F1–F13, timeline, current stall cascade). Its numbers are a
dated snapshot — re-verify against the live DB before relying on them. New
analyses go next to it as `docs/hermes/<topic>-YYYY-MM-DD.md`.

## 5. Working practice

- Save main-context: delegate bulk reading (large logs, DB dumps, plugin source,
  long card bodies) to subagents — e.g. `model: sonnet` — with a precise question,
  read-only rules and the secrets rule; they return findings, not dumps.
- Check sizes (`ls -la`, `wc -c`, SQL `length()`) before reading anything
  yourself; read slices/aggregates, never whole large files.
- Have an independent subagent fact-check reports before publishing them.
- One PR per coherent change; PR #59 (CLAUDE.md, analysis, plan) is merged.

## 6. Open questions for the owner

- Resolved 2026-10-13: GitHub App installed for `arcanum-steamwoven`; all
  `conductor-hermes-*` folders granted; scope of allowed remedies (see §1).

## 7. Owner decisions for remediation planning (2026-10-13)

1. The `kanban-controller` plugin is project code and **may be changed**.
   Dropping or simplifying it is acceptable only together with another
   working solution for what it does.
2. **No fallback model/provider.** When quota is exhausted, everything pauses
   until it recovers; remedies should make that pause clean (no respawn churn,
   no latched state), not route around it.
3. Remediation plan first (in PR #59); later implementation goes as PRs to
   `ConductorPlatform/conductor-hermes` (profiles, skill, checks, plugins).
4. Human gates (native GUI observations, merges) **may be redesigned**: propose
   options with explicit pros/cons and rationale.

Follow-up decisions (2026-10-14), see `docs/hermes/remediation-plan-2026-10-14.md` §7:
quota pause is lifted manually by the owner; controller fix A now, rebuild B
later; read-only git tool + role-split preflight; the owner does not review
PRs — Hermes PRs merge after a successful independent review; proof budget per
Work-Item accepted (owner suspects decomposition/acceptance criteria — see
`docs/hermes/decomposition-analysis-2026-10-14.md`); all `~/.hermes` changes are
delivered as configuration plus an installer from `conductor-hermes`.
`conductor-hermes` is cloned in the cloud workspace (`/home/claude/conductor-hermes`).
P0 is merged there (PR #48) and installed on the host (2026-10-14 10:40,
`verify` OK, ESTOP engaged). P1 is merged (PRs #49-#51: installer Python
re-exec, `plugins/worker-tools` + skill 0.2.2, `cron/merge_reconcile.py`) and
installed (2026-10-14 11:25, `verify` OK). Host changes: `hosts/install_host.py
plan|apply|verify|restore` with `hosts/arcanum-steamwoven/host.yaml`. The old controller
is decommissioned reversibly (PR #47 closed as superseded). P2 is merged (PR #53:
no-agent `board-watch` cron + `/arc` owner command, no LLM; plan §11) and installed
(2026-10-14 15:50, `verify` OK). P3 is merged (PR #54: Definition of Ready + proof budget in
`kanban-naming-guard`, `/arc budget`, skill 0.2.3, developer max_turns 150; plan §12; project rules
in `docs/workflow/hermes.md` + `readiness.json`) and awaits the owner's install and the L2
readback experiment. Host changes are applied only by the owner via the installer.
