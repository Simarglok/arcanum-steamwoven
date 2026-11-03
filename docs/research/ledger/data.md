# Original verification ledger: Data and loading

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Art names and missing art (ARC-RES-050)

Source: [whole-map art gaps](../whole-map-art-gaps.md#how-the-original-names-critter-art-and-falls-back).
Read on 2026-10-17 by a first reader and, independently, a second reader that saw only the questions
(method step 6); both agree on every behaviour and verdict of MA1–MA5 and MA7. Every direct branch of the functions read for those rows stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MA1 | `fix_missing_art` (0x41DA40), tables 0x5A106C, 0x5A10B8, 0x5A1104, 0x5A1150 | Nothing happens before the masks are loaded or for a number outside the list. The 19 checked pairs are tried in order: a set bit whose animation equals the current one (and, for low attack 21 only, whose weapon equals it) replaces the animation (walk, run → walk; stunned, stand → stand; ten low attacks → attack 20; decapitation, blown-out chunk, severed leg, explode → fall down; stealth walk → walk). A walk replacement sets weapon 0 when walk unarmed is missing; a stand replacement with weapon 0 sets weapon 1 when stand without a weapon is missing. Tables as the art-gaps study lists. | Original |
| MA2 | `name_normalize_aid` (0x41D510), critter, monster and unique NPC branches | Critters: elf female → human body; plate armour (4, 6) → male, elf → human, halfling → dwarf. Then the repair (MA1), then: explode clears armour, shield and weapon; stunned sets weapon 1 and clears the shield; weapon 1 with stealth walk or conceal fidget → weapon 0; animations 6–19 clear weapon and shield; walk clears the shield. The id is rebuilt with frame, rotation and palette 0. Monsters and unique NPCs: the same without the body rules. On explode the critter branch sets the **male** gender where the earlier reading wrote female; the explode file name ignores the gender, so nothing drawn changes. | Original; the explode gender is a reconstruction slip with no consumer |
| MA3 | `name_resolve_path` (0x41DAE0) | Normalizes the id (MA2) before building any name; monster and unique NPC names are folder and file name from the two name lists, armour code (monster only), shield `X`/`S`, weapon letter (two-handed sword with a shield written as sword) and animation letter. | Original |
| MA4 | missing-art table load (0x41D1F0), init (0x41CE60) | `art\missing.dat`: a header that must equal the current count of critter, monster and unique NPC ART files, then 10 critter masks, one per monster and one per unique NPC (list counts). A short read or a header mismatch rebuilds the masks by checking each pair's file and saves the file. The unique NPC read is compared with the unique count; the earlier reading compared it with the monster count. | Original; reconstruction slip in the count check |
| MA5 | art cache load (0x51AA90) | When the resolved file cannot be loaded it logs the failure and loads `art\badart.art` under the same art id; only when that fails too does it return no entry (nothing drawn). The frame lookup that follows (`tig_art_frame_data`, not read in the executable) indexes the loaded file's frame table with the requested rotation and frame, unchecked; `badart.art` has one frame. | Original (load fallback); frame lookup F0 |
| MA6 | art cache entry load (0x51B170) | A one-rotation ART (flag 1) serves every rotation from its only group. | Not checked (already the whole-map rule of ARC-DATA-016, F0) |
| MA7 | `a_name_wall_aid_to_fname` (0x4ECEB0), `build_wall_file_name` (0x4ECFC0) | At rotations 2, 3, 6, 7 the damage bits `0x400` and `0x80` swap. `0x400` turns piece 7 into piece 0 and names `L`; `0x80` turns piece 8 into piece 0 and names `L` for pieces **1–6**, else `R`; no damage names `U`. Interior name for rotations 0, 1, 6, 7, else exterior. The earlier reading wrote pieces 2–6 for `L`. | Reconstruction slip (piece 1) |
| MA8 | `object_draw` (0x43B390), `a_name_tile_aid_to_fname` (0x4EAE90) | Not read in the executable: in the earlier reading neither the object draw nor the resolver treats a tile id on an object differently from other art; rotation and frame of a tile id read 0. | Not checked (F0) |

## Prototype phase (ARC-DATA-026)

Source: [map catalogue prototypes](../../world/map-catalog.md#prototypes) and the
[mount sequence](../content-precedence.md#mount-sequence-source-facts). Question: are prototype files read from the sources
mounted before the module only, so that a module, its patches or its folder can never supply one? Read on 2026-10-17 by a
first reader and, independently, a second reader that saw only the questions (method step 6); both agree on every
behaviour below. Every direct branch of these functions stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| Prototype phase | `gamelib_init` (0x4020F0), `map_init` (0x40EA90), the map subsystem table (0x59F068), `proto_init` (0x4681B0), `gamelib_mod_load` (0x402A50), `map_mod_load` (0x40ED10) | `gamelib_init` mounts the base data (0x404A20) before its loop over the subsystem init functions; Map is one of them, and its init runs every map subsystem's init. The Proto entry of the map table has only an init (0x4681B0) and an exit: no reset, module-load or module-unload function. `proto_init` restores any missing generated prototype, then lists `proto\*.pro` and reads every listed file. `gamelib_mod_load` mounts the module (0x404C10: `.dat`, patches, folder) and only then runs the module-load functions; Map's loads `Rules\MapList.mes` and the Effect and O_Name module tables, nothing of Proto, and nothing else calls `proto_init`. So prototypes come from the base sources only and a module's copy is never read. The second reader adds that a "partial" install mounts the CD's `arcanum*.dat` (and module paths) too, which the earlier reading left out; those are still mounted at the same points, so the phase is unchanged. | Original |

The XP3 wrap (ARC-RES-051): the trial ran the three functions XP3 and XP4 name in an emulator on the unchanged
executable with 4,000 seeded inputs each against the unchanged `ExperienceRules` (method section
[Function emulation](../original-executable-check.md#function-emulation-oracle)). To-next and bonus points agreed on
every input; progress differed on 269 inputs, all with to-next above 2,147,483, and agreed on all of them once only the
C# multiplication was made 32-bit in a scratch copy. A planted mutant (every fourth level instead of every fifth) was
caught. Steamwoven multiplies in 64 bits and `ExperienceTable.Create` admits thresholds up to 2,000,000,000, so a
table with a step above 2,147,483 shows a different progress. Owner decision 2026-10-17: a rule-mode switch, Original
wraps and Steamwoven keeps the exact value ([registry](../../world/rule-modes.md#registry), implemented by ARC-WORLD-033;
the function oracle of ARC-DEV-007 finds no difference in Original mode).

## Load phases (ARC-DATA-027)

Source: [lighting sources](../../rendering/area-lighting.md#sources-and-load-phases-arc-data-027), the
[starting hero](../../world/starting-hero-runtime.md) and `ContentPhase`. Question: which files does the original read while
only the base sources are mounted (an init function; then `tig.dat`, the game folder, `arcanum*.dat` and `data/` can
supply them), and which at module load or later (every source)? Background: the [prototype phase](#prototype-phase-arc-data-026)
row; `main` calls `gamelib_init` (0x4012FF), then the UI library's init (0x53E600, at 0x401411), then `gamelib_mod_load`
(0x401444). Read on 2026-10-17 by a first reader and, independently, a second reader that saw only the questions (method
step 6); both agree on every row. The second reader adds that `mes_load` (0x4D4290) keeps a name cache, so loading a file
that is already loaded returns the loaded copy; that later module loads (`gamelib_mod_load` has six more callers) read the
module-load files again but never the game-start ones; and that `rules\gameinit.mes` is skipped when a flag set by
gameinit's init (0x4B9BC5) is set, whose meaning was not identified.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LP1 | `effect_init` (0x4E99F0), `level_init` (0x4A6620), `quest_init` (0x4C45C0), `invensource_init` (0x4BF390), `name_init` (0x41CA40) with `a_name_light_init` (0x4ED1E0) and `a_name_tile_init` (0x4EAC80) | Init functions of the gamelib table (Level entry 16, Quest 31, Inven-Source 46, Name 6) and of the map table (Effect). They load `rules\effect.mes`, `rules\xp_level.mes`, `Rules\xp_quest.mes`, `Rules\InvenSource.mes` and `Rules\InvenSourceBuy.mes` (through two name pointers), and Name's init calls the light and tile name loaders, which load `art\light\light.mes` and `art\tile\tilename.mes`. None of these subsystems has a module-load function that reads them again. | Original: base sources only |
| LP2 | `mainmenu_ui_init` (0x540930) | An init function of the UI library, which `main` runs before `gamelib_mod_load`; it loads `mes\mainmenu.mes` into the main menu's handle unless that handle is already loaded. The same init also loads it into a second handle through 0x585BC0 (the name cache returns the loaded copy; the earlier reading omitted this call); a third load site (0x5443E0) has the same guard and no reference (dead code). So no load after the module is mounted can replace the init-time copy. | Original: base sources only |
| LP3 | `gameinit_mod_load` (0x4B9C30), `map_mod_load` (0x40ED10) with `map_list_info_load` (0x4125C0), `light_scheme_mod_load` (0x4A7C90), `quest_mod_load` (0x4C4670) | Module-load functions (gameinit entry 61, Map 17, LightScheme 18, Quest 31) load `rules\gameinit.mes`, `Rules\MapList.mes`, `Rules\Lighting Schemes.mes` and `rules\gamequest.mes` after the module is mounted. | Original: every source |
| LP4 | `light_scheme_set` (0x4A7DD0), `map_load_extension` (0x4115D0) | A scheme file is loaded when a scheme is set, by the name the scheme catalogue gives (`Rules\%s.mes`); `mapinfo.txt` is opened from the map's base path when the map opens. | Original: every source |
| LP5 | `name_init` (0x41CA40) with the tile, facade, wall, portal and item name loaders (0x4EAC80, 0x4EC370, 0x4EC9B0, 0x4EC5A0, 0x4EC190), `name_missing_art_init` (0x41CE60) and `name_missing_art_load` (0x41D1F0) | Name's init (gamelib entry 6: no reset, module-load or module-unload function; it returns at once when already initialised) loads `scenery.mes`, `unique_npc.mes`, `monster.mes` and `container.mes` itself and the tile, facade, wall-name, wall-structure, portal and item-ground tables through loaders it calls once; each path has one code reference and the tables stay loaded until exit. `art\missing.dat` is read only from the missing-art init that Name's init calls (when it cannot be read, the art is probed and the file written); there is no later re-initialisation (the missing-art lookups return at once while it is not initialised). (ARC-DATA-027 part 2) | Original: base sources only |
| LP6 | `map_load_mobile` (0x410D10), called from `map_open` (0x40F4E0) | `Rules\map.patch` is checked, opened and applied line by line each time a map opens (outside the editor) and closed; only the patched objects remain. (ARC-DATA-027 part 2; the multiplayer caller of `map_open` was not examined) | Original: every source |

Not checked in the executable: when a light ART file is first opened (the earlier reading: when it is drawn, through the ART cache);
the readers take it from the stack winner, as every ART since ARC-DATA-018.

## Map patch (ARC-DATA-028)

Source: the [map patch contract](../../world/map-patch.md). Question: when and how does the original apply
`Rules\map.patch`, and to which objects? Read on 2026-10-18 by a first reader and, independently, a second reader that
saw only the questions (method step 6); both agree on every row, and the second reader added the line-buffer overrun,
the npc-flag table's null entry and the editor-mode branch. Every direct branch of these functions stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| MP1 | `map_load_mobile` (0x410D10), `map_open` (0x40F4E0, call at 0x40F794) | The patch is read on every map open (new game, map change, save load), after the map's mobile file and the save's difference and deletion files and before terrain, jump points and any sector, so it overrides restored save state and reaches only objects already loaded (a sector object only through a `P` ID, by a location search against the previous map). Editor mode skips it. | Original |
| MP2 | the same | Text mode (CR LF read as LF). `fgets` is given 4,196 bytes but the buffer holds 4,096, so a read of 4,096 characters or more overruns the stack (latent). A line starting with `#` or LF is skipped; a read without LF before the end stops the file; the last character is always cut, so a final line without LF loses its last real character. | Original bug (overrun, latent); otherwise Original |
| MP3 | `objid_id_from_str` (0x4E66B0), GUID parser (0x4E67A0), hex reader (0x4E6AA0) | Upper-case kinds only: `A`, `G`, `P`, `N`, `B`; `H` and others fail and are logged, and the line is skipped. A `G_` GUID's first group is stored as a 32-bit number, the next two as 16-bit numbers and the rest byte by byte: the Windows memory layout, as object files store it, so lines do find their objects. Each group is read by `strtoul` after a check of its first character only. Text after the GUID is not checked. | Original (the object files' layout, so `G_` lines find their objects) |
| MP4 | `map_apply_obj_patch` (0x411940), token reader (0x411880) | The attribute text starts at the line's first `:`; a found object on a line without one dereferences a null pointer (a crash). Keys and values compare ignoring case, tokens split on C white space, numbers are `atoi` (stand points `_atoi64`, x low and y high). Keys and fields: `internal_name` 22, `known_name` 23, `alignment` stat 19, `origin` 291, `notify_npc` 49 on a portal and 62 on a container (others read the value and change nothing), `magic_hit_adjust` 115, `obj_flag` 19, `portal_flag` 46, `item_flag` 87, `critter_flag` 218, `npc_flag` 280, `training`, `script` 32, `daystand` 289, `nightstand` 290, `magic_tech_complexity` 96, `level_scheme` 245, `faction` 292; a field the object's type lacks is not written. An unknown key stops the line. A flag key reads the field, then sets the named bit, or clears it after `!`; a name not found writes the field back unchanged, except in the npc table, which ends in a null entry, so an unknown npc flag name fails there. | Original bugs (missing `:` and unknown npc flag fail) |
| MP5 | `stat_base_set` (0x4B0980) | `alignment` changes only a PC or an NPC: clamped to −1000..1000, never refused (no skill depends on it); for a PC whose value changes, its followers re-check their leader. | Original |
| MP6 | `basic_skill_training_set` (0x4C6170), `tech_skill_training_set` (0x4C6850) | `training` takes a tier word (`st_untrained`, `st_beginner`, `st_expert`, `st_master`) and a skill name; the tier may rise one step at a time and needs a skill level of 0/1/9/18; the educator background then teaches followers. Another word leaves the skill name to be read as a key. | Original |
| MP7 | `script_load_hdr` (0x44BCC0), `script_name_build_scr_name` (0x4CEDC0) | `script` attaches at the given slot the script's 8-byte file header (flags, counters) and its number, but only when the number names a script file: number 0 or an unlisted number attaches nothing and logs nothing, so `script N 0` cannot clear an attachment. | Original bug; owner decision 2026-10-20 (ARC-DATA-037): Steamwoven empties the slot, Original switch `MapPatchScriptZeroIgnored` (the installed patch has five such lines) |
| MP8 | `obj_arrayfield_store` (0x4088B0), 0x40D2A0 | Writing one element of an array field the instance does not hold first copies the prototype's whole array. | Original |

## Limits census (ARC-DATA-029)

Source: the [limits census](../../world/limits-census.md). Question: how many maps can the original's map list hold, which
bounds the maps a session can have changes on? Read on 2026-10-18 by a first reader and, independently, a second reader
that saw only the question (method step 6); both agree.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LC1 | `map_init` (0x40EA90), `map_list_info_load` (0x4125C0), `map_list_info_add_internal` (0x412C90) | The map list is one table of 200 entries of 280 bytes, allocated at map initialisation. The loader reads the map list rules from message number 5000 upwards and stops at the first gap; after each entry it stops when 200 are stored, unloads the file and reports success, so further entries are ignored without a log. Adding one map later refuses when the table is full. No other path adds an entry (the other writers reuse an existing slot found by name; the unload resets the count). A name of 256 characters or more fails the load. | Original |

## Implemented functions (ARC-DEV-025)

Source: the [function registry](../../verification/functions.csv) rows that Steamwoven implements and that stood at F0 (taken
from earlier readings, not read in the executable). Question: what each function does for the part Steamwoven implements,
and where the implementation differs. Read on 2026-10-18 by a first reader and, independently, a second reader that saw only
the questions and the code (method step 6), with function ends from the executable and constants from its name tables and
the project's records. Both agree on every row; details only one reader followed are marked. Three differences of the map
list (short rows, negative coordinates, the number conversion) the second reader first listed outside the covered part;
asked to re-read them, it confirmed all three. Every direct branch stays inside
`.text`. The stat-name table at 0x5B5384 names stats 0–20 in code order only: it has 23 names, poison sits at its slot 21,
while the code reads the poison level as stat 24 (the setter and the strength and dexterity penalty).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IF1 | map name obfuscation (0x411830) | Each ASCII letter moves 13 places within its case; one that passes Z (or z) moves back by 24, not 26; other bytes stay; then the whole string is reversed. | Original; Steamwoven the same |
| IF2 | map list load (0x4125C0), token readers 0x5318F0 and 0x531A20, number conversion 0x589CB8 | Rows numbered from 5000 until the first missing number, at most 200. Name, X and Y are the first three comma-separated values: a name of 256 bytes or more fails the whole load and the game start; trailing white space stays in the name; a row with fewer than three values dereferences a null pointer (crash); an empty value reads 0; X and Y are read with the runtime's 64-bit `atoi`, which wraps on overflow, and are kept when negative. | Original (the short-row crash is an original bug; the long-name failure is the original's limit, LC1); Steamwoven accepts a long name, trims the name, keeps a short row without a start, drops a negative start and saturates on overflow |
| IF3 | armour class (0x43D880), item getters 0x461590 and 0x4614A0 | Over worn slots 1000–1008, each armour item adds its armour class adjustment plus its magic-adjusted magic armour class adjustment. With the flag argument set (callers 0x464700 and the interface at 0x556782) an unidentified magickal item adds no magic part; the attack path (0x4C8760) passes it clear. | Original; Steamwoven implements the attack path (identification not modelled) |
| IF4 | critter experience table load (0x45CF30), critter worth (0x45F0B0) | The loader opens `rules\xp_critter.mes` (and eleven texts of `mes\critter.mes`) and converts no value; the worth looks up the NPC's level as an exact entry when it is needed and converts it with `atoi`, 0 without an entry. | Original; Steamwoven converts every entry once and refuses a non-number, so a bad entry that no level reaches refuses the whole table |
| IF5 | quest experience table load (0x4C45C0), quest experience (0x4C53C0) | The loader allocates the quest arrays and opens `rules\xp_quest.mes` (a failed open fails the module start) and converts no value; 0x4C53C0 looks the level up as an exact entry with `atoi`, 0 without an entry. | Original; Steamwoven as in IF4 |
| IF6 | quest list module load (0x4C4670), entry parser 0x4C4860 | Entries 1000–1999 of `rules\gamequest.mes`, split on spaces, 23 numbers by `atoi`, the first the quest's experience level; without the file every level is 1. An entry with no token or fewer than 23 numbers hands a null pointer to `atoi` (crash). | Original (the crash an original bug); in Steamwoven an empty entry or a first token that is not a number refuses the whole table, and an entry with fewer numbers is read |
| IF7 | critter kill (0x45D900), damage (0x4B4390) along the kill's path | The damage step runs with the death flag and the current hit points as damage. An invulnerable (tested first), nigh-invulnerable or dead critter stops it; otherwise the hit-point damage grows by the current hit points and the poison is written through its setter (0 for an extraordinary constitution); a critter dead after these writes falls with the plain death; hit-point damage is then set to 32,000 in every case. A damage script on the target can cancel the step. | Original; Steamwoven reports a dead invulnerable critter as already dead where the original reports nothing (the state is the same); scripts are not modelled |
| IF8 | fatigue recovery event (0x45E610) | Steps = max(1, (now − scheduled) / 80), both times cut to whole seconds first, measured from when the event was scheduled; no upper bound; a time before the schedule gives 1. | Original; Steamwoven caps at 1,000,000 steps and refuses a time before the schedule |
| IF9 | resting event (0x45EA00), PC leader 0x45DDA0 | Hours = max(1, (now − scheduled) / 3,600), signed 32-bit and truncated, for an NPC without a PC leader (at the end of its leader chain, second reader). | Original; Steamwoven caps at 87,600 hours and refuses a time before the schedule |
| IF10 | item transfer (0x4617F0 → 0x461810), insert check 0x466510 | The entry calls the transfer with location −1. Ground path in order: the item's script point 4 (it can cancel), the insert check (weight, then technophobia, then a stack or room), off the map, into the inventory list; then the insert-item script, which cannot cancel (first reader), and the decay timer is removed (second reader). A key ring is an ordinary item. | Original; Steamwoven refuses items with a point-4 script, gold, keys and key rings (ammunition merges into a held stack since ARC-WORLD-066, [AM1](items.md#ammunition-arc-world-066)), runs the insert-item script through the host's SCR dispatcher (ARC-SCRIPT-013; refused when it uses more than the item set admits) and leaves the insert check to the host |
| IF11 | weapon skill of an item (0x465D80) | No item: melee. Not a weapon: throwing. A weapon: the boomerang flag gives throwing; complexity below 0 with range 3 or more and ammunition type 1–3 gives firearms; ammunition type 0 gives bow; anything else melee. | Original; Steamwoven implements the weapon branch only and reads complexity −2^31 as firearms (the original: melee; no data has it) |
| IF12 | stat getter (0x4B0490) | Before effects and the clamp: poison lowers strength and dexterity by min(3, poison / 100), the poison being the effective level of stat 24; both crippled legs lower speed by 5. The getter also adds ±10 speed under the Tempus Fugit flag, one follower for a training of 2 or more in skill 11 and the PC background's ±2 (stats 0, 1, 4, 6) and ±4 (stat 22). | Original; Steamwoven implements poison (from the stored level; no Steamwoven effect targets poison) and crippled legs only |

## Objects without ART (ARC-DATA-038)

Source: a main-map window around Caladon (sector (1602, 1825)) holds a scenery object (prototype 4031, at (102582, 116816))
whose ART id is 0xFFFFFFFF and which its serialized state does not hide; the area owner refused the whole window for its
ART class, so the transfer out of the Panarii temple (arrival (102647, 116816)) did not load. Read on 2026-10-21 by this
task and, independently, by its reviewer (method step 6). Every direct branch read stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| NA1 | ART name of an ART id (0x41DAE0: stored at field +0x1C of the init block by 0x40124E; the ART init 0x500590, called through the module table of 0x51F130, copies it to 0x604710 at 0x500659, and the ART file name 0x51AE50 calls it for every ART type but 8), ART cache lookup (0x51AA90), frame data of ART (0x5031C0), object rectangle (0x43DFD0), object draw body pass (0x43B390) | The callback first normalises the id (0x41D510, which returns an id of type 15 unchanged), then tests the type (bits 28-31) against 0-7 and 9-14; type 15 matches none and the callback returns error 0x10 with no file name. The cache lookup returns -1 when the name fails, and frame data returns error 13 on a -1 cache slot. The object rectangle reads the object's current ART and, when its frame data fails, returns an empty rectangle; the draw's body pass intersects that rectangle with each dirty rectangle (0x52D1A0, which reports an empty width) and blits nothing. So an object whose ART id is 0xFFFFFFFF draws no body. The shadow and overlay passes before the body pass are not read. | Original |

## Generated dialog files (ARC-DATA-042)

Source: the [generated dialog files](../../formats/generated-dialog-files.md) contract and the greeting questions of ARC-WORLD-058
(R12 of the [reaction study](../npc-reaction-reputation-evidence.md)). Question: which `gd_*` files does the dialog module open,
when and how, how does a generated line pick its entry, and which file does an NPC line read? Read on 2026-10-21 by two fresh
agents that saw only the questions and the project's own records of the dialog state (method step 6). They agree on every
row; the rows are F2. Every direct branch and jump table read stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| GD1 | file names (table 0x5A063C), dialog init and exit (0x412D40, 0x412D80) | 32 MES handles in a fixed order: `gd_pc2m`, `gd_pc2f`; `gd_cls_{m2m,m2f,f2f,f2m}`; `gd_cls_pc2m`, `gd_cls_pc2f`; `gd_rce_*`; `gd_npc_*`; `gd_dumb_pc2m`, `gd_dumb_pc2f`; `gd_cls_dumb_pc2m`, `gd_cls_dumb_pc2f`; `gd_cls_dumb_*`; `gd_rce_dumb_*`; `gd_sto_*` (the gender pairs always in the order m2m, m2f, f2f, f2m). The first letter is the speaker's gender, the second the listener's; gender value 1 selects "m". Init only marks every handle unopened; exit closes them. | Original |
| GD2 | generated file open (0x418250) | A file is opened on its first use with the path `mes\%s.mes`, and stays open until the dialog exit. A file that does not open prints an error naming the file and exits the program. | Original; Steamwoven reads every file at module load (the data does not change in between) and keeps a missing one absent, its lines reported as such (project policy: no exit) |
| GD3 | MES run count (0x4D4550) | Given a first and a last number: 0 when the file has no entry numbered exactly the first; otherwise the number of stored entries, in number order from that one, numbered at most the last. Whether the numbers are consecutive is not checked. | Original |
| GD4 | MES entry fetch (0x4D43A0) | The entry of the number; a missing one yields, in place of a line, a static error message that names the missing number and the file. | Original; Steamwoven returns a typed missing result and the caller decides what is shown |
| GD5 | NPC-line generators (0x418480 class, 0x4185F0 race, 0x418780 NPC lines) | Each reads the file of its group by the NPC's (speaker's) gender and then the PC's: `gd_cls_*` or, for a dumb NPC (0x45FC00), `gd_cls_dumb_*`; `gd_rce_*` or `gd_rce_dumb_*`; `gd_npc_*`, which has no dumb variant. The entry is the first of the range plus a uniform random number from 0 to the GD3 count less one. The rest of each generator (ranges, offsets, override lines) belongs to the greeting slice. | Original |

Anomaly (for the caller, no switch here): GD-A1 (GD3, GD5): the pick adds a random offset to the first number while the count
counts stored entries, so a block with a gap can pick a missing number (GD4's message) and never reach its last entries; without
the first entry the count is 0 (what the pick then gives is the greeting slice's row). In the installed files, 2 of the 242 aligned 50-number blocks of each `gd_cls_*`
file, 1 of 33 in `gd_cls_pc2f` and 10 of 68 in each `gd_npc_*` file have a gap (ARC-DATA-042 census; which real ranges hit one
is the greeting slice's census).

## Prototype extraction (ARC-RES-085)

Source: [prototype extraction](../prototype-extraction.md#how-the-original-builds-a-prototype), the fresh-install fix
(owner decision 2026-11-03: build the prototypes from the player's own executable). Question: what does the original run to
build a missing prototype file, and what does that build read? Read on 2026-11-03 by one reader (F1) and exercised by a local
emulated census of all 1,423 builds (the document's census section); the rows' detail is in the document.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PX1 | prototype init 0x4681B0, check 0x468660, type of a number 0x4685D0 | Every missing `proto\NNNNNN - name.pro` of the 20 number groups is built; after any build the object system restarts, then every file is read. | Original; Steamwoven builds the missing files at its first start (E3, E4) |
| PX2 | build 0x468890 | New object of the number's type, name field, prototype ID, group fill, file write, free. | Original; interpreted (E3) |
| PX3 | new object 0x405800, type defaults 0x409000 | Pool slot, full value array cleared, type defaults by field-setter calls. | Original; interpreted (E3) |
| PX4 | group fill 0x468930 | One switch case per number of field-setter calls and small helpers; writes no global. | Original; interpreted (E3) |
| PX5 | critter files 0x49B340, parser 0x49B5A0 | Three number ranges read `rules\npc.txt`, `rules\unique.txt` or `rules\monster.txt`, keys matched against stat and spell names loaded at start. | Original; the file layer reads the content stack (E3) |
| PX6 | write 0x468330, record write 0x406590 | The full record through the file layer, named after the object's name text; the unused bytes of its two IDs come from memory and differ from run to run. | Original; comparisons leave the unused ID bytes out (E3) |
