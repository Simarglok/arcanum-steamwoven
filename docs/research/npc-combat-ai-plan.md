# NPC combat AI: source map and decomposition

Status: **Research plan**, work item ARC-RES-039 (2026-10-16). This document
maps how the game organizes NPC behaviour in and around combat and
breaks the owner's goal into ordered work: **phase 1** rebuilds the original
system stage by stage with the source plus the recorded D2 fixes as the
default, **phase 2** adds
optional patches (mods) that change the play experience without replacing the
phase 1 behaviour. It records no final rule, changes no production API,
viewer, session schema or dialog port, and every stage, code and policy below
is a **proposal** until the owner accepts it; the owner decisions D1–D7 were
accepted on 2026-10-16 and D8 on 2026-10-17.

Requirements: [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002)
(threat detection and hand-off to combat) is the main target;
[FR-AI-001](../requirements/story-social-ai.md#fr-ai-001) (AI tick and
routine), [FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004)
(companion orders), [FR-COMBAT-002](../requirements/combat-magic-tech.md#fr-combat-002)
(turn order) and [FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003)
(NPC spell target) are touched where the combat AI drives them. The combat
rules themselves are in the [combat core research](combat-core-evidence.md)
(ARC-RES-030); its open question O5 ("how AI chooses attacks, flees or ends
its subturn early") is what this plan answers.

## Vocabulary and boundary

- **Source fact**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a row of the
  [original verification ledger](original-verification-ledger.md) is cited.
  Unnamed functions keep decompiler names (`sub_…`); a reading of their intent
  is marked as such.
- **First-pass finding**: what one reading of a function shows. Each is
  re-read and either confirmed or corrected by the stage research that owns
  it; nothing here is a contract.
- **Anomaly**: a branch that looks inconsistent. Under decision D2 phase 1
  fixes a confirmed slip and records it in the [anomaly
  decisions](#anomaly-decisions); a bug the retail game really has stays
  reachable behind a named switch of the "Original" setting (D8), while a
  reconstruction slip the original does not have gets no switch.
- **Project policy**: a choice this project makes where the source leaves one
  open. Every policy below is a proposal unless the [owner
  decisions](#owner-decisions-2026-10-16) settle it.

No original file, probe or game data was read for this work item. Original
executable parity is **NOT_VERIFIED**.

## Answer

- **One AI pass per NPC tick.** A per-NPC AI time event fires every
  250..5000 ms depending on the distance to the PC (250 ms next to the PC,
  5000 ms at 30 cells or more) plus up to 5 s of jitter on the first event;
  NPCs farther than 30 cells from the PC do not think, and a PC follower left
  that far behind is moved to the leader. The pass runs the first-heartbeat
  and heartbeat scripts first; a script can stop it. In a turn-based round the
  same pass runs once at the start of the NPC's subturn after its heartbeat
  script, and if that pass starts no animation goal the NPC's AP drop to 0 and
  its subturn ends (the "ends its subturn early" part of ARC-RES-030 O5). In
  real time an NPC that is attacked (missed or damaged) also gets a pass at
  once; in a round that extra pass stops at the gate unless it is the NPC's
  turn (tick (0x4AD200, 0x4AD420, 0x4AD4D0, 0x4AD5D0, 0x4AD610, 0x4AD6B0, 0x4AD6E0, 0x4AD700, 0x4AD730, 0x4AD790, 0x4AD7D0), activity (0x4AD420, 0x4AD4D0), delay (0x4AD5D0, 0x4AD610),
  subturn (0x4B7010), attacked: miss (0x4B2F60), attacked: damage (0x4B4390)).
- **Fixed pipeline.** Each pass: gate (dead, off, stoned, unconscious, PC
  off, in a conversation, someone else's turn, busy animation, generator) →
  refresh wield/weapon flags → **heal** (self, leader, other followers); if no
  heal was chosen, **look for items** (ammo, weapon, armour when flagged); if
  no pickup started either, **update the danger state** (which also picks a
  spell or item, below) → **perform** (pipeline (0x4A84F0, 0x4A8570, 0x4A88D0), gate (0x4A8570)).
- **Four danger states.** Every NPC is in one of NONE, COMBAT_FOCUS (fighting
  a target), FLEE or SURRENDER, stored as critter/NPC flags plus a
  combat-focus or fleeing-from handle. One setter owns all transitions; it
  fires the `ENTER_COMBAT`/`EXIT_COMBAT` scripts, the alerted sound and the
  reaction drop toward a PC enemy, and it is where invulnerable critters, the
  clockwork decoy, the "no flee" flag, a leader with extraordinary charisma
  (followers never flee) and the global "NPC fighting" switch are applied
  (state read (0x4AAB00), setter (0x4AABE0)).
- **Hostility is decided per candidate.** A peaceful NPC scans critters in
  the square of half-size `clamp(Perception, 1, 10)` cells, nearest first
  (decoys first, unconscious ones last), keeps only those it can see or hear,
  and attacks the first that is kill-on-sight (script veto, KOS flag and
  faction, reaction at or below AI parameter 10, alignment gap at or above
  parameter 11, decoy, guard duty), or helps an ally under attack, or avenges
  a dead ally (find target (0x4AB460), KOS (0x4AE120), protect (0x4AE3A0, 0x4AE450)).
- **A fight keeps a target and an enemy list.** A fighting NPC keeps its
  current focus while it stays valid (alive, not stoned, not its own leader or
  party, within 20 cells of it and of its leader, a master prowler it cannot
  see is dropped); else it unlocks the target and scans afresh as a peaceful
  NPC does; else it takes the last critter that hit it, else a random valid
  entry of its enemy list (the "shit list", which holds the attacker's whole
  team), else it calms down (danger update (0x4A92D0),
  validity (0x4AB990, 0x4ABBC0), enemy list (0x4AF8C0, 0x4AF930, 0x4AF9D0, 0x4AFA90, 0x4AFB30, 0x4AFBB0, 0x4AFBD0, 0x4AFC70)).
- **Morale.** When an NPC is attacked, joins an ally's fight or witnesses
  one, it first asks whether to flee; a target it finds by its own scan is
  fought without that check. It never flees with "no flee" or under mind
  control, or while the target's HP% is below parameter 3; it flees when its
  own HP% is at most parameter 0, when the opposing party is larger by
  parameter 1 or more, or higher in summed level by parameter 2 or more.
  Parameter 4 is how far to flee: once farther than that from what it flees,
  a new attacker is weighed afresh. A fleeing NPC's next action converts it
  to SURRENDER (which also leaves combat mode) while it keeps running;
  surrender ends when the danger source is dead or unconscious, or at random
  (1 in 500 per pass) once HP is at least 80 % (reaction to an
  attack (0x4AA620), should flee (0x4AB2A0, 0x4AB2F0, 0x4AB400, 0x4AB430), flee/surrender
  perform (0x4AC620, 0x4AC660)).
- **Actions, in source order.** (1) The heal ladder by HP% runs first in the
  pipeline (in combat a target above 30 % HP is healed only with parameter 14
  chance). (2) During the danger update a fighting NPC may pick a spell or
  magic item from the source's ten AI action lists (flee, summon, defensive,
  offensive, three heal levels, cure poison, fatigue recovery, resurrect),
  with a 25 % or 50 % chance per pass (50 % for ranged keepers, parameter
  15 > 1; 0 while it is a healthy follower with a healthy leader facing a
  weaker enemy). (3) In perform, an active back-off overrides everything,
  even a chosen spell: the NPC runs until it is parameter 15 cells away (5
  when parameter 15 ≤ 1). (4) Otherwise the chosen cast, item or skill runs.
  (5) Otherwise, in combat: when closer than parameter 15 (> 1), 1 in 20
  starts backing off; else a grenade throw when a fight has just started or
  after backing off, with throwing skill, the target out of its own blast, a
  clear line of fire and no protected critter in the blast; else the attack
  goal on its target (heal (0x4A8940, 0x4A8AA0), spell choice (0x4ABC20, 0x4ABC70, 0x4ABE20, 0x4ABEB0), list
  chooser (0x4ABF10), action (0x4AC180, 0x4AC250, 0x4AC320, 0x4AC350), combat perform (0x4AC6E0),
  back off (0x4AC7B0), grenade (0x4AC910)).
- **Spell and item eligibility** is a typed refusal list: unknown spell, too
  tired or no charge (an NPC without a PC leader below 20 % fatigue never
  casts; cost at or above current fatigue; an empty mana store), not enough
  intelligence or willpower, "cast highest only", a maintained spell with no
  free maintenance slot, fail chance at or above 38 % (not checked for
  healing spells on a target at or below 30 % HP), and a blocked line of fire
  (eligibility (0x4AE720)). The ten lists come from per-spell `AI_*`
  priorities in the spell data and from items' mana stores or AI action
  field. Each list holds either items or spells: in combat at or below 50 %
  fatigue items, and spells only when no item qualifies; otherwise spells,
  and items only when no spell qualifies (mt_ai lists (0x4CC2A0, 0x4CC2D0, 0x4CC310, 0x4CC350, 0x4CC380, 0x4CC470, 0x4CC4C0, 0x4CC4F0, 0x4CC520, 0x4CC580, 0x4CC6F0, 0x4CC740, 0x4CC770, 0x4CC810, 0x4CC8A0, 0x4CC930, 0x4CCA30, 0x4CCA90, 0x4CCAD0, 0x4CCB10, 0x4CCB50, 0x4CCB90, 0x4CCBF0, 0x4CCC00, 0x4CCC40),
  populate (0x4CC580), spell fields (0x457E70)).
- **Combat is contagious.** Every attack (and a fight start) notifies: the
  attacker's and the victim's followers join or complain ("upset attacking"
  their own party, origin, faction or a good target), the victim records who
  hit it and adds the attacker's team to its enemy list, a PC hitting its own
  follower changes its reaction by parameter 9 (the follower-hit anomaly of
  ARC-RES-028) and may make it leave, and nearby NPCs who see or hear (radius
  2/8/15 by loudness) either join to protect their faction/group or flee
  (attack event (0x4A94C0, 0x4A9530, 0x4A9560, 0x4A9650), followers (0x4A9B80, 0x4A9C00), noise (0x4A9E10, 0x4A9F10)). When a
  PC kills, same-faction NPCs within 3 cells already fighting that PC back
  off. Dynamite counts as an attack by the PC on every NPC within 10 cells
  that sees or hears it (not its followers, not mind-controlled ones);
  portal and container crimes alert guards (killed (0x4AA1B0),
  dynamite (0x4AA0D0), guards (0x4AEE50)).
- **Parameters are data.** The 17 AI parameters per row come from
  `rules\ai_params.mes` (rows 0..99) and the module's `rules\gameai.mes`
  (rows 100..149), filled in file order from the first entry found rather
  than by entry number; each NPC names its row in `OBJ_F_NPC_AI_DATA`
  (load (0x4A8320), module load (0x4A8400)). The local
  data has 41 base rows and one module row
  ([ARC-RES-028 L3](npc-reaction-reputation-evidence.md)).

## Source map

Every row is a self-contained unit that a stage below owns. Addresses are of
the AI functions (0x4A8320–0x4AFC70) unless named.

| # | Unit | Source | First-pass summary | Stage |
| --- | --- | --- | --- | --- |
| S1 | AI parameter table | 0x4A8320 | 17 named integers per row; 150 rows, base file fills 0..99, module 100..149. | A1 (done: [core loop](npc-ai-core-loop.md)) |
| S2 | Tick and activity | 0x4AD200, 0x4AD420, 0x4AD4D0, 0x4AD5D0, 0x4AD610, 0x4AD6B0, 0x4AD6E0, 0x4AD700, 0x4AD730, 0x4AD790, 0x4AD7D0 | Heartbeat scripts, generator branch, 30-cell activity, distance-scaled delay, follower catch-up. | A1 (done: [core loop](npc-ai-core-loop.md)) |
| S3 | Gate and pipeline | 0x4A84F0, 0x4A8570, 0x4A88D0 | Heal, else item search, else danger update; then perform; status recoveries (blind 1/160, crippled 1/8000 per pass, stun end). | A1 (done: [core loop](npc-ai-core-loop.md)) |
| S4 | Danger state and setter | 0x4AAB00, 0x4AABE0 | Four states, flags, scripts, sound, reaction drop, global switch. | A1 (done: [core loop](npc-ai-core-loop.md)) |
| S5 | Turn-based hooks | combat (0x4B7010, 0x4B2F60, 0x4B4390) | AI pass at subturn start after the heartbeat script, AP to 0 when it starts no goal; immediate pass for an attacked NPC (real time only in effect); AP rules in ARC-RES-030 C8/C11. | A1 (done: [core loop](npc-ai-core-loop.md)) |
| S6 | Sight | 0x4AF240, 0x4AF260 | Facing, invisibility vs extraordinary PE or detect-invisible, prowling against concealment, clamp 1..10, +1 when blocked; returns excess distance, 0 = seen. | A2 |
| S7 | Hearing | 0x4AF470, 0x4AF640 | Loudness 2/8/15, sleeping halves PE, moving silently, sound traversal cost along a straight path. | A2 |
| S8 | Line of fire | 0x4ADE00 | Shared with combat O4; blocker and cover rating. | A2 |
| S9 | Noise and witnesses | 0x4A9E10, 0x4A9F10, 0x4AA0D0 | Witness joins a protected side or flees; dynamite. | A2 |
| S10 | KOS, protect, upset | 0x4ADCC0, 0x4AE120, 0x4AE3A0, 0x4AE450 | Reasons KOS_FACTION/REACTION/ALIGNMENT/GUARD/DECOY; protect FACTION/ORIGIN/GROUP/SELF; upset GOOD/ORIGIN/FACTION/PARTY. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S11 | Target search and choice | 0x4AB0B0, 0x4AB460, 0x4AB990, 0x4ABBC0 | Nearest-first scan in a square box, KOS, ally help, avenge; two-candidate choice: 50 % nearest, 50 % level − distance; 20-cell leash. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S12 | Enemy list and target lock | 0x4AF8C0, 0x4AF930, 0x4AF9D0, 0x4AFA90, 0x4AFB30, 0x4AFBB0, 0x4AFBD0, 0x4AFC70 | Team added on attack, random valid pick, lock for orders. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S13 | Attack event fan-out | 0x4A94C0, 0x4A9530, 0x4A9560, 0x4A9650, 0x4A9B80, 0x4A9C00, 0x4AA300, 0x4AA420, 0x4AA4A0, 0x4AA580, 0x4AA620 | Redirect, followers, reaction changes, jilted follower, peace (focus cleared, reaction raised to 50). | A3 (done: [hostility](npc-ai-hostility.md)) |
| S14 | Morale | 0x4AAF50, 0x4AB030, 0x4AB2A0, 0x4AB2F0, 0x4AB400, 0x4AB430, 0x4AC620, 0x4AC660, 0x4AF130, 0x4AF170, 0x4AF1D0, 0x4AF210 | Flee rule, spell-assisted flee, flee → surrender, recovery. | A4 (done: [morale](npc-ai-morale.md)) |
| S15 | Back off and min distance | 0x4AC6E0, 0x4AC7B0 | Parameter 15, path away, kill-triggered back off. | A4 (done: [morale](npc-ai-morale.md)) |
| S16 | Heal and support | 0x4A8940, 0x4A8AA0 | Resurrect, healer's own fatigue < 20 %, heal skill < 90 % out of combat, heavy < 40, medium < 55, poison, light < 70 (< 90 out of combat); self, leader, followers. | A5 |
| S17 | Spell/item choice | 0x4ABC20, 0x4ABC70, 0x4ABE20, 0x4ABEB0, 0x4ABF10, 0x4AE720, AI spell lists (0x4CC2A0, 0x4CC2D0, 0x4CC310, 0x4CC350, 0x4CC380, 0x4CC470, 0x4CC4C0, 0x4CC4F0, 0x4CC520, 0x4CC580, 0x4CC6F0, 0x4CC740, 0x4CC770, 0x4CC810, 0x4CC8A0, 0x4CC930, 0x4CCA30, 0x4CCA90, 0x4CCAD0, 0x4CCB10, 0x4CCB50, 0x4CCB90, 0x4CCBF0, 0x4CCC00, 0x4CCC40) | Summon when alone, defensive by parameter 13 (see P7), offensive; chooser takes the first entry, 21 % a random start; item entries without a spell are used at once, unchecked; refusal codes 1–8. | A5 |
| S18 | Items, weapons, grenades | 0x4A8E70, 0x4A8F90, 0x4AC910, 0x4ADFF0 | Look for ammo/weapon/armour, wield best, grenade with friendly-fire check. | A5 |
| S19 | Action execution | 0x4AC180, 0x4AC250, 0x4AC320, 0x4AC350, animation (0x434AE0, 0x434B00) | Cast through invocation, item/skill goals, attack goal (approach, ranged, real-time repeat). | A6 |
| S20 | Followers and orders | broadcast (0x4C31A0), AI (0x4AA4A0, 0x4AA580) | Attack/stop orders, no-attack flag, demaintain spells after the fight, witness/near-death floats. | A7 |
| S21 | Script and dialog surface | script (0x447220), the dialog callers | `WILL_KOS`, `ENTER/EXIT/START/END_COMBAT`, heartbeat; dialog refusal in combat; cheat toggle "NPC fighting" (cheat level ≥ 3, key P). | A8 |
| S22 | Non-combat routine | 0x4ACBB0, 0x4ACD90, 0x4ACDB0, 0x4AD060, 0x4AD0B0, 0x4AD140, 0x4AD1B0, 0x4AEB10, 0x4AEB70, 0x4AECA0, 0x4AED80, 0x4AEE50 | Waypoints, standpoints, beds, portals and containers: FR-AI-001/003, out of this plan except where combat interrupts them. | — |

## First-pass anomalies (to confirm, not adopted)

**Original check (ARC-RES-048):** P1, P2 and P3 are reconstruction slips (P3 tests the redirect target's intelligence, not the candidate's), P4–P7 are the original's behaviour. The verdicts in the [verification ledger](original-verification-ledger.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024) supersede the earlier readings below where they differ.

- **P1** The item search keeps the candidate with the **largest** distance
  although it tracks a "nearest" variable (items (0x4A8E70, 0x4A8F90)).
- **P2** The party-level sum adds the leader's level once per follower instead
  of each follower's level, which skews the "outlevelled" flee test
  (party (0x4AE020, 0x4AE0A0)). Settled by ARC-RES-058: a reconstruction slip,
  the original sums each member's own level ([morale](npc-ai-morale.md#5-anomalies-and-decisions)).
- **P3** The redirect filter compares the intelligence of the threshold number
  instead of the candidate's (redirect (0x4A94C0, 0x4A9530, 0x4A9560, 0x4A9650)).
  Settled by ARC-RES-057: the original tests the spell target's intelligence
  (reconstruction slip confirmed); that test is itself recorded as a bug of
  the original (by inference, owner to confirm), with a proposed Original switch ([hostility](npc-ai-hostility.md#8-anomalies-and-decisions)).
- **P4** The heal step tests an NPC-flag constant (no-attack) against the
  critter-flag field, where the same bit means "mechanical"; the effect is
  that mechanical critters are never healed by AI (heal (0x4A8940, 0x4A8AA0)).
- **P5** While an NPC is in combat mode every pass runs its start-combat
  script, and in real time the end-combat script just before it; the intent
  is unclear (gate (0x4A8570)). Settled by ARC-RES-054: the original's design,
  kept ([core loop](npc-ai-core-loop.md#7-anomalies-and-decisions)).
- **P6** Whether FLEE → SURRENDER on the first fleeing action is the intended
  morale model or a reconstruction slip needs a second reading against
  the turn-based UI functions and the scripts that test "surrendered" (perform (0x4AC620, 0x4AC660)).
  Settled by ARC-RES-058: the original's design, kept
  ([morale](npc-ai-morale.md#3-a-surrendered-npc)).
- **P7** Parameter 13 is described as the chance of a defensive spell, but
  the defensive list is tried when the roll is **above** it, so the chance is
  `100 − parameter 13` % (spell choice (0x4ABC20, 0x4ABC70, 0x4ABE20, 0x4ABEB0)).

## Phase 1: faithful reconstruction

Goal: an NPC fight in the AREA viewer behaves as the source specifies, with
the recorded D2 fixes, with
every random roll injected and seedable and every decision explainable by a
reason code. Order follows dependencies; each item is one PR.

### Prerequisites outside this plan

Combat core slices 1–4 of [ARC-RES-030](combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(vital state, attack resolver, turn-based round, real-time cadence and combat
mode), the [reaction owner](npc-reaction-reputation-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(slices 1–2), the [companion follow slices](companion-follow-evidence.md) of
ARC-RES-024 (no code allocated yet) and, for slice I8, the magic/tech domain
(FR-MAGIC-001..006; what I8 needs from the magic core is listed in
[ARC-RES-043](magic-core-evidence.md#what-ai-slice-i8-needs-from-this-domain)). The research stages need none of these; slices I1–I7
need only the first two.

### Research stages (source reading, one document each)

Codes are allocated when each task is created (D6).

| Stage | Code | Scope (source units) | Output |
| --- | --- | --- | --- |
| A0 | ARC-RES-040 (allocated 2026-10-16 with the task, D7) for the source-reading half; ARC-RES-041 for the census | What the original loads and runs for combat, AI, skills, magic and technology: init order, every data file and prototype field these modules read, when and how, and which module owns it at runtime; original data census on the Mac (absorbs A9). | Source-reading half: [load map](combat-domain-load-map.md); census: [data census](combat-data-census.md) with data aggregates and first real fight candidates. |
| A1 | ARC-RES-054 | Core loop: S1–S5. Parameter semantics, tick cadence, pipeline order, danger state machine with every flag and script, turn-based and real-time entry points. | State diagram, transition table, cadence formula, parameter dictionary: [core loop](npc-ai-core-loop.md) (done). |
| A2 | on creation | Awareness: S6–S9. See/hear formulas, loudness, line of fire, witnesses. | Pure-function specs with worked numbers; closes combat O4. |
| A3 | ARC-RES-057 | Hostility and targeting: S10–S13. | KOS/protect/upset decision tables, target scan order, enemy-list lifecycle: [hostility](npc-ai-hostility.md) (done). |
| A4 | ARC-RES-058 | Morale: S14–S15. | Flee/surrender/back-off rules, P2 and P6 settled: [morale](npc-ai-morale.md) (done). |
| A5 | on creation | Support and magic: S16–S18. | Heal ladder, ten action lists, eligibility codes, item search (P1, P4, P7). |
| A6 | on creation | Execution: S19 and the animation attack, approach, flee and run goals. | How an intention becomes movement and attacks in both modes; closes companion O5. |
| A7 | on creation | Followers in combat and orders: S20. | Order effects, follower join rules, post-fight cleanup. |
| A8 | on creation | Script and dialog surface: S21. | Every SAP event and SCR action that reads or writes AI state. |
| A9 | in A0 (ARC-RES-041) | Data census: `ai_params.mes`/`gameai.mes` rows, spell `AI_*` fields, item AI action field, KOS/no-flee/guard/AI-row of the Crash Site and cave NPCs and creatures. | Local aggregates only, like ARC-RES-028 L3; picks the first real fight: [data census](combat-data-census.md#7-first-fight-candidates-plan-slice-i10). |

A0 comes first (D7), then A1; A2–A5 can run in parallel after it, with A5 reusing the
line-of-fire and protect definitions of A2 and A3; A6–A8 after A3.
A9 is part of A0.

### Implementation slices (proposals, after the matching research)

| Slice | Needs | Result |
| --- | --- | --- |
| I1 AI parameters | A1, A0 | Data reader for both files and a typed 17-field record with names from A1. |
| I2 Awareness | A2, I1 | Pure see/hear/line-of-fire functions over explicit inputs; synthetic matrix (FR-AI-002 acceptance). |
| I3 Danger state | A1, I1 | Session-owned per-NPC state, flags, focus, enemy list, snapshot schema version, script hook points as typed events. |
| I4 Hostility and targets | A3, I2, I3 | KOS/protect/upset with reason codes, scan and choice with injected rolls. |
| I5 Morale | A4, I4 | Flee/surrender/back-off decisions and their state transitions. |
| I6 Scheduler | A1, I3 | AI time event on the session clock, 30-cell activity, turn-based subturn entry. |
| I7 Melee and ranged executor | A6, combat slices, I4 | Intent to route planner and attack resolver; real-time and turn-based. |
| I8 Support and magic | A5, magic domain | Heal ladder, lists, eligibility, items, grenades. |
| I9 Followers in combat | A7, companion follow slices | Join/complain rules, orders, cleanup. |
| I10 First fight | I1–I7, A0 | One Crash Site fight in the AREA viewer with an AI overlay (state, target, last reason). |
| I11 Parity check | I10 | Scripted scenarios observed in the original game on the Mac compared with seeded runs; NOT_VERIFIED becomes per-rule evidence. |

### Design rules for phase 1 that phase 2 needs

1. **Every decision is a named, pure policy.** Target choice, KOS, flee,
   action choice and heal thresholds are functions over explicit records;
   the source behaviour is the default policy, not inline code.
2. **Every anomaly is a recorded decision.** A confirmed slip (P1..P7, the
   load-map anomalies LM1..LM20 of ARC-RES-040 and those A1–A8 find) is fixed in phase 1 (D2), with an entry naming the source
   behaviour, the chosen behaviour and the reason; a retail bug stays behind a
   named switch of the "Original" setting (D8) so runs can still be compared
   with the original game, and a reconstruction-only slip gets none.
3. **Randomness is injected and logged.** Each roll has a purpose label, so
   a seeded run replays exactly and a trace shows which roll decided what.
4. **Decisions return reason codes.** The overlay, tests and mods read the
   same "why" (for example `KOS_REACTION`, `FLEE_OUTNUMBERED`,
   `CAST_REFUSED_FAIL_CHANCE`).
5. **Parameters are data.** Rows load from game files and may be
   overridden from outside the repository; no number lives only in code.
6. **The default follows the source plus recorded fixes.** A change to
   default behaviour needs a source reading, an original-runtime observation
   or a recorded D2 fix, never a balance idea.
7. **State does not depend on the profile.** Danger state, focus, enemy list
   and timers have one schema for every profile, so a profile can change
   mid-game and any save loads under any profile (D4).
8. **The decision layer is replaceable as a whole.** Everything that decides
   sits behind one interface over that state, so a later combat AI or a new
   combat system (D3) can wrap or replace it, aiming at no save format
   change.

## Phase 2: patches and mods

Goal: optional changes on top of the phase 1 rules, chosen per profile,
never altering the "Steamwoven" profile (the original plus the recorded D2
fixes; called "Default" before D8). The "Original" variant set (the retail
behaviour for every D2 fix; called "Source" before D8) exists from phase 1
(design rule 2); phase 2 only exposes it as a profile. Nothing here starts
before the matching phase 1 slice exists.

| Step | When | Content |
| --- | --- | --- |
| M0 Profiles and policy seams | with the first slice that needs a second policy | "Steamwoven", "Original" (D8) and named alternative profiles; per-policy override points; the active profile is a setting that may change mid-game, saves hold only profile-independent state (D4); the decision trace is the debugging tool. |
| M1 Original profile | with M0 | The "Original" variants of every D2 fix as a player-selectable profile (for example P1 farthest item, P2 leader level per follower, the combat-core anomaly A9 double subturn), for players who want the original quirks; I11 parity runs use the same variants. |
| M2 Data overrides | after I1 | External pack (outside the repo, like the HD pack) overriding parameter rows, per-NPC row choice and spell AI priorities; validated, versioned. |
| M3 Tactical improvements | after I7/I8 | Candidates to test one at a time: threat-based target score (damage, HP, range) instead of the 50/50 rule, focus fire, leftover-AP use in turn-based mode, keeping range and cover for ranged NPCs, friendly-fire checks for area spells (the source checks only grenades), healers prioritising the weakest ally, fleeing toward allies, group cohesion. |
| M4 Companion control | after I9 | Stances (passive, defensive, aggressive), "don't use consumables", "no area spells", extended orders. |
| M5 Difficulty and accessibility | after M0 | Aggression and morale scales, "NPC fighting" as a player option instead of a cheat. |
| M6 AI sandbox | after I10 | Viewer arena: place combatants, run with a seed under two profiles, compare traces and outcomes (win rate, length of fight) for balancing. |
| M7 Mod interface | before M8 | Data-only mods first; proposal: code mods (C# or Lua) are chosen when M8 starts (D3). |
| M8 New combat AI | after M6 | A qualitative rework: revised NPC logic and group interaction patterns among allies and against enemies, as a new decision layer over the phase 1 state (design rule 8), or a full replacement built on Godot (a separate architecture decision, since Data, Simulation, Scripting and Session stay Godot-independent). |

## Anomaly decisions

Each confirmed anomaly gets a row here when its stage research settles it:
source behaviour, chosen behaviour, reason and the switch name. Until then
P1..P7 above and LM1..LM20 of the [load map](combat-domain-load-map.md#8-anomalies-found-by-this-reading)
are open, and the combat-core anomalies A1–A9 of
[ARC-RES-030](combat-core-evidence.md#anomalies-recorded-not-adopted) follow
the same rule when combat slice 2 settles them, as do the magic-core anomalies
MG1..MG20 of [ARC-RES-043](magic-core-evidence.md#anomalies-recorded-not-adopted)
when the magic slices settle them.

The combat-core rows below were also checked against the original 1.0.7.4
executable ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)).
"Reconstruction only" marks a reconstruction slip the original does not have, so both
settings of D8 follow the original and there is no switch; "Original setting"
marks a switch that the Original setting turns on to keep a bug the retail
game really has.

| Anomaly | Source behaviour | Chosen behaviour | Reason | Switch |
| --- | --- | --- | --- | --- |
| A1 (ARC-RES-030) | Reconstruction only: `sort_combat_list` moves only PCs to the round's tail; PC followers keep their scan position. The original 1.0.7.4 tests the PC leader's type ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). | PCs and the PCs' followers move to the tail; scan order kept within each group, as in the original. | Reconstruction slip; settled 2026-10-16 after the owner asked for the original behaviour. Implemented by ARC-COMBAT-003 (`TurnBasedRules.Order`). | none: both settings follow the original |
| A2 (ARC-RES-030) | Original 1.0.7.4: the bed's −30 invocation modifier is counted twice (−60). | Counted once (−30). | The run adds the modifier to a difficulty that already starts from it. ARC-COMBAT-001. | `InvocationModifierCountedTwice` (Original setting) |
| A3 (ARC-RES-030) | Reconstruction only: getting up in a round is refused on the critter's own subturn and free on any other. The original allows it only on the own turn for 5 AP ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). | Only on the critter's own subturn, for 5 AP, as in the original. | Reconstruction slip; settled 2026-10-16 after the owner asked for confirmation. ARC-COMBAT-003 provides the cost (`TurnBasedRules.GetUpCost`) and the own-subturn gate; the get-up animation lands with its host. | none: both settings follow the original |
| A4 (ARC-RES-030) | Reconstruction only: the every-fifth-run-step fatigue charge in combat mode writes back the unchanged fatigue damage, so running costs no fatigue and a critter without fatigue damage fails the step. The original writes fatigue damage + 1 ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). | Each charge adds 1 fatigue damage (every 5th run step, ×4 when fatigue-limiting, then ÷4 (at least 1) when fatigue-draining), as in the original. | Reconstruction slip; settled 2026-10-16 after the owner asked to confirm or refute. Lands with the movement/real-time slice. | none: both settings follow the original |
| A5 (ARC-RES-030) | Reconstruction only: a crippled-arm critical drops the weapon when only the shield qualifies; partial healing of crippled legs clears blindness. The original drops the shield and clears crippled legs ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). | Drop the shield; clear crippled legs, as in the original. | Reconstruction slips; owner confirmed 2026-10-16. The healing half landed with ARC-COMBAT-002 (`LifeRules.Heal`); the shield drop lands with the critical-effect (FR-COMBAT-009) and item slices. | none: both settings follow the original |
| A6 (ARC-RES-030) | Original 1.0.7.4: `object_get_ac` applies AC-adjustment effects to the whole AC and adds the effective AcAdjustment stat, which already carries them. | Effects once, through the effective AcAdjustment stat (`AttackRules.ArmorClass`). | Double count. ARC-COMBAT-001. | `ArmorClassEffectsCountedTwice` (Original setting) |
| A7 (ARC-RES-030) | Original 1.0.7.4: a critical self-hit rolls the critical-hit procedure twice (critical-miss branch and damage step); the flags add up. | Rolled once, at the damage step. | Double processing. ARC-COMBAT-001. | `CriticalSelfHitRolledTwice` (Original setting) |
| A8 (ARC-RES-030) | Reconstruction only: the critical effect chance is never assigned when the weapon's magic was cancelled. The original sets it to 0 ([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). | 0, as in the original. | Reconstruction slip. ARC-COMBAT-001. | none: both settings follow the original (the reconstruction's value is undefined) |
| A9 (ARC-RES-030) | Original 1.0.7.4: when the skip loop of `combat_turn_based_next_subturn` runs past the last entry, the new round's first subturn starts twice (heartbeat and AI twice). | Started once. | `combat_turn_based_begin_turn` already starts it; owner confirmed 2026-10-16. Implemented by ARC-COMBAT-003. | `FirstSubturnStartedTwice` (Original setting) |
| A10 (ARC-COMBAT-001) | Original 1.0.7.4: `combat_calc_dmg` keeps the backstab flags after a critical miss retargets the attack to the attacker, so the self-hit gets the backstab damage bonus. | No backstab bonus on a self-hit. | The self-hit is not a backstab. Found by ARC-COMBAT-001; fixed under D2. | `BackstabDamageOnSelfHit` (Original setting) |
| A11 (ARC-COMBAT-003) | Original 1.0.7.4: the stalled-actor check records a new actor without its action points, so its second check compares with the previous actor's last value (0 after a round start) and may force a critter that spent to stop. | The new actor's own action points are the reference. | A stale reference from another critter; fixed under D2 by ARC-COMBAT-003 ([ledger TB6](original-verification-ledger.md#turn-based-round-arc-combat-003)). | `StallCheckKeepsPreviousActionPoints` (Original setting) |
| A12 (ARC-COMBAT-003) | Original 1.0.7.4: an overexertion that knocks the PC out passes the turn on inside its damage step, then sets the action points, by now the next participant's, to 0. | The next participant keeps the action points its subturn granted. | The zero was meant for the overexerting PC; fixed under D2 by ARC-COMBAT-003 ([ledger TB9](original-verification-ledger.md#turn-based-round-arc-combat-003)). | `OverexertionKnockoutClearsNextActionPoints` (Original setting) |
| Reputation A2 (ARC-RES-028) | Original 1.0.7.4: a deliberate hit by the PC on its own follower adds AI parameter 9 to the follower's reaction, although the field describes how much the hit lowers it. | Lowered by parameter 9. | Owner decision 2026-10-17 (PR #184); implemented by ARC-WORLD-026 in `ReactionRules.PcHit`. | `FollowerHitRaisesReaction` (Original setting) |
| LM14 (ARC-RES-040) | The melee damage scaling helper always returns 100, so it changes nothing. | Kept: no scaling. | The intended factor is unknown, so there is no confirmed slip to fix. ARC-COMBAT-001. | none |
| P5 (ARC-RES-039) | Original 1.0.7.4: in combat mode every pass that reaches the gate's combat test runs the start-combat script, in real time after the end-combat script; the end-combat script also runs at the end of the NPC's subturn ([core loop](npc-ai-core-loop.md#3-one-pass)). | Kept: per-pass hooks in both settings. | The original's design, which scripts are written against; nothing marks it as a slip ([ledger P5](original-verification-ledger.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024), re-read by ARC-RES-054). Proposed by ARC-RES-054. | none |
| LM3 (ARC-RES-040) | Original 1.0.7.4: AI rows fill by position from the first entry's number, and an NPC's row is copied without a bounds check. | Rows by position as in the original; a row outside the table is refused in both settings (no out-of-bounds read, owner rule). | A latent bug of the original; the installed tables have no gap, so positional and by-number filling agree on shipped data ([census](combat-data-census.md#2-ai-parameter-rows)). Implemented by ARC-DATA-033 ([AI parameter table](../world/ai-parameters.md)); confirmed by ARC-RES-054. | none |
| LM4 (ARC-RES-040) | Original 1.0.7.4: the module unload of the AI rows does nothing, so rows 100..149 survive into the next module. | Not reachable: Steamwoven builds the module rows with each module's content layer and models no module change inside a session. | Load-map policy 1. Proposed by ARC-RES-054. | none |
| CL1 (ARC-RES-054) | Reconstruction only: a still-stunned NPC passes the gate and acts. The original ends every pass while the NPC is stunned, also the pass that clears the flag ([ledger CL1](original-verification-ledger.md#npc-ai-core-loop-arc-res-054)). | As the original. | Reconstruction slip. Lands with slice I6. | none: both settings follow the original |
| CL2 (ARC-RES-054) | Reconstruction only: after the full re-wield only "check wield" is cleared, so the next pass re-wields the weapon. The original clears "check weapon" too ([ledger CL2](original-verification-ledger.md#npc-ai-core-loop-arc-res-054)). | As the original. | Reconstruction slip. Lands with slice I6. | none: both settings follow the original |
| CL4 (ARC-RES-054) | Original 1.0.7.4: a surrender leaves combat mode and then writes back critter flags read before, so the combat-mode bit is set again and the surrendered NPC still counts as in combat mode ([ledger CL4](original-verification-ledger.md#npc-ai-core-loop-arc-res-054)). | A surrender leaves the combat-mode bit cleared. | A stale write; the setter clearly means to leave combat mode. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Lands with slice I2. | `SurrenderKeepsCombatModeFlag` (Original setting) |
| CL5 (ARC-RES-054) | Original 1.0.7.4: the gate's blind, crippled and stun writes use critter flags read before the combat scripts of the same pass, undoing a script's flag change ([ledger CL5](original-verification-ledger.md#npc-ai-core-loop-arc-res-054)). | Each write changes only its own bits of the current flags. | A stale write (latent). Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Lands with slice I6. | `GateRecoveryWritesStaleFlags` (Original setting) |
| P2 (ARC-RES-039) | Reconstruction only: the party-level sum of the flee test adds the leader's level once per follower. The original adds each member's own level, followers of followers included ([ledger MR2](original-verification-ledger.md#npc-ai-morale-arc-res-058)). | As the original. | Reconstruction slip, confirmed by two readers. Lands with slice I5. | none: both settings follow the original |
| P6 (ARC-RES-039) | Original 1.0.7.4: a fleeing NPC's next pass without an action requests SURRENDER and starts the flee goal; the dialog start, the dialog script point, the greeting, "can speak" and leaving combat mode treat SURRENDER as a lasting "out of the fight" state ([morale](npc-ai-morale.md#3-a-surrendered-npc)). | Kept in both settings. | The original's design, which the interface and scripts are written against ([ledger MR8, MR11](original-verification-ledger.md#npc-ai-morale-arc-res-058)). Proposed by ARC-RES-058. | none |
| MR3 (ARC-RES-058) | Original 1.0.7.4: a leaderless NPC counts as a party of one for the flee test, its own followers ignored, while each of its followers counts the whole group. | A leaderless NPC counts its own followers like a PC. | Looks unintended: the same group is sized differently by its leader and its members. Proposed by ARC-RES-058; owner decision needed; lands with slice I5. | `NpcLeaderPartyCountsAlone` (Original setting), proposed |
| MR7 (ARC-RES-058) | Original 1.0.7.4: the "flee spell used" flag (SPELL_FLEE) is cleared only by the flee-spell step, so a flight left before that step makes the next flight skip its flee spell or item once. | SPELL_FLEE is cleared when the NPC leaves FLEE. | A stale flag from an earlier flight (latent). Proposed by ARC-RES-058; lands with slices I5/I8. | `StaleSpellFleeSkipsFleeSpell` (Original setting), proposed |
| MR9 (ARC-RES-058) | Original 1.0.7.4: perform fleeing starts the flee goal even when the setter refused the surrender (invulnerable, or a leader with extraordinary charisma). | The flee goal starts only when SURRENDER was applied. | An ignored result (latent: needs a change during a flight, for example invulnerability, a new leader or the leader's charisma; a script writing FLEEING directly is unchecked, stage A8). Proposed by ARC-RES-058; lands with slice I5. | `FleeGoalAfterRefusedSurrender` (Original setting), proposed |
| MR13 (ARC-RES-058) | Original 1.0.7.4: mind control sets NO_FLEE and its end does not clear it, so a formerly controlled NPC never flees again. | Candidate, for the owner: both settings keep the original until the owner decides; the fix would restore NO_FLEE to its value before the control (one remembered bit, stored in both settings). | The flag outlives the control and cannot be told from data, but the original also sets NO_FLEE for good by design (the "move away from object" goal the flee goal runs, [ledger MR12](original-verification-ledger.md#npc-ai-morale-arc-res-058)), so the end may be intended. Proposed by ARC-RES-058; owner decision needed; lands with the magic slice for mind control. | `MindControlLeavesNoFlee` (Original setting), proposed only if the owner chooses the fix |
| MR15 (ARC-RES-058) | Original 1.0.7.4: when no straight "away" line is clear, the back-off path retries a line that already failed, so the back-off ends. | The back-off ends when no straight line is clear, as in the original. | The retry changes nothing; no visible difference to keep. Proposed by ARC-RES-058; lands with slices I5/I7. | none |
| P3 (ARC-RES-039) | Original 1.0.7.4: the AI redirect compares the spell target's intelligence (both record handles are the target) with the component's `MinIQ:` threshold, so it redirects every candidate or none; no candidate's intelligence is read ([ledger HS17](original-verification-ledger.md#npc-ai-hostility-and-targeting-arc-res-057)). The earlier reading (the threshold number's intelligence) was a reconstruction slip. | Each candidate's intelligence is compared; a candidate above the threshold is not redirected. | Original bug, by inference: a per-candidate filter that tests one fixed critter looks unintended (the field name fits neither reading). Proposed by ARC-RES-057, owner to confirm the verdict; which spells set `MinIQ:` is a data question. Lands with slice I8. | `RedirectTestsTargetIntelligence` (Original setting), proposed |
| HS9 (ARC-RES-057) | Original 1.0.7.4: upset attacking returns PARTY when the target's leader equals the leader argument also when both are null; find target and kill-on-sight pass the NPC's own leader, so for a leaderless NPC the scan's ally help never targets a PC or a leaderless NPC, GUARD never targets a leaderless NPC aggressor, and the guard-versus-monster clauses are dead for leaderless guards ([ledger HS9](original-verification-ledger.md#npc-ai-hostility-and-targeting-arc-res-057)). | PARTY only for a non-null leader argument. | Original bug: validity, protect, faction-same and upset's own GOOD row guard against a null leader; the guard clauses would be dead code. Proposed by ARC-RES-057, owner to confirm (visible change: townspeople and guards join by their own scan, not only as witnesses). Lands with slice I4. | `UpsetPartyMatchesMissingLeader` (Original setting), proposed |
| HS19 (ARC-RES-057) | Original 1.0.7.4: an accidental hit also adds the attacker's team to the victim's enemy list, so the victim's reaction to the PC reads 0 until peace, beside the explicit −10. | Kept in both settings. | Intent unclear, not a confirmed slip; a phase 2 patch may revisit it. | none |
| HS20 (ARC-RES-057) | Original 1.0.7.4: the scan keeps the first 50 critters of the rectangle query before sorting by distance. | Kept in both settings. | The array size; more than 50 critters in a 21×21 box is not expected. | none |
| HS21 (ARC-RES-057) | Original 1.0.7.4: validity's unconscious rule runs a nested scan that does nothing inside a scan, so an unconscious candidate is valid during a scan and otherwise only while nothing else is found. | Kept in both settings. | The original's behaviour; the scan orders unconscious critters last. | none |
| HS22 (ARC-RES-057) | Original 1.0.7.4: the reaction to an attack calls the float callback in its surrender branch without a null test (latent: always set in the game). | Not reachable: Steamwoven has no unset callback. | Never reproduce a crash (owner rule). | none |

## Owner decisions (2026-10-16)

- **D1 Parity target: the source reading first.** (Since D8 the "Original"
  setting follows the retail executable where the two differ; since the
  2026-10-18 owner decision "Reconstruction removed" the executable is the only
  reference.) The source reading was the reference; original runtime
  observations (I11) are added later as checks.
- **D2 Anomalies: fix in phase 1 and document.** A confirmed slip is fixed in
  the default behaviour and recorded under [anomaly
  decisions](#anomaly-decisions); this also covers the combat-core anomalies.
- **D3 Mod format: open.** For now the base is fixing the original logic and
  reconstruction errors (phase 1, D2). A later qualitative AI rework (NPC logic, group patterns with
  allies and enemies) may need C# or Lua, as a new combat system over the old
  one or as a full replacement based on Godot; design rules 7 and 8 keep both
  open.
- **D4 Profiles: switchable mid-game.** Any save loads under any profile.
- **D5 Task types: new types.** `AI`, `COMBAT`, `SKILLS`, `MAGIC` and
  `TECHNOLOGY` are added to the [naming policy](../workflow/task-naming.md);
  proposed convention (to confirm): combat AI slices use `AI`, combat rules
  `COMBAT`, magic slices such as I8's spell half `MAGIC`, and research stays
  `RES`.
- **D6 Codes: allocated on task creation**, not reserved in advance.
- **D7 Next step:** first a detailed study of what the original loads and
  runs (stage A0), before stage A1.
- **D8 Two rule settings (2026-10-17): "Original" and "Steamwoven".**
  "Original" is the retail 1.0.7.4 behaviour, bugs included; "Steamwoven" is
  the original with the recorded D2 fixes and is the default. They replace the
  earlier "Source" and "Default" names. A reconstruction slip that the
  original game does not have (checked in the original executable) gets no
  switch: both settings follow the original there. First used by
  ARC-COMBAT-001 (`CombatAnomalyProfile.Original` / `.Steamwoven`). On 2026-10-17
  the owner extended the two settings to every domain as one game-wide rule mode,
  switchable in the settings menu without a restart: the
  [rule modes contract](../world/rule-modes.md) (ARC-WORLD-023) holds the
  mechanism and the registry of switches.

## Method

An earlier source reading (provenance in git history), covering, by executable
address, the AI functions (0x4A8320–0x4AFC70; the whole
function list, the units S1–S21 once each), the AI spell list functions
(0x4CC1F0–0x4CCC40; list building and cache), and the AI call sites in the combat, animation, broadcast, script, dialog, magick/tech,
critter, skill, trap, start-up and UI functions. Steamwoven inputs are the
requirements and research documents linked above. Evidence level F0 unless a
ledger row is cited. Names in this document are descriptive.


