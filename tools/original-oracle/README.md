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
  and reject anything else. A run fails on any other callee, a stub rejection, any other read, any write but the stack, a fault, the instruction limit (no return), an unbalanced stack or a callee-saved
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
| 0x4507D0 | spell run-time cost | one caster (ARC-MAGIC-003): stubs for its type, race and mastered college; one spell record's cost in a heap table behind 0x6876D8; the cheat flag 0x5E75FC; the payment 0x450420 is a recording stub (its arguments are the cost) | `RunCostMatchesTheOriginal` (`SpellCostOriginalExeTests`) |
| 0x4508A0 | spell check cost | as above; the payment is asked without applying | `CheckCostMatchesTheOriginal` |
| 0x4B1660 | spell cast cost (UI and AI) | as above, with the loaded flag 0x5E7618 and the cost getter 0x450340; no payment | `CastCostMatchesTheOriginal` |
| 0x4B1790 | spell learn | one critter (ARC-MAGIC-004): stubs for its type, its college's rank (0x407470), its effective level, intelligence and willpower, its stored magick points; the spell's willpower minimum in the table behind 0x6876D8 (or the defaults with the loaded flag 0); the rank write (0x4F0270) and the stat setter are recording stubs; single player, not the local player; may call the minima, the rank getter and setter | `LearnMatchesTheOriginal` (`SpellBookOriginalExeTests`) |

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
