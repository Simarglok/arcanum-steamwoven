# Script census: refused actions and conditions ranked by use

Status: **Research**, work item ARC-RES-1101 (2026-11-12, issue #1101), slice **E5-r** of the
[missing-systems plan](../project/missing-systems-plan.md#e5-script-and-dialog-coverage). It
counts which condition and action types the widest admitted set still refuses, how much installed
content each one blocks, on which maps, attachment points and quests, and in which order admitting
them frees the most. It adds one read-only probe mode (`--script-refusal-census`) and this note; it
changes no production API, viewer, session or launcher. It builds on the
[SCR opcode and attachment census](scr-census.md) (ARC-RES-045, which counted opcode use over every
line and over the lines a run reaches) and answers its last open question for the plan: *which
refusal to remove first*.

Requirements: [FR-SCRIPT-001](../requirements/story-social-ai.md#fr-script-001) (which events carry
scripts), [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002) (which conditions and
actions the content needs) and [FR-SCRIPT-004](../requirements/story-social-ai.md#fr-script-004)
(quest states, here the quests a blocked script names). No requirement's coverage changes.

## Answer

All numbers are for the installed 1.0.7.4 `Arcanum` module and set **S33**
(`ScrAdmissionSet.S33`, the widest set: 20 of 53 conditions, 81 of 142 actions). Each host admits a
subset of S33 or a set of its own (the hosts' sets are not compared here), so what a host admits
beyond S33 is not counted.

- **How much is blocked (D).** 1,809 attached scripts run from line 0 on 7,766 placed sites (an
  object, a tile or a sector holding the script). 1,346 of them (5,118 sites, 65.9 %) are admitted
  by the production admission, whole with every script they call. 447 (2,529 sites, 32.6 %) are refused because a
  reachable clause has a type outside S33. 60 scripts have a *non-type* refusal (a computed
  goto in 13, a goto beyond the end in 2, an object operand kind S33 does not admit in 45): 16 of
  them have no refused type, 44 have both and are never freed by types alone (section "Refusals that are not types").
- **Which types (D).** 79 types occur on a reachable line and are refused: 26 conditions and 53
  actions (table below). Nine more types occur only on lines no run reaches (second table).
- **What frees most (D).** Taking the plan's slices as they are, the order that frees the most
  placed sites is: a slice the plan does not have, **VIT** (get hit points, get fatigue points, heal
  poison: one food use script stands on 729 objects on 27 maps, 836 sites freed alone), then
  **E5-a** (NPC state: 550 sites alone, 110 scripts), then **CRQ** (critter state conditions, also not
  in the plan: 267 alone), **E5-b** (208), **E5-c** (98), **E4-g spell effects** (62), a
  location/scroll-distance pair (**SCN**, 6), **E5-e** (42), **E7-b** (12). The slice table and
  the greedy order are in "Slices and the order that frees most".
- **The plan's E5-a, E5-b, E5-c and E5-e match the data, with three changes** (section "What it
  means for the issues"): follow and attack are already admitted and leave E5-a; `call script at`
  occurs once and no run reaches it, so E5-e is `loop break` alone; transfer item is one site.
- **Quests (D).** 56 of the 447 blocked scripts name a literal quest number and 12 quests are
  named by two or more of them; the table names those. Quest 1039 leads (6 blocked scripts: can
  see, newspaper, daytime, met-before and invisible).

## Method

- **Root and site.** A *root* is a script attached to an object, tile or sector and run from line 0
  at an interpreted attachment point (every point except the dialog-override slot, which names a DLG,
  and the seven trap-flagged scripts, which trap code runs); a root's run follows calls, call-in and
  call-at entries and the line after every reached dialog action, exactly as the live set of
  [ARC-RES-045](scr-census.md#reproduction) (`CsLiveSet`). A *site* is one holder: a placed
  object, a tile or a sector. Map aggregates repeat the mobiles and prototype rows are inherited
  defaults, so neither counts as a site (66 scripts occur only as prototype defaults).
- **Refused type.** A condition or action type on a clause the run can reach that is outside the set
  under test. A root is *blocked* by every refused type its reach holds. The verdict that the root has
  no other refusal comes from the production `ScrAdmission.AdmitFrom` run against the set under test with every
  condition and action type opened (its own value kinds, focus kinds and counter rule kept), so a focus or value
  kind outside the set, a bad index, a computed goto or a goto beyond the end is kept apart from the type
  refusals and the census cannot drift from the dispatcher.
- **Sole blocker.** A type is the sole blocker of a root when it is the only refused type the root
  reaches and the root has no other refusal: admitting it alone frees that root.
- **Slices.** Each refused type belongs to one group, taken from the plan's own descriptions
  (E5-a, E5-b, E5-c, E5-e, E7-b, E4-g) and, for types the plan does not name, to three added groups
  (VIT, CRQ, SCN); every refused type is in exactly one. *Freed alone* counts roots whose every refused type is inside the group.
- **Greedy order.** Each step adds the type (or the slice) that frees the most placed sites, then
  the most roots, then the one that takes part in the most still-blocked roots, then the lower
  name; a root freed only by a combination counts when its last type arrives. Greedy is an order
  that frees a lot early, not a proven optimum; the slice table gives the one-slice-alone numbers
  the order is built on.
- **Quest.** A literal quest number in operand 1 of "pc quest state" (condition 6) or "set pc quest
  state" (action 16) on a reached line; a computed quest number is not counted.
- **Maps.** The map folder name of a site (81 maps, no site of a terrain template occurs). Tile and sector scripts of a map count as that map's.
  `arcanum1-024-fixed` is the main map (5,656 of the 7,766 sites).
- Numbers and map names only leave the probe: no script text, object names or payload.

## Reproduction

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-refusal-census-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-refusal-census \
  --game-root "$GAME" [--set S30|S31|S32|S33|S38|S43|S52|S53|S54] > "$PRIVATE/refusal-census.json"
```

The run takes about five seconds and is deterministic: two runs on the owner's installation
(`/Applications/Arcanum`, module `Arcanum`) have SHA-256 `08b6d2fa6c586c44e66a8d6d8162092dee208ec76c5ac1688a79ee38fdfaf951`. It scanned 81 maps, 1,680
sector files, 12,723 mobile files, 81 aggregates, 413 terrain template sectors and 1,423
prototypes, decoded 2,399 scripts with no failure, and reached 1,940 scripts and 13,358 lines
from the 1,809 roots (two attached numbers have no file). The self-test builds synthetic scripts and
checks refused types by reach and calls, quests, the non-type refusals apart, the greedy order and the
group keys. The full report stays in the ignored `research-private/`; the tables below are its
aggregates.

## Refused types

Ranked by live clauses (clauses on lines a run reaches, once per clause). *Blocked roots* and
*blocked sites* count the attached scripts (and their holders) whose run reaches the type; *sole-blocker roots*
are the roots it frees alone; *maps* the maps with a blocked site. Names are the descriptive
names of [the opcode census](scr-census.md#condition-types-d) (`c` condition, `a` action).

| Type | Name | Slice | Live clauses | Live scripts | Blocked roots | Blocked sites | Sole-blocker roots | Maps |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| a99 | `change_script` | E5-a | 70 | 64 | 52 | 204 | 37 | 14 |
| a59 | `change_art_num` | E5-a | 48 | 43 | 41 | 170 | 30 | 7 |
| a45 | `set_day_standpoint` | E5-a | 47 | 44 | 44 | 234 | 5 | 4 |
| a127 | `toggle_item_inventory_display` | E5-b | 40 | 16 | 16 | 21 | 5 | 4 |
| a46 | `set_night_standpoint` | E5-a | 39 | 36 | 36 | 212 | 0 | 5 |
| a106 | `toggle_sector_blocked` | E5-b | 34 | 10 | 9 | 99 | 1 | 1 |
| c8 | `obj_has_bless` | E5-c | 31 | 12 | 12 | 15 | 0 | 4 |
| c15 | `obj_is_invisible` | CRQ | 31 | 30 | 32 | 73 | 2 | 6 |
| a26 | `remove_blessing` | E5-c | 29 | 3 | 14 | 18 | 2 | 4 |
| c13 | `obj_is_shrunk` | E4-g | 26 | 25 | 26 | 56 | 1 | 7 |
| c10 | `obj_met_pc_before` | CRQ | 25 | 23 | 27 | 40 | 8 | 11 |
| c12 | `obj_is_polymorphed` | E4-g | 22 | 22 | 23 | 53 | 0 | 5 |
| a20 | `loop_break` | E5-e | 22 | 18 | 17 | 68 | 7 | 4 |
| a80 | `get_magictech_adjustment` | E4-g | 22 | 21 | 21 | 23 | 16 | 10 |
| a124 | `stop_script_eye_candy` | E4-g | 22 | 22 | 22 | 42 | 18 | 1 |
| c11 | `obj_has_bad_associates` | CRQ | 21 | 21 | 22 | 52 | 0 | 4 |
| a25 | `add_blessing` | E5-c | 21 | 13 | 13 | 17 | 1 | 4 |
| c1 | `daytime` | E5-c | 19 | 19 | 19 | 53 | 8 | 3 |
| a22 | `critter_disband` | E5-a | 17 | 16 | 15 | 220 | 9 | 7 |
| c28 | `obj_can_see_obj` | CRQ | 16 | 15 | 16 | 72 | 7 | 6 |
| a104 | `play_spell_eye_candy` | E4-g | 16 | 8 | 8 | 23 | 4 | 3 |
| c26 | `obj_is_in_dialog` | CRQ | 15 | 11 | 13 | 38 | 1 | 3 |
| a31 | `adjust_reaction` | E5-a | 14 | 10 | 12 | 70 | 5 | 5 |
| a66 | `create_item` | E5-b | 14 | 12 | 12 | 62 | 7 | 5 |
| c7 | `global_quest_state` | E5-c | 13 | 7 | 6 | 6 | 3 | 1 |
| a126 | `destroy_item_named` | E5-b | 13 | 2 | 2 | 4 | 0 | 2 |
| c45 | `sector_is_blocked` | E5-b | 12 | 9 | 8 | 94 | 0 | 1 |
| a88 | `toggle_open_closed` | E5-b | 12 | 11 | 11 | 54 | 2 | 4 |
| a108 | `get_fatigue_points` | VIT | 12 | 12 | 17 | 761 | 7 | 30 |
| a53 | `set_lock_state` | E5-b | 11 | 11 | 10 | 11 | 6 | 4 |
| a117 | `grant_one_fate_point` | E5-c | 11 | 10 | 14 | 37 | 10 | 4 |
| a85 | `queue_newspaper` | E7-b | 10 | 7 | 8 | 14 | 4 | 2 |
| c31 | `obj_is_in_combat` | CRQ | 9 | 9 | 9 | 9 | 5 | 4 |
| c36 | `obj_is_open` | E5-b | 9 | 8 | 9 | 51 | 0 | 4 |
| a27 | `add_curse` | E5-c | 9 | 9 | 9 | 9 | 1 | 2 |
| a30 | `set_reaction` | E5-a | 9 | 9 | 9 | 75 | 4 | 5 |
| c9 | `obj_has_curse` | E5-c | 8 | 8 | 8 | 8 | 0 | 2 |
| c40 | `rumor_known` | E5-c | 8 | 8 | 8 | 16 | 5 | 2 |
| a28 | `remove_curse` | E5-c | 8 | 8 | 8 | 8 | 0 | 2 |
| a123 | `touch_art` | E5-b | 8 | 6 | 6 | 10 | 3 | 2 |
| c34 | `obj_within_range` | CRQ | 7 | 7 | 7 | 7 | 3 | 2 |
| a103 | `fade` | E7-b | 7 | 4 | 8 | 6 | 0 | 2 |
| a32 | `get_armor` | VIT | 6 | 6 | 7 | 110 | 1 | 3 |
| a105 | `get_hours_since_startup` | E5-c | 6 | 6 | 6 | 12 | 0 | 3 |
| a110 | `toggle_monster_generator` | E5-b | 6 | 6 | 6 | 65 | 0 | 2 |
| c29 | `obj_can_hear_obj` | CRQ | 5 | 5 | 4 | 7 | 0 | 1 |
| c46 | `monstergen_disabled` | E5-b | 5 | 5 | 5 | 63 | 0 | 2 |
| a97 | `get_current_hour` | E5-c | 5 | 5 | 5 | 7 | 2 | 1 |
| c38 | `obj_is_undead` | CRQ | 4 | 4 | 5 | 13 | 1 | 1 |
| a41 | `transfer_item` | E5-b | 4 | 1 | 1 | 1 | 1 | 1 |
| a90 | `get_scroll_distance` | SCN | 4 | 4 | 14 | 47 | 4 | 2 |
| a107 | `get_hit_points` | VIT | 4 | 4 | 4 | 730 | 2 | 27 |
| c25 | `obj_has_surrendered` | CRQ | 3 | 3 | 3 | 3 | 3 | 2 |
| c51 | `prowling` | CRQ | 3 | 3 | 3 | 87 | 3 | 1 |
| a94 | `set_written_start` | E5-b | 3 | 2 | 2 | 1 | 2 | 1 |
| a95 | `get_location` | SCN | 3 | 3 | 3 | 75 | 0 | 1 |
| a132 | `end_game_and_play_slides` | E7-b | 3 | 3 | 3 | 1 | 0 | 1 |
| c16 | `obj_has_mirror_image` | E4-g | 2 | 2 | 2 | 2 | 0 | 2 |
| c41 | `rumor_quelled` | E5-c | 2 | 2 | 2 | 7 | 0 | 1 |
| a17 | `set_quest_global_state` | E5-c | 2 | 2 | 2 | 2 | 2 | 1 |
| a39 | `get_origin` | CRQ | 2 | 2 | 3 | 46 | 1 | 1 |
| a51 | `quell_rumor` | E5-c | 2 | 2 | 3 | 1 | 1 | 1 |
| a57 | `toggle_invulnerability` | CRQ | 2 | 2 | 2 | 13 | 0 | 2 |
| a89 | `get_faction` | CRQ | 2 | 2 | 2 | 11 | 2 | 1 |
| a137 | `adjust_stat` | VIT | 2 | 2 | 2 | 3 | 0 | 3 |
| c19 | `obj_is_monster_of_type` | CRQ | 1 | 1 | 1 | 1 | 1 | 1 |
| c30 | `obj_is_invulnerable` | CRQ | 1 | 1 | 1 | 12 | 0 | 1 |
| c37 | `obj_is_animal` | CRQ | 1 | 1 | 1 | 31 | 1 | 5 |
| a38 | `get_social_class` | CRQ | 1 | 1 | 1 | 3 | 0 | 1 |
| a63 | `give_quest_xp` | E5-c | 1 | 1 | 1 | 1 | 0 | 1 |
| a70 | `get_weapon_type` | VIT | 1 | 1 | 1 | 1 | 1 | 1 |
| a86 | `float_newspaper_headline` | E7-b | 1 | 1 | 1 | 4 | 0 | 1 |
| a92 | `rename` | CRQ | 1 | 1 | 1 | 3 | 1 | 1 |
| a111 | `get_armor_coverage` | VIT | 1 | 1 | 1 | 1 | 0 | 1 |
| a119 | `set_pc_quest_unbotched` | E5-c | 1 | 1 | 1 | 1 | 1 | 1 |
| a128 | `heal_poison` | VIT | 1 | 1 | 1 | 729 | 0 | 27 |
| a130 | `stop_spell` | E4-g | 1 | 1 | 1 | 1 | 0 | 1 |
| a140 | `set_day_standpoint_ex` | E5-a | 1 | 1 | 1 | 4 | 0 | 1 |
| a141 | `set_night_standpoint_ex` | E5-a | 1 | 1 | 1 | 4 | 0 | 1 |

Types outside S33 that occur only on lines no run reaches (all-line counts):

| Type | Name | Clauses on all lines | Scripts |
| ---: | --- | ---: | ---: |
| a131 | `queue_slide` | 54 | 13 |
| c23 | `obj_has_max_followers` | 3 | 3 |
| a35 | `adjust_gold` | 2 | 2 |
| a96 | `get_day_since_startup` | 2 | 1 |
| a133 | `set_rotation` | 2 | 1 |
| c2 | `has_gold` | 1 | 1 |
| c52 | `waiting_for_leader` | 1 | 1 |
| a55 | `call_script_at` | 1 | 1 |
| a98 | `get_current_minute` | 1 | 1 |

`queue_slide` (54 clauses in 13 scripts) is not live because the model reaches no script that the
engine chooses itself (end-game slides 997 to 999, [ARC-RES-045](scr-census.md#reproduction)); those
scripts are real content for the media slice (E7-b) even though no attachment reaches them.
The other never-live rows are one to three clauses each.

## Slices and the order that frees most

| Slice | Content | Blocked roots | Blocked sites | Maps | Roots freed by the slice alone | Sites freed alone | Maps freed alone |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| E5-a | NPC state actions | 166 | 794 | 24 | 110 | 550 | 19 |
| E5-b | World-object actions and queries | 71 | 320 | 15 | 40 | 208 | 9 |
| E5-c | Story, time, reward, blessing and curse | 80 | 156 | 11 | 39 | 98 | 6 |
| E5-e | Loop break | 17 | 68 | 4 | 7 | 42 | 2 |
| E7-b | Media: fade, end slides, newspapers | 17 | 24 | 3 | 7 | 12 | 2 |
| E4-g | Spell effects and spell-state queries | 78 | 145 | 18 | 39 | 62 | 9 |
| VIT | Vitals: hit points, fatigue, poison, armor, stat adjust (no plan slice) | 30 | 876 | 32 | 13 | 836 | 30 |
| CRQ | Critter state and social queries (no plan slice) | 105 | 394 | 23 | 49 | 267 | 15 |
| SCN | Scene queries: location, scroll distance (no plan slice) | 17 | 122 | 2 | 4 | 6 | 1 |

Types by slice: E5-a `a99 a59 a45 a46 a140 a141 a22 a30 a31`; E5-b `a66 a41 a53 a88 a106 c45 c36 a126
a127 a123 a94 a110 c46`; E5-c `c7 a17 c40 c41 a51 c1 a97 a105 a63 a117 a119 a25 a26 a27 a28 c8 c9`;
E5-e `a20`; E7-b `a103 a132 a85 a86`; E4-g `a104 a124 a130 a80 c12 c13 c16`; VIT `a107 a108 a128 a32
a111 a70 a137`; CRQ `c10 c11 c15 c19 c25 c26 c28 c29 c30 c31 c34 c37 c38 c51 a38 a39 a89 a92 a57`;
SCN `a95 a90`. E5-b, E5-c and E4-g carry neighbours of the plan's lists (listed in the issue
sections); VIT, CRQ and SCN are the three groups the plan does not have. Every refused type is in
exactly one group (the report lists none outside).

Greedy order of the slices (placed sites and roots freed at each step, cumulative):

| Step | Slice | Frees sites | Frees roots | Cumulative sites | Cumulative roots |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | VIT | 836 | 13 | 836 | 13 |
| 2 | E5-a | 552 | 111 | 1388 | 124 |
| 3 | CRQ | 299 | 61 | 1687 | 185 |
| 4 | E5-b | 250 | 44 | 1937 | 229 |
| 5 | E5-c | 133 | 64 | 2070 | 293 |
| 6 | E4-g | 115 | 65 | 2185 | 358 |
| 7 | SCN | 111 | 14 | 2296 | 372 |
| 8 | E5-e | 61 | 14 | 2357 | 386 |
| 9 | E7-b | 24 | 17 | 2381 | 403 |

The nine slices free 403 of the 447 type-blocked scripts (2,381 of 2,529 sites); the other 44
scripts also have a non-type refusal (below). By type, the first 25 steps:

| Step | Type | Name | Frees sites | Frees roots | Cumulative sites |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | a59 | `change_art_num` | 129 | 30 | 129 |
| 2 | a32 | `get_armor` | 98 | 1 | 227 |
| 3 | a99 | `change_script` | 96 | 39 | 323 |
| 4 | c51 | `prowling` | 87 | 3 | 410 |
| 5 | a22 | `critter_disband` | 74 | 9 | 484 |
| 6 | a30 | `set_reaction` | 70 | 4 | 554 |
| 7 | a66 | `create_item` | 58 | 7 | 612 |
| 8 | c28 | `obj_can_see_obj` | 56 | 7 | 668 |
| 9 | a31 | `adjust_reaction` | 56 | 6 | 724 |
| 10 | a39 | `get_origin` | 43 | 2 | 767 |
| 11 | a20 | `loop_break` | 42 | 7 | 809 |
| 12 | a124 | `stop_script_eye_candy` | 38 | 18 | 847 |
| 13 | c1 | `daytime` | 38 | 8 | 885 |
| 14 | a117 | `grant_one_fate_point` | 36 | 13 | 921 |
| 15 | c37 | `obj_is_animal` | 31 | 1 | 952 |
| 16 | c10 | `obj_met_pc_before` | 27 | 18 | 979 |
| 17 | a45 | `set_day_standpoint` | 22 | 8 | 1001 |
| 18 | a46 | `set_night_standpoint` | 106 | 17 | 1107 |
| 19 | a95 | `get_location` | 71 | 2 | 1178 |
| 20 | a90 | `get_scroll_distance` | 19 | 10 | 1197 |
| 21 | a108 | `get_fatigue_points` | 25 | 9 | 1222 |
| 22 | a80 | `get_magictech_adjustment` | 19 | 17 | 1241 |
| 23 | c26 | `obj_is_in_dialog` | 18 | 9 | 1259 |
| 24 | a85 | `queue_newspaper` | 13 | 6 | 1272 |
| 25 | c15 | `obj_is_invisible` | 11 | 5 | 1283 |

## Refusals that are not types

Scripts the production admission still refuses once every condition and action type is open
(roots and the placed sites of those roots; a root can have several):

| Refusal | Roots | Sites |
| --- | ---: | ---: |
| computed goto | 13 | 80 |
| goto beyond the end | 2 | 2 |
| object operand kind 13 (every one in group) | 12 | 62 |
| object operand kind 14 (anyone in group) | 4 | 10 |
| object operand kind 2 (every follower) | 9 | 22 |
| object operand kind 3 (any follower) | 1 | 1 |
| object operand kind 16 (any scenery in vicinity) | 2 | 22 |
| object operand kind 17 (every container in vicinity) | 8 | 10 |
| object operand kind 19 (every portal in vicinity) | 8 | 56 |
| object operand kind 22 (every item in vicinity) | 1 | 0 |

S33 admits the object kinds 0, 1, 8 to 12, 15 and 21 (triggerer, attachee, the vicinity critters,
looped object, local object, extra object, scenery in vicinity, player). These refusals are *not*
in the slice tables; they would need their own work item (an object-list host for the group,
follower, container, portal and item kinds).

## The largest blocked scripts

Fifteen blocked scripts with the most placed sites:

| Script | Placed sites | Maps | Holder points | Object type | Refused types |
| ---: | ---: | ---: | --- | --- | --- |
| 30282 | 729 | 27 | 1 | 9 | a107 `get_hit_points`, a108 `get_fatigue_points`, a128 `heal_poison` |
| 1474 | 98 | 1 | tile or sector | tile or sector | a32 `get_armor` |
| 1628 | 85 | 1 | tile or sector | tile or sector | c51 `prowling` |
| 2080 | 70 | 1 | tile or sector | tile or sector | a22 `critter_disband`, a45 `set_day_standpoint`, a46 `set_night_standpoint`, a95 `get_location`, a99 `change_script` |
| 1823 | 67 | 3 | 9 | 16 | a30 `set_reaction` |
| 30284 | 53 | 2 | 12 | 16 | a66 `create_item` |
| 2866 | 48 | 2 | 12 | 16 | a31 `adjust_reaction` |
| 1648 | 45 | 1 | tile or sector | tile or sector | a22 `critter_disband` |
| 2560 | 42 | 1 | 2, 30 | 3 | c36 `obj_is_open`, a88 `toggle_open_closed` |
| 1372 | 39 | 1 | tile or sector | tile or sector | c46 `monstergen_disabled`, a22 `critter_disband`, a110 `toggle_monster_generator` |
| 2725 | 32 | 1 | tile or sector | tile or sector | c45 `sector_is_blocked`, a106 `toggle_sector_blocked` |
| 2726 | 31 | 1 | tile or sector | tile or sector | c45 `sector_is_blocked`, a106 `toggle_sector_blocked` |
| 28007 | 31 | 5 | 7 | 5 | c37 `obj_is_animal` |
| 1990 | 28 | 3 | 1 | 3 | other: ComputedGoto |
| 30009 | 25 | 4 | 9 | 16 | a22 `critter_disband` |

Object type 9 is food, 16 an NPC and 3 a scenery object. Script 30282 is the single largest type blocker in the module: one food use script
on 729 food items on 27 maps (514 on the main map) that reads hit points and fatigue and cures poison.
The four tile or sector scripts with 45 to 98 sites each (1474, 1628, 2080, 1648) are on the main
map.

## By attachment point

Placed object, tile and sector sites by attachment point (negative points, the tile and sector
scripts, are not here) and how many are blocked:

| Point | Placed sites | Blocked sites | Most frequent refused types (blocked sites) |
| ---: | ---: | ---: | --- |
| 0 | 281 | 1 | a97 (1) |
| 1 | 1262 | 820 | a107 (729), a108 (729), a128 (729), a59 (25) |
| 2 | 47 | 20 | a88 (17), c36 (16), a123 (2), a104 (1) |
| 3 | 1 | 1 | a88 (1), c36 (1) |
| 7 | 77 | 68 | c37 (31), a80 (14), c38 (3), a137 (1) |
| 8 | 4 | 2 | a20 (1), a80 (1) |
| 9 | 820 | 253 | a30 (75), c15 (58), c13 (53), c11 (52) |
| 10 | 112 | 42 | a99 (26), a39 (19), c26 (10), c1 (8) |
| 11 | 48 | 0 |  |
| 12 | 595 | 166 | a31 (58), a66 (54), a117 (29), a99 (26) |
| 13 | 20 | 12 | a99 (10), a137 (2), a110 (1), a22 (1) |
| 14 | 1 | 0 |  |
| 15 | 3 | 1 | a99 (1) |
| 18 | 15 | 2 | a119 (1), a99 (1) |
| 19 | 422 | 183 | a90 (47), a45 (45), a46 (32), c26 (26) |
| 20 | 1 | 1 | c19 (1) |
| 21 | 74 | 24 | a127 (9), a59 (5), a38 (3), a39 (3) |
| 22 | 113 | 52 | a39 (24), c28 (23), a89 (4), c15 (2) |
| 23 | 19 | 15 |  |
| 24 | 309 | 10 | a108 (7), a25 (2), a107 (1), c31 (1) |
| 25 | 299 | 3 | a26 (2), a80 (1) |
| 26 | 60 | 1 | a70 (1) |
| 27 | 9 | 4 | a22 (3), a123 (1), a99 (1) |
| 28 | 6 | 0 |  |
| 30 | 74 | 31 | a88 (27), c36 (26), a104 (3), a123 (2) |
| 32 | 16 | 13 | a127 (12), a25 (11), a26 (11), c8 (11) |
| 33 | 10 | 0 |  |
| 34 | 14 | 10 | a80 (6), a108 (1), c31 (1) |
| 35 | 2 | 1 | a80 (1) |

Points not listed carry no placed script. The heartbeat (19), use (1), dialog (9) and dying (12)
points hold most blocked sites.

## By map

The 15 maps with the most blocked sites of 81 maps (24 maps have no blocked site):

| Map | Placed sites | Blocked sites | Distinct refused types |
| --- | ---: | ---: | ---: |
| arcanum1-024-fixed | 5656 | 1965 | 76 |
| qintarra | 171 | 129 | 15 |
| k'na tha | 162 | 110 | 6 |
| t'sen-ang | 95 | 51 | 12 |
| void | 137 | 49 | 3 |
| roseborough inn2 | 61 | 37 | 9 |
| tulla interior | 107 | 26 | 4 |
| vendigroth underground | 48 | 25 | 9 |
| cave of the bangellian scourge | 40 | 22 | 6 |
| temple of the derian ka | 38 | 20 | 8 |
| iron clan hq | 27 | 17 | 2 |
| pschuylers lev 2-fixed | 25 | 17 | 4 |
| caladon-panarrii temple | 95 | 16 | 7 |
| bates mansion lev 2 | 32 | 13 | 10 |
| caladon high security prison | 25 | 11 | 3 |

## By quest

Quests named by two or more blocked scripts (numbers as the scripts name them):

| Quest | Blocked roots | Refused types touching it |
| ---: | ---: | --- |
| 1039 | 6 | c28, a85, c1, c10, c15 |
| 1100 | 4 | a51, a85, a30, a31 |
| 1009 | 3 | c7 |
| 1041 | 3 | a85, a97, a104 |
| 1013 | 2 | c7, a117 |
| 1025 | 2 | c10, a99 |
| 1040 | 2 | c7, c10, c12, c13, c15, c16 |
| 1042 | 2 | c10, a31, a99 |
| 1062 | 2 | c10, c34 |
| 1081 | 2 | c1, a99 |
| 1095 | 2 | c10, a30 |
| 1103 | 2 | a30, a127 |

## What it means for the issues

The census is a data answer; each slice keeps its own contract and checks.

- **E5-b, world-object commands (#1164).** *Built 2026-11-12 as set S37 (ARC-SCRIPT-1164, [ledger WO1–WO13](ledger/scripts.md#world-object-commands-arc-script-1164-e5-b)); the hosts apply three conditions and six actions; ARC-SCRIPT-1279 (2026-11-12) gave transfer item, set written start, destroy item named and toggle item inventory display their host (the session's named-item move and destroy and its item fields; ledger WO14–WO18).* Census at the survey: 71 blocked scripts, 208 sites alone; installed now: 117 scripts use the 13 commands, S37 admits 84 whole.
- **E5-a, NPC state actions (#1163).** *Built 2026-11-12 as set S36 (ARC-SCRIPT-1163, [ledger NS1–NS8](ledger/scripts.md#npc-state-commands-arc-script-1163-e5-a)); change art number is left to a slice with a per-object art overlay.* Blocked roots 166, sites 794, maps 24; 110 roots (550
  sites) are freed by the slice alone. The largest of the plan's slices, and the most useful: it
  contains the three most frequent types of the module's refusals (change script 52 roots, change art
  41, day and night standpoints 44 and 36). *Follow* (action 21) and *attack* (action 36) are already in S33, so the
  issue's goal text loses them; `critter_disband` (a22, 15 roots, 220 sites on 7 maps, mostly
  tile and sector scripts), `set_reaction` (a30) and `adjust_reaction` (a31) stay, with the
  two extended standpoint actions 140 and 141 (1 root each).
- **E5-b, world-object actions (#1164).** 71 roots, 320 sites, 15 maps; 40 roots (208 sites)
  alone. `create_item` (a66, 12 roots, 54 of its 62 sites are creature dying scripts) and
  `toggle_sector_blocked` with `sector_is_blocked` (a106 and c45: two scripts on 63 sites of one
  main-map area) lead; `set_lock_state` (a53, 10 roots, 11 sites), `toggle_open_closed` with `obj_is_open`
  (a88 and c36, 11 and 9 roots, 42 sites of one k'na tha script) follow. **Transfer item (a41)**
  is one script on one site and **transform** is admitted already (S33), so both leave the issue's
  headline list; `destroy_item_named` (a126, 2 roots, 4 sites), the monster generator pair (a110
  and c46, 65 sites, 2 maps), `toggle_item_inventory_display` (a127, 16 roots, 21 sites), touch art
  (a123) and set written start (a94) are the neighbours the data adds.
- **E5-c, story and time (#1165; done, [SQ1–SQ14](ledger/scripts.md#story-and-time-commands-arc-script-1165-e5-c)).** 80 roots, 156 sites, 11 maps; 39 roots (98 sites) alone, the
  smallest of the three: `grant_one_fate_point` (a117, 14 roots, mostly dying scripts), blessing and
  curse (a25 to a28 and c8, c9: 8 to 14 roots each), `daytime` (c1, 19 roots) and the three clock actions
  (a97 and a105 are live, a96 and a98 only on unreached lines) are the bulk; global quest state (c7, 6
  roots, all on the main map) and give quest XP (a63, 1 root) are rare. Rumors are 8, 2 and 3
  roots (c40, c41, a51). Quests 1009, 1013 and 1040 are the ones that touch c7.
- **E5-e, loops and calls (#1166; done, set S40, [PG15](ledger/scripts.md)).** `loop_break` (a20): 17 roots, 68 sites (65 on the main map,
  24 at the heartbeat), 7 roots (42 sites) alone. `call script at` (a55) occurs on one line no run
  reaches and is not blocked by anything: the slice is `loop break` alone and is an S slice.
- **E7-b, media actions.** Newspapers (a85, a86 queue and float headline: 8 and 1 roots) and
  fade (a103, 8 roots) and end-game slides (a132, 3 roots, 1 site) are live in 17 roots and 24
  sites; the larger media need is `queue_slide` (a131: 54 clauses, 13 scripts), which only the
  engine-chosen end-game scripts reach.
- **E4-g, spell hooks (#1168), and what it needs.** The four conditions of the plan (14, 35, 48,
  49) and give spell mastery are admitted already (S31); 14 and 35 occur live (22 and 6 clauses) and
  are in S31, 48 and 49 occur only on lines no run reaches. No type of E5-a or E5-b is among the
  spell conditions and actions, so those slices do not gate the spell hooks. The spell *effect* and
  state types the census adds as the E4-g slice (play spell eye candy a104, stop script eye candy
  a124, stop spell a130 (done since ARC-SCRIPT-1294, set S42), get magictech adjustment a80, polymorphed, shrunk and mirror image c12,
  c13, c16) block 78 roots (145 sites, 18 maps); 39 roots (62 sites) alone. Blessing and curse (a25 to a28, c8, c9) are in E5-c, which the
  plan already made the prerequisite of the spell hooks.
- **MA-10, the raise hook (#1167).** The resurrect point (18) carries 15 placed sites with 13
  scripts. 13 of the 15 are admitted whole by S33; the 2 blocked sites are `set_pc_quest_unbotched` (a119, E5-c)
  and `change_script` (a99, E5-a). The point is not gated by E5-b and the hook can be built for the
  admitted 13 first.
- **Three groups the plan does not have.** *VIT* (hit points, fatigue, poison, armor, stat adjust):
  30 roots, 876 sites, 32 maps, 836 sites alone; script 30282 is 729 of them. *CRQ* (critter state
  and social queries: met before, bad associates, invisible, in dialog, in combat, can see, prowling,
  undead, animal and the rest): 105 roots, 394 sites, 23 maps; 267 sites alone. *SCN* (get
  location, get scroll distance): 17 roots, 122 sites, 2 maps; only 6 sites are freed by it alone
  (get location shares script 2080 with E5-a). The plan has no slice for these; the
  largest is bigger than any planned slice, so a work item for VIT and one for CRQ are
  recommended before the planned slices that free less.

Suggested order by the data: VIT, E5-a, CRQ, E5-b, E5-c, E4-g effects, SCN, E5-e, E7-b (SCN's
get location shares script 2080 with E5-a, so it can ride with E5-a). The planned slices E5-a, E5-b, E5-c and E5-e have no hard
dependency on each other and on the census; all four can start.

## Limits

- **Live is a model.** The reach follows literal calls and the line after a dialog action; the 114
  reached call-ex sites run an object's own slot (not followed), 22 computed or out-of-script jumps
  stop the walk (13 computed gotos, 7 gotos to the end and 2 beyond it), and engine-chosen scripts
  (end-game slides, the mind-control dialog script) are not roots ([ARC-RES-045](scr-census.md#reproduction)).
  The counts are an approximation in both directions; objects created at run time are not sites.
- **Sites are not play time.** A site is a placed holder, not a use: a food script on 729 foods is
  one blocked script, but how often a player triggers it is not measured. Heartbeat sites run on
  the heartbeat, dialog and use sites when the player acts.
- **Hosts admit subsets.** The ranking uses S33; a host with a narrower set (the dialog, use,
  heartbeat, dying and item hosts each have one) refuses more. `--set` re-runs it for an
  earlier set; on S30, 487 roots (2,783 sites) were blocked by type, so S31 to S33 freed 40 roots.
- **Sole blocker is per type.** A root with two refused types needs both; the slice table's
  "freed alone" is the same idea per slice. The greedy order is not an optimum.
- **A refusal behind a refusal is hidden.** The production `AdmitFrom` reports the lowest refused
  line, so a non-type refusal behind a type refusal is found by the run against the type-open set;
  44 roots hold both a refused type and a non-type refusal and never free by types alone. The
  by-point and by-map tables count a script as blocked for either kind, so they exceed the 2,529
  type-blocked sites by the 16 non-type-only scripts' sites (script 1990 is one).
- **Quests are partial.** Only literal quest numbers count; a quest the script computes is missing.
  Quest and map names are not published beyond the map folder names.

## After the vitals commands (ARC-SCRIPT-1481, 2026-11-13)

The probe takes `--set S38` and `--set S43` besides S30 to S33. Under S38 (the widest set before ARC-SCRIPT-1481) 1,558 attached scripts on 6,113 placed sites are admitted whole and script 30282 is refused for `get_hit_points`, `get_fatigue_points` and
`heal_poison`; under **S43** (S38 plus those three commands; S43 itself is S42 plus them) 1,575 scripts on 6,856 sites are admitted (+17 scripts, +743 sites; 30282 is 729 of them) and 30282 is no longer in the refused lists. The numbers above stay those of S33; a host's own set is
not counted here (the use set holds the three commands since this task, [ledger VT7](ledger/scripts.md#vitals-commands-and-the-food-use-script-arc-script-1481)).


## After the critter-state conditions (ARC-SCRIPT-1733, 2026-11-13)

The probe takes `--set S52` and `--set S53` too. Under **S52** (S51 plus obj is in combat and adjust stat; the widest set before this task) 1,611 attached scripts on 6,957 placed sites are admitted whole and 155 roots (586 sites) are refused for a type; under **S53** (S52 plus the nine
critter-state conditions of the [CRQ group](ledger/scripts.md#critter-state-and-social-conditions-arc-script-1733-group-crq-of-the-script-census): 10, 15, 25, 26, 28, 29, 30, 38, 51) 1,661 scripts on 7,169 sites are admitted (+50 scripts, +212 sites) and 101 roots (368 sites) are refused for a type. The
CRQ group, 97 roots on 359 sites blocked under S52 (66 roots on 293 sites freed by it alone), is down to 38 roots on 133 sites under S53 on 6 maps: what stays in it is obj has bad associates (11, 22 roots, all of them also blocked by a polymorph or shrink condition of E4-g), obj is within range (34, 7 roots
on 7 sites, each freed by it alone), obj is monster of type (19, one root), the getters get origin (39, 3 roots, 46 sites), get faction (89, 2 roots, 11 sites) and get social class (38), toggle invulnerability (57, 2 roots, 13 sites) and rename (92, one root), which the issues named in the pull request carry.
The census counts a script admitted when the named set admits it; a host runs it under its own set, so the numbers a host admits are the opt-in trigger census's (`ScriptTriggerCensusTests`): the dialog point 9 admits 124 of 180 rows (118 before), the heartbeat point 19 96 of 141 (95 before), the others
none more; most freed roots are still refused by a host for another type (for example action 22 on the dialog point) and run once that type is admitted.

## After the critter-state tail (ARC-SCRIPT-1771, 2026-11-13)

The probe takes `--set S54` too: **S54** is S53 plus obj has bad associates (11), obj is monster of type (19) and obj is within range (34; ledger [CS12–CS16](ledger/scripts.md#critter-state-and-social-conditions-arc-script-1733-group-crq-of-the-script-census)). Under S53 1,661 attached scripts on 7,169 placed
sites are admitted whole and 101 roots (368 sites) are refused for a type; under **S54** 1,669 scripts on 7,177 sites are admitted (+8 scripts, +8 sites: the seven roots of obj is within range, each freed by it alone, and the one root of obj is monster of type) and 93 roots (360 sites) are refused for a type.
The CRQ group is down from 38 roots on 133 sites (6 maps) under S53 to 8 roots on 73 sites on 2 maps under S54: obj has bad associates (22 roots) frees almost none by itself, as every root that holds it is also blocked by a polymorph or shrink condition of E4-g (12 and 13); what stays in the group are the
getters get origin (39), get faction (89) and get social class (38) and the actions toggle invulnerability (57) and rename (92).
The opt-in trigger census (`ScriptTriggerCensusTests`) on the owner's installation: the heartbeat point 19 admits 97 of 141 rows (96 before), every other point as before (the dialog point 9 124 of 180).

