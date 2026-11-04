# Arcanum.Executable

Status: **implemented, bounded** (ARC-DEV-067, slice E2 of the [prototype extraction](research/prototype-extraction.md),
2026-11-04). Not wired into the game: the prototype builder that uses it is E3 (ARC-DEV-068), the cache and the launcher's
first-start build are E4 (ARC-DEV-069).

`Arcanum.Executable` reads the player's own PE32 `Arcanum.exe` in place and interprets the 32-bit integer subset of x86
that the original's prototype build reaches, so that Steamwoven can later run the original's own prototype code instead
of transcribing it (owner decision 2026-11-03, option 1). It is Godot-independent, has no package dependencies and
uses no emulator library: the function oracle's Unicorn ([ARC-DEV-007](../tools/original-oracle/README.md)) stays a
research tool, used here only as a local cross-check (below). Nothing of the executable enters git, a package or a
test: the tests use hand-assembled synthetic code and a synthetic PE image built in the test.

## API

- `PeImage.Read(byte[], PeReadLimits?)` returns a `PeReadResult`: the image, or a typed `PeReadFailure`
  (`TooLarge`, `Truncated`, `NotMz`, `NotPe`, `NotI386`, `NotPe32`, `TooManySections`, `BadSection`, `BadImportTable`) with
  a detail. Only i386 PE32 images; sections must be page aligned, ascending and inside the image and their raw data
  inside the file, every import slot inside a section. Limits: 64 MiB file, 256 MiB image, 96 sections, 8,192 imports, 256-byte names.
- `PeImage` holds the image base, entry point, sections (name, range, access from the characteristics) and the imports
  (`library!name`, or `library!#ordinal`, with the absolute address of the import slot). `LoadInto(MachineMemory)` maps
  the headers read-only and each section with its own access at the fixed image base (no relocations) and copies the
  bytes; the rest of a section (`.bss`) is zero.
- `MachineMemory`: a flat 4 GiB address space in 4 KiB pages, with only mapped pages present (at most 1 GiB mapped).
  Each page is read, write and/or execute. Guest reads, writes and instruction fetches check the access; a
  straddling write checks both pages before writing. `Load` and `Peek` are host access that ignores the rights (the
  loader writes read-only sections and import slots). Every refused access raises `MachineFaultException` with the
  data address.
- `X86Machine(MachineOptions?)`: the eight registers, EIP, the flags CF, PF, AF, ZF, SF, OF, DF, the memory and the
  census. As in user mode, `pushfd` shows IF always set and `popfd` cannot clear it; AC and ID are kept as written,
  so the CPUID-detection idiom sees a working ID bit and then stops at `cpuid` instead of silently taking the
  no-CPUID branch. The options give the stack (default 2 MiB at 0x100000, below the
  image), the FS base (default 0x7FFDF000: a zeroed thread block with fs:[0] = 0xFFFFFFFF, the end of the exception
  chain, and fs:[0x18] = its own address; 0 maps none and refuses FS overrides), the return sentinel (0xFFFFFFF0,
  unmapped), the import thunk range (0xFF000000) and the step limit of one call (4 × 10^9 steps: one per instruction and per hook reached, one per repeated string
  iteration).
- `Call(function, arguments, maxSteps?)` pushes the 32-bit arguments right to left and the sentinel, clears DF (the
  Win32 convention at a function's entry), runs until the sentinel is reached by a return and restores ESP; the `MachineRunResult` gives EAX, EDX, the steps and the argument bytes the
  callee popped (0 for cdecl, n for `ret n`), or the `MachineFault`. Other registers and memory keep what the call
  left, so consecutive calls share state, as the builds of E3 do after one start-up. A fault leaves the machine
  usable for the next call (ESP is restored), but its memory as the faulting call left it.
- Service hooks: `Hook(address, name, hook)` answers every call or jump to the address (also `Call` straight at it)
  instead of the code there; it need not be mapped. It fires on a control transfer to the address, not when
  execution falls through into it. The hook reads its stack arguments (`Argument(i)`, [ESP + 4 + 4i])
  and the memory, sets EAX/EDX and returns `HookResult.Return(argumentBytes)` (a cdecl or stdcall return),
  `HookResult.Continue()` (observe, then run the code at the address) or `HookResult.Stop(reason)`.
  `BindImports(image)` points every import slot at its own address in the thunk range; a call through it reaches the
  hook that `HookImport("library!name", hook)` set for every slot of that name (library case ignored), or stops with
  `UnhookedImport` and the import's name. A hook may not call the machine again (one call at a time:
  `InvalidOperationException`); any exception a hook throws is a host bug and propagates out of `Call`, it is not a
  fault.
