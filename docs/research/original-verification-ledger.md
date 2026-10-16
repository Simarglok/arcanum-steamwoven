# Original verification ledger

Status: **Research**, work item ARC-RES-048 (2026-10-17). One place that records, for every
game-mechanics anomaly and decision input this project took from a CE reading, what the retail
1.0.7.4 executable does at the same function. It applies the
[original executable check](original-executable-check.md) to the decisions of the domain studies,
and its verdicts decide which differences become switches of the two rule settings of owner
decision D8 (see [the AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16)).

The combat-core anomalies A1–A10 were checked by ARC-COMBAT-001 and are not repeated here
([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). Domain
documents keep their own tables; where a row below contradicts a domain document, this ledger is
the newer evidence, and the domain document's "likely intent" column is superseded by the verdict.
Owner decisions that rest on a contradicted fact are listed under
[Decisions to revisit](#decisions-to-revisit) and stay unchanged until the owner answers.

## Inputs and method

- The owner's unchanged GOG install, `Arcanum.exe` 1.0.7.4, SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52` (re-hashed by every reader).
- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd` for function names, addresses
  and the questions.
- Read only with Xcode's `llvm-objdump` at the CE addresses, following the method's steps. Every
  function named below keeps its direct branches inside `.text`; none reaches the GOG `.IIDKing`
  section, so none is patched.
- Two independent readings (method step 6): a first reader per domain, then a second reader that
  did not see the first verdicts and re-read the rows that set or change a decision (most CE slips
  and original bugs, and every row flagged as a decision input). [Second reading](#second-reading)
  lists the rows read once, the agreement and the one difference in classification.
- No bytes, listings or decompiled code are stored; scratch output stayed in the session
  scratchpad. Addresses are the retail virtual addresses.

## Verdicts and what they mean for the two settings

| Verdict | Meaning | Rule settings (D8) |
| --- | --- | --- |
| **CE slip** | The original differs from CE's reading; CE reconstructed it wrongly. | No switch: Steamwoven and Original both follow the original. |
| **Original bug** | The original does what CE does, and it looks unintended. | Switch candidate: Original keeps the bug; Steamwoven fixes it only when the owning slice records a fix under D2. |
| **Original** | The original does what CE does, and it is plausibly intended (or the "anomaly" was a misreading). | No switch: both settings follow it. |
| **CE change** | CE marks a deliberate change (`CE:` / `FIX:`); the row states the original. | The original behaviour is the Original setting; whether Steamwoven keeps CE's change is a D2 decision. |
| **Not checked** | No code to compare (data, labels, editor-only) or out of scope; the reason is given. | Unchanged. |

## Magic (ARC-RES-043, load map ARC-RES-040)

Source: [magic core](magic-core-evidence.md#ce-anomalies-recorded-not-adopted),
[load map](combat-domain-load-map.md#8-ce-anomalies-found-by-this-reading),
[reaction](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MG1 | `sub_450940` (0x450940), from `sub_429B50` | The pre-animation affordability check reads the **cast spell's** cost (the run's spell field) and maintenance cost; no read past the 223-entry table. It applies no mastery (as CE). | CE slip (spell index); no mastery is Original |
| MG2 | `sub_4507D0` (0x4507D0), `spell_cast_cost` (0x4B1660), 0x4508A0 | Run time: a mastered cost is halved and rounded **up** (`ceil(cost / 2)`; the odd test reads the cost before halving), then cheat 1, then dwarf ×2. `spell_cast_cost` and the UI check are as CE, so odd costs still disagree between the three. Cost 6, mastered dwarf: 6 at run time, not 8. | CE slip (run-time rounding); three formulas Original |
| MG3 | `sub_452F20` maintain branch, `sub_4532F0`, `sub_453370` | A tick with a resistance margin charges the plain cost and then the surcharged cost. | Original bug |
| MG4 | `sub_4537B0` (0x4537B0) | The resistance term is subtracted from the save, against the manual's "+1 per 10 %". | Original bug |
| MG5 | `magictech_invocation_check` (0x4564E0) | The tech-aligned target test compares a d100 with a negative value: never fires. | Original bug |
| MG6 | `sub_455250` (0x455250) | Absolute aptitude difference tested for `< 0`: the time scaling never applies. | Original bug |
| MG7 | `MTComponentInterrupt_ProcFunc` (0x452300), `sub_4573D0` | The interrupt invocation has no source, so the search returns at once: Interrupt does nothing. | Original bug |
| MG8 | `MTComponentSummon_ProcFunc` (0x452830), `magictech_effect_summon` (0x450C40) | The faction switch is fed **`Clear_Faction:`**; `List:` only picks the prototype. | CE slip |
| MG9 | `magictech_pick_proto_from_list` (0x452900) | Roll 0..100 over weights summing to 100/120 (same tables as CE); 100 falls off the end to entry 0 with a log. | Original bug |
| MG10 | `magictech_component_trait` (0x452B00), `sub_452CD0` | Elemental polymorph assigns the two flag words; restore clears only those bits. | Original bug |
| MG11 | `sub_450AC0`, `sub_450B40` | The maintained-run counter loops and returns 0. | Original bug |
| MG12 | `spell_ui_maintain_add` (0x57C290) | Signed `≥ count` test lets slot −1 through; `INT / 4` uncapped. Latent. | Original bug |
| MG13 | `sub_4537B0` | Fate save sets margin 10, display 100, and the save still rolls. | Original |
| MG14 | `magictech_cast_spell_fail_chance` (0x453B20) | Predictor differs from the resolver as CE says (R / 10, 100 above R 40, a d20 consumed). | Original |
| MG15 | `sub_455550`, `sub_450420` | Reflection charges the global "last cost". | Original bug |
| MG16 | `sub_452F20` begin branch | BEGIN cost paid before the no-stack refusal. | Original |
| MG17 | `magictech_process` (0x4510F0) | Fate maximisation evaluated on every action with components. | Original |
| MG18 | target table at 0x596140 | `Tgt_Obj_No_ST_Critter_Dead` lacks the critter bit. | Original bug |
| MG19 | run creation near 0x455C30, `sub_453FA0` | One run flag means "unresistable" and "final END scheduled"; unresistable spells end after BEGIN. | Original bug |
| MG20 | `sub_450420` | Mana stores drained before the fatigue refusal. | Original |
| Shell and reflection order (O10) | `sub_455550` (0x455550) | The "target is the caster" pass comes **before** the anti-magic shell, so a caster can target itself through its own shell; END actions get no shell or reflection exception. | CE change |
| Charm bonus (reaction A4) | `magictech_component_obj_flag` (0x454A10), `reaction_adj` (0x4C0DE0) | Charm on gives +30 and charm off −30, both through the PC's good/bad reaction effects, so the asymmetry the reaction study describes is real. Charm off has no animal test: an animal charmed by a PC (no +30) still gets −30, in the original and in CE. | CE change (asymmetry confirmed) |
| Summon palette | `magictech_effect_summon`, `sub_452CD0` | The palette applies to the current art only; after a polymorph ends the summon shows the prototype palette. | CE change |
| Magic resistance source (O9) | `sub_4537B0`, `magictech_cast_spell_fail_chance` | Both read the stored magic resistance directly: no armour, no effects, no 0..95 clamp. | CE change (`CE:`) |
| Effect remove risk | `MTComponentEffect_ProcFunc` (0x451F20), `effect_remove_one_typed` | Removes the first entry with that number, whatever its cause. | Original |
| LM5 | `magictech_min_intelligence` (0x4502E0) | Returns 5. | Original |
| LM6 | `magictech_min_willpower` (0x4502F0) | `IQ:` is the willpower minimum. | Original |
| LM7 | `mt_item_spell_to_magictech_spell` (0x4CB790) | `& 0xFF`: 10000 becomes 16. | Original |
| LM9 | `effect_parse` (0x4E9C20) | `*N` and `/N` parse the number **after** the operator, so no divide by zero; an unknown operator still counts and there is no pair bound (as CE). `min`/`max` are told apart by a case-sensitive second letter; CE's lowercase `FIX:` is a CE change. | CE slip (`*`, `/`); rest Original |
| LM10 | `item_effect_mod_load` (0x4D3FF0) | Module maximum overwrites the base and is not restored. Latent. | Original bug |
| LM19 | `magictech_post_load` (0x44F540) | Skipped slots are not cleared (the reset clears them first). | Original |
| LM20 | `bless_mod_load`, `bless_copy_field`, `curse_copy_field`, `mes_search` | No guard on a missing module file; **both** bless and curse copy names unbounded (CE's bounded bless copy is an unmarked CE change). | Original bug |

## Skills, levels and character (ARC-RES-044, ARC-RES-031, ARC-RES-027)

Source: [skills](skills-evidence.md#8-ce-anomalies-found-by-this-reading),
[load map](combat-domain-load-map.md#8-ce-anomalies-found-by-this-reading),
[Virgil auto-level](virgil-auto-level.md#anomalies-s),
[starting hero](starting-hero-evidence.md).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| Dodge (new, SKX1) | `basic_skill_effectiveness` (0x4C62E0) | Dodge effectiveness is **2 × level**; CE uses 5 × level (the project did until ARC-WORLD-023). The other basic and tech formulas match CE. | CE slip |
| SK1 | `script_execute_action` (0x447220), get-skill case | Returns after storing the level; no fall-through into "cast spell". | CE slip |
| SK2 | `skill_ui_dec_skill` (0x57AEB0) | The loop re-reads the points each pass: one click removes points until the level changes and refunds each; no hang. | CE slip |
| SK3 | `skill_ui_set_training` (0x57AC90) | Routes basic and tech correctly, except that skill 12 (repair) goes to the basic setter, which refuses it. `ui_set_skill_training` is called only from the multiplayer handler. | CE slip (labels); off-by-one is an original bug, multiplayer only |
| SK4 | `ai_check_use_skill` (0x4AE570) | The lockpick case is pick locks; disarm traps has no check. | CE slip |
| SK5 | gamble gate `sub_579840` | The Expert and Master gates read the **PC's** gambling training. | CE slip |
| SK6 | `basic_skill_effectiveness` | Prowling's formula unreachable, backstab 0, as CE. | Original |
| SK7 | `skill_invocation_run` (0x4C7160), repair | HP restored and durability lost whatever the roll. | Original |
| SK8 | `dialog_use_skill` (0x419CB0), `anim_goal_use_skill_on` (0x4350F0), `sub_42A2A0` | The paid service's forced-success request is dropped; it rolls like any use. | Original bug |
| LM1 | `item_wield_best` (0x465170) | The damage loop **adds** the five types. | CE slip |
| LM2 | `item_wield_best` | The best in-range weapon's score is stored; the best in-range weapon wins. | CE slip |
| LM11 | `level_recalc` (0x4A69C0) | The cap is level 50; reaching 51 sets XP back to the **level-50** threshold (CE: level-51 threshold). | Cap Original; XP value CE slip |
| A1 (auto-level) | `level_recalc`, `stat_base_set` (0x4B0980) | No recursion (it follows from LM11's XP value). A level-50 PC raises lagging followers by one level (cap 50) each time it earns the gap to the level-51 threshold, matching the 1.0.7.4 update note. Assumes `xp_level.mes` entry 51 > entry 50 (data, not read). | CE slip |
| A2 (auto-level) | `auto_level_apply` (0x4A7030) | The log append has no length at all (CE's pointer `sizeof` is its own rewrite). | CE slip |
| A3 (auto-level) | `auto_level_process_rule` (0x4A7340) | Null token to `atoi` unchecked. Latent. | Original bug |
| A4, A5 (auto-level) | `auto_level_process_rule`, `level_recalc` | Skip-on-failure and no death check in the follower loop, as CE. | Original |
| A6 (auto-level) | `object_list_all_followers` (0x441260) | The inner cursor advances. | CE slip |
| LM16 | `critter_fatigue_adj_set` (0x45D4F0) | Writes fatigue points; never referenced. | Original |
| LM17 | stat key table at 0x5B5384 | Unspent points hold the poison-level key, magick points the age key. | Original bug |
| SH-07 (with LM9) | `effect_parse`, `effect_adjust_func` (0x4EA6C0) | `*2` stores 2, so backgrounds using `*` double the adjustment; order percent, multiply, divide, add, min, max. | CE slip |
| Character creation | `mainmenu_ui_new_char_button_released` (0x544FF0), `mainmenu_ui_shop_refresh` (0x545E80) | Background effect added only on a button press; with no shop map the game starts before the new-game flag. | Original |

## NPC AI, reaction and companions (ARC-RES-039, ARC-RES-028, ARC-RES-024)

Source: [AI plan](npc-combat-ai-plan.md#first-pass-ce-anomalies-to-confirm-not-adopted),
[load map](combat-domain-load-map.md#8-ce-anomalies-found-by-this-reading),
[reaction](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions),
[companions](companion-follow-evidence.md#open-questions).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| P1 | `ai_look_for_item_func` (0x4A8F90) | Keeps the **nearest** item. | CE slip |
| P2 | `ai_calc_party_size_and_level_internal` (0x4AE0A0) | Sums each follower's own level. | CE slip |
| P3 | `ai_redirect_perform` (0x4A9560) | The MinIQ test reads the **redirect target's** (the spell target's) intelligence, once for the whole vicinity; candidates are filtered by their own critter flags. | CE slip (not the "candidate's intelligence" the plan expected) |
| P4 | `ai_heal_func` (0x4A8AA0) | The bit tested in the target's critter flags is "mechanical": AI never heals or resurrects mechanical critters. | Original |
| P5 | `sub_4A8570`, `combat_turn_based_subturn_end` (0x4B7630) | START/END_COMBAT are per-turn hooks: real time runs END then START every pass in combat mode; turn-based runs START on every pass (before the turn check) and END at the end of the NPC's subturn. | Original |
| P6 | `ai_action_perform` (0x4AC180), `sub_4AABE0`, `sub_57CC70` | FLEE becomes SURRENDER on the next pass, which leaves combat mode while the NPC keeps running; a surrendered NPC refuses dialog with the side it fled from. Consistent design. | Original |
| P7 | `sub_4ABC70` (0x4ABC70) | The defensive list is tried when parameter 13 is below the d100: chance `100 − p13` %. Whether that is a bug depends on the label in the original `ai_params.mes` header (data). | Original (label open) |
| LM3 | `ai_init` (0x4A8320), `ai_mod_load`, `ai_copy_params` | Rows fill by position from the first entry's number; no bounds check, so a first entry at 100 or above (150 for the module) writes past the table. Latent (no gaps in the data). | Original bug (latent) |
| LM4 | `ai_mod_unload` (0x4A84C0) | Empty: module rows survive. | Original |
| LM8 | `combat_load` (0x4B2020) | Required action points read, then set to 0. | Original |
| LM12 | `background_is_legal` (0x4C2B50), `sub_4C1500` | 8-entry race code table and 11×8 reaction table unchecked. Latent. | Original bug (latent) |
| LM13 | `invensource_get_id_list` (0x4BF5D0) | Accepts `id ≤ count`: the last set is reachable. | CE slip |
| LM15 | prototype range labels | Comments and labels only; the data census decides. | Not checked |
| LM18 | `script_unlock` (0x44C450) | Returns early only **in** editor mode; unlocks in the game. | CE slip |
| Reputation P1 (D1) | `reputation_parse` (0x4C1730) | The skip between numbers passes digits **and** `-`/`+`, so a signed effect keeps its written origin and faction. | CE slip |
| Reputation A2 | `ai_attack` (0x4A9650), `reaction_adj` | A deliberate hit on the PC's follower **adds** parameter 9: the reaction rises. | Original bug (likely) |
| Reputation A3 | `magictech_component_obj_flag` | Mind control applies −50 on start and nothing on end. | Original |
| Reputation A5 | `reputation_add` (0x4C1D20), `reputation_reaction_adj` (0x4C1AC0) | No range check. Latent bug if a dialog passes `RP 1`/`RP 2` through. | Original (latent bug) |
| Wait timer (C12, O3, TE8) | `ai_npc_wait` (0x4AA7A0) | The timer is scheduled when the leader's charisma is **not** extraordinary and the NPC is not mind-controlled, and it carries the NPC's handle. Followers of an ordinary leader time out (0, 15 min, 2 h, 1 d, 7 d, 30 d by wait index); Virgil (index 6) never does. | CE slip |
| Round order (combat O2) | `object_list_rect` (0x440B40) | Sector by sector; inside a sector the critter most recently added or moved in comes first. | Original |

## Scripts, event queue and randomness (ARC-RES-042, ARC-RES-045, ARC-SCRIPT-007, ARC-RES-046, ARC-WORLD-019)

Source: [dispatcher](scr-event-dispatcher.md#11-anomalies-recorded-not-adopted-s),
[census](scr-census.md#anomalies-for-s1-s), [admission](../scripting/scr-program-admission.md),
[event queue](game-event-queue.md#7-ce-anomalies-recorded-not-adopted),
[session randomness](../world/session-random-runtime.md#what-the-original-does).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| X1 | `script_execute_action`, dialog case (0x447282) | Flags go to flags, counters to counters. | CE slip |
| X2 | `script_execute` (0x4449B0) | With no attachee the type comes from an uninitialised slot. | Original bug |
| X3, sector | `objlist_notify_sector_changed` (0x4F2330) | Number, flags and counters are compared and the changed copy is stored: a sector script's self-removal persists. | CE slip |
| X3, tile | `tile_script_exec` (0x4C0980) | The **unmodified** record is stored back: nothing a tile script changes about itself persists, including "remove this script". | Original bug |
| X4 | `sub_44B030` | Call-script runs one local copy and writes nothing back. | Original |
| X5 | `script_execute`, `script_flags` | The two missing-file exits are as CE. | Original |
| X6 | `script_execute`, `sub_44C1B0` | −1 next, −2 skip default, −3 and −4 run default; −2 and −3 clear an auto-removing script, −4 does not. Other negative values fail the bounds check (skip default). So the admission refusal `NegativeGoto` is the project's own strictness, not the original's: in the original a literal goto of −1..−4 is a control code. | Original (CE slip in the −4 removal detail) |
| X7 | `script_execute_action`, divide (0x4474CE) | Stores the signed quotient; nothing for divisor 0. | CE slip |
| X8 | `script_set_value` (0x44BB50) | Counter writes are masked to 8 bits. | CE slip |
| X9 | `script_execute_action`, action 86 (0x4494DF) | Reads slot 1, as CE. (The headline punctuation test reads the last character; CE reads the terminator and always appends a period.) | Original |
| X10 | `script_global_flag_set` (0x444D20) | Global-flag writes are not masked: 2 sets the next flag, a negative value every higher flag of the word. | Original bug |
| TE1 | call-in cases, `sub_454790`, `datetime_add_milliseconds` (0x45AA10) | The 32-bit `seconds × 1000`, `× 8` delay wraps above 536,870 s as in CE (see [second reading](#second-reading)). | Original bug (edges only) |
| TE2 | call-at case, `sub_45AD70` | Counts from the start hour, as CE. | Original |
| TE3 (OQ1) | `timeevent_add_base_at_func` (0x45B8C0), `sub_45B750` | Ties newest first; events merged after a ping go in front of older equals. | Original |
| TE4–TE7, TE9 | `timeevent_clear_one_ex`, `timeevent_ping` (0x45B370), `timeevent_recover_handles_internal` | As CE: two removals possible, shared loop guard, merge drops stale handles without recovery, handler results ignored, null objects passed on. | Original |
| TE10 (ARC-WORLD-024) | `stat_poison_timeevent_process` (0x4B1170), `stat_base_set` (0x4B0980), `poison_timeevent_schedule` (0x4B1350) | The recovery case writes the lowered level through `stat_base_set`, which for a positive level calls `poison_timeevent_schedule` with recovery on; that adds a recovery event when `timeevent_any` finds none (the firing one is already off the list). The handler then adds another recovery event itself (120,000 ms) while the level stays positive. From the second tick on two recovery events run per period, as in CE. Read twice (the second reader, for the ARC-WORLD-024 review, decoded the post-write switch tables of `stat_base_set` and the list unlink in `timeevent_ping` 0x45B370 before the handler call). | Original bug |
| OQ2 | type table 0x5B2188, `timeevent_save`, `ai_timeevent_process` (0x4AD200) | The AI tick kind is not saved; a load schedules a new tick per NPC with 0..5000 ms jitter. | Original |
| OQ3 | `timeevent_save_nodes_to_map` and neighbours | Per-map `TimeEvent.dat` files; events follow their objects across a teleport. | Original |
| Random (R1–R6) | `random_rand` (0x4CEF50), `random_between` (0x4CEF00), `random_seed_generate` | LCG 1,664,525 / 1,013,904,223, output bits 8..22; modulo ranges, swapped bounds; time seed; no save or load; spawn-inventory and dialog reseeds. All as the contract says. | Original |

## World, movement and data (ARC-RES-047, ARC-RES-029, ARC-RES-034, ARC-DATA-021, clock)

Source: [locomotion](locomotion-evidence.md#anomalies),
[first building](first-building-evidence.md),
[generated wilderness](generated-wilderness-evidence.md#anomalies),
[combat fields](../formats/combat-fields.md), [clock policy](../world/game-clock-policy.md).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LA1 | `sub_437990` (branch 0x437A5C) | At Speed 8 the middle rate. | CE slip (doc correct) |
| LA2 | `sub_430FC0` | Combat-mode running adds 1 fatigue per 5th step (= A4). | CE slip (doc correct) |
| LA3, L2, L5 | object click near 0x54FB19, `get_always_run` (0x4304C0), `turn_on_running` (0x436AB0) | Object clicks always run; Num Lock is a toggled state seeded from the key; run needs load under 60 % and fatigue above 0. | Original |
| W1, W2, W7 | `sub_4273B0`, `critter_can_jump_window` (0x45F570), path search `sub_41F9F0` | Window pieces, who can jump, +50 path cost, as the study says. | Original |
| LA4 (a, b) | `location_rot` (0x4B8D50), `sub_4288A0`, door goals 0x428550–0x428800 | Same tile gives direction 5; the window jump and every door open-attempt goal pass `object_rot(walker, portal)`. | Original |
| LA4 (c), building U4 | `ai_is_indoor_to_outdoor_transition` (0x4AECA0) | The "opposite direction" branch really **swaps** the portal and far tiles; CE copies the far tile into both, so in CE that branch never counts as leaving. In the original a walker coming from across counts as leaving when its tile is indoor and the portal's tile is not. Side effect: on the portal's own tile (direction 5) a portal of raw rotation 0–2 is tested in reverse, so an outdoor walker there passes as "leaving". | CE slip; the direction-5 side effect is an original bug |
| Wilderness A1 | `sector_light_list_load` (0x4F72C0) | Lights move with the object rule (sector × 64 + local tile). | CE slip (confirms the proposed policy) |
| Wilderness A3, A6 | `terrain_sector_path` (0x4E84C0) | `-1.sec` path; a null name faults. | Original (A6 an original crash bug) |
| Wilderness A2, A4, A5 | — | Editor encoder and `arcanum2.dat` folder data. | Not checked |
| Combat fields | `item_weapon_damage` (0x465F70), `item_damage_min_max`, `item_weapon_range` (0x465E30), `item_weapon_magic_speed`, `item_armor_ac_adj` (0x465BA0), `ai_copy_params`, `item_weapon_skill` (0x465D80) | The field numbers the original passes (damage 116/117 by type, magic damage 118, speed 119/120, range 121/122, flags 112, ammo 125, AC 152/153, AI row 282, monster damage 303 at 2·type and 2·type+1) match the ARC-DATA-021 reader's slots. | Original (reader confirmed) |
| Field inheritance | `obj_field_fetch` (0x408A20), `sub_408F40`, `sa_get` (0x4E76C0) | An instance field replaces the prototype's whole field; a missing sparse key reads 0. A null array would fault; "null reads 0" is the project's fail-safe. | Original |
| Lock hours | `object_locked_get` (0x441D40) | Locked-by-day and by-night use hours 7..21. | Original |
| Follow C7–C9, C3 | `anim_goal_follow_obj` (0x4346A0), `sub_42BF40`, `ai_timeevent_delay` (0x4AD5D0), `ai_init` | Follow ranges 4/7 and 2/5, walk within range + 2, AI delay `250 + 4750·min(d,30)/30` ms, 30-cell catch-up, row filling. | Original |
| Clock add | `datetime_add_milliseconds` (0x45AA10), 0x45AA50 | Day carry only when the sum is above 86,400,000, so a sum of exactly one day stays as 86,400,000 ms of the same day. | Original bug (off by one; see [second reading](#second-reading)) |
| Clock current day/month | `datetime_current_day` (0x45AB00), `datetime_current_month` (0x45AB20) | The two getters are exchanged; the ordinary getters are right. Nothing calls them. | Original bug (dead code) |
| Clock subtraction | `datetime_sub_milliseconds` (0x45A9E0) | The underflow branch computes the same odd expression as CE. | Original bug |
| Clock start | `gameinit_mod_load` (0x4B9C30) | 1885 and 15:00; hour 0 stored as 1 ms. | Original |

## Decisions to revisit

These owner decisions or accepted proposals rest on a CE fact that the original does not share.
Nothing below is changed by this ledger; each needs the owner's word or its owning slice.

1. **Reputation D1** ([reaction study](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions)):
   the default "source parse" (signed effects get origin = faction = adjustment) is a CE slip. The
   original keeps the written scope, which is the other reading D1 offered; under D8 a CE slip gets
   no switch.
2. **Dodge 5 × level** ([attack resolver](../world/attack-resolver-runtime.md), ARC-COMBAT-001,
   `AttackRules.DodgeEffectiveness`): implemented from CE; the original uses 2 × level. Under D8 both
   settings follow the original; ARC-WORLD-023 changed the resolver to 2 × level.
3. **Skills §9.5** ([skills study](skills-evidence.md#9-consequences-for-steamwoven-project-policy-proposals)):
   SK1, SK2, SK4 and SK5 are CE slips, so the Original setting reproduces none of them; SK2's "one point
   per click" becomes a design choice, not a fix. SK8 stays a switch candidate; SK7 is the original's
   code.
4. **Window jump and door "leaving" test** ([locomotion](locomotion-evidence.md#anomalies) LA4/W3/D3,
   [first building](first-building-evidence.md) U4, ARC-WORLD-018 door admission and the research
   probes): the original swaps the tiles; the 324-window census and door results with an outdoor
   portal tile need recomputing. The direction-5 side effect is an Original-setting switch candidate.
5. **SCR S2 choices** ([census](scr-census.md#anomalies-for-s1-s)): X7 (quotient, not sum) and X8
   (masked counters) are settled by the original, so neither is an Original-setting option; X10 stays a
   switch candidate. X6: the S1 admission refusal of negative gotos (`NegativeGoto`) is stricter than
   the original, which reads −1..−4 as control codes; whether the Original setting admits them is
   open (the census decides whether any data uses them).
6. **Tile script 2498 self-removal** ([Virgil script attachments](virgil-script-attachments.md),
   ARC-RES-037): holds only in CE; in the original the removal does not persist.
7. **Starting hero `*n` / `/n`** ([starting hero](starting-hero-evidence.md), SH-07 and the runtime's
   refusal): the original parses the factor, so backgrounds 5 and 11 double their reaction adjustment.
8. **Magic slices** ([magic core](magic-core-evidence.md)): MG1, MG2 (mastery `ceil(cost / 2)` at run
   time) and MG8 are CE slips; O9 and O10 are answered by the CE-change rows.
9. **AI stages** ([AI plan](npc-combat-ai-plan.md)): P1, P2, P3, LM1, LM2, LM13 and LM18 are CE slips
   (P3 with a different reading than the plan expected); P4–P7 are the original's behaviour.
10. **Companion wait timeout** ([companions](companion-follow-evidence.md#open-questions) O3, event
    queue TE8): the timeout is real for leaders without extraordinary charisma.
11. **Auto-level A1 and LM11** ([Virgil auto-level](virgil-auto-level.md#anomalies-s)): no recursion
    in the original; followers rise at PC level 50 as the 1.0.7.4 notes say.

## Second reading

Four second readers re-read 77 decision-bearing rows from the executable without the first
verdicts: magic (MG1–MG12, MG15, MG18, MG19, the CE changes, O9, LM9, LM20), skills (every basic
and tech formula, SK1–SK5, SK8, LM1, LM2, LM11, LM17, auto-level A1–A3, A6), AI and scripts (P1–P4,
P7, LM13, LM18, reputation P1 and A2, the wait timer, X1–X3, X6–X8, X10, TE1) and world (LA1, LA4,
wilderness A1, the clock functions). **Every observed behaviour agrees, and so does every verdict
but one classification**: the clock's day carry on a sum above (not equal to) one day was rated
"Original" by the first reader and "Original bug" by the second; the ledger records it as an
off-by-one bug of the original. Two readers saw single lines of an early draft of this file
through a repository-wide search after forming most of their verdicts; they say so in their
notes. For magic the lines shown were MG1, MG2, MG4, MG8, MG19, O9, LM9 (verdicts already formed)
and LM20 (read from the executable afterwards); for skills SK1–SK8, LM1, LM2, LM11, A1 and LM17
(verdicts already formed). The results did not change.

Read once (first reading only): LM10, LM3, LM12, MG13–MG17, MG20, LM5–LM7, LM19, SK6, SK7, auto-level
A4 and A5, LM16, P5, P6, LM4, LM8, reputation A3 and A5, X4, X5, X9, TE2–TE7, TE9, OQ1–OQ3, the
random facts, LA2, LA3, the window rows, wilderness A3 and A6, the combat fields and inheritance,
the lock hours and the follow rows.

Details the second reading added or sharpened:

- **TE1**: the delay is `seconds × 8000` modulo 2^32, so it wraps above 536,870 s; a zero product
  becomes 1 ms before the × 8, i.e. 8 ms. The separate signed overflow above 2,147,483 s that the
  study lists does not occur.
- **Clock current day/month**: the exchanged getters have no caller and no stored pointer in the
  executable; the bug is dead code.
- **Effect min/max**: `min` and `max` are recognised case-insensitively and then told apart by a
  lowercase second letter, so `MIN` acts as max.
- **SK8**: another use-skill callback (0x428E10) does map the requested flag to a forced success,
  so the drop in 0x42A2A0 looks unintended.
- **Shell and reflection at END**: a run cast before a reflection shield appeared is reflected at
  END (source and target swap) and the reflector pays again.
- **Wilderness A1**: the original also clears each moved light's "modified" mark, which CE omits.

## Not checked

- Data questions: LM15 labels, wilderness A4/A5, P7's label in `ai_params.mes`, `xp_level.mes`
  entries 50/51 behind A1, how often shipped scripts reach SK1, which data uses the LM17 keys.
- Code left for later: wilderness A2 (editor only); locomotion W4 and the frame-time clamp;
  companion C2, C4, C10, C13 frequency; world-transition jump points and teleports; reputation A6
  and A7; the first-tick marker after a load (OQ2, H1).
