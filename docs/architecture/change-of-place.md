# One change-of-place request (MIG11, TM12, GR14–GR16)

Status: slice **S1 done: ARC-DEV-112** (the rules, no caller). Plan below. This note is the concept for MIG11 in the style of
[residency](residency.md) and [interface modes](interface-modes.md): what the original does (read again in the retail 1.0.7.4
`Arcanum.exe` for this slice), what the project builds, and the slices.

## 1. What the original does

Every change of place that is not a step goes through one request slot ([RL21](original-object-model.md), RL22–RL24, RL29). The
`ARC-DEV-112` reading of the executable (S1 read, below) confirms that account and adds points the earlier readers did not have (CP1, CP2, CP6, CP7).

### The S1 read of the executable

| ID | Function | Reading (prose only) |
| --- | --- | --- |
| CP1 | request 0x4D3380 | The request copies a 0x60-byte record into the one slot and sets the pending flag. It is refused (returns 0) only when a request is pending **and** the slot's flag word has 0x80000000; the request does not look at the in-progress flag. A request for the local player character (the test 0x40D9F0 on the object) sets 0x80000000 itself, **starts the fade-out at once** when flag 0x2 is set and **blanks the screen at once** when 0x20 is set (the map draw goes off). A request for any other object does none of this. |
| CP2 | ping 0x4D3330 | If a request is pending: set the in-progress flag, call the process on the slot, then clear the in-progress flag and the pending flag, then, when the slot's flag word holds 0x80000000 **and** 0x20, give the map draw back. The draw is given back whatever the process returned, so a failed change still unblanks a local request. |
| CP3 | process 0x4D3460, effects | Unless the host flag 0x62AC6C bit 0 is set (not modelled: no multiplayer host here), the process starts the fade-out again (flag 0x2), plays the sound (0x10, played even when bit 0 is set), the first movie (0x1), the second movie (0x40) and advances the clocks by the seconds field times 1000 in 32-bit arithmetic (0x8), in that order. Whether the second fade-out start of CP1 and CP3 changes what the player sees is not read (0x4BDFA0 not read); the plan has one fade-out step. |
| CP4 | process, wait orders and list | Unless flag 0x100, each direct follower for which 0x45D800 answers yes is ordered to wait (0x4AA7A0). Then the move list (0x4D3760). If a player character is in the list (flag 0x6018BC): wall-check reset (0x43CB70, 0x4384F0). If the destination map differs from the current one **and** the mover is a player character (0x40DA20): reset of the magic and technology AI table (0x459F50, 0x437980) and the queue's teleport step (0x45C720). The payload (0x4D39A0) then moves the list. |
| CP5 | payload return | Within the map the payload relocates and returns 1; for another map it writes the travellers and returns 1 whether or not a player character is in the list (failure paths return 0, RL30). The process leaves at once on 0, so no fade-in. |
| CP6 | process, after the payload | With **no** player character in the list the process jumps straight to the fade-in test; the destination is not opened. With one: the identity of the mover (a player character) or of its leader (an NPC, 0x45DDA0) is kept, for the pre-cache only; **only for another map** the destination is opened by one call that flushes, closes and opens (0x40FC70, return 0 leaves at once, RL30c), the view moves to the destination cell (0x4B8CE0) and the local player is found again (0x40D860, no argument); then, when an identity was kept and still resolves (0x4E50E0), the 3 × 3 pre-cache at the destination (0x4EF1E0) runs for both cases; a mover with no identity to keep (an NPC without a leader) gets none, which the plan does not model. The player-in-list flag is cleared in this branch before the open can fail (for S2, which owns the flag). |
| CP7 | process, end | Unless bit 0: when flag 0x4 is set, the fade-in: two calls through the pointers 0x601848 and 0x601850, then 0x51CF40, then 0x4BDFA0 on the record's fade-in arguments (the first three are not read; the plan has one fade-in step). Only a change that ran to its end gets here. A request made by a nested call while a request is being processed writes the slot that the process is still reading: the process reads the mover handle, the destination map, the destination cell and the flags **after** the payload (CP5, CP6, CP7), so it would read the new request's fields; the ping then clears the pending flag, so the nested request is lost as well. |
| CP8 | process, host gates | Before the wait orders the process calls 0x4A2BA0 and leaves at once when it answers 0 unless 0x62AC6C bit 1 is set and the destination is the current map; the answer is the multiplayer authority (not modelled; single player always continues). |

