# Backlog on GitHub Issues

Owner decision 2026-11-11 (ARC-DEV-409): the backlog lives in the GitHub issues of
this repository, which is free and works from cloud sessions (GitHub MCP) and from
the owner's Mac (`gh`). Lists of open work no longer live in documents or in a
coordinator's memory; documents keep contracts, evidence and registers
([open bugs](../research/open-bugs.md), [owner re-check list](../project/owner-recheck-list.md)).

## What an issue is

One issue is one work item, written as the [Definition of Ready](tasks.md#definition-of-ready)
contract: the form `.github/ISSUE_TEMPLATE/task.yml` asks for Result, Components,
Human-Gates, Requirements and the acceptance criteria `AC1`..`AC8`. A leftover or a
bug may start as a short issue (what, where, evidence) with the label `tail`; it is
made ready by completing the contract before a thread starts it.

## Labels

| Label | Meaning |
| --- | --- |
| `type:res`, `type:data`, `type:render`, `type:world`, `type:input`, `type:gpad`, `type:ui`, `type:script`, `type:dev`, `type:ai`, `type:combat`, `type:skills`, `type:magic`, `type:technology` | The type of the [task code](task-naming.md#codes); exactly one per issue |
| `status:ready` | Startable now: no open blocker; the starting thread completes the contract (Requirements, AC) before work |
| `status:blocked` | Waits for another item; the body names it under `Blocked by` (the form's heading or a line `Blocked by: #N[, #M]`) |
| `status:in-progress` | A thread or PR works on it (set by the thread that starts it, together with the assignee) |
| `status:needs-owner` | Waits for an owner decision; the body says the question and the options |
| `tail` | A leftover found while building something else |
| `bug` | A defect (the id of an [open bug](../research/open-bugs.md), when it has one, is in the body) |
| `owner-recheck` | The owner compares behaviour with the original game |
| `epic` | (no code) The parent of a plan epic (for example E1 of the [missing systems plan](../project/missing-systems-plan.md)); its children are sub-issues |
| `opus` | Needs Opus (new executable reading, save schema, threading, audit) |

At most one `status:*` label at a time. An issue with none is a note, not yet a
work item. A plan item id (`E1-f`, `MA-10`, `MT9b`, `T-s`, `B47`) stays in the body
as `Plan ref:` so the plans keep resolving.

## Life of an issue

1. **Create** (coordinator, owner or a thread that found a leftover): open the issue with the contract, the
   label `type:<type>` and a `status:*` label, then give it its code
   ([task naming](task-naming.md#codes)). Only the coordinator or the owner sets
   `status:ready`; a thread that found a leftover files it as `tail` without a
   status.
2. **Start**: the thread sets `status:in-progress` and assigns itself; the branch,
   PR title and commits carry the code; the PR body says `Closes #N`.
3. **Finish**: the merge closes the issue. A cancelled item is closed as not
   planned, with the reason in a comment.
4. **Found later**: a leftover is a new issue (label `tail`), linked from the PR
   that found it; it does not go into a document, a handoff or a memory.

## Finding work

- Cloud: the GitHub MCP tools `list_issues` (filter by label) and `issue_read`;
  search with `gh api -X GET search/issues -f q='repo:Simarglok/arcanum-steamwoven is:issue is:open label:status:ready'`.
- Mac: `gh issue list --label status:ready --label type:magic`, `gh issue view N`.
- A thread starts an issue only when the owner or the coordinator names it
  ([task authority](tasks.md#task-authority)); `status:ready` means startable, not
  assigned.
- Startable work for a type: `status:ready` plus `type:<type>`. The coordinator
  keeps the order by sub-issue order inside an epic and by milestones if used.

## Dependencies and parents

- Native "blocked by": on the Mac `gh issue edit N --add-blocked-by M` (GitHub CLI
  2.94 or newer); from a cloud session (older `gh`, no GitHub MCP tool for it) use
  the REST call `gh api -X POST repos/Simarglok/arcanum-steamwoven/issues/N/dependencies/blocked_by -F issue_id=<database id of M>`
  (the database id is the `id` field of `gh api repos/.../issues/M`, not its
  number). Also write the `Blocked by` line in the body, so that a reader and a text
  search see it without the API.
- When a blocker closes, the thread that merged it sets `status:ready` on the items
  that waited only for it (search `"Blocked by" "#N"` in issue bodies).
- Epic parents use sub-issues (MCP `sub_issue_write`, `gh issue create --parent`).

## Limits

The repository belongs to a person, not an organisation: GitHub issue types and
custom fields are not available, so type and status are labels. Projects boards
(GraphQL) are not reachable from cloud sessions; the labels above are the source of
truth and a board, if the owner wants one, is only a view.
