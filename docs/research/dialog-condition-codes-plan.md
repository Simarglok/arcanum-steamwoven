# Dialog condition codes outside the subset: plan (ARC-RES-091)

Status: **plan, nothing implemented.** Docs-only record written 2026-11-07 at the owner's request, after ARC-DEV-171 (`ru`, `rq`, bug B17)
showed that many installed dialogs still stop on a condition code the conversation runtime does not parse. The owner's decision: **the
remaining codes are to be implemented**, in slices, each checked against the retail 1.0.7.4 `Arcanum.exe`.

## What is known

- The original's condition interpreter (0x4150D0) matches each operation against a table of **37 condition codes** (0x5A06BC; ledger
  [DS1](ledger/skills.md), [DG1](ledger/scripts.md#dialog-script-stores-arc-dev-152)); the action interpreter (0x415BA0) against **35 action
  codes** (0x5A0750). An unknown code fails a condition and ends an action string (DS1).
- The conversation runtime
  ([grammar](../scripting/dialog-conversation-runtime.md#conversation-grammar-v1)) parses **16 condition codes**: `LF GF RE RA ME RU RQ` (the last two
  need the rumor port to run) and, behind an optional port, `RP` (reputation), `WA` (party), `PS HA SK TR` (skills), `GV PV PF` (script
  stores). Any other
  condition code makes the field `UnsupportedOperation`: the reply is offered blocked, or, when a whole reply group is needed, the step stops
  at a frontier (the block kind `TargetUnsupported` when the target node's reply group holds one).
- The names the owner met in play are `ch`, `in`, `ia`, `ni`, `qa`, `qb`, `le`, `lc` and the money code `$$`. The repository does not yet
  say what each one tests; the table of 37 codes has not been written down. The remaining codes are not enumerated here for the same reason.
- The only census so far
  ([skill gates](../scripting/dialog-conversation-runtime.md#skill-gates-arc-skills-004), first observation 2026-10-21, 709 DLG files):
  of 1,582 fields with a skill operation, 364 stopped at a code outside the subset (money `$$` 121, rumors 84, quests 47, and others).
  That counts only fields that also hold a skill operation, and the rumor share was closed by ARC-DEV-171 (RU1–RU4: 223 `ru`/`rq` fields in
  21 files, 20 still stop on damaged data). There is **no census of the other codes over all installed dialogs**.

## Slices

Each slice is its own task code, branch and PR. A slice adds codes to the grammar behind a typed port (as `RP`, `WA`, `GV`, `RU` were
added), records the original's behaviour in the ledger from the executable (prose only, no bytes or listings), updates the requirement
row and the table below, and keeps the unsupported rest a typed frontier. Original behaviour is followed in both rule modes unless the
original is a bug of the game (then the bug rule applies).

| Slice | Content | Needs |
| --- | --- | --- |
| **C0 census** | Read the table of 37 condition codes and the 35 action codes in the executable (names, operand reading, what each case tests, which owner it reads) and write them to the ledger. An opt-in `RealData` test (pattern of `DialogScriptStoreRealDataTests`; `DAT_ROOT`) counts, over every installed DLG the content stack resolves, per condition and action code: fields, files, and fields where it is the **first** code the runtime stops at. Fill the table below. | The owner's Mac and original data; Sonnet |
| **C1..Cn by owner** | Group the remaining codes by the state each one reads, as C0 establishes, and take the groups in order of C0's first-stop count (the 2026-10-21 census names money `$$` and quests as the largest known stops, nothing more). Each group needs a port on an existing owner (session items, quest log, hero facts, or whatever C0 finds); where the owner does not exist, the slice names it and waits. | C0; executable reading per group; owners from the session |
| **A-codes** | The same census covers action codes; a slice per group follows when an installed dialog stops on an action. Actions the runtime already shows blocked (barter, training, and so on) stay with their own tasks. | C0 |
| **Close** | When C0's table shows no installed dialog field stopping on an unsupported code, `FR-DIALOG-002` drops "other codes outside the subset" from its row. | All slices |

A slice that touches the save schema, the session core or threading is reviewed on Opus; the rest on Sonnet
([review model](../workflow/tasks.md#review-model)). Per PR, run only the smoke stages the change touches: talking to the Crash Site
NPCs is the usual one.

## Table of codes (filled by C0)

| Code | Test (executable) | Parsed | Fields | Files | First stop |
| --- | --- | --- | --- | --- | --- |
| `LF GF RE RA ME RP WA PS HA SK TR GV PV PF RU RQ` | recorded in the ledger (DS2, DG1, RU1, RU2 and earlier rows) | yes | see the sections of the runtime document | | |
| the other 21 | not yet read | no | not counted | not counted | not counted |

## Where this is linked

- [`FR-DIALOG-002` coverage row](../requirements/coverage/story-social-ai.md): "other codes" point here.
- [Conversation runtime, status and scope](../scripting/dialog-conversation-runtime.md#condition-codes-outside-the-subset-arc-res-091).
