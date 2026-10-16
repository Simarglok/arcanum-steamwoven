# Character creation and the starting hero

Status: **Research**, work item ARC-RES-025 (2026-10-16). This document answers
where the player character (PC) comes from when a new game starts: the
pre-made heroes, the new-character screen (race, gender, portrait, background),
the starting gold and items, and the place the hero appears. It changes no
production API, viewer, session or dialog profile. Requirements:
[FR-CHAR-001..007](../requirements/characters-skills.md#fr-char-001). Stat
rules (FR-CHAR-008..011) are only touched where creation resets them.

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0); the line ranges below are what was read. It
  is a specification lineage, not an observation of the original executable.
- **Local observation**: read on the owner's unchanged installation
  (`arcanum1..4.dat`, module `Arcanum.dat`/`Arcanum.PATCH0`, loose `data/proto`)
  with the bounded entry reader of `tools/research/dat_probe.py`
  (`parse_archive` + `read_bounded_entry`, in memory) and a throwaway `{n}{text}`
  message parser. Only numbers, identifiers, counts and SHA-256 digests are
  recorded; no original text, names or payload bytes are in this repository.
  The parser is not committed: it is a few lines and the facts are re-checkable
  from the digests and rules below.
- **Project policy**: a choice this project makes where the source leaves a
  product decision open. Section [Proposal](#proposal-for-steamwoven) is
  Proposal only; merging this document does not ratify it.

Original executable parity remains **NOT_VERIFIED**: nothing here was run in
the original game, and CE was read, not executed.

## Answer

The hero is always a fresh object created from a **PC prototype**. There are
two ways to get one, both from the single-player menu, and both end with the
same teleport onto the start map:

| Path | Prototype | Stats | Starting inventory and gold | Screens after the pick |
| --- | --- | --- | --- | --- |
| Pre-made hero | one of 8 (or 12, see below), prototypes 16067..16078 | reset to level-1 defaults, then the prototype's auto-level scheme spends the points | the prototype's **inventory source** (can include gold); no background money | none: the game starts at once |
| New character | generic PC prototype 16066 | level-1 defaults: every primary stat 8, 5 unspent points | the chosen **background**: its money (400 for the default background 0) and item list, plus clothes when nothing is in the armor slot (in practice always), lockpicks and bandages when the editor gave those skills | character editor, then the shop |

Before either path the game already has a placeholder PC on the **shop map**;
the start map and its location come from the module map list. In this
installation that is record 5000 `Arcanum1-024-fixed`, location
`(92958, 82592)`, the same Crash Site selector the
[Crash Site slice](crash-site-slice.md) already uses.

## The sequence in CE

1. **Game reset.** `gameinit_reset` opens the map list's `SHOPPING_MAP` (falls
   back to `START_MAP`) and calls `player_create`, which makes a PC from the
   default PC prototype (16066) at that map's starting location with a fixed
   placeholder ART id
   ([gameinit.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gameinit.c#L60-L72),
   [player.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/player.c#L118-L155),
   [type→default prototype](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L106),
   [map types in the map list](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2003-L2060)).
2. **Single player → New game** opens a screen with pre-made, new and a third button (target −2, presumably back)
   ([buttons](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L889-L892),
   [screen](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3647-L3653)).
3. **Every new PC** is made by `player_obj_create_player`: the old PC is
   destroyed, a new object is created from the chosen prototype at the old
   PC's location, it becomes the local PC, then `level_set(pc, 1)`, then
   `sub_463E20` generates the prototype's inventory source, and the inventory
   source field is cleared so it never regenerates
   ([player.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/player.c#L224-L296),
   [inventory source](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2073-L2164)).
4. **`level_set(pc, 1)`** keeps gender, race, alignment and age, resets every
   stat, skill, spell and tech to its default, sets level 1 and its XP, adds the
   level bonus points (none at level 1), and runs the object's auto-level
   scheme (none for the generic PC)
   ([level.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L708-L763),
   [defaults: primary stats 8, level 1, 5 unspent points](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L115-L144)).
   Whatever stats a prototype stores are therefore not the hero's stats; the
   scheme and the editor are.
5. **Start.** Leaving the last screen calls `sub_5412E0`: if the new-character
   path asked for it, empty weapon and armor slots are filled with the best
   item; when a new game was flagged, the PC is teleported to `START_MAP` at the
   map list location with movies 1 and 7 and a fade, a newspaper time event and
   the random-encounter schedule are set
   ([mainmenu_ui.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L1620-L1717)).

### Pre-made heroes

- The browser starts at index 1 and cycles 1..`count−1`; index *i* is
  prototype `16066 + i`. `count` is 9 (eight heroes)
  ([browse](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4242-L4323),
  [count](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L185-L188),
  [ids](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/descriptions.h#L924-L936)).
- `main.c` clears the gender-lock flag (initially set) before menu init, and
  then at menu init `sub_549A80` checks whether prototype 16075 exists and its ART
  exists. If so, `count` becomes 13 (twelve heroes, the last four female) **and
  the new-character window gets 10 buttons instead of 8**, which are the two
  gender buttons; otherwise gender cannot be changed at all. This answers the
  CE "only 8 buttons" TODO
  ([gate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L6034-L6056),
  [window, 8 of 10 buttons](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L930-L957),
  [gender buttons guarded by the flag](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3874-L3971)).
- Picking one: `player_obj_create_player` with that prototype; then for
  `j = 0..count−2` the message `551 + j` of the main-menu messages is read as a
  global-flag number and set to 1 for the chosen hero and 0 for the rest; then
  `item_wield_best_all` and the game starts. The pre-made path skips the
  character editor and the shop and never calls `item_generate_inventory`
  ([execute](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4327-L4355)).
- What CE's built-in prototype table gives each pre-made hero (used when the
  prototype file is missing; the engine then writes the file itself):
  race/gender/body type, portrait id, inventory source, background 39..50 with
  description 1039..1050, and auto-level scheme 76..87
  ([built-in PC prototypes](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L15715-L15876),
  [missing protos are created and saved](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L373-L414),
  [create and save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L481-L500),
  [file name format](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L300)).

| Prototype | Race, gender | Portrait | Inventory source | Background | Scheme |
| --- | --- | --- | --- | --- | --- |
| 16066 (generic) | human, male | 1005 | – | 0 | – |
| 16067 | halfling, male | 1017 | 108 | 39 | 76 |
| 16068 | gnome, male | 1002 | 24 | 40 | 77 |
| 16069 | half-elf, male | 1032 | 20 | 41 | 78 |
| 16070 | half-ogre, male | 1027 | 25 | 42 | 79 |
| 16071 | elf, male | 1033 | 20 | 43 | 80 |
| 16072 | human, male | 1005 | 22 | 44 | 81 |
| 16073 | half-orc, male | 1030 | 20 | 45 | 82 |
| 16074 | dwarf, male | 1020 | 24 | 46 | 83 |
| 16075 | human, female | 1025 | 23 | 47 | 84 |
| 16076 | elf, female | 1024 | 22 | 48 | 85 |
| 16077 | half-orc, female | 1031 | 26 | 49 | 86 |
| 16078 | half-elf, female | 1029 | 26 | 50 | 87 |

The hero's displayed name is the scheme's name (`auto_level_scheme_name`, read
from `mes\gamelevelname.mes`), so names stay in the game data, not in this
document.

### New character

- The screen makes a PC from 16066 (human, male, portrait 1005, background 0)
  and fills the name field with placeholder message 500; the buffer is 23
  characters
  ([create](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3656-L3684)).
- Buttons: portrait previous/next, race previous/next, background
  previous/next, and gender previous/next only when the gate above opened them
  ([handlers](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3874-L3971)).
  Changing race or gender **clears the background first**, sets the body type
  from the race table, keeps female only where the table allows it (human,
  elf, half-elf, half-orc), and selects the first matching portrait
  ([race/gender](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4000-L4166),
  [race table](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L1103-L1112)).
- Background step: `background_clear` (fields to 0, removes the effect caused
  by background) then `background_set` (stores the id and its description id
  and adds the background's effect). Re-selection therefore never stacks
  effects
  ([set/clear](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L279-L308)).
  `background_set` runs only from the background buttons
  ([calls](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3941-L3956)). A player who never
  presses them, or changes race or gender afterwards, keeps background 0 with
  description field 0, and block 0's effect is **never added**; block 0's
  money is still given, because the shop reads the field, which is 0.
- Accept: an empty buffer or the untouched placeholder text shows message
  506 and refuses; otherwise the name is stored in `OBJ_F_PC_PLAYER_NAME` and
  auto-equip on start is requested
  ([execute](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4212-L4239)).
- Accepting the new character opens the character editor explicitly
  ([accept](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L3903-L3911)); creation mode
  spends the 5 points. The editor's accept advances the window index by one,
  to the shop
  ([window order](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L1240-L1258),
  [editor button](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4376-L4382),
  [advance](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L5609-L5634)).
- **Shop.** Opening it calls `item_generate_inventory(pc)`: it destroys the
  PC's whole inventory, applies the background's money and items, and if no
  item sits in the armor slot it creates clothes by gender and armor size
  (falling back to a jacket if they cannot be worn). Background items go to
  free pack slots, so the armor slot is in practice always empty here and
  clothes are always made (inferred). It then adds crude lockpicks if the PC
  has pick-locks points and bandages if it has heal points. Then, if the map list has
  a `SHOPPING_MAP`, it flags the new game and opens barter with the first NPC
  found on the current map (with no NPC it starts at once)
  ([shop](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L4438-L4486),
  [inventory](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L207-L316)).
  CE detail: with no shop map at all the function starts the game before the
  new-game flag is set, so CE would skip the start-map teleport; this
  installation has a shop map, so the normal path applies.

### Backgrounds and starting gold

`rules/backgrnd.mes` holds blocks of 10 messages per background `b`: `10b+0`
description id (in `mes/gameback.mes`), `+1` effect id, `+2` condition string,
`+3` money, `+4` item prototype list
([layout](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L13-L19)).
A background is offered when its condition is empty or contains the
two-letter race code plus `M`/`F`, or `ANY`; `NPC` admits only NPC objects
([legality](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L497-L526),
[search](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L110-L172)).
Money is given with `item_gold_transfer`, items are created at the PC and
marked identified
([generate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L341-L380)).
Block 0's money is also the base for `background_adjust_money`, which CE uses
only in multiplayer
([adjust](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c#L461-L473)).

## Local observations

| ID | Observation |
| --- | --- |
| CC-01 | Module `Rules\MapList.mes` (SHA-256 `3df16fd2bd186415da0738c25a6b7ea8a32134f5ea95817e3bcfafdbcf55843f`, 5,454 bytes, same payload as the [NPC dialog evidence](crash-site-npc-dialog-evidence.md)) has exactly two typed records: 5000 `START_MAP` (`Arcanum1-024-fixed`, `(92958, 82592)`, `WorldMap: 0`) and 5013 `SHOPPING_MAP` (`ShopMap`, `(31, 29)`). |
| CC-02 | `rules\backgrnd.mes` exists twice: `arcanum3.dat` (SHA-256 `31b968a0d0e6a234105b07e8450ffb207c8e02338137a8a9cd7c47a396bfd944`) and `arcanum4.dat` (`a34b8189839e86fe6cc7b2ff372d7140cb81c78ffbe0f467f21c6b984b1129fe`), 7,241 bytes each. Both have 79 complete blocks (0..78, 395 messages). They differ only in block 2: `arcanum3` has the money value in the condition slot (so the block is offered to no one and gives 0), `arcanum4` has an empty condition and money 400. Which copy the game reads depends on DAT precedence: `arcanum4.dat` wins ([ARC-DATA-011](content-precedence.md)). |
| CC-03 | Block 0 (the default for a new character): description id 1000, a non-empty effect id, empty condition, money **400**, no items. |
| CC-04 | Money over all 79 blocks (`arcanum4`): 400 in 65 blocks; 0 in 6; 100 in 4; 50, 200, 500 and 800 in one each. 10 blocks carry an item list; 12 have an empty effect. No condition uses `ANY`. |
| CC-05 | Blocks 39..50 (the pre-made heroes' backgrounds) each have condition `NPC`, so the new-character screen never offers them to a PC. Their money (400 each) and the one item list among them are not applied on the pre-made path (CE step above). |
| CC-06 | Backgrounds offered per race/gender (`arcanum4`, counting block 0): human 49 M / 52 F, dwarf 34, elf 36 / 38, half-elf 39 / 41, gnome 43, halfling 44, half-orc 44 / 46, half-ogre 38. With `arcanum3` each count is one lower (block 2). |
| CC-07 | `mes\MainMenu.mes` exists in `arcanum3.dat` (`ed7307cc4a6978eb5a4a17929da0a5e135a464b5d02d8f7ee41335b9057f1192`) and `arcanum4.dat` (`ffa80260c7a0f22321def8bbf8ae9b5dc251628a55ef92af19e44d9ff4c6a72a`). In both, messages 551..562 are global-flag numbers **2350..2361** in order, and 500 and 506 exist. So each of the twelve pre-made heroes has its own flag; 551..558 cover the base eight. |
| CC-08 | The level-scheme file `rules\gamelevel.mes` (both copies) and the name file `mes\gamelevelname.mes` (`arcanum3.dat`) contain records 76..87; `rules\InvenSource.mes` (both copies) contains sets 20, 22..26 and 108; `portrait\gameport.mes` (`arcanum3.dat`) contains the twelve portrait ids in the table. Only presence was checked, not contents. |
| CC-09 | The loose `data/proto` folder holds 13 `PC` prototype files, `016086`..`016098`, i.e. descriptions 16066..16078 (file number = description + 20, [mapping](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L481-L484)), all with the same timestamp as the other 1,410 files of the folder. That is consistent with the engine writing its built-in prototypes on first start, but does not prove it (an installer copy would look the same). The files were **not** decoded: that their fields equal CE's table is expected, not verified. No PC prototype was found inside the DAT archives. |
| CC-10 | Whether prototype 16075's ART exists here (and so whether this installation shows eight or twelve heroes and gender buttons) was **not** checked. |

## What Steamwoven has today

No PC object, character model, background, portrait, inventory or creation
screen exists. The AREA viewer's conversation uses a PROJECT diagnostic
profile, "male human PC with intelligence 8"
([`AreaConversationProfile.Diagnostic`](../../tools/terrain-viewer/Core/AreaConversation.cs)),
and the session owner places one actor
([game session](../world/game-session-runtime.md)). That diagnostic profile
happens to match the untouched new character of CE step 3–4 (generic
prototype: human, male; every primary stat 8); it is still not an original
new-game state, because it has no name, background, gold or clothes.

## Proposal for Steamwoven

Proposal only; each line needs its own task and owner decision.

1. **Starting-hero record first.** A Godot-independent immutable record for
   "hero at T0": prototype id, race, gender, portrait id, background id and
   description id, name, level 1, base stats, unspent points, gold and item
   prototype ids. Two factories: *default new character* (prototype 16066,
   background 0 without its effect, 400 gold from block 0, clothes rule) and
   *pre-made hero* (prototype 16067..16078). This is where the planned stats
   task ARC-WORLD-012 gets real defaults instead of the diagnostic ones.
2. **Data readers.** `backgrnd.mes` blocks with the legality rule, the
   pre-made global flags from `MainMenu.mes` 551.., and map-list typed records
   (`START_MAP`, `SHOPPING_MAP`). DAT precedence for duplicate rules files
   should come from the planned resource-precedence task ARC-DATA-011
   rather than a local guess.
3. **Pre-made heroes need two more systems.** Auto-level schemes
   (`gamelevel.mes`) for stats/skills and inventory sources
   (`InvenSource.mes`) for items and gold; without them a pre-made hero is only
   a race/gender/portrait shell.
4. **Creation UI later.** Name, race/gender with the female table and the
   16075 gate, portrait filter (FR-CHAR-004), background browsing,
   character editor and the opening shop are UI work after 1–3.
5. **Spawn.** The hero appears at the `START_MAP` location after the shop
   step, not at the shop map; the existing Crash Site selector already
   matches record 5000.

## Open questions

Answered by [ARC-RES-027](starting-hero-evidence.md) (2026-10-16): the fields
of the 13 loose PC prototypes (CC-09: they equal CE's table) and the ART gate
(CC-10: open, twelve heroes), the pre-made inventory sources and schemes
76..87, the background, race and female effects, the context backgrounds of
FR-CHAR-006 and the starting-kit prototypes of FR-CHAR-007.

Still open:

- Whether Crash Site scripts read the pre-made flags 2350..2361.
- What the shop NPC on `ShopMap` sells (barter inventory).
