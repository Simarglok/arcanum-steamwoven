# Negative IN: concrete inventory transfer research contract

## Status, scope and evidence discipline

ARC-RES-021 refines [RES-019](dialog-state-contract.md)'s E5, U3 and
C03..C07/C16/C21. This is a **document-only proposal**, not an inventory service,
DLG adapter, parser change or working conversation. No `src/**` inputs change.
Only a lexically negative, numerically nonzero DLG `IN` action is considered.
IN conditions, `-0`, zero, positive/follower/destroy forms, general inventory,
equipment/crafting implementations and a full SCR VM remain outside admission.

Categories:

- **Source (S):** a reading of the original's functions at the cited addresses,
  evidence level F0 (not checked in the executable) unless the
  [verification ledger](../research/original-verification-ledger.md) lists them;
  not original executable parity.
- **Data-observed (D):** authenticated inherited selected static evidence only;
  no claim of normal-game live ownership, quantity or callback execution.
- **Proposal (P):** independently authored safety/API/test recommendations.
  Research review or merge does not accept them as gameplay policy.
- **Unknown (U / NOT_VERIFIED):** a named gap with a bounded next verification.

The prose and invented scenarios are independently authored project material.
Original content, selected identifiers, fingerprints, aggregate shapes, operands,
private paths and local work diaries are not reproduced here. The inherited
RES-019 evidence remains in its existing document; no new data ledger is published.

## Source index: exact branch, not inferred association

Addresses name the relevant caller/callee.

