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
| S6 | Sight | 0x4AF240, 0x4AF260 | Facing, invisibility vs extraordinary PE or detect-invisible, prowling against concealment, clamp 1..10, +1 when blocked; returns excess distance, 0 = seen. | A2 (done: [awareness](npc-ai-awareness.md)) |
| S7 | Hearing | 0x4AF470, 0x4AF640 | Loudness 2/8/15, sleeping halves PE, moving silently, sound traversal cost along a straight path. | A2 (done: [awareness](npc-ai-awareness.md)) |
| S8 | Line of fire | 0x4ADE00 | Shared with combat O4; blocker and cover rating. | A2 (done: [awareness](npc-ai-awareness.md)) |
| S9 | Noise and witnesses | 0x4A9E10, 0x4A9F10, 0x4AA0D0 | Witness joins a protected side or flees; dynamite. | A2 (done: [awareness](npc-ai-awareness.md)) |
| S10 | KOS, protect, upset | 0x4ADCC0, 0x4AE120, 0x4AE3A0, 0x4AE450 | Reasons KOS_FACTION/REACTION/ALIGNMENT/GUARD/DECOY; protect FACTION/ORIGIN/GROUP/SELF; upset GOOD/ORIGIN/FACTION/PARTY. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S11 | Target search and choice | 0x4AB0B0, 0x4AB460, 0x4AB990, 0x4ABBC0 | Nearest-first scan in a square box, KOS, ally help, avenge; two-candidate choice: 50 % nearest, 50 % level − distance; 20-cell leash. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S12 | Enemy list and target lock | 0x4AF8C0, 0x4AF930, 0x4AF9D0, 0x4AFA90, 0x4AFB30, 0x4AFBB0, 0x4AFBD0, 0x4AFC70 | Team added on attack, random valid pick, lock for orders. | A3 (done: [hostility](npc-ai-hostility.md)) |
| S13 | Attack event fan-out | 0x4A94C0, 0x4A9530, 0x4A9560, 0x4A9650, 0x4A9B80, 0x4A9C00, 0x4AA300, 0x4AA420, 0x4AA4A0, 0x4AA580, 0x4AA620 | Redirect, followers, reaction changes, jilted follower, peace (focus cleared, reaction raised to 50). | A3 (done: [hostility](npc-ai-hostility.md)) |
| S14 | Morale | 0x4AAF50, 0x4AB030, 0x4AB2A0, 0x4AB2F0, 0x4AB400, 0x4AB430, 0x4AC620, 0x4AC660, 0x4AF130, 0x4AF170, 0x4AF1D0, 0x4AF210 | Flee rule, spell-assisted flee, flee → surrender, recovery. | A4 (done: [morale](npc-ai-morale.md)) |
| S15 | Back off and min distance | 0x4AC6E0, 0x4AC7B0 | Parameter 15, path away, kill-triggered back off. | A4 (done: [morale](npc-ai-morale.md)) |
| S16 | Heal and support | 0x4A8940, 0x4A8AA0 | Resurrect, healer's own fatigue < 20 %, heal skill < 90 % out of combat, heavy < 40, medium < 55, poison, light < 70 (< 90 out of combat); self, leader, followers. | A5 (done: [support](npc-ai-support.md)) |
| S17 | Spell/item choice | 0x4ABC20, 0x4ABC70, 0x4ABE20, 0x4ABEB0, 0x4ABF10, 0x4AE720, AI spell lists (0x4CC2A0, 0x4CC2D0, 0x4CC310, 0x4CC350, 0x4CC380, 0x4CC470, 0x4CC4C0, 0x4CC4F0, 0x4CC520, 0x4CC580, 0x4CC6F0, 0x4CC740, 0x4CC770, 0x4CC810, 0x4CC8A0, 0x4CC930, 0x4CCA30, 0x4CCA90, 0x4CCAD0, 0x4CCB10, 0x4CCB50, 0x4CCB90, 0x4CCBF0, 0x4CCC00, 0x4CCC40) | Summon when alone, defensive by parameter 13 (see P7), offensive; chooser takes the first entry, 21 % a random start; item entries without a spell are used at once, unchecked; refusal codes 1–8. | A5 (done: [support](npc-ai-support.md)) |
| S18 | Items, weapons, grenades | 0x4A8E70, 0x4A8F90, 0x4AC910, 0x4ADFF0 | Look for ammo/weapon/armour, wield best, grenade with friendly-fire check. | A5 (done: [support](npc-ai-support.md)) |
| S19 | Action execution | 0x4AC180, 0x4AC250, 0x4AC320, 0x4AC350, animation (0x434AE0, 0x434B00) | Cast through invocation, item/skill goals, attack goal (approach, ranged, real-time repeat). | A6 (done: [execution](npc-ai-execution.md)) |
| S20 | Followers and orders | broadcast (0x4C31A0), AI (0x4AA4A0, 0x4AA580) | Attack/stop orders, no-attack flag, demaintain spells after the fight, witness/near-death floats. | A7 (done: [followers](npc-ai-followers.md)) |
| S21 | Script and dialog surface | script (0x447220), the dialog callers | `WILL_KOS`, `ENTER/EXIT/START/END_COMBAT`, heartbeat; dialog refusal in combat; cheat toggle "NPC fighting" (cheat level ≥ 3, key P). | A8 (done: [script surface](npc-ai-script-surface.md)) |
| S22 | Non-combat routine | 0x4ACBB0, 0x4ACD90, 0x4ACDB0, 0x4AD060, 0x4AD0B0, 0x4AD140, 0x4AD1B0, 0x4AEB10, 0x4AEB70, 0x4AECA0, 0x4AED80, 0x4AEE50 | Waypoints, standpoints, beds, portals and containers: FR-AI-001/003, out of this plan except where combat interrupts them. | — |

## First-pass anomalies (to confirm, not adopted)

**Original check (ARC-RES-048):** P1, P2 and P3 are reconstruction slips (P3 tests the redirect target's intelligence, not the candidate's), P4–P7 are the original's behaviour. The verdicts in the [verification ledger](ledger/npc-ai.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024) supersede the earlier readings below where they differ.

