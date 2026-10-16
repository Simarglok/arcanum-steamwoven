# Original executable check

Status: **Method**, adopted by the owner on 2026-10-17 for every part of the
game; first used by ARC-COMBAT-001
([combat anomalies A1–A10](combat-core-evidence.md#original-executable-check-arc-combat-001)).

CE is a reconstruction. Where its code looks wrong, or where a task must say
how **the original game** behaves, read the original executable at the same
function instead of trusting CE. CE comments give each function's address in
the retail binary (`// 0x4B7EB0` above `sort_combat_list`), and the retail
1.0.7.4 `Arcanum.exe` keeps that layout, so the same function can be read in
both and compared. This is a static reading: nothing is run, patched or copied.

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

- The owner's unchanged retail install on the Mac (`$DAT_ROOT`, today
  `/Applications/Arcanum`, GOG 1.0.7.4). Before reading, record `Version.txt`
  and the SHA-256 of `Arcanum.exe`. The 1.0.7.4 GOG binary has SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`;
  GOG adds a `ddraw.dll` import section that no checked function branches into.
- The CE research clone at the pinned commit
  `a7ff41b300ef712f0e7d088183a3d08957110cdd` (path in CLAUDE.md §4).
- Xcode's `llvm-objdump` (`xcrun -f llvm-objdump`); no other tool is needed.

## Steps

1. Find the function in CE and its address comment. Note the callees' and
   globals' addresses too: they name the calls in the listing
   (for example `obj_field_int32_get` 0x406CA0, `critter_pc_leader_get` 0x45DDA0).
2. Disassemble only that function, read-only, into a scratch location outside
   the repository:

   ```bash
   "$(xcrun -f llvm-objdump)" -d --x86-asm-syntax=intel --start-address=0x4B7EB0 --stop-address=0x4B7F90 "$DAT_ROOT/Arcanum.exe"
   ```

3. Resolve constants from CE's headers: object field indices (`obj.h`), flag
   bits (`obj_flags.h`, `combat.h`), stat numbers (`stat.h`), inventory slots
   (`item.h`).
4. Compare the instruction flow with the CE branch in question: which values
   are tested, which handle is passed, what is written back, which calls are
   made and in what order.
5. Check that the function is not patched: its branches stay inside `.text`.
6. Have a fresh agent repeat the reading independently before the verdict is
   used; record disagreements instead of picking one.

## What to record

In the research or evidence document of the task, one row per question:
function and address, the behaviour observed in the original (in prose), and
the verdict, "CE reconstruction slip" or "in the original". Add the decision
to the owning decision table (for combat and AI, the
[anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions)). The original
behaviour of the checked branch is then verified; everything else in the
document keeps its own evidence level (usually NOT_VERIFIED).

## Rules

- Read only. Never run, patch or copy the executable, and keep the install
  unchanged.
- No bytes, instruction listings, decompiled code or dumps go into git, PRs,
  issues or chat; scratch output stays in the session scratchpad or
  `research-private/`. Documents describe behaviour in their own words.
- No structure or code is translated from the binary or from CE into
  Steamwoven; implementations follow the recorded behaviour and the project's
  own design.
- A static reading shows what the code does, not how often a branch is reached
  in play; a runtime question still needs an observation in the running game.