The two points that change earlier text: CP1 (the local player's fade-out and blanking begin at the request, a frame before the process) and CP2
(the draw is given back after a failed change too). CP6 settles RL22's "without a player in the list the destination is not opened" as the jump
to the fade-in, so such a change **does** fade in.

## 2. What the project builds (S1)

`Arcanum.Simulation.ChangeOfPlace` (pure rules, no session, no viewer, no caller, no schema):

- `ChangeOfPlaceRequest`: mover, caller kind, destination map (−1 the current one) and cell, flags, seconds, two mover tests.
  `ChangeOfPlaceRules.Validate` refuses what the original does not look at: empty mover, a destination map below −1, a negative cell,
  unknown flag bits, the local-player bit in a caller's word, a clock advance below 0 or over 2 147 483 s (the original would wrap in 32 bits).
- `ChangeOfPlaceSlot` with `Submit`, `Begin` and `End`: the CP1 and CP2 rules, with the request effects (`StartFadeOut`, `BlankScreen`) returned for the host.
- `ChangeOfPlaceRules.Plan(request, world, moveListHasPlayerCharacter)`: the ordered `ChangeOfPlaceStep`s of CP3–CP7. The host carries each
  step out; the plan reads no state. `EmptiesCacheBeforeRequest(caller)` says which callers empty the sector cache first (RL24: the teleport
  spell's helper and the world-map landing).

### Decisions

| ID | Decision | Why |
| --- | --- | --- |
| CD1 | An unlisted destination is refused at `Submit`, before the slot, the fade and the blanking are touched (GI7). The original runs the effects, the wait orders and a player's resets and event carry, then stops (RL30b). | The target model's rule that a failure leaves the session on its map; the screen is never left blanked. A technical bug of the game, no rule-mode switch (owner rule on technical bugs). |
| CD2 | A request made while a request that is not the local player's is being processed is dropped (`LostWhileProcessing`); the one in flight keeps its fields. | CP7: the original lets the new record change the mover and destination under the running process and then loses the new request too, so dropping it agrees with the original on "lost". No player meets this; keeping the in-flight request whole is the only defensible reading. No switch. |
| CD3 | The clock advance is bounded (the 2 147 483 s bound of section 2) instead of wrapping. | A technical limit of 32-bit arithmetic; scripts and callers use seconds and minutes. |
| CD4 | The host flags 0x62AC6C bit 0 and 1 and the authority test 0x4A2BA0 (CP3, CP8) are not modelled. | Multiplayer and dedicated-host branches; FR-NET is separate. |
| CD5 | The local player's fade-out and blanking are request effects; the plan keeps one `FadeOutScreen` step for the process's own start. | CP1 and CP3; the second start's visible effect is unread. S2 decides how the viewer plays one fade. |

## 3. Slices

| Slice | Task | Result | Depends on | Schema |
| --- | --- | --- | --- | --- |
| S1 | ARC-DEV-112 | The rules above, tests, this note. | MIG7, MIG10 (done) | none |
| S2 | ARC-DEV-113 | The session owns the slot and executes the plan for the hero's change of place (jump points, script teleports of the hero) instead of the viewer's own path; the host calls `Begin`/`End` once per frame until MIG12. | S1 | none (the slot is not saved) |
| S3 | ARC-DEV-114 | Teleport spells (cache emptied first, RL24, needs the S4 cache model of MIG9), a non-player object sent to another map without the hero and the parking of events (R3 Q5, K5). | S2, MIG9 S4 | none |
| S4 | ARC-DEV-115 | The world-map landing and the new game through the request (the blank-screen flag, RL29). | S2 | none |
| S5 | ARC-DEV-116 | The teleport map field (FM10), the homecoming of former followers on the fold's forced waypoint pass (GR15, B09). | S2, MIG9 S4 | yes (number agreed after MIG9 S6) |

Not decided here: how a frame step (MIG12) takes over the ping; where the plan's steps for the hosts live once `Arcanum.World` holds the transition coordinator (MIG15).
