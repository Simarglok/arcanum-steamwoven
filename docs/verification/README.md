# Function verification

Status: **Implemented bounded component**, ARC-DEV-008. Steamwoven keeps one registry of the retail 1.0.7.4 functions
it implements, marks the implementing methods in code, keeps the receipts of the function oracle, and counts the lines
that still cite the reconstruction it is moving away from ([plan](../research/original-oracle-research.md#plan)). Root
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
| `evidence` | repository path (optionally with an anchor) of the evidence: the ledger section for F1/F2, the receipt for F3 |
| `method` | the implementing method (`Namespace.Type.Method`) of a `full` or `partial` row; empty otherwise |

The first rows are the experience functions of ARC-WORLD-030: three at F3 (oracle), five partial at F2 (two readings,
[ledger](../research/original-verification-ledger.md#experience-and-levels-arc-world-030)). Other domains add their
rows when they are migrated.

## OriginalFunction attribute

`[OriginalFunction(address, OriginalFunctionScope.Full | Partial)]` (`Arcanum.Simulation.OriginalFunctionAttribute`) marks
the method that implements a function. An assembly that cannot reference `Arcanum.Simulation` declares its own attribute
of the same name and shape. The root test fails when a marked method has no row, a different scope or method name, or
when a `full` or `partial` row has no marked method.

## Oracle receipts

`./arcanum oracle` ([function oracle](../workflow/toolchain.md#function-oracle)) has each `OriginalExe` test write
`receipts/<ADDR>.json`: function, name, method, seed, inputs, mutants planted and caught, unexpected differences, the
recorded difference (XP3) with its counts, result, executable SHA-256, the method's IL hash and the build configuration.
The IL hash is a SHA-256 of the method's IL with every metadata token replaced by the name it refers to
(`tests/Shared/MethodIlHash.cs`), so it changes with the method's code and not with unrelated members. The root test
fails when an F3 row has no passing receipt or when the method's IL hash differs from its receipt: a changed function
needs a new oracle run and its receipt committed. A receipt from another build configuration leaves the test
inconclusive (root checks build Debug, like `./arcanum oracle`).

## Rule-mode switches

Every boolean switch of a profile composed in `GameRules` needs a row with a ledger ID in the
[rule-mode registry](../world/rule-modes.md#registry), and every row there a switch in code.

## Reconstruction references

[`ce-references.csv`](ce-references.csv) records, per area (`AGENTS.md`, `CLAUDE.md`, `docs`, `src`, `tests`,
`tools`), how many lines of tracked text files name the reconstruction: its repository name, its pinned commit, or the
word CE. The root test counts them again and fails when an area has more than its baseline; when an area has fewer, the
test prints the new numbers and the baseline is lowered in the same PR. Lock files and binary files are not counted.