- Census mode: with `Census = new MachineCensus()` the machine records every instruction kind (its mnemonic;
  `rep`/`repe`/`repne` before a repeated string instruction), the count of each, the distinct instruction addresses,
  every called function (the entry of `Call` and every call target) and every hook reached by name; `Report()` lists
  them in a stable order. Off (null) by default.

## Faults

Every stop is a typed `MachineFault` (kind, address of the instruction or hook, detail, data address for memory):
`UnsupportedInstruction` (with the instruction's bytes), `MemoryRead`, `MemoryWrite`, `ExecuteOutsideCode` (a fetch
from memory that is not executable, for example falling off the end of a section), `ControlOutsideCode` (a call, jump
or return to an address that is neither executable code, a hook nor the sentinel), `DivideError` (zero divisor or a
quotient out of range, the CPU's #DE), `Breakpoint` (`int3`), `InstructionLimit`, `HookStopped` and
`UnhookedImport`; reaching the sentinel with ESP below the return address (a jump to it) is `ControlOutsideCode`.
So an unknown path is found with its address, never guessed. Exceptions model nothing: there is no
exception dispatch, no interrupt and no floating point.

## The instruction subset

The census of E1 found 72 kinds of instructions, none of them floating point; the subset below covers them and the rest
of the integer instructions a 32-bit compiler emits. A sweep of every instruction class of the owner's executable
(below) found every compiler-generated integer class supported.

- Prefixes: operand size `66` (the 16-bit forms of the arithmetic, logic, shift, rotate, multiply, divide, move,
  extension, exchange, bit, string and push/pop instructions; refused for `pushaw`, `popaw`, `pushfw`, `popfw`,
  `enter`, `leave`, `ret`, `call`, `jmp`, `jcc`, `loop`, `jecxz`, `shld`, `shrd` and `bswap`), `rep`/`repe` `F3`, `repne` `F2`,
  `lock` `F0` (no effect: one thread), the segment overrides CS, DS, ES, SS (flat) and FS (the FS base). Refused: GS and the
  address-size prefix `67`.
- Arithmetic and logic on 8, 16 and 32 bits in every ModRM and immediate form: `add`, `or`, `adc`, `sbb`, `and`,
  `sub`, `xor`, `cmp`, `test`, `inc`, `dec`, `neg`, `not`; `mul`, `imul` (one, two and three operands), `div`, `idiv`;
  `cbw`, `cwde`, `cwd`, `cdq`.
- Shifts and rotates by 1, CL and an immediate: `rol`, `ror`, `rcl`, `rcr`, `shl`/`sal`, `shr`, `sar`; `shld` and `shrd`
  on 32 bits.
- Moves: `mov` (every form, the accumulator-offset forms included), `movzx`, `movsx`, `lea`, `xchg`, `bswap`, `xlat`,
  `setcc`, `cmovcc`; the stack: `push`, `pop` (registers, memory, immediates, 16-bit forms), `pushad`, `popad`,
  `pushfd`, `popfd`, `enter` (nesting level 0), `leave`; the flags: `lahf`, `sahf`, `clc`, `stc`, `cmc`, `cld`, `std`.
- Bits: `bt`, `bts`, `btr`, `btc` (register offsets reach past the operand, signed, as on the CPU), `bsf`, `bsr`.
- Control: `jcc` (8- and 32-bit), `jmp` (relative and indirect, jump tables), `call` (relative and indirect),
  `ret`, `ret n`, `loop`, `loope`, `loopne`, `jecxz`; `nop` (with the `0F 1F` form and `pause`).
- Strings: `movs`, `cmps`, `stos`, `lods`, `scas` on bytes, words and dwords, once or repeated; every repeated
  iteration counts as a step, and a repeat with ECX = 0 changes nothing.
- Refused with `UnsupportedInstruction`: every floating-point and `wait` opcode, the segment register moves and pushes,
  far calls, jumps and returns, `int`, `into`, `iret`, `hlt`, `in`, `out` and the string port instructions, the BCD
  adjustments, `bound`, `arpl`, `les`/`lds`, `cpuid`, `rdtsc`, `cmpxchg`, `xadd`, `enter` with nesting, any other
  two-byte opcode and any undefined group member.

Flags follow the Intel manual (vol. 2). Where the manual leaves a flag undefined the interpreter keeps a fixed choice:
logic instructions clear AF; shifts keep AF and compute OF by the count-1 rule for every count; an 8- or 16-bit `shl`
or `shr` by its width or more gives 0 with CF the last bit out (0 beyond the width), `sar` the sign in every bit and in
CF; multiply keeps SF, ZF, AF and PF; divide keeps
every flag; the bit tests set only CF; `bsf`/`bsr` set only ZF and leave the destination unchanged for a zero
source. Compiled code does not read undefined flags; the tests check only defined ones.

## Verification

- Synthetic tests (`tests/Arcanum.Executable.Tests`, root solution): every instruction kind and flag rule above with
  hand-assembled bytes and values from the manual, all 16 conditions of `jcc` (both widths), `setcc` and `cmovcc` over all 32
  combinations of CF, ZF, SF, OF and PF, the forms the random cross-check below does not reach (strings with every
  prefix and DF, the FS forms of the exception-chain link and of a string source, ESP-relative operands, `pop esp`,
  16-bit multiply and divide, rotate-through-carry count wrap, memory forms of shifts, bits, `setcc` and `cmovcc`, the
  user-mode flags register and the CPUID-detection idiom, DF at a call's entry), the faults with their addresses, hooks (cdecl, stdcall, tail jump, observe,
  stop, a hook's own fault), the census, the memory's access rules and the PE reader on a synthetic image (sections,
  imports, loading, binding, every refusal and limit).
- Opt-in on the player's executable (`TestCategory=OriginalExe`, `ARCANUM_EXE`; inconclusive without it): the PE reads
  and loads; the level bonus points (0x4A6980) and the experience to the next level (0x4A68B0, the stat getter
  0x4B0490 answered by a hook) run on the 10,000 seeded inputs of each oracle file (`ARCANUM_ORACLE_DIR`, ARC-DEV-007)
  and give Unicorn's result for every input. Run on 2026-11-04 on the owner's Mac: all 20,000 equal.
- Local cross-checks on 2026-11-04 (scratch harnesses, not kept): 140,000 random register and memory forms of the
  arithmetic, logic, shift, rotate, double-shift, multiply, divide (with #DE), bit, scan, `setcc`, `cmovcc`, move and
  `lea` instructions on 8, 16 and 32 bits gave Unicorn 2.1.4's registers, memory and defined flags (the two
  differences were harness artefacts: `xchg eax, esp` and a bit offset that reached the harness's own stack); one
  instruction of each of the 592 instruction classes of the executable's code section (prefixes, opcode, register or
  memory form, group member) ran in a sandbox: every class the interpreter refuses is floating point or a pattern that
  occurs only where the disassembler reads data (port, segment, BCD, far-control and system instructions); none is
  a compiler-generated integer instruction.
- Speed (Release, Apple silicon, measured on a register-and-memory loop): about 100 million instructions per second.

## Not ready

- No prototype is built yet: the start-up functions, the service layer over the content stack (memory, file layer,
  MES, clock, GUID, log), the build loop and the comparison with the files the original wrote are E3.
- No allocator: the host maps any heap and answers the allocation functions with hooks (E3).
- One call at a time; a hook cannot call back into the interpreted code.
- The loader maps each section's virtual size rounded to the page, not to a larger section alignment (the retail
  executable aligns sections to the page), and makes a writable section readable, as Windows does.
- Hooks fire on control transfers only, not when execution falls through into the hooked address.
- The census mode costs time (a dictionary update per instruction); production runs leave it off.
