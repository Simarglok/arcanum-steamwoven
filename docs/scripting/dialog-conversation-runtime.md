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
loader, inventory/party/map/AI simulation, save service, voice/media, or original-runtime
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
[logbook](../rendering/terrain-viewer.md#logbook-arc-ui-008)) takes `RQ`; without it an `RQ` is a static
`CapabilityUnavailable` frontier. The optional `IDialogSkillPort` (ARC-SKILLS-004, [skill gates](#skill-gates-arc-skills-004))
answers the PC's skill levels and training and takes `TR`; when present it admits `PS`, `HA`, `SK`, `TR` and `ET`, and
without it they stay `UnsupportedOperation`.

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
   negative `-n` → `Ended(ReturnToScript, n)`; a trailing `JO 0 node` (or, with a party port,
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
`Generated` with their normal transition (phrase text lives in generic resources not loaded
here); `b h i l p` (exact) and `c r t z` (prefix) are offered blocked as
`UnsupportedInteraction`; `d q u x` have state-dependent visibility and make the step a
frontier. NPC `g:`/`m:`/`r:` (prefix) and exact `i:` are frontiers. Other text is literal.

## conversation-grammar-v1

```text
separator = SP | HTAB | ',' | ';' | ':' | '(' | ')'      field = separator* (op (separator+ op)*)? separator*
space     = SP | HTAB
op        = LETTER LETTER space* int (space+ int)?          int = ('-' | '+')? DIGIT+
          | ('SK' | 'TR' | 'ET') space* int (space* int)?   (ARC-SKILLS-004: the source's second operand, also "tr4-1")
```

| Field | Admitted operations (domains) |
| --- | --- |
| Player condition | `LF b v` (b 0..31, v 0/1, attachment flag equals v; Missing is false), `GF f v` (f ≥ 0, v 0/1; Known only), `RE t` (t ≥ 0: reaction ≥ t; t < 0: reaction ≤ −t), `RA n` (n > 0: PC race index n−1; n ≤ 0: race ≠ −n−1); with a party port `WA v` (v 1: the NPC waits for the PC, 0: it does not); with a skill port `PS n`, `HA n`, `SK s n`, `TR s t` ([skill gates](#skill-gates-arc-skills-004)). |
| NPC / Player action | `LF b v`, `GF f v`, `QU q s` (q > 0, s 0..6), `RU r` (r ≥ 1000), `RQ r` (r ≥ 1000: quell the rumor, rumor port), `RE`/`AL` unsigned = target, `+n`/`-n` = adjust, negative `IN -n` (NPC gives item name n), `MM a` (a ≥ 0); with a skill port `TR s t` (set the PC's training). |
| Player action tail | one `JO 0 node` or `FL node` (with a party port also `UW 0 node`, with a skill port also `ET s node`) as the last operation (NPC node checked statically). |

Unknown opcodes, `$`, combinators, `RE`/`AL` with `>`/`<`, non-negative `IN`, `JO`/`UW` with a non-zero
first operand, `JO`/`UW`/`FL` in NPC actions or not last, `WA`/`UW` without a party port, and a non-space separator inside one
operation (`GF 5,1`, `qu(5,6)`: the source's whitespace-skipping `atoi` would read 0 there)
→ `UnsupportedOperation`; such forms are never reinterpreted. Wrong arity,
missing separators, out-of-domain values, non-ASCII, `+` outside `RE`/`AL` actions,
overflow → `InvalidOperation`. Since ARC-SKILLS-004 a number where an operation should start, an extra operand (`LF 1 1 1`,
`JO 0 30 5`) or a stray one (`ha4, -4, lf1 1`), which the source's scanner skips (DS1), is `UnsupportedOperation` (a frontier,
not a failure); a missing operand stays `InvalidOperation`. A field of separators only is empty. GF reads/writes use the
owner's current version; `Unknown`/`Unavailable` is a frontier, `Failed`/`Stale` a failure,
`NoChange` a successful receipt.

## Skill gates (ARC-SKILLS-004)

The source's dialog skill cases, read in the retail 1.0.7.4 executable by two readers
([ledger DS1–DS5](../research/original-verification-ledger.md#dialog-skill-gates-arc-skills-004)). Skills are the source's
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
actions (in 20 files); every one parses on its own. Of the 1,582 fields that hold one, 1,215 parse whole and 367 stop at an
operation outside this subset (money `$$` 121, rumors 84, quests 47, …), never at a skill operation.

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

In `crash-site-area` mode the original **left click** on an NPC whose
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

### Consequences in the game session (ARC-SCRIPT-006)

The host borrows the area's [game session](../world/game-session-runtime.md), created for
every published area (with or without the cave), and `AreaConversationState` stores the
dialog consequences there, so they survive further talks, the cave trip and a
capture/encode/decode/restore of the session, and they change the next conversation:

| Consequence | Where it lives |
| --- | --- |
| GF conditions and writes | The session's flags: original new-game domain 0..3199, all known false (source: 100 words of flags zeroed on init and reset (0x4446E0, 0x4447D0)); flags ≥3200 are not admitted and fail the dialog. |
| QU | Session quest rules ([story domain](../world/game-session-runtime.md#story-domain)); a QU that leaves a quest Accepted lifts the NPC's effective reaction to at least 41 (a delta through the reaction domain), Completed adds 10 (source rule, checked in the original executable); such a QU takes two session revisions (quest, then reaction). A quest outside 1000..1999 is refused and fails the dialog. |
| RU | Session rumors with their game time; outside 1000..2999 is refused. |
| RQ | ARC-UI-008: the session's game-wide quelled rumors (no participant, as the original's dialog action); quelling a quelled rumor is no change; outside 1000..2999 is refused and fails the dialog. |
| RE | The session reaction domain ([NPC reaction runtime](../world/npc-reaction-runtime.md), ARC-WORLD-026): `RE t` reads the speaker's effective reaction, unsigned `RE v` sets it as a delta to the effective value, `RE ±d` adjusts it through the PC's adjustment effects. Facts are diagnostic (human NPC with the initial reaction as base; the session hero or a human PC). |
| LF, JO | Per speaker, map-local entity changes on the speaker's map: entity `oid:<object id SHA-256>`, properties `dialog.lf` (attachment flags), `follow.pc`. A value equal to the loaded baseline (attachment flags, not following) clears the key. A session holding values these ports cannot read (LF outside 32 bits, follow other than 1) is refused when the state is built. |
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
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogConversation"
"$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillTests"   # ARC-SKILLS-004
DAT_ROOT=<installation> "$DOTNET" test tests/Arcanum.Scripting.Tests/Arcanum.Scripting.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillRealData"
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~DialogSkillPort"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter "FullyQualifiedName~AreaConversation"
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
gates (0x414E60, 0x414F50), conditions (0x4150D0), actions (0x415BA0), NPC line, markers and
names (0x416840, 0x416AB0, 0x416B00), generated Player options (0x416C10, 0x417590), response and UI outcomes (0x567E30, 0x5680A0, 0x5681C0, 0x568430, 0x568480).
Strict grammar, group-order guard, look-ahead blocking, budgets, typed ports and the
diagnostic profile are project policy. Unknown: original parser edge cases, generated
phrases, all other operations, reaction/alignment modifiers, item transfer outcomes,
follower rules and the SCR continuation after the dialog.

