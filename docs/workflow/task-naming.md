# Arcanum Steamwoven task naming

This policy defines task codes and how threads, branches, pull requests and
commits carry them. The type registry below is canonical.

## Codes

A work item code is `ARC-TYPE-NNN`. `NNN` is a positive decimal number,
zero-padded to at least three digits. `TYPE` identifies a product area; it does
not identify a worker, role, assignee or model.

The type registry is `RES`, `DATA`, `RENDER`, `WORLD`, `INPUT`, `GPAD`, `UI`,
`SCRIPT`, `DEV`, `AI`, `COMBAT`, `SKILLS`, `MAGIC` and `TECHNOLOGY`. Add a
type only through an approved update to this policy.

Areas of the types added on 2026-10-16:

| Type | Area |
| --- | --- |
| `AI` | NPC behaviour: awareness, hostility, targets, morale, action choice, orders |
| `COMBAT` | Combat rules: mode, turns, action points, attack, damage, outcomes |
| `SKILLS` | Skills and their checks, training and use |
| `MAGIC` | Spells, colleges, casting, maintenance and effects |
| `TECHNOLOGY` | Technological disciplines, schematics, devices and tech effects |

The last five types were added by owner decision on 2026-10-16
([NPC combat AI plan](../research/npc-combat-ai-plan.md#owner-decisions-2026-10-16)).
`MAGIC` and `TECHNOLOGY` cover FR-MAGIC, `SKILLS` the skill part of FR-CHAR;
character state and the session runtime stay `WORLD`. Proposed convention, to
be confirmed: research on these areas stays `RES`.

### Codes from 2026-11-12: the issue number

Owner decision 2026-11-12 (ARC-DEV-409): the backlog lives in GitHub Issues of
`Simarglok/arcanum-steamwoven` ([backlog](tasks.md#backlog-in-github-issues)).
A new work item is an issue first, and its code is
`ARC-<TYPE>-<issue number>`: the type is the issue's `type:*` label, the number
is the number GitHub gave the issue. Issues and pull requests share one number
sequence, so the number is atomic and never reused, and no search for a free
code is needed. Example: issue #1093 with `type:ui` is `ARC-UI-1093`; its branch is
`arc-ui-1093-short-slug`.

- The code is built from the issue after it is created, so the issue title is
  the short English result without a code.
- A code from before this rule (`ARC-TYPE-NNN`, all numbers below 1000) keeps
  its meaning and stays in history; GitHub numbers are already past 1090, so a
  new code cannot collide with an old one. Do not create a new issue for finished
  or merged old work, and do not renumber it.
- Reuse the established code for work that belongs to it: a retry or follow-up
  of the same work item keeps its code (a follow-up that is its own result is a
  new issue). A code is never reused for different work.
- The owner or an owner-authorized session (the project's coordinator) creates
  the issue and hands out the code. A thread that receives a code confirms it
  with one check: `gh issue view N --json state,labels,title` shows the issue
  open with the expected `type:*` label, and remote branches and a PR search
  for the code show nothing else working on it ([task code](tasks.md#task-code)).
- A code written without an issue (an old-style `ARC-TYPE-NNN` allocated by the
  former rule, such as work already in progress on 2026-11-12) finishes under
  that code; its PR still says `Closes #N` when an issue exists.

Historical references keep their meaning: native `t_...` task IDs, stage codes
such as `ARC-INPUT-001-PLAN-001` and `arcanum-steamwoven/t_<id>-arc-*` branches
in older documents and PRs come from the former task board and are not
renamed.

## Titles and names

The code comes first; a PR title or commit subject may put a short area prefix before it, as in `research: ARC-RES-035 map catalogue and transition graph` or `perf(viewer): ARC-RENDER-009 ...`.

| Where | Form | Example |
| --- | --- | --- |
| Human title (task, project thread) | `ARC-TYPE-NNN — short Russian result` | `ARC-INPUT-001 — Проверить доступ к диагностической палитре` |
| Issue title (from 2026-11-12) | short English result, no code (the code is `ARC-TYPE-<issue number>`) | `Click-to-walk ignores a door in the path` |
| Pull request title | `ARC-TYPE-NNN: short English result`, optionally after an area prefix | `ARC-WORLD-015: pre-made starting hero in the game session` |
| Branch | `arc-type-nnn-short-slug`, unless the environment assigns a branch | `arc-world-015-starting-hero` |
| Commit subject | `ARC-TYPE-NNN: ...`, optionally after an area prefix | `ARC-DATA-015: universal map catalogue over the content stack` |

The text after the code in a human title is a concise, readable Russian
description of the outcome. It must contain at least one Russian Cyrillic letter
(`А–Я`, `а–я`, `Ё`, or `ё`); proper names and established terms may keep their
original spelling, so the whole suffix need not be Cyrillic.

A written task contract starts with `Work-Item: ARC-TYPE-NNN`, followed by the
readiness headers and `AC<n>` criteria described in
[tasks.md](tasks.md#definition-of-ready). For an issue, the issue body is the
contract and `Work-Item` is the code built from its number.
