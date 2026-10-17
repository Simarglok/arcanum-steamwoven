# Combat-domain load map: what CE loads and runs

Status: **Research**, work item ARC-RES-040 (2026-10-16), the CE half of stage
A0 of the [NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(owner decision D7). This document fixes, for combat, NPC AI, skills, magic and
technology, which modules the pinned source starts and in what order, every
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

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`][ce] (Sustainable Use License
  v1.0) at the line ranges linked below. CE is a reconstruction lineage, not an
  original-executable observation, and keeps decompiler names (`sub_…`); a
  reading of their intent is marked as such.
- **CE anomaly**: a branch that looks like a reconstruction slip. It is
  recorded under [section 8](#8-ce-anomalies-found-by-this-reading), never
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
   ([init][gl-init], [base mount][gl-data]). The module is mounted later, in
   `mod_load` ([mod load][gl-modload], [module mount][gl-moddata]). So every
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
   inside Map `init`, before any module exists ([proto init][proto-init]).
3. **Load order matters in two places.** MagicTech before MT-AT and Spell
   (they read its spell table and `spell.mes`); Map (with Proto inside it)
   before MagicTech-Post (summon prototypes) and Inven-Source (prototype
   checks). `spelllist.mes` is therefore read in two passes, one on each side
   of Map ([table][gl-table], [spell list][mt-load]). Bless and Curse only load
   texts; they reach Effect at run time, not at load.
4. **The AI owns 150 parameter rows and nothing else on disk.** Rows load
   positionally, not by entry number: the first entry must be 0 (base) or 100
   (module), and each next entry fills the next row whatever its number
   ([AI init][ai-init], [AI module][ai-mod]). An NPC selects its row by
   `OBJ_F_NPC_AI_DATA` without a bounds check ([row][ai-row]).
5. **Spells carry the AI's action priorities.** Line +4 of every
   `spelllist.mes` record holds ten `AI_*` values; MT-AT builds ten lists
   sorted by them, descending, at `init` ([MT-AT][mtai-init]). Items join the
   same choice in two ways: by `OIF_VALID_AI_ACTION` with a matching
   `OBJ_F_ITEM_AI_ACTION`, or, with spell mana left, through any of their five
   spells whose `AI_*` value for that action is set; a hexed wielded weapon is
   the only item considered, and NPCs with a leader skip unidentified items
   other than food and scrolls ([MT-AT items][mtai-items]).
6. **Persistent combat-domain state lives in objects, not in modules.** Skills,
   stats, spell colleges, tech degrees, effects, blessings, reputations,
   danger flags, focus, enemy list and AI row are object fields saved with the
   objects. Only seven module blobs touch this domain: Combat (turn state), Skill
   (next gambling roll), MagicTech-Post (active spells), Script (globals and
   flags), TimeEvent (timers), Anim (running animations) and MonsterGen
   (spawn counters).
7. **AI timers are never saved.** The AI time event is marked not saveable
   ([event table][te-table]); every NPC gets an immediate AI event again when
   its sector's objects are listed ([reschedule][ai-sched], [sector][sol]).
   Combat-focus wipes and NPC "wait here" timers are saved.
8. **The parsers are strict in the wrong way.** A missing `.mes` line yields a
   shared "Error! Missing line" string rather than a failure
   ([mes][mes-get]); an unknown flag or list token in a named-key line ends the
   process ([parser][sp-flags]); named keys are case-sensitive and must appear
   in the expected order, or they are skipped silently ([named][sp-named]).
9. **Much of the combat domain is code, not data.** Critical tables, hit
   locations, dodge criticals, material damage reduction, skill caps, training
   thresholds, tech degree tables, reaction race table, trap encodings and
   the action-point and attack-cost formulas are constants
   ([section 7](#7-rules-that-are-code-not-data)).
10. **This reading adds 20 CE anomalies (LM1..LM20)**, two of them in NPC
    weapon choice (LM1, LM2), one in AI row loading (LM3) and one in spell
    willpower (LM6) that phase 1 slices must decide.

## 1. Life cycle

The game library holds one table of 63 modules, each with up to ten hooks:
init, reset, mod_load, mod_unload, exit, ping, update_view, save, load and
resize ([table][gl-table]). Map holds a nested table of 17 sub-modules,
among them Effect (row 120) and Proto (row 124), run by Map's own hooks
([map table][map-table], [map init][map-init], [map mod load][map-modload]).

| Phase | When | Order | On failure |
| --- | --- | --- | --- |
| init | process start, base mounted | table order | exits the modules before the failed one; the failed one's exit is not called ([init][gl-init]) |
| mod_load | module chosen (`-mod:`, main menu, options, multiplayer) | table order | unloads the modules before the failed one, unmounts the module ([mod load][gl-modload]) |
| reset | new game or load | table order | never fails; clears `Save\Current` first ([reset][gl-reset]) |
| save | save game | table order | `Save\Current\data.sav`: version 25, then each module blob followed by `0xBEEFCAFE`; a failure while writing it deletes it ([save][gl-save]) |
| load | load game | table order | checks each sentinel; a failure stops without rollback; the version is stored, not checked ([load][gl-load]) |
| mod_unload, exit | module change, quit | reverse order | none ([mod unload][gl-modunload]) |

`mod_load` does four things in order ([mod load][gl-modload]): unload the
previous module, mount the module, empty `Save\Current`, and (outside the
editor) rebuild one mobile cache per map folder whose stored module GUID
differs ([mobiles][mobile]); only then does it call every module's `mod_load`.
Map's own `mod_load` first reads `Rules\MapList.mes`, then Effect's and
O_Name's ([map mod load][map-modload]).

### Combat-domain modules

Rows are table lines in `gamelib.c`; "per game" means cleared by `reset` or
rebuilt by `load`.

| Module | Row | Hooks | Reads | Runtime state | Saved |
| --- | --- | --- | --- | --- | --- |
| Item-Effect | 132 | init, mod_load, mod_unload, exit | `mes\item_effect.mes`; module `mes\gameitemeffect.mes` | text tables | no |
| Critter | 136 | init, exit | `Rules\xp_critter.mes`, `mes\critter.mes` | XP worth by level, texts | no |
| Script | 141 | init, reset, mod_load, mod_unload, exit, save, load | `Rules\ScriptEyeCandy.mes`; module `mes\storystate.mes` | 100-entry script cache, 2000 globals, 3200 flag bits, story state | yes |
| MagicTech | 142 | init, reset, exit | `Rules\magictech.mes`, `mes\spell.mes`, `Rules\SpellEyeCandy.mes`, `Rules\spelllist.mes` pass A | 223 spells, 25 component names, 512 run slots | via MagicTech-Post |
| MT-AT | 143 | init, reset, exit | none | ten sorted action lists, 40-entry per-critter cache | no |
| MT-Item | 144 | init, exit | none | 26 item triggers | no |
| Spell | 145 | init, exit | `mes\spell.mes` through MagicTech | college and spell texts, icons | no |
| Stat | 146 | init, exit | `mes\stat.mes` | texts | no |
| Level | 147 | init, exit | `rules\xp_level.mes`, `rules\gamelevel.mes`, `rules\userlevel.mes`, `mes\gamelevelname.mes`, `mes\userlevelname.mes`, `mes\level.mes` | XP table, auto-level schemes | no |
| Map › Effect | 148 › 120 | init, mod_load, mod_unload, exit | `rules\effect.mes`; module `rules\gameeffect.mes` | six effect tables of 400 | no |
| Map › Proto | 148 › 124 | init, exit | every `proto\*.pro`; generates missing ones | permanent prototype pool | no (prototypes are written to disk only when generated) |
| MagicTech-Post | 150 | init, save, load | `Rules\spelllist.mes` pass B | active spell runs | yes, plus per map `MT.dat` |
| Skill | 156 | init, exit, save, load | `mes\skill.mes` | next gambling roll | yes |
| Item | 158 | init, exit, resize | `mes\item.mes` | texts | no |
| Combat | 159 | init, reset, exit, save, load | `mes\combat.mes`, `Rules\CombatEyeCandy.mes` | turn-based flags, turn, action points, critter list | yes |
| TimeEvent | 160 | init, reset, exit, ping, save, load | none | timer lists for three clocks | yes, saveable types only |
| Bless, Curse | 163, 164 | mod_load, mod_unload | module `mes\gamebless.mes`, `mes\gamecurse.mes` | texts and effect numbers | no |
| AI | 165 | init, mod_load, mod_unload, exit | `rules\ai_params.mes`; module `rules\gameai.mes` | 150 parameter rows | no |
| Anim | 167 | init, reset, exit, save, load | `Rules\AnimEyeCandy.mes`, `Rules\WeaponEyeCandy.mes` | 216 animation runs | yes |
| Tech | 170 | init, exit | `mes\tech.mes` | texts | no |
| Background | 171 | init, exit | `rules\backgrnd.mes`, `mes\gameback.mes` | background table | no |
| Reputation | 172 | init, mod_load, mod_unload, exit | module `rules\gamerep.mes`, `mes\gamereplog.mes`, `mes\game_rp_npc_*.mes` | 1000 records | no |
| Reaction | 173 | init, exit | `mes\reaction.mes` | texts | no |
| Inven-Source | 177 | init, exit | `Rules\InvenSource.mes`, `Rules\InvenSourceBuy.mes` | inventory sets | no |
| Trap | 184 | init, exit | `Rules\TrapEyeCandy.mes`, `mes\trap.mes` | texts, effects | no |
| MT-Obj-Node | 189 | init, exit | none | node pool for spell runs | inside MagicTech-Post records |
| MonsterGen | 190 | init, reset, exit, save, load, resize | none | 256 spawn counters | yes |
| Party | 191 | init, reset, exit | none | 8 multiplayer slots | no: save and load exist but are not registered ([party][party]) |

Fate is not a module and reads nothing; critical hit and miss types are enums
only, and every critical table is code in `combat.c` ([tables][combat-tables]).
Description (row 131) and O_Name (Map sub-module) supply the names and
descriptions of [section 5](#5-prototypes-and-object-fields).

## 2. Where each file comes from

The mount order and the loose-over-archive rule are in
[content precedence](content-precedence.md); the CE functions are
[repository add][file-add] and [open][file-open]. What this reading adds is
the time axis:

- **Init files see the base only.** `gamelib_init` mounts `arcanum*.dat` and
  `data` ([base mount][gl-data]) and then runs every `init`. Nothing a module
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
  ([AI unload][ai-unload]).

## 3. Data file catalogue

"Pos." means entries are read in file order, "num." by entry number; "req."
means a missing file fails that phase.

| File | Module, phase | Keys | Req. | Content |
| --- | --- | --- | --- | --- |
| `rules\ai_params.mes` | AI, init | pos. from 0, rows 0..99 | yes | 17 integers per row ([params][ai-params], [init][ai-init]) |
| `rules\gameai.mes` | AI, mod_load | pos. from 100, rows 100..149 | no | same ([module][ai-mod]) |
| `Rules\magictech.mes` | MagicTech, init | num. 10..34 | yes, each | 25 component type names for `Type:` ([init][mt-init]) |
| `mes\spell.mes` | MagicTech + Spell, init | num. 0..222, 500..515, 700..779, 8000..8015, 9000..9015 | yes | spell names, college names, spell, college and mastery descriptions ([spell][spell-init]) |
| `Rules\spelllist.mes` | MagicTech init (A), MagicTech-Post init (B) | num. base 1000+50·n (n<140) or 3000+50·n (n 140..222), offsets +0..+48 | yes | 223 spell records ([section 4.2](#42-spell-records)) |
| `Rules\SpellEyeCandy.mes` | MagicTech, init (not editor) | num. groups of 10 | yes | spell visual effects ([init][mt-init]) |
| `mes\tech.mes` | Tech, init | num. 0..89 | yes | 8 discipline names, 8 degree names, descriptions, two messages ([tech][tech-init]) |
| `rules\effect.mes` | Effect, init | num. 50..399 | yes | effect definitions ([init][effect-init]) |
| `rules\gameeffect.mes` | Effect, mod_load | num. 0..49 | no | module effects ([module][effect-mod]) |
| `mes\item_effect.mes`, `mes\gameitemeffect.mes` | Item-Effect, init / mod_load | num.; module from 30000 | base yes | item effect texts ([item effect][item-effect]) |
| `mes\gamebless.mes`, `mes\gamecurse.mes` | Bless, Curse, mod_load | num. id·10 + 0 name, 1 effect, 2 text; id ≥ 50 | no | blessings and curses ([bless][bless]) |
| `mes\combat.mes` | Combat, init | num. 0..24 | yes | combat texts ([combat][combat-init]) |
| `Rules\CombatEyeCandy.mes` | Combat, init (not editor) | num. from 0 to first gap | yes | effects per reaction type ([combat][combat-init]) |
| `mes\skill.mes` | Skill, init | num. 0..35 | yes | 12 basic and 4 tech skill names and descriptions, 4 training names ([skill][skill-init]) |
| `mes\stat.mes` | Stat, init | num. 0..40, 500..527 | yes | 28 stat names and short names, genders, 11 races ([stat][stat-init]) |
| `rules\xp_level.mes` | Level, init | num. 1..51 | yes | XP per level; level 1 forced to 0 ([level][level-init]) |
| `rules\gamelevel.mes`, `rules\userlevel.mes` + name files | Level, init | schemes 50..199 game, 1..49 user | yes | auto-level schemes ([schemes][level-schemes]) |
| `Rules\xp_critter.mes` | Critter, init | num. = NPC level | yes | XP worth ([critter][critter-init]) |
| `mes\critter.mes` | Critter, init | num. 0..24 | yes | social classes, encumbrance texts |
| `rules\backgrnd.mes`, `mes\gameback.mes` | Background, init | blocks of 10 | yes | effect, condition, money, starting items; texts from 1000 ([background][background]) |
| `mes\reaction.mes` | Reaction, init | num. 0..6 | yes | seven reaction names ([reaction][reaction]) |
| `rules\gamerep.mes` + `mes\gamereplog.mes`, `mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes` | Reputation, mod_load | num. 1000..1999 | no | reputation effects, names, greetings ([reputation][reputation]) |
| `Rules\InvenSource.mes`, `Rules\InvenSourceBuy.mes` | Inven-Source, init | num. contiguous from 1 | first yes | loot and shop sets of up to 300 rate/prototype pairs ([sets][invensource]) |
| `Rules\TrapEyeCandy.mes`, `mes\trap.mes` | Trap, init | num. 10k+0..2 | yes | trap effects and texts ([trap][trap-init]) |
| `mes\item.mes` | Item, init | num. | yes | ammo and armour coverage names ([item][item-init]) |
| `Rules\AnimEyeCandy.mes`, `Rules\WeaponEyeCandy.mes` | Anim, init (not editor) | num. | yes | animation and weapon effects ([anim][anim-init]) |
| `proto\*.pro` | Proto, init | file name number | generated if missing | every prototype ([proto][proto-init]) |
| `rules\npc.txt`, `rules\monster.txt`, `rules\unique.txt` | Proto, init, only for generated prototypes | `Description: N` records | — | critter defaults ([rules text][proto-rules]) |
| `oemes\oname.mes`, `oemes\gameoname.mes` | O_Name, init / mod_load (merged) | num. | base yes | object names ([oname][oname]) |
| `oemes\faction.mes`, `oemes\gamefaction.mes` | O_Name, init / mod_load (merged) | num. | base yes | faction names; NPC faction is an AI input ([oname][oname]) |
| `mes\description.mes`, `mes\gamedesc.mes` | Description, init / mod_load | below / from 30000 | base yes | descriptions ([description][description]) |
| `scr\*.scr` | ScriptName, init / mod_load; Script on demand | file number 1000+ base, 1..999 module | — | scripts, cached on first use ([names][script-name], [cache][script-cache]) |
| `mes\storystate.mes` | Script, mod_load | num. | no | story state texts ([script][script-init]) |

## 4. Formats

### 4.1 Shared parsing rules

- **Message files.** A loaded file is sorted by entry number and searched by
  binary search. A missing number does not fail: it returns one shared static
  "Error! Missing line N in <path>" string ([mes][mes-get]). A missing or
  empty file fails `mes_load`. "Next entry" walks by position, whatever the
  numbers ([next][mes-next]).
- **Named keys** (`Key:` value lines, used by `spelllist.mes` and the eye
  candy files). A key must start exactly at the cursor after whitespace, the
  match is a case-sensitive prefix, and values end at `,`. A key out of the
  expected order is skipped without advancing ([named][sp-named]).
- **Value forms.** `(a@b)` pairs, with `b = a` when `@` is absent; a missing
  `(` ends the process ([pairs][sp-pairs]). Flag lists separate names by
  spaces or `|`, case-insensitively; any unknown name ends the process
  ([flags][sp-flags]). `a-b` ranges.
- **Other formats** are tokenised by hand per module: whitespace integers (AI
  rows), `attribute operator` pairs (effects), `token value,` pairs (level
  schemes), `name: min max, rate proto, …` (inventory sources) and digit-only
  scanners (reputation).

### 4.2 Spell records

Each of the 223 records occupies up to 49 numbered lines from its base
number ([load][mt-load]). Before parsing, every spell gets defaults: minimum
willpower {6, 9, 12, 15, 18}[n mod 5], range 99, no resistance, no duration
stat, duration modifier 1, no target ([defaults][mt-defaults]). For n ≥ 80
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

Pass B runs after Map because `Proto:` needs the prototype pool
([parse][mt-parse]); everything the AI and item triggers read is complete
after pass A. Summon lists 0..2 are weighted random, 3..5 chosen by aptitude
threshold, from tables in code ([summon][mt-summon]).

### 4.3 AI parameter rows

Each row has 17 whitespace-separated integers; missing trailing values keep
the row's previous value or zero ([params][ai-params], [init][ai-init]). The
first entry must be number 0 (base) or 100 (module); if it is absent, nothing
loads. Later entries fill rows by position until row 100 or 150 or the end of
the file, so a gap in numbering shifts every later row (LM3). The 17th value
says whether the NPC may open portals. The field meanings are stage A1's
work (plan I1). Two related tables are code: "wait here" durations {0,
15 min, 2 h, 1 day, 7 days, 30 days} by `OBJ_F_NPC_WAIT` ([wait][ai-wait])
and loudness radii {2, 8, 15}.

### 4.4 Effects

An effect line is up to seven `attribute operator` pairs (CE's comment says
five) ([parse][effect-parse]). Attributes are stats, basic skills, tech
skills, resistances, tech disciplines and ten specials (max HP, max fatigue,
reaction, critical chances and effects, reaction adjustments, XP gain).
Operators are `min N`, `max N`, `+N`, `-N`, `+N%`, `*N`, `/N`. They apply in
the order percent, multiply, divide, add, min, max; the "no innate" variant
skips race, background, class and gender causes ([apply][effect-apply]).
An object stores applied effects as two parallel arrays (number and cause).

### 4.5 Other tables

- **Level schemes**: `token value,` pairs over 8 stats, 16 skills, 16
  colleges, 8 disciplines, max HP and max fatigue; spell = college·5 + level;
  after its own scheme an NPC continues with scheme 70 ([schemes][level-schemes]).
  Virgil's replay already uses this ([ARC-RES-032](virgil-level-replay.md)).
- **Backgrounds**: per background `b`, entries 10b+0 text number, +1 effect,
  +2 race/gender condition (`<RACE><G>`, `ANY` or `NPC`), +3 money, +4 starting
  item prototypes; the list ends at the first missing +2 ([background][background]).
- **Inventory sources**: `name: min max, rate proto, …`, contiguous from 1;
  the buy file lists prototypes or `ALL`; every prototype is validated at init
  ([sets][invensource]).
- **Reputations**: three faction numbers, then up to five `adj origin faction`
  effects; after a signed adjustment the digit-only scanner re-reads the same
  number, so origin and faction become equal to the adjustment (already
  recorded as anomaly P1 with owner decision D1 in
  [ARC-RES-028](npc-reaction-reputation-evidence.md)) ([parse][rep-parse]).
- **Blessings and curses**: name, effect number and text per id; ids below 50
  are refused, and id 100 would collide with the UI message 1000, so 50..99 is
  the usable range (inference) ([bless][bless]).

## 5. Prototypes and object fields

**Loading.** Proto is a Map sub-module, so it runs at `init`, before any
module ([proto init][proto-init]). It lists `proto\*.pro`, takes the number in
each file name, generates every missing number of every type range with
defaults from code and writes it back as `proto\NNNNNN - <name>.pro`, then
reads **all** prototype files into a permanent pool. There is no on-demand
load and no reload at `mod_load`. A prototype's ID is its description number
+ 20.

**Inheritance.** An instance stores only the fields it overrides; any other
field is read from its prototype, resolved lazily by ID
([inherit][obj-inherit]). Which field groups a type carries is fixed per type
([valid][obj-valid]).

**Ranges** (description numbers, end exclusive, [ranges][proto-ranges]):
walls 1000..1008, portals 2008..2023, containers 3023..3061, scenery
4006..4051, projectiles 5028..5029, weapons 6029..6179, ammo 7038..7042,
armour 8042..8293, gold 9056..9057, food 10057..10145, scrolls 11059..11143,
keys 12060..12063, key rings 13061..13062, written 14062..14130, generic
15065..15213, PCs 16066..16079, NPCs 17067..17317, traps 26000..26008, and two
critter ranges 27309..27394 and 28310..28472 whose labels conflict (LM15).

**Generated defaults.** Non-critter defaults are code
([defaults][proto-defaults]). Critters use code for unique cases, otherwise a
`rules\*.txt` record: `Key: value` lines with case-insensitive prefixes over
37 keys, one flag per line, `Basic Stat` name and value, `Spell` names,
`Script` with its counters, damage `min max`, `Hit Points` as a target maximum
([format][proto-format]). XP worth and pool then come from
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

Flag definitions: [object flags][obj-flags]. The AI finds grenades
([grenades][ai-grenade]) and healing items ([healing][ai-heal-item]) by
generic flags, and spell items as Answer item 5 describes
([MT-AT items][mtai-items]).

**Names and descriptions.** Names come from `oemes\oname.mes` plus the
module's `gameoname.mes` ([oname][oname]); descriptions below 30000 from
`mes\description.mes`, from 30000 from the module's `mes\gamedesc.mes`
([description][description]); item effect texts likewise
([item effect][item-effect]).

### NPC weapon choice

`item_wield_best` ([wield][wield]) is how an NPC picks gear, so it belongs to
this map. The current weapon is always unwielded before the search.
Eligibility ([eligibility][wield-ok]) refuses polymorphed critters,
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
| Turn-based combat | Combat | module blob: flags and turn; when a round is active also action points and three object IDs; required AP is read back and discarded ([save][combat-save]) |
| Active spells | MagicTech-Post | module blob of 512 run slots in extents ([save][mt-save]); per map `Save\Current\maps\<map>\MT.dat` when a map closes during a teleport, plus the teleport notification ([map][mt-map], [close][te-map]) |
| Script globals, flags, story state | Script | module blob ([script][script-init]) |
| Timers | TimeEvent | module blob of saveable types per clock ([save][te-save]); per map `TimeEvent.dat` on teleport ([teleport][te-teleport]) |
| Animation runs | Anim | module blob of 216 slots |
| Next gambling roll | Skill | module blob, one value |
| Spawn counters | MonsterGen | module blob of 256 bytes ([save][monstergen]) |
| Followers | objects (leader, follower list) | object data; Party's own save is unregistered |
| Per-critter spell/item AI cache | MT-AT | not saved; cleared on reset ([reset][mtai-reset]) and on teleport ([teleport][mt-teleport]) |

**Timers** ([table][te-table]). Saved: animation, script, magic/tech, poison,
resting, fatigue, aging, turn-based combat, NPC wait here, lock, NPC respawn,
magic item recharge, body decay, item decay, combat-focus wipe, traps, scenery
respawn, random encounter and others. Not saved: **AI**, debug, background
and fidget animations, sleeping, main menu, light, multiplayer, multiplayer
control UI, UI and teleported. On map close, animation,
magic/tech, AI, combat, turn-based combat, world map and teleport timers are
cleared ([map close][te-map]).

**AI restart.** Because AI timers are not saved, every non-destroyed NPC gets
an immediate AI event when its sector's objects are folded into the sector
list ([sector][sol], [object][obj-ai-start], [reschedule][ai-sched]). A loaded game therefore resumes
AI from object fields alone.

## 7. Rules that are code, not data

The [combat core research](combat-core-evidence.md) already records the combat
formulas; listed here is only which constants have no file behind them, so the
loader does not look for one:

- **Combat**: hit location penalties (torso 0, head −50, arm and leg −30),
  random location (torso 70, leg 15, arm 10, head 5), dodge critical chance
  by training, material damage reduction for portals and containers, damage
  type to resistance (fatigue uses normal) ([tables][combat-tables],
  [location][combat-loc]), weapon wear by material, critical ladders, action
  points `max(5, Speed)`, attack cost from weapon speed, perception range
  (formulas in the [combat core research](combat-core-evidence.md)).
- **Skills**: key stat per skill, training minimums 0/1/9/18, maximum level by
  stat, opposed-skill table, gambling costs, pick-pocket penalties, monster
  melee thresholds ([tables][skill-tables]).
- **Stats**: limits and defaults, per-level cost, beauty to reaction, race
  maxima, background modifiers, derived formulas ([derived][stat-derived]).
- **Technology**: degree cost, level percentage {0, 10, 20, 35, 50, 65, 80,
  100}, minimum intelligence {0, 5, 8, 11, 13, 15, 17, 19}, schematic number
  1990 + 200·discipline + 10·degree ([tables][tech-tables]).
- **Magic**: minimum level per spell rank {1, 1, 5, 10, 15}, college icons,
  spell price 100, dwarves pay double, mastery halves the cost and doubles
  the maintenance period ([levels][spell-levels], [cost][spell-cost]); summon
  tables ([summon][mt-summon]).
- **Reaction**: an 11×8 race table ([race table][reaction-table]). **Traps**:
  scripts 30000..30007, placed prototypes 26000..26007, disarm items
  ([trap][trap-init]). **Spawners**: rate intervals ([rates][monstergen-rates]).
- **Items**: race to armour size table ([wield][wield]).

## 8. CE anomalies found by this reading

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable (magic, skills and AI sections of the ledger); the verdicts in the [verification ledger](original-verification-ledger.md#magic-arc-res-043-load-map-arc-res-040) supersede the CE readings below where they differ.

Recorded, not adopted. "Inference" marks a reading of intent.

| ID | Where | Source behaviour | Likely intent (inference) |
| --- | --- | --- | --- |
| LM1 | weapon choice ([wield][wield]) | the damage loop assigns instead of adding, so only the last damage type (fatigue) scores | sum over all five types |
| LM2 | weapon choice ([wield][wield]) | with a target, the best in-range weapon's score is never stored, so the **last** in-range weapon overrides the best | best in-range weapon |
| LM3 | AI rows ([init][ai-init]) | rows fill by position, so a numbering gap shifts later rows; the row index is not bounds-checked ([row][ai-row]) | rows by entry number; refuse bad rows |
| LM4 | AI unload ([unload][ai-unload]) | module rows 100..149 survive a module unload | clear them |
| LM5 | spell minimum intelligence ([iq][mt-iq]) | always 5 | per spell, or unused |
| LM6 | spell `IQ:` ([iq][mt-iq]) | stored and used as minimum **willpower** | name mismatch only, or a slip; stage A5 decides |
| LM7 | item spells ([MT-Item][mtitem-spell]) | item spell becomes a spell number by `& 0xFF`, so "none" (10000) becomes 16 (Stone Throw) | CE's comment reads it as deliberate (every item can be thrown); low confidence that it is a slip |
| LM8 | combat load ([save][combat-save]) | required action points are saved, read back and then set to 0 | restore them, or stop saving them |
| LM9 | effects ([parse][effect-parse]) | `*N` and `/N` parse to 0 (the operator character reaches `atoi`), so a divide would be by zero; no bound on seven pairs; an unknown operator still counts | parse the number after the operator |
| LM10 | item effects ([item effect][item-effect]) | the module file's last number overwrites the base maximum and is not restored on unload | keep both maxima |
| LM11 | level cap ([cap][level-cap]) | when a level-up reaches the level maximum (51), the XP is capped but the level is not set, so the reachable cap is 50 | cap 50 or 51; one of the two is a slip |
| LM12 | backgrounds ([race codes][bg-race]) | an 8-entry race code table is indexed by the PC's race; only races 8..10 (not playable) would read out of bounds. The reaction race table has the same 11×8 shape ([race table][reaction-table]) | latent; full tables if NPC races ever reach it |
| LM13 | inventory sources ([sets][invensource]) | `id < count` makes the last set unreachable | `id <= count` |
| LM14 | melee damage scaling ([helper][skill-helper]) | a helper always returns 100, so melee damage range scaling does nothing | a real factor; unknown |
| LM15 | prototype ranges ([ranges][proto-ranges]) | `proto.c` comments call 27309.. "monster" and 28310.. "unique", the description header says the opposite ([header][desc-header]) | labels only; the census decides |
| LM16 | fatigue adjustment ([setter][critter-fatigue]) | the setter named for the adjustment writes fatigue points; CE's FIXME says the name is wrong, and nothing in CE calls it | dead code; name only |
| LM17 | stat lookup keys ([keys][stat-keys]) | unspent points map to the poison key and magick points to age; `Resist:` in `spelllist.mes` uses this table | correct names |
| LM18 | script cache ([cache][script-cache]) | unlock decrements only in editor mode | decrement always |
| LM19 | spell run load ([save][mt-save]) | skipped slots are not cleared on load; latent, because a load first runs the reset that clears all 512 slots | clear them in the loader itself |
| LM20 | bless/curse ([bless][bless]) | a missing module file leaves an invalid handle that is searched anyway; curse copies its name unbounded ([curse][curse-copy]) | guard both |

Already recorded elsewhere and not renumbered: the combat-core anomalies
A1–A9 of [ARC-RES-030](combat-core-evidence.md), the signed reputation parse
(ARC-RES-028 P1, decision D1) and the plan's P1–P7. LM1..LM20 follow the same
rule as those: each is decided by the stage or slice that uses it and logged
in the plan's [anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions).

## 9. Consequences for Steamwoven (project policy proposals)

1. **One content snapshot with two layers.** The universal loader (whole-world
   plan) builds a base layer once and a module layer per module, and each
   combat table records which layer it came from. To match CE (D1), the
   Steamwoven setting (D8) reads init-phase files from the base only; letting a module
   or an external pack override them is a phase 2 option (plan M2).
2. **Typed refusals instead of exit.** Every reader of section 3 returns a
   typed result naming the file, entry and token. In the strict reader mode an
   unknown token or a missing required line refuses the table; in the "like
   original" mode the reader reproduces CE where that is harmless (missing
   line text, skipped out-of-order keys) and still refuses where CE would end
   the process.
3. **AI rows by number, with the CE layout reachable.** Under D2 the default
   reader keys AI rows by entry number and refuses out-of-range rows (LM3);
   the Original setting (D8) reproduces positional filling, which ARC-RES-048
   confirmed in the original. The census must show
   whether the shipped files have gaps; if they have none the two agree.
4. **No AI timer in the save.** Like CE, the session resumes AI from object
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

Source reading only at the pinned commit, in three parallel passes with spot
checks of the cited ranges: `gamelib.c` and `map.c` (module tables and
phases), `file.c` (mounting), `proto.c`, `obj.c`, `obj_flags.h` (prototypes
and fields), `ai.c`, `mt_ai.c`, `magictech.c`, `mt_item.c`, `spell.c`,
`tech.c`, `effect.c`, `item_effect.c`, `bless.c`, `curse.c`, `mt_obj_node.c`,
`combat.c`, `skill.c`, `stat.c`, `level.c`, `critter.c`, `background.c`,
`reaction.c`, `reputation.c`, `invensource.c`, `trap.c`, `monstergen.c`,
`party.c`, `fate.c`, `item.c`, `anim.c`, `script.c`, `script_name.c`,
`timeevent.c`, `mes.c` and `str_parse.c`. No CE code or structure was copied;
names in this document are descriptive.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[gl-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L129-L193
[gl-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L308-L398
[gl-reset]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L401-L454
[gl-modload]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L662-L732
[gl-modunload]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L747-L759
[gl-save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L929-L1061
[gl-load]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1065-L1190
[gl-data]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1820-L1836
[gl-moddata]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1838-L1903
[map-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L113-L131
[map-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L186-L221
[map-modload]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L278-L298
[mobile]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1056-L1145
[file-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/file.c#L573-L659
[file-open]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/file.c#L1729-L1801
[mes-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L146-L168
[mes-next]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L191-L218
[sp-named]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/str_parse.c#L471-L493
[sp-pairs]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/str_parse.c#L136-L175
[sp-flags]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/str_parse.c#L323-L395
[ai-params]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L36-L58
[ai-wait]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L152-L166
[ai-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L192-L232
[ai-mod]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L240-L273
[ai-unload]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L276-L278
[ai-row]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1564-L1573
[ai-sched]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3035-L3058
[sol]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L457-L473
[mt-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L754-L826
[mt-load]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1301-L1435
[mt-iq]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1472-L1491
[mt-summon]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L477-L535
[mt-defaults]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5520-L5567
[mt-parse]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5569-L6137
[mt-save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L866-L987
[mt-map]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1012-L1271
[mtai-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c#L76-L222
[mtai-items]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c#L402-L492
[mtitem-spell]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c#L127-L137
[spell-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L215-L256
[tech-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c#L101-L151
[tech-tables]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c#L23-L70
[effect-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L257-L290
[effect-mod]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L313-L353
[effect-parse]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L360-L481
[effect-apply]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L742-L858
[item-effect]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item_effect.c#L31-L131
[bless]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/bless.c#L28-L80
[combat-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L198-L229
[combat-save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L249-L347
[skill-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L346-L418
[stat-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L264-L309
[stat-derived]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L557-L611
[level-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L260-L345
[level-schemes]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L646-L703
[level-cap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L495-L498
[critter-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L124-L158
[background]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L72-L170
[reaction]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L144-L163
[reputation]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L118-L150
[rep-parse]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L171-L312
[invensource]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/invensource.c#L88-L258
[trap-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L82-L105
[monstergen]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/monstergen.c#L113-L131
[bg-race]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L29-L38
[skill-helper]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L963-L967
[critter-fatigue]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L428-L440
[stat-keys]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L227-L231
[monstergen-rates]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/monstergen.c#L23-L32
[party]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/party.c#L49-L88
[proto-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L229-L276
[proto-ranges]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L90-L135
[proto-defaults]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L504-L15982
[proto-rules]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16087-L16157
[proto-format]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16328-L16429
[obj-inherit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L2654-L2680
[obj-valid]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L4260-L4320
[obj-flags]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L148-L392
[oname]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/oname.c#L33-L110
[description]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/description.c#L123-L148
[item-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L150-L191
[wield]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2850-L2961
[wield-ok]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2658-L2768
[anim-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L2723-L2758
[script-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L150-L271
[script-cache]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3359-L3460
[script-name]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L38-L140
[te-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L92-L129
[te-save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L570-L676
[te-map]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1249-L1270
[te-teleport]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1556-L1753
[ai-grenade]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2546-L2561
[ai-heal-item]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3555-L3570
[mtai-reset]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c#L91-L95
[mt-teleport]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6946-L6949
[obj-ai-start]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4151-L4159
[combat-tables]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L77-L130
[combat-loc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2564-L2571
[skill-tables]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L37-L294
[spell-levels]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L25-L40
[spell-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L359-L422
[reaction-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L30-L142
[desc-header]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/descriptions.h#L1036-L1037
[curse-copy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/curse.c#L70-L80
