# Task workflow

Project: **Arcanum Steamwoven**, repository `Simarglok/arcanum-steamwoven`.

## Task authority

Work starts only from an explicit request by the owner: a direct instruction, a
Claude project thread, or a named `ARC-*` work item. The request, or the task
contract written from it, defines the code and title, context, goal, allowed
scope, sources and provenance, dependencies, acceptance criteria, checks,
documentation deliverables and publication authority. Record base, head,
workspace and reviewer findings at handoff. Repository documents govern implementation;
Obsidian holds research and discussions; the [missing systems plan](../project/missing-systems-plan.md) and
[vision](../project/vision.md) explain sequencing. None of them creates,
prioritizes or dispatches work; neither does an issue of the [backlog](#backlog-in-github-issues)
until it is launched in a thread on the owner's request (directly, or through
the coordinator at the owner's request). Do not queue the
whole product vision or start tasks as a side effect of project setup.

Continue from the [merged component catalogue](../project/status.md) and current
`origin/main`, not from a document's dated snapshot. Every work item has a code
under the [task naming policy](task-naming.md).

### One task per thread

Owner decision 2026-10-21: one Claude thread, and its session, works on exactly
one `ARC-*` code. When that code's PR merges, is handed to the owner for review,
or the owner cancels the code, the session posts its report, writes a handoff of
at most 40 lines for the next item and ends. The handoff holds the handoff
record above (base, head, workspace, reviewer findings) plus the next item
(its issue `#N` for work from the [backlog](#backlog-in-github-issues), else its
plan row verbatim), decisions taken, branch and open questions; it goes to the
project's coordinator thread, which keeps the queue of follow-up work. The next code starts in a new
thread with a fresh session from that handoff; a thread does not take a second
code, even a small fix found on the way, which goes back to the coordinator
thread. Inside one task, do not clear or restart the context while it is still
needed for correctness (part of the same decision).

Sessions compact their context at 500k tokens: `autoCompactWindow` is 500000
(ARC-DEV-058, owner decision 2026-10-29). `.claude/settings.json` holds it as a
local, untracked and ignored file of each checkout (ARC-DEV-1342, owner
decision 2026-11-12); a new checkout or worktree creates it by hand. Keep the
handoff record current in the thread's checklist, together with the branch,
changed files and checks run, so a compaction keeps it.

Why: a session carried across codes re-reads every earlier task's history on
each step. The token audit of 2026-10-21 found lane sessions living 3.5 to 5
days across 13 to 25 codes at a median context of about 550 thousand tokens.

### Backlog in GitHub Issues

Owner decision 2026-11-12 (ARC-DEV-409): everything planned and not yet started
is an issue of `Simarglok/arcanum-steamwoven`; the issue number is the task
code ([naming](task-naming.md#codes-from-2026-11-12-the-issue-number)).

- **What is where.** The open issues are the queue of unstarted work. Documents
  (`open-bugs.md`, `owner-recheck-list.md`, the audits, `missing-systems-plan`,
  `function-audit-plan`, the coverage rows) keep the research, evidence and
  status; when one lists pending work that has an issue, it links `#N` and does
  not repeat the plan. New unstarted work is an issue, not a new list row.
  An owner check is a row of `owner-recheck-list.md` with the id `ARC-TYPE-NNN#k`
  ([format](owner-checks.md)); a PR that changes owner-visible behaviour adds its
  rows in the same PR.
- **Mandatory: file the issue when a need is found** (owner rule 2026-11-12).
  Whenever a need for work comes up (a leftover part of the task, a gap or bug
  found on the way, a follow-up, a review finding that is not fixed in the PR),
  the thread or coordinator that found it creates a GitHub issue for it right
  away, with the usual labels and without asking the owner (the owner's standing
  permission to create and label issues), and reports the new issue numbers.
  A need only named in a report, a PR text or a document is not recorded.
- **One issue is one work item**: the form `.github/ISSUE_TEMPLATE/task.yml`
  has the headings `Goal` (one result, one PR), `Requirements` (FR ids or
  groups, [functional requirements](#functional-requirements)), `Components` (at
  most three), `Acceptance criteria` (draft `AC1`..`AC8`, a `Human gates` line),
  `Blocked by` and `Sources and docs`. An issue made with `gh issue create
  --body-file` uses the same headings. The criteria are fixed when work starts
  ([Definition of Ready](#definition-of-ready)).
- **Labels** (a personal repository has no issue types or custom fields):
  `type:*` (one, the code's type: `res`, `data`, `render`, `world`, `input`,
  `gpad`, `ui`, `script`, `dev`, `ai`, `combat`, `skills`, `magic`,
  `technology`), `area:*` (the FR group of the coverage matrix, one or more),
  `status:*` (one of `ready`: open, no open blocker, nobody working on it;
  `blocked`: waits for its blockers; `in-progress`: a thread, branch or PR works
  on it; `in-review`: the PR waits for the owner; the label is a cache, the
  coordinator rechecks blockers and `gh issue view` at launch, and a closed
  issue's `status:*` is ignored), `epic` (a parent issue of slices, never a task
  and never `status:ready`), `bug` (the open-bugs rule keeps the original-bug
  classification in the body), `owner-decision` (a decision of the owner comes
  first; ask through the coordinator's decision card) and `recheck` (an owner
  recheck or an item with no trace in the repository; the body asks for a
  repeatable case).
- **Dependencies** are GitHub relations, not only text:
  `gh issue edit N --add-blocked-by M` (needs a gh with `--add-blocked-by`, 2.103 is checked, on the owner's Mac). A
  cloud session (older `gh`, the GitHub MCP has no tool for it) sets the same
  relation with the REST call `gh api -X POST repos/Simarglok/arcanum-steamwoven/issues/N/dependencies/blocked_by -F issue_id=<database id of M>`
  (the `id` field of `gh api repos/.../issues/M`, not its number; checked from a
  cloud session on 2026-11-11), and also writes `Blocked by` in the body. A blocked issue has `status:blocked`; the thread that
  merges a blocker lists the issues it unblocks in its report, and the
  coordinator moves them to `status:ready`. An epic is a parent issue
  (`--parent`, label `epic`) of its slices; the parent carries no code of its own
  and no PR.
- **Life of an issue.** The coordinator creates it (labels `type:*`, `area:*`,
  `status:*`; the form cannot set labels), hands out the code and launches a
  thread on the owner's request; a direct owner request in a thread gets its
  issue from the coordinator before work starts, and a session without `gh`
  asks the coordinator for it; the thread sets
  `status:in-progress`, opens one PR whose body says `Closes #N`, and merging
  closes the issue. A cancelled, stale or duplicate issue is closed as not
  planned with a comment saying why. Work already running when the backlog was
  created (a thread, branch or PR) was not imported and keeps its old code.
- **Finding work**: `gh issue list --label status:ready --label type:ui` (epics are not listed:
  they carry no `status:ready`);
  `gh issue view N`. Project boards are not used (they cannot be written from
  a cloud session and labels are enough).

### Small search and read output

Owner decision 2026-10-27 (ARC-DEV-035): every tool output stays in the
session's context and is re-read on each later step, so search and reading
output is kept small.

- Search for files first, then lines: list matching files or counts
  (`rg -l` or `rg -c`; `grep -l` or `grep -c` where `rg` is missing), then show
  only the lines needed: `rg -n -m 20 PATTERN FILES | cut -c1-200`.
- Read files in ranges of at most 120 lines (`sed -n 'A,Bp'`, or the read
  tool's offset and limit); never `cat` a whole source file or document.
- Never read a saved large tool output whole; grep it for what you need.
- The component catalogue, the coverage matrix and the verification ledger
  are split by area ([area files](#area-files-of-the-catalogue-matrix-and-ledger)):
  find the row in the area folder, grep only that row and cut it
  (`| cut -c1-300`); their rows run to thousands of characters.
- Subagents (executable readers, reviewers) get the diff or exact file ranges
  and a short brief (a reader gets the address and the export path, not the
  whole export); they never read a catalogue, matrix or ledger file whole.

Why: in the token audit of 2026-10-21, search and read output (`grep`, `find`,
`sed`, `cat`) was the largest share of re-read tokens in the three audited
lanes (about 75 to 150 million cache-read tokens each), and saved tool outputs of
32 to 48 KB were read whole and re-read about 390 times. Godot, launcher and
`dotnet` output follow [quiet run output](#quiet-run-output).

### Area files of the catalogue, matrix and ledger

Owner decision 2026-10-27 (ARC-DEV-038, token audit item 5): the three shared
records are split by area, so a lane reads and edits only its own area and
lanes in different areas do not conflict.

| Record | Index (small, shared) | Area files (full rows) |
| --- | --- | --- |
| [Component catalogue](../project/status.md) | audit note, one line per component (name, area, first task codes), credited verification, maintenance rule | `docs/project/status/<area>.md`: the full "Implemented components" and "Merged research" rows of the area |
| [Coverage matrix](../requirements/coverage.md) | status scale and the summary counts by section | `docs/requirements/coverage/<domain>.md`, one per domain document of the register (`world.md`, `scene.md`, …): the requirement rows |
| [Verification ledger](../research/original-verification-ledger.md) | inputs and method, verdicts, one line per section with its row IDs, decisions to revisit, second reading, not checked | `docs/research/ledger/<area>.md`: the sections of the area with their rows |

- Find a row by name, task code, requirement code or row ID in the area folder,
  for example `grep -n "FR-WORLD-013" docs/requirements/coverage/*.md | cut -c1-300`,
  `grep -n "^| WT3 " docs/research/ledger/*.md | cut -c1-300` or
  `grep -n "ARC-MAGIC-004" docs/project/status/*.md | cut -c1-300`.
- Edit a row in its area file. A new component adds its full row to the area
  file and one line to the index of the catalogue; a new ledger section goes at
  the end of its area file and adds one line (section, area, row IDs) to the
  ledger's area index; a coverage status change also updates the section's counts
  in the matrix summary.
- A new area file is made only when no area fits; it gets the same header as its
  neighbours and a link from its index.
- Links to a row's section point at the area file
  (`../research/ledger/magic.md#spell-book-arc-magic-004`); old links to the
  index's section anchors still resolve through the index.

### Fewer steps

Owner decision 2026-10-27 (ARC-DEV-036): every step re-reads the whole session
context, so a task takes as few steps as its work allows.

- Put independent tool calls in one response: reads, searches and checks that
  do not depend on each other's results go out together, not one per step.
- Update the thread's progress checklist only when a step changes state, and
  in the same response as other work, never as a step of its own.
- Coordinator: send a running lane only what needs action from it; batch
  messages that are for information only.

Why: in the token audit of 2026-10-21, lanes made about one tool call per step,
status (progress-checklist) and no-reply calls were 12 to 21 percent of all
steps (about 220 such calls per audited lane), and each lane received 16 to 23 coordinator messages,
many for information only.

### Lean orientation

Owner decision 2026-10-29 (ARC-DEV-057, token audit of 2026-10-29, item 3):
orientation does not fill the main context with search output.

- Start from the entry files, types and docs the brief names (taken from the
  previous handoff).
- A search wider than a few files goes to one subagent on a cheaper model
  (Sonnet or Haiku, Explore type) with a narrow question; it returns at most 40
  lines: paths, line ranges and the answer, never file dumps. Read the ranges
  yourself only where you edit or cite them.

Why: in the token audit of 2026-10-29, re-read search and reading output was 22
percent of the week's cache-read tokens (about 62 searches and 20 reads per
session), and the [small search and read output](#small-search-and-read-output)
rule made no visible difference: per-call output was already small, the count is
the cost.

### Review model

Owner decision 2026-10-29 (ARC-DEV-057, token audit of 2026-10-29, item 4):

- The independent review runs on Sonnet by default. Use Opus for a diff over
  about 800 changed lines or one that changes original-game mechanics, the save
  schema or threading. Readings of the original executable stay on Opus.

Why: in the token audit of 2026-10-29, reviews were 8 percent of the week's
cache-read tokens (146 review subagents), all of them on Opus.

### Token rules

Owner decision 2026-11-14 (ARC-DEV-1982): cost is the context size times the
number of steps, so a thread keeps both small. This section gathers the rules
not stated elsewhere; the others are
[small search and read output](#small-search-and-read-output),
[fewer steps](#fewer-steps), [lean orientation](#lean-orientation) and
[review model](#review-model).

- **Model.** Sonnet by default. Opus only for reading the original executable,
  executable audits and big reviews (thresholds in the
  [review model](#review-model)).
- **One thread, one issue.** The thread ends as
  [one task per thread](#one-task-per-thread) says (merge, owner review or
  cancel), at once and without further work. Anything found on the way is a new
  issue, filed at once ([backlog](#backlog-in-github-issues)); its work goes
  through the coordinator to a new thread, never "while I'm here".
- **Audit slices** of the original executable hold at most 15 to 20 functions
  each.
- **Recon and executable reading in a subagent.** A wide code search follows
  [lean orientation](#lean-orientation) (cheaper model, at most 40 lines back);
  reading the exports or the executable runs in an Opus subagent. Either way
  only the conclusion enters the thread.
- **Waiting.** Wait for the Mac launch queue with one blocking command (a quiet
  `./arcanum run`, or for a run already started elsewhere
  `./arcanum queue --wait RUN_ID|--all [--timeout MIN]`,
  [waiting for a run](toolchain.md#launch-queue-arc-dev-033)), never a `sleep` or polling loop. Batch every check of the
  task into one queue run instead of one run per check.
- **Reports.** No interim status posts ("reading X"): post only a result, a
  question or a failure. The checklist carries progress, updated only when a
  step changes ([fewer steps](#fewer-steps)).
- **No long-lived multi-task threads** that accumulate context; a new task
  starts a new thread from a short handoff.

Why: the token audits of 2026-10-21 and 2026-10-29 (see the sections linked
above) found the cost in long sessions, re-read search and reading output, and
reviews all on Opus.

### Smoke scope

Owner decisions 2026-10-29 (ARC-DEV-057, token audit of 2026-10-29, item 5;
narrowed the same day in ARC-DEV-061):

- Per PR, run only the smoke stages the change touches or is directly related
  to; the Crash Site smoke only when the change is related to it (the pinned
  exterior, the cave, their inputs). A PR whose change no smoke stage covers
  runs none and says so.
- While the rolling smoke on `origin/main` is red (ARC-DEV-2226), `./arcanum smoke pr` prints
  `SMOKE red on main since SHA, #N` with the failing stages: a PR run that fails only those stages cites that issue
  instead of re-running the smoke on a clean main ([rolling smoke](toolchain.md#rolling-smoke-on-main-arc-dev-2226)).
- Mandatory (owner decision 2026-11-07, ARC-DEV-239): a PR that changes click or
  walk handling (the viewer's click-to-walk, held walk, walk requests and their
  plans, the hero walk goal, pathing the click reaches) always runs
  `./arcanum smoke pr --stages map` and `./arcanum smoke self-test`, whatever
  else it touches, and its report names both results. This overrides the
  "no smoke stage covers it" exception above. Since ARC-DEV-2223 the files of
  the `walk` and `container-click` zone rows count as such a change:
  `smoke pr --auto` adds the `map` stage itself and `smoke verify-pr` requires
  the `map` stage and the self-test.
- The complete smoke run (the Crash Site smoke, the map batteries, then every
  stage: `./arcanum smoke complete`) runs as part of the daily package build
  (`./arcanum daily`, [daily package build](packaging.md#daily-package-build-arc-dev-062)),
  and only when main changed since the last package; not per PR. For a wide
  change `./arcanum smoke pr --auto` selects `complete` itself (the shared data,
  save schema, render and shared UI code, and a file no zone table row names);
  at a PR that means **core + the Crash Site smoke** (the PR floor below), the
  rest of the complete run (the heavy zones and the map batteries) being the
  daily build's.
- **PR floor** (owner decision 2026-11-15, ARC-DEV-2223, smoke stability
  measure 1, [baseline](smoke-stability.md)): the stage set that
  `./arcanum smoke pr --auto` selects for the diff against `origin/main` is a
  lower bound, not a suggestion. A hand-picked `--stages` list may add to it,
  never replace it. Before the merge run `./arcanum smoke verify-pr` (after
  `git fetch origin main`); it reads the queue journal
  (`smoke-stages.jsonl`) and fails when a required stage, the Crash Site smoke
  or the self-test has no passing run of this checkout on the files as they
  are now (a run counts until a file of the diff changes; merging main or
  editing a document does not void it). The PR report names its `SMOKE
  verify-pr PASS` line. The check also holds the click and walk rule below
  (the `map` stage and `smoke self-test` for a change in the `walk` or
  `container-click` rows), and a path no zone row names selects `complete`
  (fail closed): the output names it, and the fix is a row in `TRIGGERS`
  (`tools/local_dev/smoke_zones.py`), not a narrower run. A diff no zone covers
  (documents, tests, research tools) requires nothing and the check passes. A
  docs-only PR needs no smoke and no check.
- Zones (owner decision 2026-11-11, ARC-DEV-396): `./arcanum smoke pr --auto`
  reads the diff against `origin/main` and runs the zones it selects (`core`,
  the heavy `walk`, `container-click`, `crash-site`, `census`, or `complete`,
  which at a PR is core + `crash-site`, see the PR floor above);
  it prints each zone and why, and the PR report names them (`SMOKE zones: ...`).
  A diff no zone covers (documents, tests, research tools) runs no smoke and the
  run says so. `--zones` names zones by hand, `--stages` still adds stages
  ([smoke zones](toolchain.md#smoke-runner-arc-dev-061)).
- `container-click` and `walk` check a sample unless `--all` (ARC-DEV-395, owner
  decision 2026-11-11); run them with `--all` when the change touches what the
  sample no longer proves ([sampled stages](toolchain.md#smoke-runner-arc-dev-061)).
  `slides` (ARC-DEV-3116, owner decision 2026-11-24) ends each installed movie by the
  key skip once its checks are done in a PR run (about 90 s instead of about 230 s); the
  complete run and the daily build play every movie to its own end (`--all` does too).
- A stage that also fails on clean main is not this PR's failure: name it in
  the PR and go on; do not investigate it in the task thread.
  The runner prints `known on main, not this PR` for the failures listed in
  `tools/local_dev/smoke_known_failures.json`
  ([known failures](toolchain.md#smoke-runner-arc-dev-061)); a failure that is
  not listed but also fails on clean main is named in the PR the same way.
- Run them with the shared runner ([smoke runner](toolchain.md#smoke-runner-arc-dev-061)):
  `./arcanum smoke pr --auto` (or `--stages S,... [--crash-site]`, on top of
  the floor) per PR, `./arcanum smoke verify-pr` before the merge and
  `./arcanum smoke complete` for the nightly run; no scratch smoke scripts.
- `SMOKE PASS` means every requested stage ran and printed its `PASS` line
  (ARC-DEV-063): a skipped stage fails the smoke, a stage that needs a hero
  gets the chain's hero of the map (else `human-male`) unless `--hero` names
  another. A whole map battery (`full`) vouches only for its `map` and
  `resources` stages; its other stages may report NONE where the map lacks
  what they need ([every requested stage runs](toolchain.md#smoke-runner-arc-dev-061)).

Why: in the token audit of 2026-10-29, a merged PR took 162 steps (94 before),
much of it the full check and smoke ritual every thread repeats; smoke runs
took long even on small fixes.

### Task code

From 2026-11-12 the code is `ARC-<TYPE>-<issue number>`, so the issue itself
is the allocation; the thread confirms it with `gh issue view N --json
state,labels,title` and the branch and PR search below
([backlog](#backlog-in-github-issues)). `gh issue view N` is meaningful only for
N of 1000 and more; an old code's number may match an unrelated old issue or
PR. The rest of this section is the rule for codes allocated before then.

Owner decision 2026-10-29 (ARC-DEV-057, token audit of 2026-10-29, item 7):

- For a code allocated before 2026-11-12, the coordinator checks the code is unused before handing it out; the thread
  confirms with one check (remote branches and a PR search for the code), not a
  full history search ([task naming](task-naming.md)).
- No checklist-only steps: a checklist update rides with other work
  ([fewer steps](#fewer-steps)).

Why: in the token audit of 2026-10-29, checklist-only steps were still 8.6 per
session (about 5 percent of the cache-read tokens), and every new thread paid
its own orientation and code check.

### Quiet run output

Owner decision 2026-10-27 (ARC-DEV-037): Godot, smoke, export and `dotnet`
output is read from a log file, not from the session.

- Run them through `./arcanum run`, which is quiet by default: the whole output
  goes to a log file, and stdout shows only PASS or FAIL with the first error
  and at most 40 lines around it; the `ARCANUM_QUEUE` lines stay on stderr
  ([quiet output](toolchain.md#quiet-output-arc-dev-037)).
- Check a run's start in its log with a narrow search or `tail -n 20`; never
  read a log whole. Use `--verbose` only when the whole output is the point.
- Give `dotnet build` and `dotnet test` `-v q --nologo`, and tests
  `--logger "console;verbosity=minimal"`.

Why: in the token audit of 2026-10-21, Godot output was the next largest share
of re-read tokens after search and reading (63 million cache-read tokens in the
map-mode lane alone).

## Definition of Ready

Owner decision 2026-10-14: work stalls when one work item carries several
results, human or device observations, or unbounded proofs. A work item is
ready only when it follows these rules.

1. **One result, one PR, at most three components.** A written task contract
   starts like this:

   ```text
   Work-Item: ARC-TYPE-NNN
   Result: <one mergeable result, one PR>
   Components: <at most 3, comma-separated>
   Human-Gates: none | ARC-TYPE-NNN[, ...]
   Requirements: FR-XXX-NNN[, ...] | none (<reason>)

   AC1. <criterion an agent can check itself>
   AC2. ...
   ```

   An issue of the [backlog](#backlog-in-github-issues) is this contract: `Goal`
   is the `Result`, `Components` and `Requirements` are the same lines, the
   `Acceptance criteria` field holds `AC1`.. and `Human gates`, and `Work-Item`
   is the code built from the issue number.

2. **Only criteria an agent can check.** Acceptance criteria are labelled `AC1`
   ... `AC8` (at most eight). A human, physical-device or native-GUI observation
   is not an acceptance criterion of an agent work item: it goes into the
   separate owner-validation work item named in `Human-Gates`, which never blocks
   merge. Saying that there is *no* such gate is fine. Independent headless, Data and
   research work may proceed while graphical validation is unavailable where its
   accepted contract permits; GUI, export and device gates stay required for scopes
   that need them. A locked Mac is not proof of failure or completion and does not
   justify substituting unrelated green checks.
3. **Every proof is finite.** "Exhaustive", "certificate", "worst-case",
   "parity", "all possible" and similar are allowed only with an explicit bound on
   the same line: a number, a list of cases, a limit or a reference.
4. **The contract is fixed when work starts.** Only an explicit owner amendment
   changes acceptance; comments that propose new criteria do not.
5. **Bounded effort.** When the work outgrows its contract, or the same failure
   repeats twice, stop and ask the owner with options: accept the practical
   contract, narrow it, stop, or continue with a larger allowance. Do not loop
   on new diagnostics.

## Functional requirements

Owner decision 2026-10-20: the [functional requirements register](../requirements/README.md)
is checked when a task is created and kept current when a change lands.

- **When creating a task or an issue**, find the requirements it serves in the
  [coverage matrix](../requirements/coverage.md) and name them in the contract's
  `Requirements:` line (`none` with a reason for tooling, research method or
  workflow work that serves no requirement). Read each named requirement in its
  domain document (behaviour, original, acceptance) and its current row: the
  status, what is implemented and what is missing. A task that duplicates delivered
  work or contradicts a requirement is raised with the owner before it starts. Product
  work that no requirement covers is raised with the owner too: either it belongs under
  an existing requirement, or the register needs a new one.
- **When the change lands**, the same PR updates the matrix row of every
  requirement whose coverage it changes (in the section's file under
  `docs/requirements/coverage/`, with the section's counts in the matrix summary): status, what is implemented, what is
  missing and the documentation links, using the matrix's status scale. A new
  requirement, or a changed one, is edited in its domain document with the owner's
  agreement, and the counts
  in the register's README and the matrix summary follow. The PR body lists the
  requirements it touched, or says that it touched none.
- The matrix is the status of record. The per-requirement "Steamwoven" text in the
  domain documents is background as of its last edit; when it disagrees with the
  matrix, the matrix wins.

## Evidence levels

| Level | Who and when | Evidence |
| --- | --- | --- |
| L1 automatic | agent, every correction | synthetic tests, headless checks, frame readback from inside the application, programmatic input |
| L2 native automatic | agent, before review when the work item requires it | the same build in a real window on the host with a scripted observer (frames, input log, clean exit); a logged-in GUI session is a precondition checked before the run. Verified 2026-10-14 with a GDScript probe: in-application frame readback works from a launchd job in the user's GUI domain without Screen Recording permission. Launch through `godot_canonical` ([toolchain](toolchain.md), sets `DOTNET_ROOT`): without it the mono build cannot load .NET and shows a modal error dialog (a C# project launched this way from launchd is not verified yet) |
| L3 human | owner, once per work item or milestone, in batches | short owner checklist in a separate owner-validation work item; never gates agent work or merge |

If an older contract still demands L3 inside agent work, the agent records that
gate as NOT_VERIFIED and asks the owner once instead of looping.

## Evidence storage

Evidence that must survive goes into the candidate branch (when it is allowed in
git) or the PR description, never only into ignored `local/`,
`research-private/` or a workspace that can be removed. For original-data
results that cannot be committed, record paths relative to the declared input
roots and digests.

## Local references

The repository checkout may contain ignored `toolkit/`. Isolated worktrees do
not inherit ignored archives. A research task must name the permitted local
toolkit/data inputs explicitly when needed; keep machine-specific paths out of
committed files. Do not copy the game or the full vault into each worktree.

## Review, publication and merge

- Every change goes through a PR from a branch, never a push to `main`, as GitHub
  user `Simarglok` ([AGENTS.md](../../AGENTS.md#github-identity)).
- Before opening the PR, get an independent review of the exact diff from a
  reviewer that did not write it, and resolve its findings. A review pass is not
  a merge.
- Owner decision 2026-10-16: the agent working the task merges its own PR,
  unless the owner explicitly asked to review it (in the task or later); then
  the agent leaves it open, reports the link and the owner merges. The agent
  merges only after the task's acceptance criteria, its checks and the root
  checks pass (only `docs_check.py` for a docs-only change, below) and the review is
  addressed, then reports which PR it merged. A PR body, a review PASS or a
  finished status alone never authorizes a merge.
- Owner decision 2026-10-16: a **docs-only** change runs no checks but the docs link check. Docs-only
  means the diff changes only Markdown documentation (`*.md`), not code,
  scripts, tests, project, build or configuration files. Markdown that a test
  reads is not docs-only (for example the size/SHA-256 tables in
  `docs/research/area-inventory.md` and `docs/research/dat-evidence.md`
  that `tools/local_dev/test_launcher_sync.py` checks). The root checks and the
  task checks are not run, except `python3 -B tools/docs_check.py` (Python
  only, the link and path check of ARC-DEV-406), which every docs-only PR runs;
  the PR says "docs-only, no checks run except docs_check". The
  independent review still applies: it confirms that the diff is docs-only (no
  changed file name appears in `tests/` or `tools/`). Such a PR can run in a cloud checkout
  without .NET or Godot.
- Before the merge of a code change, after updating the branch from a fresh
  `origin/main`, run `./arcanum smoke verify-pr`: it must print `SMOKE verify-pr
  PASS` (the smoke the diff requires, [PR floor](#smoke-scope)); on a FAIL run the
  named smoke, never a narrower one, and run the check again. A docs-only PR is
  exempt.
- An agent merge uses a merge commit for the exact reviewed head, for example
  `gh pr merge <url> --merge --match-head-commit <reviewed-sha>`, after checking
  that `gh api user --jq .login` returns `Simarglok`. Never use `--admin`,
  `--auto`, `--delete-branch`, a force option, a merge queue or another strategy
  as a workaround, and never force-push. If the merge is refused or its result is
  unclear, read the PR state first instead of retrying. After a merge, read back
  the MERGED state and merge commit, and report the merged PR.
- Owner rule 2026-11-11: a change that alters what the player sees (a drawn
  element, an HD art, a layout, a colour) shows the result before it is merged:
  one **composite image** in the thread with every version labelled (before,
  after and each alternative that was tried), so the owner compares them at a
  glance. When the owner must choose between versions, the thread forwards the
  image to the coordinator for a decision card. The images contain original
  art, so they are posted in the thread and kept local, never committed to git
  ([HD pack rule](hd-pack.md)); the PR text says which thread holds them.
- **Image archive** (owner rule 2026-11-25, ARC-DEV-3277): every image a task
  makes or uses as evidence (raw Codex outputs, collages, masters, composites,
  screenshots, sheets) is saved to `local/steamwoven-ui/archive/<TASK>/` **of the
  main checkout** before it is posted, never only in a worktree, a session
  scratchpad or `/tmp` (they are lost when the worktree is removed or the OS
  cleans up). `python3 -B tools/steamwoven_ui/image_archive.py dir <TASK>` creates
  and prints the folder (use it as `--out`); `... add <TASK> FILE... --kind
  composite|collage|screenshot|master|raw-codex` copies and registers files; the
  version the owner accepted goes into `<TASK>/final/` (`... mark <TASK>
  composite/NAME.png final`); `... scan` registers files copied there by hand.
  Every file has a row in `archive/registry.jsonl` (task, file, kind, status
  final/rejected/intermediate/unknown, source, hash, date). Tools that take
  `--out`/`--work` warn when the folder is inside a worktree or a temp folder.
  **At merge** the task also closes its statuses ([TRD SWI-PRV-6](../ui/steamwoven-interface-trd.md#provenance-and-storage)): accepted images `final`, rejected variants
  `rejected`, everything else it made `intermediate` (`mark <TASK> FILE STATUS [--note TEXT]`); no `unknown` row of the task may remain, or the thread files an issue
  naming the open rows.
  The images stay local and uncommitted ([TRD SWI-PRV-5](../ui/steamwoven-interface-trd.md#provenance-and-storage)).
- Report Implemented, Not implemented, Remaining work, the checks run with their
  results, and material uncertainties.
