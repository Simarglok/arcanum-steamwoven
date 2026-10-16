# Pre-made heroes, starting gold and items, background effects

Status: **Research**, work item ARC-RES-027 (2026-10-16). This document answers
the open questions that [ARC-RES-025](character-creation-evidence.md#open-questions)
left about the starting hero: what the 13 loose PC prototypes contain, whether
this installation opens the twelve-hero/gender gate, which gold and items each
pre-made hero starts with, what the auto-level schemes buy at level 1, and what
background, race and gender effects do. It changes no production API, viewer,
session or dialog profile. Requirements:
[FR-CHAR-001..007](../requirements/characters-skills.md#fr-char-001).

## Vocabulary and boundary

The three evidence levels are those of
[ARC-RES-025](character-creation-evidence.md#vocabulary-and-boundary):

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0). A specification lineage, not an observation
  of the original executable.
- **Local observation**: read on the owner's unchanged installation with
  [`tools/research/starting_hero_probe.py`](../../tools/research/starting_hero_probe.py)
  (below). Only numbers, identifiers, engine keywords, counts and SHA-256
  digests are recorded; no original text, names or payload bytes are in this
  repository.
- **Project policy**: a choice this project makes. Section
  [Proposal](#proposal-for-steamwoven) is Proposal only.

Original executable parity remains **NOT_VERIFIED**: nothing here was run in
the original game, and CE was read, not executed.

### The probe

```bash
python3 -B tools/research/starting_hero_probe.py --self-test
python3 -B tools/research/starting_hero_probe.py --game-root /path/to/Arcanum
```

The self-test uses invented inputs only (42 checks: C `atoi`/`strtok`
semantics, MES brace pairs, effect and scheme grammars and the per-collection
change limit, inventory-source and buy-line rules, background blocks, critter
ART names with the missing-art fallback, the v119 prototype decoder and its
refusals, mount precedence). Original mode opens the mounts in the
[ARC-DATA-011](content-precedence.md) search order (module folder, module
`PATCH0`, module `Arcanum.dat`, `data/`, `Arcanum4`, `arcanum3`, `arcanum2`,
`arcanum1`; first wins), reads each resource in memory, records every copy's
size and SHA-256 and parses the winning copy. It prints one JSON report and
writes nothing. Limits: it mounts only these eight names, so the game folder,
`tig.dat`, module `.patch1..9` and any other `arcanum*.dat` that
[content precedence](content-precedence.md) allows are not consulted (none of
them is needed on this installation); background conditions are printed only
when every token is a race/gender code, `NPC` or `ANY`, otherwise as a digest;
player names only as lengths. The rule
parsers are independently written from the CE locators cited below; no CE code
or structure is reused.

## Answer

1. **The 13 PC prototypes equal CE's built-in table.** All files
   `016086`..`016098` in loose `data/proto` decode as v119 PC prototypes and
   match CE in race, gender, body type, portrait, inventory source, background,
   background text and auto-level scheme (SH-02). Every one stores the CE
   defaults: all eight primary stats 8, level 1, 5 unspent points, age 20,
   alignment 0, no skill points, empty inventory, no gold object, bank 0. Their
   only effects are the **race effect** (`64 + race`, cause race) and, for the
   four women, the **female effect 330** (cause gender). No background effect is
   stored (SH-03).
2. **The gender gate is open.** Prototype 16075's art id resolves to an
   existing critter ART file (`art\critter\hmf\hmfuwxaa.art`), so in CE's model this installation shows **twelve
   pre-made heroes** and the **two gender buttons** (SH-04, answers
   ARC-RES-025 CC-10).
3. **Pre-made heroes' gold and items are deterministic.** They come only from
   the prototype's inventory source: every used set has a fixed coin amount
   (minimum = maximum) and every item at rate 100. Seven sets cover the twelve
   heroes: **385 gold** for nine of them, **370** for the human man and the elf
   woman, **300** for the human woman; each gets one weapon and two armor
   prototypes, and the halfling also crude lockpicks (SH-05). No background
   money, clothes rule, lockpick/bandage rule or shop applies on this path.
4. **Pre-made backgrounds 39..50 have no effect in practice.** Their blocks are
   `NPC`-only with an empty effect slot, and the pre-made path never calls
   `background_set`. Had it been called, the empty slot would read as effect 0,
   which this module defines (SH-08), so this is a hazard for any
   reimplementation that applies the stored background blindly.
5. **The default new-character background has no effect either.** Block 0's
   effect id is 86, which exists and parses to zero changes (SH-07), so the
   ARC-RES-025 quirk "block 0's effect is never added" has no visible
   consequence. Its 400 gold, clothes and skill kits are unchanged from
   ARC-RES-025.
6. **Schemes spend 5 points at level 1.** Each pre-made hero's scheme is an
   ordered list of `<attribute> <target>` steps; CE charges one point per stat
   point, skill point, spell or tech degree (and per misc unit), and the steps
   are bought in order until one cannot be paid (SH-06). The exact level-1 sheet
   is **not computed** here (it needs the key-stat caps, spell and tech
   prerequisites and the hit-point/fatigue formulas).

## CE rules used

### Pre-made inventory: `sub_463E20`

For a critter, the inventory source field names a set; with a non-zero set the
game gives `random_between(min_coins, max_coins)` gold and creates each listed
prototype when `random_between(1, 100) <= rate`; the PC path is not reseeded
(only spawn-once containers are)
([inventory source](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2073-L2164)).
`player_obj_create_player` then clears the source field
([player.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/player.c#L224-L296)),
and the pre-made screen calls `item_wield_best_all`
([execute](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4327-L4355)).
Because every used set has `min = max` and rate 100, the random calls cannot
change the result.

`Rules\InvenSource.mes` is parsed as sets numbered `1..N` in order, starting
after number 0 (an entry 0 is skipped by `mes_find_next`); the first gap stops
parsing (later sets stay unparsed). A line is
`name: min,max rate,proto rate,proto ...` (first pair = coins). A line over the
string limit, an empty line or a name of 70 or more characters fails before the
coins and leaves the set without items (CE never writes its coins, which stay
whatever the uninitialised allocation held; the probe reports 0); an incomplete pair, an unknown prototype or more
than 300 items fails afterwards and empties **only the items** (the coins
already read stay); a failing `Rules\InvenSourceBuy.mes` line for the same set
(not empty, not `ALL`, an unknown prototype or more than 300) also empties the
items. The lookup accepts only `0 < id < N`, so the last set is unreachable
([parser](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/invensource.c#L189-L390),
[lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/invensource.c#L167-L182)).

### Level 1 and the auto-level scheme

`level_set(pc, 1)` writes the default stat array (primary 8, level 1, 5
unspent points, age 20), restores gender, race, alignment and age through the
stat setter (which removes and re-adds the race and gender effects), resets
skills, spells and tech, and runs the scheme
([level_set](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L708-L763),
[defaults](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L115-L144),
[stat setter effects](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L624-L799)).
Effects stored in the prototype survive the reset (`stat_set_defaults` writes
the array directly).

Schemes 50..199 come from `rules\gamelevel.mes`, below 50 from the user file
([rule lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L678-L701)).
A rule is a list of `<token> <target>,` steps; tokens are the eight primary
stats, twelve basic skills, four tech skills, sixteen colleges, eight tech
disciplines and `maxhps`/`maxfatigue`; an unknown token stops the rule
([grammar](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L857-L942)).
Each step buys until the target is reached; the first step that cannot be paid
ends the whole rule
([stat](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L949-L982),
[skill and others](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L989-L1212)).
Costs are one point per unit: stat cost table all 1
([stat_cost](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L151-L172)),
`basic_skill_cost_inc` and `tech_skill_cost_inc` 1
([basic](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L909-L915),
[tech](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1351-L1357)), tech degrees 1 each
([tech_degree_cost_tbl](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c#L23-L32)), misc steps one
point per hit or fatigue point
([auto_level_misc](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L1178-L1212)),
`spell_cost` 1
([spell.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L347-L352)).
A skill level is 4 × points
([basic_skill_base](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L486-L499)),
so a skill target 4 is one point when no effect already adjusts that skill (a
race effect such as halfling prowling +2 changes the count). Two CE details matter for an exact replay: a
stat step starts from the **base** value but continues from the **effective**
value, and a skill/spell/tech step that cannot be raised first raises its key
stat instead.

### Effects

`rules\effect.mes` fills effects 50..399 at start, the module's
`rules\gameeffect.mes` effects 0..49 at module load
([init](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L257-L332)).
An entry is a list of `<attribute> <operator>` pairs; attributes are the 28
stats, basic and tech skills, five resistances, eight tech disciplines and ten
special values; operators are `+n`/`-n` (add), `+n%`/`-n%` (percent),
`*n`, `/n`, `min n`, `max n`
([grammar](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L360-L481)).
CE detail: for `*` and `/` it passes the whole token (`*2`) to `atoi`, which
stores **0**; the effective-value pipeline applies percent, then multiply,
divide, add, min and max, so a stored multiply factor 0 makes the value 0
before the additions (a divisor 0 would divide by zero; no data uses `/`)
([apply](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L742-L850)).
CE marks its parser as different from the original, so whether the original
reads the factor is **NOT_VERIFIED**; two backgrounds use `*` (SH-07).

Innate effects: the stat setter adds effect `64 + race` with cause race and,
for female, effect 330 with cause gender
([stat.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L786-L795),
[ids](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.h#L8-L12)).
`background_set` always reads slot `+1` and adds `atoi` of it with cause
background, so an empty slot adds effect 0
([background_set](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L279-L293)).

### Context backgrounds (FR-CHAR-006)

Besides their data effect, fourteen background ids have hard-coded engine
behaviour: some depend on place, time, light or items (62..69, 72), others
apply always (51, 55, 74, 75, 78):

| Block | Engine behaviour | Data effect (SH-07) |
| --- | --- | --- |
| 51 | followers' skill training raised towards the PC's ([educate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L389-L440)) | max followers −1 |
| 55, 74, 75 | counts as "dumb" in dialog ([critter_is_dumb](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2392-L2419)) | 55: intelligence +6, four stats −1/−2, gambling +3; 74/75: stat changes and three resistances |
| 62, 63 | magick points +4 by day / by night, −4 otherwise | none |
| 64, 65 | magick points +4 outdoors / on natural tiles, −4 otherwise ([stat_level_get](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L382-L511)) | none |
| 66 | indoors intelligence +2; outdoors strength +2, dexterity/intelligence/willpower −2 | none |
| 67 | on sinkable (water) tiles strength +2, the other three −2 | persuasion +2 |
| 68 | in light below 128 strength +2, the other three −2 | perception +2 |
| 69 | cannot wield items with magick complexity ([item.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2675-L2678)) | tech points +2 |
| 72 | cannot pick up tech items ([item.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3654-L3657)) | strength +1, constitution +1 |
| 78 | dark sight ([critter.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2363-L2385)) | none |

(Block numbers are the `BACKGROUND_*` constants of
[background.h](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.h#L7-L20).)

### Starting clothes (FR-CHAR-007)

On the new-character path, when no item sits in the armor slot, the shop step
creates clothes by gender and the race's armour size: men a nice suit (small
8065, medium 8049, large 8069), women 8134, 8133 or 8135; if wearing fails, it
destroys them and creates a jacket (8118, 8110, 8126) instead
([item_generate_inventory](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L207-L316)). Armour size
by race: small for dwarf, gnome and halfling; large for half-ogre and ogre;
medium for human, elf, half-elf, half-orc, dark elf and orc
([size table](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L79-L91)). These are source facts;
the probe only checks that the prototypes exist (SH-10) and prints the size
table as a CE constant.

### Critter ART names (gender gate)

The gate tests `tig_art_exists` on 16075's current art id
([gate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L6039-L6055)).
The path resolver first normalises the id (female elves use the human body;
plate armour is male, elf → human, halfling → dwarf; the missing-art table
`art\missing.dat`, or a rebuild from file existence when its file count does
not match, swaps animation and weapon for art that is missing; for the stand
animation without a weapon its bit `0x8` switches to the unarmed file) and then
builds
`art\critter\<body><gender>\<body><gender><armor><shield><weapon><anim>.art`
([normalise](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L648-L708),
[fallback](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L824-L853),
[table rebuild](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L410-L450),
[table load and count check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L516-L556),
[file count](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L597-L645),
[path](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c#L856-L930),
[id fields](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L36-L39)).
The probe models the body/gender normalisation, the stand/no-weapon fallback
(from `missing.dat` when its count matches, else derived) and the name; it
refuses any other animation, so the other fallback rows are not modelled.

## Local observations

| ID | Observation |
| --- | --- |
| SH-01 | Mounts read: module folder (167 files), module `PATCH0` (18,804 bytes, `d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce`), module `Arcanum.dat` (355,938,305 bytes, `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`), `data/` (1,426 files), `Arcanum4.dat` (447,614 bytes, `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c`), `arcanum3.dat` (9,605,011 bytes, `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`), `arcanum2.dat` (147,991,122 bytes, `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`), `arcanum1.dat` (447,470,816 bytes, `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576`). Resources (winner first): `rules\backgrnd.mes` `Arcanum4` over `arcanum3` (the ARC-RES-025 CC-02 pair); `rules\effect.mes` only `arcanum3` (17,497 bytes, `8ac43304c1e3ff2b351124acb7403c5de79165963f6ea0a83fa4c4d3bdb0b6b2`, 309 entries); `rules\gameeffect.mes` only module `Arcanum.dat` (417 bytes, `f8c921b72da9b004aedf9a34c8a1d4f6bbaa86abd1168d234cd811b66ead0a85`, 1 entry); `rules\InvenSource.mes` `Arcanum4` (35,149 bytes, `b66b9cd9640c5dff2b4018dfb5a5d8fc6e5c8f71b62f0a9dcfaeb4430ecfda17`) over `arcanum3` (35,008 bytes, `fc7936ffd47a77ed676ceed33315a3bebc57246338bbcaf53585d669fc2d4ef4`), 123 contiguous sets; `rules\InvenSourceBuy.mes` `Arcanum4` (23,869 bytes, `6def951c87c876cb6675ecfb2f552b7078f71840c488d34dad320ab27b8cfb7d`) over `arcanum3` (22,811 bytes, `68ff763322e448d78b57c1c3fbd9bafa72a14ed55d058a63debb18a4ea406718`); `rules\gamelevel.mes` `Arcanum4` (18,492 bytes, `5121e2fabd00ac628be4488b69fafee6639eada475b9a3eee00fe0b145919f55`) over `arcanum3` (18,506 bytes, `f76d56dfbbd00095c2e8a6ca25fb983be805fcb40f9cbe4ac3ff8b770f47ef2e`); `mes\gamelevelname.mes` only `arcanum3` (`21df5868c7fc1b79604a58bba01aeafde35436c7070e3616978e2f8e306de53a`, 58 entries); `art\missing.dat` only `arcanum1` (332 bytes, `49502ba4e4236f7c7835e697c3ff569f894f2814f82cd3543ce861c28526b7e0`). No resource has duplicate message numbers. |
| SH-02 | The 13 PC prototypes are the only copies (no `proto/` match in any archive). Each decodes completely (no trailing bytes) and all nine compared fields equal the CE table of [ARC-RES-025](character-creation-evidence.md#pre-made-heroes) for every prototype. SHA-256 per file: see [the table below](#pc-prototype-files). This settles ARC-RES-025 CC-09. |
| SH-03 | Every prototype: base stats st/dx/cn/be/in/pe/wp/ch 8, level 1, XP 0, alignment 0, unspent 5, age 20, gender and race as in the table; no basic or tech skill points; the spell/tech array holds one non-zero element (index 16, value −1), which `level_set` resets anyway; inventory count 0 and empty list, no gold handle, bank money 0, PC flags 0, critter flags 0. Effects: race effect `64 + race` (cause race) on all 13, plus effect 330 (cause gender) on 16075..16078. The stored player name is a non-empty string on all 13 (only lengths are printed). |
| SH-04 | Art ids are critter underwear, no shield, no weapon, animation 0: `0x28002000` (human male), `0x29002000` (dwarf), `0x2A002000` (halfling body), `0x2B002000` (half-ogre), `0x2C002000` (elf male), `0x20002000` (human female), `0x24002000` (elf female). `art\missing.dat` header 4,601 equals the 4,601 listed critter/monster/unique-NPC ART files, so the engine uses it; its stand/no-weapon bit is set only for the dwarf, halfling and half-ogre female bodies (the same as a rebuild from file existence), so no pre-made id is redirected. Resolved names exist in `arcanum1.dat` only: `hmm` (generic, 16069, 16072, 16073), `ghm` (16067, 16068), `hgm` (16070), `efm` (16071), `dfm` (16074), `hmf` (16075..16078; 16076 is an elf woman, normalised to the human body). The only critter folders are `dfm`, `dfx`, `efm`, `ghm`, `hgm`, `hgx`, `hmf`, `hmm` and `hmx` (no `eff`). So 16075's art exists and the gate opens. |
| SH-05 | Pre-made inventory sets (`Arcanum4`; identical in `arcanum3`), all parsed, reachable, every rate 100; the buy lines of sets 20 and 22..26 are empty and set 108 has none, so none empties a set; entry 0 is absent. Coins and prototypes (type in brackets: 5 weapon, 7 armor, 14 generic): set **20** 385 — 6029 [5], 8168 [7], 8049 [7]; set **22** 370 — 8094 [7], 6111 [5], 8170 [7]; set **23** 300 — 8200 [7], 6051 [5], 8171 [7]; set **24** 385 — 6029, 8168, 8065 [7]; set **25** 385 — 6029, 8168, 8069 [7]; set **26** 385 — 6029, 8168, 8142 [7]; set **108** 385 — 6029, 8168, 8065, 15178 [14]. 8049, 8065 and 8069 are the medium, small and large suits of the new-character clothes rule; 15178 is the crude lockpicks of the skill rule. Every listed prototype exists in `data/proto`. |
| SH-06 | Schemes 76..87 (`Arcanum4`; identical in `arcanum3`) all exist and parse, 31..49 steps each, and all twelve names exist. Standard NPC scheme 70 exists. First steps (attribute, target): see [the scheme table](#level-1-schemes). |
| SH-07 | Background blocks (`Arcanum4`): 79. Effect ids used: 0 (blocks 39..50), 86..113, 179..189, 328, 329, 335..352 and 355. Every referenced effect exists and parses. Seven blocks have an effect with zero changes: 0 (effect 86), 62..66 and 78 (all effect 342). Of the 67 blocks offered to PCs (all but the `NPC` blocks 39..50), 51 change stats, 18 basic skills, 8 special values, 7 resistances, 5 tech skills and 1 a tech discipline; their operators are 206 add, 2 multiply (blocks 5 and 11, `*2` on the bad and good reaction adjustment, stored as 0 by CE), 1 percent, 1 max. No effect exceeds 7 changes in one attribute collection; effect 66 (elf) reaches exactly 7. Blocks 53/54 and 57/58 share a description id (1053, 1056) and an effect: they are race/gender variants with different item lists. Ten blocks carry items (3, 5, 8, 31, 39, 53, 54, 57, 58, 71); all item prototypes exist. |
| SH-08 | The module's `gameeffect.mes` defines exactly one effect, number 0 (strength +6, beauty −6, intelligence −6, prowling −3, age +20; the same changes as the ogre race effect 73). It is the effect an empty background effect slot would add. |
| SH-09 | Race effects 64..74 (effect.mes): human none; dwarf st +1, cn +1, ch −1, dx −1, all four tech skills +2, tech points +3, age +80; elf wp +1, dx +1, be +1, cn −2, st −1, tech skills −2, magick points +3, age +180; half-elf dx +1, be +1, cn −1, tech skills −1, magick points +1, age +15; gnome wp +2, haggle +2, bad-reaction adjustment +10, cn −2, age +70; halfling dx +2, prowling +2, dodge +1, critical-hit chance +5, st −3, age +30; half-orc st +1, cn +1, melee +2, dodge +2, poison resistance +10, be −2, ch −2, age −5; half-ogre st +4, damage resistance +10, be −1, in −4, prowling −2; dark elf, ogre and orc (NPC races) exist too. Female effect 330: st −1, cn +1. |
| SH-10 | Starting-kit prototypes of `item_generate_inventory` all exist once in `data/proto`: suits 8049/8065/8069, jackets 8110/8118/8126, dresses 8133/8134/8135 (type 7), crude lockpicks 15178 and bandages 15179 (type 14), gold 9056 (type 8). |

### PC prototype files

| Description | File | Bytes | SHA-256 |
| --- | --- | --- | --- |
| 16066 | 016086 | 941 | `043d8db2eff4c00ebda7825f95961357322e816afec595bb62f7d25a5a283625` |
| 16067 | 016087 | 955 | `a199053280918054899eec7147542ba74f29d8ede7d2a5d66d1ae0baa902eb16` |
| 16068 | 016088 | 957 | `7f4fcfe5d7dff0006c66722a5bda794e5a4f4c0f78cf9eae228094e657de9818` |
| 16069 | 016089 | 952 | `88606221caa913d9d7a345044f156ebdd029f780dc0b02e77a052a31bee7d56b` |
| 16070 | 016090 | 950 | `4fba6efca062874aa7bd488048ca4121d5b44dae74059a2ee11844019f230876` |
| 16071 | 016091 | 949 | `0c37b472af35e6a75f2a724b3e8df89f52e9762e5b4e0fe03c0c303aab739dd4` |
| 16072 | 016092 | 950 | `05de90941aa2313767e3a7b211b4eb6bf3e6bce22e88da107e8210a59dae9b79` |
| 16073 | 016093 | 952 | `1e67e617d035742748ed5a8781d3e5a82c544090190309890c981b899da690e2` |
| 16074 | 016094 | 954 | `8f610fe9212c705d208dc4978ab736c73ebccb6570e9bcbf3f7e3e7de14ed9d3` |
| 16075 | 016095 | 964 | `1456ed75def9d0597a5b8fde960aa34b29a2692f6494a1d0cd95c1beab04ebbd` |
| 16076 | 016096 | 959 | `0e51765f3465fa455eb8a1ae10f8d081db76c9190bb6acc59fde61e8c28681c1` |
| 16077 | 016097 | 959 | `2eab260e6b881a3860147b176445b45129df60cc003da6242f16d854c6f6dd2a` |
| 16078 | 016098 | 959 | `7fed0a3ff87325d666cbc67d271ae66e440d5318cca0459471728e03eba1fce7` |

### Pre-made heroes at the start

| Prototype | Race, gender | Set | Gold | Items | Scheme |
| --- | --- | --- | --- | --- | --- |
| 16067 | halfling, male | 108 | 385 | 6029, 8168, 8065, 15178 | 76 |
| 16068 | gnome, male | 24 | 385 | 6029, 8168, 8065 | 77 |
| 16069 | half-elf, male | 20 | 385 | 6029, 8168, 8049 | 78 |
| 16070 | half-ogre, male | 25 | 385 | 6029, 8168, 8069 | 79 |
| 16071 | elf, male | 20 | 385 | 6029, 8168, 8049 | 80 |
| 16072 | human, male | 22 | 370 | 8094, 6111, 8170 | 81 |
| 16073 | half-orc, male | 20 | 385 | 6029, 8168, 8049 | 82 |
| 16074 | dwarf, male | 24 | 385 | 6029, 8168, 8065 | 83 |
| 16075 | human, female | 23 | 300 | 8200, 6051, 8171 | 84 |
| 16076 | elf, female | 22 | 370 | 8094, 6111, 8170 | 85 |
| 16077 | half-orc, female | 26 | 385 | 6029, 8168, 8142 | 86 |
| 16078 | half-elf, female | 26 | 385 | 6029, 8168, 8142 | 87 |

Items are created, moved into the pack and then worn by `item_wield_best_all`
(source fact). Effects at the start are the race effect and, for women, effect
330; the stored background (39..50) adds nothing.

### Level-1 schemes

The first six steps of each scheme (`<attribute> <target>`; skill targets are
levels, 4 points of level per skill point). With 5 points, the hero buys these
in order until one cannot be paid. A step costs one point per unit still
missing: `dx 9` from 8 costs 1 and `be 10` from 8 costs 2; a skill step costs
fewer points when an effect already raises that skill; misc steps (`maxhps`,
`maxfatigue`) cost one point per hit or fatigue point and normally exhaust the
remainder.

| Scheme | Steps | First steps |
| --- | --- | --- |
| 76 | 35 | bow 4, picklock 4, dx 9, prowling 4, pickpocket 4, bow 8 |
| 77 | 31 | persuasion 4, heal 4, firearms 4, gun_smithy 1, repair 4, haggle 4 |
| 78 | 42 | pickpocket 4, melee 4, bow 4, prowling 4, conveyance 1, dodge 4 |
| 79 | 49 | st 9, melee 4, dodge 4, maxhps 54, cn 9, throwing 4 |
| 80 | 45 | bow 4, necro_good 1, cn 9, nature 1, pe 9, cn 10 |
| 81 | 42 | melee 4, necro_evil 1, maxfatigue 34, cn 9, force 1, maxfatigue 48 |
| 82 | 49 | melee 4, dodge 4, bow 4, st 9, haggle 4, maxhps 34 |
| 83 | 36 | firearms 4, melee 4, gun_smithy 1, mechanical 1, haggle 4, electric 1 |
| 84 | 37 | be 10, persuasion 4, haggle 4, mental 1, divination 1, be 11 |
| 85 | 33 | melee 4, nature 1, necro_good 1, st 9, bow 4, air 1 |
| 86 | 33 | melee 4, heal 4, pe 9, anatomical 2, bow 4, therapeutics 1 |
| 87 | 35 | melee 4, necro_good 1, heal 4, anatomical 1, nature 1, persuasion 8 |

A spell or tech step can also spend a point on intelligence or willpower first
when the spell's or degree's prerequisite is not met (source fact), so these
rows are inputs, not the level-1 result.

## What this changes

- **WORLD-012 innate effects.** The race and female effects that
  [character stats](../world/character-stats-runtime.md) expects from its
  caller now have data: SH-09 lists them (all `add` operators).
- **FR-CHAR-007.** The pre-made starting kit is per hero (SH-05), not per
  background; the new-character kit is block 0 plus the clothes and skill
  rules of ARC-RES-025.
- **FR-CHAR-006.** The context table above lists the engine-side background
  behaviours; their data effects are SH-07.

## Proposal for Steamwoven

Proposal only; ARC-WORLD-015 or a later task decides. ARC-WORLD-015 took proposal 1 without the scheme replay (the
points stay unspent), a bounded proposal 2 (PC prototype, effect and inventory-source readers; schemes not read),
proposal 3 (`*`/`/` refused) and proposal 4: [starting hero runtime](../world/starting-hero-runtime.md).

1. **Default hero for the first playable start:** one pre-made hero is the
   simplest exact start: race/gender/portrait from the prototype, the race
   (and female) effect, gold and items from its set, no background effect,
   level 1 with 5 points spent by its scheme. The scheme replay needs a small
   bounded implementation (stat/skill/college/tech/misc steps with the CE
   order and the "raise key stat" fallback) or an owner decision to hold the
   points unspent and mark the sheet as not original.
2. **Data readers in `Arcanum.Data`:** PC prototype fields (the probe's slot
   map), `InvenSource.mes` sets with the CE parsing rules, `gamelevel.mes`
   schemes and `effect.mes`/`gameeffect.mes`, all through
   [ContentStack](content-precedence.md) rather than the probe's local mount
   list.
3. **Effect operators:** implement `add`, `percent`, `min`, `max` first; keep
   `*`/`/` behind an explicit decision because CE's stored factor is 0
   (SH-07) and the original is NOT_VERIFIED.
4. **Never apply a stored background blindly:** a pre-made hero's background
   id is display data; applying `background_set` to it would add module effect
   0 (SH-08).

## Open questions

- The exact level-1 sheet of each pre-made hero (scheme replay with CE caps,
  spell minimum level/intelligence and tech degree rules, hit-point and
  fatigue formulas).
- What the armor prototypes 8168, 8170, 8171, 8094, 8200, 8142 and weapons
  6029, 6051, 6111 are and where `item_wield_best_all` puts them.
- Whether the original executable reads the `*`/`/` effect factor (two
  backgrounds use it).
- Carried over from ARC-RES-025: whether Crash Site scripts read the pre-made
  flags 2350..2361, and what the shop NPC on `ShopMap` sells.
