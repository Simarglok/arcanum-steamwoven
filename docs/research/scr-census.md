# SCR opcode and attachment census

Status: **Research**, work item ARC-RES-045 (2026-10-16), slice **S0** of the
[SCR event dispatcher research](scr-event-dispatcher.md#10-proposed-implementation-slices)
(ARC-RES-042). It counts which attachment points carry scripts in the installed
module and which condition, action, value and focus types its scripts use, over
every line and over the lines a run can reach, and picks the first admitted set
for slice S1. It adds one read-only probe mode and this note. It changes no
production API, viewer, session, launcher or other component; the probe's
attachment walk of [ARC-RES-037](virgil-script-attachments.md) moved into a
shared function without changing that report (byte-identical before and after).

Requirements: [FR-SCRIPT-001](../requirements/story-social-ai.md#fr-script-001)
(which events occur) and [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002)
(which opcodes occur, the unknown-opcode policy). Answers unknown **U2** of
ARC-RES-042.

## Evidence discipline

- **Data (D)**: the installed 1.0.7.4 `Arcanum` module read through the
  [content stack](content-precedence.md), so every script, map, sector, mobile,
  aggregate, terrain template and prototype is the winner of its path. The probe
  prints numbers only (no names, no text, no payload); the full report stays in
  the ignored `research-private/`, and only the aggregates below are published.
- **Source (S)**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a [verification ledger](original-verification-ledger.md)
  row is cited. It covers the type catalogue (the condition and action type
  enumerations), the clause layout (the clause record) and how each operand
  is read (script 0x44BA70, 0x44BB50).
- **Operand signatures** (which operand slots of a type take a number, which an
  object) are format facts. The probe's table was taken from the order of number
  and object placeholders in the ScriptEd 1.50 editor data (`data/condition.mes`,
  `data/action.mes` of [TimoP80/ScriptEd_v1_50](https://github.com/TimoP80/ScriptEd_v1_50)
  at `da7e6d2`, GPL-2.0, read from the local toolkit archive; no text was taken).
  It agrees with how the interpreter reads its operands (as read earlier) for
  194 of the 195 types. The exception is action 86 (float newspaper headline):
  the interpreter reads its object from slot 1, one past the editor's one-slot
  signature (script 0x447220; the original does the same, ledger row X9);
  set reaction (30) as read takes slot 2's kind with slot 1's value
  (script 0x447220).
  The census checks the table against the data: no number slot holds a kind
  outside the 7 value types and no object slot a kind outside the 24 focus types.
  Taking factual operand order from GPL editor data is recorded here for the
  owner to confirm.
- **Project policy (P)**: proposals for S1, open until S1 fixes them.
- Original-executable parity is **NOT_VERIFIED**.

## Reproduction

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-census-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-census \
  --game-root "$GAME" > "$PRIVATE/census.json"
```

The run takes about two seconds and is deterministic: the report on the owner's
installation has SHA-256 `3ed1d7f9059c6b8a66fcdc5d31414b5d4f31da158d204e9c8d83712b9ed67572`
(two runs, same digest). It scanned 81 maps, 1,680 sector files, 12,723 mobile
files, 81 aggregates, 413 terrain template sectors and 1,423 prototypes, with no
refusal and no unresolved prototype.

**Live** below means reached by a run: from line 0 of every interpreted
attachment (all object slots and the prototype default slots objects inherit,
except `SAP_DIALOG_OVERRIDE`, which names a DLG, plus tile and sector scripts;
trap-flagged scripts are run by trap code, not interpreted), then from the literal entry line of every reached call, call-in,
call-at, change-this-script and change-script, and from the line after every
reached dialog action (a DLG response of 0). Not modelled: DLG responses of −N
(resume at a line the DLG names), the 114 reached call-ex sites (they run an
object's own slot), 22 steps the walk cannot follow (13 computed gotos, 7 gotos
to the end and 2 beyond it), and engine-chosen scripts (end-game slides 997–999,
the mind-control dialog script 2188). The model also over-counts: 66 scripts
occur only as prototype defaults, which no placed object uses (only objects created at run time could), and both
branches of every conditional line count as reachable. Live counts are therefore
an approximation in both directions; "all lines" is an upper bound.

## Answer

- **Scripts (D).** 2,399 script files, one name per number, all framed: 23,872
  lines, at most 221 in one script, 32 empty. Seven are trap-flagged
  (file flag bits 0 or 1), all on use slots (one of them also on a sector
  object's dialog slot). File flag bits in use: 0 (6
  scripts), 1 (1), 2 auto-removing (7), 3 death speech (31), 4 surrender speech
  (3), 8 teleport trigger (26).
- **Every opcode is known (D).** No condition type outside 0..52 and no action
  type outside 0..141 occurs anywhere; no number slot holds a value kind above 6
  and no object slot a focus kind above 23. A strict refusal of unknown types
  (ARC-RES-042 section 7) refuses no installed script.
- **Unused operand slots carry editor bytes (D).** 22,400 of 23,872 condition
  clauses and 29,297 of 29,395 action clauses hold a nonzero kind or value in a
  slot their type never reads (92,452 and 97,349 nonzero kind bytes). As read,
  the interpreter never reads those slots (apart from action 86 above, one
  clause in the data), so admission must ignore them; a
  "slots past the signature are zero" rule would refuse almost every script (P1).
- **Events (D).** 30 of the 36 attachment points carry a script somewhere; 4
  (`GET`), 5 (`DROP`), 6 (`THROW`), 16 (`END_COMBAT`), 17 (`BUY_OBJECT`) and 29
  (`LEADER_SLEEPING`) carry none. The most used are use (3,352 object slots, 389
  scripts), dialog (821, 416), heartbeat (422, 206), dying (595, 201) and the
  wield pair (309/299). Tile scripts: 3,012 tiles with 171 scripts; sector
  scripts: 20 map sectors and 20 terrain template sectors with 5 scripts.
- **Reach (D).** 1,838 script numbers are attached (20 of them dialog-override
  DLG numbers, 7 trap scripts, 2 with no file); 1,808 interpreted roots run, and
  calls add 132, so the live set is 1,940 scripts, 13,358 of the 23,872 lines and
  2,957 entries (1,019 of them not line 0). One literal call names a script with
  no file. 459 decoded scripts are never reached by this model (the 32 empty and
  the 7 trap scripts among them).
- **Opcode use (D).** 49 of 53 condition types and 136 of 142 action types occur.
  Unused conditions: 24 `obj_can_open_container`, 32 `obj_is_at_location`, 42
  `obj_is_busted`, 44 `can_open_portal`. Unused actions: 48 `cast_spell`, 67
  `action_wait_for_leader`, 116 `stop_spell_eye_candy`, 121/122
  `action_cast_(free_)unresistable_spell`, 129 `start_schematic_ui`. Value kinds 5
  (PC variable) and 6 (PC flag) never occur; focus kinds 4, 5, 7 (multiplayer
  party and team), 18 (any container), 20 (any portal) and 23 (any item) never
  occur.
- **Indices (D).** Every literal local-flag index is 0..31, every global-flag
  index 0..3199, every counter index 0..3, every local-variable and local-object
  index 0..9 and every global-variable index 0..1999. Five flag operations take a
  computed index (4 live). No arithmetic or get-story-state action writes into a
  number literal.
- **Jumps (D).** 5,548 gotos: 5,497 literal and inside the script, 9 to exactly
  the line count (the end of the script, 7 live), 2 beyond it (both live), 40
  computed (13 live); none uses the control codes −1..−4 (anomaly X6 of
  ARC-RES-042 never occurs in the data).
- **S1 first admitted set (P).** Conditions true, local flag, `==`, `<=`, global
  flag; actions do nothing, the two returns, goto, set and clear local flag, the
  five arithmetic actions, get and set story state, set and clear global flag;
  value kinds counter, local variable, number and global flag. Checked on the
  live lines it admits 96 of the 1,940 live scripts (11 of the 176 placed tile
  and sector scripts); checked on every line, as S1 will (rule P7), it admits
  144 of the 2,399 scripts (32 of them empty), 93 of them live.
  The first live refusal elsewhere is teleport (249 scripts), dialog (213), PC
  quest state (107), get object type (97), loop (85), "is named" (80) and print
  line (72). The census's admission omits rules P4 and P5 (no installed script
  breaks them, so the counts are the same). It
  is a correctness core, not a coverage target: each later slice widens it.

## Attachment points (D)

Object slots count sector objects and editor mobiles once each. The map
aggregates are the normal-game form of the same mobiles
([ARC-RES-036](data-census.md)): for every point their scripts equal the
mobiles' scripts exactly, so they are not added. Prototype defaults are the slots
those objects inherit and are listed apart; the "Scripts" and "S1" columns
include prototype-default scripts (so a point can have more scripts than slots).
"S1" is how many of the point's scripts the S1 candidate set admits on their
live lines.

| SAP | Point | Object slots | Sector objects | Mobiles | Prototype defaults | Scripts | S1 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | examine | 281 | 211 | 70 | 4 | 78 | 5 |
| 1 | use | 3,352 | 2,478 | 874 | 30 | 389 | 17 |
| 2 | destroy | 47 | 24 | 23 | 0 | 22 | 4 |
| 3 | unlock | 1 | 1 | 0 | 0 | 1 | 0 |
| 7 | hit | 77 | 0 | 77 | 10 | 28 | 0 |
| 8 | miss | 4 | 0 | 4 | 7 | 8 | 0 |
| 9 | dialog | 821 | 1 | 820 | 18 | 416 | 1 |
| 10 | first heartbeat | 112 | 1 | 111 | 2 | 31 | 0 |
| 11 | catching thief (PC) | 48 | 0 | 48 | 0 | 7 | 1 |
| 12 | dying | 595 | 0 | 595 | 14 | 201 | 28 |
| 13 | enter combat | 20 | 0 | 20 | 3 | 21 | 3 |
| 14 | exit combat | 1 | 0 | 1 | 1 | 2 | 0 |
| 15 | start combat | 3 | 0 | 3 | 0 | 3 | 1 |
| 18 | resurrect | 15 | 0 | 15 | 0 | 13 | 4 |
| 19 | heartbeat | 422 | 2 | 420 | 20 | 206 | 1 |
| 20 | leader killing | 1 | 0 | 1 | 0 | 1 | 0 |
| 21 | insert item | 74 | 0 | 74 | 12 | 41 | 7 |
| 22 | will KOS | 113 | 0 | 113 | 1 | 19 | 3 |
| 23 | taking damage | 19 | 1 | 18 | 3 | 9 | 1 |
| 24 | wield on | 309 | 0 | 309 | 61 | 56 | 0 |
| 25 | wield off | 299 | 0 | 299 | 59 | 52 | 1 |
| 26 | critter hits | 60 | 1 | 59 | 9 | 11 | 0 |
| 27 | new sector | 9 | 0 | 9 | 0 | 9 | 0 |
| 28 | remove item | 6 | 0 | 6 | 8 | 3 | 0 |
| 30 | bust | 74 | 70 | 4 | 0 | 33 | 5 |
| 31 | dialog override (names a DLG) | 20 | 0 | 20 | 0 | 20 | — |
| 32 | transfer | 16 | 0 | 16 | 0 | 14 | 0 |
| 33 | caught thief | 10 | 0 | 10 | 0 | 3 | 0 |
| 34 | critical hit | 14 | 0 | 14 | 0 | 11 | 3 |
| 35 | critical miss | 2 | 0 | 2 | 0 | 2 | 0 |

**Item points (ARC-SCRIPT-041).** The report's `item_points` repeats these counts for the eight item events (get, drop,
buy, insert item, wield on and off, remove item, transfer) with the object types that carry them and, per script, the first
clause a run from line 0 reaches outside the session's item set (`SessionItemScripts.Admission`, copied into the probe). On the
owner's installation it admits 7 insert-item scripts, 1 wield-off script (30236), 1 transfer script (2995, on a container) and
no wield-on or remove-item script; the wield-on scripts stop first at `items are being rewielded` (50, 25 scripts) and
magic-tech adjustment (91, 17), the wield-off scripts at call-ex (81, 43)
([item events](../world/inventory-runtime.md#item-events-arc-script-041)).

**Wield pairs (ARC-SCRIPT-042).** The item set copy now holds the item-effect commands (47, 50, 77, 78, 81, 91, 120) and play
sound (82). Each item point also reports `reached_scripts` and, by type, how many of the attached or called scripts a run reaches a
condition (`live_conditions`) or action (`live_actions`) in; the report's `wield_pairs` checks each object's wield-on and wield-off
scripts together (an aggregate repeats a mobile and is left out): wield on from line 0, wield off from line 0 and every call-ex it
reaches, which must name the attachee's wield-on slot (point 24) at a literal line, from that line. On the owner's installation the
item set admits 43 of the 56 wield-on and 50 of the 52 wield-off scripts and 42 of the 56 distinct wield pairs; the others stop first
at call script in (54, 8), get stat (33, 2), remove script call (125, 2), add blessing (25, 1) and get magic-tech adjustment (80, 1).
The wield-on scripts reach add effect (77) in 51 scripts, the magick-tech adjustment (91) in 51, play script eye candy (120) in 36,
identified (47) and items being rewielded (50) in 27 each; the wield-off scripts reach call-ex (81) in 44 and remove effect (78) in 6.
The report with these fields has SHA-256 `b2d2bc0b464bc7ebba4c9c0185203d75bb776449fdce87f19e1f044607bed33a` (two runs, same
digest; [wield events](../world/equipment-runtime.md#wield-events-arc-script-042)).

One dialog slot and one enter-combat slot name a script number that has no
file (the interpreter returns *skip default* for them, E4 of ARC-RES-042). One
of the seven trap-flagged use scripts is also on a sector object's dialog slot.

## Operand kinds (D)

Counted over number slots (value kinds) and object slots (focus kinds) of every
condition and action that the signature says the type reads.

| Value kind | All | Live |
| --- | ---: | ---: |
| 0 counter | 1,693 | 690 |
| 1 global variable | 870 | 330 |
| 2 local variable | 6,946 | 2,406 |
| 3 number | 23,074 | 13,373 |
| 4 global flag | 1,453 | 1,219 |
| 5 PC variable, 6 PC flag | 0 | 0 |

| Focus kind | All | Live |
| --- | ---: | ---: |
| 0 triggerer | 3,638 | 2,299 |
| 1 attachee | 3,159 | 2,089 |
| 21 player | 965 | 686 |
| 10 current looped object | 1,345 | 610 |
| 12 extra object | 310 | 197 |
| 8 everyone in vicinity | 374 | 146 |
| 15 every scenery in vicinity | 68 | 61 |
| 13 everyone in group | 30 | 11 |
| 19 every portal in vicinity | 21 | 10 |
| 2 every follower | 12 | 8 |
| 17 every container in vicinity | 15 | 8 |
| 14 anyone in group | 6 | 6 |
| 11 local object | 2 | 0 |
| 16 any scenery, 3 any follower, 9 anyone in vicinity, 22 every item | 2, 1, 1, 1 | 2, 1, 1, 1 |
| 6 everyone in team | 1 | 0 |
| 4, 5, 7, 18, 20, 23 | 0 | 0 |

## Condition types (D)

Names are the descriptive enumerator names of the earlier reading without their prefix; operands are the signature
(n number, o object).

| Type | Name | Operands | All lines | Live | Scripts (live) |
| ---: | --- | --- | ---: | ---: | ---: |
| 0 | `true` | — | 18,349 | 9,823 | 1,934 |
| 4 | `eq` | nn | 2,382 | 1,265 | 715 |
| 5 | `le` | nn | 563 | 384 | 220 |
| 20 | `obj_is_named` | on | 671 | 344 | 260 |
| 3 | `local_flag` | n | 434 | 342 | 211 |
| 6 | `pc_quest_state` | onn | 320 | 267 | 158 |
| 27 | `obj_is_switched_off` | o | 137 | 120 | 103 |
| 18 | `obj_following_pc` | oo | 127 | 113 | 113 |
| 21 | `obj_is_wielding_item` | on | 98 | 94 | 36 |
| 17 | `obj_has_item_named` | on | 88 | 82 | 31 |
| 22 | `obj_is_dead` | o | 90 | 77 | 67 |
| 15 | `obj_is_invisible` | o | 46 | 31 | 30 |
| 8 | `obj_has_bless` | on | 31 | 31 | 12 |
| 47 | `identified` | o | 71 | 30 | 27 |
| 33 | `obj_has_reputation` | on | 32 | 27 | 24 |
| 50 | `items_are_being_rewielded` | — | 28 | 27 | 27 |
| 13 | `obj_is_shrunk` | o | 26 | 26 | 25 |
| 10 | `obj_met_pc_before` | oo | 27 | 25 | 23 |
| 12 | `obj_is_polymorphed` | o | 22 | 22 | 22 |
| 14 | `obj_has_body_spell` | o | 22 | 22 | 22 |
| 11 | `obj_has_bad_associates` | o | 21 | 21 | 21 |
| 39 | `obj_jilted` | o | 21 | 21 | 21 |
| 1 | `daytime` | — | 22 | 19 | 19 |
| 28 | `obj_can_see_obj` | oo | 18 | 16 | 15 |
| 26 | `obj_is_in_dialog` | o | 21 | 15 | 11 |
| 43 | `global_flag` | n | 17 | 15 | 11 |
| 7 | `global_quest_state` | nn | 16 | 13 | 7 |
| 45 | `sector_is_blocked` | nn | 12 | 12 | 9 |
| 36 | `obj_is_open` | o | 17 | 9 | 8 |
| 31 | `obj_is_in_combat` | o | 14 | 9 | 9 |
| 40 | `rumor_known` | on | 9 | 8 | 8 |
| 9 | `obj_has_curse` | on | 8 | 8 | 8 |
| 34 | `obj_within_range` | onnn | 9 | 7 | 7 |
| 35 | `obj_is_influenced_by_spell` | on | 9 | 6 | 4 |
| 29 | `obj_can_hear_obj` | oo | 5 | 5 | 5 |
| 46 | `monstergen_disabled` | n | 5 | 5 | 5 |
| 38 | `obj_is_undead` | o | 10 | 4 | 4 |
| 25 | `obj_has_surrendered` | o | 3 | 3 | 3 |
| 51 | `prowling` | o | 3 | 3 | 3 |
| 41 | `rumor_quelled` | n | 3 | 2 | 2 |
| 16 | `obj_has_mirror_image` | o | 2 | 2 | 2 |
| 37 | `obj_is_animal` | o | 8 | 1 | 1 |
| 19 | `obj_is_monster_of_type` | on | 1 | 1 | 1 |
| 30 | `obj_is_invulnerable` | o | 1 | 1 | 1 |
| 48 | `knows_spell` | on | 32 | 0 | 0 |
| 49 | `mastered_spell_college` | on | 16 | 0 | 0 |
| 23 | `obj_has_max_followers` | o | 3 | 0 | 0 |
| 2 | `has_gold` | on | 1 | 0 | 0 |
| 52 | `waiting_for_leader` | o | 1 | 0 | 0 |

## Action types (D)

| Type | Name | Operands | All lines | Live | Scripts (live) |
| ---: | --- | --- | ---: | ---: | ---: |
| 3 | `goto` | n | 5,548 | 3,267 | 1,071 |
| 0 | `do_nothing` | — | 5,064 | 3,082 | 1,103 |
| 1 | `return_and_skip_default` | — | 3,455 | 2,016 | 994 |
| 2 | `return_and_run_default` | — | 2,074 | 1,549 | 1,095 |
| 4 | `dialog` | n | 1,710 | 939 | 419 |
| 10 | `assign_num` | nn | 2,319 | 871 | 471 |
| 23 | `float_line` | no | 612 | 480 | 258 |
| 44 | `teleport` | onnn | 428 | 321 | 296 |
| 19 | `loop_end` | — | 527 | 245 | 203 |
| 5 | `remove_this_script` | — | 281 | 244 | 224 |
| 18 | `loop_for` | o | 520 | 243 | 203 |
| 54 | `call_script_in` | nnoon | 304 | 209 | 125 |
| 56 | `toggle_state` | o | 232 | 203 | 183 |
| 16 | `set_pc_quest_state` | onn | 209 | 181 | 146 |
| 11 | `add` | nnn | 313 | 173 | 140 |
| 24 | `print_line` | nn | 189 | 162 | 116 |
| 91 | `get_magictech_adjustment_ex` | noon | 419 | 153 | 74 |
| 12 | `subtract` | nnn | 765 | 147 | 65 |
| 34 | `get_object_type` | on | 172 | 145 | 135 |
| 120 | `play_script_eye_candy` | no | 149 | 141 | 109 |
| 37 | `random` | nnn | 292 | 139 | 77 |
| 81 | `call_script_ex` | onno | 349 | 114 | 108 |
| 77 | `add_effect` | onn | 251 | 109 | 57 |
| 7 | `call_script` | nnoo | 169 | 108 | 87 |
| 15 | `assign_obj` | oo | 88 | 77 | 73 |
| 99 | `change_script` | onn | 140 | 70 | 64 |
| 6 | `change_this_script_to_script` | n | 154 | 62 | 61 |
| 82 | `play_sound` | n | 146 | 60 | 50 |
| 36 | `attack` | oo | 111 | 60 | 52 |
| 33 | `get_stat` | non | 93 | 60 | 51 |
| 59 | `change_art_num` | on | 63 | 48 | 43 |
| 45 | `set_day_standpoint` | onn | 96 | 47 | 44 |
| 68 | `destroy` | o | 62 | 47 | 45 |
| 72 | `add_reputation` | on | 53 | 47 | 31 |
| 61 | `cast_spell_on` | no | 81 | 40 | 29 |
| 127 | `toggle_item_inventory_display` | o | 40 | 40 | 16 |
| 46 | `set_night_standpoint` | onn | 86 | 39 | 36 |
| 8 | `set_local_flag` | n | 105 | 38 | 23 |
| 29 | `get_reaction` | oon | 46 | 38 | 36 |
| 60 | `damage` | onn | 73 | 37 | 29 |
| 42 | `get_story_state` | n | 39 | 36 | 35 |
| 71 | `distance_between` | oon | 43 | 35 | 34 |
| 106 | `toggle_sector_blocked` | nn | 34 | 34 | 10 |
| 52 | `create_object` | no | 32 | 29 | 12 |
| 26 | `remove_blessing` | no | 29 | 29 | 3 |
| 13 | `multiply` | nnn | 64 | 28 | 24 |
| 58 | `kill` | o | 35 | 27 | 26 |
| 80 | `get_magictech_adjustment` | nooon | 52 | 22 | 21 |
| 20 | `loop_break` | — | 32 | 22 | 18 |
| 124 | `stop_script_eye_candy` | no | 22 | 22 | 22 |
| 25 | `add_blessing` | no | 25 | 21 | 13 |
| 50 | `set_rumor` | no | 23 | 21 | 21 |
| 74 | `action_run_to` | onn | 38 | 19 | 19 |
| 40 | `transform_attachee_into_basic_prototype` | n | 21 | 19 | 19 |
| 49 | `mark_map_location` | no | 22 | 18 | 17 |
| 84 | `get_area` | on | 19 | 18 | 18 |
| 22 | `critter_disband` | o | 51 | 17 | 16 |
| 112 | `give_spell_mastery_in_college` | on | 32 | 16 | 1 |
| 104 | `play_spell_eye_candy` | no | 22 | 16 | 8 |
| 43 | `set_story_state` | n | 19 | 16 | 16 |
| 66 | `create_item` | no | 60 | 14 | 12 |
| 47 | `get_skill` | non | 33 | 14 | 9 |
| 9 | `clear_local_flag` | n | 31 | 14 | 8 |
| 31 | `adjust_reaction` | oon | 19 | 14 | 10 |
| 126 | `destroy_item_named` | no | 31 | 13 | 2 |
| 113 | `unfog_townmap` | n | 13 | 13 | 2 |
| 114 | `start_written_ui` | no | 13 | 13 | 13 |
| 88 | `toggle_open_closed` | o | 20 | 12 | 11 |
| 108 | `get_fatigue_points` | onn | 18 | 12 | 12 |
| 53 | `set_lock_state` | on | 15 | 11 | 11 |
| 138 | `apply_unresistable_damage` | onn | 13 | 11 | 7 |
| 117 | `grant_one_fate_point` | o | 11 | 11 | 10 |
| 78 | `remove_effect` | on | 250 | 10 | 10 |
| 21 | `critter_follow` | oo | 21 | 10 | 10 |
| 14 | `divide` | nnn | 14 | 10 | 9 |
| 85 | `queue_newspaper` | nn | 12 | 10 | 7 |
| 69 | `action_walk_to` | onn | 33 | 9 | 9 |
| 30 | `set_reaction` | oon | 13 | 9 | 9 |
| 27 | `add_curse` | no | 11 | 9 | 9 |
| 64 | `written_ui_start_book` | no | 10 | 9 | 9 |
| 28 | `remove_curse` | no | 8 | 8 | 8 |
| 123 | `touch_art` | n | 8 | 8 | 6 |
| 103 | `fade` | nnnn | 21 | 7 | 4 |
| 109 | `action_stop_attacking` | o | 7 | 7 | 6 |
| 136 | `cast_unresistable_spell` | no | 7 | 7 | 6 |
| 105 | `get_hours_since_startup` | n | 15 | 6 | 6 |
| 32 | `get_armor` | on | 6 | 6 | 6 |
| 110 | `toggle_monster_generator` | n | 6 | 6 | 6 |
| 97 | `get_current_hour` | n | 11 | 5 | 5 |
| 73 | `remove_reputation` | on | 6 | 5 | 5 |
| 41 | `transfer_item` | noo | 11 | 4 | 1 |
| 90 | `get_scroll_distance` | n | 7 | 4 | 4 |
| 76 | `heal_fatigue` | on | 6 | 4 | 4 |
| 93 | `action_become_prone` | o | 6 | 4 | 4 |
| 107 | `get_hit_points` | onn | 5 | 4 | 4 |
| 101 | `clear_global_flag` | n | 4 | 4 | 2 |
| 118 | `cast_free_spell` | ono | 4 | 4 | 4 |
| 132 | `end_game_and_play_slides` | — | 6 | 3 | 3 |
| 125 | `remove_script_call` | no | 5 | 3 | 3 |
| 65 | `written_ui_start_image` | no | 3 | 3 | 3 |
| 87 | `play_sound_scheme` | nn | 3 | 3 | 2 |
| 94 | `set_written_start` | on | 3 | 3 | 2 |
| 95 | `get_location` | onn | 3 | 3 | 3 |
| 100 | `set_global_flag` | n | 3 | 3 | 3 |
| 17 | `set_quest_global_state` | nn | 7 | 2 | 2 |
| 62 | `action_perform_animation` | on | 7 | 2 | 2 |
| 75 | `heal_hp` | on | 5 | 2 | 2 |
| 57 | `toggle_invulnerability` | o | 4 | 2 | 2 |
| 134 | `set_faction` | on | 3 | 2 | 2 |
| 39 | `get_origin` | on | 2 | 2 | 2 |
| 51 | `quell_rumor` | no | 2 | 2 | 2 |
| 83 | `play_sound_on` | no | 2 | 2 | 2 |
| 89 | `get_faction` | on | 2 | 2 | 2 |
| 137 | `adjust_stat` | non | 2 | 2 | 2 |
| 102 | `fade_and_teleport` | nnnonnn | 29 | 1 | 1 |
| 130 | `stop_spell` | no | 4 | 1 | 1 |
| 70 | `get_weapon_type` | on | 2 | 1 | 1 |
| 119 | `set_pc_quest_unbotched` | on | 2 | 1 | 1 |
| 38 | `get_social_class` | on | 1 | 1 | 1 |
| 63 | `give_quest_xp` | on | 1 | 1 | 1 |
| 86 | `float_newspaper_headline` | o | 1 | 1 | 1 |
| 92 | `rename` | on | 1 | 1 | 1 |
| 111 | `get_armor_coverage` | on | 1 | 1 | 1 |
| 115 | `action_try_to_steal_100_coins` | oo | 1 | 1 | 1 |
| 128 | `heal_poison` | on | 1 | 1 | 1 |
| 135 | `drain_charges` | no | 1 | 1 | 1 |
| 139 | `set_autolevel_scheme` | on | 1 | 1 | 1 |
| 140 | `set_day_standpoint_ex` | onnn | 1 | 1 | 1 |
| 141 | `set_night_standpoint_ex` | onnn | 1 | 1 | 1 |
| 131 | `queue_slide` | n | 54 | 0 | 0 |
| 35 | `adjust_gold` | on | 2 | 0 | 0 |
| 79 | `action_use_item` | ooonn | 2 | 0 | 0 |
| 96 | `get_day_since_startup` | n | 2 | 0 | 0 |
| 133 | `set_rotation` | on | 2 | 0 | 0 |
| 55 | `call_script_at` | nnoon | 1 | 0 | 0 |
| 98 | `get_current_minute` | n | 1 | 0 | 0 |

## Proposed S1 admission rules (P)

These follow from the counts above and fix the first admitted set of slice S1
(ARC-SCRIPT-007). Each rule refuses a whole script before any effect, with the
line, the component and the type number.

- **P1 Unread operand slots are ignored.** Admission checks the slots a type's
  signature reads and nothing else; the editor bytes in the other slots are kept
  but never interpreted.
- **P2 Unknown types are refused.** A condition type above 52 or an action type
  above 141 is a typed refusal (none occurs, so the rule costs no installed
  script). A known type outside the admitted set is refused the same way, with
  its own reason.
- **P3 Operand kinds.** A number slot admits only the admitted value kinds and an
  object slot only the admitted focus kinds; S1 admits no object operand at all.
  PC variables and PC flags (never used) stay refused until a slice needs them.
- **P4 Literal indices are checked statically.** Local flag 0..31, counter 0..3,
  local variable 0..9, global flag 0..3199 (global variable 0..1999 once
  admitted). A computed flag index (5 sites) is not refused statically; its
  bound is a run-time result of slice S2.
- **P5 Writes need a writable target.** An arithmetic or get-story-state action
  whose operand 0 is a number literal is refused (none occurs).
- **P6 Jumps.** A literal goto inside the script is admitted; a goto to exactly
  the line count is admitted as "end of script": *skip default* without
  auto-removal (as read, running past the last line also gives *skip default* but
  does clear an auto-removing script, the goto does not;
  script 0x4449B0;
  none of the 11 gotos to or past the end is in an auto-removing script); a negative literal, one beyond the end, and a
  computed goto are refused in S1 (2 and 13 live sites; the 13 computed ones are
  a later slice).
- **P7 What is checked.** Every line of the script, not only the lines the live
  model reaches: the model is a lower bound (DLG −N resumption, call-ex), and a
  whole-script check needs no entry-line knowledge. (ARC-SCRIPT-009 keeps this
  for `Admit` and adds region admission for the dispatcher: the lines a run from
  the event's line can reach, which is exact for the admitted set.)

## Unknowns

- **U1** Which lines DLG responses of −N resume is not modelled; the live counts
  are a lower bound.
- **U2** The 459 scripts this model never reaches may be reached through call-ex,
  DLG resumption or engine-chosen numbers; not checked.
- **U3** Whether the original interpreter reads the slots past a signature is
  **NOT_VERIFIED**; the earlier reading does not, except action 86 (one clause
  in the data; ledger row X9).

## Anomalies for S1 (S)

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019) supersede the earlier readings below where they differ.

Recorded, not adopted; the slice that implements the action decides (owner
decisions D2 and D8: fix confirmed slips; only a bug of the original game keeps its
behaviour behind a switch of the Original setting).

| ID | Source behaviour | Note |
| --- | --- | --- |
| X7 | Divide stores the **sum** of its operands when the divisor is not 0, and stores nothing when it is (script 0x447220). | A reconstruction slip: the original stores the signed quotient (ledger row X7); 14 divide clauses, 10 live. S1 admits the type; the interpreter slice (S2) chooses quotient or source sum. |
| X8 | A counter write shifts the value into its byte without masking it (script 0x44BB50), so a value above 255 or negative spills into the higher counters. | A reconstruction slip: the original masks counter writes to 8 bits (ledger row X8). S2 decides masking. |
| X9 | Float newspaper headline (86) reads its object from slot 1, outside the editor signature (script 0x447220). | Not admitted in S1; the one installed clause has kind 1 in slot 0 and 0 (triggerer) in slot 1. |
| X10 | A global-flag write shifts the value into the flag's bit without masking it (script 0x444D20), so an arithmetic or get-story-state result of 2 sets the neighbouring flag and a negative one every higher flag of the same 32-flag word. | Found in the ARC-SCRIPT-007 review; S1 admits global-flag write targets, S2 decides masking. |
