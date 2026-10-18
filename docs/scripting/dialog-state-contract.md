# Bounded dialog state and reply contract

## Scope, status and provenance

ARC-RES-019 is **research/contract only**. This document does not add a state
service, expression VM, UI, conversation loop or gameplay integration. Virgil's
conversation **does not work in the current runtime**. ARC-SCRIPT-005 later added a
separate bounded [conversation runtime](dialog-conversation-runtime.md) with its own project
decisions (source five-eligible short-circuit instead of the overflow proposal below, typed
diagnostic world port); this research document is not retroactively accepted by it. The merged
[entry resolver](npc-dialog-entry-runtime.md) computes a checked entry without
starting [DialogTransitionRuntime](dialog-transition-runtime.md). That runtime
admits LF/QU/RU only and rejects a sixth structural response before any ports.
See the [invocation contract](npc-dialog-entry-contract.md) for upstream identity
and event admission; no dirty rendering/input candidate is a source for this work.

ARC-SCRIPT-003 separately accepts and implements only the bounded
[versioned global-flag owner and standalone typed GF adapters](global-flag-state-runtime.md).
Missing admitted bootstrap bits stay Unknown; equal known writes retain a
successful no-change receipt without a revision bump. This does not adopt the
remaining proposals below or wire GF into the current DLG parser/session: GF
remains Unsupported there, and the complete Virgil trace is not implemented.

Labels used throughout:

- **S — Source-confirmed:** a reading of the original's functions at the cited
  addresses, evidence level F0 (not checked in the executable) unless the
  [verification ledger](../research/original-verification-ledger.md) lists them;
  not proof of the original executable's runtime behavior.
- **D — Data-observed:** hash-gated selected original fields/identifiers only;
  not a reconstruction of live new-game world state.
- **P — PROPOSAL:** independently authored project safety/contract recommendation.
  Document review or merge does **not** accept it as a gameplay rule or API.
- **U — Unknown / NOT_VERIFIED:** concrete verification routes are listed below.

Source-backed algebra, field observations and synthetic worksheets are
distinguished from production execution. Original display/text/operands remain private and opaque; all literal
operands and participant names in the conformance matrix are invented.

## Evidence index and selected identity

Each address below names the actual caller or callee, not just an adjacent
function. Local project seams are bound to merged
baseline `207d55f14958313c60a3f126eb303a449e91fc87`; SCRIPT-002's reviewed head
`a9fb4401c8ac2455a0517d0e722c8ff99fb01eaf` and owner merge
`2a859d5711af2af91d512b492c41e8d5edbedc76` are ancestors of that baseline.
SCRIPT-001's original changes (`fedaf90`, `251ed12`, `adccc48`) explain the
current LF/QU/RU grammar and structural cap; their source, not card completion,
is the implementation frontier.

| Key | Functions read (address) / selected observation | Evidence scope |
| --- | --- | --- |
| E1 | [Selected NPC chain](../research/crash-site-npc-dialog-evidence.md), NDE-01..06 | Base map5000; unique instance ObjectID SHA256 `bd0cff305f47b07e845a120966b402c5c44073aba7ffd1407b626f93733bf601`; prototype17102; description31073; instance SAP_DIALOG event9/script1324 whole-array override. No save/patch precedence claim. |
| E2 | Same evidence, conditional caller/entry; [entry production source](../../src/Arcanum.Scripting/NpcDialog/NpcDialogEntryResolver.cs) | PC triggerer, NPC attachee, extra absent, caller SCR line0. Following false, two attachment-local flags false, jilted false reach SCR line4 → DLG NPC1. Those booleans are diagnostic assumptions, not observed world state. |
| E3 | NPC entry before enumeration (0x4167C0, 0x416840), selected Player action before transition (0x413130), Direct dispatch (0x414810) | Fresh-entry effects occur before eligibility; selected action occurs before target entry. Regeneration (`randomize=true`) skips entry effects in source, not a retry recipe for failed commands. |
| E4 | GF query (0x4150D0), GF action (0x415BA0), global flag owner (0x444CF0, 0x444D20) | Two operands, bit identity and comparison/write value; world-global, not PC or attachment-local. |
| E5 | IN query (0x4150D0), IN action (0x415BA0), lookup (0x462480), substitute (0x45F650), transfer wrapper/unparented GET (0x4617F0, 0x461810), parented transfer (0x461CA0), placement (0x466E50, 0x466EF0) | Sign/direction, first name match, conditional substitute/followers, transfer then parent check and fallback; conditional SAP_GET/SAP_TRANSFER and existing/new junk-pile insertion. No count argument. |
| E6 | RE query (0x4150D0), RE action (0x415BA0), reaction service (0x4C0CC0, 0x4C0CE0, 0x4C0D00, 0x4C0DE0) | Signed threshold vs lexically signed delta vs unsigned target adjustment; effective query and mutable reaction storage are not identical quantities. |
| E7 | QU action (0x415BA0), RU action (0x415BA0), LF query (0x4150D0), LF action (0x415BA0), attachment flags (0x444F10, 0x444F60) | PC quest with NPC context; PC rumor; effective NPC SAP_DIALOG attachment flags, not DLG asset bits. |
| E8 | Numeric sort (0x417720), comparison (0x417E00), gender/IQ/condition/cap (0x414E60, 0x414F50), role/gender parse (0x417860) | Numeric source grouping, five eligible candidates; gender → adjusted IQ → condition; Player alternate is selector, NPC Condition is media metadata. |
| E9 | Second operand scan (0x4167C0), action integer scan (0x415BA0) | The original's operand scans use permissive `atoi` plus a digit/whitespace scan (F0), not a safe general expression grammar. Strict byte admission remains project policy. |
| E10 | [Current parser](../../src/Arcanum.Scripting/Dialog/DialogOperationParser.cs), [session](../../src/Arcanum.Scripting/Dialog/DialogSession.cs), [ports](../../src/Arcanum.Scripting/Dialog/DialogPorts.cs), [limits/types](../../src/Arcanum.Scripting/Dialog/DialogRuntimeTypes.cs) | Only LF conditions, LF/QU/RU actions; serialized structural cap5; generation tokens; partial-effect accounting. Ports are not actual state services. |
| E11 | [Read-only worksheet](../../tools/research/dialog_state_probe.py), original mode | Independently reads bounded selected SCR/DLG via existing DAT probe; verifies hashes/arity/shapes, replies2..7 →9, seven effects and unsigned Player RE. It does not reimplement the whole NPC loader or SCR VM. |

