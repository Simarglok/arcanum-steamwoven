# Minimal dialog contract (proposal only)

## Status and boundary

This is an engine-independent **proposal**, not a claim that a game-dialog
runtime is implemented or verified. It preserves source identity while refusing
to interpret unproven expressions, generated menus, resource precedence, UI,
world simulation, or SCR bytecode.

```text
DialogId, NodeId, ResponseId, ParticipantId
DialogQueryPort     // read-only state queries
DialogCommandPort   // approved state-changing effects
DialogTransition    // Direct(NodeId) | Exit | Special(tag) | Unsupported | Invalid
BranchEvaluation    // True | False | Unsupported | Invalid
```

A `NodeId` and `ResponseId` retain serialized numeric IDs. Participant IDs are
opaque caller-owned identifiers. The query port is read-only; the command/effect
port is distinct so a selector cannot mutate state while checking eligibility.
No port accepts arbitrary expression strings or SCR instructions.

## Proposed operations and failure mode

A branch-scoped condition/action is represented as a tagged operation with its
source identifier and parameter boundary retained separately from display text.
Only an operation observed and attributed in the selected evidence slice may be
admitted. Every other token is `Unsupported`; malformed data is `Invalid`.
Both evaluate fail-closed: an unsupported or invalid condition hides the branch,
and an unsupported or invalid action prevents the transition/effect.

Services are admitted only through selected evidence and are classified as:

| Service class | Proposed use | Current status |
| --- | --- | --- |
| `query` | read player/NPC state for an evidenced condition | no selected query is admitted: every nonempty condition in the selected start is unsupported |
| `command` | execute an evidenced, bounded state effect | no selected command is admitted: every nonempty action in the selected start is unsupported |
| `transition` | direct DLG target, exit, or special tag | positive/zero/negative serialized values are source facts; only a structurally validated positive direct target could be admitted |
| `unsupported` | generated menu, barter, UI, full SCR, callback-shaped `SC`, or unrecognized operation | fail closed; callback needs a later SCR/event bridge |

## Source-derived phase order

For a future proven slice, the phase order is proposed from the pinned CE
source path, not runtime observation:

1. resolve an evidenced entry;
2. read the current NPC record;
3. apply its evidenced DLG action(s);
4. enumerate ordered immediate response records;
5. apply gender/IQ and branch conditions;
6. expose response IDs;
7. select one response ID;
8. apply its selected response action(s); and
9. resolve a direct, special, exit, or SCR-bridge transition.

CE source sorts entries by numeric ID and scans subsequent player records with
a five-option bound (`src/game/dialog.c:1291-1357,2632-2637`); it uses the
serialized response value to distinguish direct, exit, and negative/special
forms (`2537-2549`). This is a **source fact** about that source revision, not
proof of original runtime ordering or a license to copy source code.

## Readiness split

**DLG-reader readiness: Blocked.** The physical seven-field subset, policy
caps, selector rejection, synthetic archive-path tests, and one read-only
candidate run pass; however it is not Ready because the observed nonempty
condition/action vocabulary is intentionally unsupported, original text
encoding/localization is unknown, and no independently sourced DLG grammar or
operation semantics has been established. This verdict is about a future
bounded reader, not a rejection of the measured physical structure.

**Game-dialog-runtime readiness: Blocked.** An actual entry, archive winner,
ordering, every operation meaning, callback/SCR bridge, transition semantics,
and each query/command service need separate evidence. Neither a reader
self-test nor a structural research-selected start proves runtime behaviour.
