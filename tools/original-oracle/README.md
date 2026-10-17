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
- Each function runs as one cdecl call. Only its own code range and the ranges of game functions it is allowed to call
  execute (all inside `.text`); service functions are stubs that answer from one fictitious world model and reject
  anything else. A run fails on any other callee, a stub rejection, a data read in the image outside the allowed
  globals, any write but the stack, a fault, the instruction limit (no return), an unbalanced stack or a callee-saved
  register not restored. Generation stops at the first failed run.
- Inputs are drawn from a seed (default 20261017, at least 10,000 per function) inside the C# side's domain, so the
  same model is the input of both sides. Output: `<out>/<ADDR>.json` (default `research-private/oracle/`, or
  `$ARCANUM_ORACLE_DIR`; a folder in the work tree outside `research-private/` or `local/` is refused). The files hold
  only synthetic inputs and the original's results; they stay local all the same.
- The `OriginalExe` tests (`tests/Arcanum.Simulation.Tests`) are inconclusive without `ARCANUM_EXE` or the files, check
  the executable's and the files' SHA-256, and replay at least two planted mutants of the C# side per function, failing
  unless each is caught.

| Function | Name | Model | OriginalExe test |
| --- | --- | --- | --- |
| 0x4A68B0 | experience to next level | stat getter stub (level 17, experience 18), threshold-table pointer 0x5F5C94 | `ToNextLevelMatchesTheOriginal` |
| 0x4A6900 | experience progress | as above, may call 0x4A68B0 | `ProgressMatchesTheOriginalExceptTheXp3Wrap` (XP3 inputs reported) |
| 0x4A6980 | level bonus points | two integers, no stubs | `BonusPointsMatchTheOriginal` |

Adding a function: a `FunctionSpec` in `oracle_functions.py` with its model, a test with mutants, and a row in
`ORACLE_FUNCTIONS` of `tools/local_dev/launch.py` (`test_launcher_sync.py` checks all three agree).

## Unicorn and its licence

Unicorn 2.1.4 is pinned in `requirements.lock` (exact version, wheel hashes; installed with `--require-hashes
--only-binary=:all: --no-deps`). On first use `oracle.py` creates a private virtual environment under
`local/original-oracle/venv` (or `$ARCANUM_ORACLE_VENV`), installs the lock there (network once) and re-runs itself in it;
a changed lock reinstalls.

Licence (checked 2026-10-17): the PyPI package declares the BSD licence for its Python bindings
(`bindings/python/pyproject.toml`); the engine library bundled in the wheel is QEMU-based and GPLv2 (upstream
`COPYING`). Nothing of Unicorn is vendored or distributed with this repository: the wheel is downloaded into an ignored
local folder, and only this separate developer tool imports it. No C# assembly references it.