- **P1** The item search keeps the candidate with the **largest** distance
  although it tracks a "nearest" variable (items (0x4A8E70, 0x4A8F90)).
  Settled by ARC-RES-059: a reconstruction slip, the original keeps the nearest
  ([support](npc-ai-support.md#6-items-on-the-ground)).
- **P2** The party-level sum adds the leader's level once per follower instead
  of each follower's level, which skews the "outlevelled" flee test
  (party (0x4AE020, 0x4AE0A0)). Settled by ARC-RES-058: a reconstruction slip,
  the original sums each member's own level ([morale](npc-ai-morale.md#5-anomalies-and-decisions)).
- **P3** The redirect filter compares the intelligence of the threshold number
  instead of the candidate's (redirect (0x4A94C0, 0x4A9530, 0x4A9560,
  0x4A9650)). Settled by ARC-RES-057: the original tests the spell target's
  intelligence (reconstruction slip confirmed); that test is itself recorded as
  a bug of the original (by inference), with an Original switch
  `RedirectTestsTargetIntelligence` (owner decision 2026-10-20;
  [hostility](npc-ai-hostility.md#8-anomalies-and-decisions)).
- **P4** The heal step tests an NPC-flag constant (no-attack) against the
  critter-flag field, where the same bit means "mechanical"; the effect is
  that mechanical critters are never healed by AI (heal (0x4A8940, 0x4A8AA0)).
  Settled by ARC-RES-059: the original tests the mechanical bit; its behaviour,
  kept ([support](npc-ai-support.md#1-the-heal-ladder)).
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
  Settled by ARC-RES-059: the original's behaviour (a skip threshold), kept; the
  file's label stays a data question ([support](npc-ai-support.md#2-spell-and-item-choice-in-a-fight)).

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
| A2 | ARC-RES-056 | Awareness: S6–S9. See/hear formulas, loudness, line of fire, witnesses. | Pure-function specs with worked numbers; closes combat O4: [awareness](npc-ai-awareness.md) (done). |
| A3 | ARC-RES-057 | Hostility and targeting: S10–S13. | KOS/protect/upset decision tables, target scan order, enemy-list lifecycle: [hostility](npc-ai-hostility.md) (done). |
| A4 | ARC-RES-058 | Morale: S14–S15. | Flee/surrender/back-off rules, P2 and P6 settled: [morale](npc-ai-morale.md) (done). |
| A5 | ARC-RES-059 | Support and magic: S16–S18. | Heal ladder, ten action lists, eligibility codes, item search (P1, P4, P7): [support](npc-ai-support.md) (done). |
| A6 | ARC-RES-066 | Execution: S19 and the animation attack, approach, flee and run goals. | How an intention becomes movement and attacks in both modes; closes companion O5: [execution](npc-ai-execution.md) (done). |
| A7 | ARC-RES-067 | Followers in combat and orders: S20. | Order effects, follower join rules, post-fight cleanup: [followers](npc-ai-followers.md) (done). |
| A8 | ARC-RES-070 | Script and dialog surface: S21. | Every SAP event and SCR action that reads or writes AI state, and the dialog paths: [script surface](npc-ai-script-surface.md) (done). |
| A9 | in A0 (ARC-RES-041) | Data census: `ai_params.mes`/`gameai.mes` rows, spell `AI_*` fields, item AI action field, KOS/no-flee/guard/AI-row of the Crash Site and cave NPCs and creatures. | Local aggregates only, like ARC-RES-028 L3; picks the first real fight: [data census](combat-data-census.md#7-first-fight-candidates-plan-slice-i10). |

A0 comes first (D7), then A1; A2–A5 can run in parallel after it, with A5 reusing the
line-of-fire and protect definitions of A2 and A3; A6–A8 after A3.
A9 is part of A0.

### Implementation slices (proposals, after the matching research)

| Slice | Needs | Result |
| --- | --- | --- |
| I1 AI parameters | A1, A0 | Data reader for both files and a typed 17-field record with names from A1. Done: reader ARC-DATA-033, typed `AiParameterRow` and `Lookup` ARC-WORLD-077 ([AI parameters](../world/ai-parameters.md)). |
| I2 Awareness | A2, I1, ARC-SKILLS-003 (prowling margin) | Pure see/hear/line-of-fire functions over explicit inputs; synthetic matrix (FR-AI-002 acceptance). Done: ARC-WORLD-078 ([AI awareness](../world/ai-awareness.md)): perception distance, facing, sight, hearing, sound cost and the clear-shot test; no AI parameter slot is read. |
| I3 Danger state | A1, I1 | Session-owned per-NPC state, flags, focus, enemy list, snapshot schema version, script hook points as typed events. Done: ARC-WORLD-079 ([danger state](../world/ai-danger-state.md)): state read and setter, focus, fleeing-from, who-hit-me-last, enemy list, target lock, allow attacking, snapshot schema 38, enter/exit combat hooks, CL4 and FO14; peace and stop attacking moved to I4. |
| I4 Hostility and targets | A3, I2, I3 | KOS/protect/upset with reason codes, scan and choice with injected rolls; the attack event's callers include the script and DLG attacks of [A8](npc-ai-script-surface.md#3-actions) and pick pocket's typed detection result (thief, victim, steal or plant, reason) of ARC-SKILLS-010 (skills plan task S15; ledger PP1–PP8, not yet merged). Done: ARC-WORLD-080 ([hostility and targets](../world/ai-hostility.md)): validity, kill-on-sight, protect, upset, the scan, choose target, the enemy-list pick, the danger update's decision, the reaction to an attack, the attack event as data with its session writes, the noise squares and the witness rule, peace and stop attacking, HS9, AW16, SP16, FO7 and FO8; the script, dialog and pick-pocket callers of the attack event are their slices'. |
| I5 Morale | A4, I4 | Flee/surrender/back-off decisions and their state transitions. Done: ARC-WORLD-082 ([morale](../world/ai-morale.md)): should-flee with reason codes and the party sums, the weighing, the flee-spell step, perform fleeing's surrender and flee goal, perform surrender, stop fleeing, the back-off start and run decision and the kill back-off; session flee-spell, perform-fleeing, stop-fleeing, back-off and kill-notification writes; the SpellFlee bit (snapshot schema 40); MR3, MR7 and MR9; execution of the flee goal and the back-off run stays I7's. |
| I6 Scheduler | A1, I3 | AI time event on the session clock, 30-cell activity, turn-based subturn entry. Done: ARC-WORLD-084 ([AI scheduler](../world/ai-scheduler.md)): the tick (0x4AD200) with its delay, wake-up jitter, near test, generator branch, far path and follower catch-up; the `ai.tick` session event (saved, rebuilt on load under OQ2's Original setting, removed by a map change); the pass (0x4A84F0) with the gate (0x4A8570) in the original's order, CL1, CL2 and the CL5 switch, and the pipeline's stages as host ports; the subturn hook (0x4B7010); the CheckWeapon bit (snapshot schema 42). Hosts and stages stay I7, I8 and I10's. |
| I7 Melee and ranged executor | A6, combat slices, I4 | Intent to route planner and attack resolver; real-time and turn-based. Done: ARC-WORLD-086 ([AI executor](../world/ai-executor.md)): perform's dispatch (0x4AC180) with the back-off run, the attack, flee, follow and spread-out starters and the start gate (0x4348E0); the goal machine (0x423560: 8-goal stack, typed steps, 100-node limit, the end step) with the attack, attempt attack, combat approach, melee and ranged walks (EX9, EX20), flee and move away (NO_FLEE, EX12), run to tile, follow, walk and run near and the real-time pause; the round's checks, consumes and pending cost (EX6); set no-flee in the session (snapshot schema 45); EX21–EX23 read. The host and its world (planners, walker, animations, hit step) are I10's; casting, item, skill, throw and pickup goals I8's; EX15 the ranged-combat slice's. |
| I8 Support and magic | A5, magic domain | Heal ladder, lists, eligibility, items, grenades. Done (the decision half, owner decision 2026-10-28): ARC-WORLD-088 ([AI support](../world/ai-support.md)): the global lists with the runtime's quicksort (U1 closed, oracle F3), a critter's list build, the Original setting's list cache (SP11), the chooser (SP18 both fixed), the eligibility refusal codes 1..8, the heal ladder (P4, SP17), the spell choice (P7), the item search (P1), the grenade (SP15) and the AI redirect (P3; no installed spell sets `MinIQ:`, census `--ai-redirect-census`); the look-for bits in the session (snapshot schema 47) and the redirect's writes; SP19–SP21 read. The goals these choices start are slice I8b. |
| I8b Support goals | I7, I8, magic cast host | The casting goals (26, 82) through the magic cast host, the item-use (39), skill (33), throw (37) and pickup (15, 16) goals and their starters, perform's cast, item and skill actions and the immediate item use (0x462CC0, EX4); split from I8 by the owner on 2026-10-28. Done: ARC-WORLD-089 ([AI action goals](../world/ai-action-goals.md)): the goals read node by node through the goal-type table (ledger AG1–AG16), the five starters, perform's cast through the magic domain, the grenade's and a thrown weapon's throw, the item use with its action points; the attack goal's pop for a gone target (AG2) and the walk near's kept gait (AG16) fixed in the I7 code; no new switch, no session state. |
| I9 Followers in combat | A7, companion follow slices | Join/complain rules, orders, cleanup. Done: ARC-WORLD-091 ([AI followers](../world/ai-followers.md)): the follower rule (0x4A9C00) and tell followers (0x4A9B80), the upset line ranges (FO18, read by this task), the combat orders attack, back off and curse through ARC-WORLD-046's order entry with the ordered attack event, the lock or the complaint's −5, stop attacking and the curse's leave, insult (FO19) and attack event for the host, one AI pass per reached follower after its order, the combat taunt and near-death rules (FO16, FO17 fixed in both settings) and the session's `ApplyFollowerRule`; the cleanup rows were already in I3, I4 and I6; no switch, no session state. |
| I10 First fight | I1–I7, A0 | One Crash Site fight in the AREA viewer with an AI overlay (state, target, last reason). Part 1 done: ARC-WORLD-092 ([AI viewer host](../world/ai-host.md)): the session's NPC melee hit step (monster table, gauntlets, weapons; XP9, XP10; the hero's critical-miss fate), the viewer's AI host (ticks, gate host, danger and perform stages, one goal run per NPC on the world clock, the walker with moved NPC bodies, the attack with combat mode per AH2, the attack event with witnesses and the PC victim's response, the overlay) in real time; exe AH1–AH4. Part 2 done: ARC-WORLD-095 (the turn-based round: the subturn hook at the subturn start and after each run, round costs through the session, the completion check, the round start's and turn set's interrupts, the pending cost's reset; exe TH1–TH6). Part 3, first slice done: ARC-WORLD-099 (the round's tail: the end-combat script asked at an NPC's subturn end, a turn set's interrupt of the last run running the completion check, the fidget after a done run; exe TH7–TH10). ARC-WORLD-102 (the casting host: the support rules' spell half, the danger stage's spell choice, perform's cast and the casting goals through the map's cast host; exe AC1–AC4). Left: the other action goals' host calls (item, skill, throw, pickup, queued cast, spell missile, goal 40), followers in combat (I9's host part), NPC ranged attacks. |
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
when the magic slices settle them (the owner decided the magic rows on
2026-10-20; each lands with the slice its row names).

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
| A11 (ARC-COMBAT-003) | Original 1.0.7.4: the stalled-actor check records a new actor without its action points, so its second check compares with the previous actor's last value (0 after a round start) and may force a critter that spent to stop. | The new actor's own action points are the reference. | A stale reference from another critter; fixed under D2 by ARC-COMBAT-003 ([ledger TB6](ledger/combat.md#turn-based-round-arc-combat-003)). | `StallCheckKeepsPreviousActionPoints` (Original setting) |
| A12 (ARC-COMBAT-003) | Original 1.0.7.4: an overexertion that knocks the PC out passes the turn on inside its damage step, then sets the action points, by now the next participant's, to 0. | The next participant keeps the action points its subturn granted. | The zero was meant for the overexerting PC; fixed under D2 by ARC-COMBAT-003 ([ledger TB9](ledger/combat.md#turn-based-round-arc-combat-003)). | `OverexertionKnockoutClearsNextActionPoints` (Original setting) |
| Reputation A2 (ARC-RES-028) | Original 1.0.7.4: a deliberate hit by the PC on its own follower adds AI parameter 9 to the follower's reaction, although the field describes how much the hit lowers it. | Lowered by parameter 9. | Owner decision 2026-10-17 (PR #184); implemented by ARC-WORLD-026 in `ReactionRules.PcHit`. | `FollowerHitRaisesReaction` (Original setting) |
| LM14 (ARC-RES-040) | The melee damage scaling helper always returns 100, so it changes nothing. | Kept: no scaling. | The intended factor is unknown, so there is no confirmed slip to fix. ARC-COMBAT-001. | none |
| P5 (ARC-RES-039) | Original 1.0.7.4: in combat mode every pass that reaches the gate's combat test runs the start-combat script, in real time after the end-combat script; the end-combat script also runs at the end of the NPC's subturn ([core loop](npc-ai-core-loop.md#3-one-pass)). | Kept: per-pass hooks in both settings. | The original's design, which scripts are written against; nothing marks it as a slip ([ledger P5](ledger/npc-ai.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024), re-read by ARC-RES-054). Proposed by ARC-RES-054. | none |
| LM3 (ARC-RES-040) | Original 1.0.7.4: AI rows fill by position from the first entry's number, and an NPC's row is copied without a bounds check. | Rows by position as in the original; a row outside the table is refused in both settings (no out-of-bounds read, owner rule). | A latent bug of the original; the installed tables have no gap, so positional and by-number filling agree on shipped data ([census](combat-data-census.md#2-ai-parameter-rows)). Implemented by ARC-DATA-033 ([AI parameter table](../world/ai-parameters.md)); confirmed by ARC-RES-054. | none |
| LM4 (ARC-RES-040) | Original 1.0.7.4: the module unload of the AI rows does nothing, so rows 100..149 survive into the next module. | Not reachable: Steamwoven builds the module rows with each module's content layer and models no module change inside a session. | Load-map policy 1. Proposed by ARC-RES-054. | none |
| CL1 (ARC-RES-054) | Reconstruction only: a still-stunned NPC passes the gate and acts. The original ends every pass while the NPC is stunned, also the pass that clears the flag ([ledger CL1](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054)). | As the original. | Reconstruction slip. Implemented by ARC-WORLD-084 (slice I6). | none: both settings follow the original |
| CL2 (ARC-RES-054) | Reconstruction only: after the full re-wield only "check wield" is cleared, so the next pass re-wields the weapon. The original clears "check weapon" too ([ledger CL2](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054)). | As the original. | Reconstruction slip. Implemented by ARC-WORLD-084 (slice I6). | none: both settings follow the original |
| CL4 (ARC-RES-054) | Original 1.0.7.4: a surrender leaves combat mode and then writes back critter flags read before, so the combat-mode bit is set again and the surrendered NPC still counts as in combat mode ([ledger CL4](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054)). | A surrender leaves the combat-mode bit cleared. | A stale write; the setter clearly means to leave combat mode. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-079 (slice I3). | `SurrenderKeepsCombatModeFlag` (Original setting) |
| CL5 (ARC-RES-054) | Original 1.0.7.4: the gate's blind, crippled and stun writes use critter flags read before the combat scripts of the same pass, undoing a script's flag change ([ledger CL5](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054)). | Each write changes only its own bits of the current flags. | A stale write (latent). Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-084 (slice I6). | `GateRecoveryWritesStaleFlags` (Original setting) |
| P2 (ARC-RES-039) | Reconstruction only: the party-level sum of the flee test adds the leader's level once per follower. The original adds each member's own level, followers of followers included ([ledger MR2](ledger/npc-ai.md#npc-ai-morale-arc-res-058)). | As the original. | Reconstruction slip, confirmed by two readers. Lands with slice I5. Implemented by ARC-WORLD-082 (slice I5). | none: both settings follow the original |
| P6 (ARC-RES-039) | Original 1.0.7.4: a fleeing NPC's next pass without an action requests SURRENDER and starts the flee goal; the dialog start, the dialog script point, the greeting, "can speak" and leaving combat mode treat SURRENDER as a lasting "out of the fight" state ([morale](npc-ai-morale.md#3-a-surrendered-npc)). | Kept in both settings. | The original's design, which the interface and scripts are written against ([ledger MR8, MR11](ledger/npc-ai.md#npc-ai-morale-arc-res-058)). Proposed by ARC-RES-058. | none |
| MR3 (ARC-RES-058) | Original 1.0.7.4: a leaderless NPC counts as a party of one for the flee test, its own followers ignored, while each of its followers counts the whole group. | A leaderless NPC counts its own followers like a PC. | Looks unintended: the same group is sized differently by its leader and its members. Proposed by ARC-RES-058; lands with slice I5. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-082 (slice I5). | `NpcLeaderPartyCountsAlone` (Original setting) |
| MR7 (ARC-RES-058) | Original 1.0.7.4: the "flee spell used" flag (SPELL_FLEE) is cleared only by the flee-spell step, so a flight left before that step makes the next flight skip its flee spell or item once. | SPELL_FLEE is cleared when the NPC leaves FLEE. | A stale flag from an earlier flight (latent). Proposed by ARC-RES-058; lands with slices I5/I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-082 (slice I5). | `StaleSpellFleeSkipsFleeSpell` (Original setting) |
| MR9 (ARC-RES-058) | Original 1.0.7.4: perform fleeing starts the flee goal even when the setter refused the surrender (invulnerable, or a leader with extraordinary charisma). | The flee goal starts only when SURRENDER was applied. | An ignored result (latent: needs a change during a flight, for example invulnerability, a new leader or the leader's charisma; no script action writes FLEEING; a spell component with undead and FLEEING would (SU17, MT13); script actions 57 (invulnerability) and 21 (a new leader) reach it, ARC-RES-070). Proposed by ARC-RES-058; lands with slice I5. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-082 (slice I5). | `FleeGoalAfterRefusedSurrender` (Original setting) |
| MR13 (ARC-RES-058) | Original 1.0.7.4: mind control sets NO_FLEE and its end does not clear it, so a formerly controlled NPC never flees again. | Steamwoven restores NO_FLEE to its value before the control (one remembered bit, stored in both settings); the Original setting leaves it set. | The flag outlives the control and cannot be told from data, but the original also sets NO_FLEE for good by design (the "move away from object" goal the flee goal runs, [ledger MR12](ledger/npc-ai.md#npc-ai-morale-arc-res-058)), so the end may be intended. Proposed by ARC-RES-058; lands with the magic slice for mind control. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. | `MindControlLeavesNoFlee` (Original setting) |
| MR15 (ARC-RES-058) | Original 1.0.7.4: when no straight "away" line is clear, the back-off path retries a line that already failed, so the back-off ends. | The back-off ends when no straight line is clear, as in the original. | The retry changes nothing; no visible difference to keep. Proposed by ARC-RES-058; lands with slices I5/I7. The decision is implemented by ARC-WORLD-082 (slice I5); the "away" path and its failure are I7's. | none |
| P3 (ARC-RES-039) | Original 1.0.7.4: the AI redirect compares the spell target's intelligence (both record handles are the target) with the component's `MinIQ:` threshold, so it redirects every candidate or none; no candidate's intelligence is read ([ledger HS17](ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057)). The earlier reading (the threshold number's intelligence) was a reconstruction slip. | Each candidate's intelligence is compared; a candidate above the threshold is not redirected. | Original bug, by inference: a per-candidate filter that tests one fixed critter looks unintended (the field name fits neither reading). Proposed by ARC-RES-057; which spells set `MinIQ:` is a data question. Lands with slice I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-088 (slice I8); the installed data sets no `MinIQ:` (one redirect spell, 183, mask animal), so the switch changes nothing there. | `RedirectTestsTargetIntelligence` (Original setting) |
| HS9 (ARC-RES-057) | Original 1.0.7.4: upset attacking returns PARTY when the target's leader equals the leader argument also when both are null; find target and kill-on-sight pass the NPC's own leader, so for a leaderless NPC the scan's ally help never targets a PC or a leaderless NPC, GUARD never targets a leaderless NPC aggressor, and the guard-versus-monster clauses are dead for leaderless guards ([ledger HS9](ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057)). | PARTY only for a non-null leader argument. | Original bug: validity, protect, faction-same and upset's own GOOD row guard against a null leader; the guard clauses would be dead code. Proposed by ARC-RES-057 (visible change: townspeople and guards join by their own scan, not only as witnesses). Lands with slice I4. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-080 (slice I4). | `UpsetPartyMatchesMissingLeader` (Original setting) |
| HS19 (ARC-RES-057) | Original 1.0.7.4: an accidental hit also adds the attacker's team to the victim's enemy list, so the victim's reaction to the PC reads 0 until peace, beside the explicit −10. | Kept in both settings. | Intent unclear, not a confirmed slip; a phase 2 patch may revisit it. | none |
| HS20 (ARC-RES-057) | Original 1.0.7.4: the scan keeps the first 50 critters of the rectangle query before sorting by distance. | Kept in both settings. | The array size; more than 50 critters in a 21×21 box is not expected. | none |
| HS21 (ARC-RES-057) | Original 1.0.7.4: validity's unconscious rule runs a nested scan that does nothing inside a scan, so an unconscious candidate is valid during a scan and otherwise only while nothing else is found. | Kept in both settings. | The original's behaviour; the scan orders unconscious critters last. | none |
| HS22 (ARC-RES-057) | Original 1.0.7.4: the reaction to an attack calls the float callback in its surrender branch without a null test (latent: always set in the game). | Not reachable: Steamwoven has no unset callback. | Never reproduce a crash (owner rule). | none |
| AW13 (ARC-RES-056) | Original 1.0.7.4: a corner step of a sound or missile line sums both L routes around the corner, so the corner tile's soundproofing (+16) and cover (one see-through cover object +40, no clear shot) count twice; a blocker on either side tile blocks ([ledger AW13](ledger/npc-ai.md#npc-ai-awareness-arc-res-056)). | The corner tile's soundproofing and objects count once; side-tile blocking kept as the original's design. | Reuse of the walking step function, where only a blocker matters: a double count, as A6 and A7. Proposed by ARC-RES-056; implemented for the line of fire by ARC-COMBAT-012 and for the sound line by ARC-WORLD-078 (slice I2). Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. | `CornerStepCountsBothRoutes` (Original setting) |
| AW14 (ARC-RES-056) | Original 1.0.7.4: the last step of a line also skips objects on the two side tiles of a corner step. | Only the target tile's objects are skipped. | Same reuse as AW13; follows AW13. Proposed by ARC-RES-056. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Owner decision 2026-10-28 (ARC-COMBAT-015, option 1): the hero's attack line to a target next to the attacker is read as the original in both settings, so an attack across a corner step is not refused or redirected by an object on a side tile (the screen-left diagonal is two edge steps and is stopped in both settings). | `CornerStepCountsBothRoutes` (Original setting, with AW13) |
| AW15 (ARC-RES-056) | Original 1.0.7.4: the line of fire asks to ignore critters, but under the missile flag a living critter still blocks unless shoot-through. | Kept: critters block lines of fire and sight. | Likely intended (no shooting through bodies). Proposed by ARC-RES-056. | none |
| AW16 (ARC-RES-056) | Original 1.0.7.4: an NPC inside both alert squares of an attack is handled twice and repeats its attacked reaction and target-choice roll. | Each NPC is handled once per attack (the attacker's square first). | One event processed twice, the A7 pattern. Proposed by ARC-RES-056; lands with the attack owner (slice I4). Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-080 (slice I4). | `WitnessHandledTwice` (Original setting) |
| AW17 (ARC-RES-056) | Original 1.0.7.4: a line longer than 100 ten-pixel samples (about 12 to 25 tiles by screen direction) fails with cost 100 and no blocker (attack difficulty +100, no magick line of sight, AI refusal 5, sound unheard). | Kept for the isometric view; the editor's top-down view is not modelled. | The original's limit; no crash or out-of-bounds access. For the owner: beyond about 12 tiles along the screen-horizontal diagonal every ranged shot gets +100 difficulty. Proposed by ARC-RES-056. | none |
| AW18 (ARC-RES-056) | Original 1.0.7.4: the line walk adds the observer's pixel offset to a line built between tile centres. | Kept: as the original. | Matters only for a moving observer. Proposed by ARC-RES-056. | none |
| AW19 (ARC-RES-056) | Original 1.0.7.4: a sleeping NPC inside an alert square is woken before any perception test. | Kept: as the original. | Plausible design (a fight nearby wakes sleepers). Proposed by ARC-RES-056. | none |
| SR12 (ARC-MAGIC-001) | Original 1.0.7.4: a spell record pair without `(` exits the process; a `Resist:` stat name that matches none of the 23 stat keys reaches the null entries of the 28-entry key table and crashes. | Fixed in both reader modes: the spell data is refused with a typed reason (`MissingParenthesis`, `UnknownName`) naming file, message and token. | No-crash rule; no installed record reaches it. Owner accepted 2026-10-20. | none (reader refusal) |
| SR13 (ARC-MAGIC-001) | Original 1.0.7.4: a flag list name that matches no entry (an empty one included) exits; a separator as the last character of the value crashes; a space-separated name followed later by `\|` is skipped (`A B\|C` reads A and C). | Exit and crash refused in both reader modes (`UnknownFlag`, `TrailingSeparator`); the skip is reproduced in like-original mode and refused in strict mode (`AmbiguousFlagList`). | No-crash rule; the skip is harmless and loses only data; no installed list reaches either. Owner accepted 2026-10-20. | none (reader modes) |
| SR14 (ARC-MAGIC-001) | Original 1.0.7.4: a byte above 0x7F met by the parser's white-space skip is classed by memory before the character table, partly run-time library data. | Refused in both reader modes (`NonAsciiByte`) on every parsed line (line +0, +1..+4, component lines). | The original's result is data-dependent; no installed line has such a byte. Owner accepted 2026-10-20. | none (reader refusal) |
| SC5 (ARC-MAGIC-002) | Original 1.0.7.4: a positional component value (no key) read after its line has ended dereferences a null cursor and crashes; an unknown action name or component type exits; a name of an ObjFlag field or a TraitIdx stat that matches nothing reaches a hole of its name table and crashes. | Fixed in both reader modes: the spell data is refused (`MissingValue`, `UnknownName`). | No-crash rule; no installed line reaches it. Owner decision 2026-10-20 (document every difference). | none (reader refusal) |
| SC11 (ARC-MAGIC-002) | Original 1.0.7.4: text after a component's last read value is never read: 40 Destroy and 3 Identify components carry a word, and record 221 repeats `Dmg_Flags:` so its second value (`Scaled`) is lost. | Read as the original in both rule modes; like-original reading lists the text, strict reading refuses the file. The record 221 data defect becomes data fix DF4 (row below), landing with MT12. | Owner's data-fix rule (ARC-DATA-022); DF4 decided 2026-10-21. | none (DF4 has its own row) |
| MG21–MG23 (ARC-MAGIC-002) | Original 1.0.7.4: `Tgt_Non_Party_Critters` lacks the critter bit (20 installed records), `Tgt_Obj_Radius` the object bit (24), `Tgt_Tile_Self` the tile bit (none). | The original in both modes: the target functions handle each bit on its own (ARC-MAGIC-007, [ledger TA6, TA10, TA11](ledger/spell-runs.md#spell-target-areas-arc-magic-007)). | Intent, not a bug. | none |
| MG1, MG2 (ARC-MAGIC-003) | Original 1.0.7.4 ([ledger PC1–PC3, PC10](ledger/magic.md#spell-cost-and-payment-arc-magic-003)): the pre-animation check reads the cast spell's cost or maintenance cost without mastery; the run-time cost halves rounding up for mastery, the UI and AI cost doubles for a dwarf then halves toward zero, the UI check ignores mastery. | Kept in both modes: implemented as the original (`SpellCostRules`; three functions checked in the function oracle). | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)). | none |
| MG20 (ARC-MAGIC-003) | Original 1.0.7.4 ([ledger PC6](ledger/magic.md#spell-cost-and-payment-arc-magic-003)): worn mana stores are drained (and their recharge scheduled) before the fatigue refusal, so a refused cast still costs stored mana. | Kept in both modes (`SpellPaymentRules.Pay`). | Owner decision 2026-10-20. | none |
| PC4 (ARC-MAGIC-003) | Original 1.0.7.4: the payment addresses the record of a spell number outside the table and reads it on its critter path (out of bounds). | Fixed in both modes: such a number is refused at once (`SpellOutOfRange`), also on the paths where the original succeeds without reading it, with no last cost kept. | No-crash rule; no caller passes one. | none (rule refusal) |
| DF4 (ARC-MAGIC-002 census) | Installed data: record 221 (line +8) repeats `Dmg_Flags:`, so the original never reads its second value and the component loses `Scaled`. | Decided: a data fix under the owner's data-fix rule (ARC-DATA-022, 2026-10-17: Steamwoven restores content the data loses, Original keeps the loss), with its ledger row and switch; it lands with MT12, where `Scaled` takes effect. | Coordinator note 2026-10-21 applying the owner's data-fix rule. | `DataFixProfile.RepeatedDamageFlagsLost` (ARC-MAGIC-012; the restored flag changes no amount: record 221's component is a full death hit with a 0–0 range) |
| P1 (ARC-RES-039) | Reconstruction only: the item search keeps the farthest candidate. The original keeps the nearest, the first on a tie ([ledger SP12](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059)). | As the original. | Reconstruction slip, confirmed by two readers. Lands with slice I8. | none: both settings follow the original |
| P4 (ARC-RES-039) | Original 1.0.7.4: the heal helper skips a target with the critter flag MECHANICAL, so AI never heals or resurrects mechanical critters; the heal skill check also refuses mechanical and undead targets ([support](npc-ai-support.md#1-the-heal-ladder)). | Kept in both settings. | The original's behaviour; the earlier reading's "no-attack" name is the same bit of another flag set. Proposed by ARC-RES-059. | none |
| P7 (ARC-RES-039) | Original 1.0.7.4: the defensive list is tried when parameter 13 < d100, a `100 − parameter 13` % chance ([support](npc-ai-support.md#2-spell-and-item-choice-in-a-fight)). | Kept in both settings; slot 13 is a skip threshold. | The original's behaviour, confirmed by two readers; the installed 0 in 12 rows reads naturally only as a threshold (inference); the file's label stays a data question. Proposed by ARC-RES-059. | none |
| SP8 (ARC-RES-059) | Original 1.0.7.4: a healing-college item spell skips the fail-chance test also when the user's fatigue is at least 80 %; a cast spell skips it only for a target at or below 30 % HP. | Kept in both settings. | Intent unclear, nothing marks it as a slip. Proposed by ARC-RES-059. | none |
| SP11 (ARC-RES-059) | Original 1.0.7.4: the 40-slot AI list cache stores only the list requested when a critter's slot is created and keeps it until an inventory change, an exhausted listed item, a reset or round-robin reuse; every other list is rebuilt on each request (and leaked) ([ledger SP11](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059)). | Each list is built fresh on every request. | The hit path writes into the caller's copy instead of the slot (inference: a cache meant for all ten lists); the frozen list keeps items-versus-spells and known spells from an old state. Proposed by ARC-RES-059; the cache is transient, never saved. Lands with slice I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-088 (slice I8). | `AiListCacheKeepsFirstList` (Original setting) |
| SP15 (ARC-RES-059) | Original 1.0.7.4: the grenade's friendly-fire scan covers NPCs only, so a PC in the blast, a follower's PC leader included, never stops a throw. | PCs and NPCs are scanned with the same protect rule. | The protect rule handles PC leaders and PC reputation, which matter only if PCs are scanned (inference). Proposed by ARC-RES-059 (visible: followers stop throwing near their PC leader; an NPC whose faction the PC's reputation grants refuses to throw at that PC). Lands with slice I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-088 (slice I8). | `GrenadeIgnoresPcsInBlast` (Original setting) |
| SP16 (ARC-RES-059) | Original 1.0.7.4: after a spell choice in the danger update's enemy-list path, the setter receives the pass record's target, which a summon or self-aimed defensive spell has replaced with the NPC itself, so the NPC becomes its own focus for one pass; the other three paths pass the found target. | The setter receives the enemy-list pick. | The paths differ only here and a critter is never a valid focus of itself. Proposed by ARC-RES-059. Lands with slices I4/I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-080 (slice I4). | `EnemyListFocusTakesSpellTarget` (Original setting) |
| SP17 (ARC-RES-059) | Original 1.0.7.4: the heal ladder's fatigue-recovery rung tests the healer's fatigue but is aimed at whichever target the helper runs for, leader and followers included. | The rung is tried only when the target is the healer. | Reads as "restore your own fatigue" (inference); reached for others only when the self attempt was refused. Proposed by ARC-RES-059 (the alternative fix, testing the target's fatigue, adds new behaviour). Lands with slice I8. Owner decision 2026-10-20: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-088 (slice I8). | `HealFatigueRungForOthers` (Original setting) |
| SP18 (ARC-RES-059) | Original 1.0.7.4: in mode 4 (defensive list) the chooser reads the second defensive value for an item AI-action entry (spell −1) too, indexing the spell table with −1, so 4 bytes before the table decide the item's target ([ledger SP18](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059)). | Both fixed: out-of-bounds read; a −1 entry in mode 4 targets the NPC itself, as a defensive self spell. | Latent: only an item with AI action 2 reaches it and the installed data has none; never reproduce an out-of-bounds read (owner rule). Proposed by ARC-RES-059. Lands with slice I8. Implemented by ARC-WORLD-088 (slice I8). | none |
| SP19 (ARC-WORLD-088) | Original 1.0.7.4: the list cache's hit path with the requested list empty builds the list into the request record, whose PC-leader field (read by the identification rule 0x4CCA30) neither the request init (0x4CC4F0) nor the cache sets ([ledger SP19](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059)). | Both settings use the critter's current PC leader. | Latent uninitialised read, reached only in the Original setting's cache, for an unidentified item that is neither food nor a scroll; never reproduce such a read (owner rule, as SP18 and SU8). Implemented by ARC-WORLD-088 (slice I8). | none |
| MG3 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): a maintenance tick with a resistance margin charges the plain cost and then the surcharged cost. | Only the surcharged cost. | Double charge. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); lands with MT9a. | `MagicAnomalyProfile.MaintenancePaysBoth` (ARC-MAGIC-009) |
| MG4 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the resistance-derived term is subtracted from the saving throw, against the manual's +1 per 10 %. | Added, as the manual says. | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); landed with MT6 (ARC-MAGIC-005). | `MagicAnomalyProfile.SaveSubtractsResistance` (Original setting) |
| MG5 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the tech-aligned target test compares a d100 with a negative value and never fires. | Kept in both settings. | The intended formula is unknown. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT7. | none |
| MG6 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the aptitude difference is taken as an absolute value and then tested for < 0, so duration and period scaling never applies. | Kept in both settings. | The intended signed scaling is unknown. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT9a. | none |
| MG7 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the Interrupt component builds an invocation without a source and interrupts nothing. | Kept in both modes (record only): the MT2 census (ARC-MAGIC-002) finds no installed record with an Interrupt component. | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)): fix only if a record uses it; none does. | none |
| MG9 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the summon list roll is 0..100 over weights summing to 100 or 120; 100 falls off to the first entry. | Roll 0..99. | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); lands with MT17. | named by MT17 (Original setting) |
| MG10 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): polymorph to an elemental assigns the critter and blit flag words, wiping the other flags. | The flags are added and removed bit by bit. | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); lands with MT18. | named by MT18 (Original setting) |
| MG11 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the maintained-run counter used by the AI returns 0. | Counts the caster's maintained runs. | Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); lands with MT9a, used by AI slice I8. | `MagicAnomalyProfile.MaintainedCountIsZero` (ARC-MAGIC-009) |
| MG12 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the maintain bar's slot test lets slot −1 through and `INT / 4` is not capped at five slots (latent). | Refuse when no slot is free; at most five slots, in both settings. | Never reproduce an out-of-bounds write (owner rule); MT20b. | none |
| MG15 (ARC-RES-043) | Original 1.0.7.4 ([ledger PT3](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011)): reflection charges the reflector a global "last cost", stale for a free cast, which paid nothing. | The reflected spell's own cost: what its stage asked in that pass (its BEGIN cost or maintenance; nothing for a free or technology cast). | Owner decision 2026-10-20; implemented by ARC-MAGIC-011. | `MagicAnomalyProfile.ReflectionPaysLastCost` |
| MG18 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the target name `Tgt_Obj_No_ST_Critter_Dead` lacks the critter bit its siblings carry. | The original in both modes: the area test runs the not-dead test for any object without the critter bit, so the value means "any object but dead ones" (ARC-MAGIC-007, [ledger TA6](ledger/spell-runs.md#spell-target-areas-arc-magic-007)); adding the bit would narrow Bolt of Lightning (record 33) to critters. | Intent, not a bug (owner decision 2026-10-20, "MT2 or MT8": classified in MT8). | none |
| MG24, MG26 (ARC-MAGIC-007) | Original 1.0.7.4 ([ledger](ledger/spell-runs.md#spell-target-areas-arc-magic-007)): the target list's helpers write past its 256 entries; a missing object's location is read through handle 0. | Fixed in both modes: the list stops at 256, a missing object has no location. | No-crash rule. | none |
| MG25 (ARC-MAGIC-007) | Original 1.0.7.4 ([ledger TA8](ledger/spell-runs.md#spell-target-areas-arc-magic-007)): the summoned target bit checks bit 0x4000 of the flags word the area test read last (the critter flags with the critter bit). | Steamwoven checks the spell flag "summoned"; no installed cast is affected. | Accepted rule for bugs of the original (owner decision 2026-10-20). | `MagicAnomalyProfile.SummonedTestReadsLastFlags` |
| MG27 (ARC-MAGIC-007) | Original 1.0.7.4 ([ledger TA9](ledger/spell-runs.md#spell-target-areas-arc-magic-007)): the tile-radius target test measures the target tile's distance to itself and never refuses a radius ≥ 0. | The original in both modes: the intended comparison is unknown and no installed cast would change. | Decided: the owner accepted the MG5 rule on 2026-10-20 (an original bug whose intended formula is unknown keeps the original), which covers MG27 (recorded by ARC-MAGIC-008). | none |
| MG19 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): one run flag means both "unresistable" and "final END scheduled", so an unresistable spell ends right after BEGIN. | Two flags; an unresistable spell keeps its duration and maintenance. | Reaches two technology records and the script action `cast_unresistable_spell`. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); lands with MT9a. | `MagicAnomalyProfile.UnresistableIsFinalEnd` (ARC-MAGIC-009) |
| MG28 (ARC-MAGIC-009) | Original 1.0.7.4 ([ledger RN1](ledger/spell-runs.md#spell-runs-arc-magic-009)): with 512 runs active a new cast quits the game. | Fixed in both modes: the cast is refused. | No-crash rule. | none |
| MG29 (ARC-MAGIC-009) | Original 1.0.7.4 ([ledger RN7](ledger/spell-runs.md#spell-runs-arc-magic-009)): the surcharged maintenance is paid by a free run, after a failed check, and (1 per period) by a timed spell with a resistance margin. | Steamwoven takes no surcharge in those three cases. | Decided: owner decision 2026-10-21 (fix); ARC-MAGIC-010. | `MagicAnomalyProfile.SurchargeAlways` |
| MG30 (ARC-MAGIC-010) | Original 1.0.7.4 ([ledger MG30](ledger/spell-runs.md#spell-run-processing-arc-magic-010)): the runs a starting run's cancels interrupt are refused by the re-entrancy guard and left without a tick or an END. | Processed right after the pass (ARC-MAGIC-011). | Owner decision 2026-10-21 10:13 (fix). | `MagicAnomalyProfile.ReEntryDropsRuns` |
| MG31 (ARC-MAGIC-010) | Original 1.0.7.4 ([ledger MG31](ledger/spell-runs.md#spell-run-processing-arc-magic-010)): interrupting a timed run with CALLBACK but no END_CALLBACK components does not end it (one technology record). | The original in both modes, no switch. | Owner decision 2026-10-21 10:13 (keep). | none |
| MG32 (ARC-MAGIC-010) | Original 1.0.7.4 ([ledger MG32](ledger/spell-runs.md#spell-run-processing-arc-magic-010)): an interrupt (or process) event outlives the run's free and can hit a new run in the reused slot. | A free removes every event of the run (ARC-MAGIC-011). | Owner decision 2026-10-21 10:13 (fix). | `MagicAnomalyProfile.FreeKeepsOtherEvents` |
| MG33 (ARC-MAGIC-011) | Original 1.0.7.4 ([ledger MG33](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011)): the cancels' environment loop compares the starting record's own fields (latent: no installed record has `Cancels_EnvSF:`). | The original in both modes, no switch. | Owner decision 2026-10-21 10:13 (keep). | none |
| MG34 (ARC-MAGIC-011) | Original 1.0.7.4 ([ledger MG34](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011)): a target the reflection swapped and the resistance skipped leaves the run's source swapped. | The source is restored for a resisted target too (ARC-MAGIC-013). | Owner decision 2026-10-21 10:13 (fix). | `MagicAnomalyProfile.ResistedReflectionKeepsSwap` |
| MG35 (ARC-MAGIC-012) | Original 1.0.7.4 ([ledger MG35](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012)): a PC's full or death hit takes the current hit points and then the difficulty share, so on hard the target lives. | Steamwoven leaves that amount alone (ARC-MAGIC-012). | Owner decision 2026-10-21 17:29 (fix). | `MagicAnomalyProfile.DifficultyScalesKillDamage` |
| MG36 (ARC-MAGIC-012) | Original 1.0.7.4 ([ledger MG36](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012)): a spell's damage step carries the caster's wielded weapon and its ignore-resistance, "uber" and damage-armour flags. | Steamwoven gives the spell no weapon (ARC-MAGIC-012). | Owner decision 2026-10-21 17:29 (fix). | `MagicAnomalyProfile.SpellDamageUsesCastersWeapon` |
| MG37 (ARC-MAGIC-012) | Original 1.0.7.4 ([ledger MG37](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012)): a margin takes at least 1 from an amount of 0, so a resisted stun-only spell heals a hit point. | Steamwoven takes nothing from 0 or less (ARC-MAGIC-012). | Owner decision 2026-10-21 17:29 (fix). | `MagicAnomalyProfile.MarginTakesFromNoDamage` |
| MG38 (ARC-MAGIC-012) | Original 1.0.7.4 ([ledger MG38](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012)): the acid's body-armour test compares the loop index with the slot, so the wearer always takes a third. | Steamwoven lets body armour spare the wearer (ARC-MAGIC-012). | Owner decision 2026-10-21 17:29 (fix). | `MagicAnomalyProfile.AcidIgnoresBodyArmour` |
| O9 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): resistance and the fail-chance predictor read the stored magic resistance: no armour, no effects, no 0..95 clamp. | As the original in both settings. | Reconstruction change, not adopted. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT6. | none |
| O10 (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the "target is the caster" pass comes before the anti-magic shell; END actions get no shell or reflection exception. | As the original in both settings. | Reconstruction change, not adopted. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT10. | none |
| Charm (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): charm on gives +30 and charm off −30, both through the PC's reaction effects; charm off has no animal test. | As the original in both settings. | Reconstruction change, not adopted. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT14. | none |
| Summon palette (ARC-RES-043) | Original 1.0.7.4 ([ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040)): the summon's palette applies to its current art only, so after a polymorph the prototype palette shows. | As the original in both settings. | Reconstruction change, not adopted. Owner decision 2026-10-20 ([magic plan](magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)); MT17. | none |
| EX6 (ARC-RES-066) | Original 1.0.7.4: a goal run that ends through an end step in a round charges an NPC the round's pending cost, the last action-point check's value: a step it already paid (or 0 at its subturn start), or after a failed check the unpaid step. The end step is also the normal end of the throw, item-use and pickup goals, so every grenade throw and item use after a walk pays the last step again ([ledger EX6](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066)). | Nothing is charged at the end step. | A stale value charged at an unrelated moment (inference), as the combat-core A6/A7 precedent; visible: in a round NPCs keep 1–2 more action points after throws, item uses and aborted actions that followed a walk. Proposed by ARC-RES-066; lands with slice I7. Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-086 (slice I7). | `EndStepRechargesPendingCost` (Original setting) |
| EX9 (ARC-RES-066) | Original 1.0.7.4: the ranged approach stops at a clear shot only when the attack has become impossible; otherwise it walks on, so a ranged attacker closes to the adjacent tile of a target that stands still and re-tests its shot only when the target changes tile ([ledger EX9](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066)). | The approach stops at the first tile where the target is within the weapon's range with a clear line (cost below 26, no foreign blocker). | Both readers read the test as inverted (inference); the fix needs the range test the callback lacks. Visible: archers and gunmen, NPCs and the PC, shoot from range. Proposed by ARC-RES-066; lands with slice I7 (phase 2 M3 builds on it). Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. | `RangedApproachSkipsClearShot` (Original setting) |
| EX15 (ARC-RES-066) | Original 1.0.7.4: the attack resolution decides "accidental" and notifies the aimed target before its ranged branch replaces the struck object with a line-of-fire blocker, so a shot without a missile that hits a blocker notifies the aimed target, not the blocker; a missile notifies what it strikes, marked accidental ([ledger EX14, EX15](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066)). | The struck object is notified, marked accidental when it is not the aimed target, as the missile path does. | The resolution's struck-versus-aimed test can matter only after the retarget (inference). Visible: a bystander hit by such a shot reacts (accidentally) and the aimed target no longer turns hostile from it. Proposed by ARC-RES-066; lands with slice I7 and the ranged-combat slice (FR-COMBAT-007). Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. | `RangedBlockerHitNotifiesAimedTarget` (Original setting) |
| EX12 (ARC-RES-066) | Original 1.0.7.4: the spread-out order's goal pushes the move-away goal away from the leader without the flight's flag 0x400, so a bent escape path (node 5) or a failed one (node 7) sets NO_FLEE, and a follower spread out in a cramped place never flees again ([ledger EX12](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066)). | A move-away run started by spreading out sets no NO_FLEE; flights keep node 7. | Flight logic reused for spreading out marks a follower that was never in danger (inference). Visible: spread-out followers keep their morale. The order belongs to the companions thread (ARC-WORLD-046). Proposed by ARC-RES-066; lands with slices I7/I9. Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-086 (slice I7). | `SpreadOutSetsNoFlee` (Original setting) |
| EX13, EX20 (ARC-RES-066) | Original 1.0.7.4: dead goal nodes, the back-off-from starter and the resolution's accidental test (EX13); a blocked step ends a ranged approach's whole run but only the segment of a melee approach (EX20). | Kept in both settings. | No effect (EX13); intent unclear, and the next AI pass or subturn hook decides again (EX20). Proposed by ARC-RES-066. | none |
| FO7 (ARC-RES-067) | Original 1.0.7.4: the no-attack flag is set and cleared only while the NPC has a PC leader and no leave clears it, so an NPC that leaves while stopped (dismissed, cursed, jilted, wait expired) never fights again until it rejoins a PC that enters combat mode or orders it to attack ([ledger FO7](ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067)). | Leaving its leader clears the no-attack flag. | A stale party order that silently disables fighting, and keeps a cursed follower from fighting back (inference). Visible: a follower told to stop and then dismissed, cursed or jilted fights normally afterwards. Proposed by ARC-RES-067; lands with slice I9. Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-080 (slice I4). | `NoAttackSurvivesLeaving` (Original setting) |
| FO8 (ARC-RES-067) | Original 1.0.7.4: the danger update chooses a spell or item for the found target before asking the setter for COMBAT_FOCUS and keeps it when setter rule 4 refuses (no-attack flag, or the global "NPC fighting" switch off), so a stopped follower still casts or uses items on enemies it scans ([ledger FO8](ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067)). | The action chosen for that target is dropped when rule 4 turns the request into NONE. | An ignored result: the refusal means "do not fight" (inference). Visible: stopped followers no longer cast or use attack items; with the switch off NPCs no longer cast at targets (the switch is stage A8's). Proposed by ARC-RES-067; lands with slices I3/I9. Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. Implemented by ARC-WORLD-080 (slice I4). | `NoAttackKeepsChosenAction` (Original setting) |
| FO14 (ARC-RES-067) | Original 1.0.7.4: a target lock survives a flight that ends in NONE; the setter then returns COMBAT_FOCUS for the scan's target without any write, and perform attacks it through combat perform with no FIGHTING flag, focus write or combat script ([ledger FO14](ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067)). | The setter clears the target lock whenever it applies a state other than COMBAT_FOCUS. | Stale state: the lock meant for the ordered target outlives the fight (inference). Visible: a follower ordered to attack that flees and calms down returns to normal targeting. Proposed by ARC-RES-067; implemented in the setter by ARC-WORLD-079 (slice I3). Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. | `LockedNoneStateFights` (Original setting) |
| FO9, FO10, FO13 (ARC-RES-067) | Original 1.0.7.4: an attack order lifts a stop even when its event does nothing (FO9); the follower complaint's −5 is skipped when the line callback is unset, which never happens (FO10); a flight never demaintains (FO13). | Kept in both settings. | The original's behaviour. Proposed by ARC-RES-067. | none |
| FO16, FO17 (ARC-RES-067) | Original 1.0.7.4: the near-death line calls the line callback with no null test (FO16, as HS22); the taunt passes a drawn follower-array entry to the activity test with no null test (FO17). Both latent. | Both fixed: null call (FO16) and null object (FO17); a null entry gives no taunt. | Never reproduce a crash (owner rule). Proposed by ARC-RES-067. | none |
| SU1 (ARC-RES-070) | Original 1.0.7.4: the cheat key P toggles the global "NPC fighting" switch only at cheat level 3 or more, and the retail build fixes the cheat level at 0, so the switch (read by setter rule 4 and the spell order) is always on ([ledger SU1](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070)). | The switch is always on in both settings; no rule-mode switch. | The original's state; plan phase 2 M5 may expose it as a player option. Proposed by ARC-RES-070. | none |
| SU8 (ARC-RES-070) | Original 1.0.7.4: a single-object script operand (actions 21, 22, 67, 109 and others) resolved from an empty object set returns an uninitialised stack slot as the object ([ledger SU8](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070)). | Both fixed: an empty set gives no object and the action does nothing. | Never reproduce an undefined read (owner rule); latent. Proposed by ARC-RES-070; lands with the SCRIPT interpreter slices. | none |
| SU16 (ARC-RES-070) | Original 1.0.7.4: the dialog start opens the reaction conversation, spending an armed "force good reaction" fate point, before its kill-on-sight refusal ([ledger SU16](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070)). | Kept in both settings. | The owner kept the same pattern in MG20 (2026-10-20); whether the order is intended is unknown **(inference)**. Alternative for the owner: run the kill-on-sight test with the boosted reaction before committing the fate point. Proposed by ARC-RES-070. | none |
| SU17 (ARC-RES-070) | Original 1.0.7.4: the spell object-flag component handles only the first special bit of its mask, so FLEEING given with the undead bit is written or cleared directly in the critter flags past the setter (no source, FIGHTING kept, no exit-combat script, no flee goal interrupted), and bits given with FLEEING or blind are dropped ([ledger SU17](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070)). | Every special bit is handled (FLEEING always through flee or stop fleeing) and the remaining bits are written. | A bypass of the setter (inference: unintended); latent until MT13's census finds a record that combines the bits. Proposed by ARC-RES-070; lands with MT13 and slice I3. Owner decision 2026-10-21: Steamwoven fixes it, the Original setting keeps it. | `ObjFlagHandlesFirstSpecialBitOnly` (Original setting) |

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


