# Followers across maps: the original's object move and the visitor model

Status: **Research, its fix built**, work item ARC-RES-076 (2026-10-29). Docs-only: no code
changes and no checks run. It answered the owner's question after ARC-DEV-065
(PR #582): a companion carried onto another map (Virgil in the Crash Site cave)
followed there but neither fought nor was fought, because the shown map had no
combat data for it. This note reads in the retail executable how party
followers move between maps and sectors, what a save keeps and which AI and
combat state travels. The fix slices V1-V6 are all built (see [Fix plan](#fix-plan)).

Requirements: [FR-WORLD-010](../requirements/world.md#fr-world-010),
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

Earlier results reused, not re-read here: the teleport walk and payload
([companion study](companion-follow-evidence.md#findings) C15, C16), followers'
links across maps ([OL31–OL33](companion-orders-loyalty-evidence.md#7-followers-across-maps-q7-companion-o7)),
NPC inventory across maps ([NI1–NI7](ledger/items.md#npc-inventory-changes-arc-world-055)),
the event queue's map scope ([P1–P7](game-event-queue.md)) and the sector cache
([SS1–SS4](main-map-streaming.md#how-the-game-keeps-sectors)). This reading
confirms them where it touches them and adds identity, sector eviction, the
same-map case and the one tie an NPC keeps to a map.

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed for this reading), read statically with
`llvm-objdump` following the [original executable check](original-executable-check.md),
by two readers that each saw only the questions and the starting addresses,
not the earlier answers (method step 6). Object fields, flags and time-event
types were named from the executable's own tables (fields 0x59BEA8, the
OF_/OCF_/ONF_ name tables, event types 0x5B2188). Every direct branch of the
functions read stays inside `.text` (1,835 and 1,182 branches checked); the
indirect calls go to the sector load and save hooks, event callbacks, jump
tables and the `CoCreateGuid` import, none to `.IIDKing`. Both readers agree on
every row below unless the row says otherwise; rows marked F1 were settled by
one reader only. Everything is read from the executable unless marked
**(inference)**. Steamwoven facts come from the code on `origin/main` at
a156e143.

## Answer

- **The original has no visitors.** A follower carried to another map is
  written whole, with its own unchanged ID, into the destination map's dynamic
  object file; its source copy is destroyed and the source map remembers its ID
  as deleted. From then on it is an ordinary object of the destination map, with
  every field it had: stats, hit points, inventory, scripts and their local
  data, reaction, AI flags, leader. Combat, AI and scripts treat it like any
  other object of that map. Back on its first map it is again a dynamic object
  of that map; its original data record never comes back.
- **What the move clears** is transient fight state (combat focus, last
  hitter, enemy list, fleeing, substitute inventory, combat mode, its pending AI
  events, animation). When the PC changes maps, its time events travel with it
  when all the objects they name travel (an NPC teleported alone only has its
  record moved); everything else stays on the object.
- **One tie remains:** the NPC's teleport map (field 243), set once to the
  first map it was teleported from. A *former* follower (dismissed or jilted)
  left on a foreign map walks home: the next forced waypoint or standpoint pass
  teleports it to its standpoint on that map. A follower with a leader is never
  sent back.
- **Steamwoven's gap was systemic** (the NPC's base record was found only through the shown map's own rows, so every system that
  resolved "the row of entity X on the shown map" failed for a carried companion). It was closed by one identity-based record
  resolver and the slices V1-V6 ([Fix plan](#fix-plan)); "visitor" is no longer a mode.

## 1. The original mechanism

### Rows

| ID | Function (address) | Original 1.0.7.4 | Level |
| --- | --- | --- | --- |
| FM1 | teleport request and run (0x4D3380, 0x4D3330 → 0x4D3460), move list (0x4D3760, head 0x601840) | A teleport request is queued and run later. The move list holds the object, every item of its inventory (critter fields 237/238, container 59/60; a count that differs from the list stops the walk) and each follower (field 241) that does not wait (ONF_AI_WAIT_HERE), recursively, so followers of followers travel too. The order of the nodes was not re-settled here; C15's reading (nodes after their followers and items, so a mismatch can leave out the object itself, possibly the PC) stands. Every entry gets the same destination tile. The destination map is opened only when a PC is in the list; an NPC teleported alone to another map only has its record moved. | F2 |
| FM2 | 0x4D3460, 0x45D800, npc wait 0x4AA7A0 | Before the walk, unless request flag 0x100 is set, every follower for which 0x45D800 answers yes is ordered to wait, so it stays behind. The two readers here disagreed (dead or fatigue-unconscious; the earlier reading C15 said unconscious). **Settled** by [RL23](../architecture/original-object-model.md#9-map-change-close-the-whole-map-build-the-next-from-files): 0x45D800 reads fatigue and is true for a critter that is not undead and has no fatigue left, so the followers left waiting are the unconscious, not the dead (applied by ARC-RES-086). | F2 (RL23) |
| FM3 | cross-map payload 0x4D39A0, writer 0x409980, handles→IDs 0x4064B0, IDs→handles 0x406520 | For another map, every listed object is prepared (FM6), then all are converted from handles to IDs before any is written, so links inside the group (leader, follower list, inventory parents) stay as IDs. Each is marked dynamic and teleported, placed on the destination tile with zero offsets and appended whole, its own ID included, to `Save\Current\maps\<destination>\mobile.mdy`. The live source copy is marked destroyed and extinct. Confirms C16 and NI4. | F2 |
| FM4 | ID getter 0x407EF0, positional ID 0x4E62D0, GUID 0x4E62A0, object read 0x409AA0, ID registry 0x4E4FD0 | IDs are never reassigned by a move. A positional ID (tile, temp id, map) is given only to static walls, portals, scenery and traps; NPCs, PCs and items get a GUID. Reading the record on the destination re-registers the same ID, so the follower keeps one GUID on every map. That map-data NPCs already carry a GUID is an **(inference)** from the getter and the stored IDs, not checked in the data files. | F2 |
| FM5 | flush 0x410070, difference save 0x410830, dynamic save 0x410B20, map open 0x40F4E0, mobile load 0x410D10, free 0x405BF0, dynamic load 0x411450 | When the source map closes, the ID of an original (non-dynamic) object that is destroyed or extinct is appended to `mobile.des`; a destroyed dynamic object is simply not written to `mobile.mdy`. A map opens in this order: its data objects, `mobile.md` differences, every `mobile.des` ID freed, `map.patch`, then `mobile.mdy`. So a follower returning home comes back from `mobile.mdy` as a dynamic object and its original data record stays deleted. There is no duplicate check: registering an ID already present replaces it, and `mobile.des` is the only guard. | F2 |
| FM6 | preparation 0x4D3D60 (both cases), 0x4D3E20 (cross-map extras) | Both cases: the animation is interrupted; an NPC's teleport map is set if it was 0 (FM10); combat focus (283), who hit me last (284), the enemy list (304), the substitute inventory (294) and fleeing-from (230) are cleared; lights are updated. Cross-map only: combat mode is left (0x4B3F20), the object's AI events are removed (0x4AD7D0), it leaves its sector (0x43CF70), floating texts are removed and a UI callback runs. | F2 |
| FM7 | rebinding 0x406520 → 0x40BDB0, lookup 0x4E50E0, handle→ID 0x40BBF0, post-load 0x410C50 | A reference is kept as an ID while saved and turned back into a handle among the objects in memory when a map opens; a GUID not found becomes null, a positional ID is searched only on the current map at its tile. So a link from an object of map A to a follower now on map B is null after A reopens, and is lost at A's next save. The PC's follower array and the followers' leader rebind because the group arrives together (OL31). Each teleported object gets a "Teleported" event (type 32), whose callback runs the whole arrival notification in mode 2 (wading, jump points, tile scripts, sector entry; [RL6](../architecture/original-object-model.md#7-sectors-what-a-sector-owns-who-is-listed-how-objects-move) and RL27, corrected by ARC-RES-086: this row first read it as a drawing refresh only), then the flag is cleared. | F2 |
| FM8 | teleport notify 0x45C720 → 0x45C7B0 (filter 0x45C500, list test 0x4D3420), queue map close 0x45BCE0 → 0x45C2F0, map open 0x45C580 / 0x45C610 | Only when a PC changes maps: all animation and magic/tech events are dropped; every event whose object parameters all travel is written to the destination's `TimeEvent.dat` and loaded there when it opens; at the source's close the AI, combat, turn-based, world-map and teleported kinds are dropped, and an event naming any object left behind (a waiting follower's wait timer, for example) is parked in the source's `TimeEvent.dat`. Events with no object stay in the global queue. Confirms P3–P5. The AI tick, open here, is settled by [RL23 and RL28](../architecture/original-object-model.md#9-map-change-close-the-whole-map-build-the-next-from-files): the carry runs before the payload removes the object's AI events, so a travelling follower's own tick is carried, reloaded at the open and replaced by the fold's first tick when its sector loads; there is one tick, not two. An event whose object cannot be found at reopen gets a null handle and later fires with no object (RL28, TE9). (Applied by ARC-RES-086.) | F2 |
| FM9 | 0x45C720, spell end 0x44F7A0, animations 0x422B10 | An active spell is carried to the destination's `MT.dat` only when its caster, parent, target and both target lists all travel; otherwise it is ended. Animations of travelling objects go to the destination's `Anim.dat`. One reader; the other read only that a 40-slot table (0x5FF620, through 0x459F50 → 0x4CC4C0) is reset, at low confidence. | F1 |
| FM10 | teleport map setter 0x45F6D0 (field 243), waypoints 0x4ACBB0, standpoint 0x4ACDB0, standpoint 0x4AD060, origin test 0x45D320 | The one tie between an NPC and a map is field 243, the teleport map: written with the current map only while it is 0, by every NPC teleport (FM6) and by the AI's home check; a script setting the night standpoint and one magic/tech effect may overwrite it. Standpoints (289/290) are bare locations; the origin (291) is a group number compared between NPCs, with no map. In the waypoint and standpoint passes, when the teleport map is not the current one, an unforced pass does nothing; a forced pass of a leaderless NPC requests a teleport of that NPC alone to its waypoint or standpoint on its teleport map. | F2 |
| FM11 | sector lock 0x4D0540, save hook 0x4D2750, object list save 0x4F1A20, free 0x4D1400, detach 0x443770, load 0x4D1A30, fold 0x4F1CD0 (index 0x4E3B30), first tick 0x441FC0 → 0x4AD790, time test 0x45A9B0, AI event 0x4AD200 | On one map, an evicted sector saves and frees only its static objects. Mobile objects (NPCs, followers, items) are only detached from drawing and lights; they stay in memory with all their state and are re-attached from a per-sector index when the sector loads again. Nothing protects the PC's or followers' sectors except lock counts. Each re-attached living NPC gets an immediate AI event whose "returning" flag is set when the sector was away at least 3,000 game milliseconds (P7: now + 3 s, ARC-RES-042 H1) or is not in the eviction history (SS3); the AI acts on it only for a leaderless NPC out of combat (forced waypoints, else forced standpoint, FM10). Whether AI or time events run for an NPC while its sector is evicted was not read. | F2 |
| FM12 | same-map branch of 0x4D39A0, 0x4EDF20 → 0x43E770, clock 0x45C200; world-map landing (ledger WT12) | On the same map, every non-inventory object of the move list is prepared (FM6, both-cases part only) and moved to exactly the destination tile; combat mode, AI events, sector membership and time events are untouched. Request flag 8 advances the clock first. World-map travel lands through such a same-map teleport, so the group lands on one tile with the PC (one reader; consistent with WT12's landing teleport when the PC stays on its map). | F2 (world map F1) |
| FM13 | follower removal 0x45DFC0 → 0x4AA8C0 → 0x45E040, wait expiry 0x4AA990, order handler 0x4C31A0 | Leaving, a wait expiry (jilted, reaction lowered, leader cleared) and every order act on the current map only; none of them teleports. No caller of the teleport request is in follower code: the callers are map, script, magic/tech, the AI home return (FM10) and the interface. | F2 |
| FM14 | player save (0x40D760, NI5) | A save holds no follower state outside the per-map files: the PC and the followers with it are dynamic objects of the current map; waiting followers are objects of the map they wait on, with the leader as an ID (OL32). | F2 (from NI5, OL32) |

### What travels, in one view

| State | In the original | At a cross-map move |
| --- | --- | --- |
| Identity | GUID on the object | kept (FM4) |
| Stats, hit points, fatigue, effects, level, XP | object fields | travel |
| Inventory and worn items | items' own records, parent links | travel, each item a dynamic record of the destination (FM1, FM3) |
| Scripts with local flags and counters, dialog-related fields, reaction slots | object fields | travel (OL33) |
| Leader, follower list | handle fields | travel as IDs, rebind on arrival (FM7) |
| NPC flags, AI row, standpoints, waypoints, origin, teleport map | object fields | travel; teleport map set if 0 (FM10) |
| Combat focus, last hitter, enemy list, fleeing, substitute inventory | object fields | cleared (FM6) |
| Combat mode, the object's AI events, animation | global lists | left or dropped (FM6, FM8) |
| Its time events | global queue | moved when all named objects travel, else parked on the source (FM8) |
| Active spells on it | magic/tech runs | moved when all participants travel, else ended (FM9, F1) |

## Fix plan

V1-V6 are built: the actor directory and record resolver (MIG1), carried companions in the AI (MIG2), targets and panels (MIG3), the
body after a load (MIG4), one dialog host (MIG5), transfer parity (MIG10) and former followers going home (MIG11 S5). The contracts are
[`record-resolver.md`](../world/record-resolver.md), [`change-of-place.md`](../architecture/change-of-place.md) and
[`object-directory.md`](../architecture/object-directory.md). Open items of the research are in the
[social ledger](ledger/social.md#followers-across-maps-arc-res-076).
