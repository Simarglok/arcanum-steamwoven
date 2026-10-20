# Original verification ledger

Status: **Research**, work item ARC-RES-048 (2026-10-17); later rows from the tasks named in their sections. One place that records, for every
game-mechanics anomaly and decision input this project took from an earlier source reading, what the retail
1.0.7.4 executable does at the same function. It applies the
[original executable check](original-executable-check.md) to the decisions of the domain studies,
and its verdicts decide which differences become switches of the two rule settings of owner
decision D8 (see [the AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16)).

The combat-core anomalies A1–A10 were checked by ARC-COMBAT-001 and are not repeated here (A11 and A12 are in
[Turn-based round](#turn-based-round-arc-combat-003); the item wear rules are in [Equipment](#equipment-arc-world-028); critter art names and missing art in [Art names](#art-names-and-missing-art-arc-res-050); the effect list is in [Durable effects](#durable-effects-arc-world-029), blessings and curses in [Blessings and curses](#blessings-and-curses-arc-world-035); experience and levels in [Experience and levels](#experience-and-levels-arc-world-030); auto-level schemes in [Auto-level schemes](#auto-level-schemes-arc-world-036); the skill state rules in [Skill rules](#skill-rules-arc-skills-001), the hero's skill reads in [Hero skills](#hero-skills-arc-skills-002), the dialog skill cases in [Dialog skill gates](#dialog-skill-gates-arc-skills-004); the prototype load phase in [Prototype phase](#prototype-phase-arc-data-026); the phase of other rule files in [Load phases](#load-phases-arc-data-027); `map.patch` in [Map patch](#map-patch-arc-data-028); audio in [Audio](#audio-arc-res-052); the logbook in [Logbook](#logbook-arc-ui-008); the map list size in [Limits census](#limits-census-arc-data-029); sector residency and loading in [Main-map streaming](#main-map-streaming-arc-data-030); terrain types and template paths in [Terrain templates](#terrain-templates-arc-data-031); the AI parameter rows, tick, gate and danger setter in [NPC AI core loop](#npc-ai-core-loop-arc-res-054); the implemented functions that stood at F0 in [Implemented functions](#implemented-functions-arc-dev-025); flee, surrender and back-off in [NPC AI morale](#npc-ai-morale-arc-res-058); hostility, targeting, the enemy list and the attack fan-out in [NPC AI hostility and targeting](#npc-ai-hostility-and-targeting-arc-res-057); sight, hearing, line of fire and witnesses in [NPC AI awareness](#npc-ai-awareness-arc-res-056); healing, the AI action lists, spell and item eligibility, the item search and grenades in [NPC AI support and magic](#npc-ai-support-and-magic-arc-res-059); tech degrees, schematics, assembly, tech items and item durability in [Technology](#technology-arc-res-062); the schematic and tech text data in [Schematic data](#schematic-data-arc-technology-001); item durability in [Item durability](#item-durability-arc-world-053); the spell record parse in [Spell records](#spell-records-arc-magic-001) and its components in [Spell components](#spell-components-arc-magic-002); spell cost and payment in [Spell cost and payment](#spell-cost-and-payment-arc-magic-003); companion orders, loyalty, the wait expiry, the level-up message, script action 139 and follower links across maps in [Companion orders and loyalty](#companion-orders-and-loyalty-arc-res-064); the character editor's point buys and refunds in [Character point pool](#character-point-pool-arc-world-052); how AI actions become goals, movement and attacks in [NPC AI action execution](#npc-ai-action-execution-arc-res-066))
([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). Domain
documents keep their own tables; where a row below contradicts a domain document, this ledger is
the newer evidence, and the domain document's "likely intent" column is superseded by the verdict.
Owner decisions that rest on a contradicted fact are listed under
[Decisions to revisit](#decisions-to-revisit) and stay unchanged until the owner answers.

## Inputs and method

- The owner's unchanged GOG install, `Arcanum.exe` 1.0.7.4, SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52` (re-hashed by every reader).
- The questions and the function addresses came from an earlier reading of a source reconstruction that
  the project no longer uses (owner decision 2026-10-18; its provenance is in git history). Function names
  in backticks are labels from that reading; the address is the reference.
- Read only with Xcode's `llvm-objdump` at those addresses, following the method's steps. Every
  function named below keeps its direct branches inside `.text`; none reaches the GOG `.IIDKing`
  section, so none is patched.
- Two independent readings (method step 6): a first reader per domain, then a second reader that
  did not see the first verdicts and re-read the rows that set or change a decision (most reconstruction slips
  and original bugs, and every row flagged as a decision input). [Second reading](#second-reading)
  lists the rows read once, the agreement and the one difference in classification.
- No bytes, listings or decompiled code are stored; scratch output stayed in the session
  scratchpad. Addresses are the retail virtual addresses.

## Verdicts and what they mean for the two settings

| Verdict | Meaning | Rule settings (D8) |
| --- | --- | --- |
| **Reconstruction slip** | The original differs from the earlier reading, which was wrong. | No switch: Steamwoven and Original both follow the original. |
| **Original bug** | The original does what the earlier reading said, and it looks unintended. | Switch candidate: Original keeps the bug; Steamwoven fixes it only when the owning slice records a fix under D2. |
| **Original** | The original does what the earlier reading said, and it is plausibly intended (or the "anomaly" was a misreading). | No switch: both settings follow it. |
| **Reconstruction change** | The earlier reading marked a deliberate change to the original; the row states the original. | The original behaviour is the Original setting; whether Steamwoven keeps the change is a D2 decision. |
| **Not checked** | No code to compare (data, labels, editor-only) or out of scope; the reason is given. | Unchanged. |

## Magic (ARC-RES-043, load map ARC-RES-040)

Source: [magic core](magic-core-evidence.md#anomalies-recorded-not-adopted),
[load map](combat-domain-load-map.md#8-anomalies-found-by-this-reading),
[reaction](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MG1 | `sub_450940` (0x450940), from `sub_429B50` | The pre-animation affordability check reads the **cast spell's** cost (the run's spell field) and maintenance cost; no read past the 223-entry table. It applies no mastery. | Reconstruction slip (spell index); no mastery is Original |
| MG2 | `sub_4507D0` (0x4507D0), `spell_cast_cost` (0x4B1660), 0x4508A0 | Run time: a mastered cost is halved and rounded **up** (`ceil(cost / 2)`; the odd test reads the cost before halving), then cheat 1, then dwarf ×2. `spell_cast_cost` halves with truncation and the UI check applies no mastery, so odd costs still disagree between the three. Cost 6, mastered dwarf: 6 at run time, not 8. | Reconstruction slip (run-time rounding); three formulas Original |
| MG3 | `sub_452F20` maintain branch, `sub_4532F0`, `sub_453370` | A tick with a resistance margin charges the plain cost and then the surcharged cost. | Original bug |
| MG4 | `sub_4537B0` (0x4537B0) | The resistance term is subtracted from the save, against the manual's "+1 per 10 %". | Original bug |
| MG5 | `magictech_invocation_check` (0x4564E0) | The tech-aligned target test compares a d100 with a negative value: never fires. | Original bug |
| MG6 | `sub_455250` (0x455250) | Absolute aptitude difference tested for `< 0`: the time scaling never applies. | Original bug |
| MG7 | `MTComponentInterrupt_ProcFunc` (0x452300), `sub_4573D0` | The interrupt invocation has no source, so the search returns at once: Interrupt does nothing. | Original bug |
| MG8 | `MTComponentSummon_ProcFunc` (0x452830), `magictech_effect_summon` (0x450C40) | The faction switch is fed **`Clear_Faction:`**; `List:` only picks the prototype. | Reconstruction slip |
| MG9 | `magictech_pick_proto_from_list` (0x452900) | Roll 0..100 over weights summing to 100/120 (the tables the magic study lists); 100 falls off the end to entry 0 with a log. | Original bug |
| MG10 | `magictech_component_trait` (0x452B00), `sub_452CD0` | Elemental polymorph assigns the two flag words; restore clears only those bits. | Original bug |
| MG11 | `sub_450AC0`, `sub_450B40` | The maintained-run counter loops and returns 0. | Original bug |
| MG12 | `spell_ui_maintain_add` (0x57C290) | Signed `≥ count` test lets slot −1 through; `INT / 4` uncapped. Latent. | Original bug |
| MG13 | `sub_4537B0` | Fate save sets margin 10, display 100, and the save still rolls. | Original |
| MG14 | `magictech_cast_spell_fail_chance` (0x453B20) | Predictor differs from the resolver as the magic study says (R / 10, 100 above R 40, a d20 consumed). | Original |
| MG15 | `sub_455550`, `sub_450420` | Reflection charges the global "last cost". | Original bug |
| MG16 | `sub_452F20` begin branch | BEGIN cost paid before the no-stack refusal. | Original |
| MG17 | `magictech_process` (0x4510F0) | Fate maximisation evaluated on every action with components. | Original |
| MG18 | target table at 0x596140 | `Tgt_Obj_No_ST_Critter_Dead` lacks the critter bit. | Original bug |
| MG19 | run creation near 0x455C30, `sub_453FA0` | One run flag means "unresistable" and "final END scheduled"; unresistable spells end after BEGIN. | Original bug |
| MG20 | `sub_450420` | Mana stores drained before the fatigue refusal. | Original |
| Shell and reflection order (O10) | `sub_455550` (0x455550) | The "target is the caster" pass comes **before** the anti-magic shell, so a caster can target itself through its own shell; END actions get no shell or reflection exception. | Reconstruction change |
| Charm bonus (reaction A4) | `magictech_component_obj_flag` (0x454A10), `reaction_adj` (0x4C0DE0) | Charm on gives +30 and charm off −30, both through the PC's good/bad reaction effects, so the asymmetry the reaction study describes is real. Charm off has no animal test: an animal charmed by a PC (no +30) still gets −30. | Reconstruction change (asymmetry confirmed) |
| Summon palette | `magictech_effect_summon`, `sub_452CD0` | The palette applies to the current art only; after a polymorph ends the summon shows the prototype palette. | Reconstruction change |
| Magic resistance source (O9) | `sub_4537B0`, `magictech_cast_spell_fail_chance` | Both read the stored magic resistance directly: no armour, no effects, no 0..95 clamp. | Reconstruction change |
| Effect remove risk | `MTComponentEffect_ProcFunc` (0x451F20), `effect_remove_one_typed` | Removes the first entry with that number, whatever its cause. | Original |
| LM5 | `magictech_min_intelligence` (0x4502E0) | Returns 5. | Original |
| LM6 | `magictech_min_willpower` (0x4502F0) | `IQ:` is the willpower minimum. | Original |
| LM7 | `mt_item_spell_to_magictech_spell` (0x4CB790) | `& 0xFF`: 10000 becomes 16. | Original |
| LM9 | `effect_parse` (0x4E9C20) | `*N` and `/N` parse the number **after** the operator, so no divide by zero; an unknown operator still counts and there is no pair bound. `min`/`max` are told apart by a case-sensitive second letter (the earlier reading's lowercase test is a reconstruction change). | Reconstruction slip (`*`, `/`); rest Original |
| LM10 | `item_effect_mod_load` (0x4D3FF0) | Module maximum overwrites the base and is not restored. Latent. | Original bug |
| LM19 | `magictech_post_load` (0x44F540) | Skipped slots are not cleared (the reset clears them first). | Original |
| LM20 | `bless_mod_load`, `bless_copy_field`, `curse_copy_field`, `mes_search` | No guard on a missing module file; **both** bless and curse copy names unbounded (the earlier reading's bounded bless copy is an unmarked reconstruction change). | Original bug |

## Skills, levels and character (ARC-RES-044, ARC-RES-031, ARC-RES-027)

Source: [skills](skills-evidence.md#8-anomalies-found-by-this-reading),
[load map](combat-domain-load-map.md#8-anomalies-found-by-this-reading),
[Virgil auto-level](virgil-auto-level.md#anomalies-s),
[starting hero](starting-hero-evidence.md).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| Dodge (new, SKX1) | `basic_skill_effectiveness` (0x4C62E0) | Dodge effectiveness is **2 × level**; the earlier reading had 5 × level (the project used it until ARC-WORLD-023). The other basic and tech formulas match the skills study. | Reconstruction slip |
| SK1 | `script_execute_action` (0x447220), get-skill case | Returns after storing the level; no fall-through into "cast spell". | Reconstruction slip |
| SK2 | `skill_ui_dec_skill` (0x57AEB0) | The loop re-reads the points each pass: one click removes points until the level changes and refunds each; no hang. | Reconstruction slip |
| SK3 | `skill_ui_set_training` (0x57AC90) | Routes basic and tech correctly, except that skill 12 (repair) goes to the basic setter, which refuses it. `ui_set_skill_training` is called only from the multiplayer handler. | Reconstruction slip (labels); off-by-one is an original bug, multiplayer only |
| SK4 | `ai_check_use_skill` (0x4AE570) | The lockpick case is pick locks; disarm traps has no check. | Reconstruction slip |
| SK5 | gamble gate `sub_579840` | The Expert and Master gates read the **PC's** gambling training. | Reconstruction slip |
| SK6 | `basic_skill_effectiveness` | Prowling's formula unreachable, backstab 0. | Original |
| SK7 | `skill_invocation_run` (0x4C7160), repair | HP restored and durability lost whatever the roll. | Original |
| SK8 | `dialog_use_skill` (0x419CB0), `anim_goal_use_skill_on` (0x4350F0), `sub_42A2A0` | The paid service's forced-success request is dropped; it rolls like any use. | Original bug |
| LM1 | `item_wield_best` (0x465170) | The damage loop **adds** the five types. | Reconstruction slip |
| LM2 | `item_wield_best` | The best in-range weapon's score is stored; the best in-range weapon wins. | Reconstruction slip |
| LM11 | `level_recalc` (0x4A69C0) | The cap is level 50; reaching 51 sets XP back to the **level-50** threshold (the earlier reading: level-51 threshold). | Cap Original; XP value reconstruction slip |
| A1 (auto-level) | `level_recalc`, `stat_base_set` (0x4B0980) | No recursion (it follows from LM11's XP value). A level-50 PC raises lagging followers by one level (cap 50) each time it earns the gap to the level-51 threshold, matching the 1.0.7.4 update note. Assumes `xp_level.mes` entry 51 > entry 50 (data, not read). | Reconstruction slip |
| A2 (auto-level) | `auto_level_apply` (0x4A7030) | The log append has no length at all (the earlier reading's pointer `sizeof` was its own rewrite). | Reconstruction slip |
| A3 (auto-level) | `auto_level_process_rule` (0x4A7340) | Null token to `atoi` unchecked. Latent. | Original bug |
| A4, A5 (auto-level) | `auto_level_process_rule`, `level_recalc` | Skip-on-failure and no death check in the follower loop. | Original |
| A6 (auto-level) | `object_list_all_followers` (0x441260) | The inner cursor advances. | Reconstruction slip |
| LM16 | `critter_fatigue_adj_set` (0x45D4F0) | Writes fatigue points; never referenced. | Original |
| LM17 | stat key table at 0x5B5384 | Unspent points hold the poison-level key, magick points the age key. | Original bug |
| SH-07 (with LM9) | `effect_parse`, `effect_adjust_func` (0x4EA6C0) | `*2` stores 2, so backgrounds using `*` double the adjustment; order percent, multiply, divide, add, min, max. | Reconstruction slip |
| Character creation | `mainmenu_ui_new_char_button_released` (0x544FF0), `mainmenu_ui_shop_refresh` (0x545E80) | Background effect added only on a button press; with no shop map the game starts before the new-game flag. | Original |
| Pre-made flags (ARC-WORLD-027) | `mainmenu_ui_pregen_char_execute` (0x545B60) | Creates basic prototype 16066 + selected index; for each hero index i below count − 1 reads `MainMenu.mes` 551 + i, `atoi`s it and writes the flag unless the result is **negative** (flag 0 is written; the earlier reading skipped ≤ 0), with 1 only for the chosen hero; then `item_wield_best_all` and the game start. Second reading by a fresh agent agreed. | Flag mapping Original; `> 0` reconstruction slip |

## NPC AI, reaction and companions (ARC-RES-039, ARC-RES-028, ARC-RES-024)

Source: [AI plan](npc-combat-ai-plan.md#first-pass-anomalies-to-confirm-not-adopted),
[load map](combat-domain-load-map.md#8-anomalies-found-by-this-reading),
[reaction](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions),
[companions](companion-follow-evidence.md#open-questions).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| P1 | `ai_look_for_item_func` (0x4A8F90) | Keeps the **nearest** item. | Reconstruction slip (confirmed by two readers, ARC-RES-059 SP12) |
| P2 | `ai_calc_party_size_and_level_internal` (0x4AE0A0) | Sums each follower's own level. | Reconstruction slip (confirmed by two readers, ARC-RES-058 MR2) |
| P3 | `ai_redirect_perform` (0x4A9560) | The MinIQ test reads the **redirect target's** (the spell target's) intelligence, once for the whole vicinity; candidates are filtered by their own critter flags. | Reconstruction slip (not the "candidate's intelligence" the plan expected); confirmed by two readings in ARC-RES-057, which records the target-intelligence test itself as a bug of the original, by inference ([HS17](#npc-ai-hostility-and-targeting-arc-res-057)) |
| P4 | `ai_heal_func` (0x4A8AA0) | The bit tested in the target's critter flags is "mechanical": AI never heals or resurrects mechanical critters. | Original (confirmed by two readers, ARC-RES-059 SP2) |
| P5 | `sub_4A8570`, `combat_turn_based_subturn_end` (0x4B7630) | START/END_COMBAT are per-turn hooks: real time runs END then START every pass in combat mode; turn-based runs START on every pass (before the turn check) and END at the end of the NPC's subturn. | Original |
| P6 | `ai_action_perform` (0x4AC180), `sub_4AABE0`, `sub_57CC70` | FLEE becomes SURRENDER on the next pass, which leaves combat mode while the NPC keeps running; a surrendered NPC refuses dialog with the side it fled from. Consistent design. (The combat-mode bit is set again: CL4, ARC-RES-054.) | Original (confirmed by two readers, ARC-RES-058 MR8, MR11) |
| P7 | `sub_4ABC70` (0x4ABC70) | The defensive list is tried when parameter 13 is below the d100: chance `100 − p13` %. Whether that is a bug depends on the label in the original `ai_params.mes` header (data). | Original (label open; confirmed by two readers, ARC-RES-059 SP4) |
| LM3 | `ai_init` (0x4A8320), `ai_mod_load`, `ai_copy_params` | Rows fill by position from the first entry's number; no bounds check, so a first entry at 100 or above (150 for the module) writes past the table. Latent (no gaps in the data). | Original bug (latent) |
| LM4 | `ai_mod_unload` (0x4A84C0) | Empty: module rows survive. | Original |
| LM8 | `combat_load` (0x4B2020) | Required action points read, then set to 0. | Original |
| LM12 | `background_is_legal` (0x4C2B50), `sub_4C1500` | 8-entry race code table and 11×8 reaction table unchecked. Latent. | Original bug (latent) |
| LM13 | `invensource_get_id_list` (0x4BF5D0) | Accepts `id ≤ count`: the last set is reachable. | Reconstruction slip |
| LM15 | prototype range labels | Comments and labels only; the [data census](combat-data-census.md#5-prototypes) found the description header's labels match the art (27309.. unique NPCs, 28310.. monsters). | Not checked in the executable |
| LM18 | `script_unlock` (0x44C450) | Returns early only **in** editor mode; unlocks in the game. | Reconstruction slip |
| Reputation P1 (D1) | `reputation_parse` (0x4C1730) | The skip between numbers passes digits **and** `-`/`+`, so a signed effect keeps its written origin and faction. | Reconstruction slip |
| Reputation A2 | `ai_attack` (0x4A9650), `reaction_adj` | A deliberate hit on the PC's follower **adds** parameter 9: the reaction rises. | Original bug (likely; owner decided 2026-10-17: Steamwoven lowers, Original switch keeps the rise) |
| Reputation A3 | `magictech_component_obj_flag` | Mind control applies −50 on start and nothing on end. | Original |
| Reputation A5 | `reputation_add` (0x4C1D20), `reputation_reaction_adj` (0x4C1AC0) | No range check. Latent bug if a dialog passes `RP 1`/`RP 2` through. | Original (latent bug) |
| Wait timer (C12, O3, TE8) | `ai_npc_wait` (0x4AA7A0) | The timer is scheduled when the leader's charisma is **not** extraordinary and the NPC is not mind-controlled, and it carries the NPC's handle. Followers of an ordinary leader time out (0, 15 min, 2 h, 1 d, 7 d, 30 d by wait index); Virgil (index 6) never does. | Reconstruction slip |
| Round order (combat O2) | `object_list_rect` (0x440B40) | Sector by sector; inside a sector the critter most recently added or moved in comes first. | Original |

## Reaction runtime (ARC-WORLD-026)

Source: [reaction](npc-reaction-reputation-evidence.md#findings), contract
[NPC reaction runtime](../world/npc-reaction-runtime.md). Read for the reaction owner on 2026-10-17 by a first reader
and, independently, a second reader that saw only the questions (method step 6); both agree on every row, and the
second reader found R1's difference. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| R1 effects | `reaction_get` core (0x4C0D00), `effect_adjust_reaction` (0x4EAA80) | Enemy list → 0; a non-PC or non-NPC → 50; stored value + the race/beauty/reputation term, then the `reaction` effects of the **PC** (the second argument), then a floor of 50 when this PC mind-controls the NPC. The earlier reading passed the NPC to the effect lookup. | Reconstruction slip (effect owner); the rest Original |
| R1 term | `sub_4C1500` (0x4C1500), table 0x5B684C | The race term (row NPC race, column PC race, no bounds check) and the PC's reaction modifier (stat 14) are skipped for an aloof (NPC flag 0x20000) or monstrous NPC; the hostility variant floors them at −49; reputation is always added. The 88 table values equal the earlier reading's (listed in the [reaction runtime](../world/npc-reaction-runtime.md)). | Original |
| R2 | `reaction_adj` (0x4C0DE0) | No-op for 0, wrong types, an NPC this PC mind-controls, and a negative delta while the PC's charisma is extraordinary and it is the NPC's PC leader. The delta always goes through the PC's good/bad adjustment effects (the earlier reading's switch to bypass them for charm, A4, was its own addition) and is added to the stored value; a negative delta while the NPC's direct leader is the PC sets "check leader" and "check reaction bad". A network client returns early (not single player). | Original |
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
| TB10 | `combat_move_cost` (0x4B7BA0), `combat_attack_cost` (0x4B7C30), `combat_check_attack` (0x4B78D0), `combat_check_use_obj` (0x4B79A0), `combat_check_pick_item` (0x4B7A20), `combat_check_cast_spell` (0x4B7AA0), `combat_check_use_skill` (0x4B7AE0), `item_weapon_magic_speed` (0x465CF0) | 2 per step (1 for a PC with always-run), attack 1/2/`max(1, 8 − speed/3)`, reach-1 attack adds the adjacent move, use 2, pick up 1, cast and skill 4; weapon speed = magic adjustment + 5 with apprentice training + speed factor, 10 unarmed. An unreferenced function at 0x4B7B20 (4 + move) is dead code. | Original |
| TB11 | `combat_turn_based_end_critter_turn` (0x4B7C90), `combat_turn_based_add_critter` (0x4B7E00) | End: action points 0, next subturn. Add: appended (a warning for a hidden awake critter), sorted, the current node unchanged. | Original |
| TB12 | `combat_save` (0x4B1EA0), `combat_load` (0x4B2020) | Saves setting, active flag, turn counter, then action points, an unused handle, the current actor, the required action points and the last actor. Load restarts the round (fresh scan, counter back to 1), restores the action points and the current actor, resets the required action points; it fails when the actor is not found again. | Original (the session keeps the saved list instead, see the contract) |
| TB13 | `object_list_rect` (0x440B40), `sector_rect_from_loc_rect` (0x4D0090), `obj_find_node_attach` (0x4E3D30) | Scan order (O2): sector rows by increasing y, sectors by increasing x, inside a sector newest-first (attach prepends; re-attached only on a sector change); destroyed, off and inventory objects excluded. | Original |
| TB14 | `sub_430FC0` (0x430FC0), `sub_4348E0` (0x4348E0) | Step: walk 2, run 1 with fatigue > 0 (else walk); the run fatigue charge is A4. Gate: false when inactive; true outside a round; false off-turn; an NPC that cannot pay needs `100 × fatigue / max > 35`; a maximum of 0 divides by zero (both rule modes avoid the fault). | Original (A4 a reconstruction slip; the division a fault fixed in both modes) |
| TB15 | `timeevent_ping` (0x45B370) | In single player game time advances only while no round is active (`combat_turn_based_is_active` 0x4B6D70); animation time always. | Original (first reader only) |

## Scripts, event queue and randomness (ARC-RES-042, ARC-RES-045, ARC-SCRIPT-007, ARC-RES-046, ARC-WORLD-019)

Source: [dispatcher](scr-event-dispatcher.md#11-anomalies-recorded-not-adopted-s),
[census](scr-census.md#anomalies-for-s1-s), [admission](../scripting/scr-program-admission.md),
[event queue](game-event-queue.md#7-anomalies-recorded-not-adopted),
[session randomness](../world/session-random-runtime.md#what-the-original-does).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| X1 | `script_execute_action`, dialog case (0x447282) | Flags go to flags, counters to counters. | Reconstruction slip |
| X2 | `script_execute` (0x4449B0) | With no attachee the type comes from an uninitialised slot. | Original bug |
| X3, sector | `objlist_notify_sector_changed` (0x4F2330) | Number, flags and counters are compared and the changed copy is stored: a sector script's self-removal persists. | Reconstruction slip |
| X3, tile | `tile_script_exec` (0x4C0980) | The **unmodified** record is stored back: nothing a tile script changes about itself persists, including "remove this script". | Original bug |
| X4 | `sub_44B030` | Call-script runs one local copy and writes nothing back. | Original |
| X5 | `script_execute`, `script_flags` | The two missing-file exits are as the dispatcher study describes. | Original |
| X6 | `script_execute`, `sub_44C1B0` | −1 next, −2 skip default, −3 and −4 run default; −2 and −3 clear an auto-removing script, −4 does not. Other negative values fail the bounds check (skip default). So the admission refusal `NegativeGoto` is the project's own strictness, not the original's: in the original a literal goto of −1..−4 is a control code. | Original (reconstruction slip in the −4 removal detail) |
| X7 | `script_execute_action`, divide (0x4474CE) | Stores the signed quotient; nothing for divisor 0. | Reconstruction slip |
| X8 | `script_set_value` (0x44BB50) | Counter writes are masked to 8 bits. | Reconstruction slip |
| X9 | `script_execute_action`, action 86 (0x4494DF) | Reads slot 1. (The headline punctuation test reads the last character; the earlier reading read the terminator and always appended a period.) | Original |
| X10 | `script_global_flag_set` (0x444D20) | Global-flag writes are not masked: 2 sets the next flag, a negative value every higher flag of the word. | Original bug |
| TE1 | call-in cases, `sub_454790`, `datetime_add_milliseconds` (0x45AA10) | The 32-bit `seconds × 1000`, `× 8` delay wraps above 536,870 s (see [second reading](#second-reading)). | Original bug (edges only) |
| TE2 | call-at case, `sub_45AD70` | Counts from the start hour. | Original |
| TE3 (OQ1) | `timeevent_add_base_at_func` (0x45B8C0), `sub_45B750` | Ties newest first; events merged after a ping go in front of older equals. | Original |
| TE4–TE7, TE9 | `timeevent_clear_one_ex`, `timeevent_ping` (0x45B370), `timeevent_recover_handles_internal` | Two removals possible, shared loop guard, merge drops stale handles without recovery, handler results ignored, null objects passed on. | Original |
| TE10 (ARC-WORLD-024) | `stat_poison_timeevent_process` (0x4B1170), `stat_base_set` (0x4B0980), `poison_timeevent_schedule` (0x4B1350) | The recovery case writes the lowered level through `stat_base_set`, which for a positive level calls `poison_timeevent_schedule` with recovery on; that adds a recovery event when `timeevent_any` finds none (the firing one is already off the list). The handler then adds another recovery event itself (120,000 ms) while the level stays positive. From the second tick on two recovery events run per period. Read twice (the second reader, for the ARC-WORLD-024 review, decoded the post-write switch tables of `stat_base_set` and the list unlink in `timeevent_ping` 0x45B370 before the handler call). | Original bug |
| TE11 (ARC-WORLD-024 finding, recorded by ARC-DEV-031) | `stat_poison_timeevent_process` (0x4B1170) | Not given a verdict. ARC-WORLD-024 reported, as a side finding without a verdict, that in a single-player game the handler seems to apply the poison damage and recovery ticks to a critter whose object is switched off (OFF flag) as well, the off-object skip looking tied to the network game; that branch has not been read to a verdict. Steamwoven does the same today: the session's poison timers carry no off flag, and the AREA viewer's event pass (ARC-WORLD-039) runs an NPC's timers whenever a covered map holds the NPC's row (its current map, or its home map for a follower the session moved). To be read at 0x4B1170 by the task that next changes the poison timers or gives event targets an off rule (owner decision 2026-10-20, ARC-DEV-030). | Not checked (F0) |
| OQ2 | type table 0x5B2188, `timeevent_save`, `ai_timeevent_process` (0x4AD200) | The AI tick kind is not saved; a load schedules a new tick per NPC with 0..5000 ms jitter. | Original |
| OQ3 | `timeevent_save_nodes_to_map` and neighbours | Per-map `TimeEvent.dat` files; events follow their objects across a teleport. | Original |
| Random (R1–R6) | `random_rand` (0x4CEF50), `random_between` (0x4CEF00), `random_seed_generate` | LCG 1,664,525 / 1,013,904,223, output bits 8..22; modulo ranges, swapped bounds; time seed; no save or load; spawn-inventory and dialog reseeds. All as the contract says. | Original |
| SN1 (ARC-SCRIPT-011) | `script_name_init` (0x4CEC10), `script_name_mod_load` (0x4CECE0), `script_name_build_scr_name` (0x4CEDC0), `script_name_build_dlg_name` (0x4CEE60) | The `scr\*.scr` listing maps `atoi(name)` ≥ 1000 at game start and 1..999 at module load, a later name replaces an earlier one with an error message; lookup takes ≥ 1000 from the first table and 1..999 from the second; the DLG name swaps the first and last three characters for `dlg`. | Original |
| DL1 (ARC-SCRIPT-012) | `dialog_load` (0x412E10), `dialog_parse_field` (0x417C20), `dialog_parse_entry` (0x417860) | As the DLG original read mode: a field is found by skipping to the next `{` (`//` comments skipped), capped at 999 characters; id, IQ and response read with `atoi`; a non-empty IQ that reads 0 and a response starting with `#` end the read; a file with no record is not opened. | Original |
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
| LA1 | `sub_437990` (branch 0x437A5C) | At Speed 8 the middle rate. | Reconstruction slip (doc correct) |
| LA2 | `sub_430FC0` | Combat-mode running adds 1 fatigue per 5th step (= A4). | Reconstruction slip (doc correct) |
| LA3, L2, L5 | object click near 0x54FB19, `get_always_run` (0x4304C0), `turn_on_running` (0x436AB0) | Object clicks always run; Num Lock is a toggled state seeded from the key; run needs load under 60 % and fatigue above 0. | Original |
| W1, W2, W7 | `sub_4273B0`, `critter_can_jump_window` (0x45F570), path search `sub_41F9F0` | Window pieces, who can jump, +50 path cost, as the study says. | Original |
| LA4 (a, b) | `location_rot` (0x4B8D50), `sub_4288A0`, door goals 0x428550–0x428800 | Same tile gives direction 5; the window jump and every door open-attempt goal pass `object_rot(walker, portal)`. | Original |
| LA4 (c), building U4 | `ai_is_indoor_to_outdoor_transition` (0x4AECA0) | The "opposite direction" branch really **swaps** the portal and far tiles; the earlier reading copied the far tile into both, so there that branch never counted as leaving. In the original a walker coming from across counts as leaving when its tile is indoor and the portal's tile is not. Side effect: on the portal's own tile (direction 5) a portal of raw rotation 0–2 is tested in reverse, so an outdoor walker there passes as "leaving". What this means for a walking step: [LV1–LV5](#leaving-a-building-through-a-locked-portal-arc-res-049). | Reconstruction slip; the direction-5 side effect is an original bug |
| Wilderness A1 | `sector_light_list_load` (0x4F72C0) | Lights move with the object rule (sector × 64 + local tile). | Reconstruction slip (confirms the proposed policy) |
| Wilderness A3, A6 | `terrain_sector_path` (0x4E84C0) | `-1.sec` path; a null name faults. | Original (A6 an original crash bug) |
| Wilderness A2, A4, A5 | — | Editor encoder and `arcanum2.dat` folder data. | Not checked |
| Combat fields | `item_weapon_damage` (0x465F70), `item_damage_min_max`, `item_weapon_range` (0x465E30), `item_weapon_magic_speed`, `item_armor_ac_adj` (0x465BA0), `ai_copy_params`, `item_weapon_skill` (0x465D80) | The field numbers the original passes (damage 116/117 by type, magic damage 118, speed 119/120, range 121/122, flags 112, ammo 125, AC 152/153, AI row 282, monster damage 303 at 2·type and 2·type+1) match the ARC-DATA-021 reader's slots. | Original (reader confirmed) |
| Field inheritance | `obj_field_fetch` (0x408A20), `sub_408F40`, `sa_get` (0x4E76C0) | An instance field replaces the prototype's whole field; a missing sparse key reads 0. A null array would fault; "null reads 0" is the project's fail-safe. | Original |
| Lock hours | `object_locked_get` (0x441D40) | Locked-by-day and by-night use hours 7..21. | Original |
| Follow C7–C9, C3 | `anim_goal_follow_obj` (0x4346A0), `sub_42BF40`, `ai_timeevent_delay` (0x4AD5D0), `ai_init` | Follow ranges 4/7 and 2/5, walk within range + 2, AI delay `250 + 4750·min(d,30)/30` ms, 30-cell catch-up, row filling. | Original |
| Clock add | `datetime_add_milliseconds` (0x45AA10), 0x45AA50 | Day carry only when the sum is above 86,400,000, so a sum of exactly one day stays as 86,400,000 ms of the same day. | Original bug (off by one; see [second reading](#second-reading)) |
| Clock current day/month | `datetime_current_day` (0x45AB00), `datetime_current_month` (0x45AB20) | The two getters are exchanged; the ordinary getters are right. Nothing calls them. | Original bug (dead code) |
| Clock subtraction | `datetime_sub_milliseconds` (0x45A9E0) | The underflow branch computes the same odd expression the clock study describes. | Original bug |
| Clock start | `gameinit_mod_load` (0x4B9C30) | 1885 and 15:00; hour 0 stored as 1 ms. | Original |

## Leaving a building through a locked portal (ARC-RES-049)

Source: ledger row LA4 (c) above, [locomotion](locomotion-evidence.md#windows-s-o-d) W3 and D3,
[first building](first-building-evidence.md#door-admission-fr-scene-014) and the whole-map step rule
of ARC-WORLD-018. Question: when does the walking player pass a closed, locked portal it has no key
for, by the "leaving a building" shortcut? Two readers (this task's and a fresh second reader that
did not see these verdicts) read every row below in the executable and agree on every observation;
only the internals of `object_locked_get`, the key check and `tile_art_id_at` were not read (F0); ARC-RES-065 reads the first two ([LK2, LK9](#doors-containers-and-locks-arc-res-065)).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LV1 | `ai_is_indoor_to_outdoor_transition` (0x4AECA0), `tig_art_tile_id_type_get` (0x5038C0) | Compares the portal's tile P with Q, the tile one step along the portal's **raw** ART rotation r (the diagonal tile for an even r; false off the map). When the direction argument is r + 3, r + 4 or r + 5 (mod 8) it really exchanges P and Q. Leaving means the first tile is indoor and the second is not; indoor is tile art with bit 8 clear or facade art with bit 25 clear, and any other art counts as indoor. | Reconstruction slip (the earlier reading copied Q into both); confirms LA4 (c) |
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
| BL4 | `object_draw`, scaled source rectangles | A scaled body's source rectangle is the destination one divided by scale/100 and **rounded to nearest**; the earlier reading truncated. The Steamwoven renderer maps whole frames with its own nearest-pixel rule, so nothing depends on it. | Reconstruction slip (no consumer) |
| BL5 | 16-bit blits for constant alpha and multiply (0x50742A, 0x506B87; stretched 0x510F44, 0x5104A6) | Constant alpha is the source's weight per channel (`dst + (src − dst)·alpha / 256`); multiply is the per-channel product of source and destination; index-0 source pixels are skipped. | Original |

## Equipment (ARC-WORLD-028)

Source: [equipment runtime](../world/equipment-runtime.md) and the item requirements
[FR-ITEM-005..007](../requirements/items-economy.md#fr-item-005). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below, and both found EQ1.
EQ7 was read the same by both; the second reader adds that `item_is_identified` answers true for complexity ≤ 0, so the earlier reading's
differently written gate has the same effect. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| EQ1 | `sub_464D20` (0x464D20) | The wear check, in order: a critter spell flag of polymorph or a "body of" form → not wieldable; slot 1002 read as 1001; complexity > 0 with background 69 (Magick Allergy) → no magick items; the destroyed bit of the item's current art → broken; `item_location_get` ≠ slot → wrong type. Weapon: two-handed with a fixed hand count → no free hand with a shield, crippled with either arm flag; other weapons crippled only with both arms; then the critter art with the weapon's subtype must exist. Shield: no free hand next to a fixed two-handed weapon; **crippled with either arm flag**; art only for an armour shield. Body armour: the race's size bit, male-only before female-only, unique NPC art, then the art. No minimum-strength test. | Original, except the shield: the earlier reading tested only "both arms crippled", a reconstruction slip; both settings refuse a shield with one crippled arm |
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
| EQ12 | `item_recalc_light` (0x467E80) | The worn items' light flags, each shifted by its effective power ratio (≤ 25 by 3, ≤ 50 by 2, ≤ 75 by 1), are ORed; the critter keeps **only the largest** size flag. | Reconstruction slip (the earlier reading set every size flag); the light drawn is the same (the largest wins in both) |
| EQ13 | `item_weapon_damage` (0x465F70), unarmed part | Bare-handed melee reads the unarmed bonus of whatever item is in slot 1007, without a type test; normal and fatigue `max(1, bonus − 25) .. bonus + 5`, other types 0; a monstrous critter its own table. | Original |
| EQ14 | race to armour size table (0x5B32C0) | Human, elf, half-elf, half-orc, dark elf, orc medium; dwarf, gnome, halfling small; half-ogre, ogre large. | Original |

## Art names and missing art (ARC-RES-050)

Source: [whole-map art gaps](whole-map-art-gaps.md#how-the-original-names-critter-art-and-falls-back).
Read on 2026-10-17 by a first reader and, independently, a second reader that saw only the questions
(method step 6); both agree on every behaviour and verdict of MA1–MA5 and MA7. Every direct branch of the functions read for those rows stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MA1 | `fix_missing_art` (0x41DA40), tables 0x5A106C, 0x5A10B8, 0x5A1104, 0x5A1150 | Nothing happens before the masks are loaded or for a number outside the list. The 19 checked pairs are tried in order: a set bit whose animation equals the current one (and, for low attack 21 only, whose weapon equals it) replaces the animation (walk, run → walk; stunned, stand → stand; ten low attacks → attack 20; decapitation, blown-out chunk, severed leg, explode → fall down; stealth walk → walk). A walk replacement sets weapon 0 when walk unarmed is missing; a stand replacement with weapon 0 sets weapon 1 when stand without a weapon is missing. Tables as the art-gaps study lists. | Original |
| MA2 | `name_normalize_aid` (0x41D510), critter, monster and unique NPC branches | Critters: elf female → human body; plate armour (4, 6) → male, elf → human, halfling → dwarf. Then the repair (MA1), then: explode clears armour, shield and weapon; stunned sets weapon 1 and clears the shield; weapon 1 with stealth walk or conceal fidget → weapon 0; animations 6–19 clear weapon and shield; walk clears the shield. The id is rebuilt with frame, rotation and palette 0. Monsters and unique NPCs: the same without the body rules. On explode the critter branch sets the **male** gender where the earlier reading wrote female; the explode file name ignores the gender, so nothing drawn changes. | Original; the explode gender is a reconstruction slip with no consumer |
| MA3 | `name_resolve_path` (0x41DAE0) | Normalizes the id (MA2) before building any name; monster and unique NPC names are folder and file name from the two name lists, armour code (monster only), shield `X`/`S`, weapon letter (two-handed sword with a shield written as sword) and animation letter. | Original |
| MA4 | missing-art table load (0x41D1F0), init (0x41CE60) | `art\missing.dat`: a header that must equal the current count of critter, monster and unique NPC ART files, then 10 critter masks, one per monster and one per unique NPC (list counts). A short read or a header mismatch rebuilds the masks by checking each pair's file and saves the file. The unique NPC read is compared with the unique count; the earlier reading compared it with the monster count. | Original; reconstruction slip in the count check |
| MA5 | art cache load (0x51AA90) | When the resolved file cannot be loaded it logs the failure and loads `art\badart.art` under the same art id; only when that fails too does it return no entry (nothing drawn). The frame lookup that follows (`tig_art_frame_data`, not read in the executable) indexes the loaded file's frame table with the requested rotation and frame, unchecked; `badart.art` has one frame. | Original (load fallback); frame lookup F0 |
| MA6 | art cache entry load (0x51B170) | A one-rotation ART (flag 1) serves every rotation from its only group. | Not checked (already the whole-map rule of ARC-DATA-016, F0) |
| MA7 | `a_name_wall_aid_to_fname` (0x4ECEB0), `build_wall_file_name` (0x4ECFC0) | At rotations 2, 3, 6, 7 the damage bits `0x400` and `0x80` swap. `0x400` turns piece 7 into piece 0 and names `L`; `0x80` turns piece 8 into piece 0 and names `L` for pieces **1–6**, else `R`; no damage names `U`. Interior name for rotations 0, 1, 6, 7, else exterior. The earlier reading wrote pieces 2–6 for `L`. | Reconstruction slip (piece 1) |
| MA8 | `object_draw` (0x43B390), `a_name_tile_aid_to_fname` (0x4EAE90) | Not read in the executable: in the earlier reading neither the object draw nor the resolver treats a tile id on an object differently from other art; rotation and frame of a tile id read 0. | Not checked (F0) |

## Durable effects (ARC-WORLD-029)

Source: [durable effects runtime](../world/durable-effects-runtime.md) and
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below. Add, the typed removal
and the hit-point setter begin with a network branch the earlier reading left out; a single player game never takes it. Every direct
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
the earlier reading's table index; asked to re-read only that instruction sequence, it found the original's index one lower, as the first
reader and the earlier LM11 and A1 readings did. Several functions begin with a network branch the earlier reading left out
(multiplayer XP split, party-wide quest states, a cheat loop over all players); a single-player game never takes it.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| XP1 | `critter_give_xp` (0x45F110) | Only a PC. A gain of exactly 0 returns at once; a negative one goes through. Easy adds half the gain, normal keeps it, **hard takes a quarter away** (truncated toward zero; the earlier reading took half). Then a dead PC gets nothing; otherwise the gain goes through `effect_adjust_xp_gain` (the `xpgain` special) and is added to the base experience through the setter. | Reconstruction slip (hard difficulty); the rest Original |
| XP2 | `stat_base_set` (0x4B0980) | Experience is clamped to 0..2,000,000,000, unspent points to 0..56, the level to 0..51. Every experience write runs `level_recalc` and refreshes the character sheet, whether or not the value changed; level and unspent points have no side effect. | Original |
| XP3 | `level_experience_points_to_next_level` (0x4A68B0), `level_progress_towards_next_level` (0x4A6900) | Both read the effective level. To-next: 999,999 at level 51 or above, else the next level's threshold minus the experience. Progress: 0 at 51 or above; 999 for level 0, an empty threshold or a negative to-next; else `1000 − 1000 × to-next / step`, truncated. **The product `1000 × to-next` is 32-bit and wraps** when to-next exceeds 2,147,483 (found by the oracle trial of ARC-RES-051, not by the two readings; see below). The installed table never gets there (largest step 80,000). | Original; the 32-bit wrap is an original bug (latent with the installed table), a rule-mode switch by owner decision 2026-10-17 |
| XP4 | `calculate_bonus_character_points` (0x4A6980) | One point per level from old + 1 to new, one more on every fifth level; no cap of its own. | Original |
| XP5 | `level_recalc` (0x4A69C0) | Only a PC; returns when the base level is at the maximum (51) or the next level is not reached. Each pass raises the level by one: the normal branch writes it, adds its points and shows the level message (or runs the auto-level scheme); when the new level would be 51, it writes the threshold of **level 50** (table index new level − 2; the earlier reading: − 1) as the experience, writes no level, grants nothing. That write re-enters the function, which finds level 51 not reached and returns: no recursion. Followers and nearby waiting followers then get (old level, the last level reached), which is 51 in that branch although the PC stays at 50. | Cap at 50 Original; XP value and no recursion: reconstruction slip (as LM11, A1) |
| XP6 | `level_init` (0x4A6620) | 51 entries from messages 1..51 of `rules\xp_level.mes` by `atoi`, 0 for a missing message, then entry 0 forced to 0. | Original |
| XP7 | `update_follower_level` (0x4A6CB0) | Leaves a follower at or above the PC's **new** level (the earlier reading tested the follower's maximum); otherwise a follower at or below the PC's old level keeps its gap, one above it moves to the PC's new level, capped at its maximum − 1 (50); a rise writes the level, adds the points (unspent cap 56) and runs the NPC's auto-level scheme. | Reconstruction slip in the first test only; the same result for every step `level_recalc` passes (new above old) |
| XP8 | `quest_state_set` (0x4C4D20), `quest_get_xp` (0x4C53C0) | After the completed/botched and not-higher refusals, a request for Completed gives the PC `quest_get_xp` of the quest's experience level **before** the state is stored; `quest_get_xp` returns the message of that number in `rules\xp_quest.mes`, 0 when missing. | Original |
| XP9 | `critter_notify_killed` (0x45DA20) | For an NPC victim with a killer: the killer if it is a PC, else the killer's PC leader, gets `worth × 20 / 100` (truncated), and only then the victim's worth is set to 0; without a PC recipient nothing is given and the worth stays. | Original |
| XP10 | `combat_dmg` (0x4B4390), the pool block | With an attacker, an NPC target not turned to stone and a positive pool: percentage = `100 × damage / maximum hit points`, award = `worth × percentage / 100` (both truncated, 32-bit), at most the pool; the pool loses the award whoever attacked; only a PC attacker gets it (no leader redirection). It runs before the kill award, so a killing blow pays both. | Original |
| XP11 | call sites of `critter_give_xp` and `quest_get_xp` | Dialog action XP (0x415BA0) and the script action that gives quest experience (0x447220) both give `quest_get_xp(value)`; quest completion (XP8); kill (XP9); pool (XP10); a cheat key gives the experience still needed. | Original |
| XP12 | data (`DAT_ROOT`) | The winning `rules\xp_level.mes` (the `Arcanum4.dat` copy) has 51 strictly increasing entries, so XP5's re-entered call returns; `xp_quest.mes` has levels 1..50, `xp_critter.mes` 0..50, every quest level in `gamequest.mes` has quest experience, and one effect rule uses `xpgain`. | Data |

## Real-time combat (ARC-COMBAT-004)

Source: [combat core](combat-core-evidence.md#entering-leaving-and-switching-mode-fr-combat-001) C1–C5, contract
[real-time combat runtime](../world/real-time-combat-runtime.md). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6). They agree on every row they share; the second reader also
found the registered defaults (RT6) and the inverted invisibility test (RT8), and the first reader confirmed both. Every
direct branch stays inside `.text`; several functions begin with a network branch the earlier reading left out (the toggle's
packet and client return, turn-based refused in a network game, a per-player auto-attack flag), which a single-player
game never takes.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| RT1 | `combat_critter_toggle_combat_mode` (0x4B3F80) | A dead critter skips every check; otherwise stoned, paralysed, stunned (leaving only) and inactive (entering only) refuse, and an NPC in its concealed animation refuses. Leaving clears the flag, unlocks the AI target, puts the arms away and, for the PC with turn-based on, ends the round. Entering writes the flag first; for the PC with turn-based on a round start that fails returns at once with the flag left set. | Original |
| RT2 | `combat_can_exit_combat_mode` (0x4B3D90) | True out of combat mode or when not active; false in an attempt-attack goal; false when an active, fighting NPC on screen (`object_list_vicinity`) has the critter or one of its followers (recursive follower arrays) as combat focus. | Original |
| RT3 | `intgame_combat_mode_toggle` (0x54EBF0) | Nothing for a dead PC; leaving through RT2, refused with intgame message 24; else entering. | Original |
| RT4 | `sub_4B6C90` (0x4B6C90), `combat_turn_based_toggle` (0x4B6D20) | Unchanged value stores; the PC's animations that cannot be interrupted refuse; out of combat mode it stores; off ends the round; on starts it, and a failed start leaves the setting unchanged. The key turns it on only for an active PC. | Original |
| RT5 | `combat_auto_attack_get` (0x4B8230) | False while a round is active; else the `auto attack` setting. | Original |
| RT6 | `combat_init` (0x4B1D50) | Registered defaults: `turn-based` and `fast turn-based` "0", **`auto attack` and `combat taunts` "1"**; a value in the configuration file wins. | Reconstruction slip (the earlier reading registered "0") |
| RT7 | `sub_429760` (0x429760) | The attack repeats only for a PC in combat mode with auto attack, a target neither destroyed, off, dead nor busted, the run flag 0x800 clear, no round, no non-lethal flag and no boomerang in flight; it then waits 5 ms (goal table). | Original |
| RT8 | `AGbeginAnimAttack` (0x432700), `sub_42EE90` (0x42EE90), `item_weapon_magic_speed` (0x465CF0) | Pause `clamp(1000 / rate(Speed), 30, 800)`, then minus `10 × (weapon speed − 10)` and clamped again; weapon speed 10 unarmed, +5 with apprentice training. A critter rolls 1..4 and plays its attacking sound on 1. Action frame below 1 marks the action at once. The invisibility clear applies to an attacker that is **not** the local PC (the earlier reading tested the opposite). | Original; the invisibility test a reconstruction slip (not modelled yet) |
| RT9 | `AGupdateAnimAttack` (0x432990) | Stoned, paralysed or stunned ends; the last frame ends; the frame before the action frame marks the action; in real time a critter's pistol or rifle two frames after the action frame, or bow on it, goes back three frames toward the target while it has ammunition, is a PC or an NPC not backing off and the target is valid; else the next frame. The pause is recomputed only on a speed-change flag. | Original |
| RT10 | `sub_437990` (0x437990) | Attack and low-attack rates per weapon slot as the earlier reading's tables, listed in the [real-time combat runtime](../world/real-time-combat-runtime.md) (both jump tables decoded); no dwarf or halfling offset for attacks; Speed 8 gives the middle value (LA1). | Original; Speed 8 a reconstruction slip (LA1) |
| RT11 | goal tables `anim_goal_node_attack` (0x5A7BD0), `anim_goal_node_attempt_attack` (0x5A7DD0) | Byte for byte the earlier reading's tables: the hit runs in the update that shows the action frame, the end one pause after the last frame appears, the repeat 5 ms later. | Original |
| RT12 | `sub_430FC0` (0x430FC0) | A4 and TB14: every fifth run step of a move in combat mode (×4 fatigue-limiting, ÷4 draining) adds 1 fatigue damage through the plain fatigue write; a runner without fatigue walks. | Original (A4 a reconstruction slip) |

## Character sheet (ARC-UI-007)

Source: [viewer character sheet](../rendering/terrain-viewer.md#character-sheet-arc-ui-007) and
[FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004). Read on 2026-10-17 by a first reader and, independently,
a second reader that saw only the questions (method step 6); both agree on every behaviour below. The second reader adds that
the letter keys arrive as character messages, so `c` and `C` act alike, and that every letter hotkey is ignored while the main
menu is active (the earlier reading tested the main menu only for Enter). Every direct branch stays inside
`.text`; the two indirect ones (the key handler's character table, the value getter's index table) stay inside their
functions. Effect aggregation (EF6), experience to the next level (XP3) and `object_get_resistance` (EQ8) were checked
before and are only referenced.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| CS1 | in-game message filter (0x54B5D0), `sub_552740` (0x552740) | The character messages `c` and `C` (one handler through the filter's character table) call `sub_552740` with the local PC and mode 1 (active), which calls `charedit_open` only while the in-game interface exists and the object is not null. The character switch is not reached while a dialog owns input (its handler gets every key), on another critter's turn in turn-based combat (only space), with a focused text edit or the main menu active. | Original |
| CS2 | `charedit_open` (0x5597C0) | Already open: closes it and returns true (a toggle). Otherwise, in order: a dead local PC refuses; then the interface mode is set to main (closing whatever mode was open) and to the sheet, either refusal refuses. Create mode clears hit-point and fatigue damage. At the end it interrupts the shown critter's animations (priority 4), marks the sheet open and suspends auto-attack while it is open; it returns true. It does not pause the game. | Original (the earlier reading skipped the animation interrupt on purpose, a reconstruction change) |
| CS3 | `charedit_refresh_stat` (0x55B2A0) | Unspent points, level, experience to next and the eight base entries draw nothing. Maximum hit points and fatigue draw `object_hp_max` and `critter_fatigue_max`. Each primary stat draws its effective level; its colour is green when `effect_adjust_stat_level_no_innate` of the base value is above the base value, red when below, white otherwise. | Original |
| CS4 | `charedit_stat_value_get` (0x55B4D0) | Entry 0 is the base unspent points (stat 21), 1 the effective level (stat 17), 2 `level_experience_points_to_next_level` (XP3); then hit points (points, current), fatigue (points, current), and base/effective pairs of the eight primary stats. | Original |
| CS5 | `charedit_refresh_basic_info` (0x55A240) | Prints the examine name, unspent points, level and experience to next; race (stat 27), gender (26) and age (25) for a PC, race and gender also for an NPC that is not monstrous (a monstrous one gets none). | Original |
| CS6 | `charedit_refresh_secondary_stats` (0x55B1B0) | Effective levels of stats 8..15 (carry weight, damage bonus, AC adjustment, speed, heal rate, poison recovery, reaction modifier, max followers), then `object_get_resistance(obj, type, 1)` for normal, magick, fire, poison and electrical: the "for display" form, which leaves out the magic adjustment of unidentified magickal armour (EQ8). | Original |
| CS7 | `charedit_refresh_alignment_aptitude_bars` (0x55EC90) | Alignment (stat 19) divided by 10 with truncation toward zero is printed and picks the bar frame (value / 10 + 10). Aptitude (stat 16): a value of 0 or more is printed on the magick side with 0 on the tech side, a negative one as its absolute value on the tech side with 0 on the magick side. | Original |

## Hero attack (ARC-COMBAT-005)

Source: [hero attack runtime](../world/hero-attack-runtime.md), [combat core](combat-core-evidence.md) C15–C21 and C26, and
[skills](skills-evidence.md#2-ranks). Read on 2026-10-17 by a first reader and, independently, a second reader that saw only
the questions (method step 6); they agree on every row. The second reader found FA6 and that the click reads Shift and Alt as
held keys (left or right), not as modifier state; both readers agree the follower test reads only left Alt. Effect aggregation
(EF6), the attack goal tables (RT11), the repeat (RT7) and `object_get_resistance` (EQ8) were checked before and are only
referenced. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| FA1 | `sub_54ED30` (0x54ED30), PC and NPC targets in combat mode | A living target gets the attack goal, or the attempt-attack goal with left or right Shift held. | Original (the reconstruction agrees) |
| FA2 | `sub_54ED30`, the follower test | Unless the target descriptor is a location: the target's PC leader is passed to `player_is_local_pc_obj`; when it is the local PC the click does nothing unless left Alt is held. Then the body-of-air refusal (intgame message 25). | Slip of the reconstruction (it passes the `&&` result to the call, so it never blocks) |
| FA3 | `sub_54ED30`, a dead target in combat mode | With left or right Alt: the use-container goal (loot); without: the function returns, nothing happens. | Slip of the reconstruction without Alt (it leaves the goal unset) |
| FA4 | `sub_54ED30`, aimed attacks | For an attack or attempt-attack goal: `,` stores aim 1 (head), else `.` 2 (arm), else `/` 3 (leg), else 0, in the goal's third scratch value. | Original (the reconstruction agrees) |
| FA5 | `sub_54ED30`, before the target switch | With nothing dragged, a PC out of combat mode whose target is attacking it enters combat mode first. | Original (the reconstruction agrees); not modelled (no AI) |
| FA6 | `sub_54ED30`, first tests | The click does nothing when the PC is not active, is stoned, or is stunned or paralysed. | Slip of the reconstruction (it returns only when stoned **and** stunned or paralysed) |
| FA7 | `basic_skill_base` (0x4C5E50), `basic_skill_level` (0x4C5EB0), `max_skill_for_stat_level` (0x4C5F70), `basic_skill_points_get` (0x4C5FA0) | Points are bits 0..5, the base is 4 per point; melee of a monstrous critter starts at 20; then the effect adjustment, a clamp to 0..20 and the cap of the key stat (table 0x5B6F04: dexterity for bow, dodge, melee, throwing, backstab and pick pocket; perception for prowling and spot trap; intelligence, willpower, intelligence, charisma for gambling, haggle, heal and persuasion), the cap table read with the stat clamped to 1..20: 3,3,3,3,3,7,7,7,11,11,11,15,15,15,19,19,19,20,20,20. | Original (the reconstruction agrees) |
| FA8 | `basic_skill_training_get` (0x4C60C0) | Bits 6..7; a monstrous critter's melee: the first tier whose melee level is below 1, 9, 18 or whose level (stat 17) is below 10, 20, 30, else master. | Original (the reconstruction agrees) |
| FA9 | `critter_is_monstrous` (0x45F730) | Critter flags & (mechanical, monster, animal, undead). | Original (the reconstruction agrees) |
| FA10 | `combat_dmg` (0x4B4390), before `critter_notify_killed` | The dying animation: 7 for a death-flag hit or at most 20 damage, else 17 for a head hit, 19 for a leg hit, 18 otherwise. | Original (the reconstruction agrees) |

## Map transitions (ARC-DATA-025)

Source: the [map transitions](../world/map-transitions.md) reader, which decodes these actions and
triggers as data; the [transition research](world-transitions.md) recorded them at F0, not read in the executable (W3, W4, W6, W9).
Both readings (this task's reader and a fresh second reader) agree on every row.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TR1 | `script_execute_action` (0x447220), teleport case (0x4481A1, jump table entry 43) | Object from operand 0, then map, x and y from operands 1–3 as values; the location packs x low and y high; the map is the value − 4999; flags 0; then `teleport_do`. | Original |
| TR2 | the same, fade-and-teleport case (0x449A85, entry 101) | Skipped in a network game. Fade out and in, then time, sound and movie from operands 0–2 (time and sound set their flags when positive; the movie flag is set for any non-zero value, an unsigned test where the earlier reading tested for positive), object from operand 3, map − 4999, x and y from operands 4–6. | Original |
| TR3 | object location change (0x444270), `map_process_jumppoint` (0x4101D0), `tile_script_exec` (0x4C0980) | After a move, for any PC (`player_is_pc_obj` 0x40DA20; town map, roof, wall check and scroll only for the local one) the jump point at the new tile teleports it with flags 0, while fewer than 9 jump teleports are nested; then for an object of type 15 or 16 (PC or NPC) the tile script runs. | Original |
| TR4 | `teleport_do` (0x4D3380), `teleport_process` (0x4D3460) | A request while a teleport of the local PC is pending is refused; any other pending request is replaced. A pending map of −1 becomes the current map when it is processed. | Original |
| TR5 | `tile_script_exec` (0x4C0980), `jumppoint_get` (0x4E3450), `objlist_notify_sector_changed` (0x4F2330) | A tile script runs on a copy and the unchanged record is stored back (X3), so its self-removal never sticks and the tile fires every time; a sector script's changed copy is kept. A tile's first jump point wins and no flag is tested. | Original |

So a script's catalogue 4998 and a jump point's −1 both keep the current map. What a teleport
then does to followers and the destination map (W9) is not read here.

## Prototype phase (ARC-DATA-026)

Source: [map catalogue prototypes](../world/map-catalog.md#prototypes) and the
[mount sequence](content-precedence.md#mount-sequence-source-facts). Question: are prototype files read from the sources
mounted before the module only, so that a module, its patches or its folder can never supply one? Read on 2026-10-17 by a
first reader and, independently, a second reader that saw only the questions (method step 6); both agree on every
behaviour below. Every direct branch of these functions stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| Prototype phase | `gamelib_init` (0x4020F0), `map_init` (0x40EA90), the map subsystem table (0x59F068), `proto_init` (0x4681B0), `gamelib_mod_load` (0x402A50), `map_mod_load` (0x40ED10) | `gamelib_init` mounts the base data (0x404A20) before its loop over the subsystem init functions; Map is one of them, and its init runs every map subsystem's init. The Proto entry of the map table has only an init (0x4681B0) and an exit: no reset, module-load or module-unload function. `proto_init` restores any missing generated prototype, then lists `proto\*.pro` and reads every listed file. `gamelib_mod_load` mounts the module (0x404C10: `.dat`, patches, folder) and only then runs the module-load functions; Map's loads `Rules\MapList.mes` and the Effect and O_Name module tables, nothing of Proto, and nothing else calls `proto_init`. So prototypes come from the base sources only and a module's copy is never read. The second reader adds that a "partial" install mounts the CD's `arcanum*.dat` (and module paths) too, which the earlier reading left out; those are still mounted at the same points, so the phase is unchanged. | Original |

The XP3 wrap (ARC-RES-051): the trial ran the three functions XP3 and XP4 name in an emulator on the unchanged
executable with 4,000 seeded inputs each against the unchanged `ExperienceRules` (method section
[Function emulation](original-executable-check.md#function-emulation-oracle)). To-next and bonus points agreed on
every input; progress differed on 269 inputs, all with to-next above 2,147,483, and agreed on all of them once only the
C# multiplication was made 32-bit in a scratch copy. A planted mutant (every fourth level instead of every fifth) was
caught. Steamwoven multiplies in 64 bits and `ExperienceTable.Create` admits thresholds up to 2,000,000,000, so a
table with a step above 2,147,483 shows a different progress. Owner decision 2026-10-17: a rule-mode switch, Original
wraps and Steamwoven keeps the exact value ([registry](../world/rule-modes.md#registry), implemented by ARC-WORLD-033;
the function oracle of ARC-DEV-007 finds no difference in Original mode).

## Audio (ARC-RES-052)

Source: the [audio study](audio-evidence.md#what-the-original-does), rows SN1–SN34, which keeps the
full table (SN34: one reading, ARC-WORLD-041). Three domains (resources and positional sound, schemes and combat music, voice and
event sounds) were each read by a first reader and, independently, a second reader that saw only
the questions (method step 6); the three differences are settled in the study. Every direct
branch of the functions read stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SN3 | `gsound_init` (0x41AA30) | soundparams key 12 is the small size's volume (default 40), key 22 the medium's (70); no overwrite. | Reconstruction slip |
| SN4 | `gsound_calc_positional_params` (0x41B420) | The attenuated volume is multiplied by the size's volume % (small 40, medium 70, large 100). | Reconstruction slip |
| SN12 | `gsound_scheme_load` (0x41BE20), `gsound_scheme_parse_entry` (0x41BF70) | A 26th entry in one scheme dereferences a null pointer (crash). | Original bug (no installed scheme reaches it; both settings refuse) |
| SN13 | `gsound_scheme_parse_entry` (0x41BF70) | `/over` wins over `/loop`; a balance-left above 127 clamps to 127. | Reconstruction slip |
| SN14 | `gsound_update` (0x41BAF0) | At most one ambient per slot per tick; hour windows never wrap, so 20 installed night entries never play. | Reconstruction slip (one per tick); original bug (windows), rule switch (ARC-UI-041) |
| SN17 | `gsound_start_combat_music` (0x41C4D0) | An NPC entering combat starts stinger and loop together; the local PC leaving combat restores the latest request. | Original |
| SN18 | `gsound_save` (0x41B1D0), `gsound_load` (0x41B0F0) | A save made during combat loses the pre-combat schemes. | Original bug, rule switch (ARC-WORLD-040) |
| SN22 | `dialog_ui_speech_start` (0x5688D0) | Voice gender is the PC's; closing the dialog does not stop the voice. | Original |
| SN24 | `sfx_critter_sound` (0x4F0ED0) | The off flag is tested on the local PC, not the sounding critter. | Reconstruction slip |
| SN26 | `AGbeginAnimLoopAnim` (0x42DA50), eye candy (0x431550, 0x431960) | Looping scenery and eye-candy sounds loop; flag bit 0x80; handle test and goal fields as written in the study. | Reconstruction slip |
| SN31 | hot-key equip (0x550000) | Equipping into an empty slot plays the slot number as a sound id. | Original bug, switch candidate |

## Load phases (ARC-DATA-027)

Source: [lighting sources](../rendering/area-lighting.md#sources-and-load-phases-arc-data-027), the
[starting hero](../world/starting-hero-runtime.md) and `ContentPhase`. Question: which files does the original read while
only the base sources are mounted (an init function; then `tig.dat`, the game folder, `arcanum*.dat` and `data/` can
supply them), and which at module load or later (every source)? Background: the [prototype phase](#prototype-phase-arc-data-026)
row; `main` calls `gamelib_init` (0x4012FF), then the UI library's init (0x53E600, at 0x401411), then `gamelib_mod_load`
(0x401444). Read on 2026-10-17 by a first reader and, independently, a second reader that saw only the questions (method
step 6); both agree on every row. The second reader adds that `mes_load` (0x4D4290) keeps a name cache, so loading a file
that is already loaded returns the loaded copy; that later module loads (`gamelib_mod_load` has six more callers) read the
module-load files again but never the game-start ones; and that `rules\gameinit.mes` is skipped when a flag set by
gameinit's init (0x4B9BC5) is set, whose meaning was not identified.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LP1 | `effect_init` (0x4E99F0), `level_init` (0x4A6620), `quest_init` (0x4C45C0), `invensource_init` (0x4BF390), `name_init` (0x41CA40) with `a_name_light_init` (0x4ED1E0) and `a_name_tile_init` (0x4EAC80) | Init functions of the gamelib table (Level entry 16, Quest 31, Inven-Source 46, Name 6) and of the map table (Effect). They load `rules\effect.mes`, `rules\xp_level.mes`, `Rules\xp_quest.mes`, `Rules\InvenSource.mes` and `Rules\InvenSourceBuy.mes` (through two name pointers), and Name's init calls the light and tile name loaders, which load `art\light\light.mes` and `art\tile\tilename.mes`. None of these subsystems has a module-load function that reads them again. | Original: base sources only |
| LP2 | `mainmenu_ui_init` (0x540930) | An init function of the UI library, which `main` runs before `gamelib_mod_load`; it loads `mes\mainmenu.mes` into the main menu's handle unless that handle is already loaded. The same init also loads it into a second handle through 0x585BC0 (the name cache returns the loaded copy; the earlier reading omitted this call); a third load site (0x5443E0) has the same guard and no reference (dead code). So no load after the module is mounted can replace the init-time copy. | Original: base sources only |
| LP3 | `gameinit_mod_load` (0x4B9C30), `map_mod_load` (0x40ED10) with `map_list_info_load` (0x4125C0), `light_scheme_mod_load` (0x4A7C90), `quest_mod_load` (0x4C4670) | Module-load functions (gameinit entry 61, Map 17, LightScheme 18, Quest 31) load `rules\gameinit.mes`, `Rules\MapList.mes`, `Rules\Lighting Schemes.mes` and `rules\gamequest.mes` after the module is mounted. | Original: every source |
| LP4 | `light_scheme_set` (0x4A7DD0), `map_load_extension` (0x4115D0) | A scheme file is loaded when a scheme is set, by the name the scheme catalogue gives (`Rules\%s.mes`); `mapinfo.txt` is opened from the map's base path when the map opens. | Original: every source |
| LP5 | `name_init` (0x41CA40) with the tile, facade, wall, portal and item name loaders (0x4EAC80, 0x4EC370, 0x4EC9B0, 0x4EC5A0, 0x4EC190), `name_missing_art_init` (0x41CE60) and `name_missing_art_load` (0x41D1F0) | Name's init (gamelib entry 6: no reset, module-load or module-unload function; it returns at once when already initialised) loads `scenery.mes`, `unique_npc.mes`, `monster.mes` and `container.mes` itself and the tile, facade, wall-name, wall-structure, portal and item-ground tables through loaders it calls once; each path has one code reference and the tables stay loaded until exit. `art\missing.dat` is read only from the missing-art init that Name's init calls (when it cannot be read, the art is probed and the file written); there is no later re-initialisation (the missing-art lookups return at once while it is not initialised). (ARC-DATA-027 part 2) | Original: base sources only |
| LP6 | `map_load_mobile` (0x410D10), called from `map_open` (0x40F4E0) | `Rules\map.patch` is checked, opened and applied line by line each time a map opens (outside the editor) and closed; only the patched objects remain. (ARC-DATA-027 part 2; the multiplayer caller of `map_open` was not examined) | Original: every source |

Not checked in the executable: when a light ART file is first opened (the earlier reading: when it is drawn, through the ART cache);
the readers take it from the stack winner, as every ART since ARC-DATA-018.

## Roofs (ARC-RENDER-021)

Source: the [roofs](../rendering/roofs.md) of whole maps and their rules (`AreaRoofGrid`), checked against the
[first building research](first-building-evidence.md#roofs-fr-scene-010) (ARC-RES-029). One reading by this task
and a second by its independent reviewer.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| RF1 | roof slot (0x4395C0), roof block tile (0x4395E0), roof screen position (0x439640), roof art at a tile (0x4396A0) | The slot of a sector-local tile index is `x/4 + 16·(y/4)`; a block is drawn from its first tile plus 2 on each axis, at that tile's screen origin moved 120 left and 200 up, less the frame's hot spot; the art is read from the sector holding the tile. | Original |
| RF2 | roof draw (0x439140) | With the roof switch on and the isometric view, each block of the drawn area is visited in steps of 4 tiles; a missing or filled roof is skipped; with hardware drawing the roof is tinted by the outdoor light colour (constant colour blend). A faded roof under the default roof blit mode (constant alpha, `0x100`; without 3D hardware the roof init switches it to stippling, `0x10000`, and a faded roof is stippled) is drawn with its alpha interpolated between four corner values chosen per piece from the three `roofshade.mes` values (opaque, partial, transparent: the table in `AreaRoofGrid.FadeCorners`, read for all 13 pieces); under another roof blit mode that mode is used instead. Roofs are drawn after all objects. | Original |
| RF3 | roof recalculation (0x439D30) | On a player tile change the block under the tile (no offset) is filled when the piece's band holds the tile's `(y % 4, x % 4)` and unfilled otherwise, read for each of the 13 pieces; only a change starts the flood. | Original |
| RF4 | roof cover test (0x43A030) | With the roof switch on, a tile counts as covered only when its tile art is of type 0 (the type getter 0x5038C0 returns bit 8 of tile art, bit 25 of facade art and 0 for any other art, so a missing tile also counts), the block holding the tile three further on both axes has a roof, is not filled and its band holds `((y + 3) % 4, (x + 3) % 4)`; a faded roof covers only when the caller counts faded roofs. The band table of all 13 pieces equals the edge bands of the research. | Original |
| RF5 | roof fill flood (0x43A1A0) | A block whose fill already equals the request stops the flood; otherwise the flood arriving from the east (1) stops at north-west outside, west and south-west outside, from the south (3) at north-west outside, north and north-east outside, from the west (5) at south-east outside, east and north-east outside, from the north (7) at south-west outside, south and south-east outside; else the bit is set and the flood goes to the blocks east, west, south and north. | Original |
| RF6 | roof fill off and on (0x439EA0, 0x439EC0), fade on and off (0x439EE0, 0x439F20), faded test (0x439FA0) | Fill off and on flood from the block of the tile; fade sets or clears the bit only when it differs (on a tile without a roof fade on still writes the empty slot back and marks the sector as having a roof list, a side effect `AreaRoofGrid` leaves out); a roof is faded when it exists, is not filled and carries the fade bit. | Original |
| RF7 | frame data of art (0x5031C0, roof branch) | A mirrored roof frame (flag bit 0) is drawn with hot spot x 0 and offset x 0. | Original |
| RF8 | teleport processing on the same map (0x4D39A0, around 0x4D3A1A) | For the local PC (outside a network game) the wall check is flushed and the roof of the tile it stands on, the tile it leaves, is unfilled before it moves (0x4EDF20); the flush (0x437DB0) also clears the pending roof update, and the move stores the PC's new location and sets it again (0x438530), so the next update (0x437E10) recalculates the roof of the new tile (0x439D30). | Original |
| RF9 | wall check: add a wall (0x438570), its roof count (0x438720), clear (0x438A50) | A wall joining the check stores its tile's normalized roof tile and counts it per tile; the first count fades that roof on, and the release of the last unfades it (a release without a count only logs a warning). Both add sites join a wall without testing its flags, so a wall whose flags forbid fading counts too; that flag only keeps its own alpha (0x438830). | Original |
| RF10 | roof hit test (0x439890), object pick (0x43D9F0) | With the roof switch on, the tile under the point moved 120 down is looked up; when its block has a roof that is not filled (faded or not) and the point falls inside that frame's rectangle on an opaque pixel (0x502FD0: palette indexes 0 and 1 miss; its mirror branch serves other art classes, so a roof frame is tested unmirrored), the pick finds no object at all. The pick does not call the roof cover test (0x43A030), whose callers are the object draw and one other wrapper. | Original |

## Starting kit (ARC-WORLD-034)

Source: the [best items](../world/equipment-runtime.md#best-items-arc-world-034) of the equipment runtime: which items a
critter wields when the game asks for its best ones, as a new game does for the pre-made hero. Read on 2026-10-18 by a first
reader and, independently, a second reader that saw only the questions (method step 6); both agree on every row, and both
confirm the earlier LM1 and LM2 verdicts. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| WB1 | best item for a slot (0x465170), the current item | The slot's item stays as the worth to beat when it still passes the wear check (0x464D20) and the slot is not the weapon slot; otherwise it is taken off (0x464C50), so the weapon slot always starts empty. | Original |
| WB2 | best item for a slot, the candidates | Every item of the inventory list in turn that passes the wear check. Weapon slot: a weapon with an ammunition type needs at least its consumption in the carried quantity of that type. Other slots: an item worn anywhere is skipped; a strictly higher worth (0x461F20) wins, no tie rule. | Original |
| WB3 | best item for a slot, the weapon score | Each damage type's `(min + max) / 2` of the weapon damage (0x465F70, with the magic adjustment) is **added** over the five types (LM1), times the skill effectiveness, divided by 100 (signed, truncated). The skill (0x465D80): a technological skill needs a tech level above 0, a basic skill other than melee a level above 0, else −1; an effectiveness of 0 counts −1 too. | Original (LM1: the reconstruction assigns) |
| WB4 | best item for a slot, the choice | The best score starts at the lowest integer; a strictly higher score wins, an equal score wins when its object handle is larger (signed 64-bit). With a target, a weapon whose range (0x465E30) reaches the target's distance competes for the in-range best the same way and **its score is stored** (LM2); an in-range best overrides the overall best. | Original (LM2: the reconstruction never stores the in-range score); the handle order is not session state, so a tie goes to the later-created item (project substitute) |
| WB5 | best item for a slot, the end | A best item is wielded (0x464AE0, also when it already is the slot's item); none in the weapon slot takes the weapon off again. | Original |
| WB6 | best items for every slot (0x4654F0) | Slots 1000 to 1008 in increasing order, the target passed through. The pre-made menu step (0x545B60) calls it for the new PC without a target before the game starts; a custom character instead gets only weapon and body armour into empty slots (0x5412E0, under a flag the name-chosen step 0x545780 sets). | Original |
| WB7 | item worth (0x461F20), item identified test (0x464370) | Not identified: 300; else the stored worth, at least 2. Identified: an item type with complexity ≤ 0, or item flag bit 0x1. | Original |
| WB8 | thrown item damage (0x462330) | A weapon with the throwable flag (0x10) reads its own damage arrays; anything else has normal damage `(weight + 99) / 100 .. (weight + 49) / 50` from its weight (0x461370, with the owner) and no other type. | Original |

## Blessings and curses (ARC-WORLD-035)

Source: [durable effects runtime](../world/durable-effects-runtime.md#blessings-and-curses-arc-world-035) and
[FR-CHAR-023](../requirements/characters-skills.md#fr-char-023). Read on 2026-10-18 by a first reader and, independently, a
second reader that saw only the questions (method step 6); both agree on every behaviour below. The blessing and curse
functions are the same code with their own fields (numbers 0x104 and 0x106, times 0x105 and 0x107), message file, effect
cause (3, 4) and UI message type. Add and remove begin with a network branch that the reconstruction leaves out; `multiplayer_is_locked`
is true in single player, so it is never taken. Every direct branch stays inside `.text`; there is no indirect one.

| ID | Function (address, bless / curse) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| BC1 | copy field (0x4C4170 / 0x4C3CC0) | Clears the caller's buffer; a number below 50 (signed) or a missing entry `number × 10 + field` leaves it empty; otherwise the text is copied without a length limit. | Original; the reconstruction's bounded copy of a blessing's text is its own change (the 80-byte buffer of BC2 would overflow on a text of 80 characters or more; the installed effect entries have 1 to 3) |
| BC2 | get effect (0x4C4420 / 0x4C3F70) | Field 1 into an 80-byte buffer, then the CRT `atoi` (`atol`: white space, one sign, digits up to the first other character, no overflow check). An empty or non-numeric text gives 0. | Original |
| BC3 | has (0x4C4280 / 0x4C3DD0) | A PC only; the number in its list. | Original |
| BC4 | add (0x4C42F0 / 0x4C3E40) | A PC only; nothing when held; appends the number and the game time, the effect add (0x4E9F70) with the effect of BC2 and the bless or curse cause; for the local PC message 1000, a UI message and the logbook button. | Original |
| BC5 | remove (0x4C4450 / 0x4C3FA0) | A PC only; the first entry only; later numbers and times move up; both lengths shortened; the typed removal (0x4EA100) with BC2's effect of the number; the logbook button for the local PC. Not held: nothing. | Original |
| BC6 | BC2 with the installed `gamecurse.mes` | Curses 52, 53 and 54 hold a placeholder text that is no number, so receiving one adds the module's effect 0 (strength +6, beauty −6, intelligence −6, prowling −3, age +20). Unreachable: the three are unused placeholder slots (their name and description are a placeholder too); the ten scripts that give curses use 50, 51 and, through the counters of the eight installed altars, 55–58 and 60–63. | Original (data placeholder, unreachable); no switch, both modes follow the original (owner decision 2026-10-20) |
## Auto-level schemes (ARC-WORLD-036)

Source: [advancement runtime](../world/advancement-runtime.md) and
[FR-CHAR-021](../requirements/characters-skills.md#fr-char-021). Read on 2026-10-17 by a first reader and, independently, a
second reader that saw only the questions (method step 6). They agree on every behaviour below. The second reader also
noted that the original logs each bought spell in the purchase text and keeps an unreferenced scheme preview (0x4A6E90:
a copy of the critter with 1,000,000 points runs the scheme); neither changes a purchase. Several functions begin with a
network branch (a client returns at once, a multiplayer PC reads its scheme elsewhere); a single-player game never takes it.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| AL1 | scheme run (0x4A7030) | Scheme 0 or no rule: nothing, not completed. Otherwise the rule runs; only when it completes and the critter is an NPC does the standard rule 70 run after it, and its result is returned. A PC never runs rule 70. | Original |
| AL2 | scheme rule lookup (0x4A6E40), scheme read (0x4A6D40) | Scheme numbers below 1 or from 200 give no rule; 1..49 come from `rules\userlevel.mes`, 50..199 from `rules\gamelevel.mes`; a missing entry gives none. | Original |
| AL3 | scheme rule run (0x4A7340) | A token up to the next space, its target the C library's `atoi` of the text up to the next comma (a missing number passes a null pointer, A3; both modes refuse such a rule at read time); tokens compared ASCII case-insensitively against stats, basic skills, tech skills, colleges, tech disciplines and `maxhps`/`maxfatigue` in that order. A step that runs out of points ends the rule (not completed); one that fails otherwise goes on to the next token; an unknown token ends the rule (not completed). | Original (a missing comma lets `atoi` swallow the next step: data, AL14) |
| AL4 | stat purchase (0x4A75E0) | From the **base** value, each unit costs the stat cost of the next value and is paid from the **base** unspent points; the base is written and read back (a value the race maximum clamps fails the step); the loop goes on from the read-back **base** value. The reconstruction continues from the effective value and reads the effective unspent points, so an effect that lowers the stat would make it pay for the same unit again. | Reconstruction slip |
| AL5 | basic and tech skill purchases (0x4A76B0, 0x4A77A0), points setters (0x4C6000, 0x4C66E0), skill levels (0x4C5EB0, 0x4C65E0), key-stat cap (0x4C5F70) | One point (cost 1) while the skill's level is below the target; a point whose `4 × points` is above the key stat's cap (3, 7, 11, 15, 19, 20 for stats up to 5, 8, 11, 14, 17, 20) is refused and the key stat is raised by one instead (AL4), any failure of that ending the step; the loop compares the level read before the stat raise. Levels: 4 × points (melee 20 for a monster), then the skill's effects, clamped 0..20, capped by the effective key stat. The tech setter moves the tech points by the change. Key stats as the skill tables give them. | Original |
| AL6 | spell purchase (0x4A7890), spell add (0x4B1790), spell level minimum (0x4B1770), spell minima (0x4502E0, 0x4502F0) | The college's next rank while below the target, cost 1; the add needs the next rank, character level 1, 1, 5, 10, 15 by rank, intelligence 5 and the spell's willpower (the record's value, before the table loads 6, 9, 12, 15, 18 by rank) and raises the magick points by 1. A rank the level refuses fails the step; one refused otherwise raises intelligence (when below 5) or else willpower by one. | Original |
| AL7 | AL6 with a target above 5 | The spell number runs on into the next college's first spell (past the last college, the mastery field). | Original bug (no installed scheme has one; both modes refuse such a rule) |
| AL8 | tech degree purchase (0x4A79C0), degree raise (0x4AFEC0) | The next degree while below the target, cost 1; the raise needs intelligence 0, 5, 8, 11, 13, 15, 17, 19 by degree and stops at 7, and adds its cost to the tech points; a refused degree raises intelligence by one. A target above 7 at degree 7 reads the cost one entry past its table. | Original; the read past the table is an original bug (no installed scheme has one; both modes refuse such a rule) |
| AL9 | hit and fatigue point purchase (0x4A7AA0) | While the maximum is below the target and the base unspent points are at least 1: the bought points rise by one (each adds 4 to the maximum before effects), then a maximum that did not rise by at least one fails the step (the point stays written, unpaid); otherwise 1 point is paid. | Original |
| AL10 | stat cost (0x4B0F50) | The value is clamped to 1..20; reaching 1 costs 0, every higher value 1. | Original |
| AL11 | level set (0x4A6F00) | Resets the sheet to its defaults (keeping gender, race, alignment and age), sets the level, no bought points, the level's experience and the points of the levels above 1, then runs the scheme and clears the damage. Its only caller is the player creation (0x40DDDA, level 1), so a new PC's scheme spends its five starting points. | Original |
| AL12 | level recalculation (0x4A69C0), follower level update (0x4A6CB0) | A PC's scheme runs once for each level written, after its points, never in the last-level branch; a follower's after its new level and points, only when the level rose. | Original (as XP5, XP7) |
| AL13 | network branches of AL1, AL3, AL5, AL8 | A network client returns before any purchase; a multiplayer PC reads its scheme from another table. | Out of scope (single player) |
| AL14 | data (`DAT_ROOT`) | All 58 installed rules parse (57 in `gamelevel.mes`, one in `userlevel.mes`); no college target is above 5 and no degree target above 7; scheme 76 (a pre-made hero's) has one missing comma, so one step is lost as the original reads it; no spell record names its own willpower. Every pre-made hero has a scheme (76..87) that spends its five starting points; Virgil carries two effects and scheme 91. | Data |

## Map patch (ARC-DATA-028)

Source: the [map patch contract](../world/map-patch.md). Question: when and how does the original apply
`Rules\map.patch`, and to which objects? Read on 2026-10-18 by a first reader and, independently, a second reader that
saw only the questions (method step 6); both agree on every row, and the second reader added the line-buffer overrun,
the npc-flag table's null entry and the editor-mode branch. Every direct branch of these functions stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MP1 | `map_load_mobile` (0x410D10), `map_open` (0x40F4E0, call at 0x40F794) | The patch is read on every map open (new game, map change, save load), after the map's mobile file and the save's difference and deletion files and before terrain, jump points and any sector, so it overrides restored save state and reaches only objects already loaded (a sector object only through a `P` ID, by a location search against the previous map). Editor mode skips it. | Original |
| MP2 | the same | Text mode (CR LF read as LF). `fgets` is given 4,196 bytes but the buffer holds 4,096, so a read of 4,096 characters or more overruns the stack (latent). A line starting with `#` or LF is skipped; a read without LF before the end stops the file; the last character is always cut, so a final line without LF loses its last real character. | Original bug (overrun, latent); otherwise Original |
| MP3 | `objid_id_from_str` (0x4E66B0), GUID parser (0x4E67A0), hex reader (0x4E6AA0) | Upper-case kinds only: `A`, `G`, `P`, `N`, `B`; `H` and others fail and are logged, and the line is skipped. A `G_` GUID's first group is stored as a 32-bit number, the next two as 16-bit numbers and the rest byte by byte: the Windows memory layout, as object files store it, so lines do find their objects. Each group is read by `strtoul` after a check of its first character only. Text after the GUID is not checked. | Original (the object files' layout, so `G_` lines find their objects) |
| MP4 | `map_apply_obj_patch` (0x411940), token reader (0x411880) | The attribute text starts at the line's first `:`; a found object on a line without one dereferences a null pointer (a crash). Keys and values compare ignoring case, tokens split on C white space, numbers are `atoi` (stand points `_atoi64`, x low and y high). Keys and fields: `internal_name` 22, `known_name` 23, `alignment` stat 19, `origin` 291, `notify_npc` 49 on a portal and 62 on a container (others read the value and change nothing), `magic_hit_adjust` 115, `obj_flag` 19, `portal_flag` 46, `item_flag` 87, `critter_flag` 218, `npc_flag` 280, `training`, `script` 32, `daystand` 289, `nightstand` 290, `magic_tech_complexity` 96, `level_scheme` 245, `faction` 292; a field the object's type lacks is not written. An unknown key stops the line. A flag key reads the field, then sets the named bit, or clears it after `!`; a name not found writes the field back unchanged, except in the npc table, which ends in a null entry, so an unknown npc flag name fails there. | Original bugs (missing `:` and unknown npc flag fail) |
| MP5 | `stat_base_set` (0x4B0980) | `alignment` changes only a PC or an NPC: clamped to −1000..1000, never refused (no skill depends on it); for a PC whose value changes, its followers re-check their leader. | Original |
| MP6 | `basic_skill_training_set` (0x4C6170), `tech_skill_training_set` (0x4C6850) | `training` takes a tier word (`st_untrained`, `st_beginner`, `st_expert`, `st_master`) and a skill name; the tier may rise one step at a time and needs a skill level of 0/1/9/18; the educator background then teaches followers. Another word leaves the skill name to be read as a key. | Original |
| MP7 | `script_load_hdr` (0x44BCC0), `script_name_build_scr_name` (0x4CEDC0) | `script` attaches at the given slot the script's 8-byte file header (flags, counters) and its number, but only when the number names a script file: number 0 or an unlisted number attaches nothing and logs nothing, so `script N 0` cannot clear an attachment. | Original bug (switch candidate: the installed patch has five such lines) |
| MP8 | `obj_arrayfield_store` (0x4088B0), 0x40D2A0 | Writing one element of an array field the instance does not hold first copies the prototype's whole array. | Original |

## Logbook (ARC-UI-008)

Source: the [viewer logbook](../rendering/terrain-viewer.md#logbook-arc-ui-008), the [story domain](../world/game-session-runtime.md#story-domain)
and [FR-SOCIAL-008](../requirements/story-social-ai.md). Question: what the original logbook shows on its rumors and quests
pages, which text it reads, how it opens and how a rumor is quelled. Read on 2026-10-18 by a first reader and,
independently, a second reader that saw only the questions (method step 6); both agree on every row. Every direct branch
stays inside `.text`; the indirect ones (the key handler's character table, the tab switch of the list builder, the dialog
action table) stay inside their functions. LB8 is a slip of the reconstruction (the first verdict of the table above), so
no rule switch; Steamwoven reads both files as the original does.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LB1 | in-game message filter (0x54B5D0) | The character messages `l` and `L` (one case of the filter's character table, next to `c`/`C` of CS1) call the logbook toggle with the local PC, under the guards of CS1 (not while another critter acts in turn-based combat, a text edit owns the keys or the main menu is active); an interface sound follows every handled letter. The interface's logbook button calls the same toggle. A keyboard key for the logbook was not found in the earlier reading of the key handlers; the executable has one. | Original |
| LB2 | logbook toggle (0x53F020), logbook close (0x53F090) | Already open: closes it (a toggle). Otherwise a null object returns; a dead **local PC** (not the argument) refuses; the interface mode is set to main, then to the logbook, either refusal refuses; then the object is stored and the window created. Close sets the main mode, then destroys the window and forgets the object. | Original |
| LB3 | logbook create (0x53F0E0), destroy (0x53F2F0), panel draw (0x53F490), tab switch (0x53F5F0), reset (0x53EFD0) | The tab and the page spread are kept across close and open: opening rebuilds the lists and redraws the kept spread. Clicking a tab resets the page to the first spread; a game reset (load, new game) sets the rumors tab and the first page. Holding left Ctrl and left Alt while opening shows a hidden page of developer quotes in place of the rumors. Neither opening nor creating pauses the game. | Original |
| LB4 | entry timestamp (0x5403C0), rumor entry (0x540310), quest entry (0x540470) | An entry starts with a date line: the month name (message month + 6 of `mes\logbk_ui.mes`), the day, a comma, the year, two spaces, the hour on a 12-hour clock (hour 0 is 12 am, hour 12 is 12 pm) with two minute digits and "am"/"pm", then four spaces. A quest entry appends its state label (message state + 19) right after it. A newline follows, then the rumor's text or the quest's description. | Original |
| LB5 | logbook list builder (0x53FBB0), rumors and quests tabs | Rumors: struck through in black when quelled, plain black otherwise. Quests: accepted and achieved in blue; completed struck through in green; completed by someone else and botched struck through in red; mentioned in plain black. | Original |
| LB6 | quest logbook list (0x4C52E0), its comparator (0x4C53A0) | Only a PC has quests. Every quest record whose state without the botched bit is not Unknown is listed (a quest botched while still Unknown is not), reported Botched when the botched bit is set and with its stored state otherwise, then sorted by the date-time of its last change, oldest first, with the run-time library's sort, which is not stable: the order of equal times is the sort's. | Original |
| LB7 | quest description (0x4C5250) | The output starts empty; the low-intelligence description is used when one is loaded and the object's intelligence is 4 or less, otherwise the normal one; with neither the description stays empty. | Original |
| LB8 | quest module load (0x4C4670) | `rules\gamequest.mes` is parsed for quests 1000..1999; only when that file loads are `mes\gamequestlog.mes` (normal descriptions) and `mes\gamequestlogdumb.mes` (low-intelligence descriptions) loaded, each searched for 1000..1999 in its own file; the second is tried even when the first is missing. The earlier reading searched the normal file for both, which made the low-intelligence descriptions the normal ones. | Reconstruction slip |
| LB9 | rumor logbook list (0x4C5A40), its comparator (0x4C5AF0) | Every rumor 1000..2999 with a non-zero learning time is listed with its quelled mark, sorted by that time, oldest first, with the same unstable sort. | Original |
| LB10 | rumor logbook text (0x4C5920, 0x4C5960, 0x4C59D0), rumor module load (0x4C5570) | Module load reads `mes\game_rd_npc_m2m.mes`, `…_m2f`, `…_f2f`, `…_f2m` and `…_m2m_dumb`. A rumor's logbook text is message 20 × number of the first file, or of the last one when the object's intelligence is 4 or less; a missing message gives an empty text, with no fallback to the other file. | Original |
| LB11 | rumor quell (0x4C5690), multiplayer lock test (0x4A2BA0) | In single player the rumor's bit (number − 1000) of a game-wide bitmap is set; there is no range check. A multiplayer game sends a message as well. No function clears a bit except the module load. | Original |
| LB12 | dialog action executor (0x415BA0) | `RQ n` quells rumor *n* with no participant and no PC test; `RU n` marks rumor *n* known to the dialog's PC. | Original |
| LB13 | date-time hour (0x45A870), minute (0x45A8B0), day (0x45A8E0), month (0x45A900), year (0x45A920) | From the (days, milliseconds) pair, unsigned: hour = ms / 3,600,000 mod 24, minute = ms / 60,000 mod 60, day = days mod 30 + 1, month = days / 30 mod 12 + 1, year = start year + days / 360. | Original |

Not modelled by Steamwoven: the order of equal times (Steamwoven orders them by number), a rumor learned at game time 0
(the original's zero time means not known; no profile starts there), the developer quotes page, the
other five tabs, the book page spreads, the month and state labels of `logbk_ui.mes` (the viewer uses its own), the
logbook button's highlight and the sounds.

## Limits census (ARC-DATA-029)

Source: the [limits census](../world/limits-census.md). Question: how many maps can the original's map list hold, which
bounds the maps a session can have changes on? Read on 2026-10-18 by a first reader and, independently, a second reader
that saw only the question (method step 6); both agree.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LC1 | `map_init` (0x40EA90), `map_list_info_load` (0x4125C0), `map_list_info_add_internal` (0x412C90) | The map list is one table of 200 entries of 280 bytes, allocated at map initialisation. The loader reads the map list rules from message number 5000 upwards and stops at the first gap; after each entry it stops when 200 are stored, unloads the file and reports success, so further entries are ignored without a log. Adding one map later refuses when the table is full. No other path adds an entry (the other writers reuse an existing slot found by name; the unload resets the count). A name of 256 characters or more fails the load. | Original |

## Main-map streaming (ARC-DATA-030)

Source: [main-map streaming](main-map-streaming.md). Question: how does the original keep a map's sectors in memory while the
game runs, and how is one sector built? Read on 2026-10-18 by a first reader and, independently, a second reader that saw
only the questions (method step 6). Both agree on every row. The second reader added the lock's refusal without an open map,
the network callers of the pre-cache and the detail of SS8; the first reader re-read the first two and the hour test of SS8.
Every direct branch stays inside `.text`; the save and load of a sector are reached through two function pointers set at
sector initialisation (game or editor).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SS1 | sector init (0x4CEF70), cache init (0x4CF810) | The sector cache holds 16 sectors; the cache init clamps a request to 8..128 and refuses while a sector is locked. Nothing else calls it. | Original |
| SS2 | sector lock (0x4D0540), limits (0x4CF790), map open (0x40F4E0, call at 0x40FB1B) | A sector is loaded on its first lock and kept in the cache. A lock fails while another is being taken (warning), while a walk over the cached sectors runs (silent) and when no map is open (logged). Sector x and y must lie below the map's tile width and height from `map.prp` divided by 64. A hit counts a reference and refreshes its use stamp; a miss with a full cache saves and evicts the least recently used entry without references, and fails when every entry is referenced. | Original (the reconstruction only logs a missing map and carries on) |
| SS3 | eviction (0x4D1400), lock (0x4D0540) | Eviction records the sector id and the game time in a history of twice the cache size (32), dropping the oldest. A sector loaded again takes the time elapsed since that record; one not in the history takes "never". | Original |
| SS4 | pre-cache (0x40FE00), teleport (0x4D3460, through 0x4EF1E0), map open in game (0x40FC70) | Locks and unlocks the 3×3 sectors around a location. A teleport of the local PC runs it at the target, after any map change; in a network game it runs for each player and on a message naming the local PC. Map open in game runs it only when asked, and both of its callers pass "no". | Original (the reconstruction calls the pre-cache from the teleport directly) |
| SS5 | game sector load (0x4D1A30) | Sources in order: the map's own `<id>.sec`, the terrain template path, fill (outside optional demo limits the own file is skipped). A template sector passes its real id to the light and object loaders, an own sector "no id". A `.dif` is looked up in the save folder under the real id for own and template sectors; a filled sector reads nothing. Sections: lights, tiles, roofs, placeholder, then by placeholder version tile scripts, sector script, town map, aptitude, light scheme, sounds, block mask, then objects, each with its `.dif` part when flagged. Then the object fold (flag: at least 3,000 ms of game time since eviction), the light fold, and an art pre-cache for a sector with town-map data. | Original |
| SS6 | object list load (0x4F1350) | A template object's location becomes the real sector's origin (x × 64, y × 64) plus the object's tile inside its slot, and its change bits are cleared. | Original (with Wilderness A1 for lights) |
| SS7 | object fold (0x4F1CD0), 0x441FC0, 0x4AD790 | Every object found at the sector that is not in an inventory and not static joins the sector's list. Outside the editor, an NPC that is not destroyed has its AI time event replaced by an immediate one carrying the SS5 flag. | Original |
| SS8 | light fold (0x4F7940) | Day is hour 6..17. Nocturnal scenery (flag 0x4) not in an inventory is switched on on a tile whose art type bit is clear, otherwise off by day and on by night. Other objects, and nocturnal scenery left on, add their lights to the sector. | Original |

## Terrain templates (ARC-DATA-031)

Source: the [map catalogue](../world/map-catalog.md#generated-sectors-arc-data-031) and the
[generated-wilderness study](generated-wilderness-evidence.md). Question: where the terrain type names come from and how a
terrain word becomes a template path. Read on 2026-10-18 by a first reader and, independently, a second reader that saw
only the questions (method step 6). Both agree on every row. The second reader added the variant counts' only consumer and
the terrain init's transition check. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TR1 | the Terrain entry of the map subsystem table (0x59F2A4), terrain init (0x4E7B00) | The entry has an init, a reset, an exit and map-new and map-close functions but no module-load or module-unload function, like Proto. The init loads `terrain\terrain.mes` once, then builds the type names, the type count and the transition table, each failing the init. Nothing loads the file again, so only the base sources count. | Original |
| TR2 | type names (0x4E8EA0) | Messages 0, 1, … up to 31 by exact number, stopping at the first missing one. A text containing `/b` (case-sensitive) marks the type blocked; the mark, everything after it and the white space before it are cut in place. A text starting with the mark has no guard. | Original |
| TR3 | type count (0x4E8F40) | The count is the number of those messages. For each type it counts the files `terrain\<name>\<slot>.sec` for variants 0..3 up to the first missing one; the init fails when there is no type or type 0 has none. The counts are read only by the word builder for new maps. | Original |
| TR4 | terrain word (0x4E87F0), fields (0x4E8DC0, 0x4E8DD0, 0x4E8DE0, 0x4E8DF0) | A sector outside the grid reads 0xFFFF. Base type bits 11..15, second type bits 6..10, transition code bits 2..5, variant bits 0..1. | Original |
| TR5 | template path (0x4E84C0), variant slot (0x4E8E60), slot table (0x5B9968), type name (0x4E8B20) | Plain when base = second or the code is 0: `terrain\<base>\<slot>.sec`, slot (variant mod 2, variant / 2). Otherwise the table entry of the code; when it is -1 the pair swaps (second to base) and the entry of 15 − code is used: `terrain\<from> to <to>\<slot>.sec`, a still missing slot written as `-1`. Slots: code 1 (1,0), 2 (1,2), 3 (1,1), 4 (3,2), 5 (0,0), 6 (2,2), 8 (3,0), 9 (2,0), 12 (3,1), the others none. A type at or above the count has no name; the path then appends a null string (a fault, Wilderness A6). | Original |

Not modelled by Steamwoven: the transition-message check of the init (0x4E90B0), which only an invalid module fails; the
variant counts beyond type 0; the error log lines.

## AI parameters and follow consent (ARC-DATA-033)

Source: the [AI parameter table](../world/ai-parameters.md) and the [follow check](../world/party-runtime.md#follow-check-arcanumsimulationparty)
that companions on any map need. Read on 2026-10-18 by this task and, independently, by its reviewer, who formed its own
answers before comparing: both agree on every row (the reviewer added the details of the controller lookup and the
32-bit level sum, which the code now follows).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| AP1 | the AI entry of the game library table (0x59A90C: init 0x4A8320, module load 0x4A8400, module unload 0x4A84C0 and exit 0x4A83F0 return at once) | The init loads `rules\ai_params.mes` before the module is mounted and fails when it cannot; the module load loads the module's `rules\gameai.mes` and succeeds without it. Each looks up the entry numbered exactly 0 (100), and when it is missing fills nothing; otherwise that entry and each next entry in number order fill consecutive rows of 17 integers, starting at row 0 (100) and stopping before row 100 (150). A row's text is split with `strtok` on space, tab and line feed, each token read with `atoi`, at most 17 tokens; missing tokens leave the field as it was (zero in the static table). Unloading the module keeps rows 100..149. | Original |
| AP2 | AI parameters of an object (0x4AAA60) | Reads the object's field 282 (the AI data) and copies the 17 integers of that row, without a range check. | Original |
| AP3 | follow check (0x4AD950) | Results 0 accepted, 1 too good, 2 too bad, 3 dislike, 4 already in group, 5 limit reached, 6 not allowed, 7 low level. A mind-controlled NPC (spell flag 0x8000000) accepts when its controller is this PC, or when no controller is found and it has a PC leader. A PC with Persuasion training Master (3 or more) goes straight to the limit steps; otherwise: another leader with charisma at least the PC's refuses (4); a forced follower (NPC flag 0x400) accepts; reaction (0x4C0CC0) at or below field 5 refuses (3); a PC alignment above the NPC's by more than field 6 (1), or below or equal and the NPC's above by more than field 7 (2); an NPC level at or above the PC's plus field 8 (7). The limit steps, skipped when the caller ignores the charisma limit: a max-followers stat of exactly 0 refuses (6), then the PC's follower count without forced followers (0x45E3F0) at or above it (5). | Reconstruction slip (the earlier reading accepted a Master persuader at once) |
| AP4 | stat getter (0x4B0490) and stat base level (0x4B0740), max followers (stat 15) | The base level is the effective charisma divided by 4 (truncated toward zero); the getter adds 1 at Persuasion training Expert (2) or more, then the critter's effects, then clamps to the stat's minimum (0x4B0FC0: 1 from the table at 0x5B5194) and maximum (0x4B0FD0: 7 from the table at 0x5B5204; its race cases are other stats'). | Original |
| AP5 | stat getter (0x4B0490) and stat base level (0x4B0740), level (17) and alignment (19) (ARC-WORLD-037) | Neither has a case of its own: the base level is the stored stat (field 220), then the getter applies the critter's effects and clamps to the tables: level 0..51, alignment −1000..1000. The effect adjustment (0x4EA690, flag 0) walks every entry of the critter's effect list with no cause filter; an effect number without a definition contributes nothing. Second reading by the reviewer agrees. | Original |

## NPC AI core loop (ARC-RES-054)

Source: [NPC AI core loop](npc-ai-core-loop.md) (plan stage A1, source units S1–S5). Question: where the AI parameters come
from and who reads them, when an NPC thinks in real time and in a round, the order of one AI pass, and how the danger state
changes. Read on 2026-10-18 by a first reader and, independently, a second reader that saw only the questions (method
step 6). Both agree on every row. The second reader added CL4, CL5, the conversation slot's writers and the negative-distance
detail of CL8; the first reader re-read them and agrees. Rows P5, LM3 and LM4 above were read again with the same result.
Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| CL1 | gate (0x4A8570) | A stunned NPC never passes the gate: when the stun animation (goal 57) no longer runs, the flag is cleared and the pass still ends; while it runs, the pass ends too. | Reconstruction slip (it lets a still-stunned NPC act) |
| CL2 | gate (0x4A8570) | After the full re-wield (all wear slots) both "check wield" and "check weapon" are cleared; after the weapon-only re-wield (slot 1004) only "check weapon". | Reconstruction slip (it keeps "check weapon" after the full re-wield) |
| CL3 | pass (0x4A84F0), setter (0x4AABE0), AI event (0x4AD200), delayed scheduler (0x4AD730), follower catch-up (0x4AD4D0) | On a network client (network flags at 0x62AC6C: bit 0 active, bit 1 host clear) the pass, the setter and the AI event do nothing (the setter returns the request; the event schedules nothing), and the delayed scheduler adds no event; the immediate scheduler (0x4AD790) has no such test. In any network game (host too) the catch-up ends the follower's conversation before moving it. | Original (the reconstruction leaves multiplayer out; single player unaffected) |
| CL4 | setter (0x4AABE0), leave combat mode (0x4B3F20) → toggle (0x4B3F80) | On a surrender the setter leaves combat mode, then writes the critter flags it read before. When the NPC was in combat mode and the toggle acted (it refuses for a paralysed, stoned or stunned critter and during a non-PC's conceal fidget), the toggle cleared critter flag bit 18 and did the rest of leaving combat mode; the write sets bit 18 again, so the NPC still tests as in combat mode. | Original bug; owner decision 2026-10-20: Original switch `SurrenderKeepsCombatModeFlag` |
| CL5 | gate (0x4A8570) | The blind, crippled and stun writes use the critter flags read before the combat scripts of the same pass, so they undo a flag change those scripts made. | Original bug (latent); owner decision 2026-10-20: Original switch `GateRecoveryWritesStaleFlags` |
| CL6 | AI init (0x4A8320), module load (0x4A8400), unload (0x4A84C0), row copy (0x4AAA60) | `rules\ai_params.mes` from entry 0 until row 99, `rules\gameai.mes` from entry 100 until row 149 (a missing module file loads nothing); each next message by position fills the next row; split on space, tab and line feed, at most 17 values by `atoi`, missing values keep the row's contents; one table of 150 rows of 17; the copy takes the AI-data field's row without a bounds check; the unload is empty. | Original; the positional fill without a bounds check is LM3 (original bug, latent), the empty unload LM4 (original) |
| CL7 | AI event (0x4AD200), near test (0x4AD420), catch-up (0x4AD4D0) | Wake-up marker on a living NPC: "check wield" set, waypoints or standpoints for a leaderless NPC outside combat mode, first-heartbeat script (point 10); not "continue" skips to the far path. Alive, no round and within 30 tiles: heartbeat (point 19); "continue" runs a generator's spawner (success: the spawner's time; failure: an encounter creature is destroyed outside a round and nothing is scheduled) or one pass; then the next event. Far path: outside a round a living NPC's animations are interrupted (priority 4); a PC follower more than 30 tiles from its leader, not waiting, not paralysed and active is moved to the leader; moved or sector loaded: next event; otherwise the encounter check and nothing scheduled. | Original |
| CL8 | delay (0x4AD5D0), distance (0x4AD610) | `250 + 4750 × d / 30` ms, signed truncating division; `d` the distance to the local PC (nearest player in a network game), negative or above 30 counted as 30; the near test counts a negative distance as near. A wake-up tick adds `random(0, 5000)`; the next event carries no marker. | Original |
| CL9 | pass (0x4A84F0), gate (0x4A8570) | Order: map clearing, network client, pass record, gate (dead; hidden unless sleeping; local PC hidden; stoned; conversation partner set, with the dialog-end callback when it is the PC and the state is neither NONE nor SURRENDER; combat scripts 16 then 15 in real time, 15 in a round, triggerer the danger source; not this NPC's turn; animation priority above 2; blind 1 in 160; crippled 1 in 8000; stunned; unconscious; generator; re-wield; demaintain), then heal, else item search, else danger update, then perform. | Original (P5 re-read) |
| CL10 | state read (0x4AAB00), setter (0x4AABE0) | Read: FLEEING (fleeing-from), SURRENDERED (fleeing-from), FIGHTING (combat focus), else NONE. Setter in order: network client; invulnerable, nigh-invulnerable or name 6719 → NONE; FLEE with NO_FLEE → COMBAT_FOCUS; FLEE or SURRENDER with an extraordinarily charismatic leader → COMBAT_FOCUS; COMBAT_FOCUS with the global switch off or NO_ATTACK → NONE; FLEE/SURRENDER: float line once, flags, surrender leaves combat mode, fleeing-from := target; target lock stops a fight request; first fight: FIGHTING, CHECK_WIELD, CHECK_GRENADE, enter-combat (point 13), alerted sound; every fight request against a PC: reaction above parameter 10 lowered to it; focus := target; otherwise BACKING_OFF cleared and, when fighting, FIGHTING cleared, DEMAINTAIN unless FLEE, exit-combat (point 14). | Original |
| CL11 | turn set (0x4B6E70), subturn hook (0x4B7010), subturn end (0x4B7630), animation event (call at 0x423B8D) | A non-local-PC subturn start sets action points to `max(5, Speed)` and runs the hook: for the current non-PC, outside a load, heartbeat (point 19) and on "continue" one pass; no running animation → action points 0 and the turn passes. A finished animation of a PC or NPC in a round runs the hook too. A non-local-PC subturn end runs the end-combat script (point 16). | Original |
| CL12 | callers of the pass, immediate (0x4AD790) and delayed (0x4AD6E0) schedulers | Pass: AI event, ranged miss (0x4B30F5), melee miss (0x4B3948), damage without death (0x4B52FC), subturn hook, follower orders (0x4C389D). Immediate event with the wake-up marker: sector fold of an NPC that is not destroyed (not in the editor). Delayed event: placing a created, duplicated or loaded NPC (not in the editor), and a resurrected non-PC. | Original |
| CL13 | every caller of the row copy (0x4AAA60) | Slots read: should-flee 0–3, flee distance 4, follow check 5–8, leader check 5–7, attack event 9 and 10, setter 10, kill-on-sight 10 and 11, portal guards 10, upset 12, spell choice 13, heal 14, spell chance, perform in combat and back-off 15, portals 16. | Original |

## Implemented functions (ARC-DEV-025)

Source: the [function registry](../verification/functions.csv) rows that Steamwoven implements and that stood at F0 (taken
from earlier readings, not read in the executable). Question: what each function does for the part Steamwoven implements,
and where the implementation differs. Read on 2026-10-18 by a first reader and, independently, a second reader that saw only
the questions and the code (method step 6), with function ends from the executable and constants from its name tables and
the project's records. Both agree on every row; details only one reader followed are marked. Three differences of the map
list (short rows, negative coordinates, the number conversion) the second reader first listed outside the covered part;
asked to re-read them, it confirmed all three. Every direct branch stays inside
`.text`. The stat-name table at 0x5B5384 names stats 0–20 in code order only: it has 23 names, poison sits at its slot 21,
while the code reads the poison level as stat 24 (the setter and the strength and dexterity penalty).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IF1 | map name obfuscation (0x411830) | Each ASCII letter moves 13 places within its case; one that passes Z (or z) moves back by 24, not 26; other bytes stay; then the whole string is reversed. | Original; Steamwoven the same |
| IF2 | map list load (0x4125C0), token readers 0x5318F0 and 0x531A20, number conversion 0x589CB8 | Rows numbered from 5000 until the first missing number, at most 200. Name, X and Y are the first three comma-separated values: a name of 256 bytes or more fails the whole load and the game start; trailing white space stays in the name; a row with fewer than three values dereferences a null pointer (crash); an empty value reads 0; X and Y are read with the runtime's 64-bit `atoi`, which wraps on overflow, and are kept when negative. | Original (the short-row crash is an original bug; the long-name failure is the original's limit, LC1); Steamwoven accepts a long name, trims the name, keeps a short row without a start, drops a negative start and saturates on overflow |
| IF3 | armour class (0x43D880), item getters 0x461590 and 0x4614A0 | Over worn slots 1000–1008, each armour item adds its armour class adjustment plus its magic-adjusted magic armour class adjustment. With the flag argument set (callers 0x464700 and the interface at 0x556782) an unidentified magickal item adds no magic part; the attack path (0x4C8760) passes it clear. | Original; Steamwoven implements the attack path (identification not modelled) |
| IF4 | critter experience table load (0x45CF30), critter worth (0x45F0B0) | The loader opens `rules\xp_critter.mes` (and eleven texts of `mes\critter.mes`) and converts no value; the worth looks up the NPC's level as an exact entry when it is needed and converts it with `atoi`, 0 without an entry. | Original; Steamwoven converts every entry once and refuses a non-number, so a bad entry that no level reaches refuses the whole table |
| IF5 | quest experience table load (0x4C45C0), quest experience (0x4C53C0) | The loader allocates the quest arrays and opens `rules\xp_quest.mes` (a failed open fails the module start) and converts no value; 0x4C53C0 looks the level up as an exact entry with `atoi`, 0 without an entry. | Original; Steamwoven as in IF4 |
| IF6 | quest list module load (0x4C4670), entry parser 0x4C4860 | Entries 1000–1999 of `rules\gamequest.mes`, split on spaces, 23 numbers by `atoi`, the first the quest's experience level; without the file every level is 1. An entry with no token or fewer than 23 numbers hands a null pointer to `atoi` (crash). | Original (the crash an original bug); in Steamwoven an empty entry or a first token that is not a number refuses the whole table, and an entry with fewer numbers is read |
| IF7 | critter kill (0x45D900), damage (0x4B4390) along the kill's path | The damage step runs with the death flag and the current hit points as damage. An invulnerable (tested first), nigh-invulnerable or dead critter stops it; otherwise the hit-point damage grows by the current hit points and the poison is written through its setter (0 for an extraordinary constitution); a critter dead after these writes falls with the plain death; hit-point damage is then set to 32,000 in every case. A damage script on the target can cancel the step. | Original; Steamwoven reports a dead invulnerable critter as already dead where the original reports nothing (the state is the same); scripts are not modelled |
| IF8 | fatigue recovery event (0x45E610) | Steps = max(1, (now − scheduled) / 80), both times cut to whole seconds first, measured from when the event was scheduled; no upper bound; a time before the schedule gives 1. | Original; Steamwoven caps at 1,000,000 steps and refuses a time before the schedule |
| IF9 | resting event (0x45EA00), PC leader 0x45DDA0 | Hours = max(1, (now − scheduled) / 3,600), signed 32-bit and truncated, for an NPC without a PC leader (at the end of its leader chain, second reader). | Original; Steamwoven caps at 87,600 hours and refuses a time before the schedule |
| IF10 | item transfer (0x4617F0 → 0x461810), insert check 0x466510 | The entry calls the transfer with location −1. Ground path in order: the item's script point 4 (it can cancel), the insert check (weight, then technophobia, then a stack or room), off the map, into the inventory list; then the insert-item script, which cannot cancel (first reader), and the decay timer is removed (second reader). A key ring is an ordinary item. | Original; Steamwoven refuses items with a point-4 script, gold, ammunition, keys and key rings, runs the insert-item script through the host's SCR dispatcher (ARC-SCRIPT-013; refused when it uses more than the item set admits) and leaves the insert check to the host |
| IF11 | weapon skill of an item (0x465D80) | No item: melee. Not a weapon: throwing. A weapon: the boomerang flag gives throwing; complexity below 0 with range 3 or more and ammunition type 1–3 gives firearms; ammunition type 0 gives bow; anything else melee. | Original; Steamwoven implements the weapon branch only and reads complexity −2^31 as firearms (the original: melee; no data has it) |
| IF12 | stat getter (0x4B0490) | Before effects and the clamp: poison lowers strength and dexterity by min(3, poison / 100), the poison being the effective level of stat 24; both crippled legs lower speed by 5. The getter also adds ±10 speed under the Tempus Fugit flag, one follower for a training of 2 or more in skill 11 and the PC background's ±2 (stats 0, 1, 4, 6) and ±4 (stat 22). | Original; Steamwoven implements poison (from the stored level; no Steamwoven effect targets poison) and crippled legs only |

## Guard conditions (ARC-SCRIPT-014)

Source: the [dispatcher's teleports and guards](../scripting/scr-dispatcher-runtime.md#teleports-and-guards-arc-script-014-set-s4)
and the guard kinds of [map transitions](world-transitions.md#conditions). Question: what the condition function does for
the five guards that stand most often between a placed object or tile and a teleport. Read on 2026-10-20 by a first reader
and, independently, a second reader that saw only the questions and the addresses (method step 6); they agree on every row.
The second reader added the object-type branches inside the helpers (SG1, SG2, SG5). The teleport actions themselves are
rows TR1, TR2 and TR4. Every call target stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SG1 | script condition (0x4451B0, jump table 0x44714C, entry 6), quest state (0x4C4CB0) | Operand 0 is a focus list, operands 1 and 2 values: an object counts when the quest state of operand 1 for it equals operand 2. The state is the PC's own entry for a PC (6 when the entry's 0x100 bit marks it botched) and the quest's global state (0x4C51A0) for any other object. | Original |
| SG2 | the same, entry 17, item search by name (0x462480) | An object counts when it holds an item whose name field equals the value: the direct inventory of a container or a critter, the first match; any other object holds nothing. | Original |
| SG3 | the same, entry 20 | An object counts when its spell flags (field 20) lack the mind-controlled bit (0x08000000) and its name field (22) equals the value. A mind-controlled object is not counted but stays in the list. | Original |
| SG4 | the same, entry 21, wielded item (0x4649C0) | An object counts when an item it wields in inventory location 1000..1008 has a name field equal to the value; the first such location ends the walk. | Original |
| SG5 | the same, entry 33, reputation (0x4C1CB0) | An object counts when it is a PC with the reputation in its reputation array; any other object has none. | Original |
| SG6 | the same, after each of the five | The focus list is released, then the true branch runs when at least one object counted and either all did or the focus kind is one of 3, 5, 7, 9, 14, 16, 18, 20 or 23 (one is enough); otherwise, and always for an empty list, the false branch. | Original. The dispatcher admits single-object focus only, where "all" is the one object; an absent object stops the run there (project rule of the single-object conditions) instead of taking the false branch. |

## Item insert script (ARC-RES-055)

Source: [item insert script](item-insert-script.md). Question: what the `print_line` action (type 24) of a script does,
for script 1341 on the Crash Site amulet's insert-item and examine points. Read on 2026-10-18 by a first reader and,
independently, a second reader that saw only the question and the addresses (method step 6). Both agree on IS1 and on
the path of IS2. The second reader added that the NPC-line step runs the printed line's action field, the generated-text
prefixes and IS3; the first reader re-read them (the release skip of IS3 directly, the action field through the step's
dialog records) and agrees. IS4 comes from the reviewing reader; the first reader re-counted the six call sites. Every
call target stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IS1 | `script_execute_action` (0x447220), jump table 0x44ADBC | The table is indexed by action type − 1 (entry 43 is the teleport case, map transitions TR1); entry 23, type 24 `print_line`, is the case at 0x447822, which only calls 0x44B390 with the action and the run state. | Original |
| IS2 | print line (0x44B390), NPC-line step 0x413A30 → 0x416840 | Loads the DLG named after the running attachment's script through the dialog cache (0x4CEE60, `dialog_load` 0x412E10) and does nothing without a script number, on a failed load or with a null attachee; operand 0 (`script_get_value` 0x44BA70) selects the line (a missing line prints a single space); the NPC-line step runs the line's action field (DLG action interpreter 0x415BA0, triggerer and attachee) and builds the text (`g:`, `m:`, `r:` prefixes or a whole `i:` generated; else text or alternate text by the triggerer's gender); operand 1 travels with the text to the interface's message callback (0x460630, slot 0x5E86D8 of the table installed at 0x45FD40), or on a network game (0x62AC6C bit 0) by the triggerer's player slot (0x4A2B10: local → the callback, another → a packet 0x4EDA60, none → nothing); the cache reference is released (0x412F40). The function writes no global or object itself. | Original |
| IS3 | print line (0x44B390) | The null-attachee test comes after the DLG load and returns without the release, leaving one cache reference behind. | Original bug (latent: never reached with an item as attachee) |
| IS4 | global-flag write (0x444D20) | Six callers: DLG action interpreter 0x415EF6, script set and clear 0x449A4A and 0x449A72, script value store 0x44BC2F, network relay 0x4EFD19, pre-made hero menu 0x545C1A (Pre-made flags row). None writes a fixed flag of its own. | Original |

## NPC AI awareness (ARC-RES-056)

Source: [NPC AI awareness](npc-ai-awareness.md) (plan stage A2, source units S6–S9; closes combat O4). Question: when a
critter sees or hears another, how the straight line between two tiles is priced for sound and missiles, how an attack
alerts nearby NPCs and what each witness does, and what arming dynamite does. Read on 2026-10-20 by a first reader and,
independently, a second reader that saw only the questions (method step 6). Both agree on every row. The second reader
first read the concealment margin as raising the observer's Perception, having taken the signed reciprocal multiplier
as positive; asked to re-check the sign, it worked the numbers (P 8, margin 85 → P 2) and agrees that the margin lowers it.
The second reader added the view-option dependence of the line (AW17), the dropped cost when a step leaves the map
(AW6), the unconscious test's cases (AW2) and the attack event's flag gate (AW9); the first reader re-read them and agrees.
No first-pass anomaly (P1–P7) belongs to this stage; the F0 summaries of S6–S9 are confirmed or refined. Every direct
branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| AW1 | perception distance (0x4AF240) and its four callers | `clamp(x, 1, 10)`; sight passes Perception after the concealment margin, hearing that minus 4, find target Perception (scan half-size), item search Perception minus 5. | Original |
| AW2 | sight (0x4AF260), unconscious (0x45D800), extraordinary (0x4B0EE0) | 1000 for an observer stunned, blinded or sleeping, or unconscious (true for a non-critter, false for an undead); 1000 for an invisible target (object flag 0x200 or spell flag 0x1) unless the observer's Perception is extraordinary or it detects invisibility (spell flag 0x200); 1000 when not facing; 1000 beyond 1000 tiles; else `max(0, d − clamp(P′, 1, 10))` + 1 when the line of fire reports a blocker (its cost is discarded). | Original (confirms the F0 summary; the early refusals are added) |
| AW3 | facing (0x45EFF0), `object_rot` (0x441B20) | `k = (direction to target − rotation of the current art + 8) mod 8`; facing unless `k` is 3, 4 or 5. | Original |
| AW4 | sight and hearing margin, effectiveness (0x4C62E0 → 0x4C6410), difficulty (0x4C8430) | Invocation: prowler as source, observer as target, skill 6, flag 0x4000 (sight) or 0x2000 (hearing). `M = clamp(E − D, 0, 100)`, `P′ = P − (P × M) / 100` (division by −100 through the signed multiplier). `E`: opposed table on prowling level − observer's Perception, /4, +10 for extraordinary intelligence, local PC ±difficulty, ≥ 0, no 95 cap. `D`: observer paralysed −50 / unconscious −50 / falling or stunned −30 / sleeping −50; sight only and untrained prowler not undead: light `p = (255 − L) × 30 / 255` divided by the prowler's carried light, `30 − p` unless the observer has dark sight; blinded +30; crippled legs +30; sight: a blocker on the observer → prowler line makes `D = −100`, else no cover and below expert +50, else −cover; hearing: minus each worn armour item's silent-move adjustments, halved from apprentice. | Original (confirms the skills study's prowling paragraph) |
| AW5 | hearing (0x4AF470), concealment (0x45EE30, 0x45EE90) | 1000 for a stunned or unconscious observer or beyond 1000 tiles; sleeping halves Perception; a target with critter flag 0x2 (set and cleared with 0x1 by concealment, also on followers) applies the margin; `H = (clamp(P′ − 4, 1, 10) + N[s] − N[0]) / 2 − sound cost`, `N = {2, 8, 15}` at 0x5B50C0; returns `d − H` when `d > H`, else 0. | Original (refines the F0 "loudness 2/8/15": the table enters hearing as 0/6/13) |
| AW6 | sound cost (0x4AF640), line (0x4201C0, 0x4203B0, 0x420330), step cost (0x43FDC0 → 0x43FE00) | Same tile 0; failed line 100. Walk from the observer's tile centre plus its pixel offset along 10-pixel samples (at most 100); each tile change prices the step with flags 0x20 (+0x10 into the target tile, no effect): wall on the crossed edge +2, closed unbroken door +1, soundproof destination +8, other objects free, nothing blocks; a corner step sums four edge steps; a step whose neighbour lookup fails returns 1, dropping its sum. | Original |
| AW7 | line of fire (0x4ADE00), step cost (0x43FE00) | Same tile 0; failed line 100 without a blocker; walk as AW6 with flags 0x0C (+0x10 into the target tile), no mover; returns the summed cost, stopping at the first blocker it reports. Wall pieces 10, 13, 14, 17, 18, 19 pass on an edge step and block in a corner step, pieces 22, 25, 26, 29–32 pass, others block (passwall flag aside); open or broken doors pass; closed doors, destination objects and living critters: not shoot-through → blocker, shoot-through not see-through +50, see-through with provides-cover +20, else 0; dead critters pass; non-wall objects on the target tile are skipped. | Original (closes combat O4) |
| AW8 | callers of 0x4ADE00: 0x425590 (0x425649), 0x429960 (0x429A8D), 0x4294F0 (0x42963C), 0x4C8430 (0x4C88D7, 0x4C8BC6), 0x4AF260 (0x4AF446), 0x4AE720 (0x4AE994), 0x4AC910 (0x4AC9F3), 0x456BC0 (0x456C67), 0x4B3170 (0x4B33E9) | The 26 threshold is in the two animation-goal callbacks: clear shot only with cost < 26 and no blocker but the target (0x429960 also needs distance ≤ max(weapon range, 1)). Ranged skill difficulty: blocker +1,000,000, else +cost. AI spell use: cost ≥ 100 or another blocker → refusal 5. Grenade: any blocker refuses. Magick line of sight: cost ≥ 100 or a non-shoot-through blocker other than the target → none. Ranged resolution: a blocker becomes the struck object. Attack update: another blocker needs a path. | Original |
| AW9 | noise (0x4A9E10), attack event gate (0x4A9650, call at 0x4A98A8), weapon loudness (0x4B2810) | Raised for a critter attacker and critter victim past the event's refusals, after the victim lost concealment, when flag bits 1, 2 and 4 are clear. Radius `N[s]`: NPCs in that square around the attacker when distance < 30, around the victim when distance > 1. Weapon loudness: none or not a weapon 1, loud flag 2, silent flag 0, else 1. | Original |
| AW10 | witness (0x4A9F10), protect (0x4AE3A0) | Skip the victim, the attacker and an NPC whose leader (none while "wait here") is either; wake a sleeper; protects the victim and perceives the victim → attacked reaction toward the attacker (0x4AA620); else protects the attacker and perceives him → toward the victim; else, not a guard (social class 5), no "no flee", danger state NONE and perceives the victim → FLEE from the attacker. Perception: sight, else hearing at the attack's loudness. | Original (confirms the F0 summary) |
| AW11 | dynamite (0x4AA0D0), trap use (0x4BC384, 0x4BC4D3) | On using an item whose spell field 2 is 176: NPCs in the 10-tile square around the user, not dead, not mind-controlled, not having the user as PC leader, that see the user or hear it at silent loudness, are attacked by the user (silent, no flags). | Original (refines F0: any user, on use, not on explosion) |
| AW12 | every call of sight and hearing | Sight: script condition 28 (0x4462F2), witness (3), dynamite, find target (3), target validity (0x4ABBA1), guards (0x4AEF8A), attack set-up (0x4B2303), skill difficulty (0x4C86B3), examine (0x5541AE), inventory window (0x572408). Hearing: condition 29 (normal), witness (attack's), dynamite (silent), find target (silent if concealed, else normal), guards (caller's: loud or silent), attack set-up (silent), skill difficulty (normal), examine (silent). Indices stay 0..2. | Original |
| AW13 | step cost (0x43FE00) for a corner step | Both L routes are summed: side tiles once, the corner tile twice (+16 soundproof, cover ×2, so one see-through cover object there denies a clear shot); a blocker on either side tile blocks. | Original bug (the corner tile's double cost; the side-tile blocking is the original's design); owner decision 2026-10-20: Original switch `CornerStepCountsBothRoutes` |
| AW14 | step cost with 0x10 on a corner step | The last step also skips objects on the side tiles. | Original bug (with AW13); owner decision 2026-10-20: Original switch `CornerStepCountsBothRoutes` |
| AW15 | step cost with 0x0C | "Ignore critters" has no effect under "projectile": living critters block unless shoot-through. | Original |
| AW16 | noise (0x4A9E10) | An NPC inside both squares is handled twice, repeating the attacked reaction and its roll. | Original bug; owner decision 2026-10-20: Original switch `WitnessHandledTwice` |
| AW17 | line (0x4201C0), `location_xy` (0x4B8680) | More than 100 samples (about 1000 pixels on the major axis) fail → cost 100; the screen geometry follows the view option at 0x5FC2D0. | Original (limit) |
| AW18 | sound cost and line of fire walks | The observer's pixel offset is added to a line built between tile centres. | Original |
| AW19 | witness (0x4A9F10) | A sleeping witness is woken before any perception test. | Original |

## NPC AI morale (ARC-RES-058)

Source: [NPC AI morale](npc-ai-morale.md) (plan stage A4, source units S14–S15). Question: when an NPC flees, how a flight
becomes a surrender and how a surrender ends, how the rest of the game treats a surrendered NPC, and when and how an NPC backs
off, including after a PC's kill. Read on 2026-10-20 by a first reader and, independently, a second reader that saw only the
questions (method step 6). Both agree on every row. The second reader added the script executor's other dialog-point rules
(generator NPCs skip every script, a mind-controlled NPC's dialog script is replaced), the stale partial-step value of MR15 and
that set no-flee accepts any object type; the first reader re-read them and agrees. P2 and P6 above were read again by both:
their ARC-RES-048 verdicts stand. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MR1 | should flee (0x4AB2F0), HP% (0x4AB400) | In order: NO_FLEE → no; spell flag mind controlled → no; the target's HP% below parameter 3 → no; own HP% at or below parameter 0 → yes; target party size minus own at or above parameter 1 → yes; target party level sum minus own at or above parameter 2 → yes; else no. HP% is `current × 100 / maximum`, signed 32-bit, truncated, no zero guard. Its only caller is 0x4AB2A0 (yes → setter FLEE, no → setter COMBAT_FOCUS, result unused), whose only caller is 0x4AA620. | Original |
| MR2 | party (0x4AE020, 0x4AE0A0), follower list (0x441260) | A PC: itself plus its follower list; an NPC with a leader (any critter): the leader's party; an NPC without a leader: size 1 and its own level. The list is the follower array with each follower's list appended recursively, unfiltered; size counts each entry and the level sum adds each entry's own level (stat 17). | Original (P2 settled: reconstruction slip) |
| MR3 | party (0x4AE020) | A leaderless NPC counts alone even when it leads followers, while each of its followers counts the whole group. | Original bug; owner decision 2026-10-20: Original switch `NpcLeaderPartyCountsAlone` |
| MR4 | reaction to an attack (0x4AA620) | Dead: nothing. NONE: should-flee about the attacker. COMBAT_FOCUS: same attacker → should-flee; another → the target choice (0x4AB0B0), should-flee only if it picks the attacker. FLEE: same attacker → nothing; another → should-flee only when farther than parameter 4 from the current source. SURRENDER: same attacker → should-flee; another → the target choice, and if it keeps the source, the fleeing float line (when active) and a flee goal away from the attacker with no state change. Then the NPC is added to the turn-based round (0x4B7E00). Callers: 0x4A9650, 0x4A9C00, 0x4A9F10 (two). | Original |
| MR5 | far enough (0x4AB030) | A mind-controlled NPC: yes. Otherwise yes when the 64-bit distance to the source exceeds parameter 4 (a negative distance is near). | Original |
| MR6 | flee spell (0x4AAF50), from the danger update (0x4A92D0) in state FLEE | SPELL_FLEE set: cleared, nothing chosen. Otherwise the AI lists are built (0x4CCA90) and the chooser (0x4ABF10) asked for the first list (flee) with the NPC as target; when it chooses, SPELL_FLEE is set. Result unused. The danger update makes no flee test in NONE or COMBAT_FOCUS and does nothing for a sleeping NPC. | Original |
| MR7 | flee spell (0x4AAF50), setter (0x4AABE0) | SPELL_FLEE is set only here and cleared by name only here (the setter clears FLEEING and SURRENDERED only), so a flight left before the next flee-spell step leaves it set and the next flight skips the spell once. | Original bug (latent); owner decision 2026-10-20: Original switch `StaleSpellFleeSkipsFleeSpell` |
| MR8 | perform (0x4AC180), perform fleeing (0x4AC620) | Perform: not this NPC's turn in a round → nothing; BACKING_OFF → back off; a chosen cast, item or skill; else by state (NONE non-combat, COMBAT_FOCUS combat, FLEE fleeing, SURRENDER surrender). Perform fleeing: setter SURRENDER with the pass's source, then the flee goal (type 25) away from it. SURRENDER is requested nowhere else. | Original (P6 settled: original's design) |
| MR9 | perform fleeing (0x4AC620) | The setter's result is ignored: the flee goal starts even when the setter turned SURRENDER into NONE or COMBAT_FOCUS (reachable through a change during a flight, for example invulnerability, a new leader or a leader's charisma; direct script writes of FLEEING unchecked). | Original bug (latent); owner decision 2026-10-20: Original switch `FleeGoalAfterRefusedSurrender` |
| MR10 | danger update (0x4A92D0), perform surrender (0x4AC660) | State SURRENDER: own HP% at least 80 and `random(1, 500) = 1` → setter NONE (stored for the pass). Perform surrender: the fleeing-from handle empty, dead or unconscious → setter NONE; otherwise nothing, no animation. | Original |
| MR11 | surrendered (0x4AF210), dialog start (0x57CC70), the script executor (call site 0x444AC8), the condition evaluator (call site 0x4460E8), greeting (0x419260), can speak (0x4AD800), leave combat (0x4B3D90), kill-on-sight (0x4AE120) | Dialog start: after the dialog script's "continue", a surrendered NPC whose source is in the talker's party flees from the talker and no dialog opens. Script run at point 9 on an NPC: skipped unless the script has "surrender speech" (0x10). The condition counts surrendered NPCs. Greeting: the fleeing message. Can speak: COMBAT_FOCUS and FLEE refuse, SURRENDER does not. Leaving combat mode is blocked only by NPCs in COMBAT_FOCUS. Kill-on-sight is never true toward the NPC's own source. The SURRENDERED bit is tested directly only by the state read. | Original |
| MR12 | flee (0x4AF130), stop fleeing (0x4AF170), set no-flee (0x4AF1D0), magic object flags (0x454A10), heal (0x4B58C0) | Flee: an NPC gets setter FLEE (callers: the fear effect, the dialog start). Stop fleeing: an NPC in FLEE → NONE, then every flee goal interrupted whatever the state (callers: the fear effect's end, the kill notification, set no-flee, resurrection). Set no-flee: stop fleeing, then NO_FLEE for any object (callers: nodes 5 (0x426A80, conditional) and 7 (0x4293D0, unconditional, after node 6's test 0x42C240 fails) of the animation goal "move away from object" (52, table 0x5ABDD0), which the flee goal (25, table 0x5A87D0) runs as its node 3; the start of mind control). So the flight itself can set NO_FLEE for good, and such an NPC later turns to fight instead of fleeing (setter rule 2). | Original |
| MR13 | magic object flags (0x454A10) | Mind control start: reaction −50 (a PC caster, non-summoned target), spell flag, follow the caster, set no-flee. Its end: spell flag cleared and disband; NO_FLEE stays set. | Original bug (NO_FLEE is also set for good by design elsewhere, MR12); owner decision 2026-10-20: Original switch `MindControlLeavesNoFlee` |
| MR14 | combat perform (0x4AC6E0), back off (0x4AC7B0) | Start: parameter 15 above 1, distance to the target below it, `random(1, 20) = 1` → BACKING_OFF and CHECK_GRENADE, back off at once; else grenade, else attack goal. Run: range parameter 15, 5 when at most 1; distance at least the range → BACKING_OFF cleared; else an "away" path (flag 0x800, 100 steps) and a run to its end tile; no path → BACKING_OFF cleared. | Original |
| MR15 | path (0x41F3C0), away (0x41F6C0), straight walk (0x41F840) | Away: `range − distance` tiles out along offsets 4, 5, 3, 6, 2, 7, 1, 0 from the direction toward the target, each a straight walk stopping at a blocking tile, blocked traversal, spotted trap or (with flag 0x400) fire, refused beyond 32 tiles; the first success wins. When all fail it retries the line with the most partial steps (a walk refused for length leaves a stale value), which fails again. | Original; the retry is an original bug (latent, no effect) |
| MR16 | kill notification (0x4AA1B0), caller 0x45DAD3 | The victim stops fleeing and stops waiting (0x4AA8C0). A PC killer: every NPC in the square of half-size 3 around the killer, not dead, without NO_FLEE, of the victim's faction (0x45D110) and in COMBAT_FOCUS on the killer gets BACKING_OFF (not CHECK_GRENADE). | Original |

## NPC AI hostility and targeting (ARC-RES-057)

Source: [NPC AI hostility and targeting](npc-ai-hostility.md) (plan stage A3, source units S10–S13). Question: when an NPC
counts another critter as an enemy, whom it protects or refuses to attack, how it scans for and chooses a target, when a target
stays valid, how the enemy list and the target lock live, and what one attack sets off. Read on 2026-10-20 by a first reader
and, independently, a second reader that saw only the questions (method step 6). Both agree on every row, and each found HS9
and the P3 reading on its own. The second reader added HS22 and what the animation helper called by peace (0x44E050)
interrupts; the first reader re-read them and agrees. The first reader added HS19. Row P3 above was read again with the same
result. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| HS1 | danger update (0x4A92D0), 0x4ABBC0 | A sleeping NPC skips it. NONE: find target, none → no setter call. COMBAT_FOCUS: the focus if valid, else unlock and find target; else who-hit-me-last if valid; else the enemy-list pick; else NONE. Each found target: spell choice (0x4ABC20), then COMBAT_FOCUS with the pass record's sound request. FLEE: the fleeing helper (0x4AAF50) only. SURRENDER: NONE when the HP ratio is at least 80 and `random(1, 500) = 1`. No should-flee test on any of these paths. | Original |
| HS2 | validity (0x4AB990), invisibility 0x45A580 | Invalid when: null; object flags 0x400103; the NPC itself; for a PC or NPC: dead, unconscious (always under non-lethal combat, else when find target returns another critter), the NPC's leader, the same non-null leader, the NPC's follower, stoned, invisible to the NPC (invisible spell flag without detect-invisible, and object-invisible or a PC not attempting an attack or spell and not in combat mode), the decoy for an NPC of intelligence 5 or more; a wall or a busted non-critter; more than 20 tiles from the NPC or from its leader; a concealed master prowler not seen. | Original |
| HS3 | find target (0x4AB460), 0x4AF240, 0x4AE4E0, 0x440B40 | Re-entrancy flag 0x5F8498; no scan for an invulnerable, nigh-invulnerable or decoy NPC. Inclusive square of half-size `clamp(Perception, 1, 10)`, PCs and NPCs, the first 50 in query order; keys: 0 for a decoy (for this NPC), else distance, +1000 when unconscious; stable ascending insertion sort. | Original |
| HS4 | find target (0x4AB460) | Per candidate: not perceived → avenge step only; a concealed PC is remembered; kill-on-sight → the candidate; an NPC fighting, fleeing or surrendering whose danger source S is valid, protected against (protect, or a guard against a leaderless monstrous S), upset NONE with the scanner's own leader, and perceived → S; avenge (0x4AE450: a dead NPC the scanner protects, its focus valid and perceived) → that focus. Nothing found: the NPC turns toward the last perceived concealed PC. | Original |
| HS5 | protect (0x4AE3A0), faction-same 0x45D110, origin-same 0x45D320, social class 0x45D010, monstrous 0x45F730 | SELF (same critter), GROUP (its non-null leader), NO when mind-controlled, FACTION, ORIGIN (guards, class 5, only), NO. Faction-same: never two PCs; a PC with an NPC by PC leadership or the PC's reputation for the NPC's non-zero faction; two non-PCs by PC leader, leadership either way, a shared non-null leader or the same non-zero faction. Origin-same: equal and non-zero. Monstrous: undead, animal, monster or mechanical. | Original |
| HS6 | kill-on-sight (0x4AE120) | NO when invalid; will-kos script (point 22, triggerer the candidate) returning 0 vetoes; NO when surrendered to the candidate; a PC follower goes straight to GUARD; KOS flag: enshroud versus undead or animal → NO, no override and not faction-same → FACTION; a PC candidate with AI reaction (0x4C0CE0) ≤ parameter 10 → REACTION; alignment gap ≥ parameter 11 (any candidate) → ALIGNMENT; decoy → DECOY; an NPC candidate in COMBAT_FOCUS on V where the NPC protects V (or is a guard and the candidate is a leaderless monster) and upset with the NPC's own leader is NONE → GUARD; else NO. | Original |
| HS7 | upset attacking (0x4ADCC0), 0x459040 | NONE when mind-controlled by the leader argument L (or no controller found and a PC leader); NONE for a non-critter target; PARTY when the target's leader equals L; GOOD when L is not null, own alignment > 0 and the target's ≥ parameter 12; ORIGIN; FACTION; else NONE. | Original; the PARTY test's null case is HS9 |
| HS8 | callers of upset (0x4A9C8A, 0x4AB80C, 0x4AE374) | The follower rule passes the attacker or the victim (never null); find target and kill-on-sight pass the NPC's own leader, null for a leaderless NPC. | Original |
| HS9 | upset attacking (0x4ADCC0) | PARTY also when the target's leader and L are both null: for a leaderless NPC every PC and every leaderless NPC is "party", so find target's ally help never targets a PC or a leaderless NPC, GUARD (NPC candidates only) never targets a leaderless NPC aggressor, and the guard-versus-leaderless-monster clauses are dead for leaderless guards. Validity, protect and faction-same test for a non-null leader before comparing, and upset's GOOD row tests L for null. | Original bug; owner decision 2026-10-20: Original switch `UpsetPartyMatchesMissingLeader` |
| HS10 | choose target (0x4AB0B0) | A null → B; B null → A; B not a critter → A; A not a critter → B; B unconscious → A; A unconscious → B; B beyond 20 → A; A beyond 20 → B; A decoy → A; B decoy → B; else `random(1, 100)`: ≤ 50 scores `level − distance`, else `−distance`; B only when strictly higher. | Original |
| HS11 | team add 0x4AF8C0, team list 0x4413E0, add 0x4AF930, remove 0x4AF9D0, pick 0x4AFA90, has 0x4AFB30, dialog distance 0x4AFBB0 | Team: the offender's PC leader (else the offender); a PC's party and all followers, else the head alone. Add (array field 0x130): skips the owner's own PC party and duplicates, appends, recalculates the combat reaction. Remove: every equal and every null entry, last entry into the hole, order not kept. Pick: purge nulls, `random(0, n − 1)` also for an empty list, circular from there, the first valid entry. Has: only for an NPC owner. Dialog distance: 5 for the player's PC (0x40DA20), else 10. | Original |
| HS12 | target lock 0x4AFBD0, unlock 0x4AFC70 | Lock (NPC owners and non-null targets only): unlock, COMBAT_FOCUS request, and critter flag 2 bit 0x200000 only when the state then is COMBAT_FOCUS on that target. Unlock clears the bit. Lock callers: the follower rule (ordered); unlock callers: 0x4ABBC0, stop attacking, the lock, leaving combat mode (0x4B411C). The lock is entered while fighting, but the setter's lock test blocks only COMBAT_FOCUS: FLEE, SURRENDER and NONE requests pass, clear FIGHTING and do not unlock (a surrender only through its combat-mode exit when the toggle acts), so "locked but not FIGHTING" is reachable (a locked follower that flees and stops fleeing). | Original |
| HS13 | attack event (0x4A9650) | Attacker first, victim second. Exits: a null handle; a nigh-invulnerable or dead critter victim (a sleeping one is woken); object flags 0x400102; the decoy; a wall; attacker = victim; a network client. Flags: bit 0 accidental (combat context flag 0x800), bit 1 no fan-out (script action), bit 2 ordered (attack order: the follower rule on the attacker with "ordered", nothing else). A non-critter attacker: an NPC victim asked into COMBAT_FOCUS on it. | Original |
| HS14 | attack event (0x4A9650), PC victim 0x4A9AD0 | Attacker's followers told (unless bit 1); the victim's PC leader (or the victim) unconcealed; KOS override cleared on an NPC victim; unless bit 1: a PC victim enters combat mode (turn to the attacker in a round) and auto-attacks back when idle, the victim's followers told, noise unless bit 0. NPC victim: turns, who-hit-me-last unless accidental and faction-same, team adds both ways for an NPC attacker. PC attacker on its follower: + parameter 9 unless accidental (Reputation A2), follow check ignoring the limit, refusal → disband and JILTED and the "won't follow" float, else one in three the "accidental attack" float; on another NPC: −10 when accidental, else the AI reaction lowered to parameter 10. Then the reaction to the attack unless accidental and faction-same. | Original |
| HS15 | tell followers 0x4A9B80, follower rule 0x4A9C00 | Direct followers (0x4411C0) of the attacker (side attacker) and of the victim (side victim). Upset not NONE: on the attacker side, target not in combat mode, not accidental → upset float toward the leader and −5 reaction to it; else nothing. Upset NONE: nothing when accidental; target lock when ordered; else the reaction to an attack. A non-critter target: nothing when accidental, lock when ordered, else COMBAT_FOCUS. | Original |
| HS16 | reaction to an attack 0x4AA620, fight or flee 0x4AB2A0, flee distance 0x4AB030 | Dead → nothing. NONE: weigh (FLEE if should-flee, else COMBAT_FOCUS). COMBAT_FOCUS: weigh when hit by its focus or choose target prefers the attacker. FLEE: weigh only when the attacker is not what it flees and it is beyond parameter 4 from that (or mind-controlled). SURRENDER: weigh when hit by whom it surrendered to or choose target prefers the attacker, else the fleeing float and a flee goal. Always: added to the turn-based round. | Original |
| HS17 | redirect init 0x4A9530, redirect 0x4A9560, per candidate 0x4A94C0, component 0x451700 | Both record handles are the spell target (target context 0x5E6D28 + 0x20); threshold from the component's `MinIQ:` field, mask from its critter-flag field. Over the target's vicinity: skip dead, the target, then all candidates when the threshold is above 0 and the **target's** intelligence exceeds it, then candidates without a mask flag; an NPC candidate gets the attack event with the target as attacker, then focus and who-hit-me-last := the target. | Original bug (inference: one fixed critter tested per candidate); confirms P3's reconstruction slip; owner decision 2026-10-20: Original switch `RedirectTestsTargetIntelligence` |
| HS18 | peace 0x4AA300, forget 0x4AA420, stop attacking 0x4AA4A0, allow attacking 0x4AA580 | Peace: head := the other side's PC leader or itself; focus and last attacker cleared, head off the list, a PC head's reaction raised to 50, NONE, attack goals at the head's side interrupted, and for each follower of the head: its focus and last attacker cleared when they name the NPC, both lists cleaned. Stop: peace with the focus, interrupt, leave combat mode; NO_ATTACK for a PC follower; unlock; a PC recurses over its followers. Allow: clears NO_ATTACK on a PC follower; a PC recurses. | Original |
| HS19 | attack event (0x4A9650), team add | Accidental hits also add the attacker's team to the victim's enemy list, so the victim's reaction to the PC reads 0 until peace, while the explicit accidental penalty is −10. | Original (intent unclear) |
| HS20 | find target (0x4AB460) | Only the first 50 critters of the query are kept, before the distance sort. | Original (limit) |
| HS21 | validity (0x4AB990), find target | During a scan the unconscious rule's nested scan does nothing, so an unconscious candidate is valid; outside a scan it is valid only while nothing else is found. | Original |
| HS22 | reaction to an attack (0x4AA620) | The surrender branch calls the float callback (0x5F8488) without the null test every other caller makes. | Original bug (latent: the callback is always set) |

## Spell records (ARC-MAGIC-001)

Source: the [spell records](../formats/spell-records.md) contract (slice MT1 of the
[magic implementation plan](magic-implementation-plan.md)), the [load map](combat-domain-load-map.md#42-spell-records)
and the [magic core definitions](magic-core-evidence.md#definitions-fr-magic-001). Read on 2026-10-20 by this task and,
independently, by a second reader that saw only the questions (method step 6): both agree on every row; the second reader
added the eye-candy branch of the init, the unreachable failure branch of the second phase and the measure of the
character table region (SR14). Every direct branch of these functions stays inside `.text`. No row is a reconstruction
slip: the earlier reading named these behaviours correctly, and the rows below add what it left open. SR12–SR14 are
latent (no installed record reaches them) and need no rule-mode switch: the reader modes (load-map policy 2) reproduce the
harmless quirk in "like original" mode, refuse it in strict mode, and refuse in both modes what ends the process (owner's
no-crash rule). The owner accepted this handling on 2026-10-20 ([anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions)
SR12–SR14); every difference from the original is listed in the contract's
[differences](../formats/spell-records.md#differences-from-the-original).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SR1 | magic-tech init (0x44EF50), second phase (0x44F160) | Loads `Rules\magictech.mes`, then `mes\spell.mes`, then reads messages 10..34 of the first as the 25 component type names (each tested, a missing one fails the init), then the eye-candy file (skipped in one mode of the init argument), then calls the record load with `Rules\spelllist.mes` and base 1000. A file that does not load fails the init. The second phase calls the component pass with the same arguments; its failure branch is unreachable. | Original |
| SR2 | record load, first pass (0x44FE30) | Loads the file (the only failure) and keeps it open; records 0..79 at base + 50·n, 80..139 likewise, then the willpower minimum is set to 0; 140..222 at base + 2000 + 50·n, then the technology flag (bit 0x4) is set and the willpower minimum set to 0. A fourth loop after them is never entered. | Original |
| SR3 | record load, second pass (0x44FFA0) | Uses the open file, runs the same ranges and bases through the component reader, writes the same forced values again, frees the file and returns success. | Original |
| SR4 | one record (0x450090) | Sets the defaults; the name is message n of `mes\spell.mes`; lines +0 and +1 are read unconditionally (a missing one is the shared "Error! Missing line N" text, which holds no key), +2 and +3 only when present; then the ten AI values are set to −1 and the defensive second value to 0 whether or not line +4 exists, and +4 is read when present. | Original |
| SR5 | component lines (0x4501D0) | From base + 50·n + 5 while the next message exists, at most 44 lines (+5..+48). | Original |
| SR6 | defaults (0x457580) | Willpower minimum {6, 9, 12, 15, 18}[n mod 5] unless a global flag is set (0x5E7600, in uninitialised data, never written by any instruction that names it: the table always applies); cost 0, resist stat −1 and value 0, maintain 0/0, duration 0/−1, duration stat info 0/1, trigger count 0, range 99, flags 0, item triggers 0, line +2 eye candy −1, the five area blocks and component pairs empty (count −1), the line +3 sets and `No_Stack` 0. | Original |
| SR7 | line +1 (0x4578F0) | Keys in the order `IQ`, `Cost`, `Maintain`, `Duration`, `DurationStatInfo` (tried only after a matched `Duration` whose second value is not −1), `DurationTriggerCount` (kept only above 0), `Resist` (stat by name in the 28-entry stat key table, 23 names then null entries), `Range`, `ChargesFromCells` (non-zero sets bit 0x20), `ChargeBeginCost` (zero sets bit 0x100), `Info` (neutral 0, friendly 1, aggressive 2, may be aggressive 3, OR-ed into the flags), `Disabled` (absent or zero sets bit 0x8, "enabled"). | Original |
| SR8 | line +2 (0x457B20) | `CastingAnim`, `Missile`, caster and target eye candy of the five actions in action order, `Is_Tech` (non-zero sets the technology flag). | Original |
| SR9 | line +3 (0x457D00) | `No_Stack` (number), `Cancels_SF`, `Cancels_EnvSF`, `Disallowed_SF`, `Disallowed_TSF` over the 32 spell-flag names (bit = index), `Disallowed_TCF` over the 32 critter-flag names, `ItemTriggers` over 26 trigger names with their own values (`Item_Used` is 0x3000). | Original |
| SR10 | line +4 (0x457E70) | The ten `AI_*` keys (`AI_Defensive` a pair whose second value is kept apart), then `No_Resist` (bit 0x200) and `No_Reflect` (bit 0x400) by presence; their values are read and ignored. | Original |
| SR11 | key and number parse (0x5318E0, 0x532320, 0x531990, 0x532480, 0x531EE0) | The delimiter is the comma. White space before a key is skipped; a key matches only as a case-sensitive prefix at the cursor, otherwise the cursor stays and every later key of the line fails. A value ends at the next comma (or the end of the line, which spends the cursor). Numbers are C `atol` (the same as `atoi` on Win32: white space, one sign, digits, 32-bit wrap). A name value is the whole text to the comma, trailing spaces included, matched case-insensitively; no match logs and exits. | Original |
| SR12 | pair parse (0x532710, 0x531B70; 0x5328F0, 0x531D80) | A pair must start with `(` after white space or the process exits; without `@` the second value is the first (`Duration:(30)` has stat 30); `)` is never checked. A stat name runs from right after `(` to its first space or `@`; without `@` the `)` is part of the name. A name matching none of the 23 stat keys reaches the table's null entries and the compare crashes. | Original bug (latent crash; refused in both modes) |
| SR13 | flag lists (0x532AC0, 0x532B90; 0x532630, 0x531FB0) | Names end at the first space or `\|`; when a space comes first and a `\|` follows, the walk resumes after the `\|`, skipping the names between (`A B\|C` reads A and C, `A\|B C` all three). An unknown or empty name logs and exits; a separator as the last character of the value sets the cursor to null and the white-space loop reads through it (crash). White space after a separator is skipped. | Original (the skip: reproduced in like-original mode, refused in strict mode); the crash an original bug (latent; refused in both modes) |
| SR14 | white-space skip of the parse functions | The byte is sign-extended before indexing the character class table, so a byte from 0x80 reads up to 254 bytes before the table; in the shipped image about 47 of those 127 entries carry the space bit, and part of the region is run-time library data. | Original bug (latent, data-dependent; a line with such a byte is refused in both modes) |
| SR15 | spell module init (0x4B1440) | Reads `mes\spell.mes` 500..515 (college names), 700..779 (descriptions of the 80 college spells), 8000..8015 (college descriptions) and 9000..9015 (mastery descriptions); a missing message is the shared missing-line text; never fails. | Original |
| SR16 | installed original (`DAT_ROOT`) | Both reader modes read lines +0..+4 of all 223 records alike (strict mode now refuses the file for its component lines, SC11); the counts match the [data census](combat-data-census.md#3-spell-records) (`SpellRecordRealDataTests`); no line holds a flag list with a skipped name, a trailing separator, a byte above 0x7F, a pair without `@` or an `IQ:` key. | Data |

## Spell components (ARC-MAGIC-002)

Source: the [spell record contract](../formats/spell-records.md#components-lines-5-onward) (slice MT2 of the
[magic implementation plan](magic-implementation-plan.md)), the [load map](combat-domain-load-map.md#42-spell-records) and
the [magic core](magic-core-evidence.md#effect-components-fr-magic-007). Read on 2026-10-20 by this task and, independently,
by a second reader that saw only the questions (method step 6); both agree on every row. One difference arose before the
comparison and was settled by re-reading: the first reader first took the range without `-` (SC4) to leave its maximum
unwritten, misplacing a stack offset around the integer conversion; both readers then read that it writes the one number
into both. The second reader added the three further table inconsistencies (MG21–MG23) and the prototype call chain
(SC7). Every direct branch stays inside `.text`; the one indirect branch, the component type switch, has all 23 table
entries inside the function.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SC1 | line +0 (0x457650) | Keys in order: `AoE:` (64-bit target list; a miss logs an error and reads on with 0), `AoE_SF:`, `AoE_NO_SF:` (replace every action's sets, 0 when absent), `Radius:`, `Count:` (replace every action's value when present), then for each action in order the same five keys with its prefix, whose lists are OR-ed into that action's area and whose numbers replace. Every action's mask gets the high word's 0x2000 (the not-invulnerable test bit, without the object bit) except for records 1, 2 and 9, also when `AoE:` is missing. Defaults of an action area: mask, sets and radius 0, count −1. | Original |
| SC2 | target names (0x5BBD70) and values (0x596140) | 65 names with 64-bit values: a kind bit (object 0x4, critter 0x40 with it, tile bit 47) and one test bit each. `Tgt_Obj_No_ST_Critter_Dead` lacks the critter bit that every other `Tgt_Obj_No_ST_Critter_*` name and the positive `Tgt_Obj_ST_Critter_Dead` carry (MG18, re-read). | Not decided (recorded as an original bug by ARC-RES-043); classified in MT8 with MG21–MG23, once the target functions are read |
| SC3 | 64-bit flag lists (0x5329E0, 0x532160) | The 32-bit lists' control flow (SR13) with 64-bit values: same separators, skip and trailing-separator crash, same exit on an unknown name; only the log text differs. | Original |
| SC4 | range (0x532550, 0x531AB0) | The value is cut at the comma and at the first `-`; the first number is the `atoi` before it, the second the `atoi` after it, or the first again without `-` (a leading `-` gives 0 and the number). A missing key gives 0 and 0. | Original |
| SC5 | component line (0x458060) | The line starts with the action name without a key (unknown: exit). The action's component array grows by one 0x68-byte entry (allocation unchecked, a failed resize exits) and its count is raised before the parse. Keys: `AoE:`, `AoE_SF:`, `AoE_NO_SF:` (0), `Radius:` (the action's line +0 radius), `Count:` (−1, not the action's), `Apply_AoE:` (0), `ItemTriggers:` (0), `Type:` over the 25 names read from `rules\magictech.mes` (missing: type 0, NoOp, and nothing more is read; unknown: exit). Every positional value (no key) is read without a null test, so a value after the line has ended is a null read (crash); a name matching none of a table with null entries (a field's flag names, the stat keys) reaches a null entry (crash). | Original; the crashes are original bugs, refused in both reader modes (SC5 row of the anomaly decisions) |
| SC6 | component data (0x458060, cases 1..23) | Per type as the contract lists them: goal names with their values 0, 43, 24, 44, 32, 62; critter-flag, spell-flag and object-field names as bits; `DmgType:` six names for Damage and five for Heal; `Dmg_Flags:`, `Play:`, `Cause:`, `Move_Location:` (with `Tile_Radius:` only after index 1), field and comparison names; defaults Count 1, Cause 6, Branch −1, Max 9999, MinIQ −1, List −1. A Damage component sets the record's "has damage" flag (0x80); an ObjFlag turning a bit of `obj_f_spell_flags` on adds it to the record's set flags. Trait and TraitIdx fix the working multiplier and divisor at 1 and the addend at 0. | Original |
| SC7 | Summon `Proto:` (0x458060, 0x4685A0, 0x468860) | A positive number is turned into the prototype whose object number is the value + 20 and kept as its 24-byte identifier; 0 or a missing key (logged) keeps a null prototype. The installed data agrees: every `Proto:` value has a prototype file at the number + 20. | Original (the value is the basic prototype number) |
| SC8 | Cast and Interrupt (0x458060) | `Spell:` and `MagicTech:` missing: the value is 10000 and an error is logged. | Original |
| SC9 | records changed by the component pass | Only the action's component count and array, the "has damage" flag and the set spell flags. | Original |
| SC10 | MG21–MG23, target values (0x596140) | `Tgt_Non_Party_Critters` lacks the critter bit its party siblings carry (MG21); `Tgt_Obj_Radius` lacks the object bit every other `Tgt_Obj_*` carries (MG22); `Tgt_Tile_Self` lacks the tile bit every other `Tgt_Tile_*` carries (MG23). Installed use: 20, 24 and 0 records. | Not decided: whether they are bugs depends on how the target functions (0x4F40B0, slice MT8) test the bits; recorded for MT8 |
| SC11 | installed original (`DAT_ROOT`) | 44 component lines hold text the cursor never reaches: a word after 40 Destroy and 3 Identify components, and a repeated `Dmg_Flags:` in record 221 (line +8) whose second value `Scaled` is lost. | Data (record 221: data fix DF4, decided 2026-10-21 under the owner's data-fix rule; lands with MT12) |
| SC12 | installed original (`DAT_ROOT`) | 1,009 component lines; no Interrupt component (MG7 record only); `Tgt_Obj_No_ST_Critter_Dead` in 105 records, but adding the critter bit would change only records 33 (five line +0 areas) and 168 (two Maintain components) (MG18 open); no `Clear_Faction:` or `Palette:` on a summon (MG8 and the summon palette change nothing installed); `No_Resist:` never with maintenance, duration or a trigger count (MG19 changes no installed record). | Data |

**F0 facts raised** (owner decision 2026-10-20: every fact of the slice's area still at F0 is re-read): the
[load map](combat-domain-load-map.md#42-spell-records)'s rows for line +0 (keys, per-action prefixes, the
not-invulnerable bit except records 1, 2 and 9) and for lines +5..+48 (first gap, at most 44, second pass after the
first, `Proto:` resolved at load); the [magic core](magic-core-evidence.md#definitions-fr-magic-001) definitions' "Components" row and the
"has damage" rule; the data side of its [effect component table](magic-core-evidence.md#effect-components-fr-magic-007)
(each type's keys, names and defaults: the six damage types with "magic" at index 4, `Dmg:a-b`, the damage flags, Effect
`Count:`/`Cause:`/`Scaled:`, Recharge `Max:` 9999, TestNBranch's four fields, Trait64's sources, TraitIdx's stat or
resistance field, AIRedirect's critter flag and `MinIQ:`, Movement's four locations, Summon's keys) — now at F2. Not
raised (run time, other slices' functions): what each component does when it runs, the summon lists (0x452900), the
EnvFlag stub (0x452F10), the target tests (0x4F40B0).

## Spell cost and payment (ARC-MAGIC-003)

Source: the [spell cost and payment runtime](../world/spell-cost-runtime.md) (slice MT5 of the
[magic implementation plan](magic-implementation-plan.md)) and the [magic core](magic-core-evidence.md#paying-fr-magic-004).
Read on 2026-10-21 by this task and, independently, by a second reader that saw only the questions (method step 6); both
agree on every row. The second reader added which run object pays in the pre-animation check and at BEGIN (the source) and
which one gates the BEGIN payment (the parent). Three functions were also run in the function oracle on 10,000 seeded
inputs each (0x4507D0, 0x4508A0, 0x4B1660; receipts in `docs/verification/receipts`): no difference, every planted mutant
caught (F3). Every direct branch stays inside `.text`; MG1, MG2 and MG20 get their second reading here and stay as decided.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PC1 | run-time cost (0x4507D0; 0x4507B0 only forwards to it) | A missing source returns success at once. The record's `Cost:` is read before any range test of the spell number. For a spell 0..79 whose college (spell / 5) is the caster's mastered one, the cost is halved toward zero and raised by 1 when the original cost is odd (a negative odd cost comes out one above its ceiling). The cheat flag then makes it 1; a dwarf PC or NPC then pays double (32-bit). It calls the payment with "apply" and returns its result. Oracle F3. | Original (MG2 run-time rounding confirmed) |
| PC2 | check cost (0x4508A0) | As PC1 without mastery, asking the payment without applying. A missing source logs an error and fails, unlike PC1. Oracle F3. | Original (MG2) |
| PC3 | pre-animation check (0x450940) | Finds the run by its slot (a failed lookup logs and fails); a free run (run flag 0x2) or a run without a source passes; the cost is the run spell's `Cost:` for a BEGIN, its maintenance cost otherwise; cheat 1; dwarf source × 2; no mastery; the source is asked without applying. | Original (MG1: the cast spell is read, no mastery) |
| PC4 | payment (0x450420), entry | A network client sends the payment to the host (out of scope). Spell 10000 has no record; another number's record is addressed unchecked, and the charge-cell flag is set only for 0..222 with the record's 0x20. A cost of 0 or less succeeds; otherwise the "last cost" global is written (the cost as given, before any halving), then a missing payer succeeds. | Original |
| PC5 | payment, charge cells | Before every other step and whatever the apply flag: an item source takes `2 × cost − adjust` cells from its parent (the technology lane's TC1), any other payer `cost` from itself; too few cells refuse, otherwise the cells are removed and nothing else is paid. | Original (the technology lane decides TC1) |
| PC6 | payment, critter | A fatigue-limiting critter pays half toward zero, at least 1. The items worn in slots 0..8 pay from their mana stores in order (a store covering the rest keeps the difference, a smaller one is emptied, a negative one adds to the rest); when applying, each store is written and a recharge of the drained amount is scheduled or merged, also for a drain of 0 after the cost is covered, and an emptied store notifies the AI. The stores are written before the fatigue test (MG20, second reading). | Original (MG20) |
| PC7 | payment, fatigue | The rest is refused when current fatigue minus the rest is −15 or lower (also when not applying); without applying the payment then succeeds. When applying, the fatigue damage becomes the stored damage plus the rest (the setter returns the clamped damage; 0 fails the payment). With no record the cast lands; a maintained record (period ≠ 0) or one with neither period nor duration fails when fatigue is now below 0; a timed, non-maintained one lands. | Original |
| PC8 | payment, item source and other objects | An item (types 5..14) with 0 spell charges is refused; a negative store succeeds and changes nothing; a positive one marks the item's flags with 0x4 whatever the apply flag and spends one charge only when applying. Any other object succeeds. | Original |
| PC9 | fatigue setter (0x45D5A0), current fatigue (0x45D700), fatigue damage (0x45D550) | The setter clamps the damage at 0, writes it and returns it (0 for a non-critter); a critter it makes unconscious ends its maintained runs and more (MT11). Current fatigue is the maximum minus the damage. | Original |
| PC10 | spell UI and AI costs (0x4B1660, 0x4B16C0, 0x4B1650, 0x4B1740) | The cast cost: the record's `Cost:` (1 before the table loads), doubled for any object of the dwarf race, then halved toward zero when spell / 5 is the mastered college, with no range test (oracle F3). The maintenance: its cost doubled for a dwarf, its period doubled when mastered, the cost never halved. A rank costs 1 character point; a paid service costs 100. | Original (MG2: the three formulas disagree) |
| PC11 | worn-store recharge (0x459590, 0x459640, 0x459680) | A drain stores the amount, the item and the time (game seconds); with merging, a pending recharge event of the item (type 23) gets the amount added; otherwise a new event fires 60000 ms × 8 (480 game seconds) later. When it fires it restores `2 × max(1, elapsed / 120)` points, at most what is pending, with no cap at the item's maximum, refreshes the interface for a PC holder and reschedules the rest without merging. | Original |
| PC12 | BEGIN payment (0x452F20, BEGIN branch to its payment) | A critter source that is dead or unconscious ends the cast; only a critter parent and a magic record reach the payment block; a parent below intelligence 5 or the record's willpower is refused for a critter source; the source pays (PC1) unless the run is free or the record sets `ChargeBeginCost: 0`; a refused payment sets the "not enough" status. | Original |

**F0 facts raised** (owner decision 2026-10-20): the [magic core](magic-core-evidence.md#paying-fr-magic-004)'s
"Paying" section — when a cast pays (the pre-animation check, the BEGIN condition, free runs), how much (its cost
formula, now with the odd test on the cost before halving, so its worked example charges 6, not 8), from where (cells,
fatigue-limiting, worn stores and MG20, the −15 refusal and fail-after-pay, item charges) and the recharge — and its MG1,
MG2 and MG20 rows now stand at F2 (the three cost functions at F3); the definitions' `ChargeBeginCost: 0` row. Not raised
(other slices' functions): an interrupted cast charged in full (MT9a), the 4 action points of a cast (MT20a), the knockout
of a caster and the runs it ends (MT11).

## NPC AI support and magic (ARC-RES-059)

Source: [NPC AI support and magic](npc-ai-support.md) (plan stage A5, source units S16–S18). Question: whom an NPC heals and
with what, how its ten AI action lists are built, sorted and cached, how the chooser picks an entry and why the eligibility test
refuses one, when it picks items up from the ground, and when it throws a grenade. Read on 2026-10-20 by a first reader and,
independently, a second reader that saw only the questions (method step 6). Both agree on every row, and each found SP11, SP15
and SP16 on its own. The second reader added that the maintained-run count (0x450AC0) is always 0 in this build, so refusal 6
needs intelligence 0..3 (the magic study's MG11), that the pair parser gives the second defensive value the first one's value
when `@` is missing, that the cast-highest flag is read for any critter type, and that an otherwise unused field of the cache
slot receives the first entry's spell; the first reader re-read them and agrees. SP18 was found by the independent review of the
pull request and re-read in the executable by the first reader. Rows P1, P4 and P7 above were read again by
both: their ARC-RES-048 verdicts stand. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SP1 | heal (0x4A8940) | Nothing for a sleeping NPC or one in FLEE. Combat argument 1, or in combat mode 0 when `random(1, 100)` exceeds parameter 14. Targets in order, first success ends: the NPC; its PC leader, else its NPC leader; that leader's followers (recursive, the NPC skipped), or without any leader the NPC's own followers. | Original |
| SP2 | heal helper (0x4A8AA0) | Target with critter flag MECHANICAL: nothing (P4). Dead: resurrect list (9), then end. HP% above 30 with combat argument 0: nothing. State NONE and HP% below 90: heal skill 10 with the first healing item when the skill check accepts (action 3). In danger and the healer's own fatigue% below 20: fatigue-recovery list (8). Then HP% below 40 heavy (6), below 55 medium (5), poison level above 0 cure poison (7), below 70 (below 90 in NONE) light (4). Lists are the healer's; chooser mode 8 with the target. | Original (P4 settled: original's behaviour); the fatigue rung is SP17 |
| SP3 | skill check (0x4AE570), skill 10 | Refusal 1 without a generic item with the healing flag; refusal 2 for a missing target, a target that is not a PC or NPC, or an undead or mechanical target; else accepted. Called by the heal helper and two dialog functions. | Original |
| SP4 | spell choice (0x4ABC20, 0x4ABC70) | Invulnerable (object flag) or nigh-invulnerable: nothing, action type set to 0, as when nothing is chosen. Global "NPC fighting" off: nothing. `random(1, 100)` above the chance: nothing. Then: no followers → summon list (1), mode 1; parameter 13 below `random(1, 100)` → defensive list (2), mode 4; offensive list (3), mode 2. Called at the four target-found points of the danger update. | Original (P7 settled: original's behaviour) |
| SP5 | spell chance (0x4ABE20), stronger (0x4ABEB0) | 0 when the NPC has a leader, both are above 50 % HP and the target is not stronger; else 50 when parameter 15 is above 1, otherwise 25. Stronger: the target's level at least the NPC's + 10 (target below 20), + 5 (20..39), + 0 (40 and above). | Original |
| SP6 | chooser (0x4ABF10), 0x4CC2A0, 0x4CC160 | Empty list: nothing. Target by mode: 1 the NPC, 2 the pass target, 8 the given target, 4 per entry (the pass target when the spell's second defensive value is not 0, else the NPC). Start index 0 when `random(1, 100)` is below 80, else `random(0, n − 1)`; circular walk. Spell −1: item used at once on the target (0x462CC0), success, nothing written. Target under that spell: skip. Eligibility refusal: skip. Else spell, item, target and action type 2 (item passing 0x4CC160) or 1 are written. 0x4CC160: mana store not empty and (first spell slot empty, or its spell has item triggers, or the valid-AI-action flag). The item's type read on the cast-with-item path is unused. | Original |
| SP7 | eligibility (0x4AE720), no item | In order: unknown spell 1; an NPC without a PC leader below 20 % fatigue 2; an NPC, a maintained spell and a zero free-maintenance count (0x450B40, intelligence / 4 minus a count that is always 0: MG11) 6; cast cost at or above current fatigue 2; intelligence below the minimum 3; willpower below the minimum 7; NPC flag CAST_HIGHEST and the spell not the highest of its college 4; no target: accepted; unless college 12 with the target at or below 30 % HP, cast fail chance at or above 38: 8; target other than the critter with a line-of-fire cost of 100 or more or a blocker other than the target: 5. Only a zero count refuses; 0x4AE6F0, a no-item wrapper, has no reference. | Original |
| SP8 | eligibility (0x4AE720), item | An NPC, a maintained spell and a zero count 6; empty mana store 2; no target: accepted; college 12 skips the fail chance when the target is at or below 30 % HP or the critter's fatigue% is at least 80, else item fail chance at or above 38: 8; the same line-of-fire test 5. No known-spell, fatigue, cost, intelligence, willpower or cast-highest test. | Original (intent unclear) |
| SP9 | spell values (0x457E70), pair parser (0x532710, 0x531B70), global lists (0x4CC1F0, 0x4CC240, 0x4CC350, 0x4CC380, 0x4CC2D0, 0x4CC310) | Ten keys fill spell record +0x12C..+0x150; `AI_Defensive:` is `(value@second)`, the second into +0x154, equal to the first when `@` is missing. At init each list holds every spell 0..222 whose value is not −1, sorted by value, highest first, with the runtime sort (ties: the sort's own order). | Original; the pair default changes nothing with the installed data, which writes both values ([census](combat-data-census.md#3-spell-records)) |
| SP10 | list build (0x4CCA90, 0x4CC580, 0x4CC8A0, 0x4CC810, 0x4CC930, 0x4CCA30, 0x456A10, 0x4CCAD0, 0x4CCB10, 0x4CCB50) | PC or NPC only. In combat mode with fatigue% at most 50: items, spells only when no item entry; otherwise spells, items only when no spell entry. Spells: magick points above 0, the known spells in the global order. Items: a hexed wielded weapon alone, else the inventory in order; a non-empty mana store and the identification rule (identified, no PC leader, or food or scroll) give each slot spell with a value and usable by aptitude (negative aptitude needs effective power above 0) as (spell, item); the valid-AI-action flag with the AI action equal to the list gives (−1, item). At most 200 entries. | Original |
| SP11 | cache (0x4CC6F0, 0x4CC770, 0x4CC520, 0x4CCB90, 0x4CCBF0, 0x4CCC00, 0x4CCC40) | Forty slots by critter, round-robin reuse. A miss builds the requested list into the new slot; a hit with that list non-empty reuses it; a hit with it empty builds into the caller's copy only, never stored and never freed (release does nothing). So a slot holds only its first list, frozen until an insert into the critter (item insert 0x466640) or a forced removal from it (item force-remove 0x467860, call at 0x467BF1), an exhausted listed item (0x450420, call at 0x45062B), a reset or reuse. | Original bug; owner decision 2026-10-20: Original switch `AiListCacheKeepsFirstList` |
| SP12 | look for item (0x4A8E70), search (0x4A8F90) | Nothing when sleeping, blinded or while the interface callback (slot 0x12C) reports an object. AMMO, WEAPON, ARMOR flags in this order, each cleared before its search, the first pickup ends. Search: not for a monstrous critter; square of half-size `clamp(Perception − 5, 1, 10)`; loose items without NO_DISPLAY or NO_NPC_PICKUP, worth above 0 and insertable, the **nearest** kept (strict compare); else the first item of the kind in each junk pile (description 3023) in query order, also skipping OFF; pickup goal. | Original (P1 settled: reconstruction slip) |
| SP13 | switch weapon (0x4ADFF0), its caller 0x4B3170 (call at 0x4B3284), item drop (0x461AB0), item insert (0x466640), gate (0x4A8570) | An NPC's ranged attack without ammunition to consume sets LOOK_FOR_AMMO, LOOK_FOR_WEAPON and CHECK_WEAPON. An NPC dropping a weapon gets LOOK_FOR_WEAPON, armour LOOK_FOR_ARMOR. An insert into an NPC sets CHECK_WIELD and clears its cache slot. The gate's best-item calls pass the pass record's target (the danger source). | Original |
| SP14 | combat perform (0x4AC6E0), grenade (0x4AC910) | Back-off start as MR14; else grenade, else attack goal. Grenade: CHECK_GRENADE needed; BACKING_OFF → no throw, flag kept; flag cleared; throwing skill points needed; the first generic item with the grenade flag; distance above its area radius; the line of fire reports no blocker (cost ignored); no living NPC the thrower protects in the square of that radius around the target; throw goal at the target's tile. | Original |
| SP15 | grenade (0x4AC910) | The friendly-fire query uses the NPC type mask only, so PCs, a PC leader included, are never protected. | Original bug; owner decision 2026-10-20: Original switch `GrenadeIgnoresPcsInBlast` |
| SP16 | danger update (0x4A92D0), chooser (0x4ABF10) | Scan, kept-focus and last-attacker paths pass a saved copy of the found target to the setter; the enemy-list path passes the pass record's target, which a chosen summon (mode 1) or self-aimed defensive spell (mode 4) has replaced with the NPC itself. | Original bug; owner decision 2026-10-20: Original switch `EnemyListFocusTakesSpellTarget` |
| SP17 | heal helper (0x4A8AA0) | The fatigue-recovery rung tests the healer's fatigue while the target may be its leader or a follower. | Original bug; owner decision 2026-10-20: Original switch `HealFatigueRungForOthers` |
| SP18 | chooser (0x4ABF10), 0x4CC2A0 | In mode 4 the second defensive value is read for every visited entry before the spell −1 test; for an item AI-action entry the spell table is indexed with −1, so 4 bytes before the table decide whether the item is used on the pass target or on the NPC. Reachable only by an item with AI action 2; the installed data has none. | Original bug (latent) |

## Technology (ARC-RES-062)

Source: [technology research](technology-evidence.md). Question: how degrees and expertise work, how
schematics are stored, learned, found and assembled, how technological items and records 140..222 behave
against magic, which scripts and dialogs touch technology, and how item durability, breaking, price and the
item side of repair work. Most of these functions had no earlier reading beyond the addresses of the
requirements; the raise (AL8), item power (EQ9), the wear check (EQ1), the skill run (SK7) and the combat
damage step and weapon wear (combat core C20, C21) were read before and agree. Read on 2026-10-20 by a first reader
and, independently, a second reader that saw only the questions (method step 6) on 13 decision-bearing
questions: 10 agree, the intelligence guard and the editor's decrease address were corrected to the second
reader's version, and the button codes of the schematic page stay a disagreement (F1). The second reader
found TC4. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| Degree raise | 0x4AFEC0 | Critter only, cap 7, the **effective** intelligence must meet {0, 5, 8, 11, 13, 15, 17, 19} for the new degree, base tech points + 1; unspent points are the editor wrapper's (0x589A20). | Original (agrees with AL8) |
| Degree lower | 0x4AFFF0, sell wrapper 0x589AF0 | Degree > 0: base tech points − cost, degree − 1, no check; the wrapper refunds the point. | Original |
| Effective expertise | 0x4B00B0 | Stored degree lowered until the effective intelligence meets its minimum, then {0, 10, 20, 35, 50, 65, 80, 100} through the expertise effects, unclamped; callers: sheet and schematic gate only. | Original |
| Intelligence guard | 0x4B02B0, base stat set 0x4B0980 | Only a base intelligence decrease: the new effective intelligence must hold every spell and degree, else the base is restored; effects are not guarded. | Original |
| Learned number | 0x4B0320 | `1990 + 200 × discipline + 10 × degree`; used by editor tooltips and the multiplayer auto-equip, not by the window. | Original |
| Registration | 0x4B0120, reading 0x56BB60 | Subtype 5 document: number from the start text line; known → message 88, document kept; new → appended without limit, document destroyed, message 89. | Original |
| Recipe | 0x56DBD0, parse 0x56DD20 | Seven lines (name, description, art, component 1, component 2, product, quantity), up to three alternatives (prototype = number + 20), one or two tokens repeat the last. | Original |
| Window open | 0x56D130, dialog 0x567E30 (F2 for the dialog path) | No combat or dialog check of its own; learned tab = first stored-degree records of each discipline; found tab for a PC crafter; dialog `su` makes the NPC the crafter. | Original |
| Component gate | 0x56E190 (F1; the button codes of 0x56DDC0 disagree between the readers) | Crafter's own inventory, exact prototype, worn, broken and changed-description items skipped; expertise in the component's discipline ≥ −complexity (signed). | Original |
| Assembly | 0x56E720 | Find both, consume 1 then 2 (one ammunition unit or destroy), create the product quantity times into the crafter; no expertise check, roll, skill, AP, time or experience. | Original |
| Tech maintenance | 0x453170 | MAINTAIN does not test the tech flag: maintained tech records check intelligence and willpower and pay maintenance. | Original (refines the magic study, which names only the BEGIN payment skip) |
| Holder gate | 0x456A10, use 0x462CC0, triggers 0x4CB830 | Only magickal items in technological hands can be refused; technological items are never refused to mages outside combat. | Original |
| Item break | 0x43F1C0 | First: destroyed art bit, damage reset to 0, unwield or drop, BUST script; second: delete, no BUST. | Original |
| Sale price | 0x461F80 | Damage scales the price (`current / maximum`) only when the PC sells. | Original |
| TC1 | payment 0x450420, 0x461590 | Cell-powered item source pays `cost × (1 + power / complexity)`: technologists pay up to double. | Original bug |
| TC2 | action 129 (0x44A7BD), 0x460B50, 0x45FD40, 0x57C6E0 | Calls an unfilled slot of an uninitialised callback structure. | Original bug (crash class, both fixed) |
| TC3 | 0x56E720 | A failed product creation returns after the components were consumed. | Original bug (latent) |
| TC4 | 0x56E720 | Both components found before consuming; one item in both lists would be consumed twice. | Original bug (latent; no installed record) |
| TC5 | 0x56E720 | The expertise gate is only the button's. | Original |
| TC6 | 0x56DD20 | More than three tokens or a line over 2000 bytes write out of bounds. | Original bug (latent, both fixed) |
| TC7 | repair quote 0x578760 | Damage counted twice in the quote. | Original |
| TC8 | data: one schematic written prototype | Its schematic number is 0: reading registers a schematic 0 whose record reads all zeros. | Original (data) |
| TC9 | 0x462CC0, 0x4CB830 | A broken item can be used and its spells fire. | Original |
| TC10 | editor 0x55F5F0, 0x55D940, 0x589AF0 | Selling a degree is not compared with the editor's snapshot: the sell handler has no comparison. The editor-open snapshot of the degrees (0x64DEEC) is read only by the tech page's draw, which in the in-game editor hides the − button at it (corrected by ARC-WORLD-052, [CP1, CP2](#character-point-pool-arc-world-052); 0x64DF10 is a per-player copy, never read). | Original bug (reachability O2: in the in-game editor the button is not drawn at the snapshot) |

## Schematic data (ARC-TECHNOLOGY-001)

Source: the [schematics](../formats/schematics.md) contract (slice TECH-1 of the
[technology research](technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted)) and the
[technology study](#technology-arc-res-062) rows above, read there by one reader. Re-read on 2026-10-20 for this slice by a
fresh agent that saw only the questions (method step 6): the tech module init, the schematic window init, the discipline,
the selection, the record read and the list parse. Every direct branch of these functions (and of the whole Schematic-UI
module 0x56CD60–0x56EA90) stays inside `.text`; there is no indirect call. Both readers agree on every point the first
reading recorded; the second reader added the details SD2, SD4 and SD6 name and found TC11. The rows are F2.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SD1 | tech module init (0x4AFCD0) | Loads `mes\tech.mes` (a failed load fails the module) and keeps the text of messages 0..7, 8..15, 16..23 and 24..87 in four tables (8, 8, 8, 64); the 64-entry getter (0x4AFE40) reads entry `a + 8b`, message `24 + a + 8b`, with a the discipline and b the degree (the help window passes `v % 8` and `v / 8`). No message is tested; a missing one is the shared missing-line text. Messages 88 and 89 are not read here (the registration reads them). The second reader confirms the arithmetic; that 0..7 are the names and 16..23 the descriptions rests on the first reading and on their use (heading, body). | Original |
| SD2 | schematic window init (0x56CD60) | Loads `rules\schematic.mes`, then `mes\schematic_text.mes`; the first failing returns failure, the second failing unloads the first. The learned count is the messages in 2000..3999 (counted only when message 2000 itself exists, else 0) divided by 7. Only for a count above 0: a list of "count" slots, filled with each of 2000, 2010, ... below 3999 whose message exists, and a per-discipline counter (0x56DB00) per start in a table of eight. The list's length for the window is the count. | Original (TC11 below) |
| SD3 | discipline (0x56DB00) | 4000 and up (signed): the item discipline field (0x61) of prototype `first product value + 20`, the record read through 0x56DBD0; 2000..3999: `(n − 2000) / 200` (3600..3999 give 8 and 9); otherwise, negative numbers included, 0. | Original |
| SD4 | selection (0x56DB60) | No arguments: the active tab's list and length, its per-discipline count table (the stored degrees on the learned tab, the found counts on the found tab), the discipline and the page. A count of 0 gives −1; otherwise the walk to the length returns the page-th entry whose discipline matches, else −1. The page bound (below the count) is the redraw's. Callers: the redraw passes the result to the record read; the build button builds nothing on −1. | Original |
| SD5 | record read (0x56DBD0) | Seven messages n..n+6 into a record of 13 integers: name, description and art through `atoi`, components 1 and 2 and the product (three integers each) through the parse, the quantity through `atoi`. n = −1 is first replaced by `6000 + 10 × discipline`. A missing message is "Error! Missing line N in rules\schematic.mes": `atoi` gives 0, and the parse writes six values (0, 0, 0, N, 0, 0) into a field of three: the components' extra values are rewritten by the next line, the product's reach the quantity (rewritten) and two integers past the record. No offset is applied; the prototype is the base + 20 (0x4685A0). | Original (the overflow of a missing list line: TC6 class) |
| SD6 | list parse (0x56DD20) | Copies the line into a 2000-byte stack buffer without a length check, splits it with `strtok` on a single space and writes each token's `atoi` into consecutive integers of the field: three tokens as they are, one or two then repeat the last value to three, none leaves the field unwritten (callers do not clear it), more than three run past the field. | Original (TC6) |
| TC11 | window init (0x56CD60), selection (0x56DB60) | The learned list has "count" slots (entries / 7) but is filled with every existing start without a bound and walked to the count: more starts than the count overflow the heap, fewer leave slots unwritten that the selection then reads; a start at 3600 or above counts past the eight-entry discipline table. The installed data has 56 starts for a count of 56, all in disciplines 0..7. | Original bug (latent, out of bounds; both fixed: the catalogue refuses such a file) |

## Item durability (ARC-WORLD-053)

Source: the [item durability runtime](../world/item-durability-runtime.md) (the item durability slice of the
[technology research](technology-evidence.md#item-durability-fr-item-011)) and the [Technology](#technology-arc-res-062) rows
above. Re-read on 2026-10-20 for this slice by a fresh agent that saw only the questions (method step 6): the maximum, the
current hit points, the damage setter, the break, the price, the sale check, the repair quote and the item side of the repair
service. Every direct branch of these functions stays inside `.text`. Both readers agree on every point the first reading
recorded; the second reader added the details below. The second reader disclosed that one line of the technology study (the
price is scaled only when the PC sells) reached it after it had read 0x461F80; its reading of that function rests on the
listing, and the row was F2 before. The rows are F2.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| DU1 | maximum (0x43D5A0) | A non-critter: hp_pts (27) + hp_adj (28), no effects, no clamp. (A critter adds level, strength and willpower and its effects: `LifeRules.MaxHitPoints`.) | Original |
| DU2 | current (0x43D600) | Maximum − hp_damage (29), no clamp. | Original |
| DU3 | damage setter (0x43D530) | A network client writes nothing; otherwise a negative value is stored as 0 through the synced setter; a critter with damage above 0 starts its rest timer; the change is notified. It never breaks the object. | Original |
| DU4 | break (0x43F1C0) | An object with object flag 0x1 returns at once. Items (types 5..14): when bit 0x400 of the current art is clear, set it on the current and the inventory art, set the damage to 0, take the item off its holder through the unwield (0x464C80, which moves only items in wear slots) and drop it when that fails, play the bust sound and run script point 30 (BUST) with the breaker as triggerer; when the bit is set, delete the object with the destroy sound, no script. Walls, portals, containers, scenery and traps have their own branches. | Original |
| DU5 | price (0x461F80) | When the first party (the seller) is a PC: worth (300 unidentified, else at least 2), the buyer's multiplier, the haggle share, then, when current < maximum, `current × price / maximum`; a negative price is 0 and exactly 1 is 2 (these bounds belong to this branch; the buy branch has its own). Signed 32 bits, truncating. The buy branch does not read damage. | Original |
| DU6 | sale check (0x462170) | Refuses a worth of 0, a broken item (current art bit 0x400), an item the buyer does not deal in or whose buy script refuses below expert haggle, and an item with item flag 0x8000 that the buyer's NPC flags do not admit; a refused item's price is 0. | Original |
| DU7 | repair quote (0x578760) | A broken item: only a repairer with master training in Repair, with the damage taken as the maximum, else a refusal line; damage 0: a "nothing to repair" line; otherwise `trunc(P / 2) × damage / maximum`, at least 2, where P is the PC's damage-scaled sale price to the repairer (TC7). | Original (TC7: a switch, owner decision 2026-10-20) |
| DU8 | repair service, item part (0x4C8E60) | The skill run is forced to succeed and the payment follows unconditionally. Nothing changes for an item without damage unless it is broken and the repairer a master. The amount is the maximum with the PC's fate full repair, else `maximum × effectiveness / 100`, at least 1; damage `max(0, damage − amount)`; hp_adj `− trunc(maximum × pct / 100)` with the maximum before the repair and no clamp; pct by the repairer's training 10, 5, 1, 0 (none, apprentice, expert, master) and 5 for a broken item. A master clears bit 0x400 of the current art only; the inventory art keeps it. | Original |

## Skill rules (ARC-SKILLS-001)

Source: [skill rules runtime](../world/skill-rules-runtime.md), [skills](skills-evidence.md#2-ranks) sections 2 and 3, the
[skills plan](skills-plan.md) task S1. Read on 2026-10-20 by a first reader and, independently, a second reader that saw only
the questions (method step 6); they agree on every row. Rows FA7–FA9 (levels, caps, points, training) and AL5 (the points
setters and the tech level) were already at F2 and are not repeated. SR10–SR14 re-read the remaining F0 facts of the
skill state domain in the [skills research](skills-evidence.md) (owner decision 2026-10-20: F0 facts are re-read domain by
domain); the editor (apart from SK2 and SK3), trainer, dialog and check facts stay F0 until their
tasks (S3–S6, S16). Every direct branch of the functions below stays inside
`.text`; none reaches `.IIDKing`. The level (0x4C5EB0) and the basic training setter (0x4C6170) were also run in the function
oracle (F3, receipts `4C5EB0.json` and `4C6170.json`).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SR1 | basic and tech training set (0x4C6170, 0x4C6850) | Only for a PC or NPC, a skill of the family (0..11, 0..3) and a tier 0..3, else 0 and nothing written. The stored tier is the raw bits 6..7 (never the monstrous melee rule). In single player the network test (0x4A2BA0) answers 1 and no message is sent. A tier above the stored one must be the next one; a lower or the same tier skips that rule. Then the skill's effective level (0x4C5EB0 with effects, monstrous melee and cap; 0x4C65E0 for tech) must reach the tier's minimum (table 0x5B6F44: 0, 1, 9, 18). A refusal returns the stored tier; a change replaces bits 6..7, keeps the others, runs the Educator background update (0x4C2950) and returns the new tier, also when the same tier is written again. | Original |
| SR2 | tech training get (0x4C67F0) | Bits 6..7 of the tech skill's stored value; no monstrous case; 0 for a non-critter or an index outside 0..3. | Original |
| SR3 | training minimum level (0x4C69C0) | The table 0x5B6F44 by tier: 0, 1, 9, 18 (no bounds check; its callers pass tiers 0..3). | Original |
| SR4 | tech base level (0x4C6580), tech points (0x4C6680) | Points are bits 0..5 of the tech value; the base level is four per point, without effects; 0 for a non-critter or an index outside 0..3. | Original |
| SR5 | point cost and refund (0x4C64B0, 0x4C64C0; tech 0x4C6AF0, 0x4C6B00) | Each answers 1. The auto-level purchases and the editor's + button use the cost (compared with the unspent points, which then fall by it, and the points rise by it); the editor's − button uses the refund. | Original |
| SR6 | key stat (0x4C64D0, tech 0x4C6B10; tables 0x5B6F04, 0x5B6F34) | Basic: dexterity for bow, dodge, melee, throwing, backstab and pick pocket, perception for prowling and spot trap, intelligence for gambling, willpower for haggle, intelligence for heal, charisma for persuasion. Tech: intelligence for repair, perception for firearms, dexterity for pick locks, perception for disarm traps. No bounds check. | Original |
| SR7 | minimum key stat (0x4C64E0, tech 0x4C6B20) | The first stat 1..20 whose cap (0x4C5F70) reaches the given level, 0 when none does (above 20); a level of 3 or less gives 1. The sheet's hint (0x55AE70) passes the base level + 4, so it names the key stat the next point needs. | Original |
| SR8 | key-stat guard (0x4C6B50) and its caller, the stat setter (0x4B0980) | The guard refuses (0) when any basic or tech skill keyed on the stat has a base level (four per point; no effects, no monstrous melee) above the cap of the value it is given, else allows (1). The stat setter clamps the requested base to the race's range, probes the effective value that base would give and restores the old base; only when the clamped base is below the old base does it call the guard, with that effective value. A refusal returns the old base and keeps it stored. A lowered intelligence or willpower then also passes the spell and degree checks (0x4B1B90, 0x4B02B0, 0x4B1C00; magic and technology). | Original |
| SR9 | points setters (0x4C6000, 0x4C66E0), arithmetic | The cap compare uses the 32-bit product 4 × points and the points are not masked before they are written, so points from 2^29 up could pass and set the training bits. No game caller passes more than the stored points + 1. | Original (latent; Steamwoven bounds points to ±1,000,000, where no product wraps) |
| SR10 | skill effect adjustment (0x4EA960 basic, 0x4EA990 tech) | The effect aggregation of one value (0x4EA6C0, EF6) over the critter's effect list, for the entries of the basic-skill (tech-skill) collection whose target is the skill, with no cause filter. The effect parser (0x4E9C20) fills the two collections from the executable's own target names, basic: bow, dodge, melee, throwing, backstab, pickpocket, prowling, spottrap, gambling, haggle, heal, persuasion; tech: repair, firearms, picklock, armtrap. Steamwoven's callers pass these changes to `SkillRules.Level`. | Original |
| SR11 | Educator update (0x4C2950) | Only for a PC whose background is 51 (Educator); over the list 0x4411C0 builds for it, for each basic and tech skill whose PC tier is expert or master, every listed critter is raised one tier at a time through the setter (0x4C6170, 0x4C6850) up to one tier below the PC's; nothing is lowered. Called after every successful training set and by the follower level update (0x4A6C90). | Original (the earlier reading agrees); not implemented (skills plan S16) |
| SR12 | skill module: init (0x4C5BD0), callbacks (0x4C5D50), exit (0x4C5DB0), load (0x4C5DC0), save (0x4C5DE0), next gambling roll (0x4C6560), clear (0x4C5E00) | Init registers the setting "follower skills" (default 1), loads `mes\skill.mes` (entries 0..11 basic names, 12..15 tech names, 16..19 four tier names, 20..31 basic and 32..35 tech descriptions; read by 0x4C62B0, 0x4C69A0, 0x4C69D0, 0x4C62C0, 0x4C69B0) and draws the first gambling roll; a failed load fails the init. The callbacks are nine UI hooks. Load and save move one 32-bit value: the pre-drawn gambling roll, which 0x4C6560 hands out while drawing the next `random(1, 100)`, and only the gambling check uses. Clear sets all sixteen stored values to 0. | Original (the earlier reading agrees) |
| SR13 | new-character kit (0x460FF0, called from the creation screen 0x545E80) | After the clothing part: any pick-locks point (points, not level) creates prototype 15178 at the critter's location and puts it in its inventory, then any heal point does the same with prototype 15179; a failed creation skips its item. | Original (the earlier reading agrees; the items' names not read) |
| SR14 | aptitude (0x4B0740, stat 16) | `(50 × magick points − 55 × tech points) / 10` of the effective point stats, truncated toward zero, plus the adjustment of the sector the critter stands in (0x45A060); this function does not clamp. `CharacterStatRules` computes the same. | Original |

## Hero skills (ARC-SKILLS-002)

Source: [hero skills runtime](../world/hero-skills-runtime.md) and [skills](skills-evidence.md#persuasion), task S2 of the
[skills plan](skills-plan.md). The levels, training, weapon speed (0x465CF0), weapon skill (0x465D80), best items (0x465170),
follow check (AP3) and max followers (AP4) the task reads are already at F2. The remaining F0 facts of the persuasion reads
(owner decision 2026-10-20) were read on 2026-10-20 by a first reader and, independently, a second reader that saw only the
questions (method step 6); they agree on both rows. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| HK1 | leader check (0x4ADB50) | Arguments: the NPC, then the prospective leader. It first clears three bits of the NPC's flags field 0xDB (0x01000000, 0x02000000, 0x04000000) and tests the values it read before. Under mind control the controller (or, with none found, any PC leader) passes. Then a leader with persuasion training master passes at once; then NPC flag 0x400 of field 0x118 passes; then the saved bits gate a reaction refusal (3) and the two alignment refusals (1, 2) against the NPC's AI parameter row. | Original (the earlier reading agrees for master); not implemented (no leader-check caller in Steamwoven) |
| HK2 | wait (0x4AA7A0) | For an NPC not already waiting: unless the leader's charisma is extraordinary or the NPC is mind-controlled, its wait index (field 0x12C) rises by one when the leader has **any** persuasion training (apprentice, expert or master alike), and an index of 6 or more schedules no timer; 0..5 schedule 0, 15 minutes, 2 hours, 1 day, 7 days or 30 days. Then the NPC leaves the follower list, keeps its leader and is marked waiting. | Reconstruction slip of the skills study's summary ("apprentice with extraordinary charisma"); the companion study's C12 already read it so; the timer itself is row Wait timer above. Not implemented (no wait timer runtime) |

## Character point pool (ARC-WORLD-052)

Source: the [character point pool](../world/character-point-pool.md) (task S5 of the [skills plan](skills-plan.md)) and the
[skills study](skills-evidence.md#2-ranks). Question: how the character editor buys and refunds stats, hit and fatigue points,
skills, spell ranks and tech degrees, what value it refunds down to, and what its hints show. Read on 2026-10-21 by this task
and, independently, by a second reader that saw only the questions (method step 6). Both agree on every row. The second
reader added the CP6 example, the degree draw (CP2), the per-player copies of 0x55F360 and the message diagnosis of CP4.
Where they first differed (whether a key stat lowered under an effect can also pin the level through a − click), the points
setter decides: it refuses a point the cap does not allow, so only an effect holds the level (CP6). Every direct branch
stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| CP1 | editor open (0x5597C0) | Besides CS2, an open in any mode but 3 by a single-player or host game fills the editor's snapshot: the 23 field values (0x55B4D0, CS4), the eight tech degrees (0x4AFE60), the sixteen college ranks (0x4B1AB0), the twelve basic and four tech skills' points (0x4C5FA0, 0x4C6680); a network client sends a packet instead. A separate function (0x55F360, a registered callback, apparently multiplayer) fills per-player copies of the same five arrays; its degree copy (the address TC10 names) is never read. | Original |
| CP2 | button handlers: basic skills (0x55D3A0), tech skills (0x55D6F0), college and degree pages (0x55D940 and its college branch), page draws (0x55C3A0, 0x55CBC0) | A − press compares the value with its snapshot for **equality** before it acts: skill points (basic and tech), a college rank; equal shows "nothing to refund" and changes nothing. The degree − calls the sell (0x589AF0, refund 1, lower only when the degree is above 0) with no comparison (TC10); the tech page draws the degree − button only when the degree is above 0 and, outside editor modes 0 and 3, above its snapshot (strictly), so in the in-game editor the button is gone at the snapshot. The skill − buttons are always drawn. | Original (TC10 decided: both modes keep the floor) |
| CP3 | stat, hit-point and fatigue buttons (0x55A5C0), +; hint (0x55AE70) | Rows: hit points, fatigue, then the eight stats in the sheet's order. A stat's + is refused ("at maximum") when its **effective** value has reached the stat's race maximum (0x4B0FD0); the cost is `StatCost(base + 1)` (0x4B0F50, AL10); too few unspent points refuse; the base + 1 is written and re-read, a lower value refused ("at maximum"); else the unspent points fall by the cost. Hit and fatigue points skip the maximum and cost 1. The hint for a stat compares the **base** with the maximum: below it shows `StatCost(base + 1)`, else 0. | Original |
| CP4 | the same, − | Equal to the snapshot refuses. The refund is `StatCost(base)` (hit and fatigue points 1); base − 1 is written; when the re-read value is at most that, the unspent points rise by the refund. The setter (0x4B0980, SR8) refuses at the race's minimum and, on a decrease, against the new **effective** value: the skill guard for every stat, the spell (0x4B1B90) and degree (0x4B02B0) checks for intelligence, the spell check (0x4B1C00) for willpower. The editor then picks a message from base − 1 = 0 (minimum), else from the checks run on base − 2 and not the effective value, so a message can name another cause or none. | Original (messages: S6) |
| CP5 | skill + (0x57ACD0) | Cost 1 (0x4C64B0, tech 0x4C6AF0) against the effective unspent points; the points setter (0x4C6000, tech 0x4C66E0) with points + 1; accepted: the unspent points fall by the cost; refused: "at maximum" when the effective level is 20, else the key stat's message (0x4C64D0, tech 0x4C6B10). Messages only for a PC while the editor is active. | Original (agrees with the skills study) |
| CP6 | skill − (0x57AEB0) with the basic handler (0x55D3A0) | A basic skill's click sets points − 1 and refunds 1 per pass while its effective level equals the level before the click, so it normally takes one point; it ends at the level change or when the setter refuses (below 0: "no points"; otherwise the key stat's message), so it never hangs (SK2). The floor is compared only before the first pass: when an effect holds the level (4 × points + effect at 20 or more, or at 0 or less, on both sides), one click passes the snapshot, down to no points, and refunds points bought before the editor opened. Example: key stat 18, 4 points at open and a +4 effect: + makes 5 (level 20); one − goes 5 → 4 → 3 (level 16), refunding 2. Once below, the equality never matches again. | **Original bug**, pending owner decision, recommended: fix in both modes (no switch); built that way, the click stops at the floor in both modes |
| CP7 | the same, tech skill | One point per click (refund 0x4C6B00 = 1), no loop; a refusal shows "no points" at 0, else the key stat's message. | Original |
| CP8 | spell and degree checks (0x4B1B90, 0x4B1C00, 0x4B02B0) | 0x4B1B90: for every known rank (ranks below each college's rank), the spell's intelligence minimum (0x4B1750 → 0x4502E0, the constant 5) above the value fails. 0x4B1C00: the same with the willpower minimum (0x4B1760 → 0x4502F0: the spell table's value once loaded, else 6, 9, 12, 15, 18 by rank). 0x4B02B0: only for a PC or NPC (any other object passes), a degree whose intelligence minimum (0x4B0110: 0, 5, 8, 11, 13, 15, 17, 19) is above the value fails. | Original (agrees with the technology reading) |
| CP9 | skill hint (0x55AE70, ids from 1000) | The key stat the next point needs: `MinimumKeyStat(base level + 4)` (SR7), from the base level only: 6, 9, 12, 15, 18 for 0..4 points, 0 at 5. | Original (SR7) |
| CP10 | unspent points (stat 21) | Every spend and refund writes the new value through the stat setter, which clamps it to 0..56 (XP2), from the effective unspent points. | Original |
| CP11 | college − (the college branch of 0x55D940's page) | Equal to the snapshot shows its message; otherwise it unlearns rank `5 × college + rank − 1` through the spell UI (0x57C670, MT3's). The college page draws its − button under the same comparison. | Original (the draw: F1, the second reader did not verify it fully) |
| CP12 | degree sell (0x589AF0, 0x4AFFF0) | The cost of the degree (0x4B00A0: 1 for every degree from 1) is refunded when 0x4AFFF0 lowers it (only above 0), which also takes the cost from the base tech points; no message when it does not. | Original (agrees with the technology reading) |

**F0 facts raised** (owner decision 2026-10-20): the [skills study](skills-evidence.md#2-ranks)'s editor + and − buttons,
their floor and snapshot and the hint (0x57ACD0, 0x57AEB0, 0x55D3A0, 0x55D6F0, 0x5597C0, 0x55AE70), now F2, with the
correction that the floor does not hold through a − click whose level an effect pins (CP6); the skills plan's S5 check list;
[FR-CHAR-015](../requirements/characters-skills.md#fr-char-015)'s editor path (0x55A5C0, 0x5597C0, the setter's guards) and
[FR-CHAR-016](../requirements/characters-skills.md#fr-char-016)'s (0x57ACD0, 0x57AEB0, 0x55D3A0), now F2. Not raised (other
tasks' functions): the sheet's skill page draw (0x55BD10, S6), the spell UI's unlearn (0x57C670, MT3), the degree raise
wrapper (0x589A20, TECH-2).

**Correction to TC10.** The degree snapshot the editor reads is 0x64DEEC (CP1), filled when the editor opens and read by
the tech page's draw (CP2); the address TC10 names belongs to the per-player copies of 0x55F360 and is never read. The sell
handler itself has no comparison, as TC10 says; in the in-game editor the − button is not drawn at the snapshot. The decision
(both modes keep the floor) stands.

## Doors, containers and locks (ARC-RES-065)

Source: [first building](first-building-evidence.md#portals-doors-and-windows-fr-scene-008) (portal frames and door
admission), the Lock kind of the [event queue](game-event-queue.md#3-event-kinds-and-consumers-s) and the
[skills plan](skills-plan.md) S9, which needs a lock state owner for portals and containers. Question: what state
does the original keep for an openable object, how does it change, and who may open one? Read on 2026-10-20 by this
task and, independently, by a second reader that saw only the questions (method step 6); both agree on every row (the second reader added the leader chain and the recursive follower list of LK9 and the door/window split of LK12, which the rows now state).
Every direct branch of the functions below stays inside `.text`; none reaches `.IIDKing`. Field numbers are the
executable's: object flags 19, portal flags 46, lock difficulty 47, key 48; container flags 56, difficulty 57, key 58
(the project's record slots 18 and 36..38). Lock bits are the same for both: locked 0x1, jammed 0x2, magically held
0x4, never locked 0x8, always locked 0x10, locked by day 0x20, locked by night 0x40, busted 0x80 (the portal's sticky bit 0x100 is not a lock bit and none of these functions reads it).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LK1 | lockable (0x441CF0) | A portal or a container, except the junk pile (the prototype whose number less 20 is 3023, read from field 336 through 0x49B290; the AI's "description 3023" of [SP12](#npc-ai-support-and-magic-arc-res-059)). The three lock functions below do nothing (return 0) for anything else. | Original |
| LK2 | `object_locked_get` (0x441D40) | 0 for a busted or never-locked object; with locked by day, 0 at hours below 7 or above 21; with locked by night, 0 at hours 7..21; otherwise the locked bit. Always locked is not read here. The hour is the game clock's (0x45AAA0). | Original (confirms the earlier reading) |
| LK3 | lock set (0x441DD0) | Writes the locked bit to the value (exactly 1 sets it, anything else clears it), keeping the other bits, and returns `object_locked_get`. Only when the object was locked and the value is 0 does it first schedule a Lock event (kind 21) for the object one game hour (3,600,000 ms) later; locking, or unlocking an object that was not locked, schedules nothing. | Original |
| LK4 | jam set (0x441F10) | Writes the jammed bit (set for a non-zero value, else cleared) and returns 1. Only when the object was not jammed and the value is non-zero does it first schedule a Lock event one game day (86,400,000 ms) later. | Original |
| LK5 | Lock event (0x441E90) | Whatever scheduled it, the event locks the object (LK3 with 1, which schedules nothing), clears its jam (LK4 with 0) and closes it: an open portal is toggled closed (LK10), a container whose ART frame is exactly 1 is closed (LK13). Locking an object never locked, or always locked, still sets its locked bit; `object_locked_get` then decides. | Original (the earlier reading named only relock and unjam) |
| LK6 | busted (0x43F630) | 0 for a destroyed object (object flag 0x1); for a portal or a container its busted bit; walls, critters and other types test their own fields. | Original |
| LK7 | `ai_attempt_open_portal` (0x4AEB70) | Results in order: 0 when LK6 says busted; 5 when the walker is not a PC or NPC; 4 when the portal's unlock script (SAP 3, 0x441980) refuses; 0 when the object is not a portal; 2 jammed; 3 magically held; unless always locked: 0 for an NPC without a PC leader (0x45DDA0), 0 when leaving a building ([LV1](#leaving-a-building-through-a-locked-portal-arc-res-049)); then 0 when LK2 says unlocked, 0 when the party has the key of field 48 (LK9), else 1 (locked). | Original (extends LV2) |
| LK8 | container open attempt (0x4AED80) | 0 for a junk pile or a busted object; 4 when the unlock script refuses; 0 for a non-container; 2 jammed; 3 magically held; 0 when LK2 says unlocked or the party has the key of field 58; else 1. No leaving test and no NPC exemption. | Original |
| LK9 | key in party (0x463370, 0x463240, 0x463340) | The holders searched are the PC at the top of the walker's leader chain (0x45DDA0), else its direct leader (0x45DE00, field 281), else the walker itself, then every follower of that holder, followers of followers included (0x441260). An inventory (field 237 count, 238 list) matches when a key (type 11) has key id field 186, or a key ring (type 12) lists in field 194 a key id, that fits the lock: a lock key or item key of 0 never fits, a lock key of 1 is opened by any key, an item key of 1 opens any lock, otherwise the two must be equal. The multiplayer branch is not taken in single player. | Original |
| LK10 | `portal_is_open` (0x4F08C0), toggle (0x4F08F0), open (0x4F0950), close (0x4F0A90) | Open means the ART frame is not 0. Open and close are one step each and fail (0) for a non-portal, a failed ART lookup or an ART of one frame. A door's raw rotation r is first made even (an odd r drops by one): 0 and 6 use frames 4..6, 2 and 4 frames 1..3. Opening from a frame outside the range jumps to its first frame and sets object flags 0x430 (no block, shoot through, see through); inside it steps up, and its last frame refuses. Closing steps down inside the range; leaving the range's first frame goes to 0 and clears 0x430; from frame 0 it refuses; from another frame it jumps to the range's last frame. A window has frames 0 and 1 and uses flags 0x420 (no block, shoot through). Toggle repeats open (or, when open, close) until it refuses, so it opens or closes at once. | Original (confirms and completes the earlier reading) |
| LK11 | open and close door animation (0x42D080, 0x42D160; 0x42D1C0, 0x42D2A0) | The begin node makes the first step (LK10), sets the animation delay to 1000 / the ART's frame rate in ms (unsigned division; 100 ms when the ART cannot be read) and plays the portal sound (open +0, close +1; [SN25](audio-evidence.md#dialogue-voice-event-sounds-scripts-and-interface)); each later node makes one step, without sound, until it refuses. | Original |
| LK12 | bust (0x4F06E0, busted ART 0x4EC830) | A door has no busted ART: the door object is destroyed (0x43CCA0). A window gets its busted ART (the ART bit 9, 0x200, set and frame 0; kept when already set), the busted bit, and the bust script (SAP 30) of the object runs with the attacker. No sound plays (the misc sound of a portal is none) and the object flags are not touched. | Original |
| LK13 | container open and close (0x43F7F0, 0x43F8F0; frame next 0x502BC0, previous 0x502C00) | Skipped for a destroyed object. Open sets the next ART frame (wrapping to 0 past the last), close the previous one (0 stays 0); each redraws the object when its ART changed. | Original |

## NPC AI action execution (ARC-RES-066)

Source: [NPC AI action execution](npc-ai-execution.md) (plan stage A6, source unit S19 and the animation goals behind it).
Question: how perform carries out a cast, an item, a skill and the danger-state actions, how the animation goals of
attacking, approaching, fleeing, moving away, running and following run, what each costs in a round, and how the attack uses
the line of fire. Read on 2026-10-21 by a first reader and, independently, a second reader that saw only the questions
(method step 6). Both agree on every row. The second reader found the missile's own setting of combat flag 0x800 (EX14),
which the first reader had missed, and the item use's exemption of magical items from the 4 action points (EX4); the first
reader re-read both and agrees. Both readers read the ranged approach's test (EX9) and the end step's charge (EX6) as
unintended, by inference. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| EX1 | perform (0x4AC180) | In a round nothing unless it is the NPC's turn; BACKING_OFF → back off; action 1 → cast (0x4AC250), 2 → item (0x4AC320 → 0x435450), 3 → skill (0x4AC350 → 0x4350F0; skill 14 → 0x436220); else by state through a four-entry table: non-combat 0x4AC380, combat 0x4AC6E0, fleeing 0x4AC620, surrender 0x4AC660. | Original (confirms MR8) |
| EX2 | cast (0x4AC250) | Invocation (0x455A20) from the item when one is set, else the NPC, with the pass's spell and target; check 0x4564E0; on success the run (0x455AC0 → 0x455C30 in single player), which starts goal 26 or 82; then, with an item, 0x4574D0 on it and food (type 9) or scroll (10) destroyed. A refused check does nothing. | Original |
| EX3 | start gate (0x4348E0), starters 0x435450, 0x4350F0, 0x434980, 0x434F80 | Gate: inactive refuses; outside a round passes; in a round only the current critter, a PC always, an NPC when the cost fits its action points or its fatigue is above 35 %. Item use: cost 4, goal 39, run flag 0x4000; skill: cost 4, goal 33, run flag 0x4000; flee: cost 0, goal 25, run flag 0x400 (0x436CB0); throw: cost 0, goal 37. Item, skill and attack starters turn running on for an NPC with current fatigue above 8, for the PC only with always-run. | Original |
| EX4 | item-use goal (39), item use (0x462CC0), skill attempt (0x42A2A0) | Goal 39: range 1, move-near when farther or the step is blocked, then the use. The use charges 4 when the use script stops it, otherwise only for an item with an empty mana store and no magical flag; magical items run their magic uncharged here; food and scrolls are destroyed. The skill attempt needs a target neither destroyed nor off, charges 4 and runs the skill invocation 0x4C7160. | Original |
| EX5 | goal stepper (0x423560), pop (0x423D10), parameter set-up (0x44DD80) | Node steps: node number, restart (node 1, step ends), push (copy of the parent's data, at most eight levels), pop (parent restarts at node 1 in the same step), pop two, push with pop (replace), end (all end nodes, run over). Delays: 0, milliseconds, the run's pace, a global, random 0..300. At most 100 nodes per step, then the owner's subturn ends and the run is killed. A run ending in a round runs the subturn hook for a PC or NPC owner. | Original |
| EX6 | goal stepper (0x423560), check (0x4B7790), subturn start (0x4B75D0), steps (0x42EB01, 0x430E73, 0x430FC0) | An end step in a round charges the owner, unless it is the local PC, the pending cost (0x5FC244) that the last check for the current critter stored: 0 at its subturn start, else the next step's cost, which the step then paid itself; so the charge repeats a paid cost. Nothing is charged on a pop. | Original bug; proposed Original switch `AbortedGoalRechargesPendingCost` |
| EX7 | attack starter (0x434B00), no-sound wrapper (0x434AE0) | Self-attack refused; a removable default-throws weapon is unwielded and thrown at the target's tile (0x434F80); gate with the attack cost; refused for Body of Air without the elemental flag, an equivalent goal, an attack run already going (updated instead), a failed priority-3 interrupt or a boomerang in flight; goal 18; the third argument goes to the goal's sound slot (combat perform: the pass's sound request; the wrapper: none). | Original |
| EX8 | attack goal (18), reach (0x429960), attack possible (0x4294F0), approach (79, 0x4280D0, 0x4296D0, 0x429AD0), no round (0x4246D0) | Target present and not dead; attack possible (attacker active, not Body of Air unless elemental, target not destroyed, off or dead, a foreign blocker needs a path), else in a round the current critter's subturn ends; reach: distance ≤ max(weapon range, 1), line-of-fire cost below 26, no blocker but the target → attempt attack; else range 1 and approach to an adjacent tile with a clear step; adjacent but out of reach: real time one-second pause, round ends the run. Approach: path to the target (failure ends a non-PC's subturn in a round), the sound slot played once, then the ranged (80) or plain (61) walk by weapon range. | Original |
| EX9 | ranged approach callback (0x425590) | Stops when the target changed tile; otherwise, when the attack is possible it returns "keep walking"; only when the attack is not possible and the line is clear (below 26, no foreign blocker) with a wielded weapon does it write the weapon range into the goals' range and stop. A ranged attacker approaching a still target walks to the adjacent tile. | Original bug; proposed Original switch `RangedApproachSkipsClearShot` |
| EX10 | attempt attack (19), hit step (0x42A630 → 0x4B3BB0), repeat (0x429760) | Turn, reach, attack start (cost charged, the run killed when unpaid), frames; at the action frame reach again → hit step into the resolution with flags 0x50000 and the goal's aim; else no hit. Repeat only for a PC in combat mode with auto-attack outside a round; an NPC's goal ends after one attack. No branch from the hit step through the resolution's melee and ranged parts tests whether the attacker is a PC. | Original |
| EX11 | flee goal (25): 0x42B940, 0x42BEA0, 0x425130 with 0x425840, 0x433270 | Running unless encumbrance level 4 or more; range 9; within 9 with the first step toward the source clear (the source's tile not blocking, the step not blocked except by the source) → move away, then restart; otherwise 2 action points (any object but the local PC) and the run ends. | Original |
| EX12 | move away (52): 0x426F10, 0x426E80, 0x426A80, 0x42C240, 0x4293D0; path builder (0x41F3C0), straight walk (0x41F840) | Get up, unconceal; within range with a clear step else back; escape tile 10 steps from the source along the source-to-NPC direction; path flags with run flag 0x400 make the straight walk accept every step and forbid the search, so planning fails only on length; no path (also a destroyed or off source, an entangled NPC) → a non-PC's subturn ends in a round, the NPC faces the source (not when stoned, stunned or paralysed) and NO_FLEE is set. The node's own NO_FLEE (a path after a partial straight line) needs a run without flag 0x400. | Original; the node's NO_FLEE branch unreachable in single player |
| EX13 | move away node 4 (0x425740), casting goal node 7, flee goal node 2's end, back-off-from starter (0x434A30), resolution's accidental test (0x4B3170) | No transition reaches the nodes; 0x434A30 has no caller or pointer; the resolution's test compares fields that are still equal (EX15). | Original (dead, no effect) |
| EX14 | missile flight (0x4B2870), notification (0x4B2690) | A missile meeting an object other than its target and launcher (a blocker on its way, or a living object picked 1 in 3 on a crossed tile) marks its combat flags with 0x800 and builds a record against that object, whose attack notification carries the accidental bit (no noise). The missile's own flags get 0x820 when it ends on its tile. These are the only live setters of the bit. | Original (answers AU5) |
| EX15 | attack resolution (0x4B3170) | The accidental test (struck object differs from the aimed one) and, for an attack without a missile (melee, or a ranged weapon without missile art), the attack notification to the aimed target come before the ranged branch replaces the struck object with a line-of-fire blocker; so a shot without a missile that strikes a blocker notifies the aimed target and not the blocker, and the test is never true. | Original bug; proposed Original switch `RangedBlockerHitNotifiesAimedTarget` |
| EX16 | non-combat perform (0x4AC380), follow starter (0x4346A0), follow goal (24: 0x42BF40, 0x42BFD0), move-near (8), run-near (56), join refresh (0x436FA0 → 0x436F30) | Non-combat perform leaves combat mode, then for a leader without "wait here" asks for the follow goal and runs the loyalty check. Starter: NPCs only, nothing while the leader is within 4 (7 spread out), existing follow runs kept, priority-3 interrupt. Goal: range 2 (5); in range with a clear step → 2 action points and re-check after 100 ms; within range + 2 → walk (8), beyond → run (56, running unless encumbered). Join sets run flag 0x80 on a running animation (frame time recomputed). | Original (answers companion O5) |
| EX17 | talk goal (21) node 0x42AE10, call at 0x42AE9F, dialog distance (0x4AFBB0) | Talks when the distance is strictly below 5 (PC talker) or 10 (others) and the partner is neither destroyed nor off; otherwise walks to within 1 tile (move-near) and tests again. | Original |
| EX18 | runtime sort (0x58A548), comparator (0x4CC2D0), list set-up (0x4CC380) | Quicksort with the middle element as pivot and a selection sort (first maximum moved to the end) for parts of eight or fewer; the comparator orders by value, highest first; input spells 0..222 by number; not stable. | Original (answers support U1 for the algorithm) |
| EX19 | casting goal (26/82) node 0x429B50, throw goal (37), node helper (0x433270), attack start (0x432700) | Casting: 4 at its start (else the goal ends), no approach; throw: no approach, 2 at its end; the node helper charges 2 to anyone but the local PC; the attack start charges the weapon's cost. | Original |

## Dialog skill gates (ARC-SKILLS-004)

Source: [dialog conversation runtime](../scripting/dialog-conversation-runtime.md#skill-gates-arc-skills-004) and
[skills](skills-evidence.md#dialog-and-scripts), task S4 of the [skills plan](skills-plan.md). Read on 2026-10-21 by a first
reader and, independently, a second reader that saw only the questions (method step 6); they agree on every row. The training
setters and the tier minimum the cases call are SR1 and SR3. Every direct branch stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| DS1 | dialog conditions (0x4150D0), actions (0x415BA0) | Each operation: skip every character that is not a letter or `$`, take two characters as the code, matched case-insensitively (the C library's `stricmp`, 0x58ADB0) against 37 condition codes or 35 action codes, read the number after the code with `atoi`, and continue scanning right after the code. A condition that fails returns 0 at once; all passing returns 1. An unknown code fails a condition and ends an action string. | Original |
| DS2 | conditions `ps`, `ha`, `sk`, `tr` (0x4150D0) | `ps` and `ha` read the PC's effective level of basic skill 11 and 9; `sk s n` the level of basic skill s, or of tech skill s − 12 from 12; `tr s n` the training the same way. n ≥ 0 passes at value ≥ n, n < 0 at value ≤ −n. A number the getters do not have (16 and above, negative) reads 0. | Original |
| DS3 | second operand (0x4167C0) | Skips white space, then decimal digits, then reads `atoi`: so `tr4-1` is −1, a missing number (`tr41,` or `sk7,5`) is 0, and a negative first operand is read again as the second. | Original |
| DS4 | action `tr s t` (0x415BA0) | Calls the basic training setter for s below 12, else the tech one with s − 12, on the PC; the setter's answer is not used and the action string goes on. | Original |
| DS5 | action `et s line` (0x415BA0) | Reads the PC's level and training of s (basic or tech as DS2). A level below the expert minimum (0x4C69C0 of tier 2: 9) speaks a generated NPC line from messages 6200..6299 with generated player options 600..699; otherwise an untrained skill does the same from 6300..6399; otherwise the dialog enters the line. Every outcome sets the action's result to 0, and the operations after it still run. | Original (the earlier reading agrees) |

## Companion orders and loyalty (ARC-RES-064)

Source: [companion orders and loyalty](companion-orders-loyalty-evidence.md) (questions Q1–Q7), re-reading the F0 rows C11–C14
and C16, O4 and O7 of the [companion study](companion-follow-evidence.md#open-questions), XP5's message and waiting followers
and SCR action 139. Read on 2026-10-21 by a first reader and, independently, a second reader that saw only the questions
(method step 6). After resolving one data-reading difference (below), both agree on every behaviour and verdict. The second reader added that the PC's own text is floated over the PC first, that the curse line is a race insult of the generated race-line files and that the multiplayer phase scans players within a screen-sized rectangle; it first read keyword 116 ("stop") as having no rule entry, which the installed `Rules\BroadCastRules.mes` does have (back off), a data-reading difference, not a code one. Every direct branch stays inside `.text`. Message numbers are of the installed files
named in the study; no game text is copied.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| OL1 | broadcast entry (0x4C2F00), presets (0x4C3B90, 0x4C3BE0), portrait menu (0x56A9D0, 0x56B110, 0x56B180), typed text (0x571950) | Orders are text: F1–F6 send presets 500–505 of `mes\broadcast.mes` with the pointer's tile; the portrait menu (`mes\follower_ui.mes` 0–7) sends name + label (wait not for a mind-controlled follower; walk and attack after a target cursor; inventory and sheet open windows); text typed after Enter goes the same way. | Original (C14) |
| OL2 | broadcast (0x4C31A0) | A copy of the PC's follower array (direct followers, not waiting NPCs), each within 10 tiles (0x4B96F0); `#` addresses every one, otherwise the first whose known name (0x441B60) is a case-insensitive prefix of the text; one AI pass each (0x4A84F0); no match → multiplayer phase only. | Original (C14) |
| OL3 | keyword lookup (0x4C3B40), init (0x4C2C10) | Whole-word, case-insensitive, first match of keywords 100–117; 14 types from `Rules\BroadCastRules.mes`; "follow other" unreachable (keyword 117 repeats "follow"). | Original |
| OL4–OL5 | 0x4C348D, 0x4C34E2 | Leave: mind control → nothing, else farewell and a **forced** leave. Wait: mind control → nothing, else acknowledgement, animations interrupted, ordinary wait with its timer. | Original |
| OL6 | 0x4C3542, 0x4AD950, 0x4AA8C0 | Follow/come: follow check (limit ignored only when forced); refusal → "cannot" line only; acceptance → acknowledgement and an unwait that never acts (followers in the array never wait): no state change. | Original bug (likely); decision candidate D1 |
| OL7–OL9 | 0x4363E0, 0x45F5C0, 0x45F600, 0x436960, 0x433C80 | Move: please-move goal (68) out of the PC's way. Stay close / spread out: clear / set NPC flag 0x10 (spread out also starts goal 69 when newly set). Walk here: run-to-tile goal (4) to the order's tile. | Original |
| OL10 | 0x4C3849, 0x4AA4A0 | Back off and stop are "stop attacking" (HS18): no movement. | Original (combat side A7) |
| OL11 | 0x4C3703, 0x4C377D, 0x43C570, 0x4AA580, 0x4A9650 | Curse and attack: boundary recorded, settled by stage A7. | Not checked here (A7) |
| OL12 | 0x418780, 0x418250, 0x418870, 0x4AD800, 0x568430 | Lines: random entry of `mes\gd_npc_*.mes` by both genders: acknowledgement 2100–2199, cannot 2200–2299, farewells 1900–2099; an NPC's own dialog may override (not traced); none when it cannot speak; floated with speech. | Original |
| OL13 | 0x4133B0, 0x4C0FC0 | Leave farewell: reaction 40 or less → 1900–1999 (labelled friendly), above → 2000–2099 (labelled hostile). | Original bug (likely); D3 |
| OL14 | 0x4C3BE0 | Preset 5 (F6) has no `#` and names no follower: no effect in single player. | Original bug (likely, data); D2 |
| OL15 | 0x56A9D0, 0x56B180 | The portrait order goes to the first follower in reach whose name shares the prefix, not necessarily the clicked one; none beyond 10 tiles. | Original bug (likely); D4 |
| OL16 | 0x56B6F0, 0x4411C0 | No portrait and no order for a waiting NPC. | Original (C14) |
| OL17 | 0x4C0DE0, 0x4B0980 | Arming: negative reaction change by the direct PC leader → check-leader (NPC flag 0x10000) and reaction bit (critter flags 2, 0x1000000); PC alignment change → each array follower gets check-leader and 0x2000000 (rose) or 0x4000000 (fell). | Original (C13) |
| OL18 | 0x4AC380, 0x423300 | Non-combat pass with a leader: leave combat mode; waiting → stop; request follow; loyalty step only without a running action goal (overlay goals do not count). | Original (C13) |
| OL19 | 0x4346A0, goal 24 (0x5A85D0), 0x433270, 0x4B7CD0 | A follow goal starts only beyond 4 (7) tiles and restarts itself every 100 ms in real time; so the loyalty step is skipped while it lives and runs only while the follower idles near its leader without one. Frequency in play: F4 needed. | Original (intent unclear); answers O4 statically; D5 |
| OL20 | 0x4AC380, 0x45DFC0, 0x413A90 | Refused check (limit ignored) → unforced leave; if it left: jilted, then the reason's refusal line (1600–2999 by reason); a refused leave falls through to check leader. | Original (C13) |
| OL21 | 0x4ADB50, 0x414290 | Check leader clears the three bits; no warning when mind controlled by the leader, Persuasion Master, or forced; reaction ≤ field 5 + 20 → 3; alignment gap > field 6 − 100 (too good) → 1 or > field 7 − 100 (too bad) → 2; warning lines 5200–5499; never leaves. | Original (C13) |
| OL22 | 0x4AA990, 0x4C0DE0 | Bits armed just before the leader is cleared stay until the NPC follows again (one stale test). | Original (latent) |
| OL23 | 0x4AA7A0, table 0x5B5088 | Game-time kind 17 naming the NPC; 0, 15 min, 2 h, 1 d, 7 d, 30 d by wait index, +1 with any leader Persuasion training before the cut (index 5 then has none), ≥ 6 none; index 0 fires at the next ping. | Original (with row Wait timer) |
| OL24 | 0x4AA990, 0x4B0FD0 | Expiry: no leader → nothing; wait off and jilted; reaction `2 × (leader charisma − 20)` (R2 rules); leader cleared; no line. | Original (C12) |
| OL25 | 0x4AA8C0, 0x4AAA30, 0x45BFF0 | Any successful unwait cancels the event: dialog `UW`, leave, join, kill notification (forced), forced disband on object destruction (0x45E180). | Original (C11) |
| OL26 | 0x45BCE0, 0x45C2F0, 0x45C580 | The event stays with the waiting NPC's map, parked while the PC is away, fires at the first ping after the return when overdue. | Original (with OQ3) |
| OL27 | 0x4A69C0, 0x460630 → 0x550750 → 0x552770, 0x5529C0, 0x4A7030 | Per level reached, local PC only: message-window entry `intgame.mes` 21 (title), 11 (level), 12 (points) or the scheme text (`level.mes` 2/3/4, 1 when completed); sound 3005; none in the last-level branch, none for followers. | Original (XP5) |
| OL28 | 0x4A69C0 | The points line shows the sum before the 56 clamp. | Original bug (cosmetic); D6 |
| OL29 | 0x440FC0, 0x440FF0, 0x440B40 | Waiting followers get the level step only inside a screen-sized rectangle centred on the PC among loaded objects (alive, waiting, top PC leader this PC). | Original (L7); D7 |
| OL30 | 0x44ABC8, 0x44B4E0, 0x44BA70, 0x4A6D90 | Action 139: object operand (a list) and a number; each PC or NPC gets field 245 written, no range check, no purchase until its next level step. | Original |
| OL31 | 0x4064B0, 0x410C50, 0x406520, 0x40BDB0, 0x40BE70, 0x40BFC0, 0x4E50E0 | Handles saved as IDs; a map open converts every marked object's handle fields and array elements back among loaded objects; not found → null (in place). | Original (answers O7) |
| OL32–OL33 | as OL31, 0x4D3460 | A waiting follower on another map keeps its leader as an ID and rebinds when its map opens; a follower carried to another map is that map's object from then on. | Original (O7; OL33 partly inference) |

## Decisions to revisit

These owner decisions or accepted proposals rest on a fact of the earlier reading that the original does not share.
Nothing below is changed by this ledger; each needs the owner's word or its owning slice.

1. **Reputation D1** ([reaction study](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions)):
   the default "source parse" (signed effects get origin = faction = adjustment) is a reconstruction slip. The
   original keeps the written scope, which is the other reading D1 offered; under D8 a reconstruction slip gets
   no switch. **Decided 2026-10-17:** the owner chose the written scope in both settings.
2. **Dodge 5 × level** ([attack resolver](../world/attack-resolver-runtime.md), ARC-COMBAT-001,
   `AttackRules.DodgeEffectiveness`): implemented from the earlier reading; the original uses 2 × level. Under D8 both
   settings follow the original; ARC-WORLD-023 changed the resolver to 2 × level.
3. **Skills §9.5** ([skills study](skills-evidence.md#9-consequences-for-steamwoven-project-policy-proposals)):
   SK1, SK2, SK4 and SK5 are reconstruction slips, so the Original setting reproduces none of them; SK2's "one point
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
   ARC-RES-037): held only in the earlier reading; in the original the removal does not persist. ARC-SCRIPT-008: it persists in
   Steamwoven (D2 fix) and not in the Original setting (`TileScriptChangesDiscarded`).
7. **Starting hero `*n` / `/n`** ([starting hero](starting-hero-evidence.md), SH-07 and the runtime's
   refusal): the original parses the factor, so backgrounds 5 and 11 double their reaction adjustment.
   **Implemented 2026-10-18 by ARC-WORLD-035** as the recommended option (read the factor as the original does, both
   modes; `/0` refused in both, it would divide by zero), put to the owner, who had not answered by then.
8. **Magic slices** ([magic core](magic-core-evidence.md)): MG1, MG2 (mastery `ceil(cost / 2)` at run
   time) and MG8 are reconstruction slips; O9 and O10 are answered by the reconstruction-change rows.
9. **AI stages** ([AI plan](npc-combat-ai-plan.md)): P1, P2, P3, LM1, LM2, LM13 and LM18 are reconstruction slips
   (P3 with a different reading than the plan expected); P4–P7 are the original's behaviour.
10. **Companion wait timeout** ([companions](companion-follow-evidence.md#open-questions) O3, event
    queue TE8): the timeout is real for leaders without extraordinary charisma.
11. **Auto-level A1 and LM11** ([Virgil auto-level](virgil-auto-level.md#anomalies-s)): no recursion
    in the original; followers rise at PC level 50 as the 1.0.7.4 notes say.

## Second reading

Four second readers re-read 77 decision-bearing rows from the executable without the first
verdicts: magic (MG1–MG12, MG15, MG18, MG19, the reconstruction changes, O9, LM9, LM20), skills (every basic
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

Read once (first reading only; P5, LM3 and LM4 were re-read by ARC-RES-054): LM10, LM3, LM12, MG13–MG17, MG20, LM5–LM7, LM19, SK6, SK7, auto-level
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
- **Wilderness A1**: the original also clears each moved light's "modified" mark, which the earlier reading omitted.

## Not checked

- Data questions: wilderness A4/A5, P7's label in `ai_params.mes`, how often shipped scripts reach
  SK1. (`xp_level.mes` entries 50/51 behind A1 are answered by [XP12](#experience-and-levels-arc-world-030): the table is
  strictly increasing.) (LM15 labels and the LM17 keys
  are answered by the [combat-domain data census](combat-data-census.md): the description header's
  labels match the art, and no shipped `Resist:` uses an LM17 key.)
- Code left for later: wilderness A2 (editor only); locomotion W4 and the frame-time clamp;
  companion C2, C4, C10; how often the loyalty step runs in play (read statically as [OL19](#companion-orders-and-loyalty-arc-res-064), an observation is still needed); what a teleport does after it is processed (followers, the destination map, W9); poison ticks on a switched-off critter ([TE11](#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019)); reputation A6
  and A7; the first-tick marker after a load (OQ2, H1).
