# Virgil's auto-level scheme 91 and the follow level gate

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-031 (2026-10-16). It answers open
question **U3** of the [follow consent research](virgil-follow-consent.md#unknowns-u)
(ARC-RES-026): how auto-level scheme 91 raises Virgil's level as the game goes
on, and whether the consent check's 30-level gate can ever refuse him. It is a
read-only probe plus this note. It changes no production API, viewer,
conversation runtime, `Arcanum.Session` or launcher file, and it adds no
level-up runtime, character-point purchase or party state. Requirements:
[FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and
[FR-CHAR-021](../requirements/characters-skills.md#fr-char-021).

Categories, as in [ARC-RES-026](virgil-follow-consent.md):

- **Source (S):** read from `alexbatalov/arcanum-ce` at
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License 1.0, not MIT). It is a reconstruction lineage, not
  original-executable parity; decompiler names are read for intent. No CE code
  or structure is copied.
- **Data (D):** the authenticated normal-game Crash Site aggregate, the six
  pinned archives and the installed content stack, read by the probe below. T0
  is the serialized state before any tick, script or player action.
- **Proposal (P):** a project recommendation, not accepted gameplay policy.
- **Unknown (U):** a named gap with its next check.

No original text or private path is published. The report holds numbers,
booleans, engine rule tokens (the vocabulary the source itself names, such as
`heal` or `maxhps`) and SHA-256 digests; MES text and scheme names stay in
memory. Original executable parity remains **NOT_VERIFIED**.

## Answer

- **The scheme never changes his level (S).** An auto-level scheme only spends
  character points that a level-up has already granted. It buys stats, skills,
  spell college levels, tech degrees and hit/fatigue points; nothing in its
  grammar names the level.
- **His level moves only with the PC's (S).** An NPC's level is raised in one
  place: when the PC levels up, each follower (and each follower waiting on
  screen near the PC) is moved by the same number of levels if he was at or
  below the PC's old level, or pulled up to the PC's new level at most if he
  was above it, never past 50. The NPC-minus-PC level gap therefore never grows
  and never becomes positive through levelling while the PC is below level 50
  (at 50 see A1; the gap still stays at or below 0). No dialog action changes
  an NPC's level, and XP goes to the PC only.
- **No installed data moves it otherwise (D).** None of the 2,399 installed
  scripts adjusts the level stat (the only two stat adjustments touch
  alignment), no effect entry names `level`, and no magic-tech definition
  names the level stat. Virgil's six attached scripts have no
  transform-into-prototype action, the one other script path that could
  replace his level.
- **Consequence (S applied to D).** Virgil starts at level 1, the same as a new
  PC, so his level can never exceed the PC's. The level gate (refuse when he is
  at least 30 levels above the PC) is unreachable for him, both when he joins
  and in the leave check while he follows. If the PC levels up while Virgil is
  neither following nor waiting nearby, Virgil falls behind and keeps that gap
  for the rest of the game (A1 is the only, unreached, exception).
- **What scheme 91 buys (D).** The winning copy (`Arcanum4.dat`) is 28 steps of
  healing, dodge, melee, lock picking, hit points and the white necromancy
  college up to level 4. The shadowed `arcanum3.dat` copy has one more step,
  white necromancy 5. When scheme 91 completes, the standard NPC scheme 70 runs
  after it.

## Inputs and reproduction

The inputs are the six pinned archives of the [cave research](cave-transition-evidence.md)
(module, resource1..4, PATCH0), the normal-game exterior aggregate
(`a548f611…de6a5`, 1,823,280 bytes), the prototype directory, and the game
root for the [content stack](content-precedence.md) (ARC-DATA-011). Every
resolved candidate must come from one of the pinned archives, and the probe
compares all archive and aggregate fingerprints before and after the run. MES
files are read through the project's [MES reader](../formats/mes-reader.md);
any refusal fails the probe.

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --virgil-auto-level-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --virgil-auto-level \
  --game-root "$GAME" --prototype-root "$GAME/data/proto" --mobile "$GAME/modules/Arcanum/maps/<exterior aggregate>" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE/auto-level.json"
```

| Resource | Candidates (winner first) | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `rules/gamelevel.mes` | resource4 (`Arcanum4.dat`) | 18,492 | `5121e2fa…5919f55` |
| | resource3 (`arcanum3.dat`), shadowed | 18,506 | `f76d56df…f47ef2e` |
| `rules/userlevel.mes` | resource3 | 92 | `90f81657…62b645f` |
| `mes/gamelevelname.mes` | resource3 | 1,259 | `21df5868…306de53a` |
| `rules/effect.mes` | resource3 | 17,497 | `8ac43304…0b6b2` |
| `rules/gameeffect.mes` | module | 417 | `f8c921b7…ead0a85` |
| `rules/spelllist.mes` | resource4 | 160,558 | `b2cdb912…1fc7878d` |
| | resource3, shadowed | 162,769 | `1471e115…cd90dc864` |

`gamelevel.mes` holds entries 50..106 (57 schemes) in both copies; the user
file holds entry 1 only; the name file names both 91 and 70.

## 1. How a level and a scheme work (S)

| ID | Claim | Source |
| --- | --- | --- |
| L1 | A scheme number selects its rule: 1..49 from `rules\userlevel.mes`, 50..199 from `rules\gamelevel.mes`, anything else none. Both files are required at start-up. | [level.c 296–308][init], [level.c 678–701][rule] |
| L2 | A rule is read as `<token> <score>,` repeated: the token up to the next space, the score with `atoi` up to the next comma. Tokens are matched case-insensitively against stats (`st`..`ch`), basic skills, tech skills, spell colleges, tech disciplines and `maxhps` / `maxfatigue`, in that order. An unknown token stops the rule as failed. There is no level token. | [level.c 68–159][tokens], [level.c 857–942][process] |
| L3 | Each step buys one unit at a time until the critter's current value reaches the score: stats at the stat cost, a basic or tech skill point at 1 character point (4 skill levels per point), a spell college level at 1 point, a tech degree at its cost, and one hit or fatigue point per character point. When a skill point is refused (the key stat caps the skill), the step buys +1 of that key stat instead and tries again; a refused spell buys intelligence or willpower unless the level is too low, and a refused tech degree buys intelligence. A step whose target is already met costs nothing. | [level.c 949–1212][buy], [level.c 1016–1023, 1061–1067, 1110–1121, 1160–1166][buy], [skill.c 486–499][skill-base], [skill.c 909–915][skill-cost], [spell.c 347–352][spell-cost] |
| L4 | Only "not enough points" stops a rule early. Any other failure (a spell the level does not allow yet, or a stat purchase of L3 that cannot raise the stat) skips the step and the rule goes on to the next one. A stopped rule restarts from its first step at the next level-up. | [level.c 867–941][process], [level.c 1079–1126][spell-buy] |
| L5 | For an NPC, a rule that finishes without running out of points is followed by the standard NPC scheme 70 in the same call. | [level.c 30–33][std], [level.c 813–821][apply] |
| L6 | A PC levels up only from experience: setting the PC's XP recalculates the level, adds 1 character point per level plus 1 more at every multiple of 5, and runs the PC's own scheme if it has one (non-zero). XP is awarded to PCs only; the dialog `XP` action gives it to the PC. | [stat.c 732–737][xp-recalc], [level.c 432–447][bonus], [level.c 454–534][recalc], [critter.c 1837–1868][give-xp], [dialog.c 2148–2150][dlg-xp] |
| L7 | After the PC's level-up, every follower in his follower list, and every living NPC within one screen of the PC who waits with the PC as leader, gets a new level: (PC new level + his level − PC old level) if he was at or below the PC's old level, otherwise the PC's new level; capped at 50; applied only if higher. He gets the matching character points and his scheme runs. | [level.c 536–556][recalc], [level.c 567–605][follower], [object.c 3524–3551][vicinity] |
| L8 | The level stat runs 0..51, unspent character points 0..56; a base value outside the range is clamped when it is set. | [stat.c 97–101][max], [stat.c 624–656][base-set] |
| L9 | The other writers of an NPC's level are a script's adjust-stat action (any stat of any focus object), a magic-tech trait-index component (sets any base stat of its target, named by the stat key `stat_level` for the level), a script's transform-into-prototype action (replaces the attachee with a new object whose level comes from the prototype), and the effects that name `level` (effective level). The script set-scheme action and the character sheet can change the scheme. The character sheet opens followers and dialog partners read-only, hides its scheme buttons then, and creates its level buttons only when it has no object. | [script.c 2767–2774][adjust], [magictech.c 2976–3003][trait-idx], [magictech.c 6117–6127][trait-idx-parse], [stat.c 225][stat-keys], [script.c 1804–1854][transform], [proto.c 16190–16191][proto-level], [script.c 2789–2797][set-scheme], [script.h 289–291][sat], [charedit_ui.c 1066–1097][sheet], [follower_ui.c 698][follower-sheet], [dialog_ui.c 325][dialog-sheet] |
| L10 | The follow check refuses with "low level" when the NPC's effective level is at least the PC's effective level plus AI field 8 (30 for Virgil's row 5). The join check, the unwait check and the leave check while following all run it. | [ai.c 3185–3187][gate], [critter.c 877–928][join], [ai.c 1505–1530][unwait], [ai.c 2412–2443][leave] |

## 2. Virgil at T0 and scheme 91 (D)

| Field | Value | Origin |
| --- | --- | --- |
| Auto-level scheme | 91 | instance |
| Level, XP, unspent points | 1, 0, 0 | instance |
| Magick points, tech points | 1, 0 | instance |
| Primary stats | all 8 | instance |
| Basic skill points | dodge 1, melee 1, heal 1 (others 0) | instance |
| Tech skill points | none | instance |
| Spell colleges | white necromancy level 1; no mastery (index 16 is −1) | instance |
| Attached scripts | 1324 dialog, 1801 dying, 1997 enter combat, 1100 heartbeat, 1568 new sector, 1848 dialog override | instance |

His T0 sheet already meets the first scheme step (white necromancy 1), and one
skill point each in heal, dodge and melee is base skill level 4, so the first
heal, dodge and melee 4 steps are met unless the key-stat cap is lower (not
evaluated here).

**Scheme 91, winning copy (resource4), 28 steps in order** (token and target):

| # | Steps |
| ---: | --- |
| 0–5 | necro_good 1, heal 4, maxhps 36, dodge 4, melee 4, picklock 4 |
| 6–11 | dodge 4, melee 8, necro_good 2, heal 8, dodge 8, maxhps 62 |
| 12–17 | melee 12, necro_good 3, heal 12, picklock 8, dodge 12, maxhps 90 |
| 18–23 | melee 16, necro_good 4, heal 16, dodge 16, maxhps 106, melee 20 |
| 24–27 | heal 20, picklock 12, dodge 20, maxhps 200 |

The shadowed resource3 copy is the same rule with one more step, `necro_good 5`,
between `melee 20` and `heal 20` (29 steps). As the source reads the spell
table, white necromancy 3 and 4 need level 5 and 10, and a fifth level would
need 15; before that level the step is skipped, not stopped (L4).

**Scheme 70** (standard NPC, both copies identical, 20 steps): melee, dodge and
heal 4 / 8 / 12 / 20 with maxhps and maxfatigue 50 / 75 / 100 / 300 after each
group. After scheme 91 completes, its finals already meet every skill step of
scheme 70, so scheme 70 adds only hit points (to 300) and fatigue (to 300).

Both rules parse completely: every token is known and every step has a score
(296 and 216 bytes, under the 2,000-byte string limit).

## 3. What else could move his level (D)

| Writer | Installed data |
| --- | --- |
| Script adjust-stat (action 137) | 2,399 script names in the six archives, all resolved by precedence (39 shadowed) and all framed. Two adjust-stat actions exist, both on stat 19 (alignment) with a literal stat operand: script 1997 (Virgil's enter-combat script) adds local variable 1 to the attachee's alignment, and script 30252 subtracts 200 from the triggerer's. None names the level, and no stat operand is computed. |
| Script set-scheme (action 139) | One: script 2023 sets the triggerer's scheme to 101. No Crash Site object attaches script 2023, and Virgil's six scripts have no set-scheme action. |
| Script transform-into-prototype (action 40) | 21 actions in 21 scripts; none in Virgil's six attached scripts. Scripts those six reach through call edges were not traced by the probe (U5). |
| Magic-tech trait-index on the level | Both `spelllist.mes` copies contain no `stat_level` key and no trait-index component. |
| Effects naming `level` | None of the 310 effect entries (engine 50..399, module 0..49). |
| Dialog actions | None changes an NPC's level or scheme (L6, L9). |

So, as installed, Virgil's base level changes only by L7 and his effective
level equals his base level.

## 4. The level gap over a game (S applied to D)

Let *g* be Virgil's level minus the PC's.

| Situation | Effect on *g* |
| --- | --- |
| T0, new game | *g* = 0 (both level 1; the PC's level is set to 1 at creation, [player.c 290][player]) |
| PC levels up while Virgil follows, or waits on screen near the PC | unchanged while *g* ≤ 0 (he gains as many levels as the PC, capped at 50) |
| PC levels up while Virgil is elsewhere (not joined yet, waiting off screen, or left) | decreases by the PC's gain; he does not catch up later, because L7 keeps the gap (the only exception is the unreached A1 path at PC level 50) |
| Any installed script, effect or dialog action | none (section 3) |

*g* starts at 0 and never becomes positive (below PC level 50 it can only stay
or fall; at 50 the A1 path could only raise him to the same 50), so it never
reaches +30: the "low level" refusal is unreachable for Virgil. With the consent results of
[ARC-RES-026](virgil-follow-consent.md#4-what-this-means-for-the-consent-check-s-applied-to-d),
his only possible refusals stay "limit reached" and "already in group", and
while he follows he leaves on his own only on dislike (reaction ≤ −1000); the
alignment gap is at most 2000, so the alignment change from his enter-combat
script cannot reach the 2001 limit either.

Each level he gains brings 1 character point (2 at multiples of 5; 59 points
from level 1 to 50). Points beyond 56 unspent are lost to the clamp (L8); that
matters only once both schemes are complete, since a running rule spends them.

## Anomalies (S)

- **A1** At PC level 50 with enough XP for 51, the recalculation caps the XP
  instead of storing level 51. Setting the XP runs the recalculation again
  (L6) with level 50 and nothing left to the next level, so as read it recurses
  without end and never reaches the follower loops. If it ended (in the
  original executable or a fix), every later XP award at level 50 would call
  L7 with PC levels 50 → 51 and raise a lagging follower by one level, capped
  at 50. [level.c 366–377][to-next], [level.c 492–500][recalc], [stat.c 732–737][xp-recalc]
  The 1.0.7.4 update notes say exactly that happens in the installed version
  ([ARC-RES-032](virgil-level-replay.md#5-outside-sources-x), X2).
- **A2** The PC's level-up message is built with `sizeof` of a pointer as the
  remaining buffer length; only the PC path builds the message.
  [level.c 831–834][apply]
- **A3** A rule token with no score left would pass a null pointer to `atoi`;
  the installed rules have none. [level.c 867–868][process]
- **A4** L4's skip-on-failure means a rule can "finish" with steps unmet, which
  for an NPC also starts scheme 70 early.
- **A5** The follower-list loop of L7 has no death check, while the waiting
  loop has one. [level.c 536–556][recalc]
- **A6** The recursive follower list used by L7 never advances its inner
  cursor, so as read it would not end if a follower had followers of his own.
  [object.c 3646–3688][all-followers]

## Proposal for implementation (P)

1. Keep the `LowLevel` result of the [party runtime](../world/party-runtime.md)
   (ARC-WORLD-013) driven by explicit inputs and covered by a synthetic test,
   but do not model a level gap for Virgil: with the installed data it cannot
   arise, so passing his and the PC's current levels is enough.
2. When follower levelling is added to the party runtime, implement L7 as a
   pure function of (follower level, PC old and new level) with the cap at
   50, tested over every input against the invariant the self-test checks.
3. Treat scheme purchase (L2–L5) as a separate later slice under FR-CHAR-021:
   it needs the stat, skill, spell, tech and hit-point cost and cap rules, and
   should import the winning `gamelevel.mes` by precedence.

## Unknowns (U)

- **U1** Which scheme-91 steps Virgil actually buys at each level was not
  replayed: that needs the key-stat skill cap, stat costs, his maximum hit
  points and the spell intelligence/willpower requirements. Answered by
  [ARC-RES-032](virgil-level-replay.md): a replay over his T0 sheet.
- **U2** Who triggers script 2023 (scheme 101) was not traced beyond the Crash
  Site. A scheme change never changes a level, so it could change only what
  Virgil buys, not the gate. Answered by [ARC-RES-032](virgil-level-replay.md):
  it is his own story event (name 6409), setting scheme 101.
- **U3** Local variable 1 in his enter-combat script was not evaluated; it
  changes his alignment, not his level. Answered by
  [ARC-RES-032](virgil-level-replay.md): it copies the player's alignment.
- **U4** Original-executable behaviour of L7 (including A1, A5 and A6) is
  NOT_VERIFIED.
- **U5** The probe does not follow call edges from Virgil's six scripts, so a
  transform-into-prototype or adjust-stat action in a script they call is
  counted only in the whole-archive totals above (no adjust-stat names the
  level anywhere). Next check: the call-edge census of the
  [cave research](cave-transition-evidence.md) applied to his scripts.
  Answered by [ARC-RES-032](virgil-level-replay.md): 20 scripts reached, only
  1997 has an adjust-stat action, 15 call-ex edges stay unresolved.

## Method

Source reading at the pinned commit through the call chains above, plus the
read-only probe `--virgil-auto-level` (self-test
`--virgil-auto-level-self-test` covers the rule grammar, the bonus-point rule,
the follower-level rule with its gap invariant over every input, and the
script action scan; the real-data run also counts transform actions and the
magic-tech level key). No CE code or structure was copied; names in this
document are descriptive.

[init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L296-L308
[rule]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L678-L701
[tokens]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L68-L159
[process]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L857-L942
[buy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L949-L1212
[spell-buy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L1079-L1126
[std]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L30-L33
[apply]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L772-L848
[bonus]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L432-L447
[recalc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L454-L560
[follower]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L567-L605
[skill-base]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L486-L499
[skill-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L909-L915
[spell-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L347-L352
[xp-recalc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L732-L737
[give-xp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1837-L1868
[dlg-xp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2148-L2150
[vicinity]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3524-L3551
[max]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L97-L101
[base-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L624-L656
[adjust]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2767-L2774
[set-scheme]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2789-L2797
[sat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L289-L291
[sheet]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/charedit_ui.c#L1066-L1097
[follower-sheet]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/follower_ui.c#L698
[dialog-sheet]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L325
[gate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3185-L3187
[join]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L877-L928
[unwait]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1505-L1530
[leave]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2412-L2443
[trait-idx]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2976-L3003
[trait-idx-parse]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6117-L6127
[stat-keys]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L225
[transform]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1804-L1854
[proto-level]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16190-L16191
[to-next]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L366-L377
[all-followers]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3646-L3688
[player]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/player.c#L290
