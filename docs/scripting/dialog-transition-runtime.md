# Bounded dialog transition runtime

## Status and scope

`Arcanum.Scripting.Dialog` is an engine-independent, synchronous, non-thread-safe
net8.0 runtime. It consumes immutable `Arcanum.Data.DlgDocument`, starts a
caller-selected NPC record, exposes its immediate eligible Player records, and
executes at most one selected checked Direct edge plus the destination NPC entry
action. The caller must serialize session, port, and externally mutated state.

The separate multi-turn [conversation runtime](dialog-conversation-runtime.md)
(ARC-SCRIPT-005) has its own admission table; this runtime is unchanged.

This is not a DLG parser, text decoder, SCR/expression VM, UI, persistence,
transaction service, world integration, or full conversation traversal. It does
not infer NPC association, script invocation, archive precedence, or a runtime
entry. Original-game execution, compatibility, and failure parity are
unverified.

## Synthetic public usage

The [NPC invocation/entry contract](npc-dialog-entry-contract.md) and
[Crash Site evidence](../research/crash-site-npc-dialog-evidence.md) describe a
separate [bounded query-only entry bridge](npc-dialog-entry-runtime.md). It computes
a checked NPC entry without calling this runtime. The selected Virgil entry is not
supported by this runtime: it requires GF/IN/RE and has six structural immediate replies. Research
does not truncate the group or skip those effects to claim compatibility.

```csharp
var document = DlgReader.Read(syntheticDlgBytes);
var request = new DialogStartRequest(
    new DialogNodeId(100),
    new DialogParticipants(new DialogId(7), new DialogParticipantId(8), new DialogParticipantId(9)),
    new[] { new DialogResponseEligibility(new DialogResponseId(200), true, true) });
var start = DialogTransitionRuntime.Start(document, request, queryPort, commandPort);
if (start.Status == DialogStartStatus.AwaitingSelection)
{
    var result = start.Session.Select(start.Session.Offers[0].Token);
}
```

The ports are intentionally typed: local-flag reads use `(DialogId, NPC, index)`;
local-flag writes use `(DialogId, NPC, index, value)`; quest state uses `(PC,
quest, state, NPC context)`; rumor logging uses `(PC, rumor)`. No port accepts
raw dialogue text, bytes, or expressions.

## Phases and frontiers

| Phase | Meaning |
| --- | --- |
| `AwaitingSelection` | Source entry has completed, and this generation's immutable offers may be used. |
| `CompletedAtFrontier` | One eligible Direct response and checked target entry completed; target response enumeration does not occur. |
| `Failed` | A static or dynamic failure is terminal. Later calls are `WrongPhase` and call no port. |

The response group is the contiguous serialized Player run immediately following
the explicit NPC entry, ending at the next NPC or EOF. Serialized order governs
validation, local-flag query order, offers, and first failure. Numeric IDs are
identity/reference keys only and are never sorted or chosen as an entry policy.
The group is capped at five; a sixth response fails rather than truncates.

`Direct` has a checked positive NPC target. `Exit` is a null/zero response and
`Special` retains a negative signed tag. All three can be visible offers; only
Direct is executable by this runtime. Exit and Special selections are
`UnsupportedTransition` with no mutation or port call.

## Eligibility, tokens, and refresh

A request supplies exactly one duplicate-free `DialogResponseEligibility` row
for every grouped response. Gender then IQ booleans are caller-owned boundaries;
the runtime does not decode alternate display text or infer character stats.
An optional `LF` condition is read only after both booleans pass. Missing is
false.

Offers contain opaque `(response ID, generation)` tokens. `RefreshOffers` is
command-free, increments the generation once, and queries each otherwise
eligible local-flag condition once in serialized order. Selection requires a
current offered token, then repeats the selected gates and local-flag read before
commands. A changed/missing selected condition returns `StaleSelection`,
increments generation once, rebuilds the complete frontier, and reuses the
selected read so that response is not queried twice. This is synchronous
freshness policy, not a concurrent transaction.

## Admitted grammar and limits

Only `Condition` and `Action` field bytes are interpreted. Empty condition is
true; nonempty condition must be exactly one `LF`. Empty action has no command;
nonempty action is ordered `LF`, `QU`, and `RU` commands.