D identity/fingerprints reused from E1 and rechecked for the selected SCR/DLG by
E11: script archive `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`,
dialog archive `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`;
selected SCR SHA256 `f726b47ca8a960b9d876583320fb9592bd79009ff1880c71650bceecfa43e4fe`
(6268 bytes, 47 entries), DLG SHA256
`34a49a2c160cf8a97856cc0a9e48baa04fe0611c5d6bf05b5f52a7310f45e53a`
(409 unique records). Both full selected archives have equal before/after hashes.
The worksheet does not independently reprove object attachment masks; E1 remains
the evidence for them. Raw resource spelling, payload, expressions and original
operand values are not published.

## Operation, representation and ownership matrix

`n`, `b`, `s`, `t`, `d`, `q`, `r` below are **metavariables**, not original values.
Arity counts active scalar operands, not mnemonic, context or unused SCR slots.
These are DLG ASCII decimal fields, not SCR's tagged 44-byte instructions.
`+`/`-` lexical presence must survive conversion; `-0` is not an IN negative
direction once numerically zero, but RE `-0` is a lexically relative action.

P shared version rule V: a query returns an explicit value or Unknown/Unavailable/
Failed/Stale plus exact participant/request identity and owner epoch/revision.
A command consumes expected versions and returns affected owner versions and a
receipt. The original has no such typed version contract. Invalid arity, overflow,
unbound owner, non-ASCII or unsupported form fails admission before ANY query or
mutation; runtime refusal stops at the applied prefix, never a replacement no-op.

| Operation and exact evidenced form | Role, identity and owner; read/write | Ordering, version and frontier |
| --- | --- | --- |
| S GF condition: `GF n b`, arity2, decimal index plus value; D selected GF actions write boolean0/1, not conditions. P admits only unsigned valid index/boolean comparison. | World/module global story bit `(WorldId, FlagId)`; NPC/PC are context, neither owns it. Read bit, compare equality. Source setter does not itself validate boolean or bounds. | Player gate after gender/IQ and entry effects; V(global). Unknown index/domain is static frontier, not “unset”; absent value is not false. E4/E9; C01,C02. |
| S/D GF action: `GF n b`, arity2; four selected instances, no observed relative form. | Same world-global bit; write explicit bool, not toggle or LF. | Preserve all four writes around IN; V(global), receipts even if already equal. P index catalogue/range must be bound by owner; no general GF opcode/VM support claim. E4; C01,C09,C16. |
| S/D IN action: `IN s`, arity1 signed decimal item **name** ID. Selected `s<0`. | Negative: NPC primary inventory → conditional substitute → PC recipient. Nonnegative source branch: PC → followers → NPC. Concrete ItemInstanceId/actual source owner resolved separately; name is neither prototype ID nor item instance. | Read lookup before transfer, then check final parent, then fallback. V(inventory/participants/location). P initially admits strictly negative nonzero form only; zero, positive/follower/destroy, unknown source or callback dependencies are explicit frontiers. E5; C03..C07,C16. |
| S IN condition: `IN s`, arity1 signed item-name decimal. No selected entry reply uses it. | Negative checks NPC then substitute presence; nonnegative checks PC then followers. Read-only presence, no transfer/count. | Not action semantics and not auto-admitted by supporting negative action. P defer both query directions pending bounded lookup service; Unknown distinct from proven absent. E5; C07,U3. |
| S/D RE condition: `RE t`, arity1 signed decimal threshold. `t>=0`: effective reaction >=t; `t<0`: <=abs(t), inclusive. | NPC's reaction **toward this PC**, ordered key `(NpcInstanceId, PcId)`; effective read may include modifiers, hostility and mind control. Neither global scalar nor PC reaction toward NPC. | After source entry and gender/IQ. V(reaction plus dependencies), inclusive boundary C08. Reject Int32.MinValue negation overflow and missing query; no guessed neutral50. E6. |
| S/D RE unsigned action: `RE n`, arity1, decimal WITHOUT leading sign; observed on Player4,5,7. | Read effective reaction `R`, request adjustment `n-R` through reaction owner. “Absolute target intent”, NOT guaranteed stored/effective result n under modifiers. | Only selected Player's action; before target entry. P typed TargetReaction(n), bounded neutral diagnostic adapter may yield n; production adapter must implement/evidence modifiers or frontier. V(reaction/dependencies). E6; C09,C10,U2. |
| S RE signed action: `RE +d` or `RE -d`, arity1, lexical sign preserved. Not selected in this step. | Relative adjustment d of same NPC→PC relationship, not unsigned target. Signed zero stays relative. | Source-confirmed distinction, P separate AdjustReaction(d) capability initially deferred. Do not treat every RE action as set. E6; C10,U2. |
| S RE leading `>`/`<` action branches | Source tests marker after value was parsed by `atoi`; marker-prefixed decimal therefore is not demonstrated as a working clamp-to-following-number grammar. | U intended/original behavior; P reject these forms, do not silently repair into min/max. E6/E9; C15,U1. |
| S/D QU action: `QU q s`, arity2 decimal quest identity/state | PC quest owner `(PcId, QuestId)`, NPC context carried; write. Current port accepts signed Int32 quest ID/state0..6. | Sixth entry command, V(quest and dependent owners). Existing signature is not implementation of quest state/reaction consequences. P adapter advertises exact state domain and dependent version effects. E7/E10; C09,C17. |
| S/D RU action: `RU r`, arity1 unsigned selected decimal | PC rumor log `(PcId, RumorId)`; write known state. Current narrow port requires r>=1000. | Seventh entry command even if no visible reply uses rumor. V(rumor); rejection/exception stops. No generic rumor subsystem claim. E7/E10; C09,C17. |
| S LF condition/action: `LF n b`, arity2, existing project domain index0..31/value0..1 | NPC's effective SAP_DIALOG attachment header, including inherited/overridden attachment identity; current DLG port uses `(DialogId,NPC,index)` and needs an adapter to that owner. Read comparison/write bit. | SCR entry flag queries and DLG LF access same effective attachment only after explicit binding; never the immutable SCR initial header or DLG asset. V(attachment), source version/generation separate. Existing Missing→false rule is narrow current behavior, not new Unknown handling. E7/E10; C18. |

