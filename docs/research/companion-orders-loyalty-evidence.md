# Companion orders, loyalty, wait timeout and the level step

Status: **Research**, work item ARC-RES-064 (2026-10-21). It re-reads, in the
retail executable, the earlier readings (F0) that the companion lane will
implement next: out-of-combat orders (companion C14), loyalty while following
(C13, open question O4), the end of a wait (C12), the level-up message and the
level step of waiting followers ([experience](../world/experience-runtime.md)
XP5, XP7), the script action that sets an auto-level scheme (SCR action 139,
[Virgil auto-level](virgil-auto-level.md)) and how a map opened after a
cross-map teleport binds its followers back (O7). Every rule below is
universal: it holds for any NPC on any map, not only for Virgil or the Crash
Site. It changes no code; the results are inputs for the
[implementation slices](#implementation-slices).

Requirements:
[FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004),
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005),
[FR-CHAR-013](../requirements/characters-skills.md#fr-char-013),
[FR-CHAR-021](../requirements/characters-skills.md#fr-char-021), and for Q7
[FR-WORLD-010](../requirements/world.md#fr-world-010).

Earlier results reused, not re-read: the follow check and the follower limit
([ledger AP3, AP4](original-verification-ledger.md#ai-parameters-and-follow-consent-arc-data-033)),
the wait timer's scheduling condition (ledger row Wait timer (C12, O3, TE8)),
the reaction change rules ([R2](original-verification-ledger.md#reaction-runtime-arc-world-026)),
the AI pass and its cadence ([core loop](npc-ai-core-loop.md), CL7–CL12), the
event queue's map scope (ledger OQ3 and the [event queue](game-event-queue.md)
P3–P5), the level recalculation and follower levels (XP5, XP7, AL12) and
"stop attacking" (ledger HS18). Combat orders (attack, back off/stop, curse)
belong to stage A7 of the [NPC combat AI plan](npc-combat-ai-plan.md) (source
unit S20); this note records only where they start.

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed for this reading), read statically with
`llvm-objdump` following the [original executable check](original-executable-check.md),
by a first reader and, independently, by a second reader that saw only the
questions (method step 6; see [Second reading](#second-reading)). Object fields
were named from the executable's own field table (0x59BEA8), animation goals
from its own goal name table (0x5A164C), data file names from its strings.
Installed data was read only for message numbers, keywords and the headings of
the generated-line files; no text of the game is reproduced here. Every direct
branch of the functions named stays inside `.text`. Everything is read from the
executable unless marked **(inference)**. Distances are tile distances
`max(|dx|, |dy|)` (0x4B96F0, read for this note).

Verdicts follow the [ledger](original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings):
**Original** (the earlier reading holds, or a new fact that looks intended),
**Reconstruction slip** (the earlier reading was wrong), **Original bug** (the
original does it and it looks unintended; a candidate for a switch of the
Original setting). The rows are also in the
[ledger](original-verification-ledger.md#companion-orders-and-loyalty-arc-res-064).

## Answer

- **Q1 Orders.** All orders are text handled by one broadcast function. F1–F6
  send six preset texts to every direct follower within 10 tiles; the portrait
  menu sends "name + order" to one follower; the player can also type an order
  after Enter. Leave, wait, follow, move, stay close, spread out and walk here
  are the out-of-combat orders: leave is a forced leave, wait is the ordinary
  wait (with its timer), move steps out of the PC's way, spread out / stay close
  switch the follow distances, walk here runs to the chosen tile. **Follow
  changes nothing** (OL6), **F6 reaches nobody** (OL14) and **back off is "stop
  attacking"**, not a movement (OL10). Every answer is a random line of the
  generated NPC line files by gender. A waiting follower is outside the
  follower array and has no portrait, so no order reaches it.
- **Q2 Loyalty.** Negative reaction changes and the PC's alignment changes arm
  three tests on each direct follower. The loyalty step runs in the follower's
  non-combat AI pass only while it has no running action goal; a live follow
  goal restarts itself every 100 ms and never ends on its own in real time, so
  during ordinary following the step is skipped until something interrupts the
  follow goal (OL19). When it runs, a refused follow check (limit ignored) makes
  the follower leave, jilted, with the refusal line of its reason; otherwise an
  armed check-leader test only floats a warning line.
- **Q3 Wait timeout.** The expiry handler drops waiting, sets jilted, changes the
  NPC's reaction to its leader by `2 × (leader's charisma − 20)` and clears the
  leader; no line, nothing else. Unwait, leave, join, death and a script follow
  cancel it. The event stays with the waiting NPC's map: away from it, it is
  parked and fires at the first ping after the PC returns.
- **Q4 Level-up message.** For each level the PC reaches, an entry in the
  in-game message window: a "Level Up" title, the new level, and the unspent
  points or, with an auto-level scheme, what the scheme bought; sound 3005. PC
  only; followers get none.
- **Q5 Waiting followers.** Only waiting followers inside a screen-sized
  rectangle around the PC get the level step; one on another map (or farther
  away) never gets it.
- **Q6 Action 139.** Writes its number into the auto-level scheme field of each
  PC or NPC its object operand names; no range check, nothing spent until the
  next level.
- **Q7 Maps.** Handles are saved as object IDs and turned back into handles when
  a map opens, for every loaded object; an ID whose object is not loaded becomes
  null. The group travels together, so the links rebind; a follower carried to
  another map is from then on an object of that map.

## 1. Out-of-combat orders (Q1, companion C14)

### How the player gives an order

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL1 | Orders are text. F1–F6 (key codes 0x3B–0x40) copy preset 0–5 (messages 500–505 of `mes\broadcast.mes`, loaded at init) and send it with the tile under the pointer. The portrait menu (`mes\follower_ui.mes` 0–7: walk, attack, stay close, spread out, back off, wait, inventory, character sheet) sends "follower's name + item label" for stay close, spread out, back off and wait (wait is not sent for a mind-controlled follower), opens a target cursor for walk (a tile) and attack (an object) and sends "name + label" with the picked tile once chosen (not when the PC is dead or unconscious), and opens the inventory or the character sheet for the other two. After Enter the player can type a text, sent with the pointer's tile. All paths reach the same broadcast entry, which in single player calls the handler directly. | presets 0x4C3B90, 0x4C3BE0 (callers 0x54E3D5, 0x54E54F, 0x54E936); portrait 0x56A9D0, 0x56B110, 0x56B180; typed 0x571950; entry 0x4C2F00; handler 0x4C31A0 | Original (C14) |
| OL2 | Receivers: a copy of the broadcasting PC's follower array (direct followers only, in array order; not their own followers, not waiting NPCs), each within 10 tiles of the PC. A text starting with `#` addresses every such follower, one per pass; otherwise the first follower whose name as the PC knows it is a case-insensitive prefix of the text takes the order and the walk stops. The PC's own text is floated over the PC first. Each handled follower then gets one AI pass at once. When no follower matched, only a multiplayer phase runs (other players' names; join and disband), which does nothing for a single player. At most 200 passes. | 0x4C31A0, name 0x441B60, distance 0x4B96F0, AI pass 0x4A84F0 | Original (C14) |
| OL3 | The order word is the rest of the text after the name or `#`, leading spaces skipped, compared whole and case-insensitively with the keywords 100–117 of `mes\broadcast.mes`, the first match winning; `Rules\BroadCastRules.mes` maps each keyword number to one of 14 types (none, leave, wait, follow, move, stay close, spread out, curse, join, disband, attack, walk here, back off, follow other). Synonyms: come = follow, close = stay close, spread = spread out, stop = back off, walk = walk here. Keyword 117 repeats "follow", so the type "follow other" is never reached. An unknown word is type none: no effect except the AI pass. | lookup 0x4C3B40, init 0x4C2C10 | Original |

### What each order does

All lines below are floated over the follower (OL12). "Mind controlled" means
the follower's spell flag 0x8000000.

| ID | Order | Effect | Address | Verdict |
| --- | --- | --- | --- | --- |
| OL4 | leave | Mind controlled: nothing. Otherwise a farewell line (OL13), then a **forced** leave: the extraordinary-charisma refusal of an ordinary leave does not apply. | 0x4C348D, leave 0x45DFC0 | Original |
| OL5 | wait | Mind controlled: nothing. Otherwise the acknowledgement line, the follower's animations are interrupted (level 3), then the ordinary wait with its timer (§3). | 0x4C34E2, 0x424070, wait 0x4AA7A0 | Original |
| OL6 | follow, come | Follow check with the charisma limit ignored only for a forced follower. Refused: the generic "cannot" line, nothing else (it does not leave). Accepted: the acknowledgement line and an unwait. Followers in the array never carry the wait flag (a wait removes the NPC from the array first), so the unwait never acts: **the order changes no state**. | 0x4C3542, check 0x4AD950, unwait 0x4AA8C0 | Original bug (likely; the unwait was meant for waiting followers, which the walk excludes) |
| OL7 | move | A "please move" goal (68) that steps the follower out of the PC's way; acknowledgement when the goal starts, "cannot" otherwise. | 0x4C35E4, 0x4363E0 | Original |
| OL8 | stay close, spread out | Acknowledgement; clear or set the follower's spread flag (NPC flag 0x10), which selects the follow distances 4/2 or 7/5 (C7, C8). Setting it (when it was clear) also starts an "attempt spread out" goal (69) relative to the leader. | 0x4C3673, 0x4C36BB, 0x45F5C0, 0x45F600, 0x436960 | Original |
| OL9 | walk here | Needs a tile (F1: the tile under the pointer; portrait walk: the picked tile). Acknowledgement, then a run-to-tile goal (4) to that tile; a run-to-tile goal already running gets the new tile. The follower keeps its leader; at its next non-combat pass the follow request does not replace the running goal, so it reaches the tile and then follows again once the leader is beyond 4 (7) tiles **(inference from OL19)**. | 0x4C37E2, 0x433C80 | Original |
| OL10 | back off, stop | Acknowledgement, then "stop attacking" (ledger HS18): peace with its focus, combat mode left and, for a follower with a PC leader, the no-attack flag. **No movement.** The menu label and F5 are this order. | 0x4C3849, 0x4AA4A0 | Original (combat side: A7) |
| OL11 | curse, attack | Boundary only (stage A7). Curse: mind controlled → nothing; otherwise an unforced leave and, when it left, a race insult from the generated race-line files (0x413D40) and the attacked-by-the-PC reaction (0x4A9650). Attack: the target is the object of the UI's target cursor; out of combat mode a target in the PC's own party is refused; the no-attack flag is cleared (0x4AA580) and an attack is requested (0x4A9650). | 0x4C3703, 0x4C377D, target 0x43C570 | Not settled here (A7) |

### Lines, presets and addressing

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL12 | Every order line is a random message in a number range of `mes\gd_npc_m2m.mes`, `_m2f`, `_f2f` or `_f2m`, chosen by the follower's and the PC's gender: acknowledgement 2100–2199, "cannot" 2200–2299, farewells 1900–1999 and 2000–2099. The follower's own dialog file may supply the line instead (its entry numbered 11,999 + the range's hundreds; the lookup was not traced further). A follower that cannot speak (the can-speak test) says nothing. The line is floated over the follower by the dialog interface callback, which also starts the line's speech. | 0x418780, 0x418250 (file table 0x5A063C), 0x418870, 0x413BE0, 0x413C90, 0x4133B0, can speak 0x4AD800, callback 0x568430 | Original |
| OL13 | The leave farewell is chosen by the reaction level (0 love … 6 hatred, 0x4C0FC0): levels 4–6 (reaction 40 or less) take 1900–1999, which the line files label friendly farewells; levels 0–3 take 2000–2099, labelled hostile farewells. A follower that likes the PC therefore says a hostile goodbye and one that dislikes it a friendly one. | 0x4133B0 | Original bug (likely; inverted against the data's own labels) |
| OL14 | Preset 5 (F6) has no `#` and names no follower, so it addresses nobody: F6 does nothing in single player. Presets 0–4 (F1–F5) are walk here, attack, stay close, spread out and back off for all followers in reach. | 0x4C3BE0, 0x4C31A0 | Original bug (likely, data: the `#` is missing) |
| OL15 | The portrait menu addresses its follower by name text: when two followers' known names share a prefix (two unnamed NPCs of the same kind, for example), the first one within 10 tiles in array order takes the order, which need not be the clicked portrait; a portrait follower farther than 10 tiles gets nothing and no line. | 0x56A9D0, 0x56B180, 0x4C31A0 | Original bug (likely) |
| OL16 | A waiting NPC is not in the follower array: it has no portrait (the portrait list is the array, 0x4411C0) and no broadcast reaches it. Only dialog `UW`, a script follow or a join brings it back. | 0x56B6F0, 0x4411C0, 0x4C31A0 | Original (C14) |

## 2. Loyalty while following (Q2, companion C13, O4)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL17 | Arming. A negative reaction change by a PC that is the NPC's direct leader sets the NPC's check-leader flag (NPC flag 0x10000) and the reaction test bit (critter flags 2, 0x1000000). A change of a PC's alignment sets, on each follower in its array, check-leader and the "too good" test bit (0x2000000) when the alignment rose or the "too bad" bit (0x4000000) when it fell. Waiting followers are not armed. | reaction 0x4C0DE0 (R2), stat setter 0x4B0980 (MP5) | Original (C13) |
| OL18 | Where the step runs. In the non-combat pass of an NPC with a leader: combat mode is left; a waiting NPC stops there (no follow, no item search); the follow goal is requested; then the loyalty step runs only if the NPC has no running goal of the action class. Goals whose descriptor marks them as overlays (eye candy, floating, a few "attempt" goals such as attempt spread out) do not count; a follow goal does. | 0x4AC380, 0x423300, goal descriptors 0x5B03D0 | Original (C13; the test is now read, no longer an inference) |
| OL19 | How often. The follow request starts a follow goal (24) only when the leader is beyond 4 (7) tiles, never replaces a running goal other than a fidget or one that an earlier follow request started, and keeps an existing follow goal (restarting it when its walk-near step finds the leader beyond the start distance). The follow goal's last step succeeds for every NPC in real time (it costs 2 action points only in a turn-based round) and restarts the goal after 100 ms; nothing in the goal ends it in real time. So the loyalty step runs on each AI tick (250 ms on the PC's tile, under 900 ms within 4 tiles, CL8) while the follower stands near its leader with no follow goal (before the leader first moves 4 (7) tiles away, or after the goal was interrupted while the leader was within that distance), and it is skipped for as long as a follow goal lives, until a conversation, an order that interrupts, a hit, combat, a map change or the end of the action points in a round stops it. How often that is in play needs an observation in the running game (F4). | 0x4346A0, goal 24 (0x5A85D0), step 0x423560, 0x433270, 0x4B7CD0 | Original (intent unclear); answers O4 statically |
| OL20 | The step. Follow check with the charisma limit ignored (results of AP3; limit and not-allowed cannot occur). Refused: an unforced leave; if the leave is refused (extraordinary charisma, mind control) the step goes on as if accepted; if it left: the jilted flag, then the refusal line of its reason (too good 1600–1699, too evil 1700–1799, dislike 1800–1899, already grouped 2600–2699, limit 2700–2799, not allowed 2800–2899, low level 2900–2999) when the line callback exists and the NPC is active. No reaction change. Effects in order: leave (array entry removed, leader cleared, forced cleared), jilted, line. | 0x4AC380, 0x4AD950, 0x45DFC0, 0x413A90, 0x45D790 | Original (C13; C13's "warning line" here is the refusal line) |
| OL21 | Check leader. Only when the follow check passed or the leave was refused, and the check-leader flag was set at the start of the pass: the flag is cleared and the test clears all three bits, then returns 0 when the NPC is mind controlled by this leader (or by an unresolvable controller while it has a PC leader), the leader has Persuasion training Master, or the NPC is a forced follower; otherwise: reaction bit and reaction ≤ AI field 5 + 20 → 3; else, leader's alignment above the NPC's, "too good" bit and gap > field 6 − 100 → 1; leader's at or below, "too bad" bit and gap > field 7 − 100 → 2. A non-zero result floats a warning line (1: 5200–5299 too good, 2: 5300–5399 too evil, 3: 5400–5499 low reaction). It never makes the follower leave. | 0x4AC4C7, 0x4ADB50, 0x414290 | Original (C13) |
| OL22 | A wait timeout, or a negative reaction change just before some other leave, arms check-leader and the reaction bit while the leader is still set; once the leader is gone the bits stay until the NPC follows again, when a single stale test can run. | 0x4AA990, 0x4C0DE0 | Original (latent, at most one extra warning) |

The other loyalty path, the PC leader hitting its own follower (reaction A2, R8:
parameter 9, then the same check), is in the
[reaction ledger](original-verification-ledger.md#reaction-runtime-arc-world-026)
and stays with stage A7.

## 3. Wait timeout (Q3, companion C12)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL23 | Scheduling (re-read with the ledger's row Wait timer): a game-time event of kind 17 naming the NPC, only when the leader's charisma is below 20 and the NPC is not mind controlled. Duration by the NPC's wait field: 0, 15 min, 2 h, 1 day, 7 days, 30 days for 0..5; any Persuasion training of the leader adds one step; 6 or more: no event. Index 0 is a zero delay, which the queue stores as 1 ms (event queue Q2): such a follower jilts at the next ping. The leader is re-read when the event fires. | wait 0x4AA7A0, table 0x5B5088, add 0x45B800 | Original |
| OL24 | Expiry: a network client does nothing; without a leader nothing; otherwise one write clears waiting and sets jilted, then the reaction change `2 × (leader's effective charisma − its maximum 20)` (no race has another charisma maximum, 0x4B0FD0) through the reaction change rules (R2: zero does nothing; for a leader that is not a PC nothing), then the leader is cleared. No line, no animation, no script; the NPC is in no follower array. | 0x4AA990, 0x4B0FD0, 0x4C0DE0 | Original |
| OL25 | Cancel: every unwait that succeeds removes the NPC's pending events of this kind (predicate 0x4AAA30). Unwait runs on dialog `UW`, leave (with the leave's force), join (which leaves first), the kill notification (forced, 0x4AA1B0) and the forced disband when the object is destroyed (0x45E180, called from object destruction 0x43CCA0); the follow order's unwait never reaches a waiting NPC (OL6). An unforced unwait that the follow check refuses keeps waiting and keeps the event. | 0x4AA8C0, 0x45BFF0, 0x45DFC0, 0x45DE90 | Original |
| OL26 | Maps: the event names only the NPC, which stays on its map. When the PC leaves, the event is parked in that map's file with it; when the map opens again it is loaded and, if overdue, fires at the first ping (ledger OQ3, event queue P4, P5). A timeout that elapsed while the PC was away therefore jilts the NPC as the PC returns. | 0x4AA990; map close 0x45BCE0, park 0x45C2F0, open 0x45C580 | Original |

## 4. Level-up message (Q4, XP5)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL27 | For each level written in the normal branch of the recalculation, and only when the PC is the local player's: one entry (type 0) in the in-game message window, a history of ten. It is drawn as message 21 of `mes\intgame.mes` (the "Level Up" title), message 11 with the new level, then either message 12 with the unspent points or, for a PC with an auto-level scheme, the scheme's text: message 2 of `mes\level.mes` followed by what was bought or message 3 when nothing, message 4 for a scheme without a rule, and a space plus message 1 when the rule completed. Drawing it plays sound 3005 (audio SN31). Three more interface callbacks follow (button state and two refreshes, not traced). The last-level branch (level 51 not reached) shows nothing; a follower's level step (0x4A6CB0) runs its scheme with no text. | 0x4A69C0, 0x460630 → 0x550750 → 0x552770, draw 0x5529C0, sound 0x41B780, scheme text 0x4A7030 | Original (XP5) |
| OL28 | The points shown are the stored unspent points plus this level's points, computed before the setter clamps unspent points to 56 (XP2): with more than 54 unspent points the line names points the PC does not have. | 0x4A6A90–0x4A6AC4 | Original bug (cosmetic) |

## 5. Waiting followers and the level step (Q5, XP5, XP7)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL29 | After the followers of the array (the recursive list, no death test, A5), the step goes to NPCs found in a rectangle the size of the game window centred on the PC, converted to tiles, among the loaded objects, that are not dead, have the wait flag and whose top PC leader is this PC. A waiting follower outside that rectangle, farther away on the same map or on another map (not loaded), gets nothing, then or later; XP7 keeps its gap, so it stays behind the PC for good (except the level-50 path of A1). | 0x4A69C0, 0x440FC0, 0x440FF0, 0x440B40, 0x4A6CB0 | Original (L7 "within one screen" confirmed) |

Steamwoven today levels every waiting NPC of the current map
([advancement runtime](../world/advancement-runtime.md#not-ready)), which is wider
than the original's rectangle; see [decision candidate](#owner-decision-candidates) D7.

## 6. SCR action 139: set auto-level scheme (Q6)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL30 | Operands: an object operand, resolved as a list (one object or a group), and a number. For each object, a PC or an NPC gets the number written into its auto-level scheme field (245); other objects are ignored; there is no range check. Nothing is bought at once: the scheme runs at the next level step (a PC per level in the recalculation, a follower only when its level rose, AL12). A number without a rule (below 1, 200 or above, or a missing entry, AL2) buys nothing; for a PC its level message then shows the "missing scheme" text instead of the points line and the points stay unspent. The script goes on with its next action. | handler 0x44ABC8 (table 0x44ADBC), operands 0x44B4E0, 0x44BA70, setter 0x4A6D90 | Original |

## 7. Followers across maps (Q7, companion O7)

| ID | Fact | Address | Verdict |
| --- | --- | --- | --- |
| OL31 | Before a cross-map teleport writes the group into the destination's mobile file (C16), and when a map closes, each object's handle fields are turned into object IDs and the object is marked. When a map opens, after its mobile objects are loaded, every non-static marked object has each handle field and each element of its handle arrays looked up among the loaded objects (a positional ID only on the current map, at its stored tile): found → the handle, not found → null (an array element is nulled in place, not removed). The PC and its group arrive in the same open, so the PC's follower array and each follower's leader bind back. | 0x4064B0, 0x410C50, 0x406520, 0x40BDB0, 0x40BE70, 0x40BFC0, 0x4E50E0 | Original |
| OL32 | A waiting follower left on another map is saved with that map, its leader as the PC's object ID. While the PC is elsewhere it is not in memory at all: no AI, no order, no portrait, no level step, its timer parked. When the PC opens that map again, the leader binds back to the PC. | as OL31; 0x4A69C0 | Original |
| OL33 | A follower carried to another map is written into that map's mobile file and its source copy is destroyed (C16): from then on it is an object of the destination map, and left waiting there it stays there. Its scripts, dialog script, leader, wait and jilted flags and reaction slots are on the object itself; nothing read here ties it back to its home map **(inference: whether a dialog or script of its own reads the current map was not traced)**. | 0x4D3460 (C16), as OL31 | Original |

## Anomalies and decisions

Reconstruction slips: none in this reading. The earlier rows C11–C14, C16 and
XP5's follower step hold; C13's floated line on leaving is the refusal line
(OL20), C14's "back off" is "stop attacking" (OL10), and O4 and O7 are answered
(OL19, OL31–OL33).

### Owner-decision candidates

None is decided here. Under D8 an original bug keeps the original behaviour in
the Original setting; the Steamwoven setting changes it only when the owner says
so.

| ID | Rows | Question | Options |
| --- | --- | --- | --- |
| D1 | OL6, OL16 | The follow order never brings a follower back. | (a) Both settings as the original: follow only acknowledges or says "cannot". (b) Steamwoven: follow also unwaits waiting followers within 10 tiles (the follow check decides); Original keeps the no-op (switch). |
| D2 | OL14 | F6 does nothing. | (a) Both as the original (F6 reserved, no effect). (b) Steamwoven sends F6 to every follower in reach as "follow" (useful only with D1 (b)); Original keeps it inert (switch). |
| D3 | OL13 | The leave farewell is chosen against the line files' labels. | (a) Both as the original. (b) Steamwoven picks the friendly range for reaction above 40; Original keeps the swap (switch). |
| D4 | OL15 | The portrait menu addresses its follower by name prefix. | (a) Both as the original (first follower in reach with that name). (b) Steamwoven addresses the clicked follower itself, still within 10 tiles; Original keeps the name match (switch). |
| D5 | OL19 | The loyalty step is skipped while a follow goal lives. | (a) Both as the original: the step runs only when the follower has no action goal (needs the host to report that). (b) Steamwoven runs the step on every non-combat AI pass of a non-waiting follower; Original keeps the goal gate (switch). An observation in the running game (F4) would show how much (a) suppresses. |
| D6 | OL28 | The level message can show more than 56 points. | (a) Both as the original. (b) Steamwoven shows the stored (clamped) value; Original keeps the sum (switch). |
| D7 | OL29 | Which waiting followers get the PC's level step. | (a) Both as the original: only those in the screen-sized rectangle around the PC; the current wider rule (every waiting NPC of the current map) is narrowed. (b) Steamwoven keeps or widens it (current map, or every waiting follower wherever it waits); Original uses the rectangle (switch). |

OL22 (one stale warning test) and OL30's missing-scheme case are recorded as the
original's and need no decision.

## Implementation slices

Proposals, one PR each; each keeps Data, Simulation, Scripting and Session free
of Godot, cites the rows it implements with `[OriginalFunction]` and the
[function registry](../verification/README.md), and updates the coverage row of
its requirements. Codes were checked unused on 2026-10-21.

| Code | Result | Requirements | Rows | Depends on |
| --- | --- | --- | --- | --- |
| ARC-WORLD-045 | Wait timer and loyalty in the session: the wait event on the session queue (schedule, durations, Persuasion step, cancel paths, expiry effects, parked on its map), the three loyalty bits armed by reaction and alignment changes, check leader with its warning result, the loyalty step with the jilt and refusal result; the "has an action goal" gate as an input from the host (D5); snapshot schema bump. Oracle candidate: check leader (0x4ADB50). | FR-SOCIAL-005 | OL17–OL26 | party runtime (ARC-WORLD-013), reaction runtime (ARC-WORLD-026), event queue (ARC-WORLD-022/024) |
| ARC-WORLD-046 | Out-of-combat orders: a session entry point for one follower or all within 10 tiles (leave, wait, follow, move, stay close, spread out, walk here) with the order's state change and its line range as a result for the host; the AREA viewer's order menu and the F1–F5 presets on the pointer tile; attack, back off and curse are refused or passed to A7. | FR-SOCIAL-004 | OL1–OL16 | ARC-WORLD-045 (wait timer), follow movement (ARC-WORLD-016), D1–D4 |
| ARC-UI-046 | Level-up message: one message-window entry per level reached by the hero (title, level, points or scheme text) with sound 3005, none for followers. | FR-CHAR-013, FR-UI-005 | OL27, OL28 | experience and advancement runtimes, D6 |
| ARC-SCRIPT-016 | SCR action 139 in the dispatcher: the object operand's PCs and NPCs get the scheme number in the session (hero and NPC advancement records), no purchase until the next level. | FR-CHAR-021 | OL30 | SCR dispatcher (ARC-SCRIPT-008), advancement runtime (ARC-WORLD-036) |
| ARC-WORLD-047 | Waiting followers and the level step: the original's screen-sized rectangle on the loaded map, and the decided Steamwoven rule for waiting followers elsewhere (on another map: applied or not when their map loads). | FR-CHAR-021 | OL29, OL32 | D7, advancement runtime |
| ARC-WORLD-048 | A companion that went to another map is that map's object: the session keeps it there when it is left waiting, a map open rebinds its leader, and the viewer opens a conversation with it on that map. | FR-SOCIAL-005, FR-WORLD-010 | OL31–OL33 | map-change group (ARC-WORLD-016/017, map mode companions), ARC-WORLD-045 |

## Unknowns

- How often the loyalty step runs in play (OL19) and the share of followers that
  ever hit a refusal: needs an observation in the running game (F4).
- The dialog-file override of generated lines (0x418870, entry 11,999 + hundreds)
  and the three interface callbacks after a level-up were not traced.
- Whether a companion's own dialog or scripts read the map it stands on (OL33).
- The multiplayer phase of the broadcast (join, disband, follow other) is out of
  scope.

## Second reading

A second reader that saw only the questions re-read Q1–Q7 in the executable. Both readers agree on every
behaviour and verdict, including all seven decision candidates. The second reader added the PC's own float
(OL2), the curse line's source (OL11) and the multiplayer phase's screen-sized search. It first read keyword
116 ("stop") as having no rule entry; the installed rule file has it (back off), so this was a data-reading
difference. Details are in the [ledger section](original-verification-ledger.md#companion-orders-and-loyalty-arc-res-064).
