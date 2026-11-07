# Bounded NPC conversation runtime (ARC-SCRIPT-005)

## Status and scope

`Arcanum.Scripting.Dialog.DialogConversation` is an independently authored, Godot-free,
synchronous, non-thread-safe net8.0 runtime for one multi-turn NPC conversation over an
immutable `Arcanum.Data.DlgDocument`. It runs the NPC entry action, offers the eligible
replies, executes the selected reply's action and then continues at the next NPC node, ends
with a return to the caller's SCR line, ends with an NPC final line, or stops at a typed
frontier. The host serializes every call and every externally mutated owner.

It covers, in a bounded form, FR-DIALOG-001..004, FR-SCRIPT-002/003 (GF inside DLG) and the
data side of FR-UI-003/005. The AREA viewer adds the first real use: talking to the Crash
Site NPC whose effective SAP_DIALOG attachment names a script (only Virgil in the core).

It is **not** a SCR VM, generic expression evaluator, generated-dialog (`gd_*`) text
loader (the [greeting](dialog-greeting.md) and the [generated phrases](#generated-phrases-arc-script-043) read the loaded files through its port), inventory/party/map/AI simulation, save service, voice/media, or original-runtime
parity claim. The older [one-edge transition runtime](dialog-transition-runtime.md) is
unchanged and still rejects GF; the two runtimes do not share admission tables.

## Public API

```csharp
var conversation = DialogConversation.Start(document,
    new DialogConversationRequest(entryNode, participants, callerScrLine,
        new DialogPcFacts(DialogPcGender.Male, Intelligence: 8, IsDumb: false, Race: 0),
        new DialogSpeakerNames("[PC]", "[NPC]")),
    new DialogConversationPorts(localFlags, commands, globalFlagOwner, world));
while (conversation.Phase == DialogConversationPhase.AwaitingSelection)
    conversation.Select(conversation.Options[0].Token);   // or Close() from the host
```

| Member | Meaning |
| --- | --- |
| `Phase` | `AwaitingSelection`, `Ended`, `AtFrontier`, `Failed`; the last three are terminal. |
| `NpcLine` | Decoded, name-substituted line of the current NPC node. |
| `Options` | 1..5 numbered `DialogOption`s with text kind, marker, transition, target/SCR line, token and optional `Block`. |
| `Effects` | Ordered `DialogEffectReceipt`s: ordinal, turn, typed command, operation source, outcome, GF receipt. |
| `End` / `Frontier` / `Failure` | Typed terminal outcome; no original text, raw expressions or exception messages. |
| `RefreshOptions()` | Re-reads gates without effects under a new generation; old tokens become `NotOffered`. |
| `Select(token)` | Current token only; re-reads the selected gates first (`StaleSelection` rebuilds offers). |
| `Close()` | Host close while awaiting: no reply effect and no SCR continuation. |

Ports: `IDialogStateQueryPort` + `IDialogCommandPort` (existing LF/QU/RU seams), the
production [`GlobalFlagOwner`](global-flag-state-runtime.md) and `IDialogWorldPort`
(reaction query and commands, alignment, negative IN, MM, JO). A missing capability makes an
NPC action or reply condition that needs it a static `CapabilityUnavailable` frontier, and
blocks (`UnsupportedOperation`) a reply whose own action needs it. The optional
`IDialogPartyPort` (ARC-WORLD-013, [party runtime](../world/party-runtime.md#dialog-seam))
answers `WA`, `JO` and `UW`; when present it admits `WA`/`UW` and takes `JO` instead of the
world port, and without it the grammar is unchanged. The optional `IDialogInventoryPort`
(ARC-WORLD-014, [inventory runtime](../world/inventory-runtime.md#dialog-seam)), when present, takes negative `IN`
instead of the world port; `IN` then needs only one of the two. The optional `IDialogRumorPort` (ARC-UI-008,
[logbook](../rendering/terrain-viewer.md#logbook-arc-ui-008)) takes `RQ` and, since ARC-DEV-171, answers the `RU` and `RQ` reply
conditions ([rumor conditions](#rumor-conditions-arc-dev-171)); without it an `RQ` action and the two conditions are a static
`CapabilityUnavailable` frontier. The optional `IDialogSkillPort` (ARC-SKILLS-004, [skill gates](#skill-gates-arc-skills-004))
answers the PC's skill levels and training and takes `TR`; when present it admits `PS`, `HA`, `SK`, `TR` and `ET`, and
without it they stay `UnsupportedOperation`. The optional `IDialogMetBeforePort` (ARC-WORLD-043,
[dialog binding](../world/npc-reaction-runtime.md#dialog-binding-arc-world-043)) answers `ME`; without it an `ME` condition is
a static `CapabilityUnavailable` frontier. The optional `IDialogReputationPort` (ARC-WORLD-044,
[reputation runtime](../world/reputation-runtime.md#dialog-and-scripts)) admits `RP` as a condition and an action; without it
`RP` stays `UnsupportedOperation`. A skipped `RP` action (a number the owner does not admit) is recorded as
`DialogCommandAttemptOutcome.Skipped` and the dialog goes on.

## Flow and source semantics

Source references are the function addresses listed under [Provenance](#provenance); implementation is independent.

1. **Entry** — the caller supplies the checked NPC node (in the viewer: the dialog
   action the [SCR dispatcher](scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3)
   reached, with its line; ARC-SCRIPT-009 replaced the [entry resolver](npc-dialog-entry-runtime.md)
   there). A "return to script" end resumes the dialog script through the dispatcher.
2. **Static step plan before any effect** — NPC action grammar, NPC text, the whole reply
   group (gender selector, condition grammar, text, markers, response targets) are checked
   before the NPC action runs. Unsupported → frontier, malformed → failure, zero effects.
3. **NPC action**, then **offers**: the reply group is the Player run after the NPC in
   numeric-ID order (source view); it must equal the serialized run or the step is a
   `GroupOrderMismatch` frontier (project guard, all selected Virgil groups match). Gates are
   gender (`-1` any, `0` female, `1` male), then effective IQ (`iq>0`: at least, `iq<0`: at
   most `-iq`; a dumb PC above 4 counts as 1), then the condition conjunction left to right,
   short-circuiting on the first false. Enumeration **stops after five eligible replies;
   later rows are not queried** (source behavior; this deliberately differs from the RES-019
   overflow-fail proposal). No eligible reply gives one generated-farewell fallback that exits.
4. **Look-ahead per offer** — a reply's action and its next NPC step are planned (pure, one
   level). If either cannot run here the reply is still shown, but `Block` says why and
   selecting it returns `UnsupportedTransition` with no effect.
5. **Selection** consumes the token before the first effect, runs the reply action, then:
   positive response → next NPC node (new turn); `0` → `Ended(ReturnToScript, caller+1)`;
   negative `-n` → `Ended(ReturnToScript, n)`; an insult (below) first runs its rule; a trailing `JO 0 node` (or, with a party port,
   `UW 0 node`) → join (or stop waiting), continue at node; a trailing `FL node` → run that node's action, show its line, `Ended(NpcFinalLine)`.
   SCR continuation is reported, never executed.

Text: NPC lines use the record text for a male PC and the alternate field for a female PC.
`@pcname@`/`@npcname@` (ASCII case-insensitive) take host-supplied names; any other token is a
frontier and an unterminated `@` is a failure. Code page is explicit
(`DialogTextEncoding.Windows1252`, data-observed for the selected English installation:
0x85 ellipsis, é, ï); CRLF becomes LF, tab/LF are kept, other C0 controls, DEL, the five
undefined 1252 bytes and texts over 4096 bytes fail closed.

Generated markers (one ASCII letter and `:`; NPC markers are read from the record text
whatever the PC gender, as in the source): Player `a e f k n s w y` (exact) are offered as
`Generated` with their normal transition (their text is a generated phrase, [below](#generated-phrases-arc-script-043)); exact `i:` (ARC-WORLD-043) is the original's **insult**: offered as `Generated` with its normal transition, and
picking it runs the reply's action, then a `ReactionCommand` of −10 (`DialogConversation.InsultReaction`) through the world
port, then reads the effective reaction; at most 20 (`InsultEndsAtOrBelow`) the talk ends as `Ended(Insulted)` with no SCR
continuation (the NPC's generated reply is not loaded), above 20 its transition follows (0x4191E0). Without a world port, or
with an action ending in `JO`/`UW`/`FL`/`ET` (the original then skips the insult, read for FL), it is offered blocked. `b h l p` (exact) and
`c r t z` (prefix) are offered blocked as
`UnsupportedInteraction`; `d q u x` have state-dependent visibility and make the step a
frontier. NPC `g:` (prefix) is the original's greeting when the conversation has a greeting port (ARC-WORLD-058,
[dialog greeting](dialog-greeting.md)), else a frontier; `m:`/`r:` (prefix) and exact `i:` are frontiers. Other text is literal.

## conversation-grammar-v1

```text
separator = SP | HTAB | ',' | ';' | ':' | '(' | ')'      field = separator* (op (separator+ op)*)? separator*
space     = SP | HTAB
op        = LETTER LETTER space* int (space+ int)?          int = ('-' | '+')? DIGIT+
          | ('SK' | 'TR' | 'ET') space* int (space* int)?   (ARC-SKILLS-004: digits are read greedily, so a second int
                                                            without a space before it starts with '-': "tr4-1")
```

| Field | Admitted operations (domains) |
| --- | --- |
| Player condition | `LF b v` (b 0..31, v 0/1, attachment flag equals v; Missing is false), `GF f v` (f ≥ 0, v 0/1; Known only), `RE t` (t ≥ 0: reaction ≥ t; t < 0: reaction ≤ −t), `RA n` (n > 0: PC race index n−1; n ≤ 0: race ≠ −n−1), `ME v` (ARC-WORLD-043; v 1: the NPC has met the PC before, 0: it has not; the original passes any other operand untested; here it is an `UnsupportedOperation` frontier); with a reputation port `RP n` (n > 0: the PC holds n; n ≤ 0: it lacks −n); with a party port `WA v` (v 1: the NPC waits for the PC, 0: it does not); with a skill port `PS n`, `HA n`, `SK s n`, `TR s t` ([skill gates](#skill-gates-arc-skills-004)); with a rumor port `RU n` and `RQ n` (|n| 1000..2999; [rumor conditions](#rumor-conditions-arc-dev-171)). |
| NPC / Player action | `LF b v`, `GF f v`, `QU q s` (q > 0, s 0..6), `RU r` (r ≥ 1000), `RQ r` (r ≥ 1000: quell the rumor, rumor port), `RE`/`AL` unsigned = target, `+n`/`-n` = adjust, negative `IN -n` (NPC gives item name n), `MM a` (a ≥ 0); with a skill port `TR s t` (set the PC's training); with a reputation port `RP n` (n > 0: add n; n ≤ 0: remove −n). |
| Quest log (quest port) | `QU q v`, `QA q v`, `QB q v` as Player conditions (q 1000..1999; the PC's state of the quest equals, is at least, is at most v; v read as for `SK`) |
| Money (gold port) | `$$ n` (n any 32-bit signed integer but −2³¹, no `+`) as a Player condition (the gold of the PC and its direct followers is ≥ n for n ≥ 0, ≤ −n for n < 0) and as an NPC or Player action (n > 0 the PC gets n coins; n < 0 the PC and its followers pay −n to the NPC) |
| Combat (combat port) | `CO` as an NPC or Player action (any number after it is read and ignored, as the original does): the attack event of the PC on the NPC runs once at the end of the action string ([below](#combat-action-arc-dev-190)) |
| Player action tail | one `JO 0 node` or `FL node` (with a party port also `UW 0 node`, with a skill port also `ET s node`) as the last operation (NPC node checked statically); after an `FL node` only a `CO` may follow (ARC-DEV-190). |

Unknown opcodes, `$` (other than the money code `$$` with a gold port, [ARC-DEV-179](#money-code-arc-dev-179)), combinators, `RE`/`AL` with `>`/`<`, non-negative `IN`, `JO`/`UW` with a non-zero
first operand, `JO`/`UW`/`FL` in NPC actions or not last, `WA`/`UW` without a party port, and a non-space separator inside one
operation (`GF 5,1`, `qu(5,6)`: the source's whitespace-skipping `atoi` would read 0 there)
→ `UnsupportedOperation`; such forms are never reinterpreted. A missing operand (except the optional second operand of
`SK`/`TR`/`ET`, which reads 0), missing separators, out-of-domain values, non-ASCII, `+` outside `RE`/`AL` actions,
overflow → `InvalidOperation`. Since ARC-SKILLS-004 a number where an operation should start, an extra operand (`LF 1 1 1`,
`JO 0 30 5`) or a stray one (`ha4, -4, lf1 1`), which the source's scanner skips (DS1), is `UnsupportedOperation` (a frontier,
not a failure). A field of separators only is empty. GF reads/writes use the
owner's current version; `Unknown`/`Unavailable` is a frontier, `Failed`/`Stale` a failure,
`NoChange` a successful receipt.

## Skill gates (ARC-SKILLS-004)

The source's dialog skill cases, read in the retail 1.0.7.4 executable by two readers
([ledger DS1–DS5](../research/ledger/skills.md#dialog-skill-gates-arc-skills-004)). Skills are the source's
numbers: 0..11 the basic skills, 12..15 the tech skills; the port reads any other number as level 0 and no training, as the
source's getters answer 0 for it. Codes match case-insensitively.

| Operation | Behaviour |
| --- | --- |
| `PS n`, `HA n` (condition) | Persuasion (skill 11) and haggle (skill 9) level shortcuts: n ≥ 0 passes at level ≥ n, n < 0 at level ≤ −n. |
| `SK s n`, `TR s n` (condition) | The PC's effective level (`SK`) or training tier (`TR`, 0 none .. 3 master) in skill s against n, as above. |
| `TR s t` (NPC or player action) | The PC's training in skill s is set to tier t through the source's training setter (one tier up at a time, any down, the tier's minimum level); a tier the rules refuse, an unknown skill or tier changes nothing and the dialog goes on. |
| `ET s node` (player action tail) | The expert-teacher gate: a level of at least 9 (the expert minimum) and any training continue the dialog at the node; otherwise the source speaks a generated "won't teach" line (message ranges 6200..6299 for a low level, 6300..6399 for an untrained skill) with generated player options, which is the frontier `ExpertTeacherRefused` here. |

The second operand of `SK`, `TR` and `ET` is read as the source reads it: the C library's `atoi` after the first operand's
digits, so `tr4-1` is skill 4, tier ≤ 1, and a missing one (`tr41,`) is 0. A negative first operand is refused
(`InvalidOperation`): the source would read it as the second operand as well. The source runs any operation after `ET`; this
subset ends a player action there, as after `JO`.

Session seam: `SessionDialogSkillPort` over `GameSessionOwner.QueryHeroSkills` and `SetHeroSkillTraining`
([hero skills](../world/hero-skills-runtime.md)). The AREA viewer passes it when the session has a hero (`--hero`); the
diagnostic PC has no skills, so the gates stay unadmitted there.

Installed original (`DialogSkillRealDataTests`, `DAT_ROOT`, first observation 2026-10-21): over the 709 DLG files the content
stack resolves, 937 `TR`, 440 `SK`, 354 `HA` and 275 `PS` conditions, 53 player and 62 NPC `TR` actions and 118 `ET`
actions (in 20 files); every one parses on its own. Of the 1,582 fields that hold one, 1,218 parse whole and 364 stop at an
operation outside this subset (money `$$` 121, rumors 84, quests 47, …), never at a skill operation.

## Fate point action (ARC-WORLD-054)

With the optional `IDialogFatePort` (`DialogConversationPorts.Fate`) the NPC and Player actions admit `FP`: the PC gets one fate
point (the source's dialog action `fp`, code 29 of its action table, [FT8](../research/ledger/skills.md#fate-points-arc-world-054)).
The receipt is a `FatePointCommand`; Applied also covers a PC already at 100 points. Every installed `fp` is written without an
operand, so `FP` takes none here; a number after it is an `UnsupportedOperation` frontier (the source reads it and ignores it).
Without the port `FP` stays unadmitted. The session's port is `SessionDialogFatePort` over the hero
([fate points](../world/fate-points-runtime.md)); the AREA viewer passes it when the session has a hero.

## Script store operations (ARC-DEV-152)

With the optional `IDialogScriptStorePort` (`DialogConversationPorts.Stores`) Player conditions and NPC and Player actions admit
`GV i v`, `PV i v` and `PF i v`, the source's condition codes 6, 30, 31 and action codes 5, 22, 23
([DG1–DG4](../research/ledger/scripts.md#dialog-script-stores-arc-dev-152)): a condition holds when the global variable *i*, the
dialog PC's variable *i* or the dialog PC's flag *i* (0 or 1) equals *v*; an action stores *v* there, in field order, as a
`ScriptStoreCommand` receipt. Both operands are read as the LF/GF pair (spaces before the second, no `+`). A global index outside
0..1999 is an `InvalidOperation` (the source reads and writes past its 2000-word array), and so is a negative PC index; a PC index
above 65535 (the session's 16-bit stores; the source's PC arrays are sparse object fields) and a PF value
other than 0 and 1 are valid data outside the subset (`UnsupportedOperation`: the source's condition never passes it and its action
spreads it over the flag's 32-flag word, GV4); no installed dialog has one. Without the port the three stay unadmitted. The
session's port is `SessionDialogScriptStorePort` (the session's global variables, the hero's variables and flags; a session without
a hero reads 0 from the PC stores and ignores a PC write, SA14); the AREA viewer always passes it. The installed dialogs hold 1285 `GV`
operations in 32 files and no `PV` or `PF`.

## Rumor conditions (ARC-DEV-171)

With the optional `IDialogRumorPort` (`DialogConversationPorts.Rumors`) a Player reply's condition admits `RU n` and `RQ n`, the source's
condition codes 17 and 18 ([RU1–RU2](../research/ledger/scripts.md#dialog-reply-gates-and-rumor-conditions-arc-dev-171)): `RU n` with
n ≥ 1000 holds when the PC has learned rumor n (`IsKnown`; the session's `QueryRumor` is not null), `RU -n` when it has not; `RQ n` holds
when rumor n is quelled (`IsQuelled`; a game-wide set, the PC does not matter), `RQ -n` when it is not. A number whose absolute value is
outside 1000..2999 (the session's rumors; the source reads its arrays unchecked there) is `UnsupportedOperation`, as is `0`; a throwing
port fails the conversation (`QueryException`). Without the port both are a `CapabilityUnavailable` frontier. The source treats a stored
time of 0 as unknown; a rumor learned at game time 0 counts as known here (an edge not modelled). The installed dialogs hold 223 Player
condition fields with `ru` or `rq` in 21 files. Two kinds are damaged data and stay outside the subset: 19 fields
of dialog 1934 read rumor 4180, and one `rq - 1143` of dialog 1906 has a space after the sign (the source's `atoi` reads 0, here an
`InvalidOperation`).

Bug B17 (the plane-wreck dialog, DLG 1696): its replies 6 to 9 hold `ru 2015` and `ru -2015`. Until this task the condition was not
admitted, so the reply group of node 5 was a frontier and the reply to the first remark was blocked for every hero. The reply gate by
intelligence was not the cause (checked in the executable, [IQ1](../research/ledger/scripts.md#dialog-reply-gates-and-rumor-conditions-arc-dev-171)):
a clever hero (IQ ≥ 5) and a dull one (IQ ≤ 4, or dumb) each have a complete path through the dialog.

## Condition codes outside the subset (ARC-RES-091)

The original matches 37 condition codes (0x4150D0, 0x5A06BC); this runtime parses 20 (`LF GF RE RA ME RP WA PS HA SK TR GV PV PF RU RQ $$ QU QA QB`) and, as an action, `CO` (35 action codes in the original).
Any other code in a Player condition is `UnsupportedOperation`: the reply is offered blocked, and a reply group that needs it is a frontier
(`TargetUnsupported` on the reply that leads there). The owner's decision (2026-11-07) is that the remaining codes are implemented. The plan,
the slices and the table of codes are in [dialog-condition-codes-plan.md](../research/dialog-condition-codes-plan.md). Slice C0 (ARC-RES-092) read
all 37 condition and 35 action codes in the executable ([DCC1–DAC12](../research/ledger/scripts.md#dialog-condition-and-action-codes-arc-res-092)) and counted
them over the 709 installed DLG files (the opt-in test `DialogCodeCensusRealDataTests`): 10,431 condition and action fields hold a code outside the
subset (10,384 stop first on one); admitting gold `$$` freed 2,857 of them (C1, ARC-DEV-179, below), the quest log `qu qa qb` freed 1,653 more (C2, ARC-DEV-180, [below](#quest-log-conditions-arc-dev-180)), the combat action `co` freed 1,325 more (C3, ARC-DEV-190, [below](#combat-action-arc-dev-190)), and the plan orders slices C1 to C10 by it. Slices C4 to C10 are not built yet; 4,596 fields still hold a code outside the subset.

## Money code (ARC-DEV-179)

With the optional `IDialogGoldPort` (`DialogConversationPorts.Gold`) Player conditions admit `$$ n` and NPC and Player actions admit `$$ n`, the source's
condition code 10 and action code 0 ([GM1–GM6](../research/ledger/scripts.md#dialog-money-code-arc-dev-179)). The scanner takes `$` as a code letter, so
`$$` is a two-character code like the others; the operand is any signed 32-bit integer but −2³¹ and a `+` sign, read as for `RP`.

- **Condition.** The signed rule on `Total`, the gold of the PC plus every direct follower of the PC as a signed 32-bit sum that wraps: n ≥ 0 holds at total ≥ n,
  n < 0 at total ≤ −n, both inclusive. A throwing port fails the conversation (`QueryException`).
- **Action.** A `GoldCommand(pc, npc, n)` receipt in field order, the string going on after it. n > 0: the PC gets n coins from nothing (no source object, no insert
  check). n < 0: the PC pays −n to the NPC; the amount still owed then falls by the PC's whole gold and each direct follower (in the follower array's order, listed
  after the PC's payment) is asked for what is left until nothing is owed. n = 0 changes nothing. **The original's bug (GM3):** a payer's transfer is all or
  nothing, yet the amount owed falls by all the payer holds, so a payer with less than the amount owed at its turn pays nothing and is still counted as having paid;
  and a payer with no gold stack at all credits the NPC the whole amount owed from nothing (GM2); the Original setting keeps both, Steamwoven makes each payer pay the lesser of its gold and the amount owed
  (`DialogRuleProfile.ShortPayerPaysNothing`, approved by the owner on 2026-11-07). A payer that cannot pay is not an error; a command the session cannot run (no hero, a PC that
  is not the session's, an NPC it does not know, a budget or revision refusal) is `Rejected` and fails the conversation, and payments already made stay made, as in
  the source.
- **Session.** `SessionDialogGoldPort` over `GameSessionOwner`: `QueryGold`, `QueryNpcGold` of each follower (a follower the session holds no list for counts 0; the host
  attaches a carried companion's map seed first), `GiveGoldToHero` (coins from nothing), `GiveGoldToNpc` (the hero to the NPC) and `MoveGoldBetweenNpcs` (a follower's
  pile to the NPC's, all or nothing as in the source; a destination without a pile gets a new `made:n` pile after the host's insert check). Each payment is one
  revision. The talking NPC as its own follower pays itself nothing (GM4).
- **Viewer.** The AREA conversation host passes the port when the session has a hero and the inventory host covers the shown map; otherwise `$$` stays unadmitted
  (a condition is a frontier, an action blocks its reply). A new pile of an NPC without one is of the gold prototype with the stored name once the barter data has loaded.
- **Installed data.** 1,576 Player condition fields (81 files) and 1,388 action fields (139 files) parse, but three action fields: `$$ - 500` twice (a space after
  the sign, which the source reads as 0) and `$$-500qu1042 2` (the operand run into the next code), forms outside the strict operand grammar (`InvalidOperation`).
- **Not modelled.** The event the source fires on the destination before a transfer from a pile (0x441980; a source pile with an insert-item script is refused),
  a follower whose list the session does not hold, a gold pile of a dead follower, and the global mode byte that makes the transfer do nothing (0x62AC6C, not identified).

## Quest log conditions (ARC-DEV-180)

With the optional `IDialogQuestPort` (`DialogConversationPorts.Quests`) Player conditions admit `QU q v`, `QA q v` and `QB q v`, the source's condition codes 8, 24 and 21
([QL1–QL5](../research/ledger/scripts.md#dialog-quest-log-conditions-arc-dev-180)). Without the port they stay `UnsupportedOperation` as before (the `QU` action needs
no port and is unchanged).

- **Operands.** The quest number `q` is 1000..1999 (another number is valid data with no state to answer: outside the subset, `UnsupportedOperation`). The second operand
  `v` is read as `SK`, `TR` and `ET` read theirs (0x4167C0, DS3): spaces before it are optional (`qa1005 4`, `qa 1005 4`) and a missing one is 0; a `+` sign is invalid, a
  negative `v` is legal (`qb q −1` never holds, `qa q −1` always does).
- **Condition.** The port answers `State(pc, q)`; `QU` holds at state = v, `QA` at state ≥ v, `QB` at state ≤ v, signed compares. The state is the source's
  getter (0x4C4CB0): 0 Unknown (a quest the PC never changed), 1 Mentioned, 2 Accepted, 3 Achieved, 4 Completed, 5 OtherCompleted, 6 Botched (6 also for a quest botched
  over a kept stored state). A throwing port fails the conversation (`QueryException`). The three conditions join the other operations of the field as a conjunction.
- **Session.** `SessionDialogQuestPort` over `GameSessionOwner.QueryQuest`: the same log the `QU` action writes through `SetQuestState`; a PC other than the session's
  throws. The AREA viewer host passes it always (the session has the one PC, with or without a hero).
- **Installed data.** 1,303 `qu`, 528 `qa` and 468 `qb` Player condition fields (139, 96 and 84 files) all parse, but two fields that stop at a form unrelated to the quest.
  Both rule settings behave the same.

## Combat action (ARC-DEV-190)

With the optional `IDialogCombatPort` (`DialogConversationPorts.Combat`) NPC and Player actions admit `CO`, the source's action code 4
([CA1–CA5](../research/ledger/scripts.md#dialog-combat-action-arc-dev-190)). Without the port it stays `UnsupportedOperation` as before.

- **Operand.** None. A number after the code is read and ignored (`co`, `co 0`, `co5` are the same; DS1 reads one after every code), as `SU` does.
- **When.** `CO` itself only sets a flag. The attack event runs **once, at the end of the action string**, after every other operation of the string and, when the
  string holds `FL node`, after the final line was shown and that node's own action ran (`co, fl 40` and `fl 40, co` do the same). The grammar keeps `CO` as the only
  operation that may follow an `FL`; any other tail (`fl 40, co, gf 3 1`) and a `CO` before or after `JO`, `UW` or `ET` (only `FL` was read to speak its node inside the string) stay `UnsupportedOperation`. A final node with its own `CO` runs its event first (the nested string), then the reply's. A failed operation earlier in
  the string stops it (the conversation fails or stops at its frontier), so the event does not run.
- **Port.** `Execute(StartCombatCommand(Npc, PlayerCharacter))`: the owner runs the attack event of the PC on the NPC (loudness 1, no flags). `Applied` is recorded as an
  effect; `Rejected` fails the conversation (`CommandRejected`), a throwing port is recorded as indeterminate (`CommandIndeterminate`). Each action string asks at most once.
- **AREA viewer.** `AreaConversationHost.HeroAttack` is set by the viewer to the shown map's AI host (`AreaAiHost.OrderAttack`, the hero on the speaker, loudness 1, no
  flags, the same event the hero's own attacks run); `HeroAttackAvailable` (the viewer: the AI host exists) is read when a talk starts, and without the sink or while it is false (no AI host: the automated runs without AI, no
  hero) the port is not offered and `CO` is outside the subset for that talk; a host that refuses the attack all the same (an unknown NPC) rejects the command. `Rebind` keeps both.
- **Not modelled.** After a string that returns 1 the original's reply step re-checks whether the NPC is hostile toward the PC before it enters the next node (SU10, CA3);
  the runtime does no such check for any reply, so a `CO` reply that goes on to a node (76 installed fields: `co` alone) enters it without that check. Replies that end in a
  final line (the rest) are not affected.
- **Installed data.** 1,329 Player action fields (203 files) hold `co`; 1,276 parse whole after the slice, 53 still stop (one at `co.fl n`, the others at another
  operation of the field). Both rule settings behave the same.

## Schematic window action (ARC-TECHNOLOGY-006)

With the optional `IDialogSchematicPort` (`DialogConversationPorts.Schematics`) a Player reply's action admits `SU` (the source's
dialog action `su`, code 30 of its action table, [NC1–NC2](../research/ledger/technology.md#npc-schematic-crafter-arc-technology-006)):
an operand is read and ignored; the receipt is an `OpenSchematicsCommand` (the NPC as crafter, the PC as viewer) recorded in field
order, so the field's other actions run before and after it; the reply's transition is `DialogOptionTransition.Schematics` with no
target node (no look-ahead; a missing or non-NPC response target does not fail the step, since the reply never enters it), and picking it ends the conversation with `DialogEndKind.SchematicWindow`: no further NPC line, no SCR
continuation, and the talk does not resume when the window closes (the original's interface mode change ends the dialog). A
rejected command (the AREA port refuses a dead NPC or PC) fails the conversation. Outside the subset: `SU` with JO, UW, FL, ET or an
insult in the same reply (offered blocked, `UnsupportedInteraction`) and `SU` in an NPC line's own action (an unsupported-operation
frontier; the source also sets its window code there, NC8, one reader). The AREA viewer's port records the speaker for the window
([schematic window](../world/schematic-window-runtime.md#npc-crafter-arc-technology-006)).

## Generated phrases (ARC-SCRIPT-043)

With a greeting port (`DialogConversationPorts.Greeting`, the [greeting seam](dialog-greeting.md)) a Player reply whose whole text
is one of the markers below (case-insensitive) shows a generated phrase as its text, and so does the farewell offered when no reply
qualifies; `DialogOption.TextOrigin` names its file and number. The phrase is picked as the reply is offered, in offer order, with the
port's facts, files, rules and a `DialogGreetingRoll.Phrase` roll (the AREA host's `dialog.phrase.line`), by the same pick as a
greeting line (GD5, the [GD-A1](../formats/generated-dialog-files.md#api) gap switch, the missing-line text, `@pcname@`/`@npcname@`).
A rebuild of the same node's offers (`RefreshOptions`, a stale selection) keeps the phrases already shown in the turn (only a reply
that became eligible since rolls its own): the original rebuilds its options with the random state it saved (ledger [GP6](../research/ledger/social.md#generated-player-phrases-arc-script-043)).

| Marker | Phrase (0x416C10) |
| --- | --- |
| `y:` `n:` `s:` `e:` `f:` | PC lines 1–99, 100–199, 200–299, 400–499, 800–899 |
| `b:` | PC lines 300–399, or 1600–1699 when the PC leads the NPC (`DialogGreetingFacts.NpcLedByPc`) |
| `h:` `k:` `w:` `p:` `l:` | PC lines 700–799, 1500–1599, 1800–1899, 1900–1999, 2300–2399 |
| `a:` `i:` | PC class lines from 1000 and 3000 (the insult, 0x419190) + 50 × the NPC's social class, 50 entries |
| no reply qualifies | PC lines 400–499 (0x414E60) |

A PC line (0x4182D0) reads `gd_pc2m` or `gd_pc2f` by the NPC's gender, a PC class line (0x418390) `gd_cls_pc2m` or `gd_cls_pc2f`;
a dumb PC (its intelligence as the reply gates read it at most 4, the original's 0x45FC00) reads their dumb variants, the class
offset still applied. Without the port, or when its facts are unknown, the text stays empty and the reply works as before; a refused
roll, a port exception or an undecodable phrase fails the conversation (`InvalidQueryResult`, `QueryException`, `InvalidText`). The
phrase changes only the text: the transitions and blocks above stay. Not built: the phrases of the prefix markers (`c d q r t u x z`;
`d r t x` append the reply's text after a phrase), the healer menu behind `h:` (ARC-SCRIPT-044) and the NPC's generated line after
an insult (group 1000).

## Barter reply (ARC-UI-050)

With the optional `IDialogBarterPort` (`DialogConversationPorts.Barter`) a Player reply whose text is exactly `b:` (the generated barter
phrase, [below](#generated-phrases-arc-script-043)) is offered with the transition `DialogOptionTransition.Barter` and no target node; without the port it stays
blocked as before (`UnsupportedInteraction`). Picking it runs the reply's action in field order, then the port's `OpenBarterCommand` (the
NPC as merchant, the PC as buyer, recorded after the action), and ends the conversation with `DialogEndKind.BarterWindow`: no further NPC
line, no SCR continuation, and the talk does not resume (the original's mode change ends the dialog, ledger
[BT7](../research/ledger/items.md#barter-arc-ui-050)). The reply's response value is not followed, so a missing target does not fail
the step. A rejected command (the AREA port refuses without a living hero) fails the conversation. Outside the subset: a `b:` reply
whose action holds JO, UW, FL, ET or SU (offered blocked). The AREA viewer's port records the speaker for the
[barter window](../world/barter-runtime.md).

## Bounds and failure accounting

Caller options may only lower the hard caps: 64 structural replies, 1024 turns (default 256),
4096 effects, 4096 text bytes, 4096 operation-field bytes, 32 operations per field. Turn and
effect budgets are checked before a step's first effect (`TurnLimit`/`EffectLimit`
frontiers). A port `Rejected` result records `Rejected` and fails; a thrown exception records
`Indeterminate` and fails; earlier `Applied` receipts stay. `JO` or `UW` refused by the world or
party port is a `JoinRefused` frontier (the source then speaks a generated refusal). No rollback is claimed.

## AREA viewer integration (PROJECT diagnostic)

The PC facts (`DialogPcFacts`) stay caller-owned. Since ARC-WORLD-015 a session started with a hero answers them
(`GameSessionOwner.QueryHeroDialogFacts`: gender, effective intelligence, race; [starting hero](../world/starting-hero-runtime.md));
the AREA viewer does not start one yet and passes its diagnostic profile below.

In `area` mode the original **left click** on an NPC whose
effective SAP_DIALOG attachment names a script talks to it. The NPC must be the
[hover pick](../rendering/terrain-viewer.md#hover-tooltip-and-action-cursor-arc-ui-005) under the
pointer: the frontmost opaque body that is not click-through (ARC-UI-005). The click takes the guarded
ground-click path of the [click-to-move route](../world/click-route-runtime.md) (focus, modal, gate,
lifecycle, pending map transfer). Within 3 tiles (Chebyshev; project rule, not source range)
the dialog opens at once; farther away the route walks to the nearest standable cell within
reach and the talk starts on arrival, unless a stick, another click, a reset/reload or a modal
cancels it (like the cave walk-to-use). The cave map has no dialog NPC. Replies are chosen by **mouse**, as in
the source (its dialog text control reads only mouse messages, F0); the controller menu navigates, confirms and
cancels. Keyboard keys are not redirected to dialog actions: the WORLD keyboard route keeps
them reserved for their original actions ([palette and keyboard route](../input/diagnostic-palette.md)),
so `T`, digits, `Esc` and `Enter` do nothing in the dialog. A diagnostic **Leave** button
closes an awaiting dialog without a reply. The panel is modal, so the actor gate stays closed. `AreaConversationHost` (viewer Core) reads scripts and dialogs
through the game's content stack (ARC-SCRIPT-011, [script content](script-content.md): the original's name for
each script number and the winning copy, so Virgil's script is `Arcanum4.dat`'s, as the game opens it), runs SAP_DIALOG through the
[SCR dispatcher](scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3) (ARC-SCRIPT-009: Following from
the session, Jilted false, the dialog slot from the session's keys over the loaded attachment), and reads the
DLG named after the script, refusing a node that is not an NPC record.

Since ARC-WORLD-032 the same host serves the viewer's map mode: the speakers are the dialog NPCs of the
map's own snapshot, and the consequences stay in the session the map carries
([dialogs in map mode](../rendering/area-map-mode.md#dialogs-arc-world-032), with a census of all interiors).

`AreaConversationProfile.Diagnostic` is a **project diagnostic, not original state**: male
human PC, intelligence 8, not dumb, alignment 0, initial NPC→PC reaction 50, names
`[PC]`/`[NPC]`, follow requests accepted. No item, party, map or AI simulation runs. The
panel shows typed identifiers in its effect ledger, never raw operations.

Since ARC-WORLD-043 the host opens the speaker's reaction conversation slot right before each conversation starts and
`EndReaction()` closes it; the viewer calls it on every close of the panel, before a resumed script runs (the original's
dialog start and end, [dialog binding](../world/npc-reaction-runtime.md#dialog-binding-arc-world-043)). `ME` reads the
session's "met before" through the state's `IDialogMetBeforePort`, and the panel names an insult's change and its end.

### Consequences in the game session (ARC-SCRIPT-006)

The host borrows the area's [game session](../world/game-session-runtime.md), created for
every published area (with or without the cave), and `AreaConversationState` stores the
dialog consequences there, so they survive further talks, the cave trip and a
capture/encode/decode/restore of the session, and they change the next conversation:

| Consequence | Where it lives |
| --- | --- |
| GF conditions and writes | The session's flags: original new-game domain 0..3199, all known false (source: 100 words of flags zeroed on init and reset (0x4446E0, 0x4447D0)); flags ≥3200 are not admitted and fail the dialog. |
| QU | Session quest rules ([story domain](../world/game-session-runtime.md#story-domain)); a QU that leaves a quest Accepted lifts the NPC's effective reaction to at least 41 (a delta through the reaction domain), Completed adds 10 (source rule, checked in the original executable); such a QU takes two session revisions (quest, then reaction). A quest outside 1000..1999 is refused and fails the dialog. |
| RU | Session rumors with their game time; outside 1000..2999 is refused. As a condition (ARC-DEV-171) the session's known rumors: `QueryRumor` is not null. |
| RQ | ARC-UI-008: the session's game-wide quelled rumors (no participant, as the original's dialog action); quelling a quelled rumor is no change; outside 1000..2999 is refused and fails the dialog. As a condition (ARC-DEV-171) the same set: `QueryRumorQuelled`. |
| RP | The session PC's reputations (ARC-WORLD-044, [reputation runtime](../world/reputation-runtime.md)): `RP` reads and changes the list; a number outside 1000..1999 is skipped. |
| RE, ME, insult | The session reaction domain ([NPC reaction runtime](../world/npc-reaction-runtime.md), ARC-WORLD-026): `RE t` reads the speaker's effective reaction, unsigned `RE v` sets it as a delta to the effective value, `RE ±d` and an insult's −10 adjust it through the PC's adjustment effects; while a talk is open they land in its conversation slot (ARC-WORLD-043). `ME` reads the remembered slot. Facts are diagnostic (human NPC with the initial reaction as base; the session hero or a human PC). |
| LF, JO | Per speaker, map-local entity changes on the speaker's map: entity `oid:<object id SHA-256>`, properties `dialog.lf` (attachment flags), `follow.pc`. Since ARC-DEV-096 (MIG8, schema 66) `dialog.lf` is a field of the object itself (one record by its identity, kept in `objects.state`, read through `QueryObjectChange` wherever a script wrote it), `follow.pc` stays the map's. A value equal to the loaded baseline (attachment flags, not following) clears the key. A session holding values these ports cannot read (LF outside 32 bits, follow other than 1) is refused when the state is built. |
| AL, IN, MM | Still in memory on the state object and lost with it; PC alignment, items and known areas wait for their own owners. |

The SCR dispatcher's viewer host (ARC-SCRIPT-009) reads Following and LF from the same session, and also
stores the dialog slot's counters and number as `dialog.counters` and `dialog.script`, so Virgil's
follow consent picks his follower entry after a round trip to the cave or a restore. The
HUD status shows the session story (quests, rumors, followers). In the synthetic
self-test without area lighting the session clock is an invented synthetic profile.


Observed with the user-owned installation: Virgil (script 1324) enters at SCR line 4 → DLG
node 1; choosing the first Direct reply each time walks 13 NPC nodes
(1, 9, 15, 19, 24, 30, 32, 35, 57, 71, 120, 210, 74) with 16 applied effects
(GF×4, IN, QU, RU×2, RE, MM×5, JO, AL) and exits to SCR line 5. Talking again while he
follows enters at SCR line 6 → node 385. Since ARC-WORLD-016 the viewer talks to Virgil with the
session party port ([companion follow](../world/companion-follow-runtime.md#viewer-binding)): his `JO` joins the
session party, and node 385 runs its `WA` conditions and `UW 0 518` instead of stopping at a frontier;
the host without `party: true` (the test above) still stops there. Since ARC-SCRIPT-006
this holds after the session went to the cave and back and through a snapshot round trip,
with the four flags, one quest and two rumors restored. These are diagnostic runs, not
original gameplay verification.

## Verification

```sh
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogConversation|FullyQualifiedName~DialogReactionBinding"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillTests"   # ARC-SKILLS-004
DAT_ROOT=<installation> "$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillRealData"
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillPort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogScriptStoreTests"   # ARC-DEV-152
DAT_ROOT=<installation> "$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogScriptStoreRealData"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogQuestTests"   # ARC-DEV-180
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogQuestPort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogGoldTests"   # ARC-DEV-179
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogGoldPort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCombatTests"   # ARC-DEV-190
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaCombatConversation"
DAT_ROOT=<installation> [ARC_DIALOG_CENSUS_OUT=<file outside the repository>] "$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCodeCensusRealData"   # ARC-RES-092
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogScriptStorePort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaConversation"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaReactionConversation"
ARC_AREA_CONVERSATION_GAME_ROOT=<installation> "$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaConversation&TestCategory=RealData"
godot_canonical --path tools/terrain-viewer/Godot -- --self-test      # synthetic panel; AREA routing: off-NPC click, keys and polled levels, palette guard
ARC_AREA_CONVERSATION_PROBE=1 [ARC_AREA_CONVERSATION_GAME_ROOT=…] [ARC_AREA_CONVERSATION_SCREENSHOTS=<ignored dir>] \
  godot_canonical --path tools/terrain-viewer/Godot -- --self-test
```

The Godot self-test has no dialog NPC in its synthetic core: the positive NPC hit test and
reach are covered by the opt-in original host test (`SpeakerAt` on the real scene plan), and a
real mouse click on a reply is not automated. Synthetic fixtures are invented. The opt-in original test checks the dialog archive hash
before and after and asserts identifiers and command kinds only; screenshots of original
text stay in an ignored local directory. Ordinary `./arcanum area` launches the registered
ARC-RENDER-007 export, which predates this change until a new export is registered.

## Provenance

Source-backed observations are a reading of the original's functions at the cited addresses,
evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them: record fields (0x417860), reply enumeration and
gates (0x414E60, 0x414F50), conditions (0x4150D0; its `ME` branch checked in the executable, ledger DR1, its `GV`/`PV`/`PF` branches, DG1–DG2, and its `RU`/`RQ` branches, RU1–RU2; the reply gate by intelligence, IQ1), actions (0x415BA0; `GV`/`PV`/`PF`, DG3–DG4), the
insult (0x416C10, 0x419190, 0x4191E0; ledger DR4, DR5), NPC line, markers and
names (0x416840, 0x416AB0, 0x416B00), generated Player options (0x416C10, 0x417590; their phrases checked in the executable, ledger GP1–GP6), response and UI outcomes (0x567E30, 0x5680A0, 0x5681C0, 0x568430, 0x568480).
Strict grammar, group-order guard, look-ahead blocking, budgets, typed ports and the
diagnostic profile are project policy. Unknown: original parser edge cases, the
prefix markers' phrases, all other operations, reaction/alignment modifiers, item transfer outcomes,
follower rules and the SCR continuation after the dialog.