QU/RU/LF conditions outside the existing LF condition subset are source seams,
not newly admitted expression support. Callback effects from inventory transfer
or quest/reaction services may alter other owners: a service that cannot provide
a bounded dependency/receipt contract must return a capability frontier rather
than be replaced by a dummy successful port.

P byte/operand admission for a later implementation: case-insensitive two ASCII
letters; decimal Int32 tokens only; the first numeric operand may be adjacent
to the mnemonic, later operands require space/tab and operation boundaries may
use space/tab/comma/semicolon. GF requires unsigned index in the explicitly bound
world flag catalogue and value0/1; IN requires negative nonzero item-name whose
absolute value fits positive Int32; RE query excludes Int32.MinValue and preserves
threshold sign, RE target is unsigned Int32 within the reaction owner's accepted
target domain. Do not invent a universal0..100 reaction clamp (E6 permits values
outside it). QU/RU/LF retain E10's narrow domains until an accepted owner decision
changes them. Parentheses/colon, comparisons/combinators, additional arguments,
overflow, unknown forms or missing owner domain are frontiers. This strict P
grammar is not the original's permissive parser and not the helper's general production API.

### IN identity, quantity, transfer and refusal

S lookup returns the **first** object with matching `OBJ_F_NAME` in inventory
list order. Substitute inventory is available only to an NPC with no leader,
a non-null substitute and distance<=20; do not always use it or confuse it with
extra/triggerer. If both primary and substitute lack the item, source action
continues without transferring anything; this is a checked source absence case,
not permission to omit IN wholesale. Positive-direction lookup additionally
searches PC followers in their source enumeration order (not sorted IDs).

There is no quantity operand or “create item” behavior in IN. Source transfers
the found object handle, without an explicit split/decrement of its quantity.
P record the concrete instance and its quantity before/after; initial diagnostic
profile uses a nonstacking item (quantity1). Stack coalescing/destruction, stacking
quantity, insertion/slot/equipment side effects and transfer scripts remain U3.
The item adapter must either evidence those outcomes or refuse their capability;
“one command” does not prove “one unit”.

The [ARC-RES-021 negative-IN contract](negative-inventory-transfer-contract.md)
refines this U3 source trace with exact callback participants, list/slot/stack
effects, placement/decay and a typed failure/conformance matrix. Nonstacking
quantity1 is a proposed capability profile, not an observed original quantity or
accepted gameplay rule. The selected original instance/type/quantity/live-parent
joins remain NOT_VERIFIED; no production inventory adapter is introduced.

S negative direction (0x415BA0) attempts transfer to PC, ignores its boolean result and
checks parent identity. If parent is not PC, removes the item and calls placement
at PC location. Placement (0x466E50, 0x466EF0) may insert into/create a junk pile, drop to ground and
schedule decay; it is not always a bare world drop. Positive direction on parent
mismatch places at NPC location if NPC has a PC leader, otherwise destroys the
item. These positive/destroy forms are **not** in the recommended initial subset.

P a negative-IN service returns Transferred(instance,newParent),
Placed(instance,location,container-or-ground), Rejected(no effect), or
Indeterminate(partial outcome + authoritative recovery reference). Proven missing
item is an explicit `MissingItem` rejection in the initial safety profile, a
**deliberate divergence** from the original's continuation (F0) requiring owner acceptance. Refused
transfer with a successful verified fallback is Applied/Placed, not automatically
failure; fallback refusal after removal is Indeterminate, not Rejected. A source
transfer wrapper dispatches to parented SAP_TRANSFER (0x461CA0) or
unparented SAP_GET (0x4617F0, 0x461810) depending on the item's current parent:
no callbacks/full VM are claimed here.
No inventory object/name/order/quantity is reconstructed from dialogue display.

