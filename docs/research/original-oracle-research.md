# Original oracle and reconstruction exclusion

Status: **Research**, work item ARC-RES-051 (2026-10-17). Owner decisions of the same day are recorded in
[architecture direction](../architecture/decisions.md) and the method is amended in the
[original executable check](original-executable-check.md#function-emulation-oracle). No code is changed; the
tooling is ARC-DEV-007 ([`tools/original-oracle`](../../tools/original-oracle/README.md); see [Plan](#plan)).

The question: how to build a complete, checkable evidence base that Steamwoven behaves like the retail 1.0.7.4
`Arcanum.exe`, except where a recorded difference says otherwise, and how to stop depending on a source reconstruction for it (since
2026-10-18 the project uses none; its provenance is in git history).

## Findings

- **A function-level oracle works on this executable.** A trial loaded the unchanged `Arcanum.exe` image into a CPU
  emulator, replaced the stat getter with a stub, pointed the experience-table pointer at a synthetic table and called
  three experience functions with 4,000 seeded random inputs each, about 2 seconds in all. The same inputs went to the
  unchanged `ExperienceRules` from `origin/main` (PR #202). To-next (0x4A68B0) and bonus points (0x4A6980) agreed on
  every input.
- **It found what two static readings missed.** Progress (0x4A6900) differed on 269 of 4,000 inputs, all with more than
  2,147,483 experience still to the next level: the original multiplies in 32 bits and wraps, `ExperienceRules.Progress`
  multiplies in 64. Changing only that multiplication to 32 bits in a scratch copy removed every difference; a planted
  mutant in the bonus-point rule (every fourth level instead of every fifth) was caught on 1,617 inputs. The installed
  `rules\xp_level.mes` (from `arcanum4.dat`) tops out at 1,380,000 with a largest step of 80,000, so the wrap never
  happens with the shipped data; `ExperienceTable.Create` admits thresholds up to 2,000,000,000, so a modded table
  would show it. Ledger row [XP3](original-verification-ledger.md#experience-and-levels-arc-world-030) records the
  verdict.
- **The executable carries most of what the reconstruction was used for.**
  - Ghidra 12.1.4 with its full auto-analysis plus a small script that creates functions at pointers found in `.data`
    and `.rdata` found 5,551 functions without any reconstruction input. 4,608 of them start at one of the 4,981 function
    addresses the reconstruction annotated (92.5 %); 373 of the 4,981 annotated addresses were not found yet (most near 0x570000, the interface
    code) and 943 functions had no annotated address. The annotated addresses were used only to measure this.
  - The object field names are in the executable as a table of 338 string pointers at 0x59BEA8, from `obj_f_begin` to
    `obj_f_max`, in field order; the stat names are a table at 0x5B5384 that names stats 0–20 in stat order (`stat_level` is
    17, `stat_experience_points` 18, the numbers the oracle saw in real calls; from 21 on the table and the code disagree,
    the poison level being stat 24 in code, ledger [IF12](original-verification-ledger.md#implemented-functions-arc-dev-025)). These can replace the reconstruction's field and stat headers.
  - The reconstruction gave 4,980 distinct names for its 4,981 addresses. Of its 3,820 descriptive names, 279 occur
    verbatim in the executable's own strings (animation and magic messages), and five Troika source paths appear
    (`GameLibX\Anim.c` and others). The other names were its own; 1,160 more functions had only `sub_…` names.
- **Scale of the reconstruction dependency** on `origin/main` (lines naming it, its repository or its commit): `docs` 2,836 lines in
  140 of 162 files, `tools` 107 lines in 42 files, `src` 84 lines in 44 files, `tests` 17 lines in 15 files,
  `CLAUDE.md` and `AGENTS.md` 10 lines.
- **Call structure.** Of the annotated functions, 866 call nothing, 3,735 call only other game functions, 302 make
  indirect calls and 47 call Win32 directly. Game logic barely touches the operating system, so the emulation boundary
  is the service layer (object fields, stats, random numbers, messages, tig), replaced by stubs.

## Tools

Owner decision 2026-10-17: only free tools with free licences; paid or licence-restricted tools (IDA, Binary Ninja's
paid tiers) are not used.

| Tool | Use | Status |
| --- | --- | --- |
| [Ghidra 12.1.4](https://github.com/NationalSecurityAgency/ghidra/releases) (Apache-2.0) | Headless analysis, decompiler, call graph, scripted function discovery | Trial on Linux arm64 with JDK 21; the release has no arm64 decompiler binary, which was built from the bundled sources with `make`. The macOS arm64 build is not tried yet. |
| [Unicorn 2.1.4](https://pypi.org/project/unicorn/) | Emulating single x86-32 functions | Trial in Python. Its engine core is GPLv2 (the PyPI metadata says BSD); the licence is checked before adoption; either way it stays in a separate tool that the C# assemblies never reference. |
| Ghidra MCP servers ([pyghidra-mcp](https://github.com/clearbluejar/pyghidra-mcp), [GhidrAssistMCP](https://github.com/jtang613/GhidrAssistMCP), [GhidraMCP](https://github.com/LaurieWired/GhidraMCP)) | Agent reading through a read-only tool subset | Not tried. |
| CrossOver 26 (the owner offered his installed copy on 2026-10-17; free alternative: Wine) with x32dbg or `frida-server.exe` inside the bottle | Scenario observation in the running game (level F4) | Not tried; Frida cannot inject into a Wine process from the host ([frida#3339](https://github.com/frida/frida/issues/3339)). A later bounded experiment. |

Practice this follows: [openblack/bw1-decomp PR #413](https://github.com/openblack/bw1-decomp/pull/413) (differential
testing of a Win32 MSVC game against its original binary in Unicorn, random states, planted mutants as controls),
[refurbished-dinosaurs-toolkit #7](https://github.com/kibertoad/refurbished-dinosaurs-toolkit/issues/7) (emulators
compared for calling single game functions; permissive stubs gave false passes) and the
[2026-10-09 agent decompilation report](https://momo5502.com/posts/2026-10-09-game-decompilation/) (agents edited the
verifier and persuaded the reviewer, so the oracle must be protected). Sources opened 2026-10-17.

## Plan

1. **ARC-RES-051** (this work item, docs-only): method amendment, owner decisions, XP3 verdict.
2. **ARC-DEV-007**: the oracle tool and its test integration, on the owner's Mac: `tools/original-oracle` (Python,
   pinned Unicorn) with a self-test on synthetic code, the C# side of the comparison in a test category
   `OriginalExe`, the function registry, the `OriginalFunction` attribute, receipts, `./arcanum oracle`, and the
   reference counter for the reconstruction, with Ghidra and Unicorn pinned in the [toolchain](../workflow/toolchain.md). First functions:
   the three experience functions of the trial. Done for the oracle tool, the `OriginalExe` category and `./arcanum oracle`;
   the registry, attribute, receipts and reference counter are ARC-DEV-008 ([function verification](../verification/README.md)).
3. **Experience XP3 switch**: the progress overflow as a rule-mode switch (owner decision 2026-10-17), with the oracle
   expecting the difference in Steamwoven mode only. Done as ARC-WORLD-033.
4. **Domain migrations**: per domain, raise the implemented functions to their target level and replace reconstruction
   references with executable addresses in the same PR. The owner chose full removal on 2026-10-18 (references go
   from history and research too; facts not yet read in the executable keep their address at F0). Done for code, tests
   and tools (ARC-DEV-009..012), contracts and requirements (ARC-DEV-013) and research documents (ARC-DEV-020);
   re-reading the F0 facts in the executable continues by domain.
5. **Closure**: the reconstruction removed from `AGENTS.md`, `CLAUDE.md` and the method; the counter becomes a ban.
   Done as ARC-DEV-023.

## Not done

The game was not run; no MCP server, CrossOver or debugger was tried; no repository code or test changed. The executable
was read and emulated only in a temporary Linux VM on the owner's Mac. The C# side ran in a cloud container with .NET
SDK 8.0.131 (not the pinned 8.0.425) on a file of the synthetic inputs and the original's outputs; that transfer predates
the rule that such outputs stay local, and the file is not kept there. Scratch scripts and outputs stay in
`research-private/`.
