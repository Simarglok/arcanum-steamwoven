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
instead of the world port; `IN` then needs only one of the two.

## Flow and source semantics

Source references are the pinned CE lineage below; implementation is independent.

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
```

| Field | Admitted operations (domains) |
| --- | --- |
| Player condition | `LF b v` (b 0..31, v 0/1, attachment flag equals v; Missing is false), `GF f v` (f ≥ 0, v 0/1; Known only), `RE t` (t ≥ 0: reaction ≥ t; t < 0: reaction ≤ −t), `RA n` (n > 0: PC race index n−1; n ≤ 0: race ≠ −n−1); with a party port `WA v` (v 1: the NPC waits for the PC, 0: it does not). |
| NPC / Player action | `LF b v`, `GF f v`, `QU q s` (q > 0, s 0..6), `RU r` (r ≥ 1000), `RE`/`AL` unsigned = target, `+n`/`-n` = adjust, negative `IN -n` (NPC gives item name n), `MM a` (a ≥ 0). |
| Player action tail | one `JO 0 node` or `FL node` (with a party port also `UW 0 node`) as the last operation (NPC node checked statically). |

Unknown opcodes, `$`, combinators, `RE`/`AL` with `>`/`<`, non-negative `IN`, `JO`/`UW` with a non-zero
first operand, `JO`/`UW`/`FL` in NPC actions or not last, `WA`/`UW` without a party port, and a non-space separator inside one
operation (`GF 5,1`, `qu(5,6)`: the source's whitespace-skipping `atoi` would read 0 there)
→ `UnsupportedOperation`; such forms are never reinterpreted. Wrong arity,
missing separators, out-of-domain values, non-ASCII, `+` outside `RE`/`AL` actions,
overflow → `InvalidOperation`. A field of separators only is empty. GF reads/writes use the
owner's current version; `Unknown`/`Unavailable` is a frontier, `Failed`/`Stale` a failure,
`NoChange` a successful receipt.

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
the source (`tc.c` reads only mouse messages); the controller menu navigates, confirms and
cancels. Keyboard keys are not redirected to dialog actions: the WORLD keyboard route keeps
them reserved for their original actions ([palette and keyboard route](../input/diagnostic-palette.md)),
so `T`, digits, `Esc` and `Enter` do nothing in the dialog. A diagnostic **Leave** button
closes an awaiting dialog without a reply. The panel is modal, so the actor gate stays closed. `AreaConversationHost` (viewer Core) re-authenticates the
selected archives on every talk (`arcanum2.dat` as Script, `arcanum3.dat` as Dialog; the original
game opens Virgil's script from `Arcanum4.dat`, which shadows this copy, see
[content precedence](../research/content-precedence.md#crash-site-impact)), resolves
the entry through the SCR resolver (Following from the session, Jilted false, LF from the
session's attachment flags seeded from the loaded attachment), reads the resolved DLG member
by ordinal and checks its payload SHA-256 before parsing.

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
| GF conditions and writes | The session's flags: original new-game domain 0..3199, all known false ([source: 100 words of flags zeroed on init and reset][gf]); flags ≥3200 are not admitted and fail the dialog. |
| QU | Session quest rules ([story domain](../world/game-session-runtime.md#story-domain)); a QU that leaves a quest Accepted lifts the NPC's reaction to at least 41, Completed adds 10 (source rule); such a QU takes two session revisions (quest, then reaction). A quest outside 1000..1999 is refused and fails the dialog. |
| RU | Session rumors with their game time; outside 1000..2999 is refused. |
| LF, RE, JO | Per speaker, map-local entity changes on the speaker's map: entity `oid:<object id SHA-256>`, properties `dialog.lf` (attachment flags), `reaction.pc`, `follow.pc`. A value equal to the loaded baseline (attachment flags, initial reaction, not following) clears the key. A session holding values these ports cannot read (LF outside 32 bits, reaction outside `int`, follow other than 1) is refused when the state is built. |
| AL, IN, MM | Still in memory on the state object and lost with it; PC alignment, items and known areas wait for their own owners. |

The SCR dispatcher's viewer host (ARC-SCRIPT-009) reads Following and LF from the same session, and also
stores the dialog slot's counters and number as `dialog.counters` and `dialog.script`, so Virgil's
follow consent picks his follower entry after a round trip to the cave or a restore. The
HUD status shows the session story (quests, rumors, followers). In the synthetic
self-test without area lighting the session clock is an invented synthetic profile.

[gf]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L150-L212

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

Source-backed observations come from `alexbatalov/arcanum-ce` at
`a7ff41b300ef712f0e7d088183a3d08957110cdd` (Sustainable Use License 1.0, **not MIT**; no
code copied or mechanically translated): [record fields][parse], [reply enumeration and
gates][enumerate], [conditions][conditions], [actions][actions], [NPC line, markers and
names][npc], [generated Player options][options], [response and UI outcomes][ui].
Strict grammar, group-order guard, look-ahead blocking, budgets, typed ports and the
diagnostic profile are project policy. Unknown: original parser edge cases, generated
phrases, all other operations, reaction/alignment modifiers, item transfer outcomes,
follower rules and the SCR continuation after the dialog.

[parse]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2641-L2734
[enumerate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1274-L1358
[conditions]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1361-L1859
[actions]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1862-L2243
[npc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2260-L2390
[options]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2393-L2549
[ui]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L281-L407