## Complete entry and one Direct trace

D/S selected trace T1 starts with E1/E2's conditional entry. NPC1's Condition is
speech metadata, **not** a Player eligibility expression. It is routed separately
from gameplay queries; no original text enters a state port.

| Ordinal | Selected record / stage | Required operation shape and effect boundary |
| --- | --- | --- |
| 1 | NPC1 entry action1 | GF(two decimal operands), world bit write |
| 2 | NPC1 entry action2 | GF(two decimal operands), world bit write |
| 3 | NPC1 entry action3 | GF(two decimal operands), world bit write |
| 4 | NPC1 entry action4 | IN(negative decimal), lookup NPC/substitute → transfer/placement to PC |
| 5 | NPC1 entry action5 | GF(two decimal operands), world bit write |
| 6 | NPC1 entry action6 | QU(two decimal operands), PC quest/NPC context |
| 7 | NPC1 entry action7 | RU(unsigned decimal), PC rumor log |
| Q | Player2..7 enumeration | Gender → adjusted IQ → RE signed threshold using **post-entry** versions |
| 8 | Selected Player4 only | RE(unsigned decimal), target-intent adjustment NPC→PC |
| 9 | Checked Direct → NPC9 | Empty target entry action; **zero commands**, not a missing/no-op substitute |
| F | AtFrontier(NPC9) | Stop before enumerating NPC9 replies, callbacks or another edge |

Selection of Player4 is **conditional** on its current gates; the actual-data
predicate worksheet admits subset `[4,7]` under appropriate diagnostic values.
No original reaction/IQ value is asserted. Player2/3/6 have empty actions;
Player4/5/7 have one unsigned RE each. All six Direct targets are NPC9. Selecting
Player2 as in earlier research would have no Player mutation; it cannot establish
RE semantics. The synthetic T2 below has a nonempty target action specifically
to independently test Player-before-target order; it is not NPC9's action.

Every command is admitted and accounted for, even when the chosen reply does
not depend on it. Static checks cover the entire immediate group, all response
actions and distinct Direct target actions before source entry mutation. A
runtime port failure preserves completed prefix, stops remaining commands and
withholds offers/target completion. No atomic conversation transaction or
rollback is inferred from the source.

## Reply grouping and recommended bounded policy (PROPOSAL)

S the original loads records then numerically sorts IDs, finds the NPC by ID, scans following
Player records until the next IQ0 NPC, and short-circuits at **five eligible**
rows. Gender is checked first, IQ next (dumb PC with IQ>LOW_INTELLIGENCE uses1),
then conditions. Generated option building can further remove a candidate and
produce a generic fallback; that text/option-generation path is excluded here.
D selected serialized and numerically sorted groups both equal `[2,3,4,5,6,7]`.
This proves agreement for this group only, not order invariance for arbitrary DLG.

D/E11 algebraic enumeration of the selected gender/IQ/RE predicates across integer
interval representatives yields subsets `[2,6]`, `[3]`, `[4,7]`, `[5]`, maximum2
for the named diagnostic selector domain0/1. These are **source-predicate
worksheet results**, not observed original sessions. Six structural rows therefore
do not imply six concurrently eligible. Effectful query behavior, real PC stats,
reaction modifiers and original runtime parity remain U2/U4.

E10 currently rejects structural6 even if only one qualifies. It neither sorts
numeric IDs nor filters before its structural cap. Raising its cap, taking the
first five structural rows, swapping the group or ignoring GF/IN/RE would not
implement this contract.

P recommend ONE initial policy `selected-equivalent-group-v1`:

1. Retain a contiguous serialized group, but prevalidate that its ordered IDs and
   next-NPC boundary equal those of the numerically sorted source view for this
   entry. Mismatch → GroupOrderFrontier before ports; no silent reorder. This
   bounded admission protects the proven selected slice without changing all DLG.
2. Hard cap **32 structural** rows solely as a resource budget; prevalidate ALL
   their admitted gates/actions/targets before effects, including currently
   hidden rows. Six is admitted only with these semantics/services, not by raising
   the existing runtime's option alone.
3. After all seven successful effects, evaluate ALL group's gender/IQ/conditions
   in the equivalent serialized order against a coherent post-entry snapshot.
   Return every eligible row if count<=5. **Six eligible → EligibleOverflow**,
   preserve the entry receipt and fail closed; never truncate, paginate, synthesize
   a reply, or execute an unadvertised sixth. No eligible → EmptyEligibleFrontier,
   not a generated generic conversation.
4. Refresh/selection revalidation is command-free and uses fresh versions;
   selected response effects are never run during filtering. Stop after one
   checked Direct and target entry; do not enumerate the next frontier.

Rationale E3/E8/E10/E11: selected order equivalence and at-most-two worksheet
eligibles allow a meaningful bounded extension, keep source's visible cap5,
avoid silent loss and preserve full command order. Evaluating past fifth eligible
and rejecting overflow intentionally **differs** from the original's short-circuit-first5 (F0);
there is no original-runtime parity claim. Owner must later accept structural32,
equivalence restriction, overflow/empty failure, missing-item failure,
version/token/profile policy and exact admitted operand domains. They remain
recommendations, not production requirements adopted by this document's merge.