| Key | Functions read (address) | Claim boundary |
| --- | --- | --- |
| E01 | Action scan (0x415BA0), negative IN (0x415BA0) | Permissive `atoi` scan, negative branch, primary/substitute lookup, transfer, parent check, removal and PC-location placement; absence continues. |
| E02 | Name lookup (0x462480) | Container/critter list index order and first `OBJ_F_NAME` match; wrong type/null and exhausted list all return null (F0). |
| E03 | Substitute gate (0x45F650), distance caller (0x441AE0), distance metric (0x4B96F0) | NPC only, no leader, non-null substitute, distance <=20; maximum absolute coordinate difference, not Euclidean/metre radius. |
| E04 | Parent query (0x4612A0), wrapper and GET (0x4617F0, 0x461810), parented TRANSFER (0x461CA0) | Inventory flag/type-sensitive parent query; default slot -1; different script participants; boolean is not verified ownership. |
| E05 | Insertion check (0x466510), free slot (0x4664C0) | Weight/background/key-ring/stack/room branches; default transfer chooses insertion location, not an explicit wear slot. |
| E06 | Stack field classifier (0x462410), prototype match (0x462930), insertion (0x466640), key special cases (0x466A00, 0x466A50, 0x466AA0) | Gold/ammo quantity fields; first matching prototype excluding self and generic-item exception; merge can destroy incoming object; nonstack insertion appends; keys/rings are additional destruction/aggregation branches. |
| E07 | Remove wrapper (0x466E00), forced removal (0x467860), equip (0x4677B0), unequip (0x467CB0) | List compaction, slot/stack/encumbrance/AI/magic-tech effects and ignored callback results; removal is not a no-effect validation. |
| E08 | Placement (0x466E50, 0x466EF0), ground drop (0x4415C0) | Existing/new junk pile or ground; gathering other items; no typed success receipt, destroyed-object drop early return. |
| E09 | Decay eligibility, scheduling and cancellation (0x467FC0, 0x468010, 0x468090, 0x468110, 0x468150) | Conditional schedule with delay172800000 at transfer/placement callers, not guaranteed destruction or successful scheduler write. |
| E10 | Script dispatch/writeback (0x441980) | Triggerer/attachee/extra, editor bypass, absent attachment success, execution and attachment writeback; return false need not mean no effects. |
| E11 | [RES-019 E5 / IN trace](dialog-state-contract.md#in-identity-quantity-transfer-and-refusal), [existing worksheet](../../tools/research/dialog_state_probe.py) | Reuse selected static negative-form evidence; quantity and live inventory are not inferred from that operand or display. |

## Literal source trace and identity/quantity distinctions

### 1. Lookup priority and substitute gate (S: E01..E03)

The negative branch negates the numeric value and looks up that **item name** in
the NPC's primary inventory first. The name lookup (0x462480) chooses count/list fields
by owner type, scans indices0 through count-1 and returns the first name match.
It does not sort IDs, choose by quantity/prototype/slot, aggregate all matches,
create an item, or assert current-parent consistency. Even a worn item can be
selected: this lookup has no slot filter.

Only after primary null does the action call the substitute lookup (0x45F650).
Its gates are: non-null NPC, type NPC, no leader (any leader, not merely a PC),
non-null substitute handle, object distance (0x441AE0) from NPC to substitute
<= 20. The object distance uses locations; the location distance (0x4B96F0)
returns max(abs(dx),abs(dy)). Thus a synthetic
coordinate delta(20,20) qualifies, (21,0) does not. Exact equality20 qualifies.
The substitute is a separate source owner, not triggerer/extra or automatically
a follower. Its type and list still have to be suitable for the lookup.
If primary and every applicable substitute lookup return null, no transfer or
fallback is called; the action sequence continues (F0). The null result also
conflates unsupported owner type with absence; a proposed typed service must not do so.

A primary hit short-circuits substitute resolution entirely. Do not query an
unneeded substitute then fail a valid primary hit on its unavailable state.
First match is authoritative for this trace: do not skip a stack/worn/unsupported
first match to reach a convenient second object. Unsupported first match is a
capability frontier under P, not permission to change S ordering.

### 2. Name is not prototype, instance, source or result (S/U/P)

Keep these fields distinct in any future resolution:

| Identity/fact | Meaning and required evidence |
| --- | --- |
| Lexical operand | Original sign + numeric magnitude; not a quantity argument. P preserves sign and excludes numeric zero, overflow and unsupported grammar before ports. |
| ItemNameId | Compared to `OBJ_F_NAME`; not a description ID or prototype key. |
| PrototypeId / resolved type | Definition association requires an explicit instance-to-prototype join, not a filename/name equality. Stack field classification depends on type. Unknown join/type must stay Unknown. |
| ItemInstanceId | Exact first selected object; independent of name collisions and prototype equality. |
| SourceInventoryId + list index | Primary N or eligible substitute S, with source list version; not necessarily current parent. |
| CurrentParent + existence | Observed item flag/type/parent and owner-list membership; callbacks may move or destroy the selected object. |
| Quantity before/after | Type/profile-specific quantity or explicitly verified nonstacking unit. No general quantity field can be read from the name operand. |
| Concrete result | Same-instance ownership, placement, merged surviving instance, destruction or unresolved effects; boolean transfer success alone is insufficient. |

S IN has no quantity operand and does not explicitly split/decrement the selected
object. E06's stack classifier identifies gold and ammo, not every same-name
item. When a qualifying matching **prototype** exists, insertion adds the whole
incoming quantity to the first matched existing object, invokes insertion script
and requests destruction of the incoming object. Prototype matching excludes
self and has a generic-item exception. Equal names do not imply equal prototypes.
No arithmetic overflow/quantity correctness guarantee is established by that
addition. Keys/key rings also have special aggregation/destruction paths.

U the selected original name-to-instance/prototype/type/stack/quantity join,
live list order, substitute existence and live parent are **NOT_VERIFIED**.
No actual original item is asserted to be nonstacking or quantity1. P's initial
`nonstacking-unit-v1` is a proposed capability restriction only, not a source-derived
quantity rule and not accepted owner policy. It requires a positively evidenced
unit/type profile, excludes keys/rings, merge/destruction, wear/hotkey slots and
unknown magic-tech/callback dependencies; it is not satisfied by missing data.

### 3. Wrapper, callbacks and removal/insertion (S: E04..E07/E10)

The transfer wrapper (0x4617F0) calls the extended transfer (0x461810) with
inventory location -1. On the local/host branch, an item considered parented
dispatches to the parented transfer (0x461CA0):

- SAP_TRANSFER: triggerer=current parent, attachee=recipient PC, extra=item,
  line0. This is the **recipient's** effective attachment, not the item's.
- If it returns false, the helper returns false before its own removal. But
  the callback may already have changed owners or attachment state.
- After insertion checks, the remove wrapper (0x466E00) precedes insertion into the chosen slot.
  It then calls decay cancellation and conditional decay scheduling.

If the item is considered unparented, the other branch runs SAP_GET with
triggerer=recipient PC, attachee=item, extra=null, line0. It checks insertion,
performs world pickup, inserts, cancels decay and returns true. A selected
inventory-list handle should normally be parented; inconsistent/unparented
resolution must be reconciled, not silently declared the happy path. The wrapper
also has a network non-host branch that sends a packet and returns true: that is
not a local completed transfer. P excludes networking/remote mutation.

Script dispatch (0x441980) returns true in editor mode or for an absent attachment;
normal execution can write back script number/flags/counters. Neither editor
success nor false callback return proves a normal-game no-effect receipt.
Substep callbacks have different roles:

| Event | Triggerer | Attachee | Extra | Source point |
| --- | --- | --- | --- | --- |
| SAP_TRANSFER | Current source parent | PC recipient | Selected item | Before insertion check/removal |
| SAP_GET | PC recipient | Selected item | null | Unparented branch before insertion check/pickup |
| SAP_REMOVE_ITEM | Source parent | Selected item | null | After forced-removal effects; return ignored |
| SAP_INSERT_ITEM | Destination parent | Selected item | null | After normal insertion effects, or before incoming stack destruction; return ignored |
| SAP_WIELD_OFF / SAP_WIELD_ON | Parent | Selected item | null | Unequip/equip branch, results ignored |

Forced removal demaintains spells **before** looking for a matching list member;
for critters it clears the classified stack handle, compacts the list left,
decrements count, usually sets item inventory location to -1, unequips or clears
PC hotkey state, notifies AI/magic-tech, recalculates encumbrance, may request NPC
wield checking and invokes removal script. It can warn/return after effects.
The negative-IN fallback calls the remove wrapper (0x466E00) directly, not the
drop function's (0x461AB0) SAP_DROP/no-drop/equipment validation. Do not claim those drop checks guard it.

Normal insertion appends the object, writes slot and parent, notifies pickup,
possibly equips, recalculates encumbrance, sets NPC wield-check flag, updates
inventory/UI/AI and invokes insertion script. Keys/rings and stack merges differ.
The raw parent field and OF_INVENTORY flag are not uniformly cleared by forced
removal itself; ground drop (0x4415C0) clears the inventory flag and writes location.
A future service must verify authoritative membership, existence, parent and
slot, not synthesize an unparented fact merely from a removal call.

These are effects/dependencies, not implementations authorized here. P may
support only verified absent relevant attachments and a bounded side-effect
profile. Unknown attachments are not empty scripts. An adapter unable to account
for dependent owner changes must refuse capability before effects, or return
Indeterminate if effects have already begun.

### 4. Post-parent check, placement and decay (S: E01/E08/E09)

The action ignores the transfer boolean, queries the **same selected handle**'s
parent, and compares it to PC. It does not verify quantity, surviving identity,
owner lists, callback outcomes or boolean success. The parent query (0x4612A0) first checks
item type and OF_INVENTORY; invalid/non-item can return before initializing its
output. A stack merge/destruction can invalidate the selected handle: neither
successful parent verification nor safe fallback on a destroyed handle is proven.
This is a limitation of the original (F0), not a reason to repair its behavior in the trace.

If queried parent differs from PC, the action calls removal again, then placement at the
PC's current location. It does not check a typed removal/placement result and does
not roll back. Placement tries junk piles in location enumeration order with a
free insertion slot. If none works, it enumerates ground items and may create a
new junk pile while collecting them via transfer calls; it then tries inserting
the selected object there. Without a successful container path it calls ground
drop. Ground drop returns early if the object is destroyed. Other items and newly
created container are additional owners/effects, not an isolated two-owner move.
Fallback is therefore **container-or-ground placement**, not always a bare drop
and not a guaranteed successful transfer or preservation of all quantities.

Placement calls `item_decay(obj, 172800000)`; parented transfer also attempts that
delay after cancellation, whereas GET only cancels. Eligibility excludes null,
editor mode, no-decay items and ordinary parented objects; ground or junk-pile
items can qualify. Scheduling returns a boolean ignored by these callers; actual
destruction additionally depends on the later event and decay enable state.
Do not promise that every placed item decays, that the scheduler succeeded, or
that this source constant defines the project's time policy.

## Typed document-only service seam (P, not C# declarations)

Proposed vocabulary; no production service or inventory runtime exists in this
change. Host serialization/lease over all participating owners is a prerequisite,
not concurrent exactly-once or an atomic multiowner transaction.

### Admission and lookup

`NegativeInventoryGiveRequest` binds world/module/map, session + source generation,
command key(stage, ordinal), DLG resource identity/record/operation offset,
NPC instance, PC identity, negative lexical form, ItemNameId, capability profile
and placement policy. Expected owner versions are a vector of
`(OwnerId, nonempty Epoch, Revision)` for NPC inventory/leader/location, any
actually needed substitute, PC inventory/location, item and dependent attachments.
The source resource generation is distinct from owner versions. Foreign owner,
epoch mismatch, revision drift or overflowing generation/revision is not an
implicit refresh. Validate roles, grammar, capabilities and budgets before calls;
recheck dynamic versions under the host lease before effects.

P strict decimal magnitude1..2147483647 with an explicit minus sign; Int32 minimum
negation, numeric zero including `-0`, positive/unsigned, extra operands and
non-ASCII are rejected/frontiers with **zero port calls**. This is deliberately
stricter than the original's scan. No supported IN condition follows from action support.

`LookupReply` echoes request identity, exact owner/version and either:

- `Found(InstanceId, ItemNameId, PrototypeId/type evidence, SourceInventoryId,
  source index, CurrentParent evidence, quantity/profile, dependency versions)`;
- `KnownAbsent`: complete valid admitted list searched, all applicable source
  gates known, no match; primary absence alone is not whole-command absence;
- `Unknown`: incomplete list/type/join/parent/quantity/gate knowledge;
- `Unavailable`: owner/dependency cannot be accessed under this capability;
- `Failed`: owner reports a specific failed read (sanitized categorical code);
- `Stale`: request owner/epoch/revision/context does not match the observed view.

Only Found with coherent instance/source/parent/versions may reach effectful
ports. Non-NPC/wrong-type source is an admission/frontier error, not KnownAbsent.
Substitute gates that are known false are recorded as skipped; Unknown gates on
an otherwise missing primary are not false or absence. A primary hit does not
require substitute versions. Never resolve by display text or a guessed prototype.

### Execution result and receipt

| Result | Required positive evidence | Failure boundary |
| --- | --- | --- |
| `Transferred` | Selected instance alive, owned/member of PC inventory with agreed slot, removed from source list, supported quantity conserved, coherent result versions and complete substep/dependency receipts | Not just helper true or parent field; merging/destroying profiles outside initial capability |
| `Placed` | Concrete verified container membership or ground existence/location at bound PC location, selected supported quantity conserved, source membership removed; placement/container/decay receipt distinguishes Scheduled, NotEligible, Failed or Unknown | Refused transfer plus verified supported placement can be Applied/Placed; unavailable fallback is never mock success |
| `Rejected(no effect)` | Attempted command performed no effects, including callbacks/dependencies; sanitized reason + checked versions | Prior dialog command prefix remains; false return alone cannot establish this |
| `Indeterminate` | Partial effects or uncertainty about any executed substep, exact known prefix/facts and authoritative recovery reference | Stop conversation; no replay, rollback or fabricated restored parent |

Receipt binds command/request/provenance, resolved source/index/instance/name/
prototype/type profile, before/after parent/membership/location/quantity/slot,
expected/result versions **for each affected owner**, and ordered substeps:
lookup/gate, parent validation, callback, insertion check, remove, insert or
fallback remove/place, verification, decay/cancel. Each substep reports
NotCalled / VerifiedNoEffect / Applied / Failed / Unknown, its dependency effects
and version changes. Unobserved after versions stay Unknown rather than copying
expected versions; Indeterminate retains last-known versions and reconciliation
reference. The synthetic test version increments below are not original rules.

Static unsupported profile → capability frontier before any port calls. Dynamically
revealed unsupported selected item → lookup-only Rejected if all reads are proven
no-effect; if a query/callback has effects, Indeterminate instead. A fallback
capability must be predeclared; if execution nevertheless removes then cannot
place, return Indeterminate, not a retroactive pre-effect rejection.
For the initial recommendation refuse stacks, keys/rings, wear/hotkey slots,
unaccounted magic-tech/AI callbacks, network mutation and unknown placement/decay
owners. A future expanded adapter must evidence actual effects and receipts first.

P budgets inherit RES-019's ceilings: 4096 candidates per participating inventory,
owners4, queries128 and command/substep calls128, separately accounted. A scan
that exceeds budget is not KnownAbsent. Placement gathering additional owners
beyond capability/budget is a frontier, not permission to drop accounting.
Callbacks must be bounded by the owner; cancellation/exception after effects is
Indeterminate unless authoritative no-effect evidence exists. No timeout can
interrupt an arbitrary blocked callback or provide rollback.

### Missing-item decision remains owner-owned

S continues subsequent DLG actions on absent item. RES-019's recommended
`MissingItem` rejection intentionally differs. Three alternatives remain open:

1. **Recommendation:** safety profile rejects proven absence, stops later commands
   and retains applied dialog prefix; require explicit owner acceptance.
2. Source-compatible continuation with an explicit no-transfer receipt and later
   commands allowed; requires a separately accepted outcome/control-flow policy.
3. Capability frontier/defer action while missing-item behavior is undecided.

This research chooses no gameplay policy for the owner. Unknown/Unavailable/
Failed/Stale must not be treated as the source-compatible proven-absence case.
Quantity1 profile, supported callback/dependency set, placement/decay policy,
budgets and stale/recovery admission likewise need accepted decisions before
production implementation. No additional implementation tasks are dispatched here.

## Independent synthetic conformance matrix

These are invented, literal future-test vectors and independently hand-traced
expected results, **not executed inventory tests**. No new worksheet/model is
shipped. Static scenario review checks them against the source trace and P
invariants; existing RES-019 worksheet only authenticates its own narrower algebra.
Neither that worksheet nor this table is an independent executable oracle of the original.

Common binding B: N=NPC, C=PC, S=optional substitute, location L=(0,0), name801,
instance I1 of prototype P1, alive nonstacking unit with q1, primary list[I1],
parent N, C list[], non-wear slot0, all relevant attachments/dependencies verified
absent or supported. Owner epochs fixed, revisions0, generation1, stable coherent
lease, permitted verified placement dependency. Default acceptance changes source
and PC inventory revisions to1, I1 revision to1; unchanged facts remain revision0.
Those increments are synthetic oracle conventions, not production promises.
Notation T=attempt transfer, R=remove, V=verify, F=placement; query traces name
primary/gate/substitute/parent. P receipts are compared literally to actual future
adapters, not computed by sharing their helpers/constants.

| ID | Exact deviation/input | Literal independent expected trace/state/result | Evidence and future production seam |
| --- | --- | --- | --- |
| N01 | B, N list[I1(name801,P1,q1),I2(name801,P2,q1)], supported transfer | primary selects I1 index0; T(I1,N,C), R/insert/V; N list[I2], C list[I1], parentC,q1; Transferred; N1/C1/I1v1; I2 unchanged; no substitute query | E01/E02/E04; real first-match lookup + transfer |
| N02 | Same as N01 but reverse list[I2,I1] | select I2 index0 despite different prototype; I1 staysN; C list[I2]; Transferred; no ID/prototype sorting | E02; ordered source owner |
| N03 | N list[], no leader; S at(10,0), list[I1] parentS | queries primaryN,gateS,substituteS; T(I1,S,C); S1/C1/I1v1,N0; parentC,q1; Transferred | E03; actual substitute resolution |
| N04 | N miss, leader present, S list[I1] | primaryN,gate leader only; substitute list NOT read; I1 staysS; command-wide KnownAbsent if no applicable source; P MissingItem Rejected/no effect, versions0; S continues | E03; leader skip and absence policy |
| N05 | N miss, no leader; S delta(20,20) vs(21,0), separate sessions | (20,20): S searched, I1 transferred S→C; (21,0): S not searched, KnownAbsent/P MissingItem, no effects; equality20 included | E03 metric; distance boundary oracle |
| N06 | N miss, no leader, substitute known null | no substitute-list read; KnownAbsent; P MissingItem/no effects; S proceeds without transfer | E01/E03; no-substitute distinction |
| N07 | N complete admitted list[] and eligible S complete list[] | KnownAbsent only after both lookups; no T/R/F; P MissingItem Rejected; S later action continues | E01; deliberate divergence |
| N08 | N list incomplete, OR N miss with leader/distance/substitute Unknown | LookupUnknown, no transfer; never KnownAbsent or default false gate; versions last-known, no invented I1 | P typed lookup; U static/live gaps |
| N09 | N owner Unavailable; independent Failed-read and Stale-read variants | Unavailable / Failed / Stale preserved respectively; no T/R/F; proven no-effect reads permit Rejected; no missing-item continuation | P query status adapter |
| N10 | B primary found; substitute Unavailable | do NOT query substitute; same Transferred result as B | E01; short-circuit dependency discovery |
| N11 | B transfer refuses, verified no callback effects; parent remainsN; fallback supported existing K atL | T(false),parentN,R,F(K),V; N list[], C list[], K list[I1],parentK,q1; Placed not Rejected; N1/K1/I1v1,C0; decay reported separately | E01/E08; real fallback/container receipt |
| N12 | As N11; no viable container or ground items; verified ground placement atL | T(false),parentN,R,F(ground),V; I1 alive unparented atL,q1; Placed; N1/I1v1,location owner1; no fictional K | E08; ground owner verification |
| N13 | SAP_TRANSFER returns false AFTER setting world bit7=1, item remainsN; bounded callbacks supported for this future extended profile; fallback verified | callback receipt records world revision1; T(false),parentN,R,F(K),V; Placed with extra owner version, not no-effect rejection; bit7 stays1 | E04/E10; extended callback dependency accounting |
| N14 | Callback returns true but destroys/moves I1; before/after not fully known | Indeterminate with callback prefix/recovery reference; no assumed parentC/q1; P does not blindly reuse handle or replay; S only runs its raw parent/fallback check | E04/E06/E10; liveness/dependency verification |
| N15 | T refuses, removal Applied, fallback Failed/Unknown; no authoritative final location | Indeterminate, source list removal known, I1 ownership/location unresolved; expected/result versions show known removal and Unknown after placement; no later DLG commands | E01/E08; compound partial-effect receipt |
| N16 | expected N(epochA,rev0), actual N(epochA,rev1) before effects | Stale, no T/R/F; no silent version refresh; item staysN | P owner-version gate |
| N17 | expected N(epochA,rev0), reply from S(epochA,rev0), OR N(epochB,rev0) | ForeignOwner / StaleEpoch, respectively; no effects despite matching revision number | P request/owner binding |
| N18 | B item in N list but current parentS or unparented, OR destroyed | ParentMismatch/Liveness frontier before mutation, no guessed sourceN transfer; reads only; if already effectful, Indeterminate | E02/E04; list-parent consistency |
| N19 | `IN -0`, `IN 0`, `IN +801`, `IN -2147483648`, extra operand or non-ASCII | UnsupportedForm/InvalidOperand before ANY port; zero queries/commands; no numeric/sign normalization into supported action | E01 scan vs P strict admission |
| N20 | B but selected I1 gold/ammo q3; C matching-prototype I9 q4 | P StackCapability frontier, no mutation; S insertion arithmetic would request I9 q7 and incoming I1 destruction, not transfer one unit or necessarily verify I1 parentC | E06; extended stack/survivor oracle, NOT observed original quantity |
| N21 | N list[I1 unsupported stack,I2 supported unit] both name801 | select I1 first; StackCapability frontier; never skip to I2; no transfer | E02/E06; first-match capability handling |
| N22 | I1 worn or hotkey slot; independent key/key-ring and magic-tech dependency variants | P Equipment/Key/Dependency frontier before effects; no pretend ordinary slot move; future source profile must record unequip/wield/remove/insert dependencies | E06/E07; actual side-effect adapter |
| N23 | Relevant callback attachment Unknown or nonempty unsupported | CallbackCapability frontier before effects; never treat unknown as absent/editor success; no simulated successful callback | E10; bounded attachment capability |
| N24 | Transfer true, parent fieldC but C list does not contain I1 / quantity Unknown | Indeterminate, not Transferred; field comparison alone insufficient; owner recovery required | E01/E04; verification stronger than the original's check |
| N25 | N candidate count4097, OR unknown placement may touch fifth owner | Budget/PlacementCapability frontier, no truncated lookup success or KnownAbsent; no effects under pure admission | P bounds/dependencies |
| N26 | Post-placement decay report NotEligible vs Failed vs Unknown, separate sessions | Preserve each scheduler status; do not say Scheduled. Verified placement plus unresolved required dependency becomes Indeterminate; optional NotEligible can be Placed only under accepted profile | E09; placement/decay owner-policy boundary |
| N27 | Transfer callback changed parent to C but booleanfalse; complete supported receipts/membership/q1 verified | Transferred if all facts verified; no fallback merely because booleanfalse; callback effects included | E01/E04; outcome not boolean |
| N28 | Prior dialog prefix GF3=1,GF4=0,GF5=1; IN pre-effect Rejected at ordinal4 | retain prefix; IN no-effect receipt; no GF6/QU/RU/offers; recovery never replays first3. If N15 instead, retain same prefix plus IN partial receipt and freeze | RES019 C16/C21; future conversation boundary |
| N29 | B current transfer returns true but result owner versions unavailable after insertion | Indeterminate with verified insertion substep and Unknown result vector; not copied expected vector or fabricated successful receipt | P receipt completeness |
| N30 | Stale source generation2 vs request1, OR foreign PC participant | StaleContext / ParticipantMismatch before effects; no transfer under earlier resource/context binding | P provenance/participant admission |

N01..N30 are requirements for future real ports, not evidence that those ports
exist. Initial capability intentionally rejects N13's expanded callback profile,
N20 stacking and N22 equipment/key dependencies; these rows distinguish S effects
from P admission instead of claiming every scenario is supported.

## Claim → source/observation → proposal → scenario → verification ledger

| Claim / acceptance | Source/Data/Unknown finding | Proposed production seam | Independent oracle and check/disposition |
| --- | --- | --- | --- |
| L01 / AC1,AC2 | E01 lexical negative/action sequence; D inherited negative form, numeric magnitude not published | Strict signed admission/provenance | N19/N30; source-range and form audit, Source confirmed; production missing |
| L02 / AC2 | E02 ordered first name match, not prototype/quantity | Versioned concrete lookup/index | N01/N02/N21; literal reordered lists audit; Source confirmed |
| L03 / AC2 | E03 conditional substitute/no leader/max-distance20 | Gate and source-owner versions | N03..N06/N10; equality/diagonal/short-circuit audit; Source confirmed, live substitute Unknown |
| L04 / AC2,AC5 | E06 name/prototype/type/quantity are distinct; no IN split/count | Resolved instance/type/profile | N20/N21; source quantity/addition audit; selected original join and live quantity NOT_VERIFIED |
| L05 / AC2 | E04/E10 parented TRANSFER vs unparented GET, different participants and network/editor exceptions | Callback capability and participant receipts | N13/N14/N23/N27; call-argument audit; Source confirmed, execution Unknown |
| L06 / AC2 | E05/E07 insertion/remove/list/slot/AI/magic-tech effects, no uniform parent clearing | Membership/slot/dependency adapter | N18/N22/N24; source substep audit; Source confirmed, production unsupported |
| L07 / AC2 | E06 stack addition/destruction and key special paths invalidate incoming-handle assumptions | Future survivor/quantity receipt | N20/N21/N24; prototype/type/path audit; Source confirmed, original association Unknown |
| L08 / AC2,AC3 | E01 boolean ignored, parent check then removal/placement | Verified Transferred/Placed, not boolean | N11/N12/N15/N27; continuation/fallback audit; Source confirmed |
| L09 / AC2 | E08 other ground items/new pile/ground; E09 conditional scheduler | Placement/location/container/decay owners | N11/N12/N25/N26; branch/eligibility audit; original placement/decay Unknown |
| L10 / AC3,AC6 | E01 absent continues; P MissingItem deliberately differs | Explicit owner decision, typed absence | N04/N06..N09/N28; divergence/status audit; recommendation only, decision open |
| L11 / AC3,AC6 | Original has no versioned typed transaction contract | Expected/result vector, substep receipts, partial recovery | N13..N18/N24/N29/N30; no-effect/partial audit; proposal only |
| L12 / AC3,AC4 | Independent invented complete matrix, no implementation-derived expected constants | Future production tests above | N01..N30 static scenario audit only; no new worksheet, inventory runtime NOT_IMPLEMENTED |
| L13 / AC5 | Existing E11 static evidence authenticated, cannot establish live list/parent/quantity | Bounded selected-data join then separately evidenced live state | U01..U03 next routes below; allowed specific NOT_VERIFIED, no original executable launch |
| L14 / AC4,AC6 | Address/privacy/diff; no C# inputs changed | No new production dependencies | Required link/address/semantic/scenario/privacy/diff checks; explicit C# rebuild skip; local-only CI_NOT_REQUIRED |
| L15 / AC7 | Developer candidate, not independent PASS/PR/merge receipt | Native exact-head dev-reviewer then existing delivery stages | Full ledger review required; publication and separate Reviewer merge/receipt OPEN |

## Unknowns and reproducible bounded next checks

| ID | Specific NOT_VERIFIED fact | Next permitted verification / frontier |
| --- | --- | --- |
| U01 | Selected original ItemNameId → actual definition/type/prototype/instance/quantity join | Privately resolve only the inherited selected negative operand; hash before/after its selected archive. In a bounded original aggregate read, locate only the previously authenticated NPC instance and its effective primary/substitute inventory references; reconcile instance/prototype overrides and object-ID list order. Follow only referenced item definitions/instances, never search by display/name alone. Fingerprint all selected inputs before/after; retain selectors and shapes locally. If a list is runtime-generated/unresolved, record Unknown, not absent or quantity1. |
| U02 | Authentic normal-game source/PC lists, leadership/substitute/distance, current parents and quantities | Static aggregate/prototype and editor inventory do not establish live state. A later separately authorized live-state observation must bind a versioned normal-game snapshot, selected command, exact instance/source/parent/quantity before/after and all affected owners. No game/Windows/legacy executable launch is authorized or required in this research; absence of that evidence is permitted NOT_VERIFIED. |
| U03 | Concrete callbacks, removal/insertion/fallback/decay effects for original selected item | First reconcile only selected effective attachments and referenced SCR identities privately; absent attachment must be proven. Then independently failure-inject actual headless owner adapters with N11..N29 literal oracles. Unknown/unsupported scripts, magic-tech, placement gathering or scheduler dependencies remain capability frontiers; editor bypass is not callback validation. |
| U04 | Original executable parity, safe destroyed-handle behavior, network/concurrency/transaction semantics | Separate explicitly authorized executable/adapter study with source version and full side-effect ledger; source/TIG source is not independent runtime confirmation. No rollback/atomic multiowner/concurrent exactly-once claim here. |
| U05 | Accepted quantity/profile, MissingItem continuation/rejection, placement/decay and budgets | Owner decision on explicit alternatives above before implementing any service/adapter. Research FULL PASS does not resolve this policy choice. |

For AC5 this candidate uses the **specific NOT_VERIFIED alternative** for the
original inventory/type/prototype/live quantity/parent joins U01..U03, rather than
inventing a selected static inventory probe. The existing read-only RES-019
SCR/DLG authentication is only a provenance recheck, not a new original inventory
observation. Its selectors, before/after fingerprints and shape output stay in
local run evidence. No broader NPC/DLG entry re-research or original data copying
is needed. The next check is the bounded procedure above; it is not a new GUI gate
or a missing runtime implementation prerequisite for accepting this document.

## Verification and future boundaries

Use Python3.10+ and [canonical toolchain](../workflow/toolchain.md). Existing
worksheets may be rechecked without changing them:

```sh
python3 -B tools/research/dat_probe.py --self-test
python3 -B tools/research/dialog_state_probe.py --self-test
python3 -B tools/research/dialog_state_probe.py --scripts "$LOCAL_SCRIPTS" --dialogs "$LOCAL_DIALOGS"
git diff --check
```

The original-mode output is **local only**, not attached, committed or pasted
into a PR. No new worksheet production CLI/self-test applies: none is added.
Required document checks are cited function addresses,
relative links, semantic branch/callback/quantity/status consistency, complete
literal scenario audit and privacy/scope diff. Static checks and the inherited
worksheet do not execute N01..N30 against an inventory implementation. The C# root
restore/build/test is explicitly skipped only while C# and dependency inputs are
unchanged; no GUI/export/Windows/legacy-game launch is required. CI_NOT_REQUIRED
under this task's local-only completion contract does not waive local checks.
Private evidence binds this run/session/workspace/base/head/UTC, commands, exit
results, file digests and full findings ledger. Logs stay outside deliverables.

Future boundaries (not tasks created by this research): owner decisions → bounded
headless versioned inventory/placement/dependency owners → independent actual-port
conformance tests → strict negative-IN DLG adapter with prefix/recovery receipts.
The current parser/runtime still lacks IN; viewers/labs/input/clock are unchanged.
Publication follows native exact-head full independent research PASS. One private
PR and a separate policy-authorized Reviewer acceptance/merge with fetched ancestry
receipt remain later delivery obligations; document preparation is not whole delivery.

