# Virgil's level-ups replayed, and the scripts that act on him

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-032 (2026-10-16). It answers the open
questions of [ARC-RES-031](virgil-auto-level.md#unknowns-u): **U1** what scheme
91 buys at each level, **U2** who runs script 2023 (scheme 101), **U3** what the
local variable in his enter-combat script holds, and **U5** what his scripts
reach through call edges. It also adds outside evidence for the level-50 case
(ARC-RES-031 A1). It is a read-only probe extension plus this note. It changes
no production API, viewer, conversation runtime, `Arcanum.Session` or launcher
file, and adds no level-up runtime. Requirements:
[FR-CHAR-021](../requirements/characters-skills.md#fr-char-021) and
[FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003).

Categories as in [ARC-RES-031](virgil-auto-level.md): **Source (S)** is
`alexbatalov/arcanum-ce` at
[`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
(Sustainable Use License 1.0; read for intent, nothing copied); **Data (D)** is
the authenticated installation (its `Version.txt` says 1.0.7.4) read by the
probe; **External (X)** is a public changelog or player report, the weakest
category, used only to confirm or contradict S and D; **Proposal (P)** and
**Unknown (U)** as before. No original text or private path is published:
numbers, booleans, engine tokens and digests only. Original executable parity
remains **NOT_VERIFIED**.

## Answer

- **U1 (S applied to D).** Replaying the purchase helpers over his T0 sheet,
  while he follows a PC from level 1 to 50, scheme 91 never completes: by level
  50 he has white necromancy 4, dodge, melee and heal 20, lock picking 12,
  dexterity 18, intelligence 18, willpower 15 and 187 hit points, and is still
  buying hit points towards 200. Scheme 70 never runs. About half of his
  level-ups buy a unit of a key stat rather than a skill, because each skill
  rank needs a higher key stat first. The replay matches an independent player report
  (X1): at level 10 his two points go into hit points, which rise from 55 to
  65, and nothing else changes on his sheet.
- **U3 (S applied to D).** Script 1997 runs whenever he enters combat. It reads
  the player's alignment and his own, and adds the difference to his own, so
  his alignment becomes the player's. It then picks his dialog script (1712,
  1842 or 1324) from global flags 2449 and 2444. The alignment gap of the
  follow check is therefore 0 after any combat.
- **U2 (S applied to D).** Script 2023 is a story event for Virgil himself: his
  name field is 6409, and the script acts only on a triggerer named 6409 when
  global flag 2593 is set and the story state is above 14. It sets flag 2449,
  sets his scheme to **101**, switches his dialog and first-heartbeat scripts,
  gives him new day and night standpoints and teleports him to map 5023.
  Scripts 2498 and 2648 set flag 2593. No Crash Site object attaches 2023 and
  no script calls it, so it is attached on another map (not traced). Scheme
  101 changes what he buys from then on (white necromancy 5, black necromancy
  2 and 5, constitution 20, dexterity 20), never his level.
- **U5 (D).** His six scripts reach 20 scripts through literal call edges, with
  15 call-ex edges unresolved and one target (1800) not installed. Only 1997
  has an adjust-stat action; none has a transform or set-scheme action.
- **Level 50 (X).** The 1.0.7.4 changelog says that a PC at level 50 keeps
  gaining XP and that followers then level up (X2). So in the installed
  version the ARC-RES-031 A1 path does end, and lagging followers catch up by
  one level per would-be PC level, still capped at 50. The gate conclusion of
  ARC-RES-031 is unchanged.

## Inputs and reproduction

The same probe run as [ARC-RES-031](virgil-auto-level.md#inputs-and-reproduction);
the output gains `replay`, `scripts.virgil_reach`, `scripts.set_scheme_scripts`,
`scripts.attached_adjust_stat_scripts`, `scripts.flag_writers` and
`scripts.set_scheme_rules`. `rules/spelllist.mes` (resource4 winner,
`b2cdb912…1fc7878d`) gives the spell willpower requirements; the DLG scan reads
709 installed DLG names, of which the project's DLG reader refuses 38 (their GF
actions are not counted).

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --virgil-auto-level-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --virgil-auto-level <same arguments as ARC-RES-031>
```

## 1. The purchase model (S)

The replay is a model of the CE helpers as read, run over explicit inputs. It
is research evidence, not a runtime.

| ID | Rule the replay applies | Source |
| --- | --- | --- |
| R1 | Effective stat = base, then effects (percent, multiply, divide, add, largest minimum, smallest maximum), clamped (primary stats 1..20 for a human). An NPC has no background term. | [stat.c 336–527][stat-level], [effect.c 741–850][fold] |
| R2 | A stat unit costs 1 (0 only to reach 1). After a purchase the loop compares the **effective** value with the target, so an effect that lowers the stat makes the helper set the same base again and pay for it. | [stat.c 151–172][stat-cost], [level.c 949–982][buy-stat] |
| R3 | A skill rank (4 levels) costs 1. The skill level is 4 × points, then effects, clamped 0..20, then capped by the key stat (3 up to 5, 7 at 6–8, 11 at 9–11, 15 at 12–14, 19 at 15–17, 20 from 18). A rank the cap refuses buys one base unit of the key stat instead. Key stats: dodge, melee and lock picking dexterity, heal intelligence. | [skill.c 37–62][key-stats], [skill.c 145–166][cap], [skill.c 506–545][basic-level], [skill.c 592–625][basic-set], [skill.c 1037–1140][tech], [level.c 989–1072][buy-skill] |
| R4 | A college rank costs 1. Rank *k* needs character level 1, 1, 5, 10, 15, intelligence 5, and the willpower named by the spell's `IQ:` value or by default 6, 9, 12, 15, 18. Too low a level skips the step; otherwise the helper buys intelligence (if short) or willpower. | [spell.c 31–37][min-level], [spell.c 465–526][spell-add], [magictech.c 1472–1490][min-stats], [magictech.c 468–474][default-wp], [magictech.c 1375–1415][spell-entries], [level.c 1079–1126][buy-spell] |
| R5 | A hit (fatigue) point costs 1 and adds 4: maximum hit points = willpower + 2 × (strength + level) + 4 + adjustment + 4 × points; fatigue = 4 × points + adjustment + 2 × (level + constitution) + willpower + 4; both then through effects. | [object.c 1632–1677][hp], [critter.c 502–524][fatigue], [level.c 1178–1212][buy-misc] |
| R6 | Per level-up: level + 1, +1 point (+2 at multiples of 5, unspent capped at 56), scheme rule from step 0, standard rule 70 if the scheme completes; running out of points stops a rule, other failures skip a step. | ARC-RES-031 L3–L7 |

The replay assumes he follows from T0 and the PC gains one level at a time. A
multi-level jump grants its points at once and could buy in a different
order (not replayed).

## 2. Inputs from his sheet (D)

| Input | Value |
| --- | --- |
| Effects | 82 (strength +1, intelligence −1), 64 (no change) |
| Effective T0 stats | strength 9, dexterity 8, constitution 8, intelligence 7 (base 8), perception 8, willpower 8 |
| Hit points | 1 point, adjustment 2: maximum 38 |
| Fatigue | 0 points, adjustment 0: maximum 30 |
| Skills | dodge, melee, heal 4 (one point each); lock picking 0; no training bits |
| Spells | white necromancy 1; no spell overrides the default willpower (white necromancy 6, 9, 12, 15, 18) |

## 3. The replay, winning copy of scheme 91 (S applied to D)

| Level | Bought | Rule stops at | Hit points |
| ---: | --- | --- | ---: |
| 2 | lock picking 4 | melee 8 | 40 |
| 3 | dexterity 9 | melee 8 | 42 |
| 4 | melee 8 | necro_good 2 | 44 |
| 5 | willpower 9, white necromancy 2 | heal 8 | 47 |
| 6–7 | intelligence base 9, 10 (effective 8, 9) | heal 8 | 49–51 |
| 8 | heal 8 | dodge 8 | 53 |
| 9 | dodge 8 | maxhps 62 | 55 |
| 10 | two hit points | melee 12 | 65 |
| 11–13 | dexterity 10, 11, 12 | melee 12 | 67–71 |
| 14 | melee 12 | necro_good 3 | 73 |
| 15–16 | willpower 11, 12 | necro_good 3 | 77–80 |
| 17 | white necromancy 3 | heal 12 | 82 |
| 18–20 | intelligence base 11, 12, 13 (one point wasted at 20, R2) | heal 12 | 84–88 |
| 21–23 | heal 12, lock picking 8, dodge 12 | melee 16 | 90–94 |
| 24–25 | dexterity 13, 15 | melee 16 | 96–98 |
| 26 | melee 16 | necro_good 4 | 100 |
| 27–29 | willpower 13, 14, 15 | necro_good 4 | 103–109 |
| 30 | white necromancy 4, intelligence base 14 | heal 16 | 111 |
| 31–32 | intelligence base 15, 16 | heal 16 | 113–115 |
| 33–34 | heal 16, dodge 16 | melee 20 | 117–119 |
| 35–36 | dexterity 17, 18 | melee 20 | 121–123 |
| 37 | melee 20 | heal 20 | 125 |
| 38–40 | intelligence base 17, 18, 19 (one point wasted at 40) | heal 20 | 127–131 |
| 41–43 | heal 20, lock picking 12, dodge 20 | maxhps 200 | 133–137 |
| 44–50 | hit points only | maxhps 200 | 143–187 |

Each level also adds 2 hit points and 2 fatigue through the level term (R5).
Two points are wasted in all by the intelligence effect (R2). Fatigue ends at
135.

The shadowed `arcanum3.dat` copy follows the same path to level 37, then
spends levels 38–40 on willpower 16–18 and white necromancy 5, and ends at
level 50 with white necromancy 5, lock picking 12, willpower 18 and 178 hit
points. So the step that `Arcanum4.dat` drops costs three of his level-ups.

## 4. Scripts that act on him (D, read with S)

Script lines below are read with the source's condition, action, focus and
value enums ([script.h 46–81][enums], [script.c 1511–1575][arith],
[script.c 1746–1757][get-stat], [script.c 2380–2394][change-script],
[script.c 904–925][named], [script.c 631–648][compare]). Unused operand slots
hold editor leftovers and are not read.

**1997, enter combat.** Lines 0–3: get the player's alignment, get his own,
subtract, add the difference to his own alignment (ARC-RES-031 found this
adjust-stat action). Lines 4–11: global flag 2449 set → dialog script 1712;
else flag 2444 set → 1842; else 1324.

**2023, scheme 101.** Line 0: needs flag 2593. Lines 1–2: story state above
14. Line 3: triggerer named 6409 (Virgil); otherwise line 12 checks name 6614,
sets flag 2477 and destroys that triggerer. Lines 4–11 for Virgil: flag 2449 =
1, scheme 101, dialog script 1712, first-heartbeat script 3092, flag 2456 = 1,
day and night standpoints, teleport to map 5023.

**Scheme 101** (14 steps, identical in both copies): necro_good 3, melee 12,
cn 15, necro_good 5, cn 18, necro_evil 2, melee 16, cn 20, melee 20,
maxhps 140, dx 20, necro_evil 5, maxfatigue 200, maxhps 250.

## 5. Outside sources (X)

| ID | Source | What it says | Agrees with |
| --- | --- | --- | --- |
| X1 | [GOG forum, 2014](https://www.gog.com/forum/arcanum_of_steamworks_and_magick_obscura/virgil_doesnt_improve_anything_when_he_gets_to_level_10) | At level 10 Virgil's sheet shows no change except hit points, 55 → 65; each level adds 2 hit points and 2 fatigue. | The replay at levels 9–10 and R5 exactly |
| X2 | [Arcanum 1.0.7.4 update notes](https://arcanumclub.org/sierra/support/arcanum_update1074.html) | XP continues at level 50; followers then level up; new script opcodes change auto-level schemes and standpoints on other maps; fixes for Virgil leaving and returning. | L7 at PC level 50 (contradicts the CE reading of A1), the set-scheme and standpoint actions of script 2023 |
| X3 | [Steam discussion](https://steamcommunity.com/app/500810/discussions/0/1746768126951068036) | The Unofficial Arcanum Patch lets the player set a follower to "no scheme"; otherwise followers spend their own points. | ARC-RES-031 L9 (no scheme control in the unpatched sheet) |

X1 and X2 are not original-executable traces; they only raise confidence in
the model where they agree.

## Anomalies (S)

- **A7** R2: with intelligence lowered by his class effect, raising it can pay
  twice for one base unit when he has two points (levels 20 and 40).

## Proposal for implementation (P)

1. A future follower level-up owner can take R1–R6 as its contract and this
   replay's level 2..50 table as a real-data regression for Virgil.
2. Keep the ARC-RES-031 gate conclusion; add the level-50 follower rule from
   X2 as the installed-version behaviour, behind an explicit policy flag,
   since it contradicts the CE reading.
3. Treat 1997's alignment copy as an input to the consent evaluator when
   combat exists: after any fight Virgil's alignment equals the PC's.

## Unknowns (U)

- **U6** Which map object attaches script 2023, and who runs scripts 2498 and
  2648 (flag 2593); next check: a script census of all map mobiles and
  prototypes.
- **U7** The 38 DLGs the DLG reader refuses were not searched for flag 2593.
- **U8** Multi-level jumps and level-ups while he waits off screen were not
  replayed.
- **U9** The 15 call-ex edges from his scripts are unresolved.

## Method

Source reading at the pinned commit, the read-only probe `--virgil-auto-level`
(self-test `--virgil-auto-level-self-test` now also covers the effect grammar
and fold, the formulas, each purchase helper including the R2 loop, the spell
gates, rule runs, the replay and the reach), and the outside sources above.

[stat-level]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L336-L527
[fold]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L741-L850
[stat-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L151-L172
[buy-stat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L949-L982
[key-stats]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L37-L62
[cap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L145-L166
[basic-level]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L506-L545
[basic-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L592-L625
[tech]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1037-L1140
[buy-skill]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L989-L1072
[min-level]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L31-L37
[spell-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L465-L526
[min-stats]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1472-L1490
[default-wp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L468-L474
[spell-entries]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1375-L1415
[buy-spell]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L1079-L1126
[hp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1632-L1677
[fatigue]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L502-L524
[buy-misc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L1178-L1212
[enums]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L46-L81
[arith]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1511-L1575
[get-stat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1746-L1757
[change-script]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2380-L2394
[named]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L904-L925
[compare]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L631-L648