## Typed Godot-free seams and lifecycle (PROPOSAL)

Names here are specification vocabulary, **not existing C# declarations**.

| Seam | Typed input → output and owner contract |
| --- | --- |
| Context | WorldId, ModuleId, MapId, NpcInstanceId, PrototypeId, PcId, optional SubstituteInventoryId, extra absent, SAP_DIALOG attachment identity, selected SCR/DLG resource hashes/policy/version, SCRLine4 distinct from Node1, SessionId; explicit participant roles, no Godot handle or original text. |
| Snapshot | SourceGeneration + participant identity + owner-version vector: globals, NPC/PC/substitute inventories, location, attachment, effective reaction dependencies, PC gender/IQ/dumb, quest and rumor. Each owner uses nonempty epoch plus monotonic revision; cross-owner host serialization/lease is required. |
| Queries | GlobalFlag(World,Flag), AttachmentFlag(Attachment,Bit), EffectiveReaction(NPC,PC), ItemLookup(SourceInventory,ItemName), SubstituteInventory(NPC), ItemParent(Item), ParticipantLocation(PC), PC eligibility facts. Reply: Known(value,identity,versions), KnownAbsent (where meaningful), Unknown, Unavailable, Failed, Stale; never default false/zero or guessed item. |
| Commands | WriteGlobalFlag(World,Flag,bool), NegativeInventoryGive(NPC,PC,ItemName,resolved instance/source/version,placement policy), TargetReaction(NPC,PC,target), QuestState(PC,Quest,State,NPC), LogRumor(PC,Rumor), AttachmentFlagWrite(binding,bit,bool). Separate capability for relative reaction and positive IN, deferred. |
| Receipt | Command key `(SessionId,generation,stage,ordinal)`; resource hash, record ID/serialized index, field/operation offset, owner/participant identity, expected/result versions, Applied/Transferred/Placed/Rejected/Indeterminate, applied prefix and recovery reference. No raw expression, text, file path or arbitrary exception message. |
| Offers | Immutable ordered ReplyId, serialized index, Direct target identity and opaque token binding session, source/resource generation, participants/attachment, offer generation, state version vector and current node. Resource/DLG ID alone cannot identify a session. |

P phases: Prepared → Entering → AwaitingSelection → Selecting → AtFrontier;
static errors have no external calls, runtime errors go to Failed with receipt.
Preparation is pure and validates participants, source identity/versions,
capabilities, arity/lexical forms/domains, budgets, grouping equivalence and every
Direct target's positive NPC role before mutation. Dynamic versions and lookup
results are checked under the host lease before each command; dependent versions
are refreshed after each Applied command and before offers. No asynchronous state
mixing is accepted. Snapshot drift during a query terminates Unknown/Stale as
appropriate; it is not a false gate.

P token behavior: stale resource/participant/owner epoch or revision invalidates
old offers; return StaleSelection with no commands and rebuild offers under a new
generation only if the same entry effects already completed and context is still
valid. A different entry/context requires explicit new invocation admission.
A valid selected token is consumed **before** its first command; duplicate calls
are AlreadyConsumed/WrongPhase, never repeated RE/IN. Single-threaded serialization
is required, not a security capability or concurrent exactly-once proof. Refresh
never repeats entry effects. Generation/ordinal/revision overflow fails closed.

P failures: Rejected guarantees the attempted command itself had no effects;
Applied prefix remains. Indeterminate (exception, removal followed by failed
placement, unknown callback outcome) freezes the session. Query failure after
entry also preserves entry prefix. Stop/recover by inspecting authoritative
owners and receipts; a fresh invocation needs reconciliation/idempotency admission
and cannot blindly replay the prefix. Neither a new generation nor restart
supplies rollback or makes seven commands atomic. An adapter unable to determine
partial substeps returns Indeterminate with a recovery reference.

P hard budgets (caller may only lower): structural32, eligible5, DLG records10000,
DLG input524288 bytes, semantic field4096 bytes/32 operations, immediate step65536
semantic bytes/128 operations, invocation depth1 and Direct edges1. Count each
(record index, semantic field kind) once; shared targets deduplicate their static
byte/operation cost but each actual selected command still consumes an ordinal.
Bound external query calls128 and command/substep calls128 separately, including
version/lookup/parent/fallback calls; a single compound inventory command is not
an unlimited loop. Inventory candidates4096 per participating inventory and
owners4 (NPC, substitute, PC, placement container); exceeding those initial
negative-IN limits is a frontier. Host supplies bounded synchronous ports and
cooperative cancellation checks before/after calls; no wall-clock interruption
of a blocked callback is promised. Pending command cancellation is Indeterminate
unless no-effect refusal is proven. Proposed values are budgets, not original constants.

Callbacks/return-to-SCR/writeback, Exit, Special/generated options, automatic first
speech, full conversations, save/patch resource precedence, original text/media,
physical input/UI and actual game-world state remain explicit future frontiers.
Current runtime may offer Exit/Special but executes neither; an unsupported
selection has no commands. No Godot/text resolver is smuggled into gameplay ports.

## Synthetic conformance matrix and independent oracles

