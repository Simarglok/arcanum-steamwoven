# Arcanum Steamwoven task naming

This policy defines task codes and how threads, branches, pull requests and
commits carry them. The type registry below is canonical.

## Codes

A work item code is `ARC-TYPE-NNN`. `NNN` (below also `N`) is a positive decimal
number: for codes issued before 2026-11-11 zero-padded to three digits, from then
on the number of the work item's GitHub issue, unpadded. `TYPE` identifies a product area; it does
not identify a worker, role, assignee or model.

**Where the number comes from** (owner decision 2026-11-11, ARC-DEV-409). The
backlog is the repository's [GitHub issues](backlog.md); a new work item's number
is the number of its issue. Nobody chooses a number by hand: the issue is created first (label
`type:<type>`), then its title is renamed to `ARC-TYPE-N: short English result`.
GitHub numbers issues and pull requests from one counter that is already above
1000, and every code issued before 2026-11-11 has at most three digits, so a new
code never equals an old one and is never reused. (Example only: issue #2000 with
the label `type:magic` is `ARC-MAGIC-2000`.) A PR for the item has its own number; its body says `Closes #N`.

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

Codes issued before 2026-11-11 (numbers up to three digits, including the ones
reserved then for work in progress) stay valid and keep their form and their
branches; they have no issue and their PRs have no `Closes`. Reuse the
established code for work that belongs to it: a retry or an unfinished follow-up
of the same item keeps its code (for a new-form code, its issue is reopened); a
follow-up that is new work gets a new issue and a new code. The code is never
reused for different work. A thread that receives a
code confirms it with one check: the issue exists, is open and carries the label
`type:<type>` of the code ([task code](tasks.md#task-code)).

Historical references keep their meaning: native `t_...` task IDs, stage codes
such as `ARC-INPUT-001-PLAN-001` and `arcanum-steamwoven/t_<id>-arc-*` branches
in older documents and PRs come from the former task board and are not
renamed.

## Titles and names

The code comes first; a PR title or commit subject may put a short area prefix before it, as in `research: ARC-RES-035 map catalogue and transition graph` or `perf(viewer): ARC-RENDER-009 ...`.

| Where | Form | Example |
| --- | --- | --- |
| Human title (task, project thread) | `ARC-TYPE-NNN — short Russian result` | `ARC-INPUT-001 — Проверить доступ к диагностической палитре` |
| Pull request title | `ARC-TYPE-NNN: short English result`, optionally after an area prefix | `ARC-WORLD-015: pre-made starting hero in the game session` |
| Branch | `arc-type-nnn-short-slug`, unless the environment assigns a branch | `arc-world-015-starting-hero` |
| Commit subject | `ARC-TYPE-NNN: ...`, optionally after an area prefix | `ARC-DATA-015: universal map catalogue over the content stack` |

The text after the code in a human title is a concise, readable Russian
description of the outcome. It must contain at least one Russian Cyrillic letter
(`А–Я`, `а–я`, `Ё`, or `ё`); proper names and established terms may keep their
original spelling, so the whole suffix need not be Cyrillic.

A written task contract starts with `Work-Item: ARC-TYPE-NNN`, followed by the
readiness headers and `AC<n>` criteria described in
[tasks.md](tasks.md#definition-of-ready).
