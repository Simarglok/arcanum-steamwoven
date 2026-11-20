# Script trigger conditions: when and where the original fires each script

## Status and scope

Status: **Research**, work item ARC-RES-2815 (epic [#2814](https://github.com/Simarglok/arcanum-steamwoven/issues/2814),
2026-11-19). The owner asked for a revision of script triggering against the executable, because script bugs such as
[#2788](https://github.com/Simarglok/arcanum-steamwoven/issues/2788) (a carried companion's tile scripts refused),
[#2789](https://github.com/Simarglok/arcanum-steamwoven/issues/2789) (a note's use script refused for a focus kind),
[#2790](https://github.com/Simarglok/arcanum-steamwoven/issues/2790) (a dialog reply blocked on a missing node) and
[#2806](https://github.com/Simarglok/arcanum-steamwoven/issues/2806) (a deferred call of Virgil's script) are hard to catch
when a tester does not know where and when a script must fire.

This note states, for every trigger kind of the retail 1.0.7.4 `Arcanum.exe`, **when** the original fires it, **for whom**
(triggerer and attachee), **which gates** stop it, and **what its return changes**; then what Steamwoven runs today and the
issue that follows each gap. It extends the [script trigger audit](script-trigger-audit.md) (ARC-SCRIPT-047, which lists
the 72 call sites of the object script entry per point and the admission sets) and corrects it where the reading differs
(section 4). The owner's manual checks built from it are the section
[ARC-RES-2815](../project/owner-recheck-list.md#arc-res-2815--where-and-when-scripts-fire-2026-11-19) of the re-check
list (Ctrl+Q overlay). The automatic guard is [#2816](https://github.com/Simarglok/arcanum-steamwoven/issues/2816) (ARC-DEV-2816, done: the census baseline of refused scripts per trigger kind and the smoke stage `script-triggers`, [toolchain](../workflow/toolchain.md)).

Evidence: the decompiled exports in `research-private` (index, per-function C, asm where the decompiler merged 64-bit
arguments), read in five slices of at most about 20 functions each (core entry and deferred calls; tile, sector and
movement; AI tick and combat mode; use and item points; combat resolution, death and dialog), each by one reader. Rows
are **F1** (one reading of the whole function) unless they say *partial*. Behaviour is in prose; no decompiled text,
listing, bytes or oracle output is in git. Steamwoven's side was read on `origin/main` cef34606f. Original parity beyond
the rows here is **NOT_VERIFIED**.

## Answer

For a tester, five rules cover most of what the original does:

1. **A script runs only where its trigger happens, for the object that holds it.** Tile scripts fire for the critter that
   steps onto the tile (the hero, any NPC, any follower), sector scripts for the hero only, heartbeats for NPCs near the
   hero, use scripts for the object used, item scripts for the item moved, dialog scripts for the NPC talked to.
2. **Distance and residency gate only the AI.** The real-time heartbeat needs the hero within 30 tiles; nothing else
   (tile, sector, follower new-sector, deferred calls) has a distance or "on the shown map" gate. A follower runs its
   tile and new-sector scripts wherever its body is.
3. **The return value matters only at some points.** "Skip default" (0) stops the default action at use, examine, get,
   drop, transfer, buy, destroy, dying, throw, taking damage, resurrect, will KOS, unlock, catching thief, the heartbeats
   and the talk; it is ignored at tile, sector, hit/miss/critical points, critter hits, enter/exit/start/end combat,
   insert/wield/remove item, new sector, leader killing, leader sleeping, bust and the dialog resume.
4. **A run never fails silently in the original**: there is no command it "cannot do". In Steamwoven a script whose
   command the host does not admit is *refused* whole; that is the bug class of #2788 and #2789, and section 5 says where
   a refused run shows.
5. **Things that repeat**: a tile re-runs every time a critter enters it again (not when it only turns on the same tile);
   the first heartbeat repeats on every load of the NPC's sector; examine runs on every hover refresh; the end and start
   combat points run on every AI pass in combat. A script stops repeating only by its own flags or by removing itself.

## 1. Common rules (TQ1, TQ3, TQ4)

**TQ1 — the object entry 0x441980** (triggerer, attachee, extra object, point, line). It runs nothing and answers "run
default" when the editor word 0x5E2F58 is set or the attachee's slot for the point is empty. At point 1 only, a PC or NPC
triggerer that does not yet know a trap script on the attachee gets a spot roll first (0x4BBFE0, 0x4BBE40); a success
marks the trap spotted and answers "skip default" **without running the script**. There is no test of a null triggerer
or attachee, of the destroyed or off flags, of the map, of dialog or combat mode. After the run the slot's flags,
counters and number are written back when they changed; a trap whose number became 0 is destroyed.

**TQ3 — the interpreter 0x4449B0**, whole-script gates in order: a multiplayer client runs only the slide scripts 997 and
999; native trap scripts 30000–30007 are applied by 0x4BCB70 and answer "skip default"; a failed first load answers "skip
default"; an NPC attachee with NPC flag 0x80000 (a generator) answers "run default" at every point; at the dialog point a
dead NPC without script file flag 0x8, or a surrendered one without flag 0x10, answers "run default", and a
mind-controlled one (spell flag 0x8000000) runs script 2188 in place of its own; a failed second load answers "run
default". Action 1 ends with "skip default", action 2 with "run default", running past the last line counts as "skip
default"; the auto-remove file flag 0x4 removes the script on all three. A goto out of the script, or 1,000 executed
lines, ends with "skip default".

**TQ4 — call script (7, 0x44B030) and call-ex (81, 0x44B170).** Call script runs the callee directly through the
interpreter (TQ1's gates do not apply) at the **caller's point**, for every triggerer × attachee pair of its two object
groups (at most 100 each), sharing one copy of the callee's header; the return is ignored and nothing is written back.
Call-ex goes through the full entry (TQ1 then TQ3) at the point and line its operands give; its return is ignored by the
caller.

## 2. Trigger kinds

"Return" says what a 0 ("skip default") changes; "ignored" means the script cannot stop the action. The last column is
Steamwoven on `origin/main` cef34606f and the issue that follows a gap.

| ID | Trigger (point) | When and where the original fires it | Triggerer → attachee | Gates | Return | Steamwoven |
| --- | --- | --- | --- | --- | --- | --- |
| TQ2 | Examine (0) | Hover description 0x57CCF0 (world hover, inventory, hotkey bar, refreshes), and at dialog start and in the follower portrait hover (return ignored there) | local PC → object | the triggerer is the local player | 0: no name or description | hover, hotkey bar, inventory, barter, dialog start (HK25, `DescribeDialogSpeaker`, interface on) and the portrait hover (HK26, `FollowerPortraitHovered`), both built by ARC-UI-1120 and ARC-UI-1281; ARC-SCRIPT-2829 found the audit row stale |
| TQ5 | Tile script | Location change 0x444270 in mode 1 (same sector) or 2 (sector change, placement, creation, teleport arrival); runner 0x4C0980. Every move 0x43E770: walk steps, AI standpoint and waypoint steps, follower catch-up, knockback slide, window jump, script moves, encounter spawn | the mover → none (point slot 1) | mover of type PC or NPC; **no** alive, off, follower, carried or window test; a same-location move is mode 0 and runs nothing; placement, creation and the teleport arrival re-run the current tile even when it did not change; the tile's sector must lock (no map open or a map being cleared runs nothing); editor word set runs nothing | ignored: the move has already happened; the tile script's own flags are its "once" | hero steps and arrivals, AI movers, pushed, carried (#2788) and created critters, arrival after a map change; `PlacedAdmission` |
| TQ6 | Sector script | Mode-2 location change of the **local PC** only (0x4F2330): a sector crossing, and also the PC's placement, creation and teleport arrival on the same sector. Before the script the sector's light and sound schemes are set | the PC → none | the sector must lock; editor word | ignored | border crossings and the hero's arrival (map change, same-map teleport landing; ARC-SCRIPT-2823); a placement outside those (new game, load) and the sector's light and sound schemes at arrival are not run here |
| TQ7 | New sector (27) | Inside the PC's sector entry (TQ6), after the sector script, for **every direct follower** of the PC (field 0xF1), also when the sector has no script | the PC → each follower | only the follower's slot and the generator flag; **no** map, distance, waiting or alive gate | ignored | runs for every direct follower of the hero at a sector crossing and at each arrival, same-map teleport, push and path-walk relocation of the hero (not at a new-game start, a quick load or a window move; the generator flag is not tested, no follower is one) (ARC-SCRIPT-2822, `AreaUseScriptHost.RunNewSector`; a follower carried from another map through its body row, so one outside the party's companion roster is skipped; a dialog a run asks for opens only when none is open; Virgil's 1568 runs) |
| TQ8 | Tile script of followers carried by a change of place; path walk | A teleport or map change takes the mover and, recursively, each direct follower **without the wait flag**, all to the same destination tile; each runs that tile's script (same map: through the move; other map: through the Teleported event in mode 2). The spell movement component (0x4524C0 → 0x455350) walks a path of at most 200 tiles and runs each step tile's script **before** the walker moves, then moves it to the last tile, which runs that tile once more | each critter → none | as TQ5; the path walk has no type gate | ignored | carried followers on a map change and the path walk ([AreaAiHostRoutes](../../src/Arcanum.World/AreaAiHostRoutes.cs) runs the last tile twice); the wait-flag rule and followers of followers not checked |
| TQ9 | Use (1) on a portal | Use goal node 0x428A10 for **any actor** (hero, NPC, companion), after the turn-based charge of 2 AP | actor → portal (extra: actor) | the trap spot of TQ1 first | 0 fails the node (no opening) | `AreaDoorHost` for hero, NPC and companion openers |
| TQ10 | Use (1) on scenery, ground items, walls, traps | Same node, any other object type except containers, critters and beds, **reached by the hero's click only**: goals 38 and 81 have one creator (the click handler 0x54ED30) and goal 12 ends unless its target is a portal, so no NPC or companion ever runs this branch (ledger TT19, ARC-WORLD-1688, re-checked by [#2826](https://github.com/Simarglok/arcanum-steamwoven/issues/2826)) | hero → object | as TQ9 | 0 fails the node (both outcomes end the run, GT125) | hero clicks only; **agrees**, an NPC user runs none in the original either (not a gap) |
| TQ11 | Unlock (3) | Portal or container open attempt (0x4AEB70, 0x4AED80), **before** the lock test, so also for unlocked doors; reached from path planning through a portal, the door animation, the loot window and script conditions | opener → portal or container | not when the object is busted; a portal needs a PC or NPC opener | 0 refuses the opening | run since ARC-SCRIPT-2860 ([ledger UL1–UL3](ledger/scripts.md#unlock-point-3-arc-script-2860); `RunUnlock`, the use set): the door goals' attempt (hero and NPC openers), the hero's use, the planners' step tests (`AreaPortals.Passages(session, walker)`) and the loot window's open (`AreaContainerLoot.Open`) run it before the lock test; one installed attachment (a door of the main map, script 1151), admitted whole; the script conditions that call the attempts (24, 44) are used by no installed script |
| TQ12 | Start (15) and end (16) combat | **Not** a global fight start or end: on every AI pass (0x4A8570) of a critter in combat mode, 16 (real time only) then 15; and 16 at every turn-based subturn end for the outgoing non-PC actor (0x4B7630) | the NPC's danger source → the NPC | not dead; not off; the hero not off; spell flag 0x10000 clear | ignored | built by ARC-SCRIPT-2820 (`AreaUseScriptHost.RunCombatPassPoint`, scene `CombatScript`): both callers run the script under the dying set, answer ignored, danger source as the triggerer on the AI pass, the actor itself at the subturn end; the 2 installed point-15 scripts are admitted (census) |
| TQ13 | First heartbeat (10), heartbeat (19) | AI tick 0x4AD200 (timed event kind 10). Point 10 on the tick the **sector fold** flags: every load of the NPC's sector after at least 3 s of game time away (or with no history); never for a created or resurrected NPC. Point 19 when the hero is within 30 tiles; period 250 ms near to 5 s at 30 tiles. On that wake-up tick, before the script, the NPC is marked to re-equip its best items and a leaderless NPC out of combat is put back on its waypoints or standpoint. Turn-based: 19 for the current NPC turn actor at its turn and when one of its runs ends (0x4B7010), without the 30-tile and dead gates | the NPC → itself | not dead (real time); no turn-based fight running (real time 19); multiplayer client none | 0 skips that tick's AI pass (10: also the heartbeat) | real time and turn-based run (`SchedulerRules`, `HeartbeatAdmission`); the AI host must be on; shown-map NPCs only |
| TQ14 | Will KOS (22) | Kill-on-sight test 0x4AE120 after target validity, asked by target finding (up to 50 critters by distance), dialog start and the reply step | candidate → deciding NPC | target validity | 0 vetoes kill-on-sight | run since ARC-AI-2821 (#2821): `AreaAiHost.AllowsKillOnSight` asks `AreaUseScriptHost.RunWillKos` (candidate as triggerer, "skip default" vetoes) at the three askers through one hook (target finding, dialog start, reply step) under `WillKosAdmission` (heartbeat set plus is wielding item, get stat, call script); **census: 60 installed rows of 7 scripts, 60 admitted** (47 by the heartbeat set alone) |
| TQ15 | Enter (13), exit (14) combat | The danger setter 0x4AABE0: 13 when the type becomes combat and the NPC was not in it (not again on a focus change); 14 when an NPC in combat goes to none, flee or surrender | danger source → NPC | not on critter flag2 0x200000; the type is forced to none by several flags | ignored | the danger-setter hook (ARC-AI-1530), NPCs of the shown map |
| TQ16 | Catching thief (11), caught thief (33) | Guard witnesses 0x4AEE50 (lock picking, damage to a guarded object, the lock spell): NPC witnesses within 10 tiles that perceive the actor. Failed steal or plant (0x4C7160): 33 then 11; plants are never caught at master training | 11: actor/thief → witness or victim; 33: victim → thief | witness alive, not mind-controlled | 11: exactly 1 lets the witness react (attack) | 11 runs for pick pocket and guard witnesses; 33 runs for an NPC thief's noticed failed pick pocket (AI host, [#2858](https://github.com/Simarglok/arcanum-steamwoven/issues/2858)) before 11, the answer ignored, under the dying set (10 installed slots on NPC thieves of the main map, 3 scripts 2931, 2840, 2839, all admitted whole); the extra object (the attempt's item) is not passed, as for 11; the hero thief carries no script |
| TQ17 | Deferred calls: call in (54), call at (55) | Kind-4 Script event in the saved game queue: call in after operand seconds of game time; call at at the operand's time of day. At firing, an object parameter that had no reference runs as null; a destroyed object or a map being cleared drops the event; the off flag is not tested. The callee runs at point 1 | stored triggerer → stored attachee | the parking rule keeps events of a closed map | ignored | call in through the session queue under the call set; call at through the same queue since ARC-SCRIPT-2824 ([#2824](https://github.com/Simarglok/arcanum-steamwoven/issues/2824)); callees with a dialog action [#2806](https://github.com/Simarglok/arcanum-steamwoven/issues/2806) |
| TQ18 | Use (1) of items, containers, bodies, beds, traps | Item use 0x462CC0 (inventory, hotkey, target cursor, NPC AI): 0 charges 4 AP and does nothing; 1 casts, reads a written item, eats, uses a skill. Containers and bodies: at the loot window, **local PC only**, after sight and lock (0x572370); an NPC opening a container fires nothing. Bed: the rest decision (0x57B180) is **reversed**: "run default" (or no script) refuses sleep ("You cannot sleep here."), only 0 lets the PC sleep. A walker stepping onto a trap object runs its use script (0x430CE3) and stops. The magick-tech component runs it with the **target** as triggerer. A critically failed disarm runs it (0x4BC9A7) | user → item or object | TQ1's trap spot | see "when" | hero and NPC item use (`ItemUseAdmission`; a refused written item still opens, #2789), loot window, bed (`AreaRest`), spell use component, traps |
| TQ19 | Get (4), drop (5), transfer (32), insert (21) | Get only for an item on the ground (a held item is a transfer); drop and transfer run **before** the remove and capacity checks; insert on every placement into any holder, also on the incoming item of a stack merge (destroyed right after) and with the **giver's gold or ammunition pile** as attachee on a gold or ammo move | taker / holder / giver → item (transfer: giver → recipient, extra item) | none beyond the move | 4, 5, 32: 0 refuses the move; 21 ignored | the hero's own moves; NPC pickup runs 4 and 21; the hero's give to an NPC or container runs 32 on the recipient (ARC-SCRIPT-2827); an NPC's and a critter's drop through a critical, an arm hit or a spell's weapon block runs 5 through the host's `DropScript` (ARC-SCRIPT-2853; skip default refuses and the item stays, a script the host cannot apply leaves it unapplied; the body decay's drop runs none, VP24); the transfer script is not run for a new pile, stack or restock item at all (ARC-SCRIPT-2857: a created object has no parent; ARC-SCRIPT-2887: 0x461810's fresh path runs the new item's get script, point 4, with the holder as triggerer before the insert check, and 0x466640 its insert-item script, 21, built for an NPC's or container's new pile, new stack and restock item; the merged and the hero-recipient cases are [#2909](https://github.com/Simarglok/arcanum-steamwoven/issues/2909), [#2910](https://github.com/Simarglok/arcanum-steamwoven/issues/2910)); gold and ammunition moves run 21 on the source pile with the destination as triggerer (ARC-SCRIPT-2855: hero and NPC takes, barter open, sale payment, steal, junk piles, hero-to-NPC ammunition, a declined split throw's merge); the NPC steal and the dialog's follower payment not ([#2882](https://github.com/Simarglok/arcanum-steamwoven/issues/2882)); limits: the runs are made before the move (the original's merge raises the target stack first, the barter open runs pile by pile), a refused move makes none of its script's effects |
| TQ20 | Wield on (24), wield off (25), remove (28) | 24 on insert into a worn slot (before 21), 25 on removal from one; both, 25 then 24, for every worn item when the magick or tech points change (a flag condition 0x32 can read); 28 on every removal, including destroy, inventory clear and drop-all | wearer / holder → item | none | ignored | the hero's moves; an NPC wearer's 24/25 run and are applied for the best-items wield, the re-wield on a points change, a sale onto a merchant's doll slot, the loot or steal of a worn item, a throw and (ARC-SCRIPT-2856) the critical drop of a worn item (25 then 28, the host's `NpcWearerScripts` context; the hero's critical drop, ARC-SCRIPT-2886, the same through `HeroWearerScripts`; the hero's give puts the item in the NPC's list unworn, so it runs 21 and the NPC wields later by its re-wield); 25 on the NPC removals by destroy, body decay, created-critter destroy and restock clear ([#2885](https://github.com/Simarglok/arcanum-steamwoven/issues/2885)) not; the remove script runs on destroy item named, the hero's used-up item and an item an NPC's script deletes from its list (ARC-SCRIPT-2854); the restock clear, the corpse's inventory walk and the gold pile or ammunition stack a move empties where point 21 already runs (ARC-SCRIPT-2879; the other emptying callers [#2900](https://github.com/Simarglok/arcanum-steamwoven/issues/2900), the last item of a dissolving junk pile [#2899](https://github.com/Simarglok/arcanum-steamwoven/issues/2899)), the invulnerable items a script destroy of an NPC or container takes out (ARC-SCRIPT-2880) and a created critter's destroy drops (ARC-SCRIPT-2903) run it, and the spell path's spent scroll or food ([#2881](https://github.com/Simarglok/arcanum-steamwoven/issues/2881)) not |
| TQ21 | Buy object (17) | Sale check 0x462170 on **every price computation** with the PC as seller, and again on the barter hover quote | item → merchant (extra: PC) | skipped when the seller's haggle training is 2 or more | anything but exactly 1: price 0, the merchant will not buy | not run; no installed attachment (census count 0, not modelled, [#2825](https://github.com/Simarglok/arcanum-steamwoven/issues/2825)) |
| TQ22 | Hit (7), miss (8), critical hit (34), critical miss (35), critter hits (26) | Melee 0x4B3770 and ranged 0x4B2F60 resolution: 7 then 34 on the weapon, then 26 on the **attacker** with the **target** as triggerer, before the damage; 8 then 35 on a miss (no 26). Ranged: 34 and 35 need a type-5 weapon. A projectile that lands on its tile with no target object runs 7/34 with no damage; one that flies past its range runs 8/35 | attacker → weapon (target as extra); 26: target → attacker | weapon present | ignored | `RunCombatPoint` and the shot-end scripts, hero and NPC attackers |
| TQ23 | Critical hit on armour (34; 35 unreachable) | Damage apply 0x4B4390, loop at 0x4B47F8 (GT201): for a damage whose context has the critical flag (0x4), on a PC or NPC whose hit points are above 0 (the loop comes after the dead check), each worn type-7 item of slots 1000–1008 in slot order, after point 23 and the invulnerable and destroyed gates and before the hit points are written. The point is 34 when the hit flag (0x2) is set, 35 when it is clear. Every caller of the damage apply that carries 0x4 has 0x2 set: a critical miss is turned into a hit on the attacker by 0x4B6410 (flags 0x2 and 0x8 set, 0x4 half of the time) before the hit step, so 35 never runs here | attacker → armour (extra: the one who takes the damage; the attacker on a critical self-hit) | critter alive, damage not prevented, taking-damage script did not skip | ignored | hero and NPC critical hits on a critter and critical self-hits (melee, ranged landing, throw at a critter or a location, strike on an object or wall) (`SessionArmourCriticalEvent`, `AreaUseScriptHost.RunArmourCritical`, ARC-SCRIPT-2828); the order against the critical effects and the resistance step differs (the original runs the loop after them, here before the damage is applied); census: 2 installed armour rows with 34 (scripts 30219, 30237), none with 35 |
| TQ24 | Taking damage (23), throw (6) | 23 in the damage apply for every damage with a target; not again for damage the script itself causes (re-entry guard). 6 when a critter throws an item; a non-PC thrower loses it first | source (may be null) → target; thrower → item | 23: target present | 23: 0 cancels the whole damage (no break, no death); 6: 0 puts the item back, nothing is thrown | 23: NPC victims (`RunTakingDamage`); 6: hero throws |
| TQ25 | Dying (12), leader killing (20), destroy (2), resurrect (18) | 12 in the kill step, every kill; 20 after the experience award for each follower of the killer's PC leader when an NPC dies; 2 when an object is destroyed (a wall also destroys what stands on its tile); 18 in the heal applier for a dead PC or NPC with the raise flag, **also** when flags block the resurrection | killer → victim; victim → each follower (extra: the PC leader); object → itself; healer → dead target | 2: not already destroyed | 12: 0 skips all death processing; 2: 0 keeps the object; 18: 0 refuses; 20 ignored | 12, 2, 18, 20 run (`RunDying`, `RunDestroyPoint`, `RunRaise`, `RunLeaderKilling`); 20 built by ARC-SCRIPT-2859 ([ledger LK1–LK3](ledger/scripts.md#leader-killing-point-20-arc-script-2859)): after the kill award (and, on the attack paths, the kill log; no kill alignment step yet) for each direct follower of the hero when the hero or a critter the hero leads kills an NPC (the one installed attachment, script 1014 of an NPC of the main map, is admitted whole by the dying set) |
| TQ26 | Bust (30), leader sleeping (29) | 30 when damage breaks an object with a broken art: scenery (first break; a second break destroys), items of types 5–14, traps, portals (busted flag); an object without broken art is destroyed (point 2) instead; walls, containers and critters never bust. 29 when a rest duration is chosen, for each direct follower | breaker → object; PC → follower | none | ignored | `RunBust`, `RunLeaderSleeping` |
| TQ27 | Dialog (9): talk and resume | Talk goal 21 (range below 5 tiles for the hero, 10 for an NPC; target not destroyed or off), then TQ3's dead/surrender/mind-control gates. "Run default" shows a **generic greeting** as a speech line, no window; a surrendered target whose danger source is in the talker's party flees instead. The dialog opens only if the talker is not dead or unconscious, can speak, no dialog is already up for the hero, and a talker in combat mode can leave it. The resume (0x568480) ends the window first and runs the NPC's dialog point at the held line + 1 (kind 1) or the reply's line (kind 2) | talker → NPC | as "when" | talk: only exactly 1 is "run default" | talk, the heartbeat's call-ex, the scripted starts and the resume (`AreaConversation`); final lines to a Player record are built ([#2804](https://github.com/Simarglok/arcanum-steamwoven/issues/2804), FLM2) |
| TQ28 | End-game slides | Scripts 997 + n from the slide UI (0x569720), point 1, all objects null, header flags and counters not initialised | none | none | ignored | `AreaSlideScripts` |
| TQ29 | Order of the arrival events (Teleported, kind 32) on another map (ARC-SCRIPT-2958) | Post-load 0x410C50 visits the open map's objects by pool index from the highest down and queues a Teleported event 1 ms ahead for each object with the teleported flag (the PC, each carried follower and every carried item). The queue insert puts a new event **before** the events of the same time, so they fire last-inserted first: in ascending pool index, all in the first ping the game clock passes that time, one after another, ahead of older events of that time; an ordinary add a callback makes fires later. The PC's event runs its tile, its sector entry and inside it point 27 for its followers (TQ6, TQ7); each follower's event runs its own arrival tile (TQ8). The pool index comes from the handle free stack (last freed slot first, a fresh slot only when empty), which holds the slots the old map's close just freed, so whether the hero's point 27 runs before or after a follower's tile script is **not fixed** by the code (the slots the close and the load hand out were not read) | each arrived object → none | as TQ5–TQ8; a ping stops a list after 501 events | ignored | the hero's arrival (tile, sector, point 27) runs first, the followers' arrival tiles in the AI step after it (checked on `origin/main` bdf9cf99d); the run-time order is [#2970](https://github.com/Simarglok/arcanum-steamwoven/issues/2970) |
| TQ30 | Point 27 for a follower left behind (ARC-SCRIPT-2958) | A follower **ordered to wait** is not in the PC's follower array: the wait order 0x4AA7A0 removes it (0x45DFC0 → 0x45E040 compacts the array), then sets its leader back (the PC here) and its WAIT bit (0x8), so point 27 never runs for it, on its map or another. A member of the array that **carries** the WAIT bit (0x8) while following (CF8–CF10) is skipped by the move list (TQ8) and stays on the closing map, but stays in the array: on the destination its ID does not resolve, so the element becomes null in place and stays counted, the hero's later sector entries call point 27 with a null attachee (not tested; the runner reads pool slot 0's script field, in practice nothing runs), and nothing read puts the follower back (the save side was not read), while the follower keeps the PC as its leader on its own map | the PC → none (null) | none | ignored | a waiting follower is outside the array (`SessionCompanion`), so nothing runs, as in the original; a following follower with the WAIT bit is carried instead of left behind (checked on bdf9cf99d; [#2969](https://github.com/Simarglok/arcanum-steamwoven/issues/2969)) |
| TQ31 | Critical-miss self-hit: hit (7), critical hit (34), critter hits (26) | 0x4B6410 turns a critical miss into a hit on the attacker (flag 0x2, target := attacker, flag 0x4 half of the time) before the hit step; where the attack is resolved at once (a swing 0x4B3770; a shot of a weapon with no projectile art, 0x4B2F60) 7, 34 (flag 0x4) and 26 run before the damage as for any hit, with the attacker as victim: 26 has the attacker as both attachee and triggerer; bare hands run 26 only. A shot with a projectile art and every throw have no self-hit: the projectile ends on the attacker's own tile and 0x4B2870 is never reached | attacker → weapon (extra: the attacker); 26: attacker → attacker | weapon present (7, 34); attacker present (26) | ignored | `SessionHitScriptEvent.SelfHit`, hero and NPC attacks on critters and objects that resolve at once (ARC-SCRIPT-2841); census: 26 placed weapon rows with 7, 2 with 34 (30047, 30064), 4 weapon prototypes with 7, none with 34. Steamwoven still self-hits on a flying critical miss: [#2979](https://github.com/Simarglok/arcanum-steamwoven/issues/2979) |

## 3. Combinations a tester meets

- **A companion recruited elsewhere** (Virgil carried from the Crash Site) runs tile scripts at every tile it steps on,
  in any town, and its new-sector script on every sector the hero enters (TQ5, TQ7). A refused companion tile run looks
  like "nothing happens where the companion should comment" (#2788).
- **The plane wreck at the Crash Site**: tile script 1695 on a row of tiles waits for Virgil (not the hero) and opens his
  remark on the flying machine (DLG 1696); its last replies set flag 2304, which ends the script from then on, so a remark
  left early comes back on his next step ([area map mode](../rendering/area-map-mode.md)). The inn of Shrouded Hills where
  Joachim stays holds Virgil's tile scripts of #2788.
- **Reading a note** runs its use script before the reading window; a refused script must not stop the reading in
  Steamwoven (the original has no refusal) (TQ18, #2789). A deferred call the script schedules fires seconds later, with
  the note's reader and the callee (Virgil) as stored (TQ17, #2806).
- **A heartbeat that opens a talk** (Virgil 1100 at the Crash Site, Wheel Clan 2700, the Void 1130 and 3081, Panarii
  temple 2602, Drog's cave 1490, Dernholm castle 1015, Vollinger's basement 2106, the Molochean agent 1717) needs the hero
  within 30 tiles, the AI running and no dialog open; a talk asked for while another dialog is up is lost, not retried.
- **Doors in turn-based combat** charge 2 AP before the door's use script runs (TQ9).
- **Sleeping in a bed** is refused unless the bed's script answers "skip default" (TQ18).

## 4. Facts that differ from earlier research

- TG2 and the [audit](script-trigger-audit.md#3-entries-that-are-not-an-object-slot) list 0x43EA20 as a mode-2 caller of
  the location change; it calls the move wrapper (mode 1 or 2 by sector). The direct mode-2 caller is the Teleported
  callback 0x43EAC0. The path walk is called from the movement component 0x4524C0, not 0x452630.
- U2 assumed a waiting follower keeps its place in the PC's follower array; the wait order removes it (TQ30). The move
  list's WAIT-bit (0x8) test therefore only meets members that carry the WAIT bit while following.
- Points 15 and 16 are per-critter calls on every AI pass in combat mode (16 also at each subturn end), not a fight's
  start and end ([audit](script-trigger-audit.md#2-attachment-points-who-fires-them) names).
- The first heartbeat (10) repeats on every load of the NPC's sector after at least 3 s away (the fold flag), not once per
  life; Steamwoven's wake-up step matches this. Created and resurrected NPCs never get it.
- Unlock (3) fires before the lock test, also for unlocked doors and during path planning.
- The guard's catching thief at 0x4AEFCD is the guard-witness rule (lock picking, damage, lock spell), not the pick-pocket
  use set; pick pocket runs 33 before 11, and 11 needs exactly 1.
- The bed's use script has reversed polarity (Steamwoven's `AreaRest` already follows it).
- Point 26's triggerer is the target; point 8 on the projectile path means "flew past its range".
- The interpreter's second load failure answers "run default" (only the first answers "skip default"); auto-remove also
  applies when the run falls past the last line; the multiplayer allow-list is 997 and 999 only.
- A dialog action inside a deferred or called script commits the running copy's flags and counters into the attachee's
  slot of the invocation point (point 1 for call in/at): *partial*, inferred from the dialog action's case.

## 5. Where a refused run shows in Steamwoven

There is no single record of refused runs; each host reports its own way (the guard
[#2816](https://github.com/Simarglok/arcanum-steamwoven/issues/2816) is to collect them in one place). In a Ctrl+B report
log, look for:

| Trigger | Log line of a refused run |
| --- | --- |
| Hero tile and sector scripts | `ARC-WORLD-031 enter` with outcome `ScriptRefused` |
| NPC and companion tile scripts | `ARC-DEV-206 npc-tile` (printed only when the run did something, or with the viewer's log-all switch); `ARC-SCRIPT-046 companion-tile ... Refused` |
| Heartbeats | `ARC-WORLD-114 heartbeat-refused npc= point= outcome= detail=` (once per NPC, point, outcome and script) |
| Item use | `ARC-SCRIPT-2789 item use script not modelled` (hero reading), `ARC-SCRIPT-1590 item-use-refused` (NPC) |
| Use click on the map | the status line `use refused: ...`; not logged |
| Deferred calls | `ARC-WORLD-039 events ... refusal=` when the refusal counter grew |
| Combat points, danger hook, dying, raise | `ARC-SCRIPT-2820 combat-script` / `combat-script-refused` (points 15, 16, once per NPC, point and script), `ARC-SCRIPT-1483 combat-point`, `ARC-AI-1530 danger-hook`, `ARC-DEV-312 dying`, `ARC-MAGIC-1167 raise-script` (only when the script ran) |
| Talk | the conversation does not open (`EntryNotAdmitted`); a companion's refused talk prints a `refused` notice |

A gate refusal (the object is not on the shown map, has no row, the hero is the victim) has no run and is silent at
several hosts.

## 6. Gaps filed

[#2820](https://github.com/Simarglok/arcanum-steamwoven/issues/2820) points 15 and 16 (built by ARC-SCRIPT-2820);
[#2821](https://github.com/Simarglok/arcanum-steamwoven/issues/2821) will KOS;
[#2822](https://github.com/Simarglok/arcanum-steamwoven/issues/2822) followers' new sector (done, ARC-SCRIPT-2822);
[#2823](https://github.com/Simarglok/arcanum-steamwoven/issues/2823) sector script on arrival, teleport and placement;
[#2824](https://github.com/Simarglok/arcanum-steamwoven/issues/2824) call at (built by ARC-SCRIPT-2824; the installed data has one line, script 1405 line 4);
[#2825](https://github.com/Simarglok/arcanum-steamwoven/issues/2825) points 3, 17, 20, 33: the recount found installed attachments at 3, 20 and 33 (the first reading "none" was wrong), so they are [#2860](https://github.com/Simarglok/arcanum-steamwoven/issues/2860) unlock (built by ARC-SCRIPT-2860), [#2859](https://github.com/Simarglok/arcanum-steamwoven/issues/2859) leader killing (built by ARC-SCRIPT-2859) and [#2858](https://github.com/Simarglok/arcanum-steamwoven/issues/2858) caught thief (runs since ARC-SCRIPT-2858); point 17 has none and stays not modelled (census count 0);
[#2826](https://github.com/Simarglok/arcanum-steamwoven/issues/2826) NPC use of scenery (not a gap: closed, the original has no NPC use of scenery, TQ10);
[#2827](https://github.com/Simarglok/arcanum-steamwoven/issues/2827) item points for NPC and holder moves (split: the hero's give runs the recipient's transfer script, ARC-SCRIPT-2827; the critical drop runs point 5, ARC-SCRIPT-2853; the rest [#2854](https://github.com/Simarglok/arcanum-steamwoven/issues/2854) remove (done on destroy item named, used-up and NPC list deletes, ARC-SCRIPT-2854; the spell path's spent scroll or food, ARC-SCRIPT-2881; restock, decay and emptied piles done by ARC-SCRIPT-2879, rest [#2899](https://github.com/Simarglok/arcanum-steamwoven/issues/2899), [#2900](https://github.com/Simarglok/arcanum-steamwoven/issues/2900), [#2880](https://github.com/Simarglok/arcanum-steamwoven/issues/2880)), [#2855](https://github.com/Simarglok/arcanum-steamwoven/issues/2855) gold/ammo insert (ARC-SCRIPT-2855), [#2856](https://github.com/Simarglok/arcanum-steamwoven/issues/2856) NPC wield (done, ARC-SCRIPT-2856: the critical drop runs 25 and 28; the hero's critical drop too, ARC-SCRIPT-2886; the rest [#2885](https://github.com/Simarglok/arcanum-steamwoven/issues/2885)), [#2857](https://github.com/Simarglok/arcanum-steamwoven/issues/2857) new piles and restock);
[#2828](https://github.com/Simarglok/arcanum-steamwoven/issues/2828) critical points on armour (done, ARC-SCRIPT-2828);
[#2829](https://github.com/Simarglok/arcanum-steamwoven/issues/2829) examine at dialog start and in the portrait hover (found already built, HK25 and HK26; the TQ2 row is corrected).
Already open before this audit: [#2804](https://github.com/Simarglok/arcanum-steamwoven/issues/2804) (built since, FLM2),
[#2806](https://github.com/Simarglok/arcanum-steamwoven/issues/2806); the admission gaps per command are in the
[refusal census](script-refusal-census.md).

## Unknowns

- **U1** Settled by TQ29 (ARC-SCRIPT-2958): the arrival events fire in ascending pool index, which the code does not
  fix; the run-time order stays open ([#2970](https://github.com/Simarglok/arcanum-steamwoven/issues/2970)).
- **U2** Settled by TQ30 (ARC-SCRIPT-2958): a waiting follower is not in the array; a following member with the WAIT
  bit left on the closed map becomes a null element ([#2969](https://github.com/Simarglok/arcanum-steamwoven/issues/2969)).
- **U3** The meaning of 0x6017C0 and 0x410270 during a map load, so whether a move while loading drops tile scripts.
- **U4** The callers that pass the entry's result up (0x452EEB, 0x467847, 0x467D49, 0x4B3163, 0x4B399C, 0x4B7681,
  0x4BC9A7, 0x5684AA) were not followed, so whether points 1, 24, 25, 16 and 35 skip a default there.
- **U5** Whether destroying an object or switching it off removes its pending AI tick, and whether dead critters stay in
  the turn-based actor list.
- **U6** The generator path 0x4BA790 of the heartbeat and critter flag 0x20000 (destroyed when the sector unloads).
- **U7** Distance gates of the use goal live in earlier goal nodes, not read here.
- **U8** The editor word 0x5E2F58 is read as the editor flag from earlier research; its init 0x43A330 was not checked.

## Reproduction

The readings come from the private exports (`research-private/ghidra-export`, index and per-function C) by address; the
slices and their function lists are the rows above. The Steamwoven side is the code at cef34606f (hosts named in each
row). Counts of installed attachments come from the [audit census](script-trigger-audit.md#5-census-of-the-installed-attachments)
of 2026-10-29; recount before relying on them.