All following identities and operand literals are invented. Common binding B:
worldW/moduleM/map5000, NPC=N/prototypeP, PC=C, optional substitute=S, extra absent,
attachmentA/event9, SCR sourceSX/DLG sourceDX generation1; sessionJ, offer generation1,
owner epochsG/I/R/Q/U/A all fixed nonempty and revisions0 unless stated. All
commands carry B and provenance. **Commands** in expected traces below do not
include pure queries; queries are named separately. An empty trace means no
command invocation, not a fabricated success. Source-backed expected algebra is
independent of the proposed seam names; P error/token/cap expectations are
explicit policy invariants, not claimed source behavior.

T2 (synthetic entry): `GF 3 1; GF 4 0; GF 5 1; IN -801; GF 6 1; QU 77 2; RU 1007`.
N initially owns nonstacking I1(name801,quantity1), C owns none; PC locationL;
flags3/4/5/6 initially0/1/0/0 and flag8=0, quest77=0, rumor1007=false, effective N→C reaction50
with modifiers/hostility/mind-control absent. Six structural replies are defined
by the tuple table; every Direct target is synthetic NPC900 with `GF 8 1` action.
Selected201 has `RE 35`; others have empty actions. Target's next group is excluded.
For the synthetic oracle only, every successful write advances its owner revision
once, including equal-value writes; transfer advances N and C inventory revisions
once each. Successful T2 entry's vector is `(G4,N1,C1,R0,Q1,U1,A0,Location0,Stats0)`;
after selected201 and target entry it is `(G5,N1,C1,R1,Q1,U1,A0,Location0,Stats0)`.
All epochs/source generation remain fixed. Production adapters return actual
owner revisions rather than pretending these test increments are an original rule.

| Reply | Gender selector | IQ signed gate | RE signed threshold |
| --- | ---: | ---: | ---: |
| 201 | -1 (any) | 5 | 40 |
| 202 | -1 | 5 | -39 |
| 203 | -1 | -4 | 40 |
| 204 | -1 | -4 | -39 |
| 205 | 1 | 5 | 40 |
| 206 | 0 | -4 | 40 |

| ID / evidence category | Exact input/state deviation from B/T2 | Independent expected output/state and command trace | Future production check |
| --- | --- | --- | --- |
| C01 S/P (E4) | Isolated entry `GF 3 1`; flag3=0; two otherwise-pass replies301 GF3=1,302 GF3=0 | Before entry [302], after [301]; flag3=1; trace `[GF(3,1)]`; eligibility query AFTER write | Actual GF adapter + parser/Start, post-effect query-order assertion |
| C02 S/P (E4) | Isolated `GF 3 1; GF 3 0`, initial0, same gates | flag3=0, [302]; trace BOTH writes, not coalesced/toggle | Receipt/ordering and repeated-write tests |
| C03 S/P (E5) | Isolated `IN -801`, N list I1(name801,q1),I2(name801,q1); C empty; transfer accepted | I1 parentC, I2 staysN; trace `IN(I1,N,C)`; no quantity request or fabricated item | Actual lookup order/concrete identity/transfer service |
| C04 S/P (E5) | N lacks801; eligible S owns I1; N no leader/distance10; C accepts | Lookup N then S; I1 parentC; trace `IN(I1,S,C)` | Conditional substitute + versioned source resolution |
| C05 S/P (E5) | N owns I1; C transfer refuses, parent remainsN; placement succeeds at L in containerK | I1 final parentK/locationL; receipt Applied/Placed; trace `IN[I1:transferC,parentN,remove,place(L,K)]` | Compound receipt, real placement, no mistaken Rejected |
| C06 P divergence (E5) | N/S known absent801; isolated IN | MissingItem/Failed, no mutation; attempted IN Rejected; no later commands. S would continue; owner approval needed | Negative-IN missing-item policy test, distinct from Unknown |
| C07 S/P (E5) | N has leader, S has801 but N lacks it; OR substitute Unknown | Leader case never searches S, MissingItem; Unknown → LookupUnknown, no mutation; no default item. Positive IN and stacks remain frontier | Role/substitute availability and Unknown tests |
| C08 S (E6) | Only RE gates40/-40, no entry; R39,40,41 respectively | Eligible upper/lower tests `(false,true)`, `(true,true)`, `(true,false)`; command trace[] | Production inclusive-threshold query tests at literal boundaries |
| C09 S/P (E3..7) | Full T2, gender0/IQ7/R50; select201 current token | Offers[201]; flags3/4/5/6=1/0/1/1, I1 parentC, quest77=2, rumor1007=true; after selection R35, flag8=1. Trace `[GF3=1,GF4=0,GF5=1,IN(I1,N,C),GF6=1,QU(C,77,2,N),RU(C,1007),RE(N,C,target35),GF8=1]`; AtFrontier900; no target reply query | Wire DLG reader/parser + real bounded ports, all nine ordinals/provenance |
| C10 S/P (E6) | Isolated RE action, neutral profile R50: unsigned35, +5, -5, +0, -0 in separate sessions | R35,55,45,50,50 respectively; target vs delta command kinds retained. Modifier-enabled profile: exact final R is Unknown, not35 | Distinct lexical forms and reaction adapter dependencies |
| C11 S (E8/E11 algebra) | Six T2 rows, gender0/IQ7/R20; gender0/IQ3/R50; gender0/IQ3/R20 | [202]; [203,206]; [204]; no selection command, no structural truncation | Actual filtered six-row admission, not max-cap-only test |
| C12 S (E8) | Six T2 rows, gender1/IQ7/R50; gender1/IQ3/R50; dumb true/IQ7, LOW_INTELLIGENCE4 | [201,205]; [203]; dumb effectiveIQ1 gives[203] at gender1/R50 | Caller facts/domain and gender→IQ→RE order tests |
| C13 P vs S (E8/E10) | Six synthetic rows201..206 all gender-any/IQ>=1/RE>=0, R50/IQ7; successful T2 entry | EligibleOverflow/Failed, no offers; ALL seven entry commands retained. P rejects, S first5. Not structural rejection or first5 truncation | Overflow after post-effects enumeration and prefix recovery |
| C14 P (E10 context) | Isolated empty-action entry with otherwise-pass201; AwaitingSelection J/gen1, all owner revisions0; external reaction revision1 (value still50) or source generation2; old token supplied | StaleSelection, trace[]; revision-only refresh gen2 without entry replay; changed source requires context readmission | Full vector/session/resource/participant token binding |
| C15 P (E9) | Admission action `GF 3 2`, `IN 0`, `RE >35`, `ZZ 1`, or non-ASCII; hidden reply has unsupported action | Explicit InvalidOperand/UnsupportedForm frontier; ZERO queries/commands including source entry. No repair/no-op | Static-before-mutation, hidden/unselected path validation |
| C16 P (E5/E10) | T2 IN attempted at ordinal4, adapter Rejected/no effect | flags3/4/5=1/0/1; flag6=0, I1 staysN, quest77=0, rumorfalse. Applied prefix first3 GF; IN Rejected, no GF5/QU/RU/offers. Recovery never replays first3 | Actual rejected-prefix and terminal duplicate-call tests |
| C17 P (E7/E10) | T2 RU ordinal7 throws/unknown effect after QU Applied | Six-command prefix remains: flags1/0/1/1,I1 parentC,quest2; rumor Indeterminate (not false). Stop, no offers/PlayerRE/target. Reconciliation required | Exception/indeterminate receipt and no rollback tests |
| C18 S/P (E7) | AttachmentA flag2=0, SCR initial header flag2=1; LF2=0; then write LF2=1 | Read0 from A, not1 from header; write/readA=1; unrelated DLG resource/header unchanged; attachment revision advances | Bind existing LF port to effective SAP owner and version |
| C19 P (E10 context) | C09 completed; select SAME token201 again or token from other sessionJ2 | AlreadyConsumed/WrongPhase or NotOffered respectively, zero NEW commands; R remains35, I1 not transferred twice | Replay/session-identity protection before first command |
| C20 P (E8/E10) | Serialized group [206,201] differs sorted [201,206]; OR structural33; field4097; ops33; step129; queries129 | GroupOrderFrontier/Budget before ports for static violations; query-budget failure retains prior entry prefix; no truncated successful slice | Independent boundary fixtures through production reader/plan |
| C21 S/P (E5) | Negative IN: transfer refuses, removal succeeds, placement fails/unknown | I1 removed/placement unresolved, command Indeterminate (NOT Rejected), failed session; no subsequent T2 commands | Inventory internal partial-effect and recovery oracle |
| C22 P frontier (E8/E10) | Zero eligible after successful entry; Exit0 or Special-2 selected in isolated valid offer | EmptyEligibleFrontier with seven-command entry receipt; Exit/Special selection UnsupportedTransition and no selection commands; no generic fallback/callback | Bounded terminal/frontier tests, no full-conversation claim |

