# Minimal dialog contract (proposal only)

## Status and boundary

This is an engine-independent **proposal**, not a claim that a game-dialog
runtime is implemented or verified. It preserves source identity while refusing
to interpret unproven expressions, generated menus, resource precedence, UI,
world simulation, or SCR bytecode.

For the implemented bounded transition subset, see
[`dialog-transition-runtime.md`](dialog-transition-runtime.md). That runtime
supersedes this proposal's blanket no-effect wording only for dynamic service
failures: static prevalidation is zero-command, while already Applied commands
remain recorded and an exception attempt is indeterminate. This historical
proposal remains the evidence boundary; neither document claims original-game
runtime compatibility.

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
For the bounded structural probe, the first callback, unsupported operation, or
other fixed stop class ends frontier inspection after classifying that row; it
must not select a later otherwise-direct response.

### ARC-RES-005 historical service table

The following table records the older unsupported `01324Virgil` structural-start
scope. It is superseded for the separately evidenced ARC-RES-006 N30 -> P31 ->
Direct(N50) slice by the current proposal below; it is retained to preserve the
earlier bounded conclusion rather than state the current service verdict.

| Service class | Proposed use | ARC-RES-005 status |
| --- | --- | --- |
| `query` | read player/NPC state for an evidenced condition | no selected query is admitted: every nonempty condition in the selected start is unsupported |
| `command` | execute an evidenced, bounded state effect | no selected command is admitted: every nonempty action in the selected start is unsupported |
| `transition` | direct DLG target, exit, or special tag | positive/zero/negative serialized values are source facts; only a structurally validated positive direct target could be admitted |
| `unsupported` | generated menu, barter, UI, full SCR, callback-shaped `SC`, or unrecognized operation | fail closed; callback needs a later SCR/event bridge |

## ARC-RES-006 selected service proposal

This proposal extends the older unsupported `01324Virgil` structural-start
record only for the separately evidenced N30 -> P31 -> Direct(N50) slice. It is
not a runtime implementation and no original parameter values or expressions
are represented here.

```text
LocalFlagQuery(DialogId, NpcParticipantId, localFlagIndex) -> 0 | 1 | missing
LocalFlagWrite(DialogId, NpcParticipantId, localFlagIndex, newValue)
QuestStateCommand(PcParticipantId, questId, questState, NpcParticipantId)
RumorLogCommand(PcParticipantId, rumorId)
DirectTransition(TargetNpcNodeId)
```

`DialogQueryPort` admits only `LocalFlagQuery` and cannot mutate state.
`DialogCommandPort` admits the three typed commands above. The bounded proposal
uses local-flag index `0..31` and boolean values `0..1`, quest-state range
`0..6`, and rumor ID domain `>=1000`. It does not infer quest monotonicity,
failure semantics outside the command result, rumor availability/quell/history,
or any global/player-local flag behavior.

For the selected slice, N30 `QU(int,int)` issues `QuestStateCommand` before
response enumeration. P31 passes gender/IQ eligibility before its
`LF(int,int)` condition queries N30's dialog-attached local state. Only a true
condition exposes P31. After caller selection, P31 `LF(int,int)` issues
`LocalFlagWrite`, then the positive serialized edge is structurally resolved as
`Direct(N50)`, and N50 `RU(int)` issues `RumorLogCommand`. The walk stops at
N50's next decision frontier. Failure of an invalid/unknown operation,
malformed/out-of-policy parameters, missing/wrong-role target, missing query, or
command returns a fail-closed no-effect/no-transition result. This is an
independently authored project proposal tested with synthetic fixtures, not a
claim about original runtime failure handling.

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

## Ordered synthetic-state walkthroughs

The following examples use invented IDs, flags, and operation tags only; they
contain no game dialogue or extracted operation text. They specify the proposed
contract above. The phase ordering is source-derived from the cited CE revision;
the stated fail-closed outcomes and the direct-candidate ordering are also
structurally tested by synthetic `DialogProbe` fixtures. Neither kind of
evidence is a claim that the original game runtime performed these transitions.

### Admissible direct transition (synthetic structural case)

