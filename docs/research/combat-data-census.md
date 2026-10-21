# Combat-domain data census and first-fight candidates

Status: **Research**, work item ARC-RES-041 (2026-10-17), the original-data
half of stage A0 of the [NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(it absorbs stage A9). It counts what the owner's installation holds for the
list in [section 10 of the combat-domain load map](combat-domain-load-map.md#10-original-data-census-next-task)
(ARC-RES-040) and picks the first real fight for plan slice I10. It adds one
read-only probe mode and this note; it changes no production API, reader,
viewer, session schema or launcher file.

Requirements: [FR-AI-001](../requirements/story-social-ai.md#fr-ai-001),
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002),
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001), where
they depend on loaded data.

## Vocabulary and boundary

- **Source fact**: an earlier source reading (provenance in git history),
  cited by the executable address of its function, level F0 (not checked in
  the executable), or from the
  [verification ledger](original-verification-ledger.md) (ARC-RES-048), where
  the retail 1.0.7.4 executable already settled the reading. Every parser in
  the probe follows the load map's reading.
- **Local observation**: a number the probe measured on the owner's
  installation (module `Arcanum`) through the module content stack of
  [content precedence](content-precedence.md) (ARC-DATA-011). Only aggregates
  are recorded: counts, numbers, entry, spell and prototype numbers, flag and
  key names. No file, text, art or path is reproduced.
- **Project policy**: a proposal for the slices that use the data; none is
  adopted by this note.

Original executable parity of the probe's own parsers is **NOT_VERIFIED**
beyond what the ledger already records.

## How to run

```bash
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --combat-census --game-root <game folder> [--module <name>]
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --combat-census-self-test
```

The report is deterministic JSON. On the owner's installation two runs gave
the same SHA-256 `5de5b853c42339096fe51af3ea940b0f8767cdb7149d7248bb74c7c0e93870cf`
in about 5 s. The self-test checks every parser on synthetic fixtures (AI rows
across a numbering gap, the named-key cursor with skipped, unknown and fatal
keys, spell numbering, effect operators, prototype ranges, art types, layer
labels and the LM1 estimate).

Readers used: the production `ContentStack`, `MesFile`, `WorldCatalog`
(prototypes and every map's mobiles), `CombatFieldReader` (ARC-DATA-021) over
prototypes and over catalogue instances by their effective fields,
`EffectRules` (to classify effect attributes and as a second opinion), the
pinned Area profiles of the Crash Site exterior and cave with
`AreaInventoryReader`, and the probe's own map-list decoder (ARC-RES-035). The
probe's new parsers are the AI rows, the spell record lines +1 and +4 and the
effect operator shapes.

## Answer

1. **Every catalogue file exists and comes from the base, except as below
   (local observation).** All 37 init-phase files are present, all in the base
   archives (`arcanum4.dat` over `arcanum3.dat` where both hold one). One init
   file has a module copy: `rules\unique.txt` sits in `Arcanum.patch0`; the
   original reads the base copy at init, and the file only matters for
   generated prototypes, which never happen here (item 5). Of the 16 module-phase
   files, 14 are in the module archive; `oemes\gameoname.mes` and
   `oemes\gamefaction.mes` do not exist, so object and faction names are the
   base tables alone.
2. **AI rows: no gap, so LM3 changes nothing (local observation).**
   `rules\ai_params.mes` has entries 0..40 without a gap, 17 values in every
   row, all numeric; rows 41..99 stay zero. `rules\gameai.mes` has one entry,
   100, with 14 values (the last three stay zero or keep the previous module's
   values). Positional and by-number loading agree. No NPC instance in the
   module uses a row above 40 or the module row (item 6).
3. **Spells: every record is present and nothing is unknown or out of order
   (local observation).** All 223 records have lines +0 and +1; 105 have an
   AI line +4. No record uses `IQ:`, so the willpower minimum is always the
   default {6, 9, 12, 15, 18}[n mod 5] for records below 80 (no record
   overrides the default; LM6's question, which stat that value gates, stays
   with stage A5). `Resist:` uses constitution (22), willpower (12), dexterity
   (2) and strength (1); the two LM17 keys never occur. On lines +1 and +4,
   the only lines the probe parses, no key is skipped for its order, none is
   unknown, and no line would end the original's process.
4. **Effects: five entries use `*` or `/`, none overflows (local
   observation).** `rules\effect.mes` has 309 entries (50..358): 587 add, 40
   percent, 11 min, 7 max, 5 multiply and 1 divide operators. Entries 91, 97,
   213, 214 and 218 hold the `*`/`/` pairs that the earlier reading mis-parsed
   (LM9, a reconstruction slip per the ledger). An entry has up to 11 pairs, but no collection gets more
   than 7, the size of the source's per-collection arrays. The module's
   `rules\gameeffect.mes` has one entry (0, five adds).
5. **Prototypes: every range is full, so nothing is ever generated; LM15 is
   settled by art (local observation).** All 1,423 prototype files are loose
   in `data\proto`, none shadowed by the module, and every slot of all 20
   description ranges is present. In 27309..27393, 81 of 85 critters use
   unique-NPC art; in 28310..28471 all 162 use monster art. The description
   header's labels (27309 unique NPCs, 28310 monsters) and the generator are
   right; the earlier reading's range comments are the slip.
6. **The first fight: an exterior creature of prototype 28359 (proposal).**
   The Crash Site exterior holds 25 creatures and 18 humans, the cave 6
   creatures and one dead human. Every creature is kill-on-sight and faction
   19 (except two instances of 28339, faction 9); all but one (27359, which
   wields a weapon) fight with their own damage table and carry nothing. No
   NPC there holds more than one weapon, so neither LM1 nor LM2 can change a
   weapon choice.
   [Section 7](#7-first-fight-candidates-plan-slice-i10) ranks three
   candidates.

## 1. Where each file comes from

"Init" files are read before a module is mounted, so the original reads the
first base candidate; "module" files are read after (load map
[section 2](combat-domain-load-map.md#2-where-each-file-comes-from)). Layer
names are content-stack roles.

| Files | Phase | Layer | Notes |
| --- | --- | --- | --- |
| `ai_params`, `magictech`, `spelleyecandy`, `tech`, `effect`, `combat`, `combateyecandy`, `skill`, `stat`, `userlevel`, `gamelevelname`, `userlevelname`, `level`, `xp_critter`, `critter`, `gameback`, `reaction`, `trapeyecandy`, `trap`, `item`, `animeyecandy`, `weaponeyecandy`, `faction`, `description`, `scripteyecandy` | init | `arcanum3.dat` only | |
| `spell`, `spelllist`, `item_effect`, `xp_level`, `gamelevel`, `backgrnd`, `invensource`, `invensourcebuy`, `oname`, `npc.txt`, `monster.txt` | init | `arcanum4.dat` over `arcanum3.dat` | the base winner is what both the original and the stack read |
| `unique.txt` | init | `Arcanum.patch0` over `arcanum4.dat` over `arcanum3.dat` | the stack's winner is the module patch; the original reads `arcanum4.dat` |
| `gameai`, `gameeffect`, `gameitemeffect`, `gamebless`, `gamecurse`, `gamerep`, `gamereplog`, four `game_rp_npc_*`, `gamedesc`, `storystate`, `maplist` | module | `Arcanum.dat` only | |
| `gameoname`, `gamefaction` | module | absent | names come from the base tables only |
| `proto\*.pro` (1,423) | init | `data` folder only | none in an archive, none in the module |

Every present MES file parses with the production reader. Module table sizes:
`gameai` 1 entry, `gameeffect` 1, `gameitemeffect` 573 (30000..31170),
`gamebless` 54 and `gamecurse` 52 messages, `gamerep` 76 (1000..1075),
`gamedesc` 1,152 (30000..31213), `storystate` 25, `maplist` 81 (5000..5080).

## 2. AI parameter rows

Source fact (AI init (0x4A8320)): the entry numbered 0 must exist; from it each
next entry by position fills the next row up to row 99; values are split on
space, tab and line feed and read with `atoi`, at most 17 per row. The module
file does the same from entry 100 up to row 149. The ledger confirms the
positional fill in the original (LM3, a latent original bug).

| Measure | `ai_params.mes` | `gameai.mes` |
| --- | ---: | ---: |
| Entries, first..last | 41, 0..40 | 1, 100 |
| Gaps in numbering | none | none |
| Values per row | 17 in all 41 | 14 |
| Non-numeric values | 0 | 0 |
| Rows loaded / by number = by position | 41 / yes | 1 / yes |

Per column of the 41 base rows (columns 0..16; meanings are stage A1's work,
the uses below are the [plan's](npc-combat-ai-plan.md#answer)
reading):

| Column | Plan reading | Min | Max | Distinct | Values (count) |
| ---: | --- | ---: | ---: | ---: | --- |
| 0 | flee at own HP% | 0 | 100 | 7 | 0 (31), 5 (1), 10 (4), 20 (1), 30 (1), 50 (1), 100 (2) |
| 1 | flee when the other party is larger by | 0 | 1000 | 8 | 1000 (27), 10 (4), 0, 7, 20, 100 (2 each), 5, 16 |
| 2 | flee when the other party's level sum is higher by | 0 | 1000 | 6 | 1000 (28), 100 (7), 0 (3), 3, 5, 10 |
| 3 | never flee while the target's HP% is below | 0 | 101 | 4 | 20 (27), 101 (8), 15 (4), 0 (2) |
| 4 | flee distance | 1 | 20 | 6 | 6 (23), 1 (8), 5, 7 (3 each), 10, 20 (2 each) |
| 5 | | −1001 | 1000 | 6 | 15, 20 (15 each), 1000 (7), −1000 (2), −1001, 25 |
| 6 | | 20 | 2001 | 7 | 1001 (15), 2001 (14), 1000 (7), 400 (2), 20, 150, 250 |
| 7 | | 20 | 2001 | 9 | 1000 (21), 2001 (9), 355 (4), 410 (2), 20, 200, 400, 510, 1001 |
| 8 | | 2 | 100 | 8 | 2 (33), 5 (2), 7, 8, 20, 25, 30, 100 |
| 9 | follower reaction change when hit by the PC | 0 | 10 | 3 | 0 (39), 5, 10 |
| 10 | attack at reaction at or below | −1000 | 0 | 2 | 0 (38), −1000 (3) |
| 11 | attack at alignment gap at or above | 2001 | 2001 | 1 | 2001 (41): never |
| 12 | | 100 | 2001 | 5 | 100 (28), 2001 (9), 350 (2), 300, 500 |
| 13 | defensive spell roll (P7) | 0 | 75 | 8 | 0, 50 (12 each), 20 (5), 30, 40 (4 each), 75 (2), 15, 25 |
| 14 | heal chance in combat | 0 | 2001 | 8 | 20 (17), 100 (11), 0 (8), 10, 25, 35, 50, 2001 |
| 15 | keep distance / back off | 0 | 5 | 4 | 1 (14), 5 (13), 0 (12), 4 (2) |
| 16 | may open portals | 0 | 1 | 2 | 1 (39), 0 (2) |

Local observation: column 11 is 2001 in every row, so the alignment-gap
attack never fires with shipped data; column 3 is 101 in 8 rows, which (by the
plan's reading) means those rows never flee. P7's direction matters for 29
rows with a non-zero column 13.

## 3. Spell records

Source fact (numbering (0x44FFA0, 0x450090, 0x4501D0, 0x450240), line +1 (0x4578F0), line +4 (0x457E70)):
record n starts at message 1000 + 50·n, 2000 more from record 140; lines +0
and +1 are always read, +2..+4 only when present, components from +5 to the
first gap. Named keys must appear at the cursor in a fixed order and are
otherwise skipped; an absent `AI_*` value is −1. The probe replays that cursor
and separately lists every key at the start of a segment, so a skipped key
would show as a difference (none does).

| Measure | Count |
| --- | ---: |
| Records with line +0 / +1 / +2 / +3 / +4 | 223 / 223 / 10 / 160 / 105 |
| Records with 0 / 1..4 / 5..9 / 10..13 component lines | 5 / 123 / 86 / 9 |
| `IQ:` (any record) | 0 |
| `Resist:`: constitution / willpower / dexterity / strength | 22 / 12 / 2 / 1 |
| `Resist:` with an LM17 key (poison level, age) or no match | 0 |
| `Info:` aggressive / friendly / neutral / may be aggressive | 82 / 75 / 0 / 0 |
| `Disabled:`, `No_Resist:`, `No_Reflect:` | 0, 2 (tech), 2 (college) |
| Keys skipped for their order, unknown keys, fatal lines (lines +1 and +4 only) | 0, 0, 0 |

Line +1 keys by record range (college 0..79 / other magic 80..139 / tech
140..222), which answers part of the magic research's open question
[O2](magic-core-evidence.md#open-questions): `Cost:` 80/36/41, `Maintain:`
53/7/6, `Duration:` 2/12/37, `DurationStatInfo:` 0/0/9,
`DurationTriggerCount:` 2/3/6, `Resist:` 26/6/5, `Range:` 0/8/42,
`ChargesFromCells:` 0/0/3, `ChargeBeginCost:` 1/0/1, `Info:` 37/43/77.

AI action lists (spell number: value, highest first; a run of numbers shares
the value after the last one; the defensive list's second value is shown as
`@1` and is 0 elsewhere):

| Action | Set | Spells |
| --- | ---: | --- |
| Flee | 7 | 69:10, 79:8, 128:8, 77:7, 3:4, 13:3, 26:1 |
| Summon | 16 | 14:10, 19:9, 24:8, 29:7, 53:6, 74:5, 73:4, 68:3, 72:2, 57, 71, 123, 124, 125, 126, 135:1 |
| Defensive | 25 | 44:15, 13:14, 18:13, 28:11, 43:10@1, 78, 113, 133, 134, 143, 197:9@1, 67:7, 41:6@1, 76:5@1, 51:4@1, 40, 212, 214:3, 10, 15, 20, 23, 25, 30, 45:2 |
| Offensive | 32 | 34:20, 39, 142:19, 49:18, 59, 207:17, 38:16, 33:15, 11:14, 22:13, 27:11, 2, 37, 193:10, 48, 127:9, 36:7, 31:6, 132:5, 12, 131, 208:4, 16, 122:3, 70, 169, 171:2, 0, 46, 47, 55, 66:1 |
| Healing light / medium / heavy | 7 / 3 / 1 | 189:8, 115:5, 137, 150, 191, 192:2, 60:1 / 62:3, 155, 156:2 / 121:8 |
| Cure poison / fatigue recover / resurrect | 3 / 1 / 2 | 152, 156:2, 61:1 / 151:2 / 64, 190:1 |

## 4. Effects

Source fact (parse (0x4E9C20)): tokens split on space and comma, an
attribute then its operator (`min`/`max` take the next token); the ledger
(LM9, SH-07) records that the original reads the number after `*` and `/`
(the earlier reading stored 0) and has no bound on pairs, while each attribute collection has
seven slots.

| Measure | `effect.mes` | `gameeffect.mes` |
| --- | ---: | ---: |
| Entries (numbers) | 309 (50..358) | 1 (0) |
| Pairs per entry: 0 / 1 / 2 / 3 / 4 / 5 / 6..7 / 8..11 | 3 / 183 / 45 / 16 / 30 / 11 / 13 / 8 | 5 pairs in its one entry |
| Most pairs in one collection | 7 | 4 |
| Operators: add / percent / min / max / multiply / divide | 587 / 40 / 11 / 7 / 5 / 1 | 5 adds |
| Entries with `*` or `/` | 91, 97, 213, 214, 218 | none |
| `min` with a non-lowercase `i` (acts as max) | 0 | 0 |
| Production `EffectRules`: parsed / refused | 304 / 5 (the `*`/`/` entries) | 1 / 0 |

## 5. Prototypes

Ranges here are description numbers; a prototype number (as in the tables
below and in section 7) is its description number + 20. Every one of the 20
description ranges of the prototype functions (0x4681B0–0x49C610) is full
(1,423 files: 8 walls, 15 portals, 38 containers, 45 scenery, 1 projectile,
150 weapons, 4 ammunition, 251 armour, 1 gold, 88 food, 84 scrolls, 3 keys, 1
key ring, 68 written, 148 generic, 13 PCs, 250 + 85 + 162 NPCs, 8 traps), so the
generator for missing prototypes and the `rules\*.txt` critter defaults never
run on this installation. The production combat-field reader reads all 1,308
item and critter prototypes; the other 115 are not its types.

**LM15.** Source fact: the earlier reading's range comments label 27309..
"monster" and 28310.. "unique NPC"; the description header and the generator
(0x49B340)
use the opposite. Local observation by current art type: 27309..27393 holds 81
unique-NPC art critters and 4 human-art critters (61 with the monster flag, 76
with a monster damage table); 28310..28471 holds 162 monster art critters (97
with the monster flag, 161 with a damage table). The data agrees with the
header and the generator. Twenty-two NPCs of the ordinary range 17067..17316
also use monster art.

**NPC prototypes (497).** AI rows 0 (291), 19 (130), 2 (59), 14 (4), 9, 15
(3 each), 10, 12 (2 each), 13, 16, 20 (1 each). NPC flags: `WANDERS` 237, `KOS`
216, `CAST_HIGHEST` 5; no other NPC flag is set in a prototype. Critter flags:
`MUTE` 221, `MONSTER` 202, `NO_FLEE` 141, `FATIGUE_IMMUNE` 71, `ANIMAL` 58,
`UNDEAD` 49, `LIGHT_SMALL` 9, `DEMON`, `IS_CONCEALED`, `MECHANICAL` 8 each,
`MOVING_SILENTLY` 5, `UNREVIVIFIABLE`, `UNRESSURECTABLE` 4 each,
`NON_LETHAL_COMBAT` 1. Factions: 0 (266), 19 (200), 10 (16), 14, 45 (4 each),
51 (3), 2 (2), 21, 54 (1 each). 57 have a magick college level, none a tech
degree, 237 a monster damage table.

**Items (798).** Seven carry the valid-AI-action item flag, each with a
matching action number: food 10094 healing light, 10154
and 10158 healing heavy, 10157 cure poison, 10155 and 10156 fatigue recover,
and generic 15179 offensive; no item has an action number without the flag.
Items with at least one spell: 84 scrolls, 43 food, 16 generic, 7 weapons, 5
armour. Generic flags: 11 grenades, 2 healing items. Weapons (150): melee 89,
firearms 41, bow 12, throwing 8; ammunition type arrow 12, bullet 36, power
cell 2, fuel 3, none 97.

## 6. NPCs and creatures in the maps

**Whole module.** Through the catalogue, the mobile objects of 79 maps read
with no issue, and every instance's prototype exists. 3,792 NPC instances by
effective fields (instance field, else the prototype's):

- AI rows: 19 (1,656), 0 (1,470), 2 (191), 4 (127), 10 (124), 15 (98), 14
  (47), 12 (34), 13 (8), 16 (6), 5, 23 (3 each), 6, 38 (2 each) and one each of
  1, 7, 8, 9, 17, 18, 22, 24..33, 35..37, 40. The highest row used is 40, so
  every used row exists; nothing uses the module's row 100.
- NPC flags: `WAYPOINTS_DAY` 3,394, `KOS` 2,437, `WANDERS` 2,010,
  `CHECK_WIELD` 909, `WAYPOINTS_NIGHT` 316, `WANDERS_IN_DARK` 248, `ALOOF` 83,
  `CAST_HIGHEST` 44, `GENERATOR` 27 (rate bits 5/10/14). Never set, among
  others: `AI_WAIT_HERE`, `LOOK_FOR_WEAPON`/`ARMOR`/`AMMO`, `NO_ATTACK`,
  `BACKING_OFF`, `CHECK_GRENADE`, `FIGHTING`, `KOS_OVERRIDE`, `FAMILIAR`.
  `WAYPOINTS_DAY` is set on almost every instance but on no prototype; what
  the bit means at run time is stage A1's question.
- Critter flags: `MUTE` 2,399, `MONSTER` 1,550, `NO_FLEE` 1,006, `ANIMAL` 946,
  `FATIGUE_IMMUNE` 647, `UNDEAD` 550, `IS_CONCEALED` 202, `MOVING_SILENTLY` 194.
- 50 factions; the largest are 19 (2,331) and 0, none (726). 407 instances
  have a magick college level, 2,513 a monster damage table.
- Carried weapons (weapon mobiles whose parent is the NPC): none 2,616, one
  1,103, two or more 73; 742 NPCs wield one.

**LM1 and LM2 (estimate).** Source fact: the original adds the five damage
averages and stores the best in-range weapon (ledger: both are reconstruction slips). The
search starts below any score, so the first eligible weapon is always taken
and a later one replaces it only on a higher score, or an equal score with a
higher handle (wield (0x465170)). So only NPCs with two or more eligible weapons
can be affected: 73 in the module. For the 31 whose weapons share one skill,
comparing stored damage alone, the earlier reading's fatigue-only score picks the same weapon

for 19 and another for 1; 11 involve a tie that the runtime handle decides; 42 mix skills
and are not estimated. LM2 needs a target and its distance: next to the
target every weapon is in range, so it can touch all 73; farther away it needs
two or more weapons in range, and 6 of the 73 hold two or more weapons with
range above 1.

**Crash Site exterior (pinned Area profile).** 43 NPCs:

| Prototype | Core / context | Range, art | AI row | Faction | Level | Flags | Damage | Gear |
| --- | ---: | --- | ---: | ---: | ---: | --- | --- | --- |
| 28359 | 10 / 3 | 28310.., monster | 19 | 19 | 1 | KOS, WANDERS (one core instance WAYPOINTS_NIGHT instead); ANIMAL | 1–3 | none |
| 28339 | 0 / 5 | 28310.., monster | 19 | 19 (3), 9 (2) | 3 | KOS, WANDERS, WANDERS_IN_DARK; ANIMAL | 3–7 | none |
| 28447 | 3 / 0 | 28310.., monster | 19 | 19 | 2 | KOS, WANDERS; ANIMAL | 1–4 | none |
| 27356 | 3 / 0 | 27309.., unique art | 14 | 19 | 3 | KOS, WANDERS; MONSTER | 3–8 | none |
| 27359 | 1 / 0 | 27309.., unique art | 15 | 19 | 12 | KOS, CHECK_WIELD, WAYPOINTS_NIGHT; MONSTER | 5–10 | wields weapon 6198 (melee); college 10 at level 2 |
| 17102 (Virgil) | 1 / 0 | NPC, human | 5 | 0 | 1 | CHECK_WIELD; NO_FLEE | — | weapon 6057 carried, not wielded; college 12 at level 1 |
| 17 other humans | 16 / 1 | NPC | 0 | 0 | 1–2 | not KOS (one ALOOF) | — | no weapon |

Every exterior NPC carries `WAYPOINTS_DAY`; every creature is `MUTE`. No NPC
here has a mastery college. Of the 17 other humans, 4 store HP damage 32000
(dead), 5 store 2..10 and 8 none.

**Crash Site cave (pinned Area profile).** 7 NPCs: six of prototype 28373
(28310.., monster art, AI row 19, faction 19, level 1, KOS, WANDERS_IN_DARK,
ANIMAL, damage 1–3, no gear) and one human 17108 (AI row 0, level 10, HP
damage 32000: dead). The cave's weapons and ammunition lie in containers, not
on the creatures.

Rows used here (columns 0..16): row 19 `0 1000 1000 101 1 20 1001 1000 2 0 0
2001 100 50 100 0 0`; row 14 `0 10 100 15 6 1000 1000 1000 2 0 0 2001 100 20 0
1 1`; row 15 `0 100 100 20 7 1000 1000 1000 2 0 0 2001 100 20 0 5 1`; row 0
`0 1000 1000 20 6 20 1001 1000 2 0 0 2001 100 50 20 1 1`; row 5 as in the
[consent research](virgil-follow-consent.md).

## 7. First-fight candidates (plan slice I10)

Proposal, ranked. All three need slices I1 (row), I4 (KOS and faction), the
awareness and approach of A2/A6 and the existing attack resolver with an NPC
damage table; none needs weapon choice, spells, items or grenades. All three
also meet the back-off a kill starts: when the PC kills an NPC, every NPC
within 3 tiles of the PC that shares the victim's faction, lacks `NO_FLEE` and
is focused on the PC gets the backing-off flag (kill hook (0x4AA1B0)), and a
backing-off NPC runs away before anything else, to 5 cells when column 15 is
at most 1 (perform (0x4AC180), back off (0x4AC7B0)). No creature here
has `NO_FLEE`, so a fight against two or more of them needs back-off (S15, A4)
or must record it as a known deferral.

1. **One exterior creature of prototype 28359** (any of the 10 core
   instances). Row 19 never flees by the plan's reading (column 0 is 0,
   columns 1 and 2 are 1000, column 3 is 101), never starts keeping distance on
   its own (column 15 is 0) and never opens portals (column 16 is 0); KOS with
   faction 19; level
   1, normal damage 1–3 from its own table; no weapon, item or college. It is
   the smallest decision surface in the area and the most common creature of
   the start (13 instances, plus 3 of the similar 28447 on the same row). A
   single opponent avoids the kill back-off; one picked near other faction-19
   creatures does not.
2. **The cave pack: six of prototype 28373.** The same row, faction and damage
   as candidate 1 in a closed interior that already loads through the cave
   profile (ARC-WORLD-017), with several hostile actors at once: turn order
   with more than one AI critter (ARC-COMBAT-003), enemy lists and Virgil
   joining the fight. Killing one of the pack makes the others nearby back
   off, so this candidate needs back-off. The natural second step after
   candidate 1.
3. **One of the three exterior creatures of prototype 27356.** Row 14 has
   other morale values (flee when the other party is larger by 10, which no
   Crash Site party reaches, and no flee while the target is below 15 % HP),
   may open portals (column 16 is 1) and still backs off to 5 cells after a
   kill nearby (column 15 is 1); a level-3 creature with damage 3–8 and the
   monster rather than animal flag. It checks that a second row and a harder
   opponent work before any weapon or spell slice.

Not first: **27359** (level 12, row 15 with a 5-cell back-off, a wielded
weapon, `CHECK_WIELD` and a magick college) needs back-off, weapon wielding
and possibly spell lists (I8); **Virgil** is a follower (A7); the injured or
dead humans have row 0 and no kill-on-sight flag.

## 8. Consequences (project policy proposals)

1. **Read AI rows by number by default.** The shipped tables have no gap, so
   the by-number reader proposed in load-map policy 3 and the Original
   positional mode give the same rows here; the Original mode only matters for
   modules with gaps.
2. **`spelllist.mes` needs no tolerance for unknown or reordered keys in the
   shipped data**, but the strict reader should still refuse them (load-map
   policy 2), since a mod may have them.
3. **`EffectRules` can admit `*` and `/` as the original reads them** (number
   after the operator, ledger LM9 and SH-07): five shipped effects are refused
   today. That is a separate data task, not part of the AI slices.
4. **The Steamwoven content snapshot can keep reading init files from the
   base** (load-map policy 1): only `rules\unique.txt` has a module copy, and
   it is unused while every prototype exists.
5. **I10 can start with candidate 1** once I1 and I4 exist (back-off waits
   until a fight has two or more creatures); LM1 and LM2 need no decision
   before I10, because no Crash Site NPC holds two weapons.

## 9. Critical charts and body types (ARC-COMBAT-009)

A second probe mode counts what the original's critical-effect procedures read
([ledger CE1–CE15](original-verification-ledger.md#critical-effects-arc-combat-009)):
every critter's critical-hit body type (field 227) and every weapon's critical
hit and miss charts (fields 129 and 132), for prototypes and for every map's
mobiles by their effective fields, through the module content stack.

```bash
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --critical-census --game-root <game folder> [--module <name>]
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --critical-census-self-test
```

On the owner's installation (2026-10-21, about 1 s, SHA-256
`d1f2ff409d23325809de45ed06582a581f260ac783d3cb84c47222d68c11c923`):

| Count | Prototypes | Map mobiles |
| --- | --- | --- |
| PC body types | 13 × 0 | none |
| NPC body types 0 / 1 / 2 / 3 / 4 / 5 | 391 / 41 / 28 / 23 / 7 / 7 | 2,677 / 619 / 158 / 216 / 3 / 119 |
| Undead NPCs by body type | 49 × 0 | 550 × 0 |
| Fatigue-immune NPCs by body type 0 / 2 / 3 | 48 / 17 / 6 | 539 / 86 / 22 |
| Weapon hit charts 0 / 1 / 2 / 3 / 4 | 51 / 28 / 66 / 2 / 3 | 620 / 217 / 881 / 0 / 0 |
| Weapon miss charts 0 / 1 / 2 / 3 / 4 / 6 / 7 / 8 | 54 / 27 / 14 / 36 / 12 / 2 / 3 / 2 | 982 / 216 / 162 / 119 / 235 / 0 / 4 / 0 |

115 prototypes are of types the combat-field reader does not read (walls,
portals, containers, scenery, traps). So the hero's body type is 0, every body
type of the procedures occurs, and the undead are all of the knockdown body type.

## Unknowns

- **U1** Column meanings beyond the plan's reading stay with stage A1; the
  table above only fixes the numbers. The label text in `ai_params.mes` that
  the ledger lists for P7 was not read (text is out of scope here).
- **U2** The LM1 estimate ignores skill effectiveness, magic adjustments and
  ammunition, and it counts carried weapons only; an exact answer needs the
  wield rules over each NPC's stats.
- **U3** Not counted: NPC skill values and the skill uses in dialogs and
  scripts; most of the magic research's [census additions](magic-core-evidence.md#data-census-additions)
  (component types per action, `Dmg_Flags:`, summon lists and factions,
  `No_Stack:` and the cancel sets, `No_Resist:` combined with maintenance or
  duration, item mana stores and charges). This note answers its key counts
  per record range and the Crash Site and cave college ranks and masteries.
  The spell-record part (component types per action, `Dmg_Flags:`, summons and
  factions, `No_Stack:` and the cancel sets, `No_Resist:` with maintenance or
  duration, records 80..139) is answered by ARC-MAGIC-002 in the
  [spell record contract](../formats/spell-records.md#installed-original);
  item mana stores and charges remain open.
- **U4** Only module `Arcanum` was measured; `Vormantown` was not.