The helper self-test executes threshold/bit/name-lookup/trace **worksheets** with
literal independent expected values (including C08..C13 algebra). It does not
execute proposed ports, tokens, inventory side effects or the future runtime.
C14..C22 are reproducible typed input/expected-state specifications for future
production tests, not claimed implemented tests. Source comparisons for C03..C07
establish selection/fallback semantics, not actual inventory execution. This
separation prevents a simplified simulator from being mistaken for the missing
production services or an independent original-runtime oracle.

## Claim → evidence → scenario → future seam ledger

| Claim | Evidence / category | Scenario or verification route | Future seam/test; disposition |
| --- | --- | --- | --- |
| L1 Exact identities and conditional entry | E1/E2 D/S; current source E10 | E11 current hash/terminal check; inherited object evidence E1 | Entry-to-state context binding; verified bounded identity, no live-state claim |
| L2 GF arity/owner/read/write | E4/E9 S, E11 D selected shapes | C01,C02,C15,C16 | Global story service + strict parser; missing production |
| L3 IN signed direction/lookup/instance/fallback | E5 S (transfer wrapper/GET (0x4617F0, 0x461810), parented TRANSFER (0x461CA0), existing/new pile placement (0x466E50, 0x466EF0)), E11 D negative selected form | C03..C07,C21; U3 for quantity/callbacks | Inventory service/receipt; negative-only proposal, positive/stack frontier |
| L4 RE threshold vs target vs relative | E6 S, E11 D unsigned selected action | C08,C09,C10,C15; U1/U2 | Effective reaction query and explicit target/delta adapter; missing production |
| L5 Seven effects, post-effects gates, Player-before-target | E3/E7 S, E11 D | T1/C09/C16/C17 | Source/selection/target staging and independent ordered receipt test |
| L6 QU/RU/LF ownership, not working services | E7 S/E10 project source | C09,C17,C18 | Quest/rumor/attachment adapters with dependency versions; missing |
| L7 Serialized/numeric equivalence and filtered cap | E8 S/E10 current code/E11 D worksheet | C11..C13,C20,C22; U4 | Owner acceptance of selected-equivalent-group-v1, independent group/filter/budget tests |
| L8 Typed context/version/query/command/token contracts | E10 existing narrow policy and entry version seams; new P not an original guarantee | C14,C19,C20,C21 | Versioned snapshot/receipt/token integration; proposed only |
| L9 Validation/applied-prefix/stop/recovery | E10 current project semantics, E5 source non-atomic sequence; P extension | C15..C17,C21 | No-effect rejection versus indeterminate compound command, terminal recovery; proposed |
| L10 Bounds and excluded callback/Exit/Special/full traversal | E10 current limits/frontiers; new P budgets | C20,C22; U3/U5 | Bounded plan/host budget tests; production excluded |
| L11 Evidence/licensing/privacy and unknown parity | E1 method, cited addresses, E11 read-only fingerprints | Address/privacy checks; U1..U6 | No payload export, no code translation, no original execution claim |

