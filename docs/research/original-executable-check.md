# Original executable check

Status: **Method**, adopted by the owner on 2026-10-17 for every part of the
game and amended the same day (ARC-RES-051): the executable is the only
reference for original behaviour, a function may be executed in an emulator as
an [oracle](#function-emulation-oracle), and CE is being phased out of the
process and the documents ([research](original-oracle-research.md)). Until a
domain is migrated, CE stays its first reading (D1) and new work there cites CE
only for the function it checks; the executable settles what
the original does. First used by ARC-COMBAT-001
([combat anomalies A1–A10](combat-core-evidence.md#original-executable-check-arc-combat-001)).

CE is a reconstruction. Where its code looks wrong, or where a task must say
how **the original game** behaves, read the original executable at the same
function instead of trusting CE. CE comments give each function's address in
the retail binary (`// 0x4B7EB0` above `sort_combat_list`), and the retail
1.0.7.4 `Arcanum.exe` keeps that layout, so the same function can be read in
both and compared. A reading is static: nothing is run, patched or copied. The
oracle runs single functions of the unchanged file in an emulator under the rules
below.

## When to use it

- A CE branch looks like a slip (an anomaly): decide whether it is a **CE
  reconstruction slip** or **a bug of the original game**. Only the second
  kind gets a switch of the "Original" setting (owner decision D8 in the
  [NPC combat AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16));
  for a CE-only slip both settings follow the original.
- The owner asks "how is it in the original, not in CE".
- A rule matters for parity and the CE reading has decompiler names, unclear
  intent or a `TODO`/`FIXME`.

Any area can use it: combat, AI, skills, magic and technology, scripts,
dialog, items, rendering logic, loading. It answers questions about code;
questions about data (prototypes, MES, maps) still go through the data probes
on the original files.

## Inputs

- The owner's unchanged retail install on the Mac (`$DAT_ROOT`, GOG 1.0.7.4). Before reading, record `Version.txt`
  and the SHA-256 of `Arcanum.exe`. The 1.0.7.4 GOG binary has SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`;
  GOG adds an `.IIDKing` section holding a second `ddraw.dll` import, which no
  checked function branches into.
- The CE research clone at the pinned commit
  `a7ff41b300ef712f0e7d088183a3d08957110cdd` (path in CLAUDE.md §4).
- Xcode's `llvm-objdump` (`xcrun -f llvm-objdump`). Ghidra (Apache-2.0) may be
  used instead for the same reading, headless and with names from the project's
  own records; its project stays in `research-private/`. Only free tools with
  free licences are used (owner decision 2026-10-17); paid or licence-restricted
  tools such as IDA are not.

## Steps

1. Find the function in CE and its address comment; the stop address is the
   address comment of the next function in the same CE file. The listing shows
   no names (calls read `<.text+0x5ca0>`), so note the callees' and globals'
   addresses from their CE comments too (for example `obj_field_int32_get`
   0x406CA0, `critter_pc_leader_get` 0x45DDA0).
2. Disassemble only that function, read-only, into the session scratchpad or
   the ignored `research-private/` folder, never into tracked files:

   ```bash
   "$(xcrun -f llvm-objdump)" -d --x86-asm-syntax=intel --start-address=0x4B7EB0 --stop-address=0x4B8040 "$DAT_ROOT/Arcanum.exe" > "$SCRATCH/sort_combat_list.asm"
   ```

3. Resolve constants from CE's headers: object field indices (`obj.h`), flag
   bits (`obj_flags.h`, `combat.h`), stat numbers (`stat.h`), inventory slots
   (`item.h`).
4. Compare the instruction flow with the CE branch in question: which values
   are tested, which handle is passed, what is written back, which calls are
   made and in what order. Check the arithmetic too: operand width (8, 16 or 32
   bits), signed or unsigned compares and division, where a product or sum can
   wrap, and how a division rounds. A reading that names only the formula can
   miss a 32-bit wrap (ledger XP3).
5. Check that the function is not patched: its branches stay inside `.text`
   and never reach `.IIDKing`.
6. Have a fresh agent repeat the reading independently before the verdict is
   used; record disagreements instead of picking one.

## What to record

In the research or evidence document of the task, one row per question:
function and address, the behaviour observed in the original (in prose), and
the verdict, "CE reconstruction slip" or "in the original" (a bug of the
original game). Add the decision
to the owning decision table (for combat and AI, the
[anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions)). The original
behaviour of the checked branch is then verified; everything else in the
document keeps its own evidence level (usually NOT_VERIFIED).

The cross-domain results of the first systematic pass (ARC-RES-048) are in the
[verification ledger](original-verification-ledger.md).

## Function emulation (oracle)

Owner decision 2026-10-17: single functions of the unchanged retail executable
may be executed in a CPU emulator to compare their results with Steamwoven.
Short of observing the running game, this is the strongest evidence for a function (F3 below), because it finds
differences a reader misses and needs no reading of the code at all.

- The emulator maps the image of the file read from the install into its own
  memory; the install is never written, and the file is never copied into git,
  the cloud or another device. Its SHA-256 is checked before every run.
- Only the emulated function and the functions the run allows execute. There is
  no operating system, file system or network. Service functions (object fields,
  stats, random numbers, messages, the tig layer) are replaced by stubs that read
  one fictitious world model; the same model becomes the inputs of the C# side.
  Any call outside the allowed set fails the run (a permissive stub gives false
  passes).
- Inputs are synthetic or drawn from a seed; installed data may be used for
  realistic tables. Every run includes planted mutants of the C# side that must
  be caught.
- Generated inputs and the original's outputs stay local (`research-private/` or
  a temporary folder). Git, PRs and documents get only the summary: function,
  seed, number of inputs, mutants, result, executable SHA-256. Documents describe
  behaviour in prose as before.
- A difference is either fixed in Steamwoven or recorded as a ledger row and, for
  a deliberate difference, a [rule-mode](../world/rule-modes.md) switch; the run
  then expects it in Steamwoven mode only and no difference in Original mode.
- Running the game as a whole (for example under CrossOver) is not part of this
  permission; it needs its own work item.

The tool is [`tools/original-oracle`](../../tools/original-oracle/README.md) (ARC-DEV-007):
`./arcanum oracle [--function ADDR|--all]` writes at least 10,000 seeded inputs per
function with the original's outputs to `research-private/oracle/`, then runs the
`OriginalExe` tests, which compare Steamwoven on them and replay at least two planted
mutants per function ([toolchain](../workflow/toolchain.md#function-oracle)). A
function gains an oracle with its own world model, test and launcher row. First
functions: experience to next level (0x4A68B0), progress (0x4A6900) and bonus points
(0x4A6980), at F3; progress is compared in Original mode, and its Steamwoven mode
may differ only on the XP3 inputs (ARC-WORLD-033). Each run writes a receipt (seed, inputs, mutants, result, executable
SHA-256 and the method's IL hash) to `docs/verification/receipts/`, and the
[function registry](../verification/README.md) (ARC-DEV-008) lists every implemented
function with its status and evidence level; a root test fails when an F3 function's
code changes after its receipt.

### Evidence levels for a function (F0–F4)

| Level | Evidence |
| --- | --- |
| F0 | Implemented from a CE reading only (NOT_VERIFIED) |
| F1 | One reading of the whole function in the executable |
| F2 | Two independent readings (step 6) |
| F3 | Oracle: the function and Steamwoven compared on recorded inputs, mutants caught |
| F4 | Observed in the running game (a scenario trace or save) |

## Rules

- The install stays read-only. Never patch the executable, never copy the file out of the install
  (into git, the cloud or another device), and keep the install unchanged. It is
  executed only function by function in the emulator, as above.
- No bytes, instruction listings, decompiled code or dumps go into git, PRs,
  issues or chat; scratch output stays in the session scratchpad or
  `research-private/`. Documents describe behaviour in their own words.
- No structure or code is translated from the binary or from CE into
  Steamwoven; implementations follow the recorded behaviour and the project's
  own design.
- A static reading shows what the code does, not how often a branch is reached
  in play; a runtime question still needs an observation in the running game.
