# Virgil as a companion: join, follow, wait and map change

Status: **Research**, work item ARC-RES-024 (2026-10-16). This document fixes
how the pinned source specifies an NPC companion such as Virgil: the JO
consent, how the leader link is assigned, at what distance the follower keeps
up, how waiting works and how followers cross a map change. It prepares a
later companion implementation and changes no production API, viewer, dialog
port or session schema. Requirements:
[FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003),
[FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004),
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005) and
[FR-WORLD-010](../requirements/world.md#fr-world-010).

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0); the line ranges below are what was read. CE
  is a reconstruction lineage, not an original-executable observation, and
  several functions keep decompiler names (`sub_…`), so a reading of their
  intent is marked as such.
- **Local observation**: only results already recorded by merged evidence for
  Virgil are reused (named per row). This work item ran no new probe and read
  no original file.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open. Every policy below is a **proposal**.

Distances are cell distances `max(|dx|, |dy|)` (Chebyshev), which is what the
source's
[`location_dist`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L535-L556)
and `object_dist` compute. Original executable parity remains
**NOT_VERIFIED**.

## Answer

- **State** is the NPC's leader handle, the leader's ordered follower array,
  five NPC flag bits (wait here, jilted, forced follower, spread out, check
  leader), three critter bits that arm the loyalty tests (reaction bad,
  alignment good, alignment bad) and a pending wait timer. A waiting companion
  is **not** in the follower array but keeps its leader handle.
- **JO consent**: `JO a node` runs the follow check with `a` as "ignore the
  charisma limit". On success the NPC joins (forced follower when `a ≠ 0`) and
  the conversation continues at `node`. On refusal the NPC says a generated
  refusal line chosen by the refusal reason, and the player gets one generated
  reply that keeps the chosen reply's own response.
- **Follow distance**: the follow goal starts only when the leader is more
  than **4** cells away (**7** when spread out). The goal's range is then
  **2** cells (**5**): it walks while within 4 (7), runs beyond that, and
  re-checks every 100 ms. A follower more than **30** cells from its PC leader
  jumps onto the leader's cell at its next AI tick instead of walking.
- **Waiting**: wait removes the NPC from the array, keeps the leader handle and
  sets the wait flag. A waiting NPC does not follow, is not moved by the
  30-cell rule and does not go along through teleports. Rejoining runs the
  follow check again (dialog `UW`); a script follow brings it back forced.
- **Map change**: a teleport carries every non-waiting follower, recursively
  with their followers and inventories, to the **same destination cell** as
  the PC. Unconscious followers are first set waiting and stay behind. On a
  cross-map teleport the group is written into the destination map's mobile
  object file with handles turned into object IDs. The source copies are marked
  destroyed and extinct. The cave scripts use teleport flags 0, so a
  non-waiting Virgil follows the PC into the Crash Site cave and back.

## State model (source facts)

| State | Leader handle | In leader's follower array | Flags | How it is entered |
| --- | --- | --- | --- | --- |
| Free | null | no | jilted may be set | initial, leave, timeout, loyalty loss, leader's hit, failed re-join after healing |
| Following | PC | yes, appended at the end | forced may be set; jilted cleared | `JO`, `UW`, script follow, mind control and the follow spell component (both forced) |
| Waiting | PC (kept) | no | wait here | `WA`, wait order, script wait, unconscious at teleport |
| Dead | PC (kept) | no | wait cleared | death of a follower or waiting NPC |

"Following PC" in dialog `FO` and in the SCR `FOLLOWING` condition compares
only the leader handle, so it is **true while waiting** too. `WA` / the SCR
waiting condition test the wait flag, and `WT` / SCR `JILTED` test the jilted
flag.

## Findings

