# Gap review: the narrow gaps, the owner's gaps and the intended differences

Status: **Research, waiting for the owner's answers**, work item ARC-RES-088 (2026-11-04), a review of
the [gap register](gap-register.md) (G1, ARC-RES-086) after the architecture audit. Docs-only: no code
changes and no checks run.

Scope: the 25 register rows that are not systemic: the 12 narrow gaps (GR12–GR15, GR19, GR23,
GR27–GR31, GR35), the 5 gaps for the owner's decision (GR6, GR22, GR34, GR36, GR37) and the 8
intended differences (GI1–GI8). The 20 systemic gaps are settled by the
[target model](target-model.md) and its decisions GQ1–GQ8 and are not reopened here.

Requirements (reference only, no coverage row changes): FR-WORLD
([005](../requirements/world.md#fr-world-005), [010](../requirements/world.md#fr-world-010),
[016](../requirements/world.md#fr-world-016)), FR-RUNTIME
([001](../requirements/world-travel-runtime.md#fr-runtime-001)), FR-TRAVEL
([003](../requirements/world-travel-runtime.md#fr-travel-003),
[004](../requirements/world-travel-runtime.md#fr-travel-004)), FR-LIFECYCLE
([002](../requirements/ui-media-lifecycle.md#fr-lifecycle-002)–[004](../requirements/ui-media-lifecycle.md#fr-lifecycle-004)),
FR-UI ([008](../requirements/ui-media-lifecycle.md#fr-ui-008)), FR-SCRIPT
([001](../requirements/story-social-ai.md#fr-script-001)–[003](../requirements/story-social-ai.md#fr-script-003)),
FR-AI ([001](../requirements/story-social-ai.md#fr-ai-001),
[004](../requirements/story-social-ai.md#fr-ai-004)), FR-SCENE
([014](../requirements/scene.md#fr-scene-014)) and FR-NET
([002](../requirements/network.md#fr-net-002)).

## Method

- **Original side:** the rows of R1–R6 as the register cites them ([object model and
  residency](original-object-model.md) OM, RL; [persistence](original-persistence.md) PS;
  [frame and time](original-time-loop.md) TL; [goals](original-goals.md) GS;
  [scripts and modes](original-scripts-authority.md) SA; [follower move](../research/follower-map-transfer.md)
  FM) and the ledgers they point to. Nothing new was read in the executable; where a recommendation
  rests on a row read at F1 or on an inference, it says so.
- **Steamwoven side:** `origin/main` cee56c3 (after #596), read in the code and the component
  contracts; each "why" names the work item that built or left the part.
- **Rule for the recommendations:** the owner's rule on bugs of the original (2026-10-28,
  [rule modes](../world/rule-modes.md#what-is-a-mode-difference)): a technical bug (a lost state, a
  leak, a crash, a value read and overwritten) is fixed in both modes with no switch; a gameplay
  bug or an owner-recorded improvement keeps the original behind the Original setting and changes
  Steamwoven; an original rule that is neither follows the original in both modes.
- **Already decided:** where a row has a migration slice (MIG) or an owner decision (GQ), the
  recommendation keeps it unless the audit contradicts it, and the options say what changing it
  would mean.

## Answer

- **The register holds for 21 of 25 rows.** Four rows describe Steamwoven wrongly or incompletely
  and are corrected in place in this PR: GR12 (companions already run tile scripts, ARC-SCRIPT-046;
  wading comes from the map data for every critter, the hero included), GR13 (the heartbeat points
  10 and 19 run since ARC-WORLD-114; the far branch is in the rules, only "sector not loaded" is
  never answered), GR30 (a busted container already spills like the original; the gap is the
  generic destroy) and GR31 (the merchant's substitute container is modelled as a viewer lookup).
- **Two intended differences need an owner word they do not have.** GI6 (the saved random
  generator) is visible to a player, since a reload no longer rerolls, and has neither an owner
  decision nor an Original switch, unlike GI4, its sibling. GI4 (saved AI ticks) meets the fold's
  first tick of GQ2 A at a load, which no document reconciles yet; MIG9 has to.
- **One new intended difference is proposed:** GR19's turn-based round resumed as saved (the
  original's restart reads the saved round counter and overwrites it, a technical slip).
- **No GQ decision is contradicted by the audit.** Two get a condition: GQ8 B builds an Original
  branch on an inferred consequence (PS34), which D9 must confirm before it is built; GQ7 A keeps a
  link the original forgets, with one small visible case (below).
- **The answer list** is at the [end](#answer-list): one line per row, the recommendation first.

## 1. Narrow gaps

### GR12 Location notification (tile scripts, wading, sector entry)

- **Steamwoven, why:** the hero's tile and sector scripts run since ARC-SCRIPT-017 (ledger TS2),
  a main-map window's template sectors since ARC-WORLD-081 (PG9); a following companion of the
  shown map runs the tile scripts of the tiles it steps into since ARC-SCRIPT-046 (ledger TR3,
  `TerrainViewerCompanions.RunCompanionTileScripts`). NPCs moved by the AI host's movers, visitors
  from another map, jumps, teleport arrivals and created critters run none: that is slice 3 of the
  [script trigger audit](../research/script-trigger-audit.md#6-fix-slices-prioritised), not started;
  point 27 (new sector) for followers is its slice 9 (Virgil's 1568), not started. Wading
  (OF_WADING) is drawn from the flag as the map data holds it (`AreaScenePlan`, ARC-RENDER-018); no
  relocation recomputes it, for the hero either.
- **Original:** every relocation in modes 1 and 2 runs the wading test (which loads an absent
  sector), a PC or NPC runs the tile's scripts, the local PC's sector change runs the sector script
  and point 27 for each follower (RL5, RL6, RL18).
- **Record:** corrected (companions, wading).
- **Recommendation:** an original rule, not a bug: follow it in both modes. Build the notification
  once as TM5's relocation hook in MIG9, so every mover (AI host, follower, teleport, create)
  passes it; slices 3 and 9 of the script audit then only widen the admitted commands.
- **Options:** **A (recommended)** in MIG9 as TM5's hook, both modes. **B** script audit slice 3
  now on today's movers, moved into the world runtime by MIG15; worth it only if a story tile an
  NPC must trigger blocks the alpha. **C** leave NPC tile scripts out (a known difference).

### GR13 The heartbeat's world side (routine, generators, far branch)

- **Steamwoven, why:** `AiSchedulerRules` has the original's tick, the 30-tile near test, the far
  branch with its catch-up and the encounter end (ARC-RES-039 slices); since ARC-WORLD-114 the AI
  tick runs an NPC's first heartbeat (10) and heartbeat (19) scripts (`AreaUseScriptHost.RunHeartbeat`)
  in a narrow set widened by ARC-SCRIPT-047 and ARC-SCRIPT-049 (81 of 141 installed heartbeats and
  23 of 23 first heartbeats admitted). Not built: the waypoint, standpoint, day and night pass and
  the generator's spawner (0x4BA790), because the routine (FR-AI-001) and generators (FR-AI-004)
  were never started ([AI scheduler](../world/ai-scheduler.md)); the host answers every NPC's sector
  as loaded (`AreaAiHost.SectorLoaded`), so the far branch never ends a heartbeat.
- **Original:** the near branch runs point 19 and the pass; the far branch stops animations and
  brings a follower to its leader; the heartbeat ends off a loaded sector; encounter creatures are
  destroyed (RL16, RL17).
- **Record:** corrected (points 10 and 19 run; the far branch exists).
- **Recommendation:** an original rule: both modes. The "loaded" answer needs TM5's sector model
  (MIG9); the routine is K3 and the spawner K4 on top of it.
- **Options:** **A (recommended)** MIG9 for the sector answer, then K3 (routine) and K4
  (generators), both modes. **B** the daily routine earlier, as alpha item N12 (NPC routine and doors)
  on today's host, answering "loaded" for the shown map; it would be rewritten by MIG9.

### GR14 Transfer parity

- **Steamwoven, why:** the session moves the hero's followers by `npc.cell` and `npc.map`; the move
  list is the party, not followers of followers; ARC-WORLD-084 removes the traveller's AI tick;
  FM6's clearing was not part of the follower slices. MIG10 (V5) is in work as ARC-DEV-073.
- **Original:** FM1, FM2 (unconscious followers wait, RL23), FM6, RL22, RL23, RL28 (the carried tick
  is replaced by the fold's first tick).
- **Record:** accurate.
- **Recommendation:** original rules, both modes; keep MIG10 as it runs. One note for it: the carried
  tick under GI4 (see GI4).
- **Options:** **A (recommended)** keep MIG10 (ARC-DEV-073) as planned. **B** narrow it to followers
  of followers and the waiting rule and leave FM6's clearing to MIG7 with the links (TM10).

### GR15 Former followers never go home

- **Steamwoven, why:** there is no teleport map field (field 243); the follower plan's V6 waited for
  V1 and V5, and G1 moved it to MIG11 because its trigger is the fold (TM5).
- **Original:** the teleport map is written at an NPC's first teleport while it is 0; a forced
  waypoint or standpoint pass of a leaderless NPC away from its teleport map teleports it home
  (FM10, FM11, RL17). At a map open the fold forces that pass for every sector not in the eviction
  history.
- **Record:** accurate.
- **Recommendation:** an original rule: both modes. MIG11 as planned.
- **Options:** **A (recommended)** MIG11 as planned (after MIG7, MIG9, MIG10). **B** an earlier
  narrow slice after MIG10: set the teleport map at the transfer and send a dismissed companion home
  at the next open of its current map (the original's map-open case, without the 3 s re-fold); MIG11
  then moves it into the request. Worth it if a stranded companion bothers you in play before MIG9.

### GR19 The turn-based round at a load

- **Steamwoven, why:** `CombatDomain` saves the turn-based round as every other domain is saved
  (GI1); the subturn count and the stalled check are not saved (S19).
- **Original:** the Combat blob holds the running flag and the round counter, the acting critter's
  action points and the current turn and sub-turn objects; a load rebuilds the order from the
  critters around the hero, sets the counter to 0 and its first step makes it 1 (the saved counter
  is read, then overwritten), restores the action points, and fails the whole load when the
  current turn object is not in the rebuilt order (PS13, F2 for the reset, F1 for the failures).
- **Record:** accurate.
- **Recommendation:** the restart is a consequence of not saving the order, and its visible results
  (round 1 again, a load that fails) are technical slips: resume the saved round in both modes, save
  the subturn count too, and record it as an intended difference GI9 (technical bug rule). The order
  rebuild is not a rule a player relies on.
- **Options:** **A (recommended)** exact resume in both modes, new GI9, the subturn saved. **B** a
  switch: Original rebuilds the order and shows round 1, Steamwoven resumes. **C** the original's
  rebuild in both modes.

### GR23 Sleep and random encounters

- **Steamwoven, why:** never started: FR-TRAVEL-003 is "not started", FR-TRAVEL-004 partial (the
  palette's +1 hour is one explicit jump, so every timer fires once, late, as the original's sleep
  steps do, [event queue](../world/game-event-queue-runtime.md)). The alpha list (N16 rest,
  approved 2026-10-27, wave 3) holds rest.
- **Original:** sleep adds 1 h per real-time step, travel ⌊tiles / 64⌋ h per step; both are chains of
  the real-time list (TL26–TL29); an interrupted sleep is not resumed (PS32, inference).
- **Record:** accurate.
- **Recommendation:** original rules, both modes. Build sleep (K7) and encounters (K4) on the real-time
  domain of MIG12; today's clock has no real-time list, so an earlier rest window is built twice.
- **Options:** **A (recommended)** K7 and K4 after MIG12. **B** N16 rest now on today's clock as a
  stepped wait (one jump per hour), rewritten by MIG12. **C** after the alpha.

### GR27 Scripts start no goals

- **Steamwoven, why:** the script hosts apply state changes and ask for talks but start no goal run
  (V7, V8); a goal run exists only inside the AI host (`AiGoalRun`). The heartbeat of the Crash
  Site's Molochean agent (1717) stops on set day standpoint (45) and later needs run to (74);
  ARC-SCRIPT-051 is reserved for it.
- **Original:** perform animation, walk to, run to, use item, steal, get up and the casts start
  goals; stop attacking and become prone stop them (GS30).
- **Record:** accurate.
- **Recommendation:** original rules, both modes; scripts start goals through the one goal table
  (TM7) in D8 after MIG13. ARC-SCRIPT-051 can start on today's AI goal runs only if the agent is
  wanted before MIG13.
- **Options:** **A (recommended)** D8 after MIG13. **B** ARC-SCRIPT-051 now for walk to and run to on
  `AiGoalRun`, moved by MIG13.

### GR28 Writeback of a script run

- **Steamwoven, why:** `ScrDispatcher` returns `ScrWriteback` records that the host applies after
  the run (`ScrDispatchTypes.cs`), a design of the first dispatcher slices; the dialog action's
  mid-run commit (SA8) was recorded by G1 (E11).
- **Original:** a run works on a copy and writes all twelve bytes once if anything changed; no nesting
  guard, so a nested run on the same slot survives only when the outer run changed nothing; the
  dialog action commits flags and counters mid-run (SA8–SA11).
- **Record:** accurate.
- **Recommendation:** the lost inner write is a lost state, a technical bug: in both modes the outer
  run writes back only what it changed, so the inner run's changes survive. The mid-run commit of the dialog action is a rule a talk can depend on: follow it in both
  modes. D8 first counts whether any installed script nests on its own slot; if none, today's
  semantics are equal and only the commit is built.
- **Options:** **A (recommended)** D8: count, keep both writes in both modes, follow the mid-run
  commit. **B** reproduce the original's overwrite in both modes. **C** a switch (Original overwrites).

### GR29 Global variables, PC variables and PC flags

- **Steamwoven, why:** the admission's value kinds are counters, local variables, numbers and global
  flags (`ScrAdmission`, base set); global variables (kind 1) and PC variables and flags (kinds 5, 6)
  have no session store, so a script using them is refused. Known stops: Dernholm castle's opening
  heartbeat 1015, one of the two installed heartbeats whose first refusal is a global variable operand
  ([script trigger audit](../research/script-trigger-audit.md)).
- **Original:** 2000 global variables, 3200 global flags, a story number; PC flags and variables are
  fields 272 and 273 of the PC (SA13, SA14).
- **Record:** accurate.
- **Recommendation:** original rules, both modes; MIG8 as planned (global variables in the session,
  PC fields on the hero's entry).
- **Options:** **A (recommended)** MIG8. **B** a narrow earlier slice: a saved global-variable store
  and the PC fields as a session domain (one schema step), moved into the directory by MIG8.

### GR30 Destroy: a holder's items and links to the destroyed

- **Steamwoven, why:** a container broken by an attack spills every item onto its cell through the
  drop path with junk piles and decay (ARC-COMBAT-008, ARC-WORLD-063), as the original's bust branch
  does (ledger OD13): that part is exact. The generic destroy (a script's destroy, 68, since
  ARC-SCRIPT-052) records `obj.destroyed` for an NPC of the shown map and has no item step; each store
  removes its own records; no shared destroy path (H2).
- **Original:** destroy (0x43CCA0) runs point 2 first (it may refuse); a destroyed container or critter
  generates its inventory if it never did, then drops only OF_INVULNERABLE items on its tile; the rest
  stays in the destroyed holder's list (what becomes of it at the save is an inference, OM18, F1);
  links from elsewhere stay valid handles to a flagged object until the map is cleared, and consumers
  test the flag (OM17, OM18, OM29).
- **Record:** corrected (the bust path is exact; the gap is the generic destroy).
- **Recommendation:** dropping invulnerable items is an original rule that keeps quest items alive:
  both modes. The other items vanish with their holder in both modes, written as gone rather than kept
  in a dead list (the original's leftover list is a technical leak). Links: identities stay and
  consumers test "destroyed", as the original's handles do. All of it is TM4's destroy (MIG6).
- **Options:** **A (recommended)** in TM4's destroy (MIG6), both modes. **B** add the invulnerable drop
  now to the script destroy (narrow, D3), moved by MIG6. **C** leave it.

### GR31 Summoner, substitute inventory, scenery and projectile links

- **Steamwoven, why:** a merchant's substitute container is found by the original's rule (no leader,
  within 20 tiles, 0x45F650) and kept as a lookup of the viewer's inventory host per shown map
  (`AreaInventoryHost`, ARC-WORLD-090), used by barter and restock; it is not a saved link and a move
  does not clear it. The summoner, the scenery occupant (70) and the projectile weapon (80) are not
  modelled because summons and the remaining projectile kinds were not built.
- **Original:** the summoned list of the spell run, field 294, fields 70 and 80 (OM25, OM28); FM6 clears
  294 on a move.
- **Record:** corrected (the substitute container exists as a lookup).
- **Recommendation:** original rules, both modes: links as `ObjectId` fields (TM10) in MIG7; the summon
  relation with K5.
- **Options:** **A (recommended)** TM10 in MIG7, summons in K5. **B** keep the substitute container a
  lookup (no field) and model only the summon relation.

### GR35 Interface mode stack

- **Steamwoven, why:** the cursor mode is derived from which panel is open (`AreaCursorMode`, the
  cursor art by mode since ARC-UI-056), each window has its own flag (`WorldModal`, `WorldUiFocus`);
  windows were built one by one in the original-UI lane.
- **Original:** one stack of 21 modes; windows replace the top, pickers are pushed, target modes over an
  inventory are pushed and pop twice; the game view acts in modes 0, 1, 2, 3, 16 and 18 (SA36–SA45).
- **Record:** accurate.
- **Recommendation:** an original rule, both modes; MIG16 as planned (independent).
- **Options:** **A (recommended)** MIG16 as planned. **B** after the alpha; window-to-window cases are
  fixed one by one meanwhile.

## 2. Gaps for the owner's decision

All five were answered on 2026-11-04 ("Везде по рекомендации"). The audit contradicts none of them.

| Row | Decision | Why Steamwoven is as it is | Check after the audit | Options |
| --- | --- | --- | --- | --- |
| GR6 No prototype fallback at read time | GQ1 A (hybrid directory; GR6 stays open) | Map rows are immutable copies and created objects store their record (D1, D2) | R6 found no script that changes a prototype in play, so the fallback is invisible; holds | **A (recommended)** keep GQ1 A. **B** GQ1 B, the full field table (MIG6 and MIG7 become one larger slice) |
| GR22 Pause with any modal window | GQ3 B (Original pauses with the Esc menu only; Steamwoven with every modal window) | The session clock stands while a dialog, the palette or the inventory is open (`WorldModal`, [event queue](../world/game-event-queue-runtime.md)) | TL23: the Esc menu is the one raiser of the pause counter; the comfort pause is an improvement, so a switch fits the rule; holds | **A (recommended)** keep GQ3 B. **B** GQ3 A, the original in both modes. **C** GQ3 C, every window pauses in both |
| GR34 No named command boundary | GQ5 A (a thin typed request boundary) | Viewer hosts call the session owner directly (SA31) | Not a difference a player sees; holds | **A (recommended)** keep GQ5 A. **B** GQ5 B, direct calls until the network lane |
| GR36 A link to an object on another map | GQ7 A (identity kept in both modes) | Domain references are identities and survive moves (ARC-RES-076) | The one visible case: an NPC that stays keeps a companion who left in its enemy list or as its last hitter and can resume the fight on return, where the original forgets it (the move itself clears the mover's own links, FM6, in both). Reactions, which drive most hostility, are separate state. Small; holds | **A (recommended)** keep GQ7 A. **B** GQ7 B, a switch (Original nulls) |
| GR37 The turn-based setting in a save | GQ8 B (Original loads in the saved mode; Steamwoven keeps the player's setting) | Settings live outside saves (FR-LIFECYCLE-004; `AreaGameOptions.TurnBased`) | The Original branch rests on an inferred consequence (PS34: the load writes the cached copy back "consequence inferred"); D9 confirms it in the oracle or the executable before building it | **A (recommended)** keep GQ8 B, with D9's confirmation first. **B** GQ8 C, never from the save (no Original branch) |

## 3. Intended differences

| Row | What and why | Original | Check after the audit | Recommendation | Options |
| --- | --- | --- | --- | --- | --- |
| GI1 One atomic JSON snapshot | By requirement (FR-LIFECYCLE-002, 003); write, read back, compare, replace; schema steps (ARC-UI-037) | A save folder of module blobs; a load refuses a `data.sav` version other than 25 (PS1–PS4) | Representation only; the content rules of TM8 follow the original; holds | Keep, both modes | **A (recommended)** keep. **B** keep and add a later task to import the original's saves (not planned anywhere) |
| GI2 One event queue with a scope per event | Owner decision OQ3 | Per-map files written only around a hero's teleport (PS26, RL28) | Same carry and parking results; R3 Q5 (an NPC group sent without the hero) stays with MIG11 and K5; holds | Keep, both modes | **A (recommended)** keep. **B** per-map event files as the original (no behaviour gained) |
| GI3 Ties fire first scheduled first | Owner decision OQ1 | Newest first outside a ping, merge order inside: a list-insert artefact (TE3) | Technical; holds | Keep, both modes, no switch | **A (recommended)** keep. **B** an Original switch for the newest-first order |
| GI4 AI ticks are saved | Owner decision OQ2 (seeded replays); `RebuildOnLoad` gives the Original rule | Not saved; at each sector load the fold gives every living NPC an immediate tick with the "returning" flag (PS31, FM11) | **Needs reconciling:** under GQ2 A the fold gives first ticks and the 3 s re-placement at a load too; with saved ticks Steamwoven must say whether a load folds (and re-places NPCs) or restores ticks only. The same question meets MIG10's carried tick (GR14) | Keep the switch; MIG9 (K1) writes the rule. Proposal: Original rebuilds the ticks by the fold; Steamwoven restores the saved ticks and the fold gives no new ones; the fold's waypoint re-placement follows the original's history test in both modes, since it is a world rule, not a timing | **A (recommended)** as stated, written in K1. **B** drop the saved ticks and rebuild by the fold in both modes (simpler, loses exact timing after a load) |
| GI5 Events at their own deadlines | By requirement FR-RUNTIME-001 (ARC-WORLD-039): the pump steps to each deadline, at most 64 steps a frame; a wait stays one jump, so timers fire once, late, as the original's | One ping per frame, a late event fires once and repeaters re-arm from "now" (TL20–TL22) | Only frame-level lateness differs; jumps behave as the original; holds | Keep, both modes (technical) | **A (recommended)** keep. **B** an Original switch for per-ping lateness |
| GI6 The random generator is saved | Design of session randomness (ARC-WORLD-019), no owner decision, no switch | Not saved, seeded once from the clock; only the skill module's one pre-rolled value is saved, so the next skill roll repeats after a reload and everything else rerolls (PS14, PS32) | **Visible to a player:** after a reload the same actions give the same results in Steamwoven; in the original a reload rerolls hits, damage and every other draw, though not the next skill roll. An improvement without the owner's word and without the switch the rule asks for | Record it as an owner decision and add the Original switch: reseed at a load and keep only a saved next-skill roll | **A (recommended)** Steamwoven saves the generator, Original reseeds at load and keeps the next skill roll. **B** saved in both modes, recorded as your decision. **C** reseed in both modes (as the original) |
| GI7 A failed transfer leaves the session on its map | Owner rule on technical bugs; FR-WORLD-016 | The payload may stop half way; effects already applied stay, the fade-in never runs (RL30) | Technical; holds | Keep, both modes | **A (recommended)** keep. **B** none sensible: the original's half move is a broken state |
| GI8 Prototypes from the player's executable | Owner decision 2026-11-03 (ARC-RES-085; the ARC-DEV-066 line: ARC-DEV-067 done, ARC-DEV-068 in work, ARC-DEV-069 next) | The game writes the 1,423 missing prototype files on its first run (OM12) | Same result without running the original; holds | Keep | **A (recommended)** keep the DEV-066 line. **B** ask the player to run the original once (the fallback only) |

## 4. Corrections made in the register

| Row | Correction |
| --- | --- |
| GR12 | Companions run tile scripts (ARC-SCRIPT-046); wading is read from the map data for every critter; the open part is the AI host's movers, visitors, jumps, teleports and created critters, and point 27 |
| GR13 | Points 10 and 19 run from the AI tick (ARC-WORLD-114, ARC-SCRIPT-047, ARC-SCRIPT-049); the far branch is in the rules and never ends a heartbeat because the host answers every sector as loaded |
| GR30 | A busted container spills as the original (ARC-COMBAT-008, OD13); the gap is the generic destroy |
| GR31 | A merchant's substitute container is a viewer lookup (ARC-WORLD-090), not a link |

## Answer list

Answer by row id (for example "GR19 A, GI6 B"); "all by recommendation" takes every A. Rows whose A is
an existing MIG step or GQ decision change nothing when answered A.

| Row | Question | A (recommended) | Other options |
| --- | --- | --- | --- |
| GR12 | NPC tile scripts and location effects | In MIG9, both modes | B script slice 3 now; C leave out |
| GR13 | Routine, generators, the heartbeat's end | MIG9, then K3 and K4 | B routine now (N12) |
| GR14 | Transfer parity | MIG10 as running | B narrower MIG10 |
| GR15 | Former followers go home | MIG11 | B earlier slice at map open |
| GR19 | Turn-based round at a load | Resume in both modes, new GI9 | B switch; C original in both |
| GR23 | Sleep and encounters | K7, K4 after MIG12 | B rest now (N16); C after alpha |
| GR27 | Scripts start goals | D8 after MIG13 | B ARC-SCRIPT-051 now |
| GR28 | Nested writeback | Keep both writes, follow the mid-run commit | B original overwrite; C switch |
| GR29 | Global and PC variables | MIG8 | B narrow slice earlier |
| GR30 | Destroy of a holder | TM4 in MIG6, invulnerable items dropped | B narrow now; C leave |
| GR31 | Summoner and other links | TM10 in MIG7, K5 | B substitute stays a lookup |
| GR35 | Interface mode stack | MIG16 | B after alpha |
| GR6 | Prototype fallback | Keep GQ1 A | B GQ1 B |
| GR22 | Pause with windows | Keep GQ3 B | B GQ3 A; C GQ3 C |
| GR34 | Request boundary | Keep GQ5 A | B GQ5 B |
| GR36 | Link to an object on another map | Keep GQ7 A | B switch |
| GR37 | Turn-based setting in a save | Keep GQ8 B, D9 confirms first | B GQ8 C |
| GI1 | One JSON snapshot | Keep | B add original save import later |
| GI2 | Scoped event queue | Keep | B per-map files |
| GI3 | FIFO ties | Keep, no switch | B Original switch |
| GI4 | Saved AI ticks and the fold | Keep switch; K1 writes the load rule | B rebuild in both |
| GI5 | Deadline stepping | Keep | B Original switch |
| GI6 | Saved random generator | Original reseeds at load, Steamwoven saves | B saved in both; C reseed in both |
| GI7 | Safe failed transfer | Keep | — |
| GI8 | Prototypes from the executable | Keep the DEV-066 line | B run the original once |

## Owner answers

Not answered yet. The answers are recorded here before this PR merges.