```text
separator = SP | HTAB | ',' | ';' | ':' | '(' | ')'
A = separator*
S = separator+
field = A operation (S operation)* A
operation = LF A int S int | QU A int S int | RU A int
int = '-'? ASCII_DIGIT+
```

Opcodes are exactly two ASCII letters and case-insensitive. The first integer
may be attached directly to one of these already admitted opcodes; the second
integer of `LF`/`QU` and every operation boundary still require `S`. This is a
narrow project safety-policy admission informed by the selected versioned slice,
not a claim about universal DLG or original-runtime grammar. `+` is rejected;
leading zeros and `-0` are admitted. LF index is `0..31`, LF expected/write
value is `0..1`, QU quest ID is signed Int32 and state is `0..6`, and RU ID is
`1000..Int32.MaxValue`. Unknown opcodes, combinators, callbacks, SC/generated
forms, and second condition operations are `UnsupportedOperation`. Non-ASCII or
control bytes, malformed syntax, overflow, arity errors, and out-of-domain
arguments are `InvalidOperation`.

Defaults cap responses at 5, a semantic field at 4096 bytes and 32 operations,
and an immediate step at 65536 semantic bytes and 128 operations. The whole-step
sum counts each `(serialized record index, field kind)` once, so shared Direct
target actions are deduplicated. All caps and grammar are statically validated
before source commands.

## Failure and receipt policy

Static failures (entry/role/eligibility, response cap, grammar/caps, target
structure) execute and query nothing. After prevalidation, source NPC actions
run once in operation order, then offers are evaluated. Each applied command is
recorded with typed command, source offsets and a global ordinal. A port
`Rejected` result guarantees that command had no effect; it records `Rejected`,
stops, and fails terminally. A thrown `Exception` records `Indeterminate` with
the same exception instance and fails terminally; it makes no rollback claim.
Earlier Applied effects and receipts remain visible. Query exceptions and invalid
query results have equivalent terminal accounting.

`DialogOperationSource` preserves typed record ID, serialized index, field kind,
payload offset, operation offset, length, and ordinal. Diagnostics intentionally
exclude raw operation bytes and display text.

## Verification and opt-in data method

Public integration tests use invented DLG bytes through the real `DlgReader` and
typed in-memory ports. They cover ordered command routing and provenance,
gender/IQ/LF gate order, refresh/stale generations, current-token checks,
conditioned Exit/Special zero-port selection, static fail-closed grammar and
ordered cap attribution, shared-target cap deduplication, direct/exit/special,
and command/query terminal-failure receipts.

The opt-in `DlgRuntimeRealData` category only reads existing
`arcanum3.dat` member `dlg\01478Erland.dlg`, checks the versioned archive
SHA-256 `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`
before and after, checks the observed structural 31-to-50 Direct classification,
and uses manual research IDs 30/31/50 with isolated in-memory typed ports. It
runs separate Found(0) and Found(1) sessions, proves one hidden and one visible
P31 gate without identifying the original expected value, and selects the
visible Direct edge to prove the N30 quest, P31 local-flag, and N50 rumor typed
receipt order. It logs neither extracted text nor operation bytes or parameters.
It must be run with `DAT_ROOT` pointing to the user-owned installation; an
absent or mismatched input fails safely as inconclusive operational evidence and
is never a pass.

## Provenance and exclusions

| Label | Applied meaning |
| --- | --- |
| Source-derived | The documented phase vocabulary and positive/zero/negative response roles are read from the original's dialog functions (F0, not checked in the executable), not copied implementation. |
| Observed structure | `DlgDocument` serialized order, roles, IDs, payload offsets, and the hash-gated local member are reader/data observations. |
| Project safety policy | Strict byte admission, caps, caller eligibility booleans, generation tokens, and per-command partial-effect accounting. |
| Synthetic result | The test suite's invented-byte, typed-port behavior only. |

No DialogProbe code is used or mechanically translated. The original game, callbacks/SCR, generated
menus, unsupported operations, archive winner, invocation, text encoding, and
full traversal remain outside the implementation.
