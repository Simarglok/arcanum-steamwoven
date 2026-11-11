# Original executable check

Status: **Method**, adopted by the owner on 2026-10-17 for every part of the
game and amended the same day (ARC-RES-051): the executable is the only
reference for original behaviour, a function may be executed in an emulator as
an [oracle](#function-emulation-oracle). Since 2026-10-18 the project uses no
source reconstruction at all (owner decision, [research](original-oracle-research.md)):
facts not yet read in the executable keep their address at level F0 until their
domain is implemented; the task that implements or changes a function reads it
in the executable first (owner decision 2026-10-20). First used by ARC-COMBAT-001
([combat anomalies A1–A10](combat-core-evidence.md#original-executable-check-arc-combat-001)).

Where a task must say how **the original game** behaves, or where an earlier
reading (F0) looks wrong, read the original code at the function's address in
the retail 1.0.7.4 `Arcanum.exe`. Addresses come from the
[function registry](../verification/functions.csv), the research documents and
the [verification ledger](original-verification-ledger.md). The code is read in
the exports of the executable kept in `research-private/` (the decompiled C of
the [Ghidra text export](../workflow/toolchain.md#ghidra-text-export-arc-dev-377)
and any further export beside it), searched as text; the executable itself is
read only for what the exports lack (below). A reading is static: nothing is
run, patched or copied. The oracle runs single functions of the unchanged file
in an emulator under the rules below.

## When to use it

- A recorded F0 fact looks like a slip (an anomaly): decide whether it is a
  **reconstruction slip** (the earlier reading was wrong) or **a bug of the
  original game**. Only the second kind gets a switch of the "Original" setting
  (owner decision D8 in the
  [NPC combat AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16));
  for a reconstruction slip both settings follow the original.
- A task implements or changes an original function whose recorded facts are
  F0: read it before writing the code (owner decision 2026-10-20).
- The owner asks "how is it in the original".
- A rule matters for parity and its recorded reading is F0, has unclear intent
  or an open question.

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
- The function's address (see above) and the project's own records of the
  callees, globals and constants it uses.
- The exports in `research-private/` (owner's Mac, never in git): the decompiled
  C of every function Ghidra found, `research-private/ghidra-export/c/` with
  `index.tsv` and `funcs/<address>.c`, searched with `rg`
  ([how-to](../workflow/toolchain.md#ghidra-text-export-arc-dev-377)). The
  decompiler's names, variables and types are a guess; they are checked, not
  trusted.
- Only for what the exports lack: Xcode's `llvm-objdump` (`xcrun -f llvm-objdump`)
  on the executable, or Ghidra (Apache-2.0) headless with names from the
  project's own records; its project stays in `research-private/`. Only free
  tools with free licences are used (owner decision 2026-10-17); paid or
  licence-restricted tools such as IDA are not.

## Steps

1. Search the exports first. The function at an address: open
   `research-private/ghidra-export/c/funcs/<address>.c` (eight lowercase hex
   digits) or look the address up in `index.tsv`; its callers: `rg -l
   "FUN_<address>" research-private/ghidra-export/c/funcs`; a constant or string:
   `rg -n` over `funcs`. Note the callees' and globals' addresses from the
   registry and the research documents too (for example the object field getter
   0x406CA0 and the PC leader getter 0x45DDA0): the export names are Ghidra's.
   The export has no file for a function that is only reached through a pointer
   (1,498 of the census starts, among them the goal-round handler 0x4B7150): a
   missing file does not mean the executable has no such function.
2. Only for a missing function, or for a detail of an exported one that the
   decompiled C leaves open (operand width, signed or unsigned compare, a
   wrapping sum or product, a division), disassemble that one function,
   read-only, into the session scratchpad or the ignored `research-private/`
   folder, never into tracked files. The stop address is the start of the next
   function (the census or Ghidra's analysis, or the prologue and call target
   scan of the [oracle research](original-oracle-research.md#findings)); the
   listing shows no names (calls read `<.text+0x5ca0>`):

   ```bash
   "$(xcrun -f llvm-objdump)" -d --x86-asm-syntax=intel --start-address=0x4B7EB0 --stop-address=0x4B8040 "$DAT_ROOT/Arcanum.exe" > "$SCRATCH/sort_combat_list.asm"
   ```

3. Resolve constants from the project's own records: object field indices and
   flag bits in the format documents (`docs/formats`), stat numbers and inventory
   slots in the domain contracts, and values the oracle observed in real calls.
4. Compare the code flow with the recorded reading in question: which values
   are tested, which handle is passed, what is written back, which calls are
   made and in what order. Check the arithmetic too: operand width (8, 16 or 32
   bits), signed or unsigned compares and division, where a product or sum can
   wrap, and how a division rounds. A reading that names only the formula can
   miss a 32-bit wrap (ledger XP3).
5. Check that the function is not patched: its branches stay inside `.text`
   and never reach `.IIDKing`. A direct call into `.IIDKing` shows in the
   decompiled text as a call to an address in that range; indirect calls and
   jumps through a table are checked in the function's disassembly.
6. Have a fresh agent repeat the reading independently before the verdict is
   used; record disagreements instead of picking one.

## What to record

In the research or evidence document of the task, one row per question:
function and address, the behaviour observed in the original (in prose), and
the verdict, "reconstruction slip" or "in the original" (a bug of the
original game). Add the decision
to the owning decision table (for combat and AI, the
[anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions)). The original
behaviour of the checked branch is then verified; everything else in the
document keeps its own evidence level (usually NOT_VERIFIED).

The cross-domain results of the first systematic pass (ARC-RES-048) are in the
[verification ledger](original-verification-ledger.md), one file per area under
`ledger/` (ARC-DEV-038); a new section goes into its area file and adds a line to
the ledger's area index.

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
- One owner-approved extension (2026-11-03, [decisions](../architecture/decisions.md)):
  on the player's own machine, Steamwoven may interpret the prototype build of the
  player's verified 1.0.7.4 executable (its SHA-256 checked before every run) to write
  the prototype files the original writes at its first start
  ([prototype extraction](prototype-extraction.md)). Nothing of the executable or of
  the built files is copied into git or translated into Steamwoven code, and any path
  outside the build's recorded functions and services stops the build.

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
| F0 | Implemented from an earlier reading only, not checked in the executable (NOT_VERIFIED) |
| F1 | One reading of the whole function in the executable (its decompiled export, or its disassembly) |
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
- No structure or code is translated from the binary into
  Steamwoven; implementations follow the recorded behaviour and the project's
  own design.
- A static reading shows what the code does, not how often a branch is reached
  in play; a runtime question still needs an observation in the running game.
