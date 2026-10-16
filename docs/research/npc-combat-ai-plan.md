# NPC combat AI: source map and decomposition

Status: **Research plan**, work item ARC-RES-039 (2026-10-16). This document
maps how the pinned source organizes NPC behaviour in and around combat and
breaks the owner's goal into ordered work: **phase 1** rebuilds the original
system stage by stage with source parity as the default, **phase 2** adds
optional patches (mods) that change the play experience without replacing the
phase 1 behaviour. It records no final rule, changes no production API,
viewer, session schema or dialog port, and every stage, code and policy below
is a **proposal** until the owner accepts it.

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

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`][ce] (Sustainable Use License
  v1.0) at the line ranges linked below. CE is a reconstruction lineage, not an
  original-executable observation, and keeps decompiler names (`sub_…`); a
  reading of their intent is marked as such.
- **First-pass finding**: what one reading of a function shows. Each is
  re-read and either confirmed or corrected by the stage research that owns
  it; nothing here is a contract.
- **CE anomaly**: a branch that looks inconsistent. Phase 1 records it and
  keeps the source behaviour behind an explicit switch; phase 2 may fix it.
- **Project policy**: a choice this project makes where the source leaves one
  open. Every policy below is a proposal.

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
  turn ([tick][timeevent], [activity][activity], [delay][delay],
  [subturn][cb-tb], [attacked: miss][cb-hit], [attacked: damage][cb-dmg]).
- **Fixed pipeline.** Each pass: gate (dead, off, stoned, unconscious, PC
  off, in a conversation, someone else's turn, busy animation, generator) →
  refresh wield/weapon flags → **heal** (self, leader, other followers); if no
  heal was chosen, **look for items** (ammo, weapon, armour when flagged); if
  no pickup started either, **update the danger state** (which also picks a
  spell or item, below) → **perform** ([pipeline][process], [gate][gate]).
- **Four danger states.** Every NPC is in one of NONE, COMBAT_FOCUS (fighting
  a target), FLEE or SURRENDER, stored as critter/NPC flags plus a
  combat-focus or fleeing-from handle. One setter owns all transitions; it
  fires the `ENTER_COMBAT`/`EXIT_COMBAT` scripts, the alerted sound and the
  reaction drop toward a PC enemy, and it is where invulnerable critters, the
  clockwork decoy, the "no flee" flag, a leader with extraordinary charisma
  (followers never flee) and the global "NPC fighting" switch are applied
  ([state read][danger-get], [setter][danger-set]).
- **Hostility is decided per candidate.** A peaceful NPC scans critters in
  the square of half-size `clamp(Perception, 1, 10)` cells, nearest first
  (decoys first, unconscious ones last), keeps only those it can see or hear,
  and attacks the first that is kill-on-sight (script veto, KOS flag and
  faction, reaction at or below AI parameter 10, alignment gap at or above
  parameter 11, decoy, guard duty), or helps an ally under attack, or avenges
  a dead ally ([find target][find-target], [KOS][kos], [protect][protect]).
- **A fight keeps a target and an enemy list.** A fighting NPC keeps its
  current focus while it stays valid (alive, not stoned, not its own leader or
  party, within 20 cells of it and of its leader, a master prowler it cannot
  see is dropped); else it unlocks the target and scans afresh as a peaceful
  NPC does; else it takes the last critter that hit it, else a random valid
  entry of its enemy list (the "shit list", which holds the attacker's whole
  team), else it calms down ([danger update][danger-update],
  [validity][valid], [enemy list][shitlist]).
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
  (1 in 500 per pass) once HP is at least 80 % ([reaction to an
  attack][react], [should flee][should-flee], [flee/surrender
  perform][perform]).
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
  goal on its target ([heal][heal], [spell choice][spell-choice], [list
  chooser][chooser], [action][action], [combat perform][perform-combat],
  [back off][back-off], [grenade][grenade]).
- **Spell and item eligibility** is a typed refusal list: unknown spell, too
  tired or no charge (an NPC without a PC leader below 20 % fatigue never
  casts; cost at or above current fatigue; an empty mana store), not enough
  intelligence or willpower, "cast highest only", a maintained spell with no
  free maintenance slot, fail chance at or above 38 % (not checked for
  healing spells on a target at or below 30 % HP), and a blocked line of fire
  ([eligibility][cast-check]). The ten lists come from per-spell `AI_*`
  priorities in the spell data and from items' mana stores or AI action
  field. Each list holds either items or spells: in combat at or below 50 %
  fatigue items, and spells only when no item qualifies; otherwise spells,
  and items only when no spell qualifies ([mt_ai lists][mt-lists],
  [populate][mt-populate], [spell fields][spell-ai]).
- **Combat is contagious.** Every attack (and a fight start) notifies: the
  attacker's and the victim's followers join or complain ("upset attacking"
  their own party, origin, faction or a good target), the victim records who
  hit it and adds the attacker's team to its enemy list, a PC hitting its own
  follower changes its reaction by parameter 9 (the follower-hit anomaly of
  ARC-RES-028) and may make it leave, and nearby NPCs who see or hear (radius
  2/8/15 by loudness) either join to protect their faction/group or flee
  ([attack event][attack], [followers][follow-join], [noise][noise]). When a
  PC kills, same-faction NPCs within 3 cells already fighting that PC back
  off. Dynamite counts as an attack by the PC on every NPC within 10 cells
  that sees or hears it (not its followers, not mind-controlled ones);
  portal and container crimes alert guards ([killed][killed],
  [dynamite][dynamite], [guards][guards]).
- **Parameters are data.** The 17 AI parameters per row come from
  `rules\ai_params.mes` (rows 0..99) and the module's `rules\gameai.mes`
  (rows 100..149), filled in file order from the first entry found rather
  than by entry number; each NPC names its row in `OBJ_F_NPC_AI_DATA`
  ([table][params], [load][params-load], [module load][params-mod]). The local
  data has 41 base rows and one module row
  ([ARC-RES-028 L3](npc-reaction-reputation-evidence.md)).

## Source map

Every row is a self-contained unit that a stage below owns. Line ranges are
in `src/game/ai.c` unless named.

| # | Unit | Source | First-pass summary | Stage |
| --- | --- | --- | --- | --- |
| S1 | AI parameter table | [36–59][params], [192–274][params-load] | 17 named integers per row; 150 rows, base file fills 0..99, module 100..149. | A1 |
| S2 | Tick and activity | [2859–3066][timeevent] | Heartbeat scripts, generator branch, 30-cell activity, distance-scaled delay, follower catch-up. | A1 |
| S3 | Gate and pipeline | [288–436][process] | Heal, else item search, else danger update; then perform; status recoveries (blind 1/160, crippled 1/8000 per pass, stun end). | A1 |
| S4 | Danger state and setter | [1586–1610][danger-get], [1612–1730][danger-set] | Four states, flags, scripts, sound, reaction drop, global switch. | A1 |
| S5 | Turn-based hooks | combat.c [3024–3038][cb-tb], [762–775][cb-hit], [1868–1876][cb-dmg] | AI pass at subturn start after the heartbeat script, AP to 0 when it starts no goal; immediate pass for an attacked NPC (real time only in effect); AP rules in ARC-RES-030 C8/C11. | A1 |
| S6 | Sight | [3994–4076][see] | Facing, invisibility vs extraordinary PE or detect-invisible, prowling against concealment, clamp 1..10, +1 when blocked; returns excess distance, 0 = seen. | A2 |
| S7 | Hearing | [4078–4197][hear] | Loudness 2/8/15, sleeping halves PE, moving silently, sound traversal cost along a straight path. | A2 |
| S8 | Line of fire | [3305–3369][lof] | Shared with combat O4; blocker and cover rating. | A2 |
| S9 | Noise and witnesses | [1157–1224][noise], [1226–1247][dynamite] | Witness joins a protected side or flees; dynamite. | A2 |
| S10 | KOS, protect, upset | [3259–3303][upset], [3423–3496][kos], [3498–3541][protect] | Reasons KOS_FACTION/REACTION/ALIGNMENT/GUARD/DECOY; protect FACTION/ORIGIN/GROUP/SELF; upset GOOD/ORIGIN/FACTION/PARTY. | A3 |
| S11 | Target search and choice | [1775–1838][choose], [1903–2044][find-target], [2046–2154][valid] | Nearest-first scan in a square box, KOS, ally help, avenge; two-candidate choice: 50 % nearest, 50 % level − distance; 20-cell leash. | A3 |
| S12 | Enemy list and target lock | [4223–4370][shitlist] | Team added on attack, random valid pick, lock for orders. | A3 |
| S13 | Attack event fan-out | [849–1052][attack], [1081–1155][follow-join], [1285–1454][peace] | Redirect, followers, reaction changes, jilted follower, peace (focus cleared, reaction raised to 50). | A3 |
| S14 | Morale | [1732–1773][spell-flee], [1840–1901][should-flee], [2458–2477][perform], [3950–3992][flee-api] | Flee rule, spell-assisted flee, flee → surrender, recovery. | A4 |
| S15 | Back off and min distance | [2479–2496][perform-combat], [2498–2533][back-off] | Parameter 15, path away, kill-triggered back off. | A4 |
| S16 | Heal and support | [438–617][heal] | Resurrect, healer's own fatigue < 20 %, heal skill < 90 % out of combat, heavy < 40, medium < 55, poison, light < 70 (< 90 out of combat); self, leader, followers. | A5 |
| S17 | Spell/item choice | [2156–2257][spell-choice], [2259–2315][chooser], [3606–3681][cast-check], mt_ai.c [134–591][mt-lists] | Summon when alone, defensive by parameter 13 (see P7), offensive; chooser takes the first entry, 21 % a random start; item entries without a spell are used at once, unchecked; refusal codes 1–8. | A5 |
| S18 | Items, weapons, grenades | [619–773][items], [2535–2597][grenade], [3371–3381][switch] | Look for ammo/weapon/armour, wield best, grenade with friendly-fire check. | A5 |
| S19 | Action execution | [2317–2401][action], anim.c [13932–14023][anim-attack] | Cast through invocation, item/skill goals, attack goal (approach, ranged, real-time repeat). | A6 |
| S20 | Followers and orders | broadcast.c [184–370][broadcast], ai.c [1340–1407][stop] | Attack/stop orders, no-attack flag, demaintain spells after the fight, witness/near-death floats. | A7 |
| S21 | Script and dialog surface | script.c [1774–1780][sat-attack], [2543][sat-stop], dialog.c callers | `WILL_KOS`, `ENTER/EXIT/START/END_COMBAT`, heartbeat; dialog refusal in combat; cheat toggle "NPC fighting" (cheat level ≥ 3, key P). | A8 |
| S22 | Non-combat routine | [2599–2858][routine], [3725–3948][portals] | Waypoints, standpoints, beds, portals and containers: FR-AI-001/003, out of this plan except where combat interrupts them. | — |

## First-pass CE anomalies (to confirm, not adopted)

- **P1** The item search keeps the candidate with the **largest** distance
  although it tracks a "nearest" variable ([items][items]).
- **P2** The party-level sum adds the leader's level once per follower instead
  of each follower's level, which skews the "outlevelled" flee test
  ([party][party]).
- **P3** The redirect filter compares the intelligence of the threshold number
  instead of the candidate's ([redirect][attack]).
- **P4** The heal step tests an NPC-flag constant (no-attack) against the
  critter-flag field, where the same bit means "mechanical"; the effect is
  that mechanical critters are never healed by AI ([heal][heal]).
- **P5** While an NPC is in combat mode every pass runs its start-combat
  script, and in real time the end-combat script just before it; the intent
  is unclear ([gate][gate]).
- **P6** Whether FLEE → SURRENDER on the first fleeing action is the intended
  morale model or a reconstruction slip needs a second reading against
  `tb_ui.c` and the scripts that test "surrendered" ([perform][perform]).
- **P7** Parameter 13 is described as the chance of a defensive spell, but
  the defensive list is tried when the roll is **above** it, so the chance is
  `100 − parameter 13` % ([spell choice][spell-choice]).

## Phase 1: faithful reconstruction

Goal: an NPC fight in the AREA viewer behaves as the source specifies, with
every random roll injected and seedable and every decision explainable by a
reason code. Order follows dependencies; each item is one PR.

### Prerequisites outside this plan

Combat core slices 1–4 of [ARC-RES-030](combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(vital state, attack resolver, turn-based round, real-time cadence and combat
mode), the [reaction owner](npc-reaction-reputation-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(slices 1–2), the [companion follow slices](companion-follow-evidence.md) of
ARC-RES-024 (no code allocated yet) and, for slice I8, the magic/tech domain
(FR-MAGIC-001..006). The research stages need none of these; slices I1–I7
need only the first two.

### Research stages (source reading, one document each)

| Stage | Proposed code | Scope (source units) | Output |
| --- | --- | --- | --- |
| A1 | ARC-RES-040 | Core loop: S1–S5. Parameter semantics, tick cadence, pipeline order, danger state machine with every flag and script, turn-based and real-time entry points. | State diagram, transition table, cadence formula, parameter dictionary. |
| A2 | ARC-RES-041 | Awareness: S6–S9. See/hear formulas, loudness, line of fire, witnesses. | Pure-function specs with worked numbers; closes combat O4. |
| A3 | ARC-RES-042 | Hostility and targeting: S10–S13. | KOS/protect/upset decision tables, target scan order, enemy-list lifecycle. |
| A4 | ARC-RES-043 | Morale: S14–S15. | Flee/surrender/back-off rules, P2 and P6 settled. |
| A5 | ARC-RES-044 | Support and magic: S16–S18. | Heal ladder, ten action lists, eligibility codes, item search (P1, P4, P7). |
| A6 | ARC-RES-045 | Execution: S19 and the `anim.c` attack, approach, flee and run goals. | How an intention becomes movement and attacks in both modes; closes companion O5. |
| A7 | ARC-RES-046 | Followers in combat and orders: S20. | Order effects, follower join rules, post-fight cleanup. |
| A8 | ARC-RES-047 | Script and dialog surface: S21. | Every SAP event and SCR action that reads or writes AI state. |
| A9 | ARC-RES-048 (Mac) | Data census: `ai_params.mes`/`gameai.mes` rows, spell `AI_*` fields, item AI action field, KOS/no-flee/guard/AI-row of the Crash Site and cave NPCs and creatures. | Local aggregates only, like ARC-RES-028 L3; picks the first real fight. |

A1 comes first; A2–A5 can run in parallel after it, with A5 reusing the
line-of-fire and protect definitions of A2 and A3; A6–A8 after A3.
A9 can run any time on the Mac.

### Implementation slices (proposals, after the matching research)

| Slice | Needs | Result |
| --- | --- | --- |
| I1 AI parameters | A1, A9 | Data reader for both files and a typed 17-field record with names from A1. |
| I2 Awareness | A2, I1 | Pure see/hear/line-of-fire functions over explicit inputs; synthetic matrix (FR-AI-002 acceptance). |
| I3 Danger state | A1, I1 | Session-owned per-NPC state, flags, focus, enemy list, snapshot schema version, script hook points as typed events. |
| I4 Hostility and targets | A3, I2, I3 | KOS/protect/upset with reason codes, scan and choice with injected rolls. |
| I5 Morale | A4, I4 | Flee/surrender/back-off decisions and their state transitions. |
| I6 Scheduler | A1, I3 | AI time event on the session clock, 30-cell activity, turn-based subturn entry. |
| I7 Melee and ranged executor | A6, combat slices, I4 | Intent to route planner and attack resolver; real-time and turn-based. |
| I8 Support and magic | A5, magic domain | Heal ladder, lists, eligibility, items, grenades. |
| I9 Followers in combat | A7, companion follow slices | Join/complain rules, orders, cleanup. |
| I10 First fight | I1–I7, A9 | One Crash Site fight in the AREA viewer with an AI overlay (state, target, last reason). |
| I11 Parity check | I10 | Scripted scenarios observed in the original game on the Mac compared with seeded runs; NOT_VERIFIED becomes per-rule evidence. |

### Design rules for phase 1 that phase 2 needs

1. **Every decision is a named, pure policy.** Target choice, KOS, flee,
   action choice and heal thresholds are functions over explicit records;
   the source behaviour is the default policy, not inline code.
2. **Every CE anomaly is a switch.** P1..P7 (and those A1–A8 find) are
   implemented as the source does them behind a named flag, so a patch can
   flip one without forking the rule.
3. **Randomness is injected and logged.** Each roll has a purpose label, so
   a seeded run replays exactly and a trace shows which roll decided what.
4. **Decisions return reason codes.** The overlay, tests and mods read the
   same "why" (for example `KOS_REACTION`, `FLEE_OUTNUMBERED`,
   `CAST_REFUSED_FAIL_CHANCE`).
5. **Parameters are data.** Rows load from game files and may be
   overridden from outside the repository; no number lives only in code.
6. **The source profile is frozen.** A change to default behaviour needs a
   source reading or an original-runtime observation, never a balance idea.

## Phase 2: patches and mods

Goal: optional changes on top of the phase 1 rules, chosen per profile,
never altering the "Original" profile. Nothing here starts before the
matching phase 1 slice exists.

| Step | Proposed code | Content |
| --- | --- | --- |
| M0 Profiles and policy seams | ARC-RES-049 (plan), then implementation | "Original", "Fixed" and custom profiles; per-policy override points; save files record the profile; the decision trace is the debugging tool. |
| M1 Bug-fix profile | after A1–A8 | Each confirmed anomaly gets a "fixed" variant (for example P1 nearest item, P2 real party level, the ARC-RES-030 A9 double subturn); the profile is "Original + fixes", nothing else. |
| M2 Data overrides | after I1 | External pack (outside the repo, like the HD pack) overriding parameter rows, per-NPC row choice and spell AI priorities; validated, versioned. |
| M3 Tactical improvements | after I7/I8 | Candidates to test one at a time: threat-based target score (damage, HP, range) instead of the 50/50 rule, focus fire, leftover-AP use in turn-based mode, keeping range and cover for ranged NPCs, friendly-fire checks for area spells (the source checks only grenades), healers prioritising the weakest ally, fleeing toward allies, group cohesion. |
| M4 Companion control | after I9 | Stances (passive, defensive, aggressive), "don't use consumables", "no area spells", extended orders. |
| M5 Difficulty and accessibility | after M0 | Aggression and morale scales, "NPC fighting" as a player option instead of a cheat. |
| M6 AI sandbox | after I10 | Viewer arena: place combatants, run with a seed under two profiles, compare traces and outcomes (win rate, length of fight) for balancing. |
| M7 Mod interface | owner decision | Data-only mods first; whether code mods (C# plugins or a script language) are allowed is decision D3. |

## Owner decisions

- **D1 Parity target.** CE reading as the reference until original-runtime
  observations (I11) exist, or wait for observations before each slice?
- **D2 Anomalies.** Keep the source behaviour in "Original" and fix only in
  "Fixed" (proposed), or fix obvious slips already in phase 1? The same
  decision should cover the combat-core anomalies A1–A9 of ARC-RES-030, whose
  slice 2 leaves them to an owner decision, so the two tracks share one
  policy.
- **D3 Mod format.** Data-only overrides, or also code plugins; where mod
  files live and how they are validated.
- **D4 Saves.** May a profile change mid-game, and do patched saves load in
  "Original"?
- **D5 Task type.** The naming registry has no `AI` or `COMBAT` type; the
  implementation slices would go under `WORLD` (session/simulation) unless
  the owner adds a type to the [naming policy](../workflow/task-naming.md).
- **D6 Codes.** ARC-RES-040..049 above are proposed and not yet allocated on
  the board.

## Method

Source reading only at the pinned commit: `ai.c` (function list in full;
bodies of S1–S21 read once), `ai.h`, `mt_ai.c` and `mt_ai.h` (list building
and cache), and the AI call sites found by searching `src/` (`combat.c`,
`anim.c`, `broadcast.c`, `script.c`, `dialog.c`, `magictech.c`, `critter.c`,
`skill.c`, `trap.c`, `main.c`, UI files). Steamwoven inputs are the
requirements and research documents linked above. No CE code or structure
was copied; names in this document are descriptive.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[params]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L36-L59
[params-load]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L192-L233
[params-mod]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L240-L274
[process]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L288-L436
[gate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L310-L421
[heal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L438-L617
[items]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L619-L773
[danger-update]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L775-L847
[attack]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L849-L1052
[follow-join]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1081-L1155
[noise]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1157-L1224
[dynamite]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1226-L1247
[killed]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1249-L1283
[peace]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1285-L1454
[stop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1340-L1407
[danger-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1586-L1610
[danger-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1612-L1730
[spell-flee]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1732-L1773
[choose]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1775-L1838
[should-flee]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1840-L1901
[find-target]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1903-L2044
[valid]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2046-L2154
[spell-choice]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2156-L2257
[chooser]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2259-L2315
[action]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2317-L2401
[perform]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2458-L2477
[perform-combat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2479-L2496
[back-off]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2498-L2533
[grenade]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2535-L2597
[routine]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2599-L2858
[timeevent]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2859-L3066
[activity]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2940-L2996
[delay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2998-L3012
[upset]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3259-L3303
[lof]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3305-L3369
[switch]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3371-L3381
[party]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3383-L3421
[kos]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3423-L3496
[protect]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3498-L3541
[cast-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3606-L3681
[portals]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3725-L3948
[guards]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3868-L3948
[flee-api]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3950-L3992
[see]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3994-L4076
[hear]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L4078-L4197
[shitlist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L4223-L4370
[mt-lists]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c#L134-L591
[mt-populate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c#L297-L346
[spell-ai]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5826-L5866
[cb-tb]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3024-L3038
[cb-hit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L762-L775
[cb-dmg]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1868-L1876
[react]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1409-L1454
[anim-attack]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L13932-L14023
[broadcast]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/broadcast.c#L184-L370
[sat-attack]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1774-L1780
[sat-stop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2543-L2550