Initial synthetic state: current NPC `N100`; readable flag `met=false`; no
commands have executed. The fixture has three otherwise-admissible responses:
`R200 -> N300`, `R210 -> N250`, and `R220 -> N250`; all conditions/actions are
empty, so no query or command service is invoked.

1. Proposed/source-derived order: enumerate the immediate responses, query no
   state, and retain their response IDs and positive targets.
2. Structurally tested ordering: choose `R210`, because target `N250` is the
   smallest target ID and `R210` is the smallest response ID among the tied
   `N250` candidates. `R200` is not selected merely because its response ID is
   smaller.
3. Selected response: `R210`; effect/state after selection: no command and no
   synthetic-state mutation; selected ID is `210`.
4. Transition: include direct target `N250`, then expose its next immediate
   response frontier without selecting a second response. The structural probe
   reports `second-decision-frontier`; it does not execute a runtime dialogue.

### Unsupported or invalid condition (synthetic fail-closed case)

Initial synthetic state: current NPC `N100`; response `R200 -> N250` has a
nonempty condition tag that this bounded contract does not recognize. Query
result: `Unsupported` (an invalid condition would yield `Invalid` instead).
Selected response: none, because the branch is hidden. Effect/state after:
no command runs and state is unchanged. Transition: `Unsupported`/`Invalid`,
not `Direct(N250)`; later responses are not inspected after the fixed stop.

### Unsupported or invalid action (synthetic fail-closed case)

Initial synthetic state: current NPC `N100`; response `R200 -> N250` has an
otherwise admissible condition but a nonempty unrecognized action tag. Query
result: `True` for the synthetic eligibility check; command result:
`Unsupported` (or `Invalid` for malformed action data). Selected response:
`R200` is not admitted for a transition. Effect/state after: no command runs
and state is unchanged. Transition: blocked as `Unsupported`/`Invalid`, never
`Direct(N250)`; a later direct response is not selected past that fixed stop.

### Selected N30 -> P31 -> N50 proposal (synthetic fixture)

The test fixture uses invented N100/P200/N300 identifiers and invented state
values; these names only mirror the selected operation positions, not original
dialogue content. Before the walk, the typed query port holds one NPC/dialog
local flag and the command log is empty.

1. N100 runs its ordered QU command list through the command port before any
   response is exposed.
2. P200 first passes the synthetic gender/IQ gate, then LF reads the typed
   NPC/dialog local flag. A false or missing read hides P200; no P200 LF write,
   RU command, or transition occurs.
3. On P200 selection, its LF write runs before target resolution. A failed
   write blocks the direct transition and prevents the target RU command.
4. The direct target is checked as N300/NPC, then N300 runs RU through the PC
   log command port and the walk stops at its next frontier.

The self-test also uses multiple invented QU actions to prove command order,
asserts PC/NPC/dialog participant routing, and separately retains structural
direct/exit/special classification. It never executes original game dialogue.

## ARC-RES-005 historical readiness snapshot

**Historical ARC-RES-005 DLG-reader readiness: Blocked.** The physical seven-field subset, policy
caps, selector rejection, synthetic archive-path tests, and one read-only
candidate run pass; however it is not Ready because the observed nonempty
condition/action vocabulary is intentionally unsupported, original text
encoding/localization is unknown, and no independently sourced DLG grammar or
operation semantics has been established. This verdict is about a future
bounded reader, not a rejection of the measured physical structure.

**Historical ARC-RES-005 game-dialog-runtime readiness: Blocked.** An actual entry, archive winner,
ordering, every operation meaning, callback/SCR bridge, transition semantics,
and each query/command service need separate evidence. Neither a reader
self-test nor a structural research-selected start proves runtime behaviour.

### ARC-RES-006 readiness update

**Physical DLG reader: Ready** for its stated bounded byte-preserving subset.
**Selected semantic subset: proposal/synthetic-ready** for only LF condition,
QU/LF/RU commands, and a checked positive direct edge; the real hashed slice is
structurally validated but its execution is not observed. **Actual in-game
conversation: Blocked** by runtime entry/invocation, identity, precedence,
generated dialogue, SCR bridge, nonselected operations, game services/UI and
gameplay. These three verdicts are deliberately independent.
