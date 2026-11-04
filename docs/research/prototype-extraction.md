# Prototype extraction from the player's executable

Status: **Research**, work item ARC-RES-085 (2026-11-03), slice E1 of the fresh-install
fix. Docs-only: no code changes and no checks run (the census below ran locally, not as a repository check). It says how the original builds its
prototype files, what a build needs at run time, and how Steamwoven builds the same files
from the player's own `Arcanum.exe` on any operating system. The slices that implement it
are E2–E4 ([plan](#slices)); E2 (the interpreter) and E3 (the builder, with the answers to the open questions) are done
([E3 results](#e3-results-arc-dev-068)).

Requirements (reference only, no coverage row changes):
[FR-CONTENT-001](../requirements/content-product.md#fr-content-001),
[FR-CONTENT-004](../requirements/content-product.md#fr-content-004),
[FR-PRODUCT-005](../requirements/content-product.md#fr-product-005).

## The problem and the owner's decision

A GOG installation the original never ran has no `data\proto` folder: the installed
archives hold no prototype file, and `Arcanum.exe` builds all 1,423 of them in code and
writes them at its first start ([OM11, OM12](../architecture/original-object-model.md#3-prototypes)).
Steamwoven reads prototypes from files only ([content prototypes](../world/map-catalog.md#prototypes)),
so on such an installation the map plan refuses the first map (`AREA_MAP_PLAN_MISSINGSOURCE`,
"prototype N"; found on Windows 11 on 2026-10-29). It is not a Windows problem: an installation
unpacked from the GOG installer with `innoextract`, the usual way on macOS and Linux, is in
the same state, and there the original cannot be started at all.

Options put to the owner on 2026-11-03: (1) build the prototypes from the player's own
executable without starting it, by interpreting its prototype code; (2) import a `data/proto`
folder from another of the player's installations; (3) start the community reimplementation
once, which writes the folder itself (its README installs from the GOG installer with
`innoextract` and its own reconstructed prototype code writes `data/proto` at its first start);
transcribing the prototype values into Steamwoven's code was excluded, because it would put
original content into git. **Owner decision 2026-11-03: option 1.**

## How the original builds a prototype

Read on 2026-11-03 in the retail 1.0.7.4 executable (SHA-256 `9bd80511…8d52`) with
`llvm-objdump`, one reader (F1 for every row), then exercised by the emulated census below.
For the type defaults of PX3 and the group fill of PX4, F1 covers their call structure and what
the census showed, not the value each case sets: nobody read the 85,000 instructions case by case,
and the extractor does not need it, because it runs them.
Numbers are the code's basic prototype numbers; the file and ID number is the basic number
plus 20 ([OM3](../architecture/original-object-model.md#1-identity-handles-and-persistent-ids)).

| ID | Function (address) | Original 1.0.7.4 | Level |
| --- | --- | --- | --- |
| PX1 | prototype init 0x4681B0, check 0x468660, type of a number 0x4685D0 | The check walks 20 groups, each a range of basic numbers (tables 0x5B37FC and 0x5B384C), and builds every number whose file `proto\<ID number, 6 digits> - <group name>.pro` does not exist (0x52FB80). When it built at least one, the object system is shut down (0x4051F0) and started again (0x405110) before every `proto\*.pro` file is listed and read (0x406600): the game itself always plays from the files. The object type of a number (0x4685D0) is the count of the starts of groups 1–18 at or below it, with 18 read as 16: groups 0–17 are the 18 object types in order, and the last two groups (27309–27393 and 28310–28471) are NPCs too. | F1 |
| PX2 | build 0x468890 | One prototype: a new object of the number's type (0x405800), its name field (22) set to the type's name number (0x468720; 400 for walls, 800 for portals and so on), its ID set to a prototype ID of the number (0x408020), its fields filled by the group fill (0x468930), the file written (0x468330) and the object freed (0x405BF0). | F1 |
| PX3 | new object 0x405800, type defaults 0x409000 | The object gets a pool slot, a GUID that 0x408020 then replaces, a full value array of its type's field count, every field cleared by the field walk (0x40C880 with 0x40C6B0), and the type's defaults (0x409000, the same field-setter calls as the group fill, with a switch on the type). | F1 (structure and census) |
| PX4 | group fill 0x468930 (to 0x49A289) | One switch on the number with a case per prototype, about 85,000 instructions, almost all of them argument pushes and calls of the object field setters: the plain setter 0x406D40 (8,931 call sites), the array setters 0x407340 and 0x4074E0, the getters 0x406CA0 and 0x4078A0; the ART id builders (0x5043C0 and its family, small leaf functions); a few rule helpers (critter stats 0x4B0980, light 0x441C70, auto-level scheme 0x4A6DE0, others). It writes no global of the image (census). | F1 (structure and census) |
| PX5 | critter files 0x49B340, line parser 0x49B5A0 | Three number ranges also read a text file of the game's data through the file layer (0x5303A0, line reads 0x530540): basic numbers 17067–17316 `rules\npc.txt`, 27309–27393 `rules\unique.txt`, 28310–28471 `rules\monster.txt`. Their keys are matched against the executable's static name tables and against names that other subsystems load from MES files at start (stat names from `mes\stat.mes`, spell names from `mes\spell.mes`). | F1 |
| PX6 | write 0x468330, record write 0x406590 (0x4096B0, 0x409980), file write 0x4E7010 → 0x5306A0, name text 0x4071A0 and 0x4E7400 | The writer refuses an object whose ID is not a prototype ID. The file name is `proto\%06d - %s.pro` with the ID number and the text of the object's own name field (22) in `oemes\oname.mes` (handle loaded by 0x4E72B0); the check of PX1 builds its expected name from the group's name number instead, so the two agree only while the fill leaves field 22 alone (81 files of the census carry an object's own name, such as a unique NPC's). The record is the object's full record (version 119), written through the file layer. The bytes of the record's two 24-byte IDs that their kind does not use are copied from memory as they are, so they differ from run to run and from build to build; every comparison of prototype files must leave them out. | F1 |

## Emulated census

Research only, local: a scratch harness on the function oracle's emulator
([oracle](../../tools/original-oracle/README.md); nothing of it is kept or shipped) ran the
unchanged executable from the owner's installation:

1. the start-up functions a build depends on: the object system (0x405110), the group names
   (0x4E72B0), stats (0x4B0340), spells (0x44EF50), effects (0x4E99F0) and the critter
   experience table (0x45CF30);
2. then the build (0x468890) of each of the 1,423 numbers.

Only service functions were replaced: memory (allocation 0x58A716 and 0x58A99E,
reallocation 0x58A072, free 0x58A347), the file layer (open 0x5303A0, close 0x530330, line read
0x530540, write 0x5306A0, exists 0x52FB80) answered from the installed archives in memory,
MES loading and lookup (0x4D4290, 0x4D43A0, 0x4D45E0, 0x4D4630), the clock (0x58B846), the GUID
(0x4E62A0) and the debug log (0x4FEB70). The harness was permissive (any code of the
executable could run), so it is a census of what a build reaches, not an oracle verdict.

Results:

- All 1,423 builds returned; every one wrote its file. The builds wrote no global of the image.
- 235 functions of the executable ran (the start-up functions, the object system, the
  writer, the group fill and its helpers), plus the stubs above: 97,863 distinct instruction
  addresses of 72 instruction kinds, none of them floating point. The kinds are the 32-bit
  integer moves, arithmetic and logic on 8, 16 and 32 bits (with `adc`, `sbb`, `neg`, `not`,
  signed and unsigned multiply and divide, `cltd`), shifts (`shl`, `shr`, `sar`, `shrd`),
  sign and zero extension, `set` on a condition, the conditional and indirect jumps (jump
  tables), `call`, `ret`, `leave`, `push` and `pop`, the direction flag and the string
  instructions `rep movs`, `rep stos` and `repne scas`.
- Ten files were read: `oemes\oname.mes`, `oemes\faction.mes`, `mes\stat.mes`,
  `rules\magictech.mes`, `mes\spell.mes`, `rules\SpellEyeCandy.mes`, `rules\spelllist.mes`,
  `rules\effect.mes`, `rules\xp_critter.mes`, `mes\critter.mes` by the start-up functions, and
  the three `rules\*.txt` files of PX5 by 497 builds.
- Compared with the owner's macOS `data/proto` (written by the community reimplementation,
  see below), leaving out the unused ID bytes of PX6: every file has the same name, 1,411 have
  the same length, and **1,311 are equal**. 112 differ in fields: 56 NPCs, 15 portals, 13 PCs
  (12 of them also in length), 11 generic items, 7 traps, one scenery object and 9 critters
  named after themselves (9 of the 81 self-named files of PX6; the groups do not overlap). Which
  side is right is not known yet: the macOS files are not the original's. They serve as a
  diagnostic only; no verdict and no implementation detail is taken from them, and E3 is judged
  against the files the original wrote on Windows.
- Globals the builds read that none of the start-up functions above sets (zero in the
  harness): the local PC (0x5D1158; no PC exists yet when the original builds its prototypes,
  so zero is the original's value as well), two callbacks set from the start-up information
  (0x5E8658, 0x5E8668, called through 0x4601C0 and 0x460240 when set), the sound file index
  (0x601770, 0x601774, read by 0x4CEDC0 through 0x44BCC0), the ART name function (0x604710,
  read by the ART file name 0x51AE50), the network flag (0x62AC6C), the run-time library's
  locale (0x6873B4) and three tables read by 0x44D305. These are the open inputs of E3.

### The owner's macOS prototype files

The owner's macOS install's `data/proto` and `modules/Arcanum/maps` carry the time
the community reimplementation's build was first started from that folder, not a start of the
original (which cannot run on macOS), so the macOS real-data checks so far have read
prototypes built by the reconstruction. They are equal to the original's in most fields
(above), but not proven equal: E3 compares both sets with the files the original wrote on the
owner's Windows installation (2026-10-29).

## Design for the extractor

- **Interpret, never transcribe.** At its first start, Steamwoven reads the player's own
  `Arcanum.exe` in place, read-only, and runs the original's start-up functions and the
  prototype build of PX1–PX6 in its own x86-32 interpreter. Nothing of the executable or of
  the built files enters git, a package or another device; the built files are the player's
  local data, like an [HD pack](../formats/hd-pack.md). Only the verified 1.0.7.4 executable
  (its SHA-256) is interpreted; for any other executable the launcher shows that the
  prototypes are missing (the detection of ARC-DEV-066).
- **Interpret the object system and the writer too.** Hooking the field setters (PX4) would
  need a Steamwoven writer of the record format and an exact model of every rule helper the
  fill calls. Interpreting the whole build, the object system and the record writer
  included, gives the original's bytes by construction; only the service layer of the census
  is Steamwoven code: memory, the file layer over the content stack (opened before the first
  build, since the content stack's own prototype source is what the build fills), MES loading (or the MES
  code itself over the file layer), the clock, the GUID and the log. The builds are
  independent of each other and write no global, so they can run one after another in one
  interpreter after one start-up.
- **The interpreter** reads the PE image (sections, no imports or relocations: the image
  base is fixed), keeps a flat 32-bit memory with the image, a stack and a heap, and executes
  the integer subset the census reached (above), with the flags those instructions set. Any instruction outside the subset, any jump or call outside the image
  that is not a service hook, and any access outside the mapped memory stops the build with
  the address, so an unknown path is found, never guessed. It needs no floating point.
- **Where the files go.** Into a folder of Steamwoven's settings folder, keyed by the
  executable's SHA-256 and the identity of the data it read (`rules\*.txt` and the MES files),
  mounted by the content stack as a base source after `data/`, where the original writes them
  ([PX1](#how-the-original-builds-a-prototype), [content precedence](content-precedence.md)).
  An installation that already has `data/proto` keeps it.
- **Time.** The scratch harness took 11 minutes for all 1,423 builds, almost all of it in the
  key matching of PX5; a C# interpreter without a per-instruction hook should take seconds
  (estimate; the E2 interpreter runs about 100 million instructions per second, [measured](../Arcanum.Executable.md#verification)).
  The launcher shows the build's progress once and stores the result.

## Slices

| Slice | Code | Content | Done when |
| --- | --- | --- | --- |
| E1 | ARC-RES-085 | This document | Merged |
| E2 | ARC-DEV-067 | PE image reader and x86-32 interpreter (Godot-independent), service-hook interface, census mode that lists every executed function and instruction kind ([contract](../Arcanum.Executable.md)) | Synthetic machine-code tests for every instruction kind and flag rule; no original bytes in tests |
| E3 | ARC-DEV-068 | Prototype builder over the player's executable: the start-up functions, the services over the content stack, the original's own check and build loop, the captured files; the open inputs of the census settled by reading | **Done:** all 1,423 files equal to the files the original wrote on the owner's Windows installation (an opt-in real-data test; the Windows files stay local); the macOS differences listed ([results](#e3-results-arc-dev-068)) |
| E4 | ARC-DEV-069 | The cache, the content-stack source, the launcher's first-start build and progress, the ARC-DEV-066 detection as the fallback for an unverified executable | A fresh installation (no `data/proto`) starts the game on macOS and Windows |

## E3 results (ARC-DEV-068)

The builder ([contract](../Arcanum.Executable.md#the-prototype-builder)) runs the original's own functions in the E2 interpreter:
the 17 game modules' inits and the first nine object modules' inits in the original's order, then the prototype module's check
(0x468660), which composes `proto\%06d - %s.pro` for each of the 1,423 numbers and calls the build (0x468890) for every file the file
layer does not report. Only the system services are Steamwoven code. On the owner's Mac (Release, Apple silicon) a fresh installation takes
about 2.43 × 10^9 interpreted instructions and 26 s.

- **Compared with the files the original wrote** (the owner's Windows installation, CrossOver, read only, 2026-11-04): all 1,423
  names equal and all 1,423 files equal field by field, read through the repository's own v119 record reader (the unused bytes of the IDs
  of PX6 and the storage handle of an array are not part of what the reader returns). The one thing the original leaves to chance is
  **word 0 of an element of the script array** (slot 31, 65 values in the whole set): it is a stack or heap address the original copies
  without having set it. A second build over other uninitialized memory (another fill byte, stack and heap base) differs from the first in exactly
  these values and nowhere else, so the comparison leaves them out; this is checked, not assumed.
- **Questions 1 to 6** of the plan, answered from the executable:
  1. *Callbacks.* The start information every module init receives (built by 0x4020F0) holds the editor flag at +0 (0), the main window's handle at +4 (a fixed 1 here) and two window callbacks at +8
     (view change) and +0xC (redraw) that the original's window code supplies; the host answers them with stubs. The view change is called once, by the location
     module's init, and the redraw never; no prototype depends on either (all files equal).
  2. *The "sound file index" and the ART name function.* 0x601770/0x601774 is the index of the script files (the `scr\*.scr` listing that
     0x44BCC0 parses into numbers, read by 0x4CEDC0 for the NPC scripts), not a sound index; it needs the base sources' `scr` directory. The ART name
     function 0x41DAE0 is installed by the window library's art module (table entry 0x500590, called with colour depth 16 from 0x401100):
     the host writes the module's tables and its cache limit and lets the code do the rest. The art file frame counts of the AID fields (the last frame of an
     animation, the first frame of a critter) come from reading the art files' headers through the file layer
     (`art\tile`, `art\critter`, `art\monster`, `art\unique_npc` listings, the unique and monster NPC art, the trap eye-candy art).
     The display's colour conversion (0x52C560) and window rectangle (0x51CEB0) are answered with fixed values; nothing a prototype holds
     comes from them.
  3. *The tables read by 0x44D305.* The function 0x44D2F0 scans a table of 216 records of 0xD38 bytes (0x68770C to 0x739E4C) for a record in
     use (flag bit 0 set, bit 1 clear) whose fields +0x14 and +0x18 equal its two arguments: the run-time table of active spell
     effects. A prototype build asks it 647 times (one caller, 0x423316) and finds no record in use, so no part of the spell start-up has to run further; the
     table is filled by casting.
  4. *Order.* Game modules 0 to 16 (table 0x59A330, 0x2C bytes a record, init at +4), then inside the map module's init (0x40EA90) the object modules `Scroll` to
     `Obj` (table 0x59F068, 0x34 bytes a record), then the prototype module (0x4681B0) whose check is the build loop. The two tables
     are read from the executable, not written down in the builder.
  5. *Does the check find the self-named files?* No, and not only those 81: on an installation that has all 1,423 files the check still
     builds **307** of them again at every start, because the name it composes from the group's name text differs from the one the build writes (the object's own
     name text): 85 NPCs of the group named "6401 Monster" and 162 of "6402 Unique NPC" (written as "NPC"), and 60 items (such as `Weapon`
     against "5678 Auto Loading Mechanism"). The rebuilt files equal the first build (the same test checks it). The builder's `ExistingFiles` option
     reproduces this.
  6. *The 112 files that differ from the macOS set.* Against the original's files the macOS set (the community reimplementation's build in
     `/Applications/Arcanum/data/proto`, 2026-11-04) has 1,373 equal and 50 different files; 62 more differ only in the unset
     word 0 above. The 50: 15 portals (file numbers 2028 to 2042, two fields: the portal flags, 805319680 against 805319697), one scenery object
     (4042, one field), 11 generic items (15152, 15170 to 15173, 15181 to 15186; one field), one PC (16086, a text), 19 NPCs (28330 to 28333,
     28396 to 28407, 28479, 28484, 28486; the field after the flags, 0 against 4, 8, 16, 32768, …) and 3 NPCs (27331, 27339, 27340; script
     element words). In each of them the original's value is the reference. Which are reconstruction slips and which are the
     original's own behaviour is for the task that consumes them (D8 "Original" and "Steamwoven" settings).

Not done in E3: the cache, the content-stack source for the built files and the launcher's first-start build and progress (E4),
and the use of the builder outside tests.
