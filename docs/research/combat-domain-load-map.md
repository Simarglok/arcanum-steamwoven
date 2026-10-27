# Combat-domain load map: what the game loads and runs

Status: **Research**, work item ARC-RES-040 (2026-10-16), the source-reading half of stage
A0 of the [NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(owner decision D7). This document fixes, for combat, NPC AI, skills, magic and
technology, which modules the game starts and in what order, every
data file and prototype field these modules read, in which phase and how,
which module owns the resulting state at runtime and what reaches a save. It
changes no production API, reader, viewer, session schema or dialog port. The
same census over the owner's original data (what those files actually hold,
plan stage A9, and the first real fight candidates) is a separate follow-up
task on the Mac; [section 10](#10-original-data-census-next-task) lists what it
must count.

Requirements: [FR-AI-001](../requirements/story-social-ai.md#fr-ai-001),
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002),
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001) and the
FR-MAGIC rows of [combat-magic-tech](../requirements/combat-magic-tech.md),
where they depend on loaded data; [FR-CONTENT-001](../requirements/content-product.md#fr-content-001)
for the module layer.

## Vocabulary and boundary

- **Source fact**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a row of the
  [original verification ledger](original-verification-ledger.md) is cited.
  Unnamed functions keep decompiler names (`sub_…`); a reading of their intent
  is marked as such.
- **Anomaly**: a branch that looks like a slip. It is
  recorded under [section 8](#8-anomalies-found-by-this-reading), never
  adopted silently; under D2 its stage decides the fix and logs it in the
  plan's [anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions).
- **Project policy**: a choice this project makes where the source leaves a
  runtime decision open. Every policy in [section 9](#9-consequences-for-steamwoven-project-policy-proposals)
  is a **proposal**.

No original file, probe or game data was read for this work item. Original
executable parity remains **NOT_VERIFIED**. "Base" means the `arcanum*.dat`
archives and the `data` folder; "module" means `Modules\<name>.dat`, its
`.patch0`..`.patch9` files and the `Modules\<name>` folder (the default module
is `Arcanum`). The physical precedence between these copies is already
specified in [content precedence](content-precedence.md) (ARC-DATA-011) and is
not repeated here.

## Answer

1. **Two load phases, and the phase decides what a module can override.** At
   start the game mounts only the base and runs every module's `init`
   (init (0x4020F0), base mount (0x404A20)). The module is mounted later, in
   `mod_load` (mod load (0x402A50), module mount (0x404C10)). So every
   file read in `init` comes from the base only; a module cannot replace it.
   Module-aware data in or next to the combat domain: AI rows 100..149
   (`rules\gameai.mes`), effects 0..49 (`rules\gameeffect.mes`), item effect
   texts from 30000 (`mes\gameitemeffect.mes`), blessings and curses
   (`mes\gamebless.mes`, `mes\gamecurse.mes`), reputations
   (`rules\gamerep.mes` and its texts), descriptions from 30000
   (`mes\gamedesc.mes`), object and faction names merged over the base
   (`oemes\gameoname.mes`, `oemes\gamefaction.mes`), scripts 1..999,
   `mes\storystate.mes` and `Rules\MapList.mes`.
2. **Spells, technologies, skills, stats, levels, combat texts, backgrounds,
   inventory sources, traps and all prototypes are base-only and fixed for the
   process.** None of them has a `mod_load`; prototypes are read eagerly once,
   inside Map `init`, before any module exists (proto init (0x4681B0)).
3. **Load order matters in two places.** MagicTech before MT-AT and Spell
   (they read its spell table and `spell.mes`); Map (with Proto inside it)
   before MagicTech-Post (summon prototypes) and Inven-Source (prototype
   checks). `spelllist.mes` is therefore read in two passes, one on each side
   of Map (spell list (0x44FE30, 0x44FFA0, 0x450090, 0x4501D0)). Bless and Curse only load
   texts; they reach Effect at run time, not at load.
4. **The AI owns 150 parameter rows and nothing else on disk.** Rows load
   positionally, not by entry number: the first entry must be 0 (base) or 100
   (module), and each next entry fills the next row whatever its number
   (AI init (0x4A8320), AI module (0x4A8400)). An NPC selects its row by
   `OBJ_F_NPC_AI_DATA` without a bounds check (row (0x4AAA60)).
5. **Spells carry the AI's action priorities.** Line +4 of every
   `spelllist.mes` record holds ten `AI_*` values; MT-AT builds ten lists
   sorted by them, descending, at `init` (MT-AT (0x4CC1F0, 0x4CC210, 0x4CC220, 0x4CC240, 0x4CC270, 0x4CC2A0, 0x4CC2D0, 0x4CC310, 0x4CC350, 0x4CC380)). Items join the
   same choice in two ways: by `OIF_VALID_AI_ACTION` with a matching
   `OBJ_F_ITEM_AI_ACTION`, or, with spell mana left, through any of their five
   spells whose `AI_*` value for that action is set; a hexed wielded weapon is
   the only item considered, and NPCs with a leader skip unidentified items
   other than food and scrolls (MT-AT items (0x4CC810, 0x4CC8A0, 0x4CC930, 0x4CCA30)).
6. **Persistent combat-domain state lives in objects, not in modules.** Skills,
   stats, spell colleges, tech degrees, effects, blessings, reputations,
   danger flags, focus, enemy list and AI row are object fields saved with the
   objects. Only seven module blobs touch this domain: Combat (turn state), Skill
   (next gambling roll), MagicTech-Post (active spells), Script (globals and
   flags), TimeEvent (timers), Anim (running animations) and MonsterGen
   (spawn counters).
7. **AI timers are never saved.** The AI time event is marked not saveable
   (event type table 0x5B2188, ledger OQ2); every NPC gets an immediate AI event again when
   its sector's objects are listed (reschedule (0x4AD730, 0x4AD790), sector (0x4F1CD0)).
   Combat-focus wipes and NPC "wait here" timers are saved.
8. **The parsers are strict in the wrong way.** A missing `.mes` line yields a
   shared missing-line message rather than a failure
   (mes (0x4D43A0)); an unknown flag or list token in a named-key line ends the
   process; named keys are case-sensitive and must appear
   in the expected order, or they are skipped silently.
9. **Much of the combat domain is code, not data.** Critical tables, hit
   locations, dodge criticals, material damage reduction, skill caps, training
   thresholds, tech degree tables, reaction race table, trap encodings and
   the action-point and attack-cost formulas are constants
   ([section 7](#7-rules-that-are-code-not-data)).
10. **This reading adds 20 anomalies (LM1..LM20)**, two of them in NPC
    weapon choice (LM1, LM2), one in AI row loading (LM3) and one in spell
    willpower (LM6) that phase 1 slices must decide.

## 1. Life cycle

The game library holds one table of 63 modules, each with up to ten hooks:
init, reset, mod_load, mod_unload, exit, ping, update_view, save, load and
resize. Map holds a nested table of 17 sub-modules,
among them Effect and Proto, run by Map's own hooks
(map init (0x40EA90), map mod load (0x40ED10)).

| Phase | When | Order | On failure |
| --- | --- | --- | --- |
| init | process start, base mounted | table order | exits the modules before the failed one; the failed one's exit is not called (init (0x4020F0)) |
| mod_load | module chosen (`-mod:`, main menu, options, multiplayer) | table order | unloads the modules before the failed one, unmounts the module (mod load (0x402A50)) |
| reset | new game or load | table order | never fails; clears `Save\Current` first (reset (0x402380)) |
| save | save game | table order | `Save\Current\data.sav`: version 25, then each module blob followed by `0xBEEFCAFE`; a failure while writing it deletes it (save (0x403030)) |
| load | load game | table order | checks each sentinel; a failure stops without rollback; the version is stored, not checked (load (0x403410)) |
| mod_unload, exit | module change, quit | reverse order | none (mod unload (0x402C60)) |

`mod_load` does four things in order (mod load (0x402A50)): unload the
previous module, mount the module, empty `Save\Current`, and (outside the
editor) rebuild one mobile cache per map folder whose stored module GUID
differs (mobiles (0x4102F0, 0x4106D0)); only then does it call every module's `mod_load`.
Map's own `mod_load` first reads `Rules\MapList.mes`, then Effect's and
O_Name's (map mod load (0x40ED10)).

### Combat-domain modules

Modules are listed in the order of the game library's module table
(game library functions 0x4020F0–0x405070); "per game" means cleared by
`reset` or rebuilt by `load`.

| Module | Hooks | Reads | Runtime state | Saved |
| --- | --- | --- | --- | --- |
| Item-Effect | init, mod_load, mod_unload, exit | `mes\item_effect.mes`; module `mes\gameitemeffect.mes` | text tables | no |
| Critter | init, exit | `Rules\xp_critter.mes`, `mes\critter.mes` | XP worth by level, texts | no |
| Script | init, reset, mod_load, mod_unload, exit, save, load | `Rules\ScriptEyeCandy.mes`; module `mes\storystate.mes` | 100-entry script cache, 2000 globals, 3200 flag bits, story state | yes |
| MagicTech | init, reset, exit | `Rules\magictech.mes`, `mes\spell.mes`, `Rules\SpellEyeCandy.mes`, `Rules\spelllist.mes` pass A | 223 spells, 25 component names, 512 run slots | via MagicTech-Post |
| MT-AT | init, reset, exit | none | ten sorted action lists, 40-entry per-critter cache | no |
| MT-Item | init, exit | none | 26 item triggers | no |
| Spell | init, exit | `mes\spell.mes` through MagicTech | college and spell texts, icons | no |
| Stat | init, exit | `mes\stat.mes` | texts | no |
| Level | init, exit | `rules\xp_level.mes`, `rules\gamelevel.mes`, `rules\userlevel.mes`, `mes\gamelevelname.mes`, `mes\userlevelname.mes`, `mes\level.mes` | XP table, auto-level schemes | no |
| Map › Effect | init, mod_load, mod_unload, exit | `rules\effect.mes`; module `rules\gameeffect.mes` | six effect tables of 400 | no |
| Map › Proto | init, exit | every `proto\*.pro`; generates missing ones | permanent prototype pool | no (prototypes are written to disk only when generated) |
| MagicTech-Post | init, save, load | `Rules\spelllist.mes` pass B | active spell runs | yes, plus per map `MT.dat` |
| Skill | init, exit, save, load | `mes\skill.mes` | next gambling roll | yes |
| Item | init, exit, resize | `mes\item.mes` | texts | no |
| Combat | init, reset, exit, save, load | `mes\combat.mes`, `Rules\CombatEyeCandy.mes` | turn-based flags, turn, action points, critter list | yes |
| TimeEvent | init, reset, exit, ping, save, load | none | timer lists for three clocks | yes, saveable types only |
| Bless, Curse | mod_load, mod_unload | module `mes\gamebless.mes`, `mes\gamecurse.mes` | texts and effect numbers | no |
| AI | init, mod_load, mod_unload, exit | `rules\ai_params.mes`; module `rules\gameai.mes` | 150 parameter rows | no |
| Anim | init, reset, exit, save, load | `Rules\AnimEyeCandy.mes`, `Rules\WeaponEyeCandy.mes` | 216 animation runs | yes |
| Tech | init, exit | `mes\tech.mes` | texts | no |
| Background | init, exit | `rules\backgrnd.mes`, `mes\gameback.mes` | background table | no |
| Reputation | init, mod_load, mod_unload, exit | module `rules\gamerep.mes`, `mes\gamereplog.mes`, `mes\game_rp_npc_*.mes` | 1000 records | no |
| Reaction | init, exit | `mes\reaction.mes` | texts | no |
| Inven-Source | init, exit | `Rules\InvenSource.mes`, `Rules\InvenSourceBuy.mes` | inventory sets | no |
| Trap | init, exit | `Rules\TrapEyeCandy.mes`, `mes\trap.mes` | texts, effects | no |
| MT-Obj-Node | init, exit | none | node pool for spell runs | inside MagicTech-Post records |
| MonsterGen | init, reset, exit, save, load, resize | none | 256 spawn counters | yes |
| Party | init, reset, exit | none | 8 multiplayer slots | no: save and load exist but are not registered (party (0x4B9E50, 0x4B9EC0)) |

Fate is not a module and reads nothing; critical hit and miss types are enums
only, and every critical table is code in the combat functions.
Description and O_Name (Map sub-module) supply the names and
descriptions of [section 5](#5-prototypes-and-object-fields).

## 2. Where each file comes from

The mount order and the loose-over-archive rule are in
[content precedence](content-precedence.md). What this reading adds is
the time axis:

- **Init files see the base only.** `gamelib_init` mounts `arcanum*.dat` and
  `data` (base mount (0x404A20)) and then runs every `init`. Nothing a module
  ships can change a spell, a skill table, a level table, a prototype or the
  base AI rows 0..99.
- **Module files are read after the module is mounted**, and only by the
  hooks listed in the Answer, item 1. Each of them keeps base and module
  entries apart by number range (AI 0..99/100..149, effects 50..399/0..49,
  texts below/from 30000, scripts 1000+/1..999) instead of letting the module
  replace base entries.
- **A module change does not reload init data.** `mod_unload` drops only the
  module tables; AI's unload does nothing at all, so rows 100..149 of the
  previous module stay until the next module overwrites them
  (AI unload (0x4A84C0)).

## 3. Data file catalogue

"Pos." means entries are read in file order, "num." by entry number; "req."
means a missing file fails that phase.

| File | Module, phase | Keys | Req. | Content |
| --- | --- | --- | --- | --- |
| `rules\ai_params.mes` | AI, init | pos. from 0, rows 0..99 | yes | 17 integers per row (init (0x4A8320)) |
| `rules\gameai.mes` | AI, mod_load | pos. from 100, rows 100..149 | no | same (module (0x4A8400)) |
| `Rules\magictech.mes` | MagicTech, init | num. 10..34 | yes, each | 25 component type names for `Type:` (init (0x44EF50, 0x44F150, 0x44F160)) |
| `mes\spell.mes` | MagicTech + Spell, init | num. 0..222, 500..515, 700..779, 8000..8015, 9000..9015 | yes | spell names, college names, spell, college and mastery descriptions (spell (0x4B1440)) |
| `Rules\spelllist.mes` | MagicTech init (A), MagicTech-Post init (B) | num. base 1000+50·n (n<140) or 3000+50·n (n 140..222), offsets +0..+48 | yes | 223 spell records ([section 4.2](#42-spell-records)) |
| `Rules\SpellEyeCandy.mes` | MagicTech, init (not editor) | num. groups of 10 | yes | spell visual effects (init (0x44EF50, 0x44F150, 0x44F160)) |
| `mes\tech.mes` | Tech, init | num. 0..89 | yes | 8 discipline names, 8 degree names, descriptions, two messages (tech (0x4AFCD0)) |
| `rules\effect.mes` | Effect, init | num. 50..399 | yes | effect definitions (init (0x4E99F0)) |
| `rules\gameeffect.mes` | Effect, mod_load | num. 0..49 | no | module effects (module (0x4E9BB0)) |
| `mes\item_effect.mes`, `mes\gameitemeffect.mes` | Item-Effect, init / mod_load | num.; module from 30000 | base yes | item effect texts (item effect (0x4D3F60, 0x4D3FE0, 0x4D3FF0, 0x4D4050, 0x4D4080)) |
| `mes\gamebless.mes`, `mes\gamecurse.mes` | Bless, Curse, mod_load | num. id·10 + 0 name, 1 effect, 2 text; id ≥ 50 | no | blessings and curses (bless (0x4C4110, 0x4C4130, 0x4C4150, 0x4C4170)) |
| `mes\combat.mes` | Combat, init | num. 0..24 | yes | combat texts (combat (0x4B1D50)) |
| `Rules\CombatEyeCandy.mes` | Combat, init (not editor) | num. from 0 to first gap | yes | effects per reaction type (combat (0x4B1D50)) |
| `mes\skill.mes` | Skill, init | num. 0..35 | yes | 12 basic and 4 tech skill names and descriptions, 4 training names (skill (0x4C5BD0, 0x4C5D50)) |
| `mes\stat.mes` | Stat, init | num. 0..40, 500..527 | yes | 28 stat names and short names, genders, 11 races (stat (0x4B0340)) |
| `rules\xp_level.mes` | Level, init | num. 1..51 | yes | XP per level; level 1 forced to 0 (level (0x4A6620)) |
| `rules\gamelevel.mes`, `rules\userlevel.mes` + name files | Level, init | schemes 50..199 game, 1..49 user | yes | auto-level schemes (schemes (0x4A6DE0, 0x4A6E40)) |
| `Rules\xp_critter.mes` | Critter, init | num. = NPC level | yes | XP worth (critter (0x45CF30)) |
| `mes\critter.mes` | Critter, init | num. 0..24 | yes | social classes, encumbrance texts |
| `rules\backgrnd.mes`, `mes\gameback.mes` | Background, init | blocks of 10 | yes | effect, condition, money, starting items; texts from 1000 (background (0x4C2270, 0x4C22D0, 0x4C2300, 0x4C2390, 0x4C2420)) |
| `mes\reaction.mes` | Reaction, init | num. 0..6 | yes | seven reaction names (reaction (0x4C0BD0)) |
| `rules\gamerep.mes` + `mes\gamereplog.mes`, `mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes` | Reputation, mod_load | num. 1000..1999 | no | reputation effects, names, greetings (reputation (0x4C1620)) |
| `Rules\InvenSource.mes`, `Rules\InvenSourceBuy.mes` | Inven-Source, init | num. contiguous from 1 | first yes | loot and shop sets of up to 300 rate/prototype pairs (sets (0x4BF390, 0x4BF510, 0x4BF5D0, 0x4BF640)) |
| `Rules\TrapEyeCandy.mes`, `mes\trap.mes` | Trap, init | num. 10k+0..2 | yes | trap effects and texts (trap (0x4BBD00)) |
| `mes\item.mes` | Item, init | num. | yes | ammo and armour coverage names (item (0x460E70)) |
| `Rules\AnimEyeCandy.mes`, `Rules\WeaponEyeCandy.mes` | Anim, init (not editor) | num. | yes | animation and weapon effects (anim (0x421B00)) |
| `proto\*.pro` | Proto, init | file name number | generated if missing | every prototype (proto (0x4681B0)) |
| `rules\npc.txt`, `rules\monster.txt`, `rules\unique.txt` | Proto, init, only for generated prototypes | `Description: N` records | — | critter defaults (rules text (0x49B340)) |
| `oemes\oname.mes`, `oemes\gameoname.mes` | O_Name, init / mod_load (merged) | num. | base yes | object names (oname (0x4E72B0, 0x4E72F0, 0x4E7320, 0x4E7390)) |
| `oemes\faction.mes`, `oemes\gamefaction.mes` | O_Name, init / mod_load (merged) | num. | base yes | faction names; NPC faction is an AI input (oname (0x4E72B0, 0x4E72F0, 0x4E7320, 0x4E7390)) |
| `mes\description.mes`, `mes\gamedesc.mes` | Description, init / mod_load | below / from 30000 | base yes | descriptions (description (0x4D4210)) |
| `scr\*.scr` | ScriptName, init / mod_load; Script on demand | file number 1000+ base, 1..999 module | — | scripts, cached on first use (names (0x4CEC10, 0x4CECC0, 0x4CECE0), cache (0x44C390, 0x44C450, 0x44C480, 0x44C4B0, 0x44C4E0)) |
| `mes\storystate.mes` | Script, mod_load | num. | no | story state texts (script (0x4446E0, 0x4447D0, 0x444830, 0x444890, 0x4448B0, 0x4448D0, 0x444930, 0x444990)) |

## 4. Formats

### 4.1 Shared parsing rules

- **Message files.** A loaded file is sorted by entry number and searched by
  binary search. A missing number does not fail: it returns one shared static
  missing-line message naming the number and the path (mes (0x4D43A0)). A missing or
  empty file fails `mes_load`. "Next entry" walks by position, whatever the
  numbers (next (0x4D4460)).
- **Named keys** (`Key:` value lines, used by `spelllist.mes` and the eye
  candy files). A key must start exactly at the cursor after whitespace, the
  match is a case-sensitive prefix, and values end at `,`. A key out of the
  expected order is skipped without advancing.
- **Value forms.** `(a@b)` pairs, with `b = a` when `@` is absent; a missing
  `(` ends the process. Flag lists separate names by
  spaces or `|`, case-insensitively; any unknown name ends the process.
  `a-b` ranges.
- **Other formats** are tokenised by hand per module: whitespace integers (AI
  rows), `attribute operator` pairs (effects), `token value,` pairs (level
  schemes), `name: min max, rate proto, …` (inventory sources) and digit-only
  scanners (reputations).

### 4.2 Spell records

Each of the 223 records occupies up to 49 numbered lines from its base
number (load (0x44FE30, 0x44FFA0, 0x450090, 0x4501D0)). Before parsing, every spell gets defaults: minimum
willpower {6, 9, 12, 15, 18}[n mod 5], range 99, no resistance, no duration
stat, duration modifier 1, no target (defaults (0x457580)). For n ≥ 80
the willpower minimum is forced to 0; for n ≥ 140 the spell is forced to
technology.

| Line | Pass | Required | Content |
| --- | --- | --- | --- |
| +0 | A | always read; a missing line yields the error text, which matches no key, and init still succeeds | `AoE:` over 65 target names, `AoE_SF:`, `AoE_NO_SF:`, `Radius:`, `Count:`, then the same per action `[Begin]`, `[Maintain]`, `[End]`, `[Callback]`, `[EndCallback]`; "not invulnerable" is added except for spells 1, 2 and 9 |
| +1 | A | as +0 | `IQ:` (used as minimum willpower, LM6), `Cost:`, `Maintain:(cost@period)`, `Duration:(period@stat)` and `DurationStatInfo:`, `DurationTriggerCount:`, `Resist:(STAT@value)`, `Range:`, `ChargesFromCells:`, `ChargeBeginCost:`, `Info:` (neutral, friendly, aggressive, may be aggressive), `Disabled:` |
| +2 | A | no | casting animation, missile, caster and target effects per action, `Is_Tech:` |
| +3 | A | no | `No_Stack:`, spell flags it cancels or forbids, `Disallowed_TCF:` critter flags, `ItemTriggers:` |
| +4 | A | no | `AI_Flee:`, `AI_Summon:`, `AI_Defensive:(value@second)`, `AI_Offensive:`, `AI_HealingLight:`, `AI_HealingMedium:`, `AI_HealingHeavy:`, `AI_CurePoison:`, `AI_FatigueRecover:`, `AI_Resurrect:`; absent values are −1; `No_Resist:` and `No_Reflect:` act by presence |
| +5..+48 | B | stops at first gap | one effect component per line: action, area keys, `Type:` (one of 25) and a type payload: animation goal, AI redirect (critter flag, `MinIQ:`), cast, charge-and-branch, damage or heal (`DmgType:`, `Dmg:a-b`, `Dmg_Flags:`), effect add or remove (`Cause:`, `Scaled:`), environment or object flag, eye candy, interrupt, movement, recharge, summon (`Proto:` resolved immediately, `Clear_Faction:`, `Palette:`, `List:`), test-and-branch, trait setters |

Lines +0..+4 and the component lines were read in the executable by ARC-MAGIC-001 and ARC-MAGIC-002 (two readers each,
[ledger](ledger/magic.md#spell-components-arc-magic-002); format in the
[spell record contract](../formats/spell-records.md)); the rows below stand, with the ledger's detail.

Pass B runs after Map because `Proto:` needs the prototype pool
(parse (0x457650, 0x4578F0, 0x457B20, 0x457D00, 0x457E70, 0x458060)); everything the AI and item triggers read is complete
after pass A. Summon lists 0..2 are weighted random, 3..5 chosen by aptitude
threshold, from tables in code.

### 4.3 AI parameter rows

Each row has 17 whitespace-separated integers; missing trailing values keep
the row's previous value or zero (init (0x4A8320)). The
first entry must be number 0 (base) or 100 (module); if it is absent, nothing
loads. Later entries fill rows by position until row 100 or 150 or the end of
the file, so a gap in numbering shifts every later row (LM3). The 17th value
says whether the NPC may open portals. The field meanings are stage A1's
work (plan I1). Two related tables are code: "wait here" durations {0,
15 min, 2 h, 1 day, 7 days, 30 days} by `OBJ_F_NPC_WAIT`
and loudness radii {2, 8, 15}.

### 4.4 Effects

An effect line is up to seven `attribute operator` pairs (a comment of the
earlier reading says five) (parse (0x4E9C20)). Attributes are stats, basic skills, tech
skills, resistances, tech disciplines and ten specials (max HP, max fatigue,
reaction, critical chances and effects, reaction adjustments, XP gain).
Operators are `min N`, `max N`, `+N`, `-N`, `+N%`, `*N`, `/N`. They apply in
the order percent, multiply, divide, add, min, max; the "no innate" variant
skips race, background, class and gender causes (apply (0x4EA6C0)).
An object stores applied effects as two parallel arrays (number and cause).

### 4.5 Other tables

- **Level schemes**: `token value,` pairs over 8 stats, 16 skills, 16
  colleges, 8 disciplines, max HP and max fatigue; spell = college·5 + level;
  after its own scheme an NPC continues with scheme 70 (schemes (0x4A6DE0, 0x4A6E40)).
  Virgil's replay already uses this ([ARC-RES-032](virgil-level-replay.md)).
- **Backgrounds**: per background `b`, entries 10b+0 text number, +1 effect,
  +2 race/gender condition (`<RACE><G>`, `ANY` or `NPC`), +3 money, +4 starting
  item prototypes; the list ends at the first missing +2 (background (0x4C2270, 0x4C22D0, 0x4C2300, 0x4C2390, 0x4C2420)).
- **Inventory sources**: `name: min max, rate proto, …`, contiguous from 1;
  the buy file lists prototypes or `ALL`; every prototype is validated at init
  (sets (0x4BF390, 0x4BF510, 0x4BF5D0, 0x4BF640)).
- **Reputations**: three faction numbers, then up to five `adj origin faction`
  effects; after a signed adjustment the digit-only scanner re-reads the same
  number, so origin and faction become equal to the adjustment (already
  recorded as anomaly P1 with owner decision D1 in
  [ARC-RES-028](npc-reaction-reputation-evidence.md)) (parse (0x4C1730)).
- **Blessings and curses**: name, effect number and text per id; ids below 50
  are refused, and id 100 would collide with the UI message 1000, so 50..99 is
  the usable range (inference) (bless (0x4C4110, 0x4C4130, 0x4C4150, 0x4C4170)).

## 5. Prototypes and object fields

**Loading.** Proto is a Map sub-module, so it runs at `init`, before any
module (proto init (0x4681B0)). It lists `proto\*.pro`, takes the number in
each file name, generates every missing number of every type range with
defaults from code and writes it back as `proto\NNNNNN - <name>.pro`, then
reads **all** prototype files into a permanent pool. There is no on-demand
load and no reload at `mod_load`. A prototype's ID is its description number
+ 20.

**Inheritance.** An instance stores only the fields it overrides; any other
field is read from its prototype, resolved lazily by ID
(inherit (0x408F40)). Which field groups a type carries is fixed per type
(valid (0x40C260)).

**Ranges** (description numbers, end exclusive):
walls 1000..1008, portals 2008..2023, containers 3023..3061, scenery
4006..4051, projectiles 5028..5029, weapons 6029..6179, ammo 7038..7042,
armour 8042..8293, gold 9056..9057, food 10057..10145, scrolls 11059..11143,
keys 12060..12063, key rings 13061..13062, written 14062..14130, generic
15065..15213, PCs 16066..16079, NPCs 17067..17317, traps 26000..26008, and two
critter ranges 27309..27394 and 28310..28472 whose labels conflict (LM15).

**Generated defaults.** Non-critter defaults are code
(defaults (0x468930)). Critters use code for unique cases, otherwise a
`rules\*.txt` record: `Key: value` lines with case-insensitive prefixes over
37 keys, one flag per line, `Basic Stat` name and value, `Spell` names,
`Script` with its counters, damage `min max`, `Hit Points` as a target maximum
(format (0x49BB70)). XP worth and pool then come from
`Rules\xp_critter.mes`. This path runs only for prototypes missing from disk;
the shipped `.pro` files are what the game normally reads (to be confirmed by
the census).

### Combat-relevant fields

| Carrier | Fields |
| --- | --- |
| Every object | AC, HP points, adjustment and damage, material, five resistances (normal, fire, electrical, poison, magic), scripts by attachment point, flags, spell flags, name, description |
| Critter | critter flags and flags 2, 28 base stats, 12 basic and 4 tech skills (bits 0..5 points, 6..7 training), spell/tech array (0..15 college levels, 16 mastery, 17..24 tech degrees), fatigue points, adjustment and damage, critical body type, effects and causes, fleeing-from, ammunition handles, inventory and inventory source, followers, death time, auto-level scheme |
| NPC | NPC flags (fighting, kill on sight, wait here, spread out, aloof, cast highest, look for weapon/armour/ammo, backing off, no attack, check grenade and others), leader, **AI row**, combat focus, who hit me last, XP worth and pool, origin, faction, social class, reaction base and per-PC memory, wait, generator data, ten monster damage values, enemy list, substitute inventory, waypoints and standpoints |
| PC | PC flags, fate flags, background, reputations, blessings, curses with timestamps |
| Item | item flags (valid AI action, needs target, throw, no ranged use), weight, worth, mana store, magic/tech complexity (sign), discipline, five spells and their mana store, **AI action**, effects text, slot, use animation |
| Weapon | weapon flags (loud, silent, two-handed, throwable, boomerangs, ignore resistance, damage armour and others), to-hit bonus, damage minimum, maximum and magic adjustment for five types (normal, poison, electrical, fire, fatigue), speed, range, minimum strength (each with its magic adjustment), ammo type and use, missile and visual effect, critical hit and miss charts, magic critical chances and effects |
| Ammo, armour, generic | ammo type and quantity; armour size and gender flags, AC and resistance adjustments, silent-move adjustment, unarmed bonus damage; generic flags (lockpick, trap device, healing item, grenade), usage bonus and count |

The AI finds grenades
(grenades (0x4AC910)) and healing items (healing (0x4AE570)) by
generic flags, and spell items as Answer item 5 describes
(MT-AT items (0x4CC810, 0x4CC8A0, 0x4CC930, 0x4CCA30)).

**Names and descriptions.** Names come from `oemes\oname.mes` plus the
module's `gameoname.mes` (oname (0x4E72B0, 0x4E72F0, 0x4E7320, 0x4E7390)); descriptions below 30000 from
`mes\description.mes`, from 30000 from the module's `mes\gamedesc.mes`
(description (0x4D4210)); item effect texts likewise
(item effect (0x4D3F60, 0x4D3FE0, 0x4D3FF0, 0x4D4050, 0x4D4080)).

### NPC weapon choice

`item_wield_best` (wield (0x465170)) is how an NPC picks gear, so it belongs to
this map. The current weapon is always unwielded before the search.
Eligibility (eligibility (0x464D20)) refuses polymorphed critters,
magical items for the magick-allergic background, broken art, the wrong slot,
fixed two-handed weapons with a shield or a crippled arm, any weapon when both
arms are crippled, any missing critter art for
the weapon type, and armour of the wrong size, gender or for unique art; it
does **not** check minimum strength. A weapon needs no ammo or enough ammo;
its score is average damage × skill effectiveness / 100, untrained skills
count −1 (melee always allowed), ties go to the higher handle. Other slots
take the highest worth. Two slips: LM1, LM2.

## 6. Runtime owners and persistence

| State | Owner at runtime | Persists |
| --- | --- | --- |
| Base and module tables (AI rows, spells, effects, levels, texts) | their module, process lifetime | not saved; rebuilt from files |
| Prototypes | Proto pool, process lifetime | `.pro` files, never in the save |
| Skills, stats, colleges, degrees, effects, blessings, curses, reputations, reaction memory, danger flags, focus, enemy list, AI row | the objects | object data in the save |
| Turn-based combat | Combat | module blob: flags and turn; when a round is active also action points and three object IDs; required AP is read back and discarded (save (0x4B1EA0, 0x4B2020)) |
| Active spells | MagicTech-Post | module blob of 512 run slots in extents (save (0x44F250, 0x44F3C0, 0x44F540)); per map `Save\Current\maps\<map>\MT.dat` when a map closes during a teleport, plus the teleport notification (map (0x44F7A0, 0x44FA70, 0x44FC30), close (0x45BCE0)) |
| Script globals, flags, story state | Script | module blob (script (0x4446E0, 0x4447D0, 0x444830, 0x444890, 0x4448B0, 0x4448D0, 0x444930, 0x444990)) |
| Timers | TimeEvent | module blob of saveable types per clock (save (0x45AED0, 0x45B050)); per map `TimeEvent.dat` on teleport (teleport (0x45C2F0, 0x45C500, 0x45C580, 0x45C610)) |
| Animation runs | Anim | module blob of 216 slots |
| Next gambling roll | Skill | module blob, one value |
| Spawn counters | MonsterGen | module blob of 256 bytes (save (0x4BA460, 0x4BA490)) |
| Followers | objects (leader, follower list) | object data; Party's own save is unregistered |
| Per-critter spell/item AI cache | MT-AT | not saved; cleared on reset (reset (0x4CC210)) and on teleport (teleport (0x459F50)) |

**Timers** (event type table 0x5B2188). Saved: animation, script, magic/tech, poison,
resting, fatigue, aging, turn-based combat, NPC wait here, lock, NPC respawn,
magic item recharge, body decay, item decay, combat-focus wipe, traps, scenery
respawn, random encounter and others. Not saved: **AI**, debug, background
and fidget animations, sleeping, main menu, light, multiplayer, multiplayer
control UI, UI and teleported. On map close, animation,
magic/tech, AI, combat, turn-based combat, world map and teleport timers are
cleared (map close (0x45BCE0)).

**AI restart.** Because AI timers are not saved, every non-destroyed NPC gets
an immediate AI event when its sector's objects are folded into the sector
list (sector (0x4F1CD0), object (0x441FC0), reschedule (0x4AD730, 0x4AD790)). A loaded game therefore resumes
AI from object fields alone.

## 7. Rules that are code, not data

The [combat core research](combat-core-evidence.md) already records the combat
formulas; listed here is only which constants have no file behind them, so the
loader does not look for one:

- **Combat**: hit location penalties (torso 0, head −50, arm and leg −30),
  random location (torso 70, leg 15, arm 10, head 5), dodge critical chance
  by training, material damage reduction for portals and containers, damage
  type to resistance (fatigue uses normal)
  (location (0x4B65A0)), weapon wear by material, critical ladders, action
  points `max(5, Speed)`, attack cost from weapon speed, perception range
  (formulas in the [combat core research](combat-core-evidence.md)).
- **Skills**: key stat per skill, training minimums 0/1/9/18, maximum level by
  stat, opposed-skill table, gambling costs, pick-pocket penalties, monster
  melee thresholds.
- **Stats**: limits and defaults, per-level cost, beauty to reaction, race
  maxima, background modifiers, derived formulas (derived (0x4B0740)).
- **Technology**: degree cost, level percentage {0, 10, 20, 35, 50, 65, 80,
  100}, minimum intelligence {0, 5, 8, 11, 13, 15, 17, 19}, schematic number
  1990 + 200·discipline + 10·degree.
- **Magic**: minimum level per spell rank {1, 1, 5, 10, 15}, college icons,
  spell price 100, dwarves pay double, mastery halves the cost and doubles
  the maintenance period (cost (0x4B1660, 0x4B16C0, 0x4B1740)); summon
  tables.
- **Reaction**: an 11×8 race table (0x5B684C, ledger R1 term). **Traps**:
  scripts 30000..30007, placed prototypes 26000..26007, disarm items
  (trap (0x4BBD00)). **Spawners**: rate intervals.
- **Items**: race to armour size table (wield (0x465170)).

## 8. Anomalies found by this reading

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable (magic, skills and AI sections of the ledger); the verdicts in the [verification ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040) supersede the earlier readings below where they differ.

Recorded, not adopted. "Inference" marks a reading of intent.

| ID | Where | Source behaviour | Likely intent (inference) |
| --- | --- | --- | --- |
| LM1 | weapon choice (wield (0x465170)) | the damage loop assigns instead of adding, so only the last damage type (fatigue) scores | sum over all five types |
| LM2 | weapon choice (wield (0x465170)) | with a target, the best in-range weapon's score is never stored, so the **last** in-range weapon overrides the best | best in-range weapon |
| LM3 | AI rows (init (0x4A8320)) | rows fill by position, so a numbering gap shifts later rows; the row index is not bounds-checked (row (0x4AAA60)) | rows by entry number; refuse bad rows |
| LM4 | AI unload (unload (0x4A84C0)) | module rows 100..149 survive a module unload | clear them |
| LM5 | spell minimum intelligence (iq (0x4502E0, 0x4502F0)) | always 5 | per spell, or unused |
| LM6 | spell `IQ:` (iq (0x4502E0, 0x4502F0)) | stored and used as minimum **willpower** | name mismatch only, or a slip; stage A5 decides |
| LM7 | item spells (MT-Item (0x4CB790)) | item spell becomes a spell number by `& 0xFF`, so "none" (10000) becomes 16 (Stone Throw) | the earlier reading's comment takes it as deliberate (every item can be thrown); low confidence that it is a slip |
| LM8 | combat load (save (0x4B1EA0, 0x4B2020)) | required action points are saved, read back and then set to 0 | restore them, or stop saving them |
| LM9 | effects (parse (0x4E9C20)) | `*N` and `/N` parse to 0 (the operator character reaches `atoi`), so a divide would be by zero; no bound on seven pairs; an unknown operator still counts | parse the number after the operator |
| LM10 | item effects (item effect (0x4D3F60, 0x4D3FE0, 0x4D3FF0, 0x4D4050, 0x4D4080)) | the module file's last number overwrites the base maximum and is not restored on unload | keep both maxima |
| LM11 | level cap (cap (0x4A69C0)) | when a level-up reaches the level maximum (51), the XP is capped but the level is not set, so the reachable cap is 50 | cap 50 or 51; one of the two is a slip |
| LM12 | backgrounds (legality (0x4C2B50)) | an 8-entry race code table is indexed by the PC's race; only races 8..10 (not playable) would read out of bounds. The reaction race table has the same 11×8 shape (table 0x5B684C) | latent; full tables if NPC races ever reach it |
| LM13 | inventory sources (sets (0x4BF390, 0x4BF510, 0x4BF5D0, 0x4BF640)) | `id < count` makes the last set unreachable | `id <= count` |
| LM14 | melee damage scaling (helper (0x4C6510)) | a helper always returns 100, so melee damage range scaling does nothing | a real factor; unknown |
| LM15 | prototype ranges | the earlier reading's range comments call 27309.. "monster" and 28310.. "unique", the description header says the opposite | labels only; the census decides |
| LM16 | fatigue adjustment (setter (0x45D4F0)) | the setter named for the adjustment writes fatigue points; the earlier reading flagged the name as wrong, and nothing calls it | dead code; name only |
| LM17 | stat lookup keys (stat key table 0x5B5384) | unspent points map to the poison key and magick points to age; `Resist:` in `spelllist.mes` uses this table | correct names |
| LM18 | script cache (cache (0x44C390, 0x44C450, 0x44C480, 0x44C4B0, 0x44C4E0)) | unlock decrements only in editor mode | decrement always |
| LM19 | spell run load (save (0x44F250, 0x44F3C0, 0x44F540)) | skipped slots are not cleared on load; latent, because a load first runs the reset that clears all 512 slots | clear them in the loader itself |
| LM20 | bless/curse (bless (0x4C4110, 0x4C4130, 0x4C4150, 0x4C4170)) | a missing module file leaves an invalid handle that is searched anyway; curse copies its name unbounded (curse (0x4C3CC0)) | guard both |

Already recorded elsewhere and not renumbered: the combat-core anomalies
A1–A9 of [ARC-RES-030](combat-core-evidence.md), the signed reputation parse
(ARC-RES-028 P1, decision D1) and the plan's P1–P7. LM1..LM20 follow the same
rule as those: each is decided by the stage or slice that uses it and logged
in the plan's [anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions).

## 9. Consequences for Steamwoven (project policy proposals)

1. **One content snapshot with two layers.** The universal loader (whole-world
   plan) builds a base layer once and a module layer per module, and each
   combat table records which layer it came from. To match the source reading (D1), the
   Steamwoven setting (D8) reads init-phase files from the base only; letting a module
   or an external pack override them is a phase 2 option (plan M2).
2. **Typed refusals instead of exit.** Every reader of section 3 returns a
   typed result naming the file, entry and token. In the strict reader mode an
   unknown token or a missing required line refuses the table; in the "like
   original" mode the reader reproduces the game where that is harmless (missing
   line text, skipped out-of-order keys) and still refuses where the game would end
   the process.
3. **AI rows by number, with the positional layout reachable.** Under D2 the default
   reader keys AI rows by entry number and refuses out-of-range rows (LM3);
   the Original setting (D8) reproduces positional filling, which ARC-RES-048
   confirmed in the original. The census must show
   whether the shipped files have gaps; if they have none the two agree.
4. **No AI timer in the save.** As the game does, the session resumes AI from object
   fields and schedules an immediate pass per NPC on load or sector entry;
   this also keeps saves independent of the AI profile (D4, design rule 7).
5. **Snapshot additions are known now.** The session snapshot will need, as
   slices arrive: turn-based combat state, active spell runs, saveable timers
   (wait here, combat-focus wipe, poison, fatigue), the gambling roll and
   spawner counters. Each is versioned with its slice, as WORLD-016 did.
6. **Prototype fields are the AI's inputs.** Slice I1 reads the AI row, I4
   the NPC flags, faction, reaction and enemy list, I8 the item AI action and
   spell `AI_*` values; all are present in the prototype and instance readers
   the project already has or plans (ARC-DATA-015, census ARC-RES-036), so no new
   format work is needed beyond `spelllist.mes` and the two AI files.

## 10. Original data census (next task)

Stage A9 and the "first fight candidates" output of A0 need the owner's data.
The follow-up task, ARC-RES-041 ([combat-domain data census](combat-data-census.md),
probe `--combat-census`), ran the census on the Mac through the module content
stack and recorded local aggregates only, as [ARC-RES-036](data-census.md) and
[ARC-RES-028](npc-reaction-reputation-evidence.md) did. It counted:

- which layer (base archive, patch, module, loose) holds each file of
  section 3, and whether a module file exists at all;
- `ai_params.mes` and `gameai.mes`: entry numbers, gaps (LM3), values per
  column (min, max, distinct);
- `spelllist.mes`: records present, `AI_*` values per action list, unknown
  tokens, `Resist:` stat names (LM17), `IQ:` values;
- `effect.mes`, `gameeffect.mes`: operators used, especially `*` and `/`
  (LM9);
- prototypes: count per range (LM15), AI row, NPC flags (kill on sight, no
  flee, aloof, wait here), faction, AI action on items, weapons with ammo;
- Crash Site and cave NPCs and creatures: AI row, flags, weapons and spells,
  to pick the first real fight (plan I10).

## Method

An earlier source reading (provenance in git history), organised by the
executable functions it cites, in three parallel passes with spot checks of
the cited functions: the game library and map functions (module
tables and phases), the file functions (mounting), the prototype and object
functions and object flags (prototypes and fields), and the AI, AI spell list,
magick/tech, item spell, spell, tech, effect, item effect, bless, curse,
spell-run node, combat, skill, stat, level, critter, background, reaction,
reputation, inventory source, trap, monster generator, party, fate, item,
animation, script, script name, time event, message file and string parser
functions. Evidence level F0 unless a ledger row is cited. Names in this
document are descriptive.


