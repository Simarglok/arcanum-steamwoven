# Original verification ledger

Status: **Research**, work item ARC-RES-048 (2026-10-17); later rows from the tasks named in their sections. One place that records, for every
game-mechanics anomaly and decision input this project took from a CE reading, what the retail
1.0.7.4 executable does at the same function. It applies the
[original executable check](original-executable-check.md) to the decisions of the domain studies,
and its verdicts decide which differences become switches of the two rule settings of owner
decision D8 (see [the AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16)).

The combat-core anomalies A1–A10 were checked by ARC-COMBAT-001 and are not repeated here (A11 and A12 are in
[Turn-based round](#turn-based-round-arc-combat-003); the item wear rules are in [Equipment](#equipment-arc-world-028); critter art names and missing art in [Art names](#art-names-and-missing-art-arc-res-050); the effect list is in [Durable effects](#durable-effects-arc-world-029); experience and levels in [Experience and levels](#experience-and-levels-arc-world-030))
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
| LM15 | prototype range labels | Comments and labels only; the [data census](combat-data-census.md#5-prototypes) found the description header's labels match the art (27309.. unique NPCs, 28310.. monsters). | Not checked in the executable |
| LM18 | `script_unlock` (0x44C450) | Returns early only **in** editor mode; unlocks in the game. | CE slip |
| Reputation P1 (D1) | `reputation_parse` (0x4C1730) | The skip between numbers passes digits **and** `-`/`+`, so a signed effect keeps its written origin and faction. | CE slip |
| Reputation A2 | `ai_attack` (0x4A9650), `reaction_adj` | A deliberate hit on the PC's follower **adds** parameter 9: the reaction rises. | Original bug (likely; owner decided 2026-10-17: Steamwoven lowers, Original switch keeps the rise) |
| Reputation A3 | `magictech_component_obj_flag` | Mind control applies −50 on start and nothing on end. | Original |
| Reputation A5 | `reputation_add` (0x4C1D20), `reputation_reaction_adj` (0x4C1AC0) | No range check. Latent bug if a dialog passes `RP 1`/`RP 2` through. | Original (latent bug) |
| Wait timer (C12, O3, TE8) | `ai_npc_wait` (0x4AA7A0) | The timer is scheduled when the leader's charisma is **not** extraordinary and the NPC is not mind-controlled, and it carries the NPC's handle. Followers of an ordinary leader time out (0, 15 min, 2 h, 1 d, 7 d, 30 d by wait index); Virgil (index 6) never does. | CE slip |
| Round order (combat O2) | `object_list_rect` (0x440B40) | Sector by sector; inside a sector the critter most recently added or moved in comes first. | Original |

## Reaction runtime (ARC-WORLD-026)

Source: [reaction](npc-reaction-reputation-evidence.md#findings), contract
[NPC reaction runtime](../world/npc-reaction-runtime.md). Read for the reaction owner on 2026-10-17 by a first reader
and, independently, a second reader that saw only the questions (method step 6); both agree on every row, and the
second reader found R1's difference. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| R1 effects | `reaction_get` core (0x4C0D00), `effect_adjust_reaction` (0x4EAA80) | Enemy list → 0; a non-PC or non-NPC → 50; stored value + the race/beauty/reputation term, then the `reaction` effects of the **PC** (the second argument), then a floor of 50 when this PC mind-controls the NPC. CE passes the NPC to the effect lookup. | CE slip (effect owner); the rest Original |
| R1 term | `sub_4C1500` (0x4C1500), table 0x5B684C | The race term (row NPC race, column PC race, no bounds check) and the PC's reaction modifier (stat 14) are skipped for an aloof (NPC flag 0x20000) or monstrous NPC; the hostility variant floors them at −49; reputation is always added. The 88 table values equal CE's. | Original |
| R2 | `reaction_adj` (0x4C0DE0) | No-op for 0, wrong types, an NPC this PC mind-controls, and a negative delta while the PC's charisma is extraordinary and it is the NPC's PC leader. The delta always goes through the PC's good/bad adjustment effects (CE's switch to bypass them for charm, A4, is CE's own addition) and is added to the stored value; a negative delta while the NPC's direct leader is the PC sets "check leader" and "check reaction bad". A network client returns early (not single player). | Original |
| R3 | `sub_4C12F0`, `sub_4C1290`, `sub_4C1360` (0x4C12F0, 0x4C1290, 0x4C1360) | Reads search slots 0..9 (slot 0 only when asked) with the base as fallback; a write goes to the PC's own slot (slot 0 first), else the first empty slot 1..9, else evicts the slot closest to 50 when it is closer than the new value, else is dropped. | Original |
| R5, R6 | `sub_4C1020` (0x4C1020), `sub_4C10A0` (0x4C10A0), `reaction_met_before` (0x4C0C40), `reaction_forget` (0x4C0F50) | Start copies the stored value into slot 0 (100 when below 100 and the fate point is spent); end clears slot 0 and writes the value to the PC's slot; met before and forget look at slots 1..9 only. | Original |
| R4 | `reaction_translate` (0x4C0FC0) | Thresholds 0, 20, 40, 60, 80, 100; love = 0 … hatred = 6. | Original |
| R2 effects | `effect_adjust_good_bad_reaction` (0x4EAAB0) | Negative: special effect 7 (bad), positive: 8 (good), 0 returns 0. | Original |
| R8 hit | `ai_attack` (0x4A9650), PC hits NPC | Direct leader is the attacker: a deliberate hit adds parameter 9 (reputation A2, above), then the follow check, which may jilt it. Otherwise accidental −10; deliberate lowers it to parameter 10 when the hostility variant is above it. | Original (A2 an original bug) |
| R8 quest | `quest_state_set_internal` (0x4C4E60) | Accepted: below 41 → adjust by 41 − effective; completed: +10. | Original |

## Turn-based round (ARC-COMBAT-003)

Source: [combat core](combat-core-evidence.md#turn-order-fr-combat-002) C6–C14 and C30, contract
[turn-based round runtime](../world/turn-based-round-runtime.md). Read for the round on 2026-10-17 by a first reader
and, independently, a second reader that saw only the questions (method step 6). Both agree on every row they share; the
second reader found the merge of a later scan (TB3), the first reader the game-time row (TB15, read once). Every direct
branch stays inside `.text`; the only jumps out of a function are tail jumps to function entries.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TB1 | `combat_recalc_perception_range` (0x4B7300) | Effective perception (stat 5) of the local PC, halved with signed truncation, + 5, raised to 10. | Original |
| TB2 | `combat_turn_based_start` (0x4B71E0) | Turn counter 0, animations interrupted, the square `PC ± range` (inclusive) scanned for PCs and NPCs straight into the list, sorted, then begin turn. | Original |
| TB3 | `combat_turn_based_begin_turn` (0x4B73A0), `object_list_copy` (0x4414E0) | Turn counter + 1; the rescan is **merged** into the list: listed critters stay (out of range or dead too), each new one is put at the head, so newcomers come first in reverse scan order; then the sort, the first participant not skipped, else the local PC when it is active, else the round ends. The stall globals (last actor, last action points, forced flag) are reset. | Original |
| TB4 | `sub_4B7580` (0x4B7580), `sub_4B7DC0` (0x4B7DC0), `critter_is_active` (0x45D790) | Skipped: not active (destroyed or off, dead, unconscious, stunned or paralysed, stoned), or `OF_DONTDRAW` and not sleeping. | Original |
| TB5 | `combat_turn_based_whos_turn_set` (0x4B6E70), `sub_4B7010` (0x4B7010) | Action points = effective speed (stat 11), at least 5. An NPC (not during a load) runs its heartbeat script and, on 1, its AI; with no animation started its action points become 0 and the completion check runs. | Original |
| TB6 | `sub_4B7080` (0x4B7080), `combat_tb_timeevent_process` (0x4B7150) | Any PC keeps its turn while action points > 0. The same actor seen again with unchanged action points sets the forced flag; a new actor only becomes the last actor and the last action points keep the previous actor's value (**A11**). A 2 ms turn event then forces the spend and passes on at ≤ 0. | Original; A11 an original bug |
| TB7 | `combat_turn_based_next_subturn` (0x4B7690), `combat_turn_based_subturn_end` (0x4B7630) | End script of an NPC, next entry; past the end directly: end turn, no second start; past the end through the skip loop: end turn, then the first subturn starts again (A9). | A9 confirmed (original bug) |
| TB8 | `combat_turn_based_end_turn` (0x4B7740) | Game and animation time + 1000 ms, then begin turn. | Original |
| TB9 | `combat_check_action_points` (0x4B7790), `combat_consume_action_points` (0x4B7CD0) | Covered → paid. Else the **local** PC with action points > 0 and current fatigue > 1 takes 2 fatigue damage through `combat_dmg` (no attacker, no blood splotch) and then the action points are set to 0, whoever's turn it now is (**A12**: a knockout in that damage step already passed the turn on); anyone else: 0 and next subturn. | Original; A12 an original bug |
| TB10 | `combat_move_cost` (0x4B7BA0), `combat_attack_cost` (0x4B7C30), `combat_check_attack` (0x4B78D0), `combat_check_use_obj` (0x4B79A0), `combat_check_pick_item` (0x4B7A20), `combat_check_cast_spell` (0x4B7AA0), `combat_check_use_skill` (0x4B7AE0), `item_weapon_magic_speed` (0x465CF0) | As CE: 2 per step (1 for a PC with always-run), attack 1/2/`max(1, 8 − speed/3)`, reach-1 attack adds the adjacent move, use 2, pick up 1, cast and skill 4; weapon speed = magic adjustment + 5 with apprentice training + speed factor, 10 unarmed. An unreferenced function at 0x4B7B20 (4 + move) is dead code. | Original |
| TB11 | `combat_turn_based_end_critter_turn` (0x4B7C90), `combat_turn_based_add_critter` (0x4B7E00) | End: action points 0, next subturn. Add: appended (a warning for a hidden awake critter), sorted, the current node unchanged. | Original |
| TB12 | `combat_save` (0x4B1EA0), `combat_load` (0x4B2020) | Saves setting, active flag, turn counter, then action points, an unused handle, the current actor, the required action points and the last actor. Load restarts the round (fresh scan, counter back to 1), restores the action points and the current actor, resets the required action points; it fails when the actor is not found again. | Original (the session keeps the saved list instead, see the contract) |
| TB13 | `object_list_rect` (0x440B40), `sector_rect_from_loc_rect` (0x4D0090), `obj_find_node_attach` (0x4E3D30) | Scan order (O2): sector rows by increasing y, sectors by increasing x, inside a sector newest-first (attach prepends; re-attached only on a sector change); destroyed, off and inventory objects excluded. | Original |
| TB14 | `sub_430FC0` (0x430FC0), `sub_4348E0` (0x4348E0) | Step: walk 2, run 1 with fatigue > 0 (else walk); the run fatigue charge is A4. Gate: false when inactive; true outside a round; false off-turn; an NPC that cannot pay needs `100 × fatigue / max > 35`; a maximum of 0 divides by zero (both rule modes avoid the fault). | Original (A4 a CE slip; the division a fault fixed in both modes) |
| TB15 | `timeevent_ping` (0x45B370) | In single player game time advances only while no round is active (`combat_turn_based_is_active` 0x4B6D70); animation time always. | Original (first reader only) |

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
| SN1 (ARC-SCRIPT-011) | `script_name_init` (0x4CEC10), `script_name_mod_load` (0x4CECE0), `script_name_build_scr_name` (0x4CEDC0), `script_name_build_dlg_name` (0x4CEE60) | As CE: the `scr\*.scr` listing maps `atoi(name)` ≥ 1000 at game start and 1..999 at module load, a later name replaces an earlier one with an error message; lookup takes ≥ 1000 from the first table and 1..999 from the second; the DLG name swaps the first and last three characters for `dlg`. | Original |
| DL1 (ARC-SCRIPT-012) | `dialog_load` (0x412E10), `dialog_parse_field` (0x417C20), `dialog_parse_entry` (0x417860) | As CE and the DLG original read mode: a field is found by skipping to the next `{` (`//` comments skipped), capped at 999 characters; id, IQ and response read with `atoi`; a non-empty IQ that reads 0 and a response starting with `#` end the read; a file with no record is not opened. | Original |
| DF1 (ARC-DATA-022) | `dialog_load` (0x412E10) over four stone-examination DLGs | Data defect: the only record lacks its effects field, no record is read, the file is not opened and the print line shows nothing. | Original bug (data) |
| DF2 (ARC-DATA-022) | `dialog_parse_entry` (0x417860), NPC line 315 of `01306benjamin_gershwin.dlg` | Data defect: the line's effects are in its response field, which an NPC line never uses; they are never applied. | Original bug (data) |
| DF3 (ARC-DATA-022) | `dialog_parse_entry` (0x417860), reply 361 of `01009mayor_blackroot.dlg` | Data defect: the final-line action is in the response field; `atoi` reads 0 and the reply ends the talk. | Original bug (data) |

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
| LA4 (c), building U4 | `ai_is_indoor_to_outdoor_transition` (0x4AECA0) | The "opposite direction" branch really **swaps** the portal and far tiles; CE copies the far tile into both, so in CE that branch never counts as leaving. In the original a walker coming from across counts as leaving when its tile is indoor and the portal's tile is not. Side effect: on the portal's own tile (direction 5) a portal of raw rotation 0–2 is tested in reverse, so an outdoor walker there passes as "leaving". What this means for a walking step: [LV1–LV5](#leaving-a-building-through-a-locked-portal-arc-res-049). | CE slip; the direction-5 side effect is an original bug |
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

## Leaving a building through a locked portal (ARC-RES-049)

Source: ledger row LA4 (c) above, [locomotion](locomotion-evidence.md#windows-s-o-d) W3 and D3,
[first building](first-building-evidence.md#door-admission-fr-scene-014) and the whole-map step rule
of ARC-WORLD-018. Question: when does the walking player pass a closed, locked portal it has no key
for, by the "leaving a building" shortcut? Two readers (this task's and a fresh second reader that
did not see these verdicts) read every row below in the executable and agree on every observation;
only the internals of `object_locked_get`, the key check and `tile_art_id_at` were taken from CE.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LV1 | `ai_is_indoor_to_outdoor_transition` (0x4AECA0), `tig_art_tile_id_type_get` (0x5038C0) | Compares the portal's tile P with Q, the tile one step along the portal's **raw** ART rotation r (the diagonal tile for an even r; false off the map). When the direction argument is r + 3, r + 4 or r + 5 (mod 8) it really exchanges P and Q. Leaving means the first tile is indoor and the second is not; indoor is tile art with bit 8 clear or facade art with bit 25 clear, and any other art counts as indoor. | CE slip (CE copies Q into both); confirms LA4 (c) |
| LV2 | `ai_attempt_open_portal` (0x4AEB70) | For the player: busted passes; not a critter refuses; the unlock script may refuse; jammed, magically held refuse; unless always-locked, leaving (with the caller's direction, unchanged) passes; not locked passes; otherwise the key decides. | Original |
| LV3 | `object_calc_traversal_cost_func` (0x43FE00) | A diagonal step is four cardinal legs, all of which must be clear. A closed portal counts on a leg's start tile when its edge (r \| 1) is the leg's direction and on the leg's destination tile when its edge + 4 is; both pass the **leg's direction** to the open attempt, so the leaving test exchanges P and Q exactly when the leg enters P from across. | Original |
| LV4 | `sub_4272E0` (0x4272E0), `object_traversal_check_blocking_door` (0x440700), door goals 0x428550, 0x428690, 0x428750, window jump `sub_4288A0`, `object_rot` (0x441B20), `location_rot` (0x4B8D50) | Before the step the walk only finds the closed portal from the walker's tile; the open-door and jump goals then repeat the open attempt with the direction from the walker's tile to P (`location_rot`, the exact inverse of `location_in_dir` for neighbours, 5 on the same tile). | Original |
| LV5 | the walk as a whole | A step passes only when both attempts call it leaving. Out of P: r = 3, 5, 7 when P is indoor and the tile across is not; r = 4, 6 when P is indoor and the diagonal Q is not (the tile across is never read); r = 0, 1, 2 **never** (the traversal wants P indoor and Q outdoor, the goal on P tests the reverse). Into P from across: both attempts exchange, so it passes when Q is indoor and P is not (Q is the tile across for an odd r). | Original; the direction-5 side effect of LA4 (c) never opens a walking step (the traversal refuses it first) |

**Recount (start map, opt-in probes on the owner's install).** The 1,138 window edges give exactly
the ARC-RES-047 census (locomotion report digest unchanged before the door count was added): 324
jumpable by day, the 18 rotation-1 windows still refused, now because the two attempts disagree.
There are 215 closed locked doors the shortcut can apply to (no unlock script; not busted, jammed,
held, never- or always-locked), all of odd rotation: 150 open when leaving from the portal's tile, as
before; none from across, because no such door stands on an outdoor tile with an indoor tile across; 4 of rotation 1
stand on an indoor tile with outdoor beyond and stay shut. The [first house](first-building-evidence.md)
door is unchanged. On the 54 complete interiors 48 stay walkable from their start with the same 22
window-jump steps. The step evidence now decides these portals by the step (LV5) instead of leaving
them Unknown ([area step assessment](../world/area-step-assessment.md#whole-maps-arc-world-018)).

## Rendering: body blends and wading (ARC-RENDER-018)

Source: the [viewer map mode](../rendering/area-map-mode.md#body-blends-and-wading-arc-render-018).
Both readings (this task's reader and a fresh second reader) agree on every row.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| BL1 | `object_setup_blit` (0x443170) | The body's blend is chosen in this order: frozen (additive with a constant colour), blit additive `0x10`, blit multiply `0x40`, blit constant alpha `0x100`, otherwise the light-derived render flags. Constant alpha replaces the flags with that mode alone and copies the serialized blit alpha into the render alpha only when the incoming blit word carries no alpha mode; on the ordinary body draw that word holds the shadow pass's subtract-and-colour value, so the test passes. The serialized blit alpha is read nowhere else. Multiply and constant-alpha bodies keep no colour or palette-tint mode. | Original |
| BL2 | `sub_442520` (0x442520) | A body whose blit word has additive, subtract or multiply loses every colour, palette and alpha mode of its render flags and its render palette: it is drawn with the base palette, unlit. | Original |
| BL3 | `object_draw` (0x43B390), wading branch | Wading (`OF_WADING` without `OF_WATER_WALKING`): an upright body's lowest 15 destination rows are drawn with the alpha modes replaced by constant alpha 92, its other modes kept, then the rest of the body normally; a flat wading body is drawn whole at alpha 92 and not again. | Original |
| BL4 | `object_draw`, scaled source rectangles | A scaled body's source rectangle is the destination one divided by scale/100 and **rounded to nearest**; CE truncates. The Steamwoven renderer maps whole frames with its own nearest-pixel rule, so nothing depends on it. | CE slip (no consumer) |
| BL5 | 16-bit blits for constant alpha and multiply (0x50742A, 0x506B87; stretched 0x510F44, 0x5104A6) | Constant alpha is the source's weight per channel (`dst + (src − dst)·alpha / 256`); multiply is the per-channel product of source and destination; index-0 source pixels are skipped. | Original |

## Equipment (ARC-WORLD-028)

Source: [equipment runtime](../world/equipment-runtime.md) and the item requirements
[FR-ITEM-005..007](../requirements/items-economy.md#fr-item-005). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below, and both found EQ1.
EQ7 was read the same by both; the second reader adds that `item_is_identified` answers true for complexity ≤ 0, so CE's
differently written gate has the same effect. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| EQ1 | `sub_464D20` (0x464D20) | The wear check, in order: a critter spell flag of polymorph or a "body of" form → not wieldable; slot 1002 read as 1001; complexity > 0 with background 69 (Magick Allergy) → no magick items; the destroyed bit of the item's current art → broken; `item_location_get` ≠ slot → wrong type. Weapon: two-handed with a fixed hand count → no free hand with a shield, crippled with either arm flag; other weapons crippled only with both arms; then the critter art with the weapon's subtype must exist. Shield: no free hand next to a fixed two-handed weapon; **crippled with either arm flag**; art only for an armour shield. Body armour: the race's size bit, male-only before female-only, unique NPC art, then the art. No minimum-strength test. | Original, except the shield: CE tests only "both arms crippled", a CE slip; both settings refuse a shield with one crippled arm |
| EQ2 | `sub_465010` (0x465010) | Always allows (the hexed-item refusal is never returned). | Original |
| EQ3 | `item_check_remove` (0x466DA0) | The no-drop item flag (0x20) refuses first; a worn item then asks EQ2. | Original |
| EQ4 | `sub_464C80` (0x464C80) | Taking a worn item off: remove check, insert check into the owner, remove, insert at the free grid place; a non-worn item: true, unchanged. | Original |
| EQ5 | `item_wield_set` (0x464AE0) | Same item in the slot: true. Then the wear check, the item's remove check, the occupant's remove check, both removed, the occupant's insert check (on failure both go back where they were), the occupant into the grid, the item into the slot. Last, for slot 1003 (medallion) only, the animation speed is recalculated **for the item**, not the wearer. | Original; the medallion recalculation is a bug of the original (an item has no running animation, so it does nothing), no session effect, no switch |
| EQ6 | `item_location_get` (0x4655D0), `item_armor_coverage` (0x465C30), `tig_art_item_id_armor_coverage_get` (0x504570) | Armour by the coverage bits 14..16 of its inventory art (torso 1006, shield 1005, helmet 1000, gauntlets 1007, boots 1008, ring 1001, medallion 1003), a weapon 1004, a generic item with the torch/shield flag 1005, else −1; an art that is not item art reads coverage 0. | Original |
| EQ7 | `item_armor_ac_adj` (0x465BA0) | Not armour: 0. AC adjustment, plus the magic AC adjustment (scaled with an owner) unless the "unidentified" argument is set for an unidentified magickal item. Combat passes 0, so the magic part always counts. | Original |
| EQ8 | `object_get_resistance` (0x43D6D0) | Base, then for a critter each worn armour item's adjustment and scaled magic adjustment (same gate), the poison bonus `5 × (CN − 4)` when positive, effects; a monstrous NPC unclamped, else 0..95. | Original |
| EQ9 | `item_effective_power` (0x4614A0), `item_effective_power_ratio` (0x461540), `item_adjust_magic` (0x461590) | Power `(complexity + aptitude) / 2` with signed truncation, clamped to 0..complexity on the item's side (a non-critter owner gets the complexity); ratio 100 for a neutral item, else `100 × power / complexity`; adjustment `value × power / complexity` (magickal), `value − value × power / complexity` (technological), unchanged (neutral). | Original |
| EQ10 | `item_weapon_magic_speed` (0x465CF0) | No item 10; the magic speed adjustment scaled for an owner, + 5 at apprentice training in the weapon's skill, + the speed factor. | Original (as TB10) |
| EQ11 | `item_weapon_range` (0x465E30), `item_weapon_min_strength` (0x465E90) | Not a weapon: 1. Range + scaled magic range. Minimum strength + magic adjustment (scaled with a critter); without the fixed hand count, two-handed with a shield +4, one-handed without one −2. | Original |
| EQ12 | `item_recalc_light` (0x467E80) | The worn items' light flags, each shifted by its effective power ratio (≤ 25 by 3, ≤ 50 by 2, ≤ 75 by 1), are ORed; the critter keeps **only the largest** size flag. | CE slip (CE sets every size flag); the light drawn is the same (the largest wins in both) |
| EQ13 | `item_weapon_damage` (0x465F70), unarmed part | Bare-handed melee reads the unarmed bonus of whatever item is in slot 1007, without a type test; normal and fatigue `max(1, bonus − 25) .. bonus + 5`, other types 0; a monstrous critter its own table. | Original |
| EQ14 | race to armour size table (0x5B32C0) | Human, elf, half-elf, half-orc, dark elf, orc medium; dwarf, gnome, halfling small; half-ogre, ogre large. | Original |

## Art names and missing art (ARC-RES-050)

Source: [whole-map art gaps](whole-map-art-gaps.md#how-the-original-names-critter-art-and-falls-back).
Read on 2026-10-17 by a first reader and, independently, a second reader that saw only the questions
(method step 6); both agree on every behaviour and verdict of MA1–MA5 and MA7. Every direct branch of the functions read for those rows stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MA1 | `fix_missing_art` (0x41DA40), tables 0x5A106C, 0x5A10B8, 0x5A1104, 0x5A1150 | Nothing happens before the masks are loaded or for a number outside the list. The 19 checked pairs are tried in order: a set bit whose animation equals the current one (and, for low attack 21 only, whose weapon equals it) replaces the animation (walk, run → walk; stunned, stand → stand; ten low attacks → attack 20; decapitation, blown-out chunk, severed leg, explode → fall down; stealth walk → walk). A walk replacement sets weapon 0 when walk unarmed is missing; a stand replacement with weapon 0 sets weapon 1 when stand without a weapon is missing. Tables as CE. | Original |
| MA2 | `name_normalize_aid` (0x41D510), critter, monster and unique NPC branches | Critters: elf female → human body; plate armour (4, 6) → male, elf → human, halfling → dwarf. Then the repair (MA1), then: explode clears armour, shield and weapon; stunned sets weapon 1 and clears the shield; weapon 1 with stealth walk or conceal fidget → weapon 0; animations 6–19 clear weapon and shield; walk clears the shield. The id is rebuilt with frame, rotation and palette 0. Monsters and unique NPCs: the same without the body rules. On explode the critter branch sets the **male** gender where CE writes female; the explode file name ignores the gender, so nothing drawn changes. | Original; the explode gender is a CE slip with no consumer |
| MA3 | `name_resolve_path` (0x41DAE0) | Normalizes the id (MA2) before building any name; monster and unique NPC names are folder and file name from the two name lists, armour code (monster only), shield `X`/`S`, weapon letter (two-handed sword with a shield written as sword) and animation letter. | Original |
| MA4 | missing-art table load (0x41D1F0), init (0x41CE60) | `art\missing.dat`: a header that must equal the current count of critter, monster and unique NPC ART files, then 10 critter masks, one per monster and one per unique NPC (list counts). A short read or a header mismatch rebuilds the masks by checking each pair's file and saves the file. The unique NPC read is compared with the unique count; CE compares it with the monster count. | Original; CE slip in the count check |
| MA5 | art cache load (0x51AA90) | When the resolved file cannot be loaded it logs the failure and loads `art\badart.art` under the same art id; only when that fails too does it return no entry (nothing drawn). The frame lookup that follows (CE `tig_art_frame_data`, not read in the executable) indexes the loaded file's frame table with the requested rotation and frame, unchecked; `badart.art` has one frame. | Original (load fallback); frame lookup CE reading |
| MA6 | art cache entry load (0x51B170) | A one-rotation ART (flag 1) serves every rotation from its only group. | Not checked (already the whole-map rule of ARC-DATA-016, read in CE) |
| MA7 | `a_name_wall_aid_to_fname` (0x4ECEB0), `build_wall_file_name` (0x4ECFC0) | At rotations 2, 3, 6, 7 the damage bits `0x400` and `0x80` swap. `0x400` turns piece 7 into piece 0 and names `L`; `0x80` turns piece 8 into piece 0 and names `L` for pieces **1–6**, else `R`; no damage names `U`. Interior name for rotations 0, 1, 6, 7, else exterior. CE writes pieces 2–6 for `L`. | CE slip (piece 1) |
| MA8 | `object_draw` (0x43B390), `a_name_tile_aid_to_fname` (0x4EAE90) | Not read in the executable: in CE neither the object draw nor the resolver treats a tile id on an object differently from other art; rotation and frame of a tile id read 0. | Not checked (CE reading only) |

## Durable effects (ARC-WORLD-029)

Source: [durable effects runtime](../world/durable-effects-runtime.md) and
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below. Add, the typed removal
and the hit-point setter begin with a network branch that CE leaves out; a single player game never takes it. Every direct
branch stays inside `.text`; the only indirect one, the aggregation's operator table, stays inside its function.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| EF1 | `effect_add` (0x4E9F70) | Only a PC or NPC; no bound on the number, the cause or the list length. Records strength, the encumbrance level, maximum hit points and maximum fatigue, appends number and cause, recalculates encumbrance when strength changed, adds each maximum's non-zero difference to its damage through the setter, recalculates the animation speed. | Original |
| EF2 | `effect_remove_one_typed` (0x4EA100) | The first entry with the number, whatever its cause; the animation speed is recalculated on every path, also when nothing matched. | Original |
| EF3 | `effect_remove_all_typed` (0x4EA200), `effect_remove_one_caused_by` (0x4EA2E0), `effect_remove_all_caused_by` (0x4EA3C0) | Equality on the number or the cause; the "all" forms step back after each removal. None recalculates the animation speed. | Original (presentation only, no session effect) |
| EF4 | `effect_count_effects_of_type` (0x4EA4A0) | Entries with the number; 0 for a non-critter. | Original |
| EF5 | `effect_remove_internal` (0x4EA520) | Moves each later entry up in both arrays in one loop, shortens both by one, then the same strength and maxima handling as EF1; no animation speed. | Original |
| EF6 | `effect_adjust_func` (0x4EA6C0) | The no-innate form skips race, background, class and gender. Adds and percents summed, multiply and divide factors multiplied, the highest minimum and the lowest maximum (−1 means unset, so a real −1 cannot be told apart); applied as percent, multiply, divide, add, minimum, maximum. Effect numbers are not bounds-checked against the 400-entry tables. | Original (the session's aggregation keeps the −1 mark; no installed rule uses −1); a number above 399 reads past the tables (both modes refuse it, owner decision on out-of-bounds access) |
| EF7 | `object_hp_damage_set` (0x43D530), `critter_fatigue_damage_set` (0x45D5A0) | Both clamp a negative value to 0. Hit points: a positive value schedules the resting timer; no unconsciousness or death logic. Fatigue: a non-zero value (not on a prototype) schedules the 80-second recovery, and in that branch only a critter that just became unconscious drops its maintained spells, notifies its items, is knocked down and ends its turn. | Original |
| EF8 | `object_hp_max` (0x43D5A0), `critter_fatigue_max` (0x45D670) | The effect adjustment applies to the whole sum: points, adjustment and the stat part. | Original |
| EF9 | `MTComponentEffect_ProcFunc` (0x451F20) | Count plus `scaled × parent aptitude / 100` (truncated) for a PC or NPC parent; adds that many entries, or removes as many with EF2; nothing at 0 or below. | Original |
| EF10 | EF1, EF5 and EF7 together | An effect that lowers a maximum so far that the clamped damage write is 0 leaves the critter at no fatigue (or no hit points) with neither the faint consequences nor any death handling. | Original bug; no fix recorded, both modes follow it (the session reports the values and runs no faint or death step for an effect change) |

## Experience and levels (ARC-WORLD-030)

Source: [experience runtime](../world/experience-runtime.md),
[FR-CHAR-013](../requirements/characters-skills.md#fr-char-013) and
[FR-CHAR-014](../requirements/characters-skills.md#fr-char-014). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6). They agree on every row. On XP5 the second reader first named
CE's table index; asked to re-read only that instruction sequence, it found the original's index one lower, as the first
reader and the earlier LM11 and A1 readings did. Several functions begin with a network branch that CE leaves out
(multiplayer XP split, party-wide quest states, a cheat loop over all players); a single-player game never takes it.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| XP1 | `critter_give_xp` (0x45F110) | Only a PC. A gain of exactly 0 returns at once; a negative one goes through. Easy adds half the gain, normal keeps it, **hard takes a quarter away** (truncated toward zero; CE takes half). Then a dead PC gets nothing; otherwise the gain goes through `effect_adjust_xp_gain` (the `xpgain` special) and is added to the base experience through the setter. | CE slip (hard difficulty); the rest Original |
| XP2 | `stat_base_set` (0x4B0980) | Experience is clamped to 0..2,000,000,000, unspent points to 0..56, the level to 0..51. Every experience write runs `level_recalc` and refreshes the character sheet, whether or not the value changed; level and unspent points have no side effect. | Original |
| XP3 | `level_experience_points_to_next_level` (0x4A68B0), `level_progress_towards_next_level` (0x4A6900) | Both read the effective level. To-next: 999,999 at level 51 or above, else the next level's threshold minus the experience. Progress: 0 at 51 or above; 999 for level 0, an empty threshold or a negative to-next; else `1000 − 1000 × to-next / step`, truncated. | Original |
| XP4 | `calculate_bonus_character_points` (0x4A6980) | One point per level from old + 1 to new, one more on every fifth level; no cap of its own. | Original |
| XP5 | `level_recalc` (0x4A69C0) | Only a PC; returns when the base level is at the maximum (51) or the next level is not reached. Each pass raises the level by one: the normal branch writes it, adds its points and shows the level message (or runs the auto-level scheme); when the new level would be 51, it writes the threshold of **level 50** (table index new level − 2; CE: − 1) as the experience, writes no level, grants nothing. That write re-enters the function, which finds level 51 not reached and returns: no recursion. Followers and nearby waiting followers then get (old level, the last level reached), which is 51 in that branch although the PC stays at 50. | Cap at 50 Original; XP value and no recursion: CE slip (as LM11, A1) |
| XP6 | `level_init` (0x4A6620) | 51 entries from messages 1..51 of `rules\xp_level.mes` by `atoi`, 0 for a missing message, then entry 0 forced to 0. | Original |
| XP7 | `update_follower_level` (0x4A6CB0) | Leaves a follower at or above the PC's **new** level (CE tests the follower's maximum); otherwise a follower at or below the PC's old level keeps its gap, one above it moves to the PC's new level, capped at its maximum − 1 (50); a rise writes the level, adds the points (unspent cap 56) and runs the NPC's auto-level scheme. | CE slip in the first test only; the same result for every step `level_recalc` passes (new above old) |
| XP8 | `quest_state_set` (0x4C4D20), `quest_get_xp` (0x4C53C0) | After the completed/botched and not-higher refusals, a request for Completed gives the PC `quest_get_xp` of the quest's experience level **before** the state is stored; `quest_get_xp` returns the message of that number in `rules\xp_quest.mes`, 0 when missing. | Original |
| XP9 | `critter_notify_killed` (0x45DA20) | For an NPC victim with a killer: the killer if it is a PC, else the killer's PC leader, gets `worth × 20 / 100` (truncated), and only then the victim's worth is set to 0; without a PC recipient nothing is given and the worth stays. | Original |
| XP10 | `combat_dmg` (0x4B4390), the pool block | With an attacker, an NPC target not turned to stone and a positive pool: percentage = `100 × damage / maximum hit points`, award = `worth × percentage / 100` (both truncated, 32-bit), at most the pool; the pool loses the award whoever attacked; only a PC attacker gets it (no leader redirection). It runs before the kill award, so a killing blow pays both. | Original |
| XP11 | call sites of `critter_give_xp` and `quest_get_xp` | Dialog action XP (0x415BA0) and the script action that gives quest experience (0x447220) both give `quest_get_xp(value)`; quest completion (XP8); kill (XP9); pool (XP10); a cheat key gives the experience still needed. | Original |
| XP12 | data (`DAT_ROOT`) | The winning `rules\xp_level.mes` (the `Arcanum4.dat` copy) has 51 strictly increasing entries, so XP5's re-entered call returns; `xp_quest.mes` has levels 1..50, `xp_critter.mes` 0..50, every quest level in `gamequest.mes` has quest experience, and one effect rule uses `xpgain`. | Data |

## Decisions to revisit

These owner decisions or accepted proposals rest on a CE fact that the original does not share.
Nothing below is changed by this ledger; each needs the owner's word or its owning slice.

1. **Reputation D1** ([reaction study](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions)):
   the default "source parse" (signed effects get origin = faction = adjustment) is a CE slip. The
   original keeps the written scope, which is the other reading D1 offered; under D8 a CE slip gets
   no switch. **Decided 2026-10-17:** the owner chose the written scope in both settings.
2. **Dodge 5 × level** ([attack resolver](../world/attack-resolver-runtime.md), ARC-COMBAT-001,
   `AttackRules.DodgeEffectiveness`): implemented from CE; the original uses 2 × level. Under D8 both
   settings follow the original; ARC-WORLD-023 changed the resolver to 2 × level.
3. **Skills §9.5** ([skills study](skills-evidence.md#9-consequences-for-steamwoven-project-policy-proposals)):
   SK1, SK2, SK4 and SK5 are CE slips, so the Original setting reproduces none of them; SK2's "one point
   per click" becomes a design choice, not a fix. SK8 stays a switch candidate; SK7 is the original's
   code.
4. **Window jump and door "leaving" test** ([locomotion](locomotion-evidence.md#anomalies) LA4/W3/D3,
   [first building](first-building-evidence.md) U4, ARC-WORLD-018 door admission and the research
   probes): the original swaps the tiles. **Settled by ARC-RES-049** ([rows LV1–LV5](#leaving-a-building-through-a-locked-portal-arc-res-049)):
   the start-map counts do not change, and the step evidence decides these portals as the original
   does. The direction-5 side effect never opens a walking step; it stays a switch candidate only
   where a goal attempt runs without the traversal (using a door directly, not traced).
5. **SCR S2 choices** ([census](scr-census.md#anomalies-for-s1-s)): X7 (quotient, not sum) and X8
   (masked counters) are settled by the original, so neither is an Original-setting option; X10 stays a
   switch candidate. Implemented by ARC-SCRIPT-008 ([dispatcher core](../scripting/scr-dispatcher-runtime.md)):
   X7 and X8 as the original in both modes, X10 and X3 tile as Original switches. X6: the S1 admission refusal of negative gotos (`NegativeGoto`) is stricter than
   the original, which reads −1..−4 as control codes; whether the Original setting admits them is
   open (the census decides whether any data uses them).
6. **Tile script 2498 self-removal** ([Virgil script attachments](virgil-script-attachments.md),
   ARC-RES-037): holds only in CE; in the original the removal does not persist. ARC-SCRIPT-008: it persists in
   Steamwoven (D2 fix) and not in the Original setting (`TileScriptChangesDiscarded`).
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

- Data questions: wilderness A4/A5, P7's label in `ai_params.mes`, how often shipped scripts reach
  SK1. (`xp_level.mes` entries 50/51 behind A1 are answered by [XP12](#experience-and-levels-arc-world-030): the table is
  strictly increasing.) (LM15 labels and the LM17 keys
  are answered by the [combat-domain data census](combat-data-census.md): the description header's
  labels match the art, and no shipped `Resist:` uses an LM17 key.)
- Code left for later: wilderness A2 (editor only); locomotion W4 and the frame-time clamp;
  companion C2, C4, C10, C13 frequency; world-transition jump points and teleports; reputation A6
  and A7; the first-tick marker after a load (OQ2, H1).
