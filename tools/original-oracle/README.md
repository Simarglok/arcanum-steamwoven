# Function oracle (ARC-DEV-007)

Runs single functions of the unchanged retail 1.0.7.4 `Arcanum.exe` in a CPU emulator and writes seeded inputs with
the original's outputs to a local folder outside git; the `OriginalExe` tests compare Steamwoven with them. Method and
rules: [function emulation](../../docs/research/original-executable-check.md#function-emulation-oracle).

```sh
python3 -B tools/original-oracle/oracle.py --self-test        # root check; synthetic code, no game file
./arcanum oracle [--function ADDR | --all] [--exe PATH]       # generate, then run the OriginalExe tests
python3 -B tools/original-oracle/oracle.py generate --all --exe "$ARCANUM_EXE"   # generation only
python3 -B tools/original-oracle/oracle.py list
```

## What a run guarantees

- The executable is read once, read-only; its SHA-256 must be
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52` or nothing runs. The PE image is mapped in the
  emulator's memory only.
- Each function runs as one cdecl call; it may read only its stack frame (arguments included), the image globals and
  the heap data (the current table) its model names. Only its own code range and the ranges of game functions it is
  allowed to call execute (all inside `.text`); service functions are stubs that answer from one fictitious world model
  and reject anything else (a stub answers EAX, or EAX and EDX for a 64-bit object handle). A run fails on any other callee, a stub rejection, any other read, any write but the stack, a fault, the instruction limit (no return), an unbalanced stack or a callee-saved
  register not restored. Generation stops at the first failed run.
- Inputs are drawn from a seed (default 20261017, at least 10,000 per function) inside the C# side's domain, so the
  same model is the input of both sides. Output: `<out>/<ADDR>.json` (default `research-private/oracle/`, or
  `$ARCANUM_ORACLE_DIR`; a folder in the work tree outside `research-private/` or `local/` is refused). The files hold
  only synthetic inputs and the original's results; they stay local all the same.
- The `OriginalExe` tests (`tests/Arcanum.Simulation.Tests`) are inconclusive without `ARCANUM_EXE` or the files, check
  the executable's SHA-256 and the one each file records, and replay at least two planted mutants of the C# side per function, failing
  unless each is caught.

| Function | Name | Model | OriginalExe test |
| --- | --- | --- | --- |
| 0x4A68B0 | experience to next level | stat getter stub (level 17, experience 18), threshold-table pointer 0x5F5C94 | `ToNextLevelMatchesTheOriginal` |
| 0x4A6900 | experience progress | as above, may call 0x4A68B0 | `ProgressMatchesTheOriginal` (Original mode; Steamwoven differs only on XP3 inputs) |
| 0x4A6980 | level bonus points | two integers, no stubs | `BonusPointsMatchTheOriginal` |
| 0x4C5EB0 | basic skill level | one critter (ARC-SKILLS-001): stubs for its type, its twelve stored basic skills, its eight primary stats, the monstrous test and an additive effect change; the key-stat and cap tables; may call the base level, points and cap | `SkillLevelMatchesTheOriginal` (`SkillOriginalExeTests`) |
| 0x4C6170 | basic skill training set | the same critter and a requested tier (also outside 0..3); the network test answers single player; the array write and the Educator update are recorded stubs; may call the level | `TrainingSetMatchesTheOriginal` (`SkillOriginalExeTests`) |
| 0x4C62E0 | basic skill effectiveness | a source and an optional target (ARC-SKILLS-003): stubs for their basic levels (0x4C5EB0), the target's perception and intelligence (0x4B0490), the source's extraordinary intelligence (0x4B0EE0), the local PC (0x40DA50, a 64-bit answer), the difficulty setting (0x404590); the skill flag and opposed tables; may call the opposed effectiveness | `BasicEffectivenessMatchesTheOriginal` (`SkillCheckOriginalExeTests`) |
| 0x4C6410 | opposed skill effectiveness | the same model, for any basic skill | `OpposedEffectivenessMatchesTheOriginal` |
| 0x4C69F0 | tech skill effectiveness | the same model with the tech levels (0x4C65E0) and the target's spell flags (field 0x14, Magnetic Inversion) | `TechEffectivenessMatchesTheOriginal` |
| 0x4F5090 | fate armed | one PC (ARC-WORLD-054): stubs for its fate flags (field 0xFE) and fate points (stat 20); the kinds' mask table; the window callback pointer 0x603D48 null | `ArmedMatchesTheOriginal` (`FateOriginalExeTests`) |
| 0x4F50C0 | fate arm | the same PC; the authority test answers single player; the stat, field, hit-point and fatigue writes are recording stubs (the stat setter's clamp is applied by the test); may call the immediate effect 0x4F52D0 | `ArmMatchesTheOriginal` |
| 0x4F51B0 | fate undo | as above | `UndoMatchesTheOriginal` |
| 0x4F5270 | fate use | as above | `UseMatchesTheOriginal` |
| 0x4507D0 | spell run-time cost | one caster (ARC-MAGIC-003): stubs for its type, race and mastered college; one spell record's cost in a heap table behind 0x6876D8; the cheat flag 0x5E75FC; the payment 0x450420 is a recording stub (its arguments are the cost) | `RunCostMatchesTheOriginal` (`SpellCostOriginalExeTests`) |
| 0x4508A0 | spell check cost | as above; the payment is asked without applying | `CheckCostMatchesTheOriginal` |
| 0x4B1660 | spell cast cost (UI and AI) | as above, with the loaded flag 0x5E7618 and the cost getter 0x450340; no payment | `CastCostMatchesTheOriginal` |
| 0x4B1790 | spell learn | one critter (ARC-MAGIC-004): stubs for its type, its college's rank (0x407470), its effective level, intelligence and willpower, its stored magick points; the spell's willpower minimum in the table behind 0x6876D8 (or the defaults with the loaded flag 0); the rank write (0x4F0270) and the stat setter are recording stubs; single player, not the local player; may call the minima, the rank getter and setter | `LearnMatchesTheOriginal` (`SpellBookOriginalExeTests`) |
| 0x4537B0 | spell resistance | one magic run, record and target (ARC-MAGIC-005): the run, the record and its `Resist:` pair on the heap behind their globals, the target handle, type and fate-maximised globals; stubs for the target's type, critter flags 2, stored magic resistance, aptitude and `Resist:` stat, the extraordinary test, the fate use, the random range (the pre-drawn d100 and d20, recorded) and the AI notice; the run's margin and the displayed-margin global are the call's **writable** outputs, read back after it | `ResistMatchesTheOriginal` (`SpellResistanceOriginalExeTests`; Steamwoven differs only on MG4's inputs) |
| 0x453B20 | spell fail chance | the same target and record (the record in the spell table behind 0x6876D8); no writes | `FailChanceMatchesTheOriginal` |
| 0x454700 | spell range | a parent location, a target tile and an optional target object (ARC-MAGIC-006): stubs for the object's flags (field 0x13) and its location (0x406DA0 field 2, a 64-bit answer); the record's `Range:` in the spell table behind 0x6876D8 with the loaded flag 0x5E7618; may call the range getter 0x450280 and the tile distance 0x4B96F0; coordinates up to the 32-bit edges | `RangeMatchesTheOriginal` (`SpellInvocationOriginalExeTests`) |
| 0x456430 | spell forbidden flags | an optional caster and target: stubs for their spell flags (field 0x14), the target's type and critter flags (0xDA); the record's three masks on the heap | `FlagsMatchTheOriginal` |
| 0x456A10 | item gate | an optional holder, an optional second object and an item: stubs for the item's complexity (field 0x60), both aptitudes (stat 16; the second is recorded) and the item power 0x4614A0 (a recording stub) | `ItemGateMatchesTheOriginal` |
| 0x4F2D20 | spell area test | one target context on the heap (ARC-MAGIC-007): the mask, spell flags and radius; candidate, self, source and other roles; stubs for the candidate's fields and stats, hit points (0x43D600), the PC leader (0x45DDA0, a 64-bit answer), locations, the tile's objects (0x4407C0: a list the stub writes into the caller's struct with `poke`), the blocked flag, the path (0x41F3C0) and the sector kinds; the failure-bit global 0x603D40 and the path globals are writable; inputs reading through a null handle are outside the model | `AreaTestMatchesTheOriginal` (`SpellTargetOriginalExeTests`; Steamwoven differs only on MG25's inputs) |
| 0x4F40B0 | spell target collection | a context over a world of eight objects: roles, run list and summoned list on the heap, the rectangle, tile and party queries (lists written with `poke`; the rectangle read with `peek`), the direction, run-list nodes from a pool; may call the area test and its helpers, the list adders and 0x58AC70; the output list and the built run list are read back as a count and an FNV-1a digest; inputs reading through a null handle are redrawn and counted | `CollectionMatchesTheOriginal` |
| 0x453D40 | spell re-aim | the run, the component and the current target and tile globals; stubs for the type and locations; the current target, tile and type are writable | `ReAimMatchesTheOriginal` |
| 0x453EE0 | spell AI notice | the record's aggressive flag, the run's action, parent and other object, the current target and the last-notified global (writable); the AI call 0x4A9650 is a recording stub; may call 0x453F20 | `AiNoticeMatchesTheOriginal` |
| 0x455250 | spell time scaling | one run on the heap (ARC-MAGIC-009): spell, parent and target handles and types, a time value {days, ms}; stubs for the parent's mastered college (0x4B1CB0) and both aptitudes (stat 16); the time value is the writable output | `TimeMatchesTheOriginal` (`SpellRunOriginalExeTests`) |
| 0x452F20 | spell stage | one run (spell, action, source, parent, target, flags, margin) and its record (willpower minimum, cost, maintenance, flags, stacking rules) in the spell table; stubs for types, intelligence, willpower, race, mastery, dead and cannot-act, the payment 0x450420 (answers by the source's fatigue, recording each cost), the no-stack test (its out-parameter written with `poke`; the object it is asked about — source, target or none — is recorded), the cancels, the local player, the messages (the number read with `peek`), the maintain bar and the END processing; may call the BEGIN cost 0x4507D0, the maintenance helpers 0x4532F0/0x453370 and the minimum getters; the run's action, the no-stack object and the target list count are writable | `StageMatchesTheOriginal` (Steamwoven differs only on MG3's inputs) |
| 0x4545E0 | spell maintenance cap | a 512-slot run table and the spell table on the heap with up to fifteen runs of three parents; stubs for the parent's type and intelligence, its location and the range test (whose answer the cap ignores); inputs reading a missing parent through handle 0 are redrawn and counted (MG26) | `CapMatchesTheOriginal` |
| 0x453FA0 | spell scheduler | one run (spell, action, flags, source, parent and target with their types, the trigger count left) and its record (maintenance cost and period, the duration block, the trigger count, the technology flag, the END, CALLBACK and END_CALLBACK component counts) in the spell table; the pass's globals (callback now, END callback, applied, callback pending, the callback guard); the real tick helpers 0x454790/0x4547F0, the struct getters, the time helper 0x45A950 and the time scaling 0x455250; stubs for the cap, the tick queue 0x4548D0 (recording each tick's game time), the local player, the maintain bar (add and refresh), the free, the target's refresh, the recursive processing and the callback action (recording the run's action), the messages, the stats and the mastery; the run and the callback guard and pending flag are writable | `ScheduleMatchesTheOriginal` (Steamwoven differs only on MG19's inputs) |
| 0x457110 | spell interrupt | one run (id, spell, action) and its record's CALLBACK and END_CALLBACK component counts; stubs for the run lookup (found or not), the removal of the run's tick events (only those; the removal itself is not compared: Steamwoven does not model the nested-removal guard) and the processing (recording the run's action); the run's action, the target list count and the removal guard are writable | `InterruptMatchesTheOriginal` |
| 0x455550 | spell per-target stage | one run (source, parent, set spell flags, run flags) and its record's flags (No_Reflect) in the spell table; the context's candidate and the source's spell flags (+0x5C); the global last cost; stubs for the candidate's spell flags, the reflection's caster 0x459040 (found only for a target with full reflection, as both readers have it), the payment (recording payer, cost, apply, spell), the local player, the messages and the maintain bar; the run, the candidate and the stored flags are writable | `TargetStageMatchesTheOriginal` (Steamwoven differs only on MG15's inputs) |
| 0x453410 | spell no-stack test | a 512-slot run table with up to thirteen runs over four objects and five spells' self bits; arguments the run, the spell, the object and the out slot | `NoStackMatchesTheOriginal` |
| 0x4534E0 | spell cancels | the same table with the five spells' `Cancels_SF:` and `Cancels_EnvSF:`; stubs for the interrupt (recording the run) and the environment test (its answer drawn) | `CancelsMatchTheOriginal` |
| 0x4518D0 | spell Damage component | one run (parent with its type and aptitude, other object, margin) and one component (`Dmg:` range, type, flags) on the heap, the current target global; stubs for the combat context init 0x4B2210 (attacker and target written into the context), the random range (its empty-range answer without a draw, as 0x4CEF00 has it), the target's current fatigue, the AI notice, the damage step and the acid's steps (recording the context's five amounts, damage flags, context flags and source) and the damage effect; the stored object flags are writable | `DamageComponentMatchesTheOriginal` (Steamwoven differs only on MG37's inputs) |
| 0x4521A0 | spell Heal component | the same run and component; stubs for the context init, the random range and the heal applier 0x4B58C0 (recording the context's amounts and flags) | `HealComponentMatchesTheOriginal` |
| 0x4B5810 | spell acid steps | a context on the heap (target, normal amount, flags); stubs for the target's type, the item in each slot 1000..1008 and the damage step (recording each step's object and amount and lowering the amount in place by that item's resistance, as 0x4B6860 does) | `AcidMatchesTheOriginal` (Steamwoven differs only on MG38's inputs) |
| 0x461F80 | barter item price | one item (type, stored worth, complexity, flags, current art, hit points, inventory location), the PC and one merchant (price multiplier, NPC flags); the worth, identified test, sale and buy checks and reaction scale run as they are; stubs for the PC test, the PC's haggle effectiveness and training, the merchant's buy-object script answer and buy-list verdict, the broken art and the standard reaction; the checks on (flag 0) | `PriceMatchesTheOriginal` (TradeOriginalExeTests) |

A stub may `peek` an argument it was given by pointer and `poke` a list it answers into the stack or the heap (a world-model
write, not the function's). A call may name `writable` ranges (world-model outputs such as a global the function sets); any other write still fails the
run, and the oracle reads them back with `read_u32`.

Adding a function: a `FunctionSpec` in `oracle_functions.py` with its model, a test with mutants that writes its receipt,
a row in `ORACLE_FUNCTIONS` of `tools/local_dev/launch.py` and its test class in `ORACLE_TEST_CLASSES` (`test_launcher_sync.py` checks all three agree), and its F3
row in the [function registry](../../docs/verification/README.md) with the committed receipt.

## Unicorn and its licence

Unicorn 2.1.4 is pinned in `requirements.lock` (exact version, wheel hashes; installed with `--require-hashes
--only-binary=:all: --no-deps`). On first use `oracle.py` creates a private virtual environment under
`local/original-oracle/venv` (or `$ARCANUM_ORACLE_VENV`; an existing folder that is not a virtual environment is
refused), installs the lock there (network once) and always re-runs itself in it, so a Unicorn of the host interpreter
is never used; a changed lock reinstalls.

Licence (checked 2026-10-17): the PyPI package declares the BSD licence for its Python bindings
(`bindings/python/pyproject.toml`); the engine library bundled in the wheel is QEMU-based and GPLv2 (upstream
`COPYING`). Nothing of Unicorn is vendored or distributed with this repository: the wheel is downloaded into an ignored
local folder, and only this separate developer tool imports it. No C# assembly references it.
