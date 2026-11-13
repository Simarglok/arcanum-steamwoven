# Poison sources: what the original has and what the repository builds

Status: **Research**, work item ARC-RES-1329 (2026-11-13, issue #1329), the rest of slice **E8-g** of the
[missing-systems plan](../project/missing-systems-plan.md). It lists every way the installed 1.0.7.4 game puts poison on a
critter or takes it off (traps, food, weapons, spells, scripts, timers), counts each one in the installed data, says what
Steamwoven does for it, and files each gap as an issue. It adds one read-only probe mode (`--poison-census`) and this note
(ledger rows [PZ1–PZ10](ledger/combat.md#poison-sources-arc-res-1329)); it changes no production API, viewer, session or
launcher.

Requirements: [FR-CHAR-026](../requirements/characters-skills.md#fr-char-026) (life state: poison in it) and
[FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010) (poison next to fatigue, knockout and death). No
requirement's coverage changes: the rows already say what is built; the gaps below are issues.

## Answer

- **In the installed data the original has one door for poison.** Every increase goes through the damage step (0x4B4390, the poison slot of the
  damage packet, written at 0x4B51BA); every decrease through the heal applier (0x4B58C0), the script action `heal_poison`
  (action 128, 0x44A77E), the fate window's full heal (0x4F52D0), the poison timer's recovery (0x4B1170) or a negative poison
  damage (a resistance above 100 %). The two multiplayer routines (0x49D100, 0x4A6010) only re-apply the current level. The generic stat setter could write stat 24 through effects or script action 137 in principle; the installed data never does. The
  installed data never writes the stat any other way: no effect of `effect.mes`, `item_effect.mes`, a blessing or a curse
  changes the poison level (only the recovery rate, 4 effects, and `resistpoison`, 17 effects), no script action names stat 24
  as a target, and no critter prototype or placed mobile starts poisoned (0 of 1,308 prototypes, 0 of the placed mobiles).
- **So a poison source is a producer of a damage packet with a poison amount.** The callers of the damage step are listed in
  ledger GT210; the ones that can carry poison are the melee and ranged hit (0x4B3770), the projectile hit (0x4B2F60), the
  spell Damage component (0x4518D0), the native trap script 30006 (0x4BCDC0) and script damage (actions 60 and 138). The burn,
  the knockback collision, the kill, the poison tick, the action-point overdraft and the object-flag kill carry none.
- **Steamwoven builds every one of them for the hero and for NPCs on the viewer's maps**, except where the table below says
  *gap*. Three gaps carry poison: the food use script (the one script on 729 food items that cures poison), the wield-on
  scripts of poisoning items, and the combat-point scripts (hit, critter hits, critical hit) of poisoning weapons. One more
  gap, an NPC or companion walking onto a poison trap, was already an issue (#1469).
- **One older open question is closed here:** the poison timer does hit a critter whose object is switched off (PZ9), as
  Steamwoven already does.

## The sources (AC1)

Numbers are for the installed `Arcanum` module (1.0.7.4 data, `/Applications/Arcanum` stack), counted by
`--poison-census`. *Placed* counts the placed mobiles (NPCs and the items they carry; chests, floors and shop stock are
sector objects the probe does not tally for items), scripts by attachment (an object, a tile or a sector holding the script).

| # | Source | Original (exe) | Installed data | Steamwoven | Gap |
| --- | --- | --- | --- | --- | --- |
| S1 | **Weapon hit**: the poison range of a weapon's damage table | The hit rolls the weapon's five ranges (0x465F70), the resolver adds the critical multiplier, resistance 3 (poison) applies, and the packet's poison slot is added to the level (0x4B51A4–0x4B51BA); none against undead, mechanical or stoned (0x4B4390 start) | 4 weapon prototypes (6058, 6115, 6151 melee with 10–35, 1–20, 20–40; 6167 a bow 10–30); placed: 6058 ×4, 6115 ×1, plus one instance (6062) whose own damage table holds poison | Built: the attack resolver rolls the poison slot, the life state adds it (ARC-COMBAT-002, ARC-COMBAT-005), the hero's and the NPCs' attacks both apply it | none |
| S2 | **Critter's own damage table** (monsters, a critter with no weapon) | A monstrous critter takes the NPC damage table (field 0x12F) in place of a weapon (0x465F70, ledger AH1), poison included | 28 NPC prototypes (9 in 27309.., 19 in 28310..), poison 1–2 to 50–75; placed: 299 NPCs of 22 prototypes on 25 maps | Built: `SessionCritterCombat.Natural(npc.Damage)` keeps all five slots (ARC-WORLD-092) | none |
| S3 | **Spell Damage component** of the poison type | The component fills the poison slot and calls the damage step (0x4518D0, 0x451A4C) | 8 records: 11 (college 2, rank 2, 15–50, Begin and Maintain, also a gas cloud), 129 (unused, Maintain 5), 164 Poison 70, 169 Venom 10, 170 Cleaner 20, 171 Strong Poison 290 (and two effects), 185 Explode (Poison) 5–30, 200 Mustard Gas 15–25 (Begin and Maintain, a gas cloud). No record casts another poison record | Built: spell damage with the poison slot (ARC-MAGIC-012), the run's MAINTAIN stage and the Summon of the clouds (E1-b, SUM14) | none |
| S4 | **Food and items that hold a poison spell** | An item's slot spell is cast when it is used (eaten, read, thrown) | Food 10085 (164), 10088 (170), 10095 (171), 10103 (169); scroll 11090 (spell 11); grenade 15186 (200). Records 129 and 185 are held by no item. Placed on mobiles: 10085 ×7, 10088 ×19, 10095 ×6, 10103 ×5, 11090 ×2. None has an AI action | Built: the item spell path, the target cursor (ARC-DEV-365), the throw (ARC-COMBAT-023); smoke `tech-products` uses record 171 on a living NPC (ARC-MAGIC-051) | none |
| S5 | **NPC casters** | An NPC casts a spell its college levels allow from the offensive list (priority 14 for record 11) | 28 placed NPCs of 8 maps hold the college level for record 11 | Built: AI spell casting with the spell run | none |
| S6 | **Native trap script 30006** (poison) | 0x4BCDC0: damage in the range of the low and second counter bytes, victims in the square of half side 4, 2 or 1 around the trapped object | 215 placed sites on 13 maps (200 trap objects, 9 containers, 6 portals), 213 of them with counters 10–50; one more trap, script 30000, casts spell 11 (1 site, top counter byte 11). No prototype default is a poison trap | Built for the hero: a step, a use and a strike set it off (ARC-WORLD-107, ARC-WORLD-110) | **gap**: an NPC's or a companion's step onto a trap object and an NPC's use of a trapped object do not meet it: issue [#1469](https://github.com/Simarglok/arcanum-steamwoven/issues/1469) (not duplicated) |
| S7 | **Script damage**, actions 60 and 138 with damage type 1 | Each object of the operand's list gets a fresh packet with no attacker (0x448A82, 0x44AB93; ledger GT194); the victim's point-0x17 script may veto it | 18 clauses (14 scripts with action 60, 49 placed sites on 3 maps, 44 of them tile sites of one map, 5 object sites at the points use, hit, dialog, critter hits and critical hit; 4 scripts with action 138, no attachment of their own: each is called from a wield-on item script) | Built: the damage set (S30) in the placed and heartbeat sets, which the use, tile and sector runs share (ARC-DEV-243, ARC-DEV-255) | the use and tile sites run; the dialog-point site does not (no dialog host names the damage actions) and the next two rows do not run |
| S8 | **Wield-on item scripts that poison** (point 24) | Run after a wield (0x4677B0, ledger IT8) | 6 scripts call six poison scripts (2677, 2679, 2681, 2682 with action 138; 2664, 2678 with action 60); 5 of the callers are attached at point 24 (5 mobile sites on 4 maps), the point of the sixth is not read | Not run: the item-event set (`SessionItemScripts.Admission`, set S14) has no damage action | **gap**: [#1482](https://github.com/Simarglok/arcanum-steamwoven/issues/1482) |
| S9 | **Combat-point scripts that poison** (hit 7, critter hits 26, critical hit 34) | Run from the combat code (0x4B2BAE, 0x4B3003, 0x4B37CD…) | one action-60 poison script on each of the three points (3 sites); a hit script (point 7, 1 site) also casts spell 11 | Not run: the combat points have no host ([script trigger audit](script-trigger-audit.md), slice 5, 61 attachments) | **gap**: [#1483](https://github.com/Simarglok/arcanum-steamwoven/issues/1483) |
| S10 | **Poison timer** (event kind 6) | A damage tick every 15 s through the damage step (1, 2 at 200, 3 at 550 normal damage), recovery −PoisonRecovery every 120 s; the handler runs in the single-player game whether or not the object is switched off | not data | Built (ARC-WORLD-024, ARC-WORLD-039); off-flag read closed by [PZ9](ledger/combat.md#poison-sources-arc-res-1329) | none |

The ways **out** of poison (not sources, listed so nothing is missed): the heal applier through spells (58, 61 Halt Poison,
152 Cure Poison, 156 Cure All, 194 and three unused records) and the Herbology and other potions, the NPC AI's cure-poison
list (spells 61, 152, 156), the fate window's full heal, a stat-24 write by `heal_poison`, and the recovery tick. All are
built (since ARC-SCRIPT-1481, issue #1481, also **`heal_poison`**; the text below is the finding before it): the food use script **30282** (`get_hit_points`, `get_fatigue_points`, `get_stat` of stat 24,
heals and `heal_poison`) stands on **729 food items on 27 maps** and is refused whole (actions 107, 108 and 128 are in no
admission set). That is the one gap of the cure side; it is the largest single refusal of the module
([script-refusal-census](script-refusal-census.md)).

## Gaps and the issues filed (AC2)

| Gap | Evidence | FR groups | Issue |
| --- | --- | --- | --- |
| The food use script 30282 and the vitals commands (get hit points 107, get fatigue points 108, `heal_poison` 128, and get stat for stat 24) | 729 sites on 27 maps, 836 sites freed with the other VIT commands (script-refusal-census); the use host answers get stat for strength to charisma, 16, 17 and 19 only (GS3) | FR-ITEM-009, FR-SCRIPT-002, FR-CHAR-026, FR-COMBAT-010 | [#1481](https://github.com/Simarglok/arcanum-steamwoven/issues/1481) |
| Wield-on and wield-off item scripts that deal poison damage | S8 above; the item set stops at the first damage action | FR-ITEM-007, FR-ITEM-009, FR-SCRIPT-001, FR-CHAR-026 | [#1482](https://github.com/Simarglok/arcanum-steamwoven/issues/1482) |
| Combat-point scripts (hit 7, critter hits 26, critical hit 34) | S9 above; 61 attachments of the combat points, 3 of them poison | FR-SCRIPT-001, FR-COMBAT-005, FR-CHAR-026 | [#1483](https://github.com/Simarglok/arcanum-steamwoven/issues/1483) |
| An NPC or companion meeting a poison trap | S6 above, 215 sites | FR-NAV-006, FR-ITEM-016 | #1469 (existing) |

## Method and limits

- **Exe reads (F1, one reader, the decompiled exports and the listings in `research-private`)**: the callers of the stat
  setter 0x4B0980 and getter 0x4B0740 with stat 24 (a scan of every `PUSH 0x18` before the call in all 6,711 functions found
  six writers: 0x49D100, 0x4A6010, 0x4B1170, 0x4B4390, 0x4B58C0, 0x4F52D0, plus the script action 128 case of 0x447220), the
  damage step's poison write, the heal applier's poison read and the poison handler 0x4B1170 (PZ1–PZ9). No bytes, listings or
  decompiled text are in this note.
- **Data census**: `dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --poison-census --game-root GAME`. It reads
  the spell records (SC reader, like-original mode), every prototype through the combat-field reader, every map's mobiles by
  effective fields, every installed `scr` script, and every attachment (the ARC-RES-037 walk, now also with the attachment's
  counters). Output is numbers and map names only. The placed counts include only mobiles; items in chests, on the floor and in
  shop stock are not tallied, so the item counts are a floor, not the number of poison items in the game.
- **Not counted**: dialog files (a dialog result does not run damage actions; not read), the script `call` graph beyond
  literal call operands, spell casts by computed operands, and the sector-object items.

## Issues filed

[#1481](https://github.com/Simarglok/arcanum-steamwoven/issues/1481) (food use script 30282, the vitals commands), [#1482](https://github.com/Simarglok/arcanum-steamwoven/issues/1482) (wield-on poison scripts) and [#1483](https://github.com/Simarglok/arcanum-steamwoven/issues/1483) (combat-point scripts); the trap gap was already [#1469](https://github.com/Simarglok/arcanum-steamwoven/issues/1469).
