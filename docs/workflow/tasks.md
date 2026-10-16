# Task workflow

Project: **Arcanum Steamwoven**, repository `Simarglok/arcanum-steamwoven`.

## Task authority

Work starts only from an explicit request by the owner: a direct instruction, a
Claude project thread, or a named `ARC-*` work item. The request defines the
outcome, scope and relevant checks. Repository documents govern implementation;
Obsidian holds research and discussions; the [roadmap](../project/roadmap.md) and
[vision](../project/vision.md) explain sequencing. None of them creates,
prioritizes or dispatches work. Do not queue the whole product vision or start
tasks as a side effect of project setup.

Continue from the [merged component catalogue](../project/status.md) and current
`origin/main`, not from a document's dated snapshot. Every work item has a code
under the [task naming policy](task-naming.md).

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

   AC1. <criterion an agent can check itself>
   AC2. ...
   ```

2. **Only criteria an agent can check.** Acceptance criteria are labelled `AC1`
   ... `AC8` (at most eight). A human, physical-device or native-GUI observation
   is not an acceptance criterion of an agent work item: it goes into the
   separate owner-validation work item named in `Human-Gates`, which never blocks
   merge. Saying that there is *no* such gate is fine.
3. **Every proof is finite.** "Exhaustive", "certificate", "worst-case",
   "parity", "all possible" and similar are allowed only with an explicit bound on
   the same line: a number, a list of cases, a limit or a reference.
4. **The contract is fixed when work starts.** Only an explicit owner amendment
   changes acceptance; comments that propose new criteria do not.
5. **Bounded effort.** When the work outgrows its contract, or the same failure
   repeats twice, stop and ask the owner with options: accept the practical
   contract, narrow it, stop, or continue with a larger allowance. Do not loop
   on new diagnostics.

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
- Merge authority comes from the owner: by default the owner merges. An agent
  merges only when the owner authorized it for the task or as a standing project
  instruction, and only after the task's acceptance criteria, its checks and the
  root checks pass and the review is addressed.
- An agent merge uses a merge commit for the exact reviewed head, for example
  `gh pr merge <url> --merge --match-head-commit <reviewed-sha>`, after checking
  that `gh api user --jq .login` returns `Simarglok`. Never use `--admin`,
  `--auto`, a force option or another strategy as a workaround, and never
  force-push. If the merge is refused or its result is unclear, read the PR state
  first instead of retrying, and report what was observed.
- Report Implemented, Not implemented, Remaining work, the checks run with their
  results, and material uncertainties.
