# Arcanum Steamwoven task naming

Policy ID: `arcanum-steamwoven-task-naming-v1` (schema 1).

This policy defines human-readable titles and machine-readable ownership for
new cards on the `arcanum-steamwoven` board. The canonical registry and fixed
fields are in [task-naming.json](task-naming.json); keep this explanation in
sync with that file.

## Codes and titles

A root card uses `ARC-TYPE-NNN — short Russian result`. A separately tracked
stage card uses `ARC-TYPE-NNN-STAGE-NNN — short Russian result`. The text after
the separator is a concise, readable Russian description of the outcome. It
must contain at least one Russian Cyrillic letter (`А–Я`, `а–я`, `Ё`, or `ё`);
proper names and established terms may retain their original spelling, so the
entire suffix need not be Cyrillic. `NNN` is a positive decimal number,
zero-padded to at least three digits for a root and exactly three digits for a
stage. For example:

```text
ARC-INPUT-001 — Проверить доступ к диагностической палитре
ARC-INPUT-001-PLAN-001 — Согласовать границы проверки палитры
```

The root number is allocated by the manager only after checking the live
board. Reuse the established root for work that belongs to it. A stage keeps
that root number and receives the next unused three-digit index for its stage
type within that work item. The full `Card-Code` must be unique; sequence
numbers do not imply ordering across different stage types. `TYPE` identifies
a product area; it does not identify a worker, role, assignee, or model.

The current type registry is `RES`, `DATA`, `RENDER`, `WORLD`, `INPUT`,
`GPAD`, `UI`, `SCRIPT`, and `DEV`. Add a type only through an approved update
to this policy and its canonical JSON registry. The stage registry is
`INTAKE`, `PREFLIGHT`, `PLAN`, `IMPL`, `REVIEW`, `FIX`, `PUBLISH`, `MERGE`,
`DIAG`, and `CLOSE`.

## Required body fields and ownership

Every new card body starts with these three machine-readable lines, in this
order. For a new root:

```text
Work-Item: ARC-TYPE-NNN
Card-Code: ARC-TYPE-NNN
Root-Task: SELF
```

For a stage card, `Work-Item` remains the root code, `Card-Code` is the full
stage code, and `Root-Task` is the stable native `t_...` ID of the root card:

```text
Work-Item: ARC-INPUT-001
Card-Code: ARC-INPUT-001-PLAN-001
Root-Task: t_...
```

For a new root, `Root-Task: SELF` marks that card as its own root. It is a
sentinel, not a replacement for the card's native ID. Preserve native `t_...`
IDs; the human-readable code supplements them and never renames or replaces
them.

`Root-Task` records root ownership. Kanban dependency parents record ordering
or gating and are separate data: do not infer ownership from dependency edges
or from the most frequent task ID mentioned in a body. If the owner is
ambiguous, perform bounded diagnosis and stop short of assigning a guessed
root.

## Retries and stage cards

Use `arcanum-steamwoven:<Card-Code>` as the idempotency key. A retry reuses the
original card and its original code/key; it does not allocate a new code.
Migration preserves existing legacy keys. A legacy key may be reused only
when the existing non-archived card and the supplied creation payload match;
it does not permit creating a second card or reviving archived work.
Status changes and native review actions also remain on the original card.
The `REVIEW` stage token is for an explicitly created, separately tracked
stage card, not for a native review action or a status switch.

## Adoption and enforcement boundary

When a migration is separately authorized, idle cards and cards currently
visible as `done` can be normalized. Defer cards that are running until they
reach a safe boundary, and preserve archived history. This document does not
itself authorize live-board changes.

Migration changes only the human-readable title and prepends ownership fields
to an idle card's unchanged contract. Preserve native IDs, dependency links,
statuses, assignees, workspaces, branches, PR/artifact/evidence bindings and
historical runs/comments. An archived original root may lack the new body
fields: it remains a valid owner only when its explicit native ID and original
root code are verified. A dependency or a stage card is not a substitute root.

The automated plugin guard covers agent `kanban_create` only. CLI, dashboard,
and auxiliary `decompose` creation paths require their own audit; this policy
does not establish a universal runtime guarantee. Do not claim that every
creation path enforces this contract until those paths have been checked.
