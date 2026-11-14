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
   later rows are not queried** (source behavior; this deliberately differs from the retired RES-019
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
continuation (the NPC's last words are its race line 1000 with the greeting port, [below](#insult-line-arc-script-1505)), above 20 its transition follows (0x4191E0). Without a world port, or
with an action ending in `JO`/`UW`/`FL`/`ET` (the original then skips the insult, read for FL), it is offered blocked. `b h l p` (exact) and
`c z` (prefix) are offered blocked as
`UnsupportedInteraction`, and so is a prefix `t` without the [trainer ports](#trainer-reply-arc-script-1150) and a prefix `r` without the [rumor ports](#rumor-reply-arc-script-1304); with them `t:` is the trainer reply and `r:` the rumor reply; `q` with the [quest ports](#quest-state-reply-arc-script-1532) is the quest-state reply; `d:` and `x:` are the [directions replies](#directions-reply-arc-script-1533) with their ports and otherwise, like `u` and `q` without its ports
(state-dependent visibility), make the step a frontier. NPC `g:` (prefix) is the original's greeting when the conversation has a greeting port (ARC-WORLD-058,
[dialog greeting](dialog-greeting.md)), else a frontier; an exact `i:` is the [insulted line](#insult-line-arc-script-1505) with the greeting port, else a frontier; `m:`/`r:` (prefix) are the [NPC price and rumor lines](#npc-price-and-rumor-lines-arc-script-1535) with their ports, else frontiers. Other text is literal.

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
| Story and areas (story, area and map-area ports) | `SS n` as a Player condition (the signed rule on the game's story state) and as an NPC or Player action (the story state rises to n); `AR n` (n in ±1..4095: the PC knows area n, n < 0: does not know area −n) and `IA n` (the PC's map is in area n, n ≤ 0: not in area −n) as Player conditions |
| PC stats (stat port) | `CH n`, `PE n`, `LE n`, `AL n`, `MA n` (the signed rule on charisma, perception, level, alignment, aptitude), `TA n` (on the negated aptitude), `NA n` (alignment ≥ −n for n ≥ 0, ≤ n for n < 0) as Player conditions; n is signed, no `+` |
| Item holdings (item-hold port) | `IN n`, `NI n` as Player conditions (n ≥ 0: the PC or a follower holds an item named n; n < 0: the NPC or its substitute container holds an item named −n; `NI` the negation); n is signed, no `+`, not −2³¹ |
| Combat (combat port) | `CO` as an NPC or Player action (any number after it is read and ignored, as the original does): the attack event of the PC on the NPC runs once at the end of the action string ([below](#combat-action-arc-dev-190)) |
| Player action tail | one `JO 0 node` or `FL node` (with a party port also `UW 0 node`, with a skill port also `ET s node`) as the last operation (NPC node checked statically); after an `FL node` only a `CO` may follow (ARC-DEV-190). |

Unknown opcodes, `$` (other than the money code `$$` with a gold port, [ARC-DEV-179](#money-code-arc-dev-179)), combinators, `RE`/`AL` with `>`/`<`, non-negative `IN` as an action, `JO`/`UW` with a non-zero
first operand, `JO`/`UW`/`FL` in NPC actions or not last, `WA`/`UW` without a party port, and a non-space separator inside one
operation (`GF 5,1`, `qu(5,6)`: the source's whitespace-skipping `atoi` would read 0 there)
→ `UnsupportedOperation`; such forms are never reinterpreted. A missing operand (except the optional second operand of
`SK`/`TR`/`ET`/`LC` and the Player conditions `QU`/`QA`/`QB`/`SC`, which reads 0), missing separators, out-of-domain values, non-ASCII, `+` outside `RE`/`AL` actions,
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

The original matches 37 condition codes (0x4150D0, 0x5A06BC); this runtime parses 30 (`LF GF RE RA ME RP WA PS HA SK TR GV PV PF RU RQ $$ QU QA QB SS AR IA CH PE LE AL MA TA NA`) and, as actions, `CO` and `SS` besides the earlier ones (35 action codes in the original).
Any other code in a Player condition is `UnsupportedOperation`: the reply is offered blocked, and a reply group that needs it is a frontier
(`TargetUnsupported` on the reply that leads there). The owner's decision (2026-11-07) is that the remaining codes are implemented. The plan,
the slices and the table of codes are in [dialog-condition-codes-plan.md](../research/ledger/scripts.md). Slice C0 (ARC-RES-092) read
all 37 condition and 35 action codes in the executable ([DCC1–DAC12](../research/ledger/scripts.md#dialog-condition-and-action-codes-arc-res-092)) and counted
them over the 709 installed DLG files (the opt-in test `DialogCodeCensusRealDataTests`): 10,431 condition and action fields hold a code outside the
subset (10,384 stop first on one); admitting gold `$$` freed 2,857 of them (C1, ARC-DEV-179, below), the quest log `qu qa qb` freed 1,653 more (C2, ARC-DEV-180, [below](#quest-log-conditions-arc-dev-180)), the combat action `co` freed 1,325 more (C3, ARC-DEV-190, [below](#combat-action-arc-dev-190)), the story and area codes `ss ar ia` 1,051 more (C4, ARC-DEV-191, [below](#story-state-and-area-codes-arc-dev-191)), the PC stat conditions `ch pe le al ma ta na` 1,026 more (C5, ARC-DEV-192, [below](#pc-stat-conditions-arc-dev-192)) and the item conditions `in ni` 1,056 more (C6, ARC-DEV-194, [below](#item-conditions-arc-dev-194)); the plan orders slices C1 to C10 by it. Slices C7 to C10 are not built yet; 1,463 fields still hold a code outside the subset.

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
- **Hostile re-check.** After a string that returns 1 the original's reply step checks whether the NPC is hostile toward the PC before the transition (SU10, CA3); it is
  modelled behind its own port by [ARC-DEV-193](#hostile-check-after-a-reply-arc-dev-193), so a `CO` reply that goes on to a node (76 installed fields: `co` alone) ends the
  talk when the attack made the NPC kill-on-sight toward the PC. Replies that end in a final line (the rest) are not checked.
- **Installed data.** 1,329 Player action fields (203 files) hold `co`; 1,276 parse whole after the slice, 53 still stop (one at `co.fl n`, the others at another
  operation of the field). Both rule settings behave the same.

## Story state and area codes (ARC-DEV-191)

Three optional ports admit `SS`, `AR` and `IA`, the source's codes 27, 33 and 35 of the condition interpreter and 18 of the action interpreter
([SV1–SV6](../research/ledger/scripts.md#dialog-story-state-and-area-codes-arc-dev-191)). Without its port a code stays `UnsupportedOperation` as before.

- **`SS n`** (`IDialogStoryPort`). Condition: the signed rule on the story state, n ≥ 0 holds at state ≥ n, n < 0 at state ≤ −n. Action: the setter keeps the larger value, so
  `ss 3` after `ss 8` changes nothing and the port answers `Applied`; `Rejected` fails the conversation, a throwing port fails a condition (`QueryException`) or is recorded
  as indeterminate. `SessionDialogStoryPort` over `QueryStoryState` / `RaiseStoryState` (the state `get/set story state` of the SCR actions uses).
- **`AR n`** (`IDialogAreaPort`). n > 0: the PC knows area n; n ≤ 0: area −n is not known. An area outside 1..4095 (area 0 included) is valid data the session cannot hold
  and stays outside the subset. `SessionDialogAreaPort` over `IsAreaKnown`; since this slice the dialog `MM` action marks the area known in the session
  (`DiscoverArea`, the set `AR` reads and the world map shows) instead of only in the viewer's memory state.
- **`IA n`** (`IDialogMapAreaPort`). n > 0: the area of the PC's map equals n; n ≤ 0: it differs from −n. The AREA viewer answers from the shown map's use host
  (`AreaScriptHost.PcMapArea`, what get area stores for the PC, GA2) and offers the port only when it can answer when the talk starts (map mode); a port that answers
  `null` later fails the conversation (`InvalidQueryResult`).
- **Installed data.** `SS` 703 Player condition fields (40 files) and 102 action fields (61 in NPC lines), `AR` 37, `IA` 238; all parse but two `ss` fields at a form unrelated
  to the story state. Both rule settings behave the same.

## PC stat conditions (ARC-DEV-192)

With the optional `IDialogStatPort` (`DialogConversationPorts.Stats`) Player conditions admit `CH`, `PE`, `LE`, `AL`, `MA`, `TA` and `NA`, the source's condition codes 1, 2, 20, 3, 4, 5 and 32
([PQ1–PQ5](../research/ledger/scripts.md#dialog-pc-stat-conditions-arc-dev-192)). Without the port they stay `UnsupportedOperation` (the `AL` action needs no port and is unchanged).

- **Rules.** `CH`, `PE`, `LE`, `AL`, `MA` (stats 7, 5, 17, 19, 16): the signed rule, n ≥ 0 holds at value ≥ n, n < 0 at value ≤ −n. `TA` (stat 16): the same on the aptitude negated. `NA`
  (stat 19): n ≥ 0 holds at alignment ≥ −n, n < 0 at alignment ≤ n (not the signed rule; the installed pairs `na 799` / `na −800` split the scale cleanly, so it is the designed form).
  `int.MinValue` and a `+` sign are damage here.
- **Port.** `Stat(pc, stat number)` answers the PC's stat as the original's getter does (null when the owner does not hold the stat: the conversation fails with `InvalidQueryResult`; a
  throwing port fails it with `QueryException`). `SessionDialogStatPort` over `QueryHeroScriptStat` (strength to charisma and the aptitude with the life's poison and crippled
  legs, the stored level and alignment); the AREA viewer offers it only with a session hero (the diagnostic PC has no stats), like the skill gates.
- **Limits.** The getter's other effects (Tempus Fugit, the follower bonus, the background adjustments) are not modelled in the session (ledger row 0x4B0490), so a condition
  can differ from the original where they matter.
- **Installed data.** `CH` 478, `MA` 418, `TA` 88, `NA` 69, `LE` 44, `AL` 31 and `PE` 11 Player condition fields; all parse, none stops on these codes. Both rule settings behave
  the same.

## Hostile check after a reply (ARC-DEV-193)

With the optional `IDialogHostilePort` (`DialogConversationPorts.Hostile`) the reply step makes the check the original makes after a picked reply's action string
([HR1–HR4](../research/ledger/scripts.md#dialog-hostile-check-after-a-reply-arc-dev-193)). Without the port nothing is checked, as before.

- **When.** After the reply's action string ran (and its `CO` attack event, CA2) and before the insult and the transition. The original's action interpreter returns 0 when the
  string held `fl`, `jo`, `uw`, `et`, `ce`, `su`, `ii` or `ri` and 1 otherwise, and the reply step returns before the check on 0 (HR2): so the check is made for every reply
  whose transition is the next node, an exit or return to the script, a barter option or the single fallback exit, whatever its action, and skipped for the transitions
  `FinalLine`, `Join`, `ExpertTeacher` and `Schematics`.
- **What.** `Hostile(npc, pc)` answers whether the NPC is alive and the AI turns kill-on-sight toward the PC now (0x45D8D0, 0x4AE120; the will-kos script included in the
  original). True: the NPC speaks a generated line (race line group 1000, 0x4185F0) and the talk ends at once without the reply's own transition; the conversation ends as
  `Ended(HostileRefusal)` with no SCR continuation and the NPC's line is its race line 1000 ([below](#insult-line-arc-script-1505), ARC-SCRIPT-1505; without the greeting port the line stays the previous one). The insult
  of an `i:` reply comes after the check (DR5), so a hostile NPC ends the talk before the reaction change. A throwing port fails the conversation (`QueryException`).
- **AREA viewer.** `AreaConversationHost.NpcHostile` is set by the viewer to the shown map's AI host (`AreaAiHost.HostileToHero`: the speaker is a known critter that is not dead and
  `HostilityRules.KillOnSight` toward the hero is not `No`); like `CO` it needs the AI host (`HeroAttackAvailable`), without it the port is not offered. `Rebind` keeps it.
- **Not modelled.** The can-speak refusal of the pick (SU9), the stored response (state 3), the same check at the start of a talk (DR2: a talk with an NPC that is hostile at the
  start still opens its first line), the will-kos script (the viewer's AI host answers without it).

## Item conditions (ARC-DEV-194)

With the optional `IDialogItemHoldPort` (`DialogConversationPorts.ItemHolds`) Player conditions admit `IN` and `NI`, the source's condition codes 11 and 23
([IH1–IH4](../research/ledger/scripts.md#dialog-item-conditions-arc-dev-194)). Without the port they stay `UnsupportedOperation`. The `IN` action keeps its own rules (a negative operand through the inventory or
world port; the non-negative form is the item hand-over action below), and `NI` is no action.

- **Rules.** `IN n` with n ≥ 0 holds when the PC or, failing that, one of its followers holds an item whose name field equals n; `IN −n` holds when the NPC or, failing that, its substitute container
  holds an item named n. `NI` is the exact negation of both (`NI −n` with no substitute container holds when the NPC does not hold the item). A field is a conjunction, as for every condition.
  `int.MinValue` and a `+` sign are damage here.
- **Port.** `PartyHolds(query)` and `NpcHolds(query)` (the NPC, the PC and the item name) answer bool; a throwing port fails the conversation (`QueryException`). `SessionDialogItemHoldPort` over the hero's lists (starting kit,
  taken items, made items), `GameSessionOwner.QueryHolderItems` (an NPC's or container's current list, a carried follower's home map attached first) and the viewer's substitute container
  (`AreaInventoryHost.SubstituteContainer`: BT11, within 20 tiles of the NPC's map row, no leader). The AREA viewer offers the port with a session hero and the inventory wiring.
- **Limits.** The hero's coins are a count in the session, not a listed item, so a name only a gold pile carries is not found for the hero; the substitute container is looked up from the NPC's map
  row, not from where the AI has walked it; an NPC condition field holds neither code in the installed data, and only Player conditions admit them here.
- **Installed data.** `IN` 671 and `NI` 410 Player condition fields; all parse, none stops on these codes. Both rule settings behave the same.

## Item hand-over action (ARC-DEV-199)

With the optional `IDialogItemTakePort` (`DialogConversationPorts.ItemTake`) a reply's or an NPC line's action admits the non-negative `IN` (the source's action `in`, case 9 of its action table, the PC
side of [DAC5 and TK1–TK4](../research/ledger/scripts.md#dialog-item-hand-over-arc-dev-199)): the hero hands the first item whose name field equals n to the NPC. The receipt is a `TakeItemCommand` (the NPC,
the PC and the item name) recorded in field order, so the field's other actions run before and after it. Without the port the non-negative form stays `UnsupportedOperation`; the negative form keeps its own rules
(the inventory or world port, `GiveItemCommand`, the NPC gives to the PC) and needs no `ItemTake` port.

- **Port.** `Execute(TakeItemCommand)` answers Applied or Rejected; a rejection fails the conversation (`CommandRejected`) and a throwing port is `CommandIndeterminate`. `SessionDialogItemTakePort` finds the
  first hero item of that name (starting kit, then taken items, then made items) and moves it with `GameSessionOwner.GiveItemToNpc` and the host's insert check; the viewer accepts every item for the NPC
  (`AreaInventoryHost.TakeItemDialogPort`, wired with a session hero and the inventory wiring).
- **Nobody holds it.** The action does nothing and is Applied, as in the source (an item the story promised, or one the PC already gave). A follower holding it (and the PC not) is Rejected, because the
  session has no move from one NPC's list to another's; when the NPC is itself the first follower holding it, nothing moves and the result is Applied.
- **Refused hand-over (ARC-DEV-238).** When the host's insert check refuses the move, the source's fallback runs (TK2, in both rule modes): an NPC the PC leads has the item laid on its cell, any other NPC destroys it (`GameSessionOwner.SettleRefusedHandover`); both are Applied. A leader cell the host cannot give, or an item whose removal needs an event the session does not run, is Rejected. The viewer's NPC insert check accepts everything, so its talks do not reach it.
- **Limits.** The three hero lists
  stand for the original's single one, so the order of the search is an approximation. A refusal of the move (an unknown NPC, a script or item effect the session cannot run, a full list) is a
  conversation failure instead of the item vanishing.
- **Installed data.** The `in` action appears in 383 Player and 223 NPC action fields; all parse, none stops on it.

## Companion conditions and actions (ARC-DEV-195)

With the optional `IDialogCompanionPort` (`DialogConversationPorts.Companion`) Player conditions admit `FO` and `WT`, and an action admits `WA`, `LV`, `SO` and `SC` (in a reply or in an NPC line)
([CF1–CF7](../research/ledger/scripts.md#dialog-companion-state-and-actions-arc-dev-195)). Without the port they stay `UnsupportedOperation`; the `WA` condition keeps its own port (the party
port, `IsWaiting`) and `WT` is the only one here without an installed field.

- **Conditions.** `FO 1` holds when the PC is the NPC's leader (also while it waits), `FO 0` when it is not; `WT 1` when the NPC is jilted, `WT 0` when it is not. Another operand is outside the subset
  (the source passes it untested), a `+` sign is damage; a field is a conjunction, as for every condition. A throwing port fails the conversation (`QueryException`).
- **Actions.** `WA` makes a following NPC wait, `LV` forces it out of the party (not jilted), `SO` and `SC` set and clear its spread-out flag (`SO` that newly sets the flag also starts the spread-out goal 69 toward the hero on a follower the shown map's AI host runs, as the original does right after the bit through 0x436960: the port's `spreadOutStarted` callback, the viewer's `SpreadOutStart` hook; a flag already set starts nothing; ARC-SCRIPT-1135, CF4, CF6). They take no operand: a number after the code is outside
  the subset (the rule of `FP`). Each is recorded as a `CompanionCommand` in field order; none ends the string early, so the reply's hostile check still follows (HR2). A rejection fails the
  conversation (`CommandRejected`), a thrown exception is `CommandIndeterminate`.
- **Session.** `SessionDialogCompanionPort` over `GameSessionOwner`: `QueryCompanion` (`LedByPc`, `Jilted`), `WaitParty` with the host's wait timer facts, `LeaveParty` with `Force`, `SetSpreadOut`. Waiting
  while already waiting (the bit set), leaving while not in the party and a flag already in the asked state are Applied with no change. `WA` on an NPC that is not in the party sets the wait flag on its free record (ARC-SCRIPT-1136, CF8 to CF10, schema 96): the NPC stays free and the timer is the one of its wait index; the `WA 0/1` condition reads that bit alone (`SessionDialogPartyPort.IsWaiting`).
- **Viewer.** The AREA conversation host passes the port for every speaker, also where the party port is not wired (the session party needs no hero; there `fo` reads the session record, which is free, and `wa` is refused); a wait gets the roster's timer facts (`AreaCompanionRoster.WaitFacts`) for a roster
  companion and none for another speaker.
- **Installed data.** `FO` appears in 2 Player condition fields, `WT` in none; `WA` 49, `LV` 83, `SO` 47 and `SC` 49 action fields; none stops outside the subset. Both rule settings behave the same.

## NPC counters (ARC-DEV-196)

With the optional `IDialogCounterPort` (`DialogConversationPorts.Counters`) Player conditions and actions admit `LC i v` ([KC1–KC4](../research/ledger/scripts.md#dialog-npc-counters-arc-dev-196)). Without the port it stays
`UnsupportedOperation`.

- **Rules.** The NPC's dialog attachment holds four byte counters. As a condition `LC i v` holds when counter i equals v (so a v outside 0..255 never holds); as an action it stores the low eight bits of v
  in counter i, keeping the others. The index wraps onto counter i modulo 4 as the source's shift count does (index 4 is counter 0, a negative index counts from the end; ARC-DEV-228); a second operand left out reads 0; a `+` sign is damage. A field is a
  conjunction; the action is recorded as a `CounterWriteCommand` in field order and does not end the string.
- **Port.** `Read(query)` answers 0..255 (anything else is `InvalidQueryResult`; a throwing query is `QueryException`); `Execute(command)` as for every action port. The AREA conversation state is the port: its
  counters are the speaker's dialog counters in the session (`AreaScriptHost.CountersProperty`, beside the local flags), the loaded attachment's when the session holds none; a value equal to the loaded
  one clears the session entry. The script host reads the same property back, and a DLG started by a script at another point that holds `LC` is refused at the start like one holding `LF`.
- **Installed data.** `LC` appears in 315 Player condition fields and 213 action fields; all of them parse, among them 21 with the index 4 and one with a missing second operand (these 22 stopped before ARC-DEV-228).

## Spell college condition (ARC-DEV-197)

With the optional `IDialogCollegePort` (`DialogConversationPorts.Colleges`) Player conditions admit `SC n v` ([CK1–CK3](../research/ledger/scripts.md#dialog-spell-college-rank-arc-dev-197)); without it the condition stays
`UnsupportedOperation`. `SC` as an action is the companion port's (spread-out clear) and is not touched.

- **Rule.** The PC's rank in college n (0..5) against v: above 0 the rank must be at least v, at 0 or below at most −v, so `SC n 0` holds only at rank 0 (unlike the stat conditions, whose threshold 0 always holds).
  The second operand is optional and reads 0. A college beyond 0..15 is outside the subset; a negative college, a `+` sign and the most negative number are damage.
- **Port.** `Rank(query)` answers the rank or null (then `InvalidQueryResult`; a throwing port is `QueryException`). `SessionDialogCollegePort` reads the session hero's spell book (`QueryHeroSpellBook`); the AREA
  conversation host offers it with a session hero.
- **Installed data.** `SC` appears in 158 Player condition fields; all parse. Both rule settings behave the same.

## Follower name condition and kill action (ARC-DEV-227)

With the optional `IDialogFollowerNamePort` (`DialogConversationPorts.FollowerNames`) Player conditions admit `PA n` and with the optional `IDialogKillPort` (`DialogConversationPorts.Kill`) the actions admit `NK`
([PN1–PN5](../research/ledger/scripts.md#dialog-follower-name-and-kill-arc-dev-227)); without a port the code is `UnsupportedOperation`.

- **`PA n`.** Holds when some direct follower of the PC that is not mind-controlled has the name number |n|; a negative n holds when none has (`-0` is 0). The operand is required, a `+` sign and the most
  negative number are damage. `HasFollowerNamed(query)` answers the whole search; a throwing port is `QueryException`. `SessionDialogFollowerNamePort` walks `QueryParty().Followers` and asks the host for each follower's
  name number and mind-controlled flag, stopping at the first match; the viewer reads them from the stored record (`AreaInventoryHost.FollowerNameFacts`), and a follower it cannot read throws.
- **`NK`.** A `KillNpcCommand` on the dialog's NPC, in field order, in replies and NPC lines; the talk is not ended and the later fields run. No operand (a number after it is outside the subset). `Applied` or `Rejected`
  as for every action port (a refusal fails the conversation). The viewer's kill is the script kill's (`TerrainViewerAi.KillByScript`).
- **Installed data.** `PA` appears in 366 Player condition fields, `NK` in 9 action fields; all parse. Both rule settings behave the same.

## Window actions (ARC-DEV-229)

With the optional `IDialogWindowPort` (`DialogConversationPorts.Windows`) a Player reply's action admits the action spelled c-e (the dialog NPC's character sheet), `II` and `RI`
([WN1–WN4](../research/ledger/scripts.md#dialog-window-actions-arc-dev-229)); each is outside the subset (the reply is offered blocked) unless the port `Offers` its window.

- **Rule.** As `SU`: every action of the field runs in order, the reply's target node is never entered, the string returns 0 (no hostile check), and the talk ends for the window of the last window action of the field:
  `DialogEndKind.NpcSheetWindow`, `ItemIdentifyWindow` or `ItemRepairWindow` (reply transitions `NpcSheet`, `ItemIdentify`, `ItemRepair`). A number after the code is read and ignored. They are Player reply actions
  only (no NPC line, no condition). A dialog transfer (JO, UW, FL, ET) or an insult in the same reply is offered blocked (`UnsupportedInteraction`).
- **Port.** `Execute(OpenWindowCommand)`: Applied means the host opens the window once the talk ended; Rejected fails the conversation; a thrown exception is indeterminate.
- **Viewer.** The AREA host passes a window port that offers all three windows: the NPC sheet (ARC-UI-088: the talk ends and the speaker's character sheet opens read-only, ledger CHW24) and, since ARC-UI-1731, the item identify and repair windows (the host names the window and the speaker with `TakeItemServiceRequest`; the last window action of a reply decides, an earlier one is dropped; [item service runtime](../world/item-service-runtime.md)).
- **Installed data.** The c-e action appears in 47 Player reply fields, `II` in 26 and `RI` in 61 (132 fields); all parse with every port.

## Newspaper and origin actions (ARC-DEV-230)

With the optional `IDialogNewspaperPort` (`Newspaper`) and `IDialogOriginPort` (`Origins`) the actions `NP` and `OR` are admitted in NPC lines and Player replies, never as conditions
([NQ1–NQ8](../research/ledger/scripts.md#dialog-newspaper-queue-and-npc-origin-arc-dev-230)); without their port they stay outside the subset.

- **`np`.** The first operand is the issue and the second the front flag (0 when missing, any other value, also a negative one, is true); a missing first operand is 0 (an installed dialog writes a bare `np`);
  the command is `QueueNewspaperCommand(Issue, Front)`. The queue rule is the owner's (session `QueueNewspaper`, 0x4BF140); an issue that is already queued is its no-change outcome, still Applied.
- **`or`.** The first operand is read as it is (any 32-bit number); a second one is not read, so a number after it is outside the subset. The command is `SetOriginCommand(Npc, PlayerCharacter, Origin)`.
- **Neither clears the string's result**, so the reply goes on to its target node and the hostile check; a refusing port fails the conversation, a throwing one is indeterminate.
- **Viewer.** `np` goes to the session's newspaper queue (schema 74, `newspaper` section; the daily pop and the vendor's printed issue are [below](#newspaper-vendor-reply-arc-ui-1739)). `or` sets the session object field
  `npc.origin` of the speaker (cleared when it equals the map data's origin), which `AreaConversationHost.Scope` reads next to the faction, and the reputation scope of the talk in progress follows.
- **Installed data.** `np` appears in 21 fields (17 Player replies, 4 NPC lines), `or` in 4 NPC lines; all parse. No code of the tables is outside the subset any more.

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

An exact `l:` reply is the world-map reply (state 30, ledger [VA1–VA5](../research/ledger/scripts.md#virgils-altar-talk-and-the-world-map-reply-l-arc-script-1358), ARC-SCRIPT-1358): with `IDialogWorldMapPort` (the viewer's map mode) its pick runs the reply's action and ends the talk with `DialogEndKind.WorldMapWindow` (transition `WorldMap`, no script continuation), and the host opens the world map as the Map button does; without the port it stays blocked.
| `a:` `i:` | PC class lines from 1000 and 3000 (the insult, 0x419190) + 50 × the NPC's social class, 50 entries |
| no reply qualifies | PC lines 400–499 (0x414E60) |

A PC line (0x4182D0) reads `gd_pc2m` or `gd_pc2f` by the NPC's gender, a PC class line (0x418390) `gd_cls_pc2m` or `gd_cls_pc2f`;
a dumb PC (its intelligence as the reply gates read it at most 4, the original's 0x45FC00) reads their dumb variants, the class
offset still applied. Without the port, or when its facts are unknown, the text stays empty and the reply works as before; a refused
roll, a port exception or an undecodable phrase fails the conversation (`InvalidQueryResult`, `QueryException`, `InvalidText`). The
phrase changes only the text: the transitions and blocks above stay. The `t:` phrase (PC lines 500..599) is built (ARC-SCRIPT-1150); its argument is
stored after the phrase, not appended to it (ledger DTR1). The `r:` phrase (the PC's class line 2000) is built too (ARC-SCRIPT-1304, ledger RMR1). The `d:` (PC lines 1300..1399) and `x:` (1400..1499) phrases are built (ARC-SCRIPT-1533, ledger DIR1). The `u:N` (PC lines 900..999 with the skill's name) and `z:N` (1000..1099 with the spell's name) phrases are built (ARC-SCRIPT-1798, ledger DSV1, DSV3).

## Skill service reply `u:` (ARC-SCRIPT-1534)

A Player reply whose text starts with `u:` (either case) is the original's **skill service reply** (0x416C10; [ledger DSV1–DSV3](../research/ledger/social.md#skill-service-reply-u-arc-script-1534)): the number after the marker (read like the
C library's atoi) is a skill the NPC would use on the PC. The original asks the skill test 0x4AE570 whether the NPC can, and leaves the reply out when it cannot (the five-reply count still includes it, as for a directions reply with nothing to ask for).

- **Skills 12 (repair) and 14 (pick locks) are built as the original behaves: never offered.** Their cases of the test want the target to be an item (12) or a portal or container (14), and the dialog hands it the PC (a critter), so the reply
  never shows, whatever the NPC and the data: the 8 installed `u: 12` replies (3 files) are dead data in both rule settings, and the repair is reached through the `ri` action ([item services](../world/item-service-runtime.md)). Such a reply is planned
  as a left-out reply (`DialogConversation.IsUnreachableServiceReply`, tests `DialogServiceReplyTests`, `DialogServiceOfferTests`).
- **Skill 10 (heal) is offered when the NPC may heal the PC** (the host's `DialogHealerFacts.SkillAllowed` for skill 10: a healing item in the NPC's pack and a healer that is neither undead nor mechanical); **any other number passes the test** and is offered (a text whose atoi is 0 is skill 0).
  The option text is the PC line 900..999 with the skill's name (empty for a number without one). Both need the healer menu's ports (`Healer`, `Greeting`, `World`, `Gold` and `Purse` or `Trainer`); without them the reply stays a frontier, as before.
- **The pick** runs the reply's action, then offers the skill as the healer menu's option does (ledger HLR7, HLR9, HLR10; states 0xE then 0x10): a follower of the PC (its leader is the PC) is served at once and free, anyone else is quoted `(level + 2) × (tier + 1)` of the NPC's *named* skill on the reaction scale, paid
  and then served by the NPC's skill goal 33 on the PC (tool: the healing item for skill 10, none for other skills, 0x4C91F0). The host starts the goal for any of the 16 skills; the attempt goal of a skill other than heal and pick pocket is not built in the AI host (`AttemptSkill`: nothing happens and the goal ends), so a
  `u:N` other than 10 and 5 (pick pocket: the NPC tries to steal the PC's gold) pays and does nothing yet (no installed reply has one). A number outside the 16 skills starts nothing.
- **`z:N` (a spell sold for gold)** has no filter: the option text is the PC line 1000..1099 with the spell's name, the pick sells spell N for 100 on the reaction scale (free for a follower), and the NPC casts it on the PC (ledger HLR8, HLR11, DSV3). No installed reply uses it. Without the healer menu's ports it stays an offered blocked reply.

## Trainer reply (ARC-SCRIPT-1150)

With the optional `IDialogTrainerPort` (`DialogConversationPorts.Trainer`: the skill names and the hero's own gold), the world port (the price's
reaction), the skill port (the training) and the gold port (the payment), a Player reply whose text starts with `t:` (either case) is the original's
**trainer reply** ([ledger DTR1–DTR9](../research/ledger/social.md#trainer-reply-arc-script-1150)). It is offered as `Generated` (marker `t`, the
phrase PC line 500..599 through the greeting port) with the transition `DialogOptionTransition.Trainer`; the numbers after the marker (comma-separated,
`a-b` ranges, read like the original's list reader 0x417F90, `DialogConversation.ParseTrainerSkills`) are the skills it teaches: 0..11 basic, 12..15 tech.
A reply with a number that is no skill, with more numbers than four (five options hold the skills and the way out), with a dialog transfer or window
action (`JO`, `UW`, `FL`, `ET`, `SU`, ...) or without one of the ports stays blocked (`UnsupportedInteraction`) as before.

Picking it runs the reply's action (the hostile check too), then opens the **talk** without leaving the node: `NpcLine` and `Options` are replaced
screen by screen (`Turn` and `CurrentNode` stay; `RefreshOptions` rebuilds a screen with the texts it showed, `Close` ends the dialog):

| Screen | NPC line | Options |
| --- | --- | --- |
| Menu (0x418CA0) | class line 3000 | one per listed skill (its `skill.mes` name; `TrainerStep`), then the way out: PC line 800..899 with the reply's own transition |
| Already trained (0x418C40) | class line 4000 | PC line 600..699, the reply's own transition |
| Cannot be taught | class line 5000 | as above; the setter refuses the first tier below effective level 1 (ledger SR1), asked through `IDialogSkillPort.Level` without a write |
| Price (0x418A40) | class line 1000 with its `%d` = 100 on the reaction scale (0x4C1150: 200 below reaction 0, 200 − 2r below 50, else 120 − 2r/5, r at most 100; a price of 1 is 2) | PC line 1..99 (pay) and 100..199 (the reply's own transition) |
| No gold (0x418B30) | class line 2000 when the hero's own gold (`IDialogTrainerPort.PcGold`, not the followers') is below the price | as the first notice |
| Trained (0x418F30) | class line 6000 after the hero paid the NPC (`GoldCommand` of −price through the gold port) and the first tier was set (`SkillTrainingCommand` tier 1 through the skill port) | the PC class line 1000 (+ 50 × the NPC's social class), the reply's own transition |

"The reply's own transition" is the reply's response decoded as for any reply: a positive value continues at that node (the reply's action is not run
again), zero ends the dialog at the caller's next line, a negative value ends it at that SCR line. The lines come from the greeting port's generated-dialog
files like the greeting's (the class group + 50 × the NPC's social class, the dumb files, the override line 9999 + group / 1000); without the port, or when
it does not know the facts, a line is empty and the talk works. The talk reads the same `Phrase` roll as a Player phrase for PC lines and the `Line` roll
for NPC lines. Receipts: the payment and the training are effects of the turn (a refusal fails the conversation with `CommandRejected`; the budget of
two effects is checked before the payment). The Educator update after a training (ledger SR11) and the debug prefix are not built.

Session seam: `SessionDialogTrainerPort` over `GameSessionOwner.QueryGold` (the hero's own stack only) with the host's skill names; a PC other than the
session's or a session without a hero throws on a read (a query failure). The AREA host builds it when the session has a hero and the module's
`mes\skill.mes` loaded with the greeting texts (`AreaGreetingTexts.Skills`, decoded with the hover text profile); otherwise the reply stays blocked.
Tests: `DialogTrainerTests` (synthetic), `DialogTrainerPortTests`, and the opt-in `DialogTrainerRealDataTests` (`DAT_ROOT`: all 315 installed `t:` replies
in 58 files are admitted, the files hold groups 1000..6000 and the PC ranges).

## Rumor reply (ARC-SCRIPT-1304)

With the rumor port (`DialogConversationPorts.Rumors`, the known and quelled rumors) and the greeting port (the lines, the rumor texts and the draw), and
for a price also the world port (the reaction), the gold port (the payment) and a purse (`DialogConversationPorts.Purse`, or the trainer port, which answers
the same own-gold read), a Player reply whose text starts with `r:` (either case) is the original's **rumor reply**
([ledger RMR1–RMR9](../research/ledger/social.md#rumor-reply-arc-script-1304)). It is offered as `Generated` (marker `r`, the phrase the PC's class line
2000 through the greeting port) with the transition `DialogOptionTransition.Rumor`; the text after the marker reads as `$price,list`
(`DialogConversation.ParseRumorReply`: the price after the first `$`, the rumors of the list after the next comma, comma-separated numbers or `a-b`
ranges as in the trainer's list; parentheses and spaces around the numbers do not matter, the installed replies use them). A reply without a `$` or the
comma, with a rumor outside 1000..2999 (the session's rumors), with a dialog transfer or window action in the same reply, or without one of the ports
stays blocked (`UnsupportedInteraction`) as before. A free reply (price below 1) needs no money port.

Picking it runs the reply's action (the hostile check too), then the talk, in the screens of the [trainer's talk](#trainer-reply-arc-script-1150)
(`TrainerStep` becomes `RumorStep` for the price's yes):

| Step | What happens |
| --- | --- |
| Draw (0x418FC0) | The listed rumors that are not quelled (`IDialogRumorPort.IsQuelled`) and not known by the PC (`IsKnown`) stay, in order and with repeats; one is drawn by the `Rumor` roll of the greeting port (`0..count−1`; a single candidate draws nothing). A roll the port refuses or answers out of range fails the conversation (`InvalidQueryResult`), a throwing port is a `QueryException` |
| Nothing left | the NPC's class line 7000 and the PC line 600..699, the reply's own transition; nothing is paid or written |
| Price below 1 | the tale at once |
| Price | the price screen of the trainer's talk with the reply's price on the reaction scale; no leaves by the reply's own transition; yes, with enough own gold, pays the NPC (`GoldCommand` of −price) and tells; too little gold is the class line 2000 and changes nothing |
| Tale (0x4190E0) | the NPC's line is the rumor's text (`DialogGreeting.RumorTale`: the rumor file of the NPC's and the PC's gender, message 20 × number + social class + 1, else 20 × number + 1, else empty; the names substituted; origin `DialogLineOriginKind.Rumor`, no voice); the rumor is written to the PC's logbook (`RumorLogCommand`, a known rumor stays); the only answer is the PC class line 1000 with the reply's own transition |

Receipts: the payment and the logbook write are effects of the turn (a refusal fails the conversation with `CommandRejected`; the budget of two effects is
checked before the payment, one before a free write). The rumor files come with the greeting sources (`DialogGreetingSources.RumorLines`, the four files of
`RumorSpeechTexts` in the original's order); without them the text is empty and the rumor is still learned (the original reads an absent message as empty).
The seams: `SessionDialogPursePort` (the hero's own gold, as the trainer port reads it), the AREA host's rumor state (`AreaConversationState`) and
`AreaGreetingTexts.RumorLines` (loaded with the greeting texts; a file that is absent or unusable leaves its rumors without a text, the count is in `Detail`).
An NPC node whose text starts with `r:` stays a frontier (slice G1). Tests: `DialogRumorTests` (synthetic), `DialogTrainerPortTests` (the purse), and the
opt-in `DialogRumorRealDataTests` (`DAT_ROOT`: all 239 installed `r:` replies in 32 files are admitted; an installed rumor talk runs to the logbook).

## Story reply (ARC-SCRIPT-1321)

With the story port (`DialogConversationPorts.Story`, the same `IDialogStoryPort` the `SS` code reads) and the greeting port (the lines and files), a Player reply whose
text starts with `c:` (either case; the rest of the text is not read) is the original's **story reply**
([ledger STY1–STY4](../research/ledger/social.md#story-reply-arc-script-1321)). It is offered as `Generated` (marker `c`) with the transition
`DialogOptionTransition.Story`; its phrase is the single PC line numbered 1700 + the story state (`DialogGreeting.Phrase` with the state, built when the options are
built). A reply with a dialog transfer or window action in the same reply, or without one of the two ports, stays blocked (`UnsupportedInteraction`) as before. The AREA
host's conversation passes both ports, so the viewer offers the reply.

Picking it runs the reply's action (the hostile check too), then, in the screen of the [trainer's talk](#trainer-reply-arc-script-1150):

| Step | What happens |
| --- | --- |
| Story state | read again after the action, so the line is the state the action left (the phrase was built before it); a throwing port is `QueryException`, a state outside 0..1,000,000 (the session never holds one) is `InvalidQueryResult` |
| Line (0x41A440) | `DialogGreeting.StoryTale`: the NPC's own override record 10000 + the state (male-PC text, alternate text for a PC that is not male); else the story file by the NPC's gender then the PC's (`GeneratedDialogFiles.StoryLineFile`: m2m, m2f, f2f, f2m), the entry numbered the NPC's race + 100 × the state, fetched by number (no draw), the names substituted; an entry the file lacks reads as the missing-line text (origin `MissingLine`), a file the module lacks as an empty line |
| Answer | the PC's class line 1000 is the only option, offered with the reply's own transition (`Direct`, `ScriptLine` or the return to the script); the action does not run again |

Receipts: the action's effects are recorded as for any reply; the story talk writes nothing. A rebuild keeps the screen and a stale token is `NotOffered`, as for the other
talks. Tests: `DialogStoryLineTests` (synthetic) and the opt-in `DialogStoryLineRealDataTests` (`DAT_ROOT`: all 212 installed `c:` replies in 41 files are admitted; an
installed story talk tells an installed line).

## Healer reply (ARC-SCRIPT-1732)

With the healer port (`DialogConversationPorts.Healer`, `IDialogHealerPort`) and the greeting, world, gold and purse (or trainer) ports, a Player reply whose whole text is `h:` (either
case) is the original's **healer reply** ([ledger HLR1–HLR12](../research/ledger/social.md#healer-reply-arc-script-1732)). It is offered as `Generated` (marker `h`) with the transition
`DialogOptionTransition.Healer`; its phrase is the PC line 700..799 of the generated phrases (ARC-SCRIPT-043). A reply with a dialog transfer or window action in the same reply, or without
one of the ports, stays blocked (`UnsupportedInteraction`) as before (none of the installed 52 has an action). The reply's own response is followed when the player leaves the talk
(the way out), as for the [trainer's talk](#trainer-reply-arc-script-1150), whose screens the talk reuses.

Picking it runs the reply's action (the hostile check too), then:

| Step | What happens |
| --- | --- |
| Menu (0x419830) | the NPC's class line 14000; the host's facts (`DialogHealerFacts`: the NPC's rank in college 12, whether its skill service is allowed, its heal skill's level and tier, its leaders) set the options in the original's order: the skill service (PC line 900..999 with the skill's name), a first submenu (rank above 0), a second (rank above 1), the spell 64 (rank exactly 5), the way out (PC line 800..899); a throwing port is `QueryException`, a rank outside 0..5 `InvalidQueryResult` |
| Submenus | the same NPC line; the first holds the spells 60 (rank above 0) and 62 (rank above 2), the second the spell 61 (rank above 1), each the PC line 1000..1099 with the spell's name; both end with the way out |
| Offer | an NPC whose own leader is the PC serves at once and free; any other quotes (level + 2) × (tier + 1) for the skill or 100 for a spell on the reaction scale (`ReactionPrice`, the NPC class line 1000 with the price) and asks yes (PC line 1..99) or no (100..199, the way out) |
| Payment | the PC's own gold must cover the price (else the notice 2000); the effect budget is checked before it; the gold goes to the NPC through the gold port, then the effect runs |
| Effect | `HealerSkillCommand` (the heal skill 10, flag `NoPcLeader`, which the AREA host passes to the skill starter as the start flag 0x2000; no skill goal reads it, so it changes no run, ledger HLR10) or `HealerSpellCommand` (the spell, `Free` when no PC leads the NPC) through the healer port, recorded as an effect; `Rejected` fails the conversation (`CommandRejected`); then the NPC's line 15000 and the PC's class line 1000 as the only way on, which follows the reply's own transition |

The AREA host passes the port when the viewer gives it the shown map's AI host (`AreaConversationHost.HealerHost`) and the speaker is an NPC or a companion that host runs (ARC-SCRIPT-1996: a follower is in the host's follower list, not its NPC list), the module's skill texts have loaded
and the session has a hero; the spell names come from the viewer's loaded spell data (`SpellName`). The port (`AreaConversationHost.HealerPort`) reads the facts from `AreaAiHost.HealerFacts`
(the NPC's college rank from the host's spell facts, the heal tool and test the AI heal already uses, its heal skill's level and tier, its leaders) and starts the effects through
`AreaAiHost.HealerService` (the skill starter's goal 33 on the hero) and `AreaAiHost.HealerCast` (the script cast entry with the NPC as the caster). Tests: `DialogHealerTests` (synthetic),
`AreaAiSkillTests`, `AreaAiFollowerTests` (a companion's facts, service and spell), `AreaHealerCastTests` (the cast host) and `AreaHealerConversationTests` (the host side), the smoke stage `healer-talk` (`./arcanum smoke pr --stages healer-talk`, Crash Site, ARC-SCRIPT-1790) and the opt-in `DialogHealerRealDataTests` (`DAT_ROOT`: the census of the 52 installed `h:` replies in 15 files, the lines the talk
reads and an installed healer's talk to the spell).

## Directions reply (ARC-SCRIPT-1533)

With the optional `IDialogDirectionsPort` (`DialogConversationPorts.Directions`: the area names and the bearing from the NPC to an area) and the known-area (`Areas`) and greeting ports, a
Player reply whose text starts with `d:` or `x:` (either case; `d:$price,areas`) is the original's **directions reply** ([ledger DIR1–DIR9](../research/ledger/social.md#directions-replies-arc-script-1533)).
A reply with a price also needs the `World`, `Gold` and purse ports (the price on the reaction scale, the payment), `x:` the `World` port (the area marking); without them, or with an area outside
1..4095, no `$` or no comma after it, it stays a frontier (`UnsupportedGeneratedText`) as before. It is offered as `Generated` (marker `d` or `x`, PC lines 1300..1399 or 1400..1499, the arguments
not shown) with the transition `Directions`, **unless the PC knows every area of its list** (`IDialogAreaPort`), when it is left out; a left-out reply still takes a slot of the five. A dialog
transfer or window action in the same reply keeps it blocked (`UnsupportedInteraction`), and a blocked reply is left out the same way. Picking it runs the reply's action, then the talk, in the screens of the [trainer's talk](#trainer-reply-arc-script-1150)
(`TrainerTalk`; the options are `DirectionsStep`, the reply's own response is followed on leaving):

| Screen | NPC line | Options |
| --- | --- | --- |
| Page (0x419E70) | NPC line 100..199 (`x:` 500..599, 0x418780) | up to four areas still unknown to the PC, each named by the area table; then the more option (PC line 600..699, the next page) while areas remain, else the way out (PC line 800..899, the reply's response) |
| Price (0x41A0F0, 0x41A230 → 0x418A40) | class line 1000 with the price on the reaction scale | yes (PC 1..99) pays, no (PC 100..199) leaves; no screen for a price below 1, and a follower of the PC (`NpcLedByPc`) is never charged |
| Way (0x41A150, 0x41A290) | `d:`: line 200 / 300 / 400 by the distance (under 2 / under 100 / more units of 3168 tiles) + 10 per direction (0x4B8D50, 0x4B96F0); `x:`: 600 (under 2) or 700 + 10 per direction, the units printed into the line from 2 on | the PC's class line 1000 only; `x:` then marks the area known (`MarkAreaCommand`, as `MM`), **`d:` does not** (ledger DIR5, DIR6) |

The way is built before the payment, so a failed line never follows a spent price; the effect budget covers the payment and the marking together. A refused or throwing port, an area the port cannot
name, a bearing outside the eight directions fail the talk (`InvalidQueryResult`, `QueryException`). The viewer's host (`AreaDialogDirections`) names the areas from `mes\gamearea.mes` (`WorldAreaTable`,
loaded with the greeting texts) and measures from the speaker's location (a following companion's live cell, else its T0 cell: another NPC the AI host has moved is measured from where the map data puts it, a limit of the talk range too, [#1601](https://github.com/Simarglok/arcanum-steamwoven/issues/1601)) with `LineOfFireRules.Rotation` and `WorldTravelGeometry.Distance`.
Tests: `DialogDirectionsTests` (synthetic), `AreaDialogDirectionsTests` (the host's seam) and the opt-in `DialogDirectionsRealDataTests` (`DAT_ROOT`: the two installed `d:` replies are admitted and run
to their way; no installed `x:`).

## Quest-state reply (ARC-SCRIPT-1532)

With the quest port (`IDialogQuestPort`, the PC's quest state), the world port (the NPC's reaction) and the quest rules port (`DialogConversationPorts.QuestReplies`,
`IDialogQuestReplyPort`: the module's `rules\gamequest.mes` numbers, read by `QuestReplyTable`), a Player reply whose text is `q:` (either case) and a quest number 1000..1999
is the original's **quest-state reply** ([ledger DQR1–DQR5](../research/ledger/social.md#quest-state-reply-arc-script-1532)). The reply is planned with its own gates only (gender,
IQ, condition); which record it shows depends on the PC's state, so it is chosen when the offers are built (`ResolveQuestReply`, 0x4C4C00): the PC's quest state 0..6, then the
PC's Intelligence (below 5 reads table field 16 + state, the reaction is not asked), else the reaction (below 20 reads field 9 + state, else field 2 + state). A chosen -1 drops
the reply, which still counted as one of the five eligible places; any other number is a Player record of the same dialog, planned as the reply (its text, response value and
action; its own gates are not tested; a chosen `q:` record is chosen through in turn, at most 8 deep) and offered under the `q:` record's response id. A chosen record that is missing,
is no Player record or loops, a table without that field, and a chosen record that cannot be planned leave the option blocked (`TargetInvalid` or `UnsupportedInteraction`).
Without any of the three ports, or for a number outside 1000..1999, the reply stays the frontier of `d`, `u` and `x` (`UnsupportedGeneratedText`). The AREA host passes the
table when `rules\gamequest.mes` loads from the module (`AreaGreetingTexts.QuestReplies`). Tests: `DialogQuestReplyTests` (synthetic) and the opt-in
`DialogQuestReplyRealDataTests` (`DAT_ROOT`: the 5 installed `q:` replies in 1 file are offered or dropped for every quest state and each of the three groups; a state-5 or
state-6 choice that names a record the file lacks is blocked; a picked reply runs on without failure).

## Insult line (ARC-SCRIPT-1505)

With the greeting port (the lines and files, as for the [generated phrases](#generated-phrases-arc-script-043)) the NPC's race line 1000 is spoken in two places
([ledger INS1–INS4](../research/ledger/social.md#insult-line-arc-script-1505)): an NPC node whose whole text is `i:` (either case; 15 installed nodes in 9 files) says it as
the node's line after the node's action ran, and the node's replies are offered as for a plain line (`StepPlan.InsultLine`, the line is picked when the node is entered with
the `Line` roll of the greeting port: the race group 1000 + 50 × (0 when the PC's race is the NPC's, else the PC's race + 1), the dumb file for a dumb NPC, the override record 10999 + 1, names substituted; the race generator, not the class one); and the NPC's last
words when a Player `i:` reply ends the talk (the reaction at most 20) or when the hostile check turns the NPC on the PC, which replace the NPC's line before the talk ends as
`Ended(Insulted)` or `Ended(HostileRefusal)` (no replies, no SCR continuation). Without the greeting port a node `i:` stays a frontier (`UnsupportedGeneratedText`) and the last words
are not changed. An `i:` node that is planned without its replies (the target of a final line, a greeting slot's node) stays a frontier, as the line is picked at the node's entry. The NPC lines `m:` and `r:` are the
[next section](#npc-price-and-rumor-lines-arc-script-1535). The dialog's item hand-over to the PC (`IN -n`) runs the hero's room test and lays a refused item on the hero's tile since
ARC-WORLD-1131, so it is not open any more. Tests: `DialogInsultLineTests` (synthetic) and the opt-in `DialogInsultLineRealDataTests` (`DAT_ROOT`: 15 nodes in 9 files, and no node starts with `m:` or `r:`).

## NPC price and rumor lines (ARC-SCRIPT-1535)

An NPC node whose text starts with `m:` or `r:` (either case, a prefix; no installed node has either, 0 of 709 files, checked by the opt-in `DialogInsultLineRealDataTests`) hands the node to the
price screen or the rumor draw of the trainer's and rumor talks ([ledger INS3](../research/ledger/social.md#insult-line-arc-script-1505), 0x416840 → 0x418A40, 0x418FC0). The node's action
runs first; the node's own replies are not built (the original returns 0). `m:$price,node`: the screen of the [trainer's talk](#trainer-reply-arc-script-1150) (the NPC's class line 1000 with
the price on the reaction scale, a base price of 0 shown as 0 and paying nothing, the PC lines 1..99 and 100..199), whose yes (the `GoldCommand` from the hero's own gold, the notice of line 2000 when it
does not cover the price) pays and follows the `node` number like a response (a node, 0 ends the talk, a negative number is a script line), and whose no, and the line-2000 notice, follow the node's
own response field. `r:$price,list`: the [rumor draw](#rumor-reply-arc-script-1304) of the list with the node's own response in the place of a reply's response (the way on after the tale, the
notice of line 7000 and the no). The numbers are read as the Player replies read theirs (`ParseNpcPriceLine`, `ParseRumorReply`). The line needs the greeting port and the money ports (`World`, `Gold`
and a purse or the trainer port; `r:` also the rumor port, and the money ports only for a price of at least 1), else the node stays a frontier (`UnsupportedGeneratedText`), as it does with
arguments outside the subset (no `$` or no comma after it, a negative `m:` price, a rumor outside 1000..2999) and where the node is planned without its replies or nested (a final line's target, a greeting slot). The
follow nodes are planned when the node is planned, before its action runs, so a missing or Player-role follow fails the step. A follow that is a plain, `g:` or `i:` node is planned then as an ordinary entry; a follow
that is itself an `m:` or `r:` node the ports can run is planned when the way is taken (ARC-SCRIPT-1746, [#1746](https://github.com/Simarglok/arcanum-steamwoven/issues/1746), 0x414810 enters any node), so
chains run and a cycle is bounded by the turn limit; the plan is made before the price of a yes is paid, so a chained node that cannot run (a follow of its own that is missing or has the Player role) stops or fails the pick with no gold spent, instead of failing the first node's entry.
The effect and turn budgets are checked before the payment. Tests: `DialogNpcTalkTests` (synthetic).

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

## Newspaper vendor reply (ARC-UI-1739)

A Player reply with the marker `p:` (exact, as the other generated replies; ledger [NP1–NP8](../research/ledger/scripts.md#newspaper-vendor-and-daily-event-arc-ui-1739)) opens the original's newspaper vendor: after its action the NPC line 4200..4299 and four ways on (buy the shown issue, hear its headline, the back issues, leave) replace the node's NPC line and replies; the PC phrases are 2200..2299, 2000..2099, 2100..2199 and 800..899.

- **Port.** `IDialogNewspaperStandPort` (`DialogPorts.NewspaperStand`, with the greeting texts, the world facts and the gold and purse ports): `Issues()` (the shown issue and the reserved words that are not −1), `Headline(issue)` and `Execute(GiveNewspaperCommand)`. Without the port the marker stays outside the subset.
- **Headline.** The newspaper file's message with the markup taken off, then a closing full stop (unless it ends in `.`, `?` or `!`) and a space, then the NPC line 4300..4399; yes (PC 1..99) goes to the price, no (100..199) leaves.
- **Back issues.** One option per reserved word that is not −1, in table order, its headline as the text; none: NPC 4400..4499 and the answer 600..699, else NPC 4500..4599 and the way out 800..899.
- **Price and payment.** The price screen of the other talks with the base price 2; yes pays (gold to the NPC) and gives the issue, then the NPC class line 15000 and the PC class line 1000.
- **Viewer.** `AreaConversationHost.NewspaperStandPort` reads the session's queue, the headline through `WrittenTexts.Headline`, and gives a new newspaper (prototype 14064, the executable's basic number; the project's 14084) through the viewer's item maker `GiveHeroItem` (`AreaUseScripts.GiveHeroPrototypeItem` with the inventory's insert check), then writes the issue as the item's written start line (`SetItemWrittenStart`). The player reads it through the reading window's use path ([WR1](../world/reading-window-runtime.md)): right click or use on the item in the pack. A pack with no room leaves the paper on the hero's tile.
- **Daily event.** Not a dialog matter: see [the event queue](../world/game-event-queue-runtime.md#daily-newspaper-event-arc-ui-1739).
- **Script actions.** The script actions a85 `queue_newspaper` and a86 `float_newspaper_headline` came with ARC-SCRIPT-1764 ([the dispatcher runtime](scr-dispatcher-runtime.md#newspaper-commands-arc-script-1764-set-s59)); a85 queues by the rule `np` uses.

```sh
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogNewspaperTests"   # ARC-UI-1739
```

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
| AL, IN, MM | AL and IN are still in memory on the state object and lost with it (their owners wait); MM marks the area known in the session since ARC-DEV-191 (an area outside 1..4095 is only remembered here). |

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
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogStoryAreaTests"   # ARC-DEV-191
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogStoryPort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogStatTests"   # ARC-DEV-192
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogStatPort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaStoryConversation"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaCombatConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogHostileTests"   # ARC-DEV-193
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogInsultLine"   # ARC-SCRIPT-1505
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogDirections"   # ARC-SCRIPT-1533
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaDialogDirections"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaHostileConversation|FullyQualifiedName~hostile_check"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogItemHoldTests"   # ARC-DEV-194
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogItemHoldPort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaItemHoldConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogItemTakeTests"   # ARC-DEV-199
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogItemTakePort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCompanionTests"   # ARC-DEV-195
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCompanionPort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaCompanionConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCounterTests"   # ARC-DEV-196
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaCounterConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCollegeTests"   # ARC-DEV-197
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogCollegePort"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogFollowerTests"   # ARC-DEV-227
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogFollowerNamePort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaFollowerConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogWindowTests"   # ARC-DEV-229
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaWindowConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogNewspaperTests"   # ARC-DEV-230
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~NewspaperStateTests"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaNewspaperConversation"
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

## Original dialog evidence carried from the state contract

Carried from the retired ARC-RES-019 research contract; its proposal, conformance
and future-seam parts were dropped (this runtime and the
[negative-IN contract](negative-inventory-transfer-contract.md), the
[global-flag owner](global-flag-state-runtime.md) and the
[ledger rows](../research/ledger/scripts.md) DG1-DG4, DCC9, DAC3, IQ1 and HR1 hold
the accepted behaviour). Labels: S = a reading of the original's functions at the
cited addresses (F0 unless the [ledger](../research/original-verification-ledger.md)
lists it); D = hash-gated selected data fields, not live world state. Hashes and
instance identities of the selected Virgil chain are in the
[Crash Site NPC evidence](../research/crash-site-npc-dialog-evidence.md) and the
[dialogue evidence record](../research/dialog-evidence.md).

**Evidence index (what the original does, by address).**

- Entry order (0x4167C0, 0x416840): a fresh NPC entry runs its action effects
  before reply eligibility; the selected Player's action (0x413130) runs before
  the transition into the target (Direct dispatch 0x414810). A regeneration
  (`randomize=true`) skips the entry effects in the source; it is not a retry
  recipe for a failed command.
- Operand scans (0x4167C0 second operand, 0x415BA0 action integers): permissive
  `atoi` plus a digit/whitespace scan, not a safe expression grammar; strict
  byte admission is project policy.
- GF (query 0x4150D0, action 0x415BA0, owner 0x444CF0/0x444D20): two operands,
  bit index and compare/write value; a world-global bit, not PC- or
  attachment-local. The source setter does not itself validate boolean or bounds.
- RE (0x4150D0, 0x415BA0, reaction service 0x4C0CC0, 0x4C0CE0, 0x4C0D00,
  0x4C0DE0): a signed threshold in the condition, a lexically signed delta or an
  unsigned target in the action; the effective query and the mutable reaction
  storage are not the same quantity.
- QU/RU/LF (0x415BA0, 0x4150D0, attachment flags 0x444F10/0x444F60): PC quest
  with the NPC as context; PC rumor; LF reads the NPC's effective SAP_DIALOG
  attachment header (inherited or overridden), never the immutable SCR initial
  header or the DLG asset.
- Reply grouping (sort 0x417720, compare 0x417E00, gender/IQ/condition/cap
  0x414E60/0x414F50, role/gender parse 0x417860): records load then sort by
  numeric ID, the NPC is found by ID, following Player records are scanned to the
  next IQ-0 NPC and the scan short-circuits at five eligible rows; gender first,
  then adjusted IQ (a dumb PC above LOW_INTELLIGENCE uses 1), then conditions;
  the Player alternate field is the gender selector, the NPC Condition is media
  metadata, not an eligibility expression. Generated option building can remove
  a candidate and yield a generic fallback.
- D: the selected serialized and numerically sorted Virgil groups are both
  `[2,3,4,5,6,7]` (agreement for this group only). An algebraic enumeration of the
  selected gender/IQ/RE predicates gave eligible subsets `[2,6]`, `[3]`, `[4,7]`,
  `[5]` (at most 2) for the diagnostic selector domain 0/1: worksheet results,
  not observed sessions (worksheet
  [`dialog_state_probe.py`](../../tools/research/dialog_state_probe.py)).

**Operation, representation and ownership.** Metavariables below are not original
values; DLG fields are ASCII decimal, not SCR's tagged instructions; `+`/`-`
lexical presence must survive conversion (`-0` is no IN direction, but RE `-0` is
a relative action).

| Operation (S) | Role, owner and read/write |
| --- | --- |
| GF condition/action `GF n b` | World-global story bit; NPC and PC are context only. Condition = equality; action = explicit bool write (D: four selected actions write 0/1, none relative, none a toggle). An unknown index is not "unset". |
| IN action `IN s`, `s<0` | Negative: NPC primary inventory, then conditional substitute, then PC recipient; non-negative: PC, followers, NPC. `s` is an item **name** ID (`OBJ_F_NAME`), not a prototype or instance. |
| IN condition `IN s` | Read-only presence (negative: NPC then substitute; non-negative: PC then followers); no transfer, no count; no selected reply uses it. |
| RE condition `RE t` | `t>=0`: effective reaction >= t; `t<0`: <= abs(t), inclusive. The NPC's reaction toward this PC, keyed `(NPC, PC)`, may include modifiers, hostility, mind control. |
| RE action `RE n` (no sign) | Reads effective R, requests adjustment `n-R`: an absolute target intent, not a guaranteed stored result under modifiers (D: Player 4, 5, 7). `RE +d`/`RE -d` is a relative adjustment of the same relation. A leading `>`/`<` is tested after `atoi` ran, so a marker-prefixed number is not a working clamp (ledger DAC3). |
| QU `QU q s`, RU `RU r` | PC quest `(PC, quest)` and PC rumor log writes; entry commands 6 and 7 even when no visible reply uses a rumor. |

**IN identity, quantity, transfer.** The lookup returns the **first** object with
a matching name in inventory list order (no slot filter, so a worn item can be
chosen). The substitute applies only to an NPC with no leader, a non-null
substitute and distance <= 20. If primary and substitute lack the item, the
source continues without transferring. The positive direction also searches the
PC's followers in source enumeration order. There is no quantity operand and no
item creation: the source transfers the found handle without an explicit
split/decrement, so "one command" does not prove "one unit". Negative direction
(0x415BA0): transfer to the PC, ignore the boolean result, check the parent;
if the parent is not the PC, remove the item and place it at the PC's location
(an existing or new junk pile, or the ground with decay), not always a bare drop.
Positive direction on parent mismatch places at the NPC's location when the NPC
has a PC leader, otherwise destroys the item. The wrapper dispatches to parented
SAP_TRANSFER (0x461CA0) or unparented SAP_GET (0x4617F0, 0x461810) by the item's
current parent. Stacking, slots and callbacks: the negative-IN contract.

**Complete entry and one Direct trace (D/S, selected Virgil chain).** The
conditional entry (SAP_DIALOG: PC triggerer, NPC attachee, caller SCR line 0;
Following false, two attachment-local flags false and jilted false reach SCR
line 4 -> DLG NPC 1) is diagnostic assumption, not observed world state.

| Step | Record / stage | Operation shape |
| --- | --- | --- |
| 1-3 | NPC 1 entry actions 1-3 | GF, world bit write |
| 4 | NPC 1 entry action 4 | negative IN: lookup NPC/substitute, transfer or placement to the PC |
| 5 | NPC 1 entry action 5 | GF write |
| 6, 7 | NPC 1 entry actions 6, 7 | QU (PC quest, NPC context), RU (PC rumor log) |
| Q | Player 2..7 enumeration | gender, then adjusted IQ, then RE threshold on **post-entry** state |
| 8 | the selected Player 4 | unsigned RE (target intent NPC to PC) |
| 9 | checked Direct to NPC 9 | NPC 9's entry action is empty: zero commands |
| F | NPC 9 | stop before enumerating its replies |

Player 2, 3 and 6 have empty actions, Player 4, 5 and 7 one unsigned RE each; all
six Direct targets are NPC 9, so choosing Player 2 would mutate nothing and cannot
establish RE semantics. A static check covers the whole entry group, every
response action and every distinct target action before the first entry
mutation; a runtime failure keeps the completed prefix and stops the rest (no
atomic conversation transaction or rollback is inferred from the source).

## Provenance

Source-backed observations are a reading of the original's functions at the cited addresses,
evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them: record fields (0x417860), reply enumeration and
gates (0x414E60, 0x414F50), conditions (0x4150D0; its `ME` branch checked in the executable, ledger DR1, its `GV`/`PV`/`PF` branches, DG1–DG2, and its `RU`/`RQ` branches, RU1–RU2; the reply gate by intelligence, IQ1), actions (0x415BA0; `GV`/`PV`/`PF`, DG3–DG4), the
insult (0x416C10, 0x419190, 0x4191E0; ledger DR4, DR5), the reply step's hostile check (0x413130; ledger HR1–HR4), NPC line, markers and
names (0x416840, 0x416AB0, 0x416B00), generated Player options (0x416C10, 0x417590; their phrases checked in the executable, ledger GP1–GP6), response and UI outcomes (0x567E30, 0x5680A0, 0x5681C0, 0x568430, 0x568480).
Strict grammar, group-order guard, look-ahead blocking, budgets, typed ports and the
diagnostic profile are project policy. Unknown: original parser edge cases, the
prefix markers' phrases, all other operations, reaction/alignment modifiers, item transfer outcomes,
follower rules and the SCR continuation after the dialog.

