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
| `query` | read player/NPC state for an evidenced condition | proposal; no selected service is admitted yet |
| `command` | execute an evidenced, bounded state effect | proposal; no selected service is admitted yet |
| `transition` | direct DLG target, exit, special tag, or SCR bridge | positive/zero/negative serialized values are source facts; semantics remain bounded |
| `unsupported` | generated menu, barter, UI, full SCR, or unrecognized operation | fail closed |

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

DLG-reader readiness may become Ready only for a named observed grammar subset
when physical fields, comments/empties, ID/reference checks, caps, and the full
synthetic rejection suite pass. Game-dialog-runtime readiness remains Blocked
until an actual entry, archive winner, ordering, every admitted operation,
transition semantics, and each service have separate evidence. Neither a
reader self-test nor a structural research-selected start proves runtime
behaviour.
