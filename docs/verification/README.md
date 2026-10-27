# Function verification

Status: **Implemented bounded component**, ARC-DEV-008. Steamwoven keeps one registry of the retail 1.0.7.4 functions
it implements, marks the implementing methods in code, keeps the receipts of the function oracle, and refuses any
reference to the source reconstruction it no longer uses ([plan](../research/original-oracle-research.md#plan)). Root
tests in `tests/Arcanum.Session.Tests/Verification` keep all four consistent.

## Registry

[`functions.csv`](functions.csv), one row per executable address, sorted by address:

| Column | Content |
| --- | --- |
| `address` | `0x` and upper-case hex, as in the executable |
| `name` | Steamwoven's own name for the function: plain lower-case words, never a reconstruction's identifier |
| `domain` | the area (experience, combat, ...) |
| `status` | `full`, `partial`, `not implemented` or `out of scope` |
| `reason` | what is left out and why; required unless the status is `full` |
| `level` | evidence level F0–F4 of the [original executable check](../research/original-executable-check.md#evidence-levels-for-a-function-f0f4) |
| `evidence` | repository path (optionally with an anchor) of the evidence: for F1/F2 the ledger section in its area file (`docs/research/ledger/<area>.md#<section>`; ARC-DEV-040 moved the rows that named the ledger index's anchors there) or the research study that holds the readings (audio, technology), the receipt for F3, the contract or research document whose claims the code follows for F0 |
| `method` | the implementing method (`Namespace.Type.Method`) of a `full` or `partial` row; empty otherwise |

The registry covers the experience functions of ARC-WORLD-030 (three full at F3 by the oracle; progress since its XP3
switch, ARC-WORLD-033: a known difference keeps a row from `full` until it is implemented), the skill rules of
ARC-SKILLS-001 (the basic level and the basic training setter partial at F3 by the oracle, the others F2), the audio rows (ARC-DATA-040,
F2, [audio study](../research/audio-evidence.md), marking methods of `Arcanum.Data`, which declares its own attribute; `Arcanum.Scripting`
does the same since ARC-SCRIPT-013),
the rows other tasks added, and since ARC-DEV-009 and ARC-DEV-010 the original functions the Simulation, Session and Data code implements and names (F2 where
the [ledger](../research/original-verification-ledger.md) has two readings, F0 where the function was not read in the
executable). Since ARC-DEV-025 no implemented row is at F0: the twelve that were F0 have been read by two readers
([ledger IF1–IF12](../research/ledger/data.md#implemented-functions-arc-dev-025)). Since 2026-10-20
(owner decision, ARC-DEV-030) a new implemented row starts at F1 or higher: the implementing task reads the function in
the executable first. A host method that
only calls the registered rule carries no mark (one method per address). Other domains add their rows when they are migrated.

## OriginalFunction attribute

`[OriginalFunction(address, OriginalFunctionScope.Full | Partial)]` (`Arcanum.Simulation.OriginalFunctionAttribute`) marks
the method that implements a function; a method that implements two functions carries two marks (one method per
address). An assembly that cannot reference `Arcanum.Simulation` declares its own attribute
of the same name and shape. The root test loads `Arcanum.Data`, `Arcanum.Simulation`, `Arcanum.Scripting` and
`Arcanum.Session`, and reads the tracked C# sources under `src/`, `tools/` and `experiments/` for marks in other assemblies.
It fails when a marked method has no row, a different scope or method name, a mark lies in an assembly it does not load,
or a `full` or `partial` row has no marked method.

## Oracle receipts

`./arcanum oracle` ([function oracle](../workflow/toolchain.md#function-oracle)) has each `OriginalExe` test write
`receipts/<ADDR>.json`: function, name, method, seed, inputs, mutants planted and caught, unexpected differences, the
other rule mode's differences (Steamwoven progress: only on XP3 inputs) with their counts, result, executable SHA-256, the method's IL hash and the build configuration.
The IL hash is a SHA-256 of the IL of the method and of every method of the same assembly it reaches (callees, accessors,
lambdas, local functions and state machines), with every metadata token replaced by the name it refers to and the
ordinals of compiler-generated names dropped (`tests/Shared/MethodIlHash.cs`): it changes with that code and not with
unrelated members; a static field read brings in its type's initializer. Virtual and interface calls reach only the
declaration, not the implementations behind it. Methods of other assemblies (the runtime) are named, not hashed, and a runtime or SDK change that
renames them invalidates every receipt. The root test
fails when an F3 row has no passing receipt or when the method's IL hash differs from its receipt: a changed function
needs a new oracle run and its receipt committed. A receipt from another build configuration leaves the test
inconclusive (root checks build Debug, like `./arcanum oracle`).

## Rule-mode switches

Every boolean switch of a profile composed in `GameRules` needs a row with a ledger ID (letters and a number) in the
[rule-mode registry](../world/rule-modes.md#registry), and every row there a switch in code.

## Reconstruction references

Since ARC-DEV-023 no tracked text file may name the reconstruction (owner decision 2026-10-18): its repository name,
its pinned commit, its two-letter abbreviation (also as an identifier prefix) or a Markdown reference label made of it.
The root test reads every tracked file except lock files and binary files and lists each offending line. The only
exception is the original DLG action opcode spelled with the same two letters, which the
[dialog study](../research/dialog-evidence.md) counts. Facts that were never read in the executable cite the function's
address at level F0; their provenance is in git history. Until ARC-DEV-023 a per-area baseline only kept the count from
growing (ARC-DEV-008 to ARC-DEV-020).
