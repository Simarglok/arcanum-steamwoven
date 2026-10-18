# Minimal NPC-dialog invocation and entry contract

## Status

This is an engine-independent bridge contract grounded in the
[Crash Site Virgil evidence](../research/crash-site-npc-dialog-evidence.md).
The bounded production [entry runtime](npc-dialog-entry-runtime.md) implements
attachment/resource binding and query-only entry selection, not a general SCR VM,
NPC lifecycle, dialogue UI or gameplay parity claim. The existing
[one-edge runtime](dialog-transition-runtime.md) remains
unchanged. Its LF/QU/RU ports and caller-selected NPC start do not implement this
whole original-data chain. No future implementation tasks are automatically authorized.

## Identity and source ownership

An invocation has distinct stable identities for world/module/map, NPC instance,
prototype, triggerer PC, optional extra object, event, effective script attachment,
selected SCR resource, current SCR line, selected DLG resource and DLG node.
Do not reuse script number as instance ID, SCR line as DLG node, or DLG lowest ID
as the start policy. A dialog/session identity must also bind participant context
and resource version/fingerprint; stale handles cannot select a new generation.

The NPC loader owns instance/prototype fields and presence masks. Effective
attachment selection is a **whole-array override**, not a per-slot merge. Missing
instance field may inherit prototype array; explicit overridden array without
SAP_DIALOG means no binding at that event. Invalid/duplicate identity, mismatched
prototype, malformed bitmap and missing location are errors, never guessed NPCs.
Map association carries original selected map metadata and aggregate provenance;
save/deletion/patch overlays require an explicit separately evidenced policy.

A resource service resolves a validated numeric catalogue key to exactly one
selected raw SCR resource and derives the DLG identifier by the evidenced naming
rule. Return fingerprint, locator identity, source-policy identity and content
version. Missing/ambiguous candidates are failures. Explicit selected archives
must not silently become a claim of game-wide precedence. Text bytes remain opaque
until a named decoder/variant policy is selected.

## Invocation and conditional entry resolution

For the investigated object-dialog route the invocation is SAP_DIALOG, triggerer
PC, attachee NPC, extra absent, SCR line 0. The event dispatcher owns speaking,
life/consciousness, combat, exclusivity, host and world admission queries; UI's
physical buttons/menu input are not SCR semantics.

The entry resolver consumes:

- effective script attachment identity, flags/counters and version;
- typed participants and caller SCR line;
- named SCR source/version plus bounded container/entry validation;
- query results with state-version bindings: following(NPC,PC), jilted(NPC),
  and encountered attachment-local flag bits;
- explicit capability set and traversal budget.

A result is either `ResolvedEntry(SCR line, DLG resource, NPC node, context/version)`
or a typed frontier/error. The selected evidence reaches line 4 → node 1 only
under the documented false predicates. True alternatives, missing query results,
unknown representations and unsupported operations may not fall through.
SCR callback context preserves both current SCR line and DLG start. Deferred
callbacks/return continuation, attachment-state writeback and original host-copy
quirks are separate dependencies, not assumed implemented or repaired.

## Query, command and display boundaries

Queries return value, owner/version and explicit failure; no absent result defaults
to false/zero. Commands return outcome and an ordered applied-prefix receipt.
A failed later command does not imply prior commands rolled back. Before mutation,
validate static structure and required capabilities for the admitted bounded slice.
Unknown opcode, arity, active operand representation or participant ownership is a
frontier with no replacement no-op.

| Capability | State owner / required contract | Current implementation |
|---|---|---|
| Following and jilted queries | World companion relationships; NPC and PC roles explicit | Typed version-bound entry ports implemented; actual state services missing |
| SCR-local flag reads/control flow/callback | Effective event attachment; source/version and SCR-line context | Bounded query-only entry slice implemented; callback/writeback missing (DLG LF alone is not dispatch) |
| Global flag writes | Shared world story state; typed identity/boolean, order/version | Missing |
| Inventory transfer | NPC/substitute inventory and PC; direction, lookup, transfer/drop failure policy | Missing |
| Reaction eligibility/set | NPC attitude toward PC; threshold sign and absolute/relative/set representation distinct | Missing |
| Quest/rumor commands | PC with quest/NPC context; PC rumor log | Existing QU/RU command ports only; state services are external |
| LF query/write | DialogId + NPC + bit identity/value | Existing narrow DLG ports; mapping to effective attachment remains adapter work |
| Gender/IQ | PC stats/type/dumb state; gender → IQ → condition ordering | Caller booleans accepted; computation missing |
| Speech metadata | NPC Condition is speech/media metadata, not Player gate | Missing media resolution |
| Text | Role-specific alternate semantics, explicit lossless decoder, substitution dependencies | Reader preserves bytes; formatter/UI missing |