## Unknowns and bounded verification routes

| ID | Not established | Concrete next verification (not a blocker for this document) |
| --- | --- | --- |
| U1 | Original parser grammar, `>`/`<` intention, all GF/IN/RE forms/domains | Inspect version-matched editor documentation and additional bounded field shapes; trace original parser with synthetic author-created dialogue in a separately authorized executable study. Do not fix `atoi` semantics by intuition. |
| U2 | Original effective/stored reaction, modifiers/AI side effects and target outcome | Trace reaction service with named race/effects/leadership profiles and effective+base before/after values; use independent literal adapter tests. Exact vanilla compatibility needs original executable evidence; ledger rows R1–R8 record the reaction functions checked so far. |
| U3 | Original quantity/type/prototype/live-parent joins, live substitute inventory and actual stack/equipment/placement/decay/callback outcomes | [ARC-RES-021](negative-inventory-transfer-contract.md) refines the source-only insertion/remove/stack/SAP_TRANSFER/placement trace and gives bounded next checks plus a proposed typed matrix. Static/editor data and the F0 function trace do not prove live ownership or execution; refuse unsupported dependencies. Quantity1/MissingItem remain owner-decision proposals before any adapter. |
| U4 | Original visible reply parity, generic/generated fallback, max5 short circuit under state changes | Run authorized original sessions with named PC gender/IQ/dumb and reaction profile, record identifier-only offered sets/order before/after. Current integer worksheet is not a GUI observation. |
| U5 | Session concurrency/persistence, callback/Exit/Special closure, full traversal, rollback | Separate dependency-ledger design and failure injection on real services; original callback/return study; no transaction guarantee inferred. |
| U6 | Exact owner acceptance of safety subset/domains/caps/divergences | Owner decision on the explicit P recommendations before implementation; retain Unknown/proposal if declined or undecided. No production cards are created by this research. |

## Dependency-aware future implementation boundaries (document only)

1. Owner accepts/refines the bounded P policy and missing-state dependency profiles.
   This is prerequisite, not an automatically dispatched product milestone.
2. Versioned headless state adapters: global flags; then negative item lookup/
   transfer/placement with substep receipts; reaction effective-query/target
   adapter; quest/rumor/attachment adapters. Independently test concrete owners
   and refuse unevidenced callbacks/stacks. No Godot/text API here.
3. Strict DLG semantic plan and static validation for accepted forms/groups/
   targets/budgets, on the production DlgReader and resolved-entry identities.
   Depends on (1); commands require (2), not mock success or opcode skipping.
4. Ordered entry → eligibility → token → selected Direct → target-entry bridge.
   Depends on (2)/(3), source/owner versions and complete C01..C22 production
   oracles. Stop before target offers. No API change is implemented in this PR.
5. Later separate scopes may address real-state/world invocation, callbacks,
   positive IN/relative RE, original gameplay parity, text/media/UI and full
   conversations. UI/export/device evidence cannot establish missing state semantics.

## Headless reproduction and acceptance boundaries

Use Python3.10+ and existing canonical tools from [toolchain](../workflow/toolchain.md).
No GUI, original executable, export or device check is an AC here; CI_NOT_REQUIRED.

```sh
python3 -B tools/research/dat_probe.py --self-test
python3 -B tools/research/dialog_state_probe.py --self-test
python3 -B tools/research/dialog_state_probe.py --scripts "$LOCAL_SCRIPTS" --dialogs "$LOCAL_DIALOGS"
git diff --check
```

Original mode reads existing explicitly selected archives, checks full input
hashes before/after, resolves numeric1324 and exact derived DLG uniqueness, checks
bounded framing and selected shape/order/predicate algebra, prints only hashes,
identifiers and categorical shape/results. It writes no extracted resource, text
or operands. File hashes use the existing Python3.10-compatible streaming DAT
hash helper; self-test exercises empty-file and `abc` known-answer hashes through
that same path. DAT helper lineage is shared for archive framing; the independent
DLG worksheet is not independent source lineage or a new production parser.
Wrong hash/missing input/unknown shape/failed worksheet is FAIL, never parity PASS.
Private run evidence carries task/run/session/workspace/base/head/UTC/commands/
results/digests; logs and work diaries do not belong in this public document.

Document acceptance checks cited addresses against the functions read, selected
identities/shapes/order, table/form consistency, synthetic literal expectations,
helper tests and diff/privacy; it does not accept the proposed gameplay policy.
Independent exact-head review must recheck the **whole** contract including these
frontiers, not just spelling or helper exit status. Later private PR publication
is separate from review; NO_MERGE remains in force for this task's delivery.

