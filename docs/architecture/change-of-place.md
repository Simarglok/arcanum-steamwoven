# One change-of-place request (MIG11, TM12, GR14–GR16)

Status: slices **S1 done: ARC-DEV-112** (the rules), **S2 done: ARC-DEV-113** (the session runs the hero's change) **S3 done: ARC-DEV-114** (the
session's part of the teleport spell, a non-player mover without the hero, its events) **S4 done: ARC-DEV-115** (the world-map landing and the new game) and **S5 done: ARC-DEV-116** (the teleport map and the homecoming of former followers). Plan below. This note is the concept for MIG11 in the style of
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

| CP9 | fade-and-teleport 0x449A85 (S2 read, ledger TR2) | The script action starts the flag word at 0x6 (fade-out and fade-in), adds 0x8 when the seconds operand is positive, 0x10 when the sound operand is positive and 0x1 (the first movie) when the movie operand is not zero (an unsigned test), then makes the request. A plain teleport (action 44, TR1) and a jump point (TR3) request with flags 0. |

The two points that change earlier text: CP1 (the local player's fade-out and blanking begin at the request, a frame before the process) and CP2
(the draw is given back after a failed change too). CP6 settles RL22's "without a player in the list the destination is not opened" as the jump
to the fade-in, so such a change **does** fade in.

### The S3 read of the executable

| ID | Function | Reading (prose only) |
| --- | --- | --- |
| CP10 | movement component 0x4524C0, teleport helper 0x452650 | The spell's movement component switches on its type (0 to 3; the jump table is at 0x452640). Types 0 (the target tile) and 1 (a random tile in a radius, 0x4F4E40) end in the **immediate relocation** 0x455350 of the object the component applies to, with no request and no cache pass. Type 3 calls the helper 0x452650 with that object. Type 2 checks the anti-teleport zones itself (0x4BDD30): in the host branch (flag 0x62AC6C bit 0 set) it requests flags 0x120 to the current map from the start location (0x410280) without emptying the cache; its single-player branch calls 0x4608b0, which was not read. In the helper, after the anti-teleport check (a refusal shows message 10000 to the local player and nothing to another object), a **single-player** run (bit 0 clear) empties the sector cache (0x4D0BC0, argument 0) and requests the object's change of place with **flags 0x120** (no wait orders and blank screen: no fade, no movie, no clock advance) to **entry 1 of the map list** (0x40FF50 with 1; 0x40FF40 is the current map) at the location of the chosen area (0x4CAED0); the host branch empties the cache too and requests the current map. The object is the component's current target, so it may be the hero or any other object. |
| CP11 | payload for a mover with no player character (RL22), AI-event removal 0x4AD7D0, combat exit 0x4B3F20, preparation 0x4D3D60 / 0x4D3E20 (FM6) | A non-player mover (a spell target that is an NPC, a waypoint pass) has **no player character in its move list**, so the destination map is not opened and the hero stays where it is: the objects of the list are prepared (the danger links are cleared for both cases; combat mode is left and the AI events are removed on another map only), written to the destination's dynamic file and the live copies left destroyed. The queue's teleport step (RL28) runs only for a player character that changes map: nothing is carried or parked for a mover that is not one. The mover is always an object of the open map (a live handle exists for no other object). |
| CP12 | dispatcher 0x45B610 (RL15), RL22, R3 Q5 first half | The live copy of a non-player mover stays in memory destroyed and extinct until the open map closes, and the events that named it die when the dispatcher finds their object destroyed; the arriving copy has none, and its heartbeat starts at the first fold of its sector on the destination (RL17). So the NPC's life timers and a wait timer are lost with the old copy; this is read from the dispatcher's rule, not from an execution of a move, and counts as the original's behaviour (F2). |
| CP13 | world-map landing 0x561860 (S4 read, ledger WT12) | The helper empties the cache (0x4D0BC0 with 0), then requests for the local player character (0x40DA50) into the map the map list flags as the start map (0x40FF50 with 1) at the landing location with the flag word **0x20 alone**: no fade, sound, movie or clock advance. There is no comparison with the current map, so the request is made even when the player is already on that map (a same-map request). |
| CP14 | new game start 0x5412E0 (S4 read, ledger OS5) | With the new-game flag set (and the host flag 0x62AC6C bit 0 clear) the routine starts its own fade-out (0x4BDFA0), looks up the start map (0x40FF50 with 1) and its location (0x40FF70) and requests the local player character's teleport with the flag word **0x45**: the first movie, the second movie and the fade-in; **0x2 is not set**. |
| CP15 | forced route step tail 0x4ACBB0 (S5 read, ledger WS6) | The request of a forced waypoint step on another map is made from a record built on the step's stack: its flag word is written **0**, its object is the NPC, its location the step's target (the waypoint, or the standpoint for the standpoint step) and its map the NPC's teleport map; the other fields of the 0x60-byte record (the movie, fade and sound arguments, the seconds) are not written and none is read with flag 0. So the homecoming is a plain request: no fade, sound, movie, clock advance or blanking, and (flag 0x100 clear) the wait orders and the move list of the mover run, which for an NPC with no followers hold nothing. The request is made whatever the slot holds, and only a pending local-player request refuses it (CP1). The step answers 1 either way. |
| CP15b | standpoint tail 0x4ACDB0 | The standpoint step ends in the same forced tail (WS6); its own record fill was not read separately, so flags 0 for its request is taken from the waypoint step's (CP15). |
| CP16 | teleport map in the preparation 0x4D3D60 (S5, ledger FM6, FM10) | The preparation every object of a change of place gets (both cases) calls the teleport map getter (0x45F6D0) for an NPC, which writes the open map into field 243 while it is 0, so the first change of place an NPC makes, a follower's carried with the hero or an NPC's own, ties it to the map it leaves. A leaderless NPC the fold's forced pass meets on another map is then sent back to that map. |

CP10 settles that a teleport spell has two paths: a same-map tile move that needs no request, and the area path (the helper) that is a change of
place with the cache emptied before it. The component, the area choice and the anti-teleport zones are MT16 (ARC-MAGIC); this slice builds the session's
part that the area path and every non-player request need.


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

## 3. What the session does (S2)

`GameSessionOwner` (`src/Arcanum.Session/GameSessionChangeOfPlace.cs`) holds the slot (not saved: no schema change) and runs the plan:

- `RequestHeroChangeOfPlace(caller, map, cell, flags, seconds, listed)`: the hero's request (the local player character), through
  `Submit`; it writes the slot only, no revision. `QueryChangeOfPlace` and `ChangeOfPlaceInProgress` read the slot.
- `ProcessChangeOfPlace(host)`: the ping; until MIG12's frame step calls it once per frame, the viewer calls it in the frame of its own
  request, right after its transfer checks (CD7). It takes the request (`Begin`), plans it (`Plan`; the mover is the hero, so the move list holds a player
  character) and runs the steps in order; for each step the session's part comes first, then the host's (`IChangeOfPlaceHost.Run`, false
  stops the change there). The session's parts: the clock advance (`AdvanceClocks`, game time by the seconds × 1000 as an explicit wait,
  so a round does not hold it; the animation clock is not the session's before MIG12), the move list (`BuildMoveList`, with the host's
  facts and the request's "no wait orders"), and the payload (`RelocateObjects` or `WriteTravellers`): the hero at the host's arrival
  (`IChangeOfPlaceHost.Arrive`: the cell, the facing, the followers' body edits) with the move list in one revision of `ApplyChanges`,
  which writes the wait orders, FM6's clearing and the carry of the travellers' events together (the plan's `WaitOrders` and
  `CarryEvents` are written there). The move list is the session's whatever the host has: the viewer passed none before S2 when it had
  no companion group, now its wait orders, FM6's clearing and the event carry always apply. `KeepPlayerId` keeps the hero, which always
  resolves. Every other step is the host's alone.
- Another map: before its payload the host readies the destination (`ReadyDestination`): `Ready` goes on, `Failed` stops the change
  before its payload is written (a clock advance already made on the old map stays, as in the original), `Pending` suspends it (the slot stays in progress, later requests are refused) until
  `ResumeChangeOfPlace(host, ready)`, which writes the payload and runs the rest, or stops with none of it written (CD6). A host may resume from inside `ReadyDestination`.
- A host that throws, or answers `Arrive` without edits, ends the change (stopped) before the exception goes on: the slot is never left
  in progress.
- `ChangeOfPlaceRun` reports the status (`Completed`, `AwaitingDestination`, `Stopped`, `Refused`, `Idle`), the steps run, the step that
  stopped it, the failure (`ChangeOfPlaceStopped` for a host stop or a destination not readied) and whether the host gives the map draw
  back (`End`, CP2). The slot is emptied when the change ends, however it ends.

The viewer (`TerrainViewerChangeOfPlace.cs`) has no path of its own any more for the hero's transfers: a map-mode transfer (a jump point,
a use, a tile or sector script's teleport of the player) and the Crash Site cave's endpoints are requested once the actor is settled and
pinged in the same frame. The caller and flags follow CP9 (`AreaChangeOfPlace`). A same-map change and the cave pair (both maps loaded)
run in one ping; another map of map mode is readied by loading its scene, and the published map resumes the change (on the origin loaded
again after a failed load, the change stops with none of its payload written and the actor stands where it started; a published map
that is not the request's destination stops it too, and the origin is loaded again). The viewer's parts are the
coordinator's map switch at the cave's open, the new grid session, camera and sound once the run returns, and the load for another map;
the fades, the sound, the movies, the pre-cache and the resets of the wall check and of the magic and technology AI table answer yes and
draw or do nothing (NOT READY: presentation, B16; the pre-cache is TM5's). The request's at-once effects (CP1) are not drawn either.

### Decisions (S2)

| ID | Decision | Why |
| --- | --- | --- |
| CD6 | Another map's payload waits for the host's destination (`ReadyDestination`); a destination that cannot be readied stops the change with none of the payload written (the clock advance on the old map, which comes first, stays; the viewer's companions' places recorded on leaving stay recorded). The original writes the travellers first and is left half moved when its open fails (RL30c). | The target model's failure rule (TM12, GI7): a failed change leaves the session on its map. The viewer's open is an asynchronous scene load over several frames; on success the order of the session's writes is the original's, since the session is the only state and nothing reads it while the scene loads. A technical bug of the game, no rule-mode switch. |
| CD7 | The viewer pings in the frame of the request, not at the start of the next frame (CP1). | Until MIG12 there is no frame step that runs the ping first; with no fade drawn the frame of difference is not visible, and the actor is settled and input waits while the request is pending. |
| CD8 | The arrival cell of another map is the one the loaded map admits (its start when the authored arrival is not admitted), as before S2. | The original places the player without a passability check (W9); the viewer's grid needs an admitted cell. Unchanged behaviour, recorded as a host answer of `Arrive`. |

## 4. What the session does for other movers (S3)

`GameSessionOwner` (`GameSessionChangeOfPlaceMovers.cs`, `GameSessionChangeOfPlace.cs`):

- `RequestChangeOfPlace(mover, caller, map, cell, flags, seconds, listed)`: the request of a map NPC. It writes the slot only, starts no fade and
  blanks nothing (CP1), overwrites any pending request that is not the hero's and is refused only by the hero's own pending one (`Submit`'s rules
  already said so). The mover must be an NPC entity (a map object that is not the hero); anything else is a caller's bug and throws.
- A teleport spell's or a landing's caller **empties the sector cache before its request**, for the hero's and for a non-player's alike
  (`EmptyCacheBefore`, `EmptySectorCache`, `SectorCacheRules.EvictUnlocked`): every loaded sector of the open map enters the eviction history at the
  game time of the request, nothing is held, and a later load of a sector folds as a fresh one (RL8). A request that the project refuses (CD1)
  never reaches the original's slot, so it empties nothing; a request that the hero's pending one refuses has emptied the cache, as the
  helper empties first and requests after. Derived data: no revision, not saved. `ChangeOfPlaceRules.TeleportSpellFlags` is the helper's word 0x120.
- **Pre-cache.** When the hero's change ends on the open map and the session holds that map's sector cache, the `PreCache` step touches one cell of
  each sector of the 3 × 3 around the hero's arrival (`PreCacheArrival`, the same list the viewer's arrival touch uses), so a cache emptied by a
  teleport spell holds what the pre-cache loaded and nothing else (RL24, CP6). A refused touch is left to the view's own touch. On another map the
  hero's map change has dropped the cache, and the host opens the destination's own.
- **A non-player mover's change** (`ProcessChangeOfPlace` and `Plan` with no player in the list): the steps are the effects of the request, the
  wait orders and the move list, then the payload, then the fade-in when asked; no wall reset, no event carry, no key kept, no flush, close, open, view
  move, pre-cache. The destination is not readied: `IChangeOfPlaceHost.ReadyDestination` is for the hero. The move list is the mover alone with its
  inventory, which stays with it (the session models no NPC leader, TM10); a host that reports followers of the mover, or a mover that is a member of the hero's party, stops the change before anything
  moves (`ChangeOfPlaceStopped`, NOT READY: NPC groups and a companion sent alone).
- The payload (`MoverPayload`, `ApplyChangesCore` with an `NpcDeparture`) is one revision: the host's placement edits for the mover (`Arrive`; the
  cell and the facing of its answer are the host's record, the session writes the edits), the mover's danger links cleared (FM6, both cases) and, on
  **another map only**, the mover leaves combat mode (its target unlocked, as a combat-mode exit does), loses its AI tick (FM6, both settings) and its
  own events (the ones that name only it: its life timers, a wait timer, a restock timer) are handled by `NonPlayerMoveDropsEvents`: Steamwoven moves them to the destination map, where they are
  parked until the hero is there; the Original setting drops them (CP12). A refused edit or an event the queue cannot take stops the change with nothing
  written.

### Decisions (S3)

| ID | Decision | Why |
| --- | --- | --- |
| CD9 | The empty-the-cache pass of a teleport caller runs in the call of its request unless the project refuses the request (validation or an unlisted destination, CD1); the hero's pending request, which refuses it in the slot, does not undo it. | CP10 and RL24 (the helper empties first, then requests); CD1's refusals never reach the original's slot. |
| CD10 | A non-player mover's payload writes only what the host's `Arrive` gives plus the session's own clearing; the session does not know where an NPC's home map or stored cell is. The cell and facing the host answers are its record. | The NPC's place is the viewer's vocabulary of `npc.map`, `npc.cell` and `npc.home` (ARC-WORLD-017, ARC-DEV-079); the session has no per-NPC home, and a caller (MT16, S5's homecoming) will have a host that does. |
| CD11 | The events that name the mover and nothing else (its life timers, a wait timer, a restock timer, a deferred call with only it) move with it (Steamwoven) or are dropped (Original, `NonPlayerMoveDropsEvents`); the AI tick goes in both settings. An event that also names another object is left where it is. | CP12, RL15: the original loses them with the destroyed copy, which a player would take for a bug (a poisoned NPC that never recovers); the session's one-map rule for an NPC's timers already carries them to the map the NPC is on. A switch of the Original setting (owner rule on bugs of the original), listed in [rule modes](../world/rule-modes.md). |
| CD12 | Not modelled by a move without the hero: the spell runs of the mover are dropped by no setting (Steamwoven moves their events with the caster; the Original setting keeps them, as the run table would be left holding runs without events), a running turn-based round the mover takes part in is not unwound, the other NPCs' danger links that name the mover are not cleared (the original nulls them when their map opens again, GR36, which `OpeningMapNullsLinksToLeftObjects` already covers), and a member of the hero's party is refused (the party's own transfer assumes its members stand where the hero does) like a mover with followers. | The original drops every event that names the destroyed copy (RL15), but nothing here reads the effect on spell runs and on a round, and the viewer has no caller yet that sends an NPC away. Open: runs and groups (the sweep that reads RL28's run files for a non-player). |

## 5. What the session and viewer do for the world-map landing and the new game (S4)

The two callers that are not transfers make the hero's request through the S2 slot; nothing in the session's plan or the rules changed.

- **World-map landing** (`TerrainViewerApp.RequestWorldMapLanding`): `RequestHeroChangeOfPlace(WorldMapLanding, startMap, landing, BlankScreen, 0, ...)`
  and the ping in the same frame, with `LandingHost`. On the start map the plan is a relocation: the session builds the move list (wait orders,
  FM6) and writes the hero on the landing cell (the original places it without a passability check, W9); the followers stay in their rows.
  The viewer then loads the map window around the landing and stands the actor and the followers that travel on the cell the window admits
  (the shipped stand-up of a window move, CD8's rule). From another map (the debug teleport of ARC-DEV-050) the plan writes the
  travellers on the start map: the scene load is the destination readied (CD6) and its arrival resumes the change, as a jump point's does.
  The landing's caller empties the session's sector cache before the request (S3, `EmptyCacheBefore`, RL24); the viewer opens no cache, so
  there is nothing to empty.
- **New game** (`Arcanum.TerrainViewer.Core.AreaNewGameStart`): after the wield (OS5) the start requests the hero's teleport to the start
  map with `NewGameFlags` (0x45). A new character that went through the opening shop of ARC-UI-038 has its session on the shop map (a
  separate map-list row, the original's shopping map), so its plan writes the travellers on the start map with the destination ready at
  once (the start scene is shown already). A pre-made hero and a new character without the shop have their session on the start map, so
  the plan is a relocation to the start cell. Whether the original's routine also requests for a pre-made hero was not read (the
  new-game flag 0x64C418 is set in the shop window's refresh, OS2); the viewer requests for every new game, its own choice (CD15).

### Decisions (S4)

| ID | Decision | Why |
| --- | --- | --- |
| CD13 | On the start map the landing writes the hero on the landing cell before the window loads; the viewer's stand-up on the admitted cell follows as a second placement. A failed window load puts the hero back on its origin cell (the origin map loads again); the followers keep their cells (the payload's edits do not move them), but the move list's wait orders and FM6 clearing, written with the payload, stay (as a same-map relocation of the original leaves them). | CP13 and RL24: a same-map landing relocates, and the viewer cannot place on a cell outside its loaded window; the origin's session is never left with the followers on a cell no scene shows. A viewer rule, no rule-mode switch. |
| CD14 | The sector cache the helper empties before the request (RL24) is emptied by the session (S3, CD9) in `RequestHeroChangeOfPlace`; the viewer opens no session cache (K1 §3.3, TM5), so the landing empties nothing here, and no pre-cache touch follows. | The cache is derived data; the viewer's own view touch stays the viewer's until it opens a cache. |
| CD15 | The new game requests for every new game, including a pre-made hero or a new character without the shop, whose session is already on the start map (a same-map request). The routine reads no map to compare (CP14), but whether it runs for a pre-made hero was not read. | A viewer choice: one start path for every new game, and the same-map plan runs the move list and the resets a new game's hero meets in the original. |
| CD16 | The request's at-once effects (the blank screen of the landing) and the movies and fade-in of the start are answered by the viewer without drawing (NOT READY: presentation, B16). | As S2: no fade, sound, movie or blanking is drawn. |

## 6. What the session and viewer do for the homecoming of former followers (S5)

A former follower is a companion the hero released (dismissed, cursed or lost) on a map that is not its own. The original ties every NPC to one map
through its teleport map (field 243, FM10) and sends a leaderless NPC back there on the forced waypoint or standpoint pass of its sector's fold
(RL17, WS1, WS6): the open map is the destination only for an NPC on its own map, otherwise the pass **requests the NPC's change of place** to its
waypoint or standpoint on the teleport map (CP15).

- **The teleport map** is object state of the NPC's directory entry: the mobile object field `npc.teleport` (schema 69), a record that names the
  teleport map and holds its number, `GameSessionOwner.QueryTeleportMap` and `EnsureTeleportMap` (the getter's write: while it is unset the open map
  becomes the tie, once). It is written by the change of place's preparation (CP16, FM6): every traveller of the hero's change (same map or another), with the
  hero's map at that moment, and a non-player mover, in the same revision as the move (`ApplyChangesCore`); and by the first route step of a leaderless
  NPC (WS3). An entity that is not a mobile object (`oid:`) takes none.
- **The route steps of a companion** (`AreaAiHost`, `AreaAiHostRoutes.cs`): the NPC rows of the shown map's companions (a visitor's row is its home map's) are
  route NPCs like any other, with the session's teleport map (the row's own is the baseline). A forced pass of a free companion on its teleport map puts it on its
  waypoint or standpoint through its own walk (`IAreaAiFollowers.Jump`); a following companion has a leader, so the standpoint step is not its own and the
  caller's tick rules do not run the waypoint step for it.
- **The request** (`SendHome`): the step's `RequestTeleport` outcome becomes `RequestChangeOfPlace` with the caller `NpcWaypoint`, flags 0 (CP15), the NPC's
  teleport map and the step's target. Steamwoven makes the request in the next frame, one per frame (`FlushHomecomings`; the waiting list is the viewer's and survives a rebuilt AI host); the Original setting makes it at
  once over any pending non-player request (`NonPlayerRequestsOverwriteEachOther`, RL21: the two NPCs that go home in one fold lose the first).
- **The ping** (`ProcessNpcChangeOfPlace`, per frame after the AI pump, until MIG12's frame step): a pending request that is not the hero's runs through
  `HomecomingHost`: the session's non-player plan (S3), the host's `Arrive` the companion group's own edits for the move (`AreaCompanionGroup.Arrive`: its
  placement on the map it leaves cleared, its cell on the destination, its home map's away change cleared when the destination is home); the destination
  is not opened and the hero stays. The group is bound again, so the destination's scene no longer holds the companion, and the home map shows it on
  the cell the step chose when it opens.

### Decisions (S5)

| ID | Decision | Why |
| --- | --- | --- |
| CD17 | The teleport map is an object field, saved (schema 69), and the baseline is the row's own field 243 (0 in every row of the installed maps, ledger WS12), not a project default. | FM10: one tie between an NPC and a map, written by every teleport and read by the route steps; a save must keep it or a loaded game forgets where a former follower belongs. |
| CD17b | A save of an older schema has no teleport map for a companion that was already released on another map: its first forced pass there ties it to that map, so it does not go home. | The original's field was set at the teleport the older session did not record; nothing in the older save says where it came from. Only new carries tie it. |
| CD18 | Both rule settings send a former follower home; there is no switch for the homecoming itself. | GR15 and the gap review: an original rule, not a slip of the game. |
| CD19 | Under Steamwoven the requests of NPCs wait for the slot and are made one per frame; the Original setting lets the last of two requests in one fold overwrite the first (RL21). | A player would take the lost first request for a bug (a companion left on the wrong map until the next fold): a switch of the Original setting (owner rule on bugs of the original), listed in [rule modes](../world/rule-modes.md). |
| CD20 | The route steps compare the NPC's own cell with the target as raw coordinates, as the original does (WS4, WS5, WS9): a former follower on the destination whose cell lies within the standpoint radius of its home map's standpoint counts as at its standpoint and is not sent. | The original has no map in the test; a technical oddity of no practical weight (the maps' cell ranges overlap rarely and the radius is 1 or 4), kept as the original has it. |
| CD21 | Only the shown map's companions are moved by the viewer; the NPC of the shown map that is not a companion never has another teleport map than the shown one, so the request has no viewer caller for it. | The viewer holds no NPC that stands on a map other than its own except the companions the hero carried (TM3, MIG1). |

Not done here: the groups of an NPC and its followers (no NPC leader, TM10), the unforced pass of the original (a walk goal by day, the hour-6 and hour-21 rolls, WS6, WS7)
and the fold's re-fold after 3 s on the map where the NPC is not the hero's neighbour (the viewer's fold runs when its sector loads).

## 7. Slices

| Slice | Task | Result | Depends on | Schema |
| --- | --- | --- | --- | --- |
| S1 | ARC-DEV-112 | The rules above, tests, this note. | MIG7, MIG10 (done) | none |
| S2 | ARC-DEV-113, **done** | The session owns the slot and executes the plan for the hero's change of place (jump points, script teleports of the hero) instead of the viewer's own path; the viewer pings in the frame of its request until MIG12 (section 3, CD7). | S1 | none (the slot is not saved) |
| S3 | ARC-DEV-114, **done** | The session's part of the teleport spell (the caller word 0x120, the cache emptied first, the 3 × 3 pre-cache that follows), a non-player mover's request and change with no hero (the destination not opened, the danger links, the AI and combat exit, the events kept or dropped: section 4). No viewer caller yet: the movement component is MT16 and the homecoming is S5. | S2, MIG9 S4 | none |
| S4 | ARC-DEV-115, **done** | The world-map landing and the new game through the request (the blank-screen flag, RL29; section 5). | S2 | none |
| S5 | ARC-DEV-116, **done** | The teleport map field (FM10, schema 69), the homecoming of former followers on the fold's forced waypoint pass (GR15, B09; section 6), the Original switch for two requests in one fold (CD19), the smoke stage `follower-home` (SM29). | S2, MIG9 S4 | 69 |

Not decided here: how a frame step (MIG12) takes over the ping; where the plan's steps for the hosts live once `Arcanum.World` holds the transition coordinator (MIG15).