Node 1's seven effects must retain GF/GF/GF/IN/GF/QU/RU order. Existing QU/RU
support is not permission to omit the preceding effects. Reaction is queried after
entry effects. Player RE commands run only on selected replies, before target
entry effects. Destination node 9 has no entry action in the inspected step.

## Lifecycle and frontier policy

Proposed phases:

1. `Unresolved`: validate participants, world context, identities and resources.
2. `EntryResolved`: conditional SCR trace yields checked DLG NPC start; no UI yet.
3. `Admitted`: all needed bounded structure/capabilities/query versions checked.
4. `Entered`: apply ordered NPC entry effects, then compute eligible replies.
5. `AwaitingSelection`: issue generation-bound stable response identities/tokens.
6. `AtFrontier`: one checked selected edge and target entry complete; stop before
   target reply evaluation.
7. `Failed`: preserve explicit error and applied prefix; stale/repeated selections
   do not repeat commands. Recovery/new invocation requires a new generation.

The current runtime has its own narrower AwaitingSelection/CompletedAtFrontier/
Failed phases. It groups contiguous serialized replies, rejects a sixth structural
reply and executes one Direct edge. The original numerically sorts and caps
*eligible* replies (F0, not checked in the executable). Selected grouping equivalence is not global equivalence. Virgil's six
structural replies plus missing GF/IN/RE make current admission unsupported.
Static inspection can record the chosen conditional response 2 → node 9, but
cannot report executed gameplay or hide those admission failures.

Exit, special/generated transitions, return-to-SCR callbacks, repeated Direct
traversal and automatic event-driven first speech are separate future capabilities.
Current Exit/Special offers are non-executable selections with no mutation, not
working conversation closure. No physical controller/menu contract is specified.

Errors/frontiers retain category, originating layer, participant/event/resource
identity, source version, record/entry identity, required missing capability,
state version, applied command prefix and stop reason. Public diagnostics must
redact original text/expressions/values and local paths. Relevant categories:
missing/ambiguous identity/resource, malformed fields/bitmap/count, wrong NPC role,
unknown query, unsupported operation/operand, cycle/budget, stale context/token,
command failure, unknown codec/formatter, save/patch source policy unresolved.

## Text policy and future dependency map

For selected ASCII fields, strict lossless ASCII decoding is sufficient as a
research subset; it is not an installation-wide charset. High bytes require an
explicit evidenced decoder. NPC ordinary/female field is selected by PC context;
Player alternate is gender eligibility. Preserve missing alternate, signed IQ
bounds, dumb adjustment, metadata versus conditions, and absence of substitutions.
Unknown tokens/delimiters fail closed in future safe formatting rather than copying
unsafe source behavior. Font/glyph/media/UI observation remains separate.

Implementation can later be split without inventing a full engine:

- NPC loading: validated mobile/prototype masks/arrays, map association, overlay
  policy and stable identity; prerequisite to event binding.
- Minimal event/SCR bridge: typed invocation/state owners, the evidenced query and
  control-flow subset, conditional callback/entry and writeback; no general VM claim.
- State adapters: global flags, inventory and reaction first; QU/RU real services
  and ordered partial-failure receipts, not no-op mocks.
- Dialogue policy/traversal: source-versus-project reply grouping/cap decision,
  eligibility, one edge versus subsequent frontier, Exit/callback semantics.
- Text/media: lossless codec, role variants, substitution and speech resolution.
- UI: consume typed snapshot/tokens and report selection; physical input, layout,
  focus and rendering tests do not prove SCR semantics.

Each future capability needs separate owner acceptance and independent tests.
This research creates none of those production tasks and does not waive missing
state, original execution or original display evidence.
