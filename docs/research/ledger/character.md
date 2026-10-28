# Original verification ledger: Character

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Skills, levels and character (ARC-RES-044, ARC-RES-031, ARC-RES-027)

Source: [skills](../skills-evidence.md#8-anomalies-found-by-this-reading),
[load map](../combat-domain-load-map.md#8-anomalies-found-by-this-reading),
[Virgil auto-level](../virgil-auto-level.md#anomalies-s),
[starting hero](../starting-hero-evidence.md).

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

## Durable effects (ARC-WORLD-029)

Source: [durable effects runtime](../../world/durable-effects-runtime.md) and
[FR-CHAR-022](../../requirements/characters-skills.md#fr-char-022). Read on 2026-10-17 by a first reader and, independently,
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

Source: [experience runtime](../../world/experience-runtime.md),
[FR-CHAR-013](../../requirements/characters-skills.md#fr-char-013) and
[FR-CHAR-014](../../requirements/characters-skills.md#fr-char-014). Read on 2026-10-17 by a first reader and, independently,
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

## Character sheet (ARC-UI-007)

Source: [viewer character sheet](../../rendering/terrain-viewer.md#character-sheet-arc-ui-007) and
[FR-UI-004](../../requirements/ui-media-lifecycle.md#fr-ui-004). Read on 2026-10-17 by a first reader and, independently,
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

## Starting kit (ARC-WORLD-034)

Source: the [best items](../../world/equipment-runtime.md#best-items-arc-world-034) of the equipment runtime: which items a
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

Source: [durable effects runtime](../../world/durable-effects-runtime.md#blessings-and-curses-arc-world-035) and
[FR-CHAR-023](../../requirements/characters-skills.md#fr-char-023). Read on 2026-10-18 by a first reader and, independently, a
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

Source: [advancement runtime](../../world/advancement-runtime.md) and
[FR-CHAR-021](../../requirements/characters-skills.md#fr-char-021). Read on 2026-10-17 by a first reader and, independently, a
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

## Character point pool (ARC-WORLD-052)

Source: the [character point pool](../../world/character-point-pool.md) (task S5 of the [skills plan](../skills-plan.md)) and the
[skills study](../skills-evidence.md#2-ranks). Question: how the character editor buys and refunds stats, hit and fatigue points,
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

**F0 facts raised** (owner decision 2026-10-20): the [skills study](../skills-evidence.md#2-ranks)'s editor + and − buttons,
their floor and snapshot and the hint (0x57ACD0, 0x57AEB0, 0x55D3A0, 0x55D6F0, 0x5597C0, 0x55AE70), now F2, with the
correction that the floor does not hold through a − click whose level an effect pins (CP6); the skills plan's S5 check list;
[FR-CHAR-015](../../requirements/characters-skills.md#fr-char-015)'s editor path (0x55A5C0, 0x5597C0, the setter's guards) and
[FR-CHAR-016](../../requirements/characters-skills.md#fr-char-016)'s (0x57ACD0, 0x57AEB0, 0x55D3A0), now F2. Not raised (other
tasks' functions): the sheet's skill page draw (0x55BD10, S6), the spell UI's unlearn (0x57C670, MT3), the degree raise
wrapper (0x589A20, TECH-2).

**Correction to TC10.** The degree snapshot the editor reads is 0x64DEEC (CP1), filled when the editor opens and read by
the tech page's draw (CP2); the address TC10 names belongs to the per-player copies of 0x55F360 and is never read. The sell
handler itself has no comparison, as TC10 says; in the in-game editor the − button is not drawn at the snapshot. The decision
(both modes keep the floor) stands.

## Backgrounds (ARC-WORLD-069)

Source: the [equipment runtime](../../world/equipment-runtime.md#backgrounds-arc-world-069). Question: where the background id lives
(field, object types, prototype or instance, how it is set), every rule that reads it (what it decides, which item property, the
result code and message), whether the wear and carry refusals apply to NPCs and on which paths, and every other effect of a
background. Read on 2026-10-21 by two fresh agents that saw only the questions (method step 6), independently; they agree on every
row, so the rows are F2 (BG8's tile tests are inferred from their use by both).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| BG1 | field 0x101, reader (0x4C2690) | The background id is PC field 0x101 `obj_f_pc_background` (0x102 its description number in `mes\gameback.mes`), in the PC-only block; the reader answers 0 for a null object or any type but PC (15), so an NPC never has one. The generic getter reads the instance's value, else the prototype's. Default 0, "no significant background"; the built-in pre-made PC prototypes carry 39–50 (tagged "NPC", not selectable at creation). | Original |
| BG2 | creation (0x5448E0, 0x4C25E0, 0x4C2650, 0x4C2B50), data | Only character creation sets it: next and previous step through the backgrounds a race and gender may take (`rules\backgrnd.mes` block n: 10n description, 10n+1 effect, 10n+2 race and gender codes, 10n+3 gold, 10n+4 item prototypes); setting writes both fields and adds the effect with cause 1 (background); clearing zeroes them and removes every cause-1 effect; a change of race or gender clears it. | Original (no character creation yet) |
| BG3 | wear check (0x464D20) | After the spell-flags test (code 9) and before the broken, slot, size and gender tests: an item of complexity (field 0x60) above 0 and background 69 (Magick Allergy) is code 18, `mes\item.mes` 118, shown only to the local player. No PC or type test: an NPC passes through the reader's 0. Wield, the paperdoll, wield-best at start and in play (silently skipped) and the combat re-check all run it. | Original |
| BG4 | insert check (0x466510) | For a PC or NPC parent only (containers skip it): the weight (code 1; a key going onto a key ring skips it), then an item of complexity below 0 and background 72 (Technophobia) is code 15, `mes\item.mes` 115; then the room (code 2). Pickup, loot, dialog and script transfers, steal and plant, assembly products, the start kit and the background's gift items, unwield, and the NPC AI's loot filter run it; the inventory and barter windows run it before their raw insert; a few raw inserts (a same-parent slot move, multiplayer replays, an animation callback) do not. | Original |
| BG5 | 0x45FC00, 0x45FB90 | "Speaks like an idiot": intelligence 4 or less, or background 55, 74 or 75 (dialog intelligence and the script's intelligence read as 1). "Dark sight": background 78 or a critter flag, which inverts the darkness penalty of a skill check (0x4C8430). | Original (not modelled) |
| BG6 | effective stat (0x4B0490) | Backgrounds 62 and 63 change magick points by ±4 by the hour (6..17); 64 and 65 by an outdoor or natural tile; 66 (outdoors), 67 (on water) and 68 (light below 128) raise strength by 2 and lower dexterity, intelligence and willpower by 2 (66 elsewhere raises intelligence by 2). | Original (not modelled) |
| BG7 | 0x4C2950, 0x4C26F0, 0x4A5710, 0x4A57F0, 0x463E20 | Educator (51) raises each follower's skill training to one below the PC's; the start kit gives the background's gold and items (each flagged and inserted with the insert check); multiplayer auto-equip scales gold by the background's money; a container's generated inventory seeds its random generator with the player's race × background, gender and name. 0x4C2830 (take the gold and items back) has no caller. | Original (not modelled) |
| BG8 | effect (rules\effect.mes, cause 1) | Every other effect of a background (stats, skills, resistances, magick and tech points, followers, age) is its effect line; no price, reaction or script reads the background directly. | Original (not modelled) |

## Background table (ARC-DATA-044)

Source: the reader [`BackgroundTable`](../../../src/Arcanum.Data/Characters/BackgroundTable.cs) (N10a, FR-CHAR-005), on top of
BG1–BG2 and [ARC-RES-025](../character-creation-evidence.md#backgrounds-and-starting-gold). Question: how the original loads
`rules\backgrnd.mes` and `mes\gameback.mes`, how a block is found, which race and gender may take it, how the creation buttons
step, and how a description is split into name and text. Read on 2026-10-28 by the implementing task with `llvm-objdump`
(one reader, F1); the installed counts are checked by the opt-in real-data test (79 blocks, 67 offered to a PC, the CC-06
counts per race and gender).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| BD1 | `background_init` (0x4C2270), gamelib table entry "Background" | Loads `rules\backgrnd.mes` and `mes\gameback.mes` at game start (an init function, so the base sources); either missing fails the init. It also allocates the 32-byte name buffer. | Original |
| BD2 | search first/next/previous (0x4C2300, 0x4C2390, 0x4C2420) | Block b is looked up by its condition message 10b+2; the search starts at 0, after or before the current block and stops at the first block whose 10b+2 is absent (no wrap), returning the first block the legality check allows. | Original |
| BD3 | legality (0x4C2B50) | An empty condition allows anyone. Otherwise the condition is upper-cased in place (ASCII a–z) and searched (substring) for the race code (table 0x5B6AEC: HU, DW, EL, HE, GN, HA, HO, HG for races 0..7) followed by `F` or `M` (gender 0 or 1), then for `ANY`, then, for an NPC object (type 16) only, for `NPC`. The table has no entry for races 8..10. | Original |
| BD4 | creation buttons (0x5452C0, 0x545300), handler (0x544FF0) | Next: the next allowed block, else the first; when neither exists the button changes nothing. Previous: the previous allowed block, else the last one reached by stepping forward from the current block, else the current block; it always succeeds. On success the handler clears the background and sets the result with its description number, so pressing previous with no other choice re-sets the current block (its effect is added once). | Original |
| BD5 | `background_set` (0x4C25E0), description (0x4C24B0) | Stores the block in field 0x101 and the description number (`atoi` of 10b+0) in 0x102, then adds effect `atoi` of 10b+1 with cause 1. An empty slot is effect 0, the module's `gameeffect.mes` effect. A missing message reads the text "Error! Missing line …" (0x4D43A0), so its number is 0. | Original |
| BD6 | description text (0x4C24E0, 0x4C2530) | Description 0 reads 1000; 1..999 have no text; the shown description is everything after the first line feed (the whole text when there is none). | Original |
| BD7 | name (0x4C2590) | Copies at most 32 bytes of the text into the 32-byte buffer and cuts it at the first line feed; without a line feed it logs "Background text has no name!" and returns the copy, which is unterminated when the text has 32 bytes or more. | Technical bug of the original (owner rule 2026-10-28): both modes refuse such a text (`NoName`), no switch; latent, every installed text a PC can see has a short first line |
| BD8 | money and items (0x4C26F0, 0x4C2B10), base money (0x4C2A70), scaling (0x4C2AA0) | Money is `atoi` of 10b+3; the item list 10b+4 is split on space, tab and line feed and each token `atoi`'d to a basic prototype. Block 0's money is the base the multiplayer money scaling divides by. | Original |

Project admission policy (not a rule mode): a block whose description, effect, money or item message is absent, or whose number
or item token is not a plain digit run, is refused (`MissingMessage`, `Malformed`, `InvalidText`) instead of read through `atoi`;
a text without a line feed in its first 32 bytes is `NoName`. No installed block or PC-visible text is refused.