| ID | Claim | Source fact | Consequence for Steamwoven |
| --- | --- | --- | --- |
| C1 | JO, UW, WA, LV, SC, SO dialog actions | [dialog.c 2081–2118](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2081-L2118), [dialog.c 2119–2141](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2119-L2141), [action names 316–345](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L316-L345) | `JO a n` and `UW a n`: the first operand is passed both as "ignore charisma limit" to the check and as `force` to the join/unwait. On success the dialog continues at node `n` and skips the reply's own response. On refusal it shows the reason line plus one generated PC reply that, as we read the copied option fields, keeps the chosen reply's response type and target. `WA` waits, `LV` leaves (forced), `SC` / `SO` clear / set spread out. The conversation runtime accepts only `JO 0 node` today; `JO a≠0` and `UW`, `WA`, `LV`, `SC`, `SO` are `UnsupportedOperation`. |
| C2 | Follow check and its order | [ai.c 3131–3200](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3131-L3200), [ai.h 28–54](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.h#L28-L54) | First match wins: (1) mind control by this PC → OK; a mind-controlled NPC whose controller cannot be resolved is OK when it has a PC leader (our reading of the decompiled lookup), while one controlled by another PC falls through; (2) PC Persuasion training ≥ master → OK; (3) another leader whose charisma ≥ PC's → already in group; (4) forced follower → OK; (5) reaction ≤ AI param 5 → dislike; (6) PC alignment above NPC's by more than param 6 → too good, below by more than param 7 → too bad; (7) NPC level ≥ PC level + param 8 → "low level"; (8) unless the limit is ignored: max followers 0 → not allowed, non-forced follower count ≥ max → limit reached. Eight typed results, OK plus seven refusals. |
| C3 | AI parameter rows | [ai.c 36–59](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L36-L59), [ai.c 192–228](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L192-L228), [ai.c 236–275](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L236-L275), [ai.c 1564–1573](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1564-L1573) | The NPC's AI-data field indexes a table of 17-integer rows from `rules\ai_params.mes` (rows 0..99) and the module's `rules\gameai.mes` (rows from 100 to 149). As read, each row index is the first entry's number plus a running count, not each entry's own number. The follow thresholds are row fields 5..8. |
| C4 | Follower limit | [stat.c 376–381](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L376-L381), [stat.c 601–603](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L601-L603), [critter.c 1155–1177](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1155-L1177) | Max followers = charisma / 4 (integer), +1 at Persuasion expert or above. Forced followers do not count. Waiting followers are outside the array, so they do not count either. |
| C5 | Join | [critter.c 877–928](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L877-L928) | Only NPCs join. An unforced join re-runs the check. An existing leader is left first (forced). The NPC is appended to the leader's array and gets the leader handle; jilted is cleared and forced is set when forced. Speed is recalculated, and the NPC copies the leader's concealment and Tempus Fugit flag. A second JO for the same PC therefore leaves and re-appends; it never duplicates the entry. |
| C6 | Leave | [critter.c 935–1031](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L935-L1031) | An unforced leave is refused when the leader's charisma is extraordinary (≥ 20, [stat.c 806–826](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L806-L826)) or the NPC is mind controlled. Otherwise it clears waiting, removes the NPC from the array keeping the others' order, nulls the leader, clears forced and reveals the NPC. |
| C7 | When the follower moves | [ai.c 2403–2448](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2403-L2448), [anim.c 13782–13868](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L13782-L13868) | Each non-combat AI tick of an NPC with a leader and no wait flag asks for a follow goal. No new goal starts while the leader is within 4 cells (7 when spread out). |
| C8 | How close it gets | [anim.c 862–886](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L862-L886), [anim.c 8699–8756](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L8699-L8756), [anim.c 4503–4599](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4503-L4599), [anim.c 4767–4795](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4767-L4795), [goal stepping 3689–3790](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3689-L3790) | The follow goal's range is 2 (5 spread out). Within range, with the step toward the leader not blocked, it waits 100 ms and re-evaluates; the goal stays alive. Otherwise it pushes a walk-near goal when within range + 2, and a run-near goal beyond. Our reading of the decompiled goal table is consistent across the four step functions; the move-near goals themselves were not read. |
| C9 | AI cadence and the 30-cell rule | [ai.c 2859–2996](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2859-L2996), [ai.c 2998–3012](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2998-L3012), [object.c 2224–2264](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2224-L2264) | The next AI tick is due after `250 + 4750 × min(d, 30) / 30` ms, `d` = distance to the local PC (250 ms on the same cell, about 408 ms adjacent, 5 s at 30+). Within 30 cells (real-time only) the heartbeat script and AI run. Beyond 30, a living, active, non-paralysed, non-waiting follower is moved onto its PC leader's cell (our reading of the decompiled move helper), at that tick; it does not walk. The first tick after a heartbeat start adds a random 0–5 s. |
| C10 | Shared cell | [anim.c 14798–14895](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L14798-L14895) | When several living critters stand on one cell, one stays (a PC first) and the NPCs are asked to step away. It is called from the in-range branch of the follow request and for leaderless NPCs, not by the 30-cell move or the teleport; our reading is that a group stacked on one cell separates at the followers' next follow request. |
| C11 | Waiting | [ai.c 1456–1530](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1456-L1530), [ai.c 1246–1252](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1246-L1252) | Wait: forced leave, leader handle restored, wait flag set (no-op if already waiting). Unwait: needs a leader and the flag. Unforced unwait re-runs the follow check and stays waiting on refusal. Success clears the flag and the wait timer, then joins again (re-appended at the end of the array). Death force-unwaits the NPC before the death handling of C17. |
| C12 | Wait timeout | [ai.c 152–159](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L152-L159), [ai.c 1472–1485](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1472-L1485), [ai.c 1532–1561](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1532-L1561), [proto.c 16173](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16173) | Durations by the NPC wait field: 0, 15 min, 2 h, 1 day, 7 days, 30 days; the prototype default is index 2. Any Persuasion training of the leader adds one step, and index ≥ 6 means no timeout. On expiry the NPC drops waiting, becomes jilted, has its reaction changed by `2 × (charisma − max charisma)` (zero or negative) and loses the leader. **Source anomaly**: as read, CE schedules the timer only when the leader's charisma *is* extraordinary, and never fills the event's NPC parameter. That contradicts the source's own comment that extraordinary charisma means followers never leave of their own accord ([critter.c 944–946](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L944-L946)), and a negative reaction change is dropped for such a leader anyway ([reaction.c 275–279](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L275-L279)). The timeout is therefore an open question, not a contract (O3). |
| C13 | Loyalty while following | [ai.c 2412–2443](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2412-L2443), [ai.c 3203–3257](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3203-L3257), [reaction.c 280–298](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L280-L298), [stat.c 740–765](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L740-L765), [anim.c 3460–3488](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3460-L3488) | Only when the NPC has no active animation goal: the follow check without the charisma limit runs. A refusal plus a permitted unforced leave makes it jilted and floats a warning line. Otherwise a pending "check leader" runs a check that **only warns**. A negative reaction change while following arms the reaction test (reaction ≤ param 5 + 20). A PC alignment change arms the alignment test in its direction (gap > param 6 or 7 − 100). "No active animation goal" is our reading of the decompiled goal test; because the follow goal of C8 stays alive, how often this check runs during normal following is open (O4). Separately, when the PC leader hits its follower, reaction drops by param 9 unless the hit is accidental, and the same charisma-free check can make it leave, jilted, with the refusal line ([ai.c 996–1015](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L996-L1015)). |
| C14 | Orders | [broadcast.c 184–370](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/broadcast.c#L184-L370), [follower_ui.c 28–36](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/follower_ui.c#L28-L36), [follower_ui.c 674–707](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/follower_ui.c#L674-L707) | Orders reach only followers in the array within **10** cells of the PC: leave, wait, follow, move, stay close, spread out, curse, attack, walk here, back off. Mind control blocks leave, wait and curse. The portrait menu offers walk, attack, stay close, spread out, back off, wait, inventory and character sheet; there is no "follow" item. A waiting NPC is outside the array, so orders cannot reach it; dialog `UW` or a script follow (C17) brings it back. |
| C15 | Teleport carries followers | [teleport.c 164–271](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L164-L271), [teleport.c 280–365](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L280-L365) | Unless the skip-followers flag is set, unconscious followers are set waiting first. Then the walk always schedules the teleported object, its non-waiting followers (recursively, at the **same destination cell**, at any distance from the PC) and every inventory item. An inventory count mismatch stops that walk before the object's own node is added (nodes are added after their followers and items); the caller ignores the result, so the teleport goes on with what was scheduled, possibly without the PC itself. As read, the skip-followers flag (used by spells) skips only the unconscious step, not the recursion. |
| C16 | Cross-map payload | [teleport.c 369–450](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L369-L450), [teleport.c 452–476](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L452-L476), [teleport.c 478–523](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L478-L523), [obj.c 1170–1186](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L1170-L1186) | Cross-map: each scheduled object has its AI timer, combat mode and transient links (focus, last hitter, enemy list, fleeing) cleared and its handles converted to object IDs. It is set to the arrival cell with zero offsets and appended to `Save\Current\maps\<destination>\mobile.mdy`. The live copy is marked destroyed and extinct, and the destination map opens afterwards. Same map: each non-inventory object is moved to the cell. Waiting followers stay on the source map with their leader handle. |
| C17 | Other paths that change the link | [script.c 1666–1675](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1666-L1675), [script.c 2121–2125](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2121-L2125), [critter.c 722–727](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L722-L727), [script.c 972–990](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L972-L990) | Script follow is always forced; script disband is forced; script wait uses the same wait. Death removes the NPC from the array but restores the leader handle. Scripts can ask whether a PC is at the follower limit. Mind control and the follow spell component join forced ([magictech.c 4081](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4081), [magictech.c 2133–2136](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2133-L2136)). After healing, an NPC with a leader re-runs an unforced join: success re-appends it and clears forced; as read, refusal only nulls the leader and leaves its array entry in place ([combat.c 2176–2182](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2176-L2182)). |
| C18 | Conditions that read the state | [dialog.c 1639–1650, 1725–1750, 1763–1784](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1639-L1784), [script.c 869–880](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L869-L880), [script.c 1269–1290](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1269-L1290), [script.c 1473–1490](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1473-L1490) | Dialog `FO 0/1` = leader is / is not this PC; `WA 1/0` = waiting / not; `WT 1/0` = jilted / not; `PA n` = a non-mind-controlled follower in the array has name `n` (`PA -n` = none). SCR: following PC (leader equals PC), jilted, waiting for leader. |

## Virgil (local observations from merged evidence)

| ID | Observation | Evidence |
| --- | --- | --- |
| V1 | Virgil's dialog file (payload SHA-256 `34a49a2c…5e53a`) contains actions `JO:21`, `UW:4`, `WA:2`, `LV:2`, `SC:2`, `SO:2` and conditions `WA:12`, `PA:2`. The JO/UW operands were not tabulated. | Aggregate in [dialog evidence](dialog-evidence.md#bounded-local-structural-result); the hash is the Virgil DLG of [NDE-06](crash-site-npc-dialog-evidence.md). |
| V2 | His SAP_DIALOG script 1324 tests "NPC following this PC" in SCR line 0 and jilted in line 2. A follower enters at SCR line 6 → node 385, which the conversation runtime stops at a `WA` condition frontier. Under C18, `FOLLOWING` is also true while he waits, and `WA` then separates waiting from following. | [entry evidence](crash-site-npc-dialog-evidence.md#conditional-caller-and-entry-rule) and its operation ledger, [conversation runtime](../scripting/dialog-conversation-runtime.md#area-viewer-integration-project-diagnostic). |
| V3 | The first-Direct walk applies one `JO` and continues; the diagnostic PROJECT profile accepts every follow request. Nothing measured his AI-data row, level, alignment or wait field. | [Conversation runtime](../scripting/dialog-conversation-runtime.md#area-viewer-integration-project-diagnostic). |
| V4 | Both cave scripts teleport the triggerer with flags 0, so by C15 a following (not waiting, conscious) Virgil arrives on the PC's arrival cell: (2087,2018) in the cave, (92913,82598) on return. | [Cave transition T2/T4](cave-transition-evidence.md#findings). |

## Open questions

- **O1** *Answered by [ARC-RES-026](virgil-follow-consent.md):* AI row 5
  (dislike ≤ −1000, alignment gaps 2001, level gate 30), level 1, alignment 1,
  wait index 6, reaction base 60, no forced/aloof bit. A real PC can be refused
  only at his follower limit (or by another leader).
- **O2** *Answered by [ARC-RES-026](virgil-follow-consent.md):* all 21 `JO`
  and 4 `UW` have first operand 0 (not forced, charisma limit applies); nodes
  74, 535, 547 and 518.
- **O3** The wait-timeout anomaly of C12 (condition and missing NPC parameter).
  It needs an original-executable observation or a second independent source
  before it becomes behaviour. Virgil's wait index 6 schedules no timer, so it
  does not affect him.
- **O4** How often the C13 loyalty check runs while a follow goal is alive.
- **O5** The move-near / run-near goals (so the exact stopping distance),
  speed recalculation and the follower in combat or turn-based mode were not
  read. The 100 ms step costs 2 action points, which only matters in
  turn-based mode.
- **O6** Virgil's other five occupied script slots (for example heartbeat or
  new-sector reactions while following) were not decoded.
- **O7** How the destination map rebinds the follower array and leader IDs on
  open, and how a waiting follower left on another map is shown, were not read
  beyond the ID conversion of C16.

## Proposed implementation slices (project policy, not accepted)

Each slice is a separate PR. All keep Data/Simulation/Scripting/Session free
of Godot.

1. **Party state in the session owner**: leader link, ordered followers and
   the five NPC flags, the three loyalty-test bits and the wait timer per NPC, with guarded join / leave / wait / unwait
   transitions following C5, C6 and C11. Add them to the snapshot schema as a
   new version. Waiting stays a separate state with its leader kept.
   Synthetic tests: join, rejoin without a duplicate entry, leave refusal,
   wait/unwait, restore.
2. **Consent evaluator**: a pure function over explicit inputs (C2–C4) that
   returns OK or one of the seven reasons. Then bind `JO a n` / `UW a n` in
   the conversation world port so a refusal becomes the reason line, not a
   frontier. Real parameters wait for O1; until then the diagnostic profile
   stays labelled as such.
3. **Follow movement** in Simulation for one follower: start beyond 4 (7),
   close to 2 (5), walk within range + 2, run beyond, re-plan every 100 ms
   game time, the 30-cell move and shared-cell separation. Reuse the
   click-route planner and exact movement for a second actor, then let the
   AREA viewer draw Virgil keeping up.
4. **Cross-map group**: the cave transition places every non-waiting follower
   on the arrival cell and separates them, and leaves waiting followers on the
   source map. This is the FR-WORLD-010 A→B→A acceptance with one entity per
   identity.
5. Later: orders (C14), loyalty and timeout (C12–C13, after O3/O4), Persuasion
   and charisma effects once character rules exist.

## Method

Source reading only, at the pinned commit above, through the call chains named
in each row. No CE code or structure was copied; names in this document are
descriptive. The open questions are the parts this reading could not settle.
