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
the exports of the executable (or the executable itself) first. A host method that
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

## Function census

ARC-RES-106 (V0 of the [function audit plan](../research/function-audit-plan.md#5-recommendation)),
[`census.csv`](census.csv): the denominator of the audit, one row per function start found in the code section of the pinned
1.0.7.4 executable, addresses and tags only (no bytes, listings or names). Made by `tools/research/exe_census.py`, which
reads the player's own `Arcanum.exe` with `llvm-objdump`, streams the disassembly and stores none of it:

```sh
python3 -B tools/research/exe_census.py --self-test                    # synthetic checks, no game file
python3 -B tools/research/exe_census.py --exe "$DAT_ROOT/Arcanum.exe"   # rewrites census.csv; refuses another file
```

| Column | Content |
| --- | --- |
| `address`, `size` | start in upper-case hex; bytes up to the next start |
| `range` | the start's 64 KiB block (`0x4B` = 0x4B0000..0x4BFFFF) |
| `source` | how the start was found, joined with `+`: `call` (a direct call lands on it), `ptr` (a dword in `.data`/`.rdata` or a code immediate holds its address and the previous instruction ends a block), `pro` (`push ebp; mov ebp, esp` after a block end), `reg` (the registry names it), `seed` (entry point, WinMain, loop roots) |
| `callers` | call and unconditional-jump sites in other functions that go straight to it (a count of sites, not of distinct callers; its own recursion is left out) |
| `calls_out` | distinct other functions it calls or jumps to directly |
| `address_taken` | `y` when a data dword (4-aligned, in `.data` or `.rdata`) or an operand of the code holds a value equal to its address, so it may be reached through a table, callback or event; plain numeric constants in the code range count too, so this is an upper bound |
| `registered` | `y` when [`functions.csv`](functions.csv) has a row |
| `cited` | number of Markdown documents under `docs/` that cite the address as `0xNNNNNN`; this README and the audit plan are left out, since they describe the census itself |
| `reach` | by direct calls only: `registered` (reached from a registry function), `loop` (from a game-loop root: the in-game loop, module ping, engine ping, the six per-frame slots, [TL1/TL3/TL5/TL6](../architecture/original-time-loop.md)), `both`, `none` |

Counts of the run on 2026-11-11 (retail 1.0.7.4, SHA-256 `9bd80511…3678d52`):

| | Functions |
| --- | --- |
| In the census | 5,815 (the Ghidra trial of 2026-10-17 found 5,551) |
| Registered (rows of `functions.csv`) | 871 of the registry's 871; 867 of them are also found by the scan on its own |
| Cited in a document, not registered | 2,656 (the plan's 2,078 counted call targets only) |
| `reach` registered / loop / both / none | 724 / 79 / 2,840 / 2,172 |
| `address_taken` | 1,185 |
| `reach` none and `address_taken` y | 1,034 (reachable only through a table, callback or event: V1 treats them as roots) |
| `reach` none, not `address_taken`, not registered | 1,138 (no direct route from a registry function or a loop root; V1 sorts the cited ones first, 1,024 of the `none` rows are cited) |
| Direct callers: 100 or more / size 16 bytes or less | 49 / 581 (helpers and thunks) |

What the numbers do not say:

- **Reach is direct-call reach.** Time events, goal handlers, window and script callbacks, and the 62-module table are
  called through pointers, so most of the game's per-frame work shows as `none` or `address_taken`. `reach` marks what is
  known to be live, not what is dead; a `none` row is never evidence that nothing runs it.
- **The start list is a scan, not an analysis.** A leaf function that no direct call, data pointer or frame prologue points
  at is missing (4 of the 871 registry rows were found only because the registry names them). Jump tables and the byte
  index tables behind them (210 ranges) are cut out of the code before the scan, and a direct call target that is not on a
  decoded instruction boundary would be dropped and reported (none in this run). A `size` runs to the next start, so it
  includes any table inside the function. The largest unit, 0x468930, is the one giant switch function the
  [prototype extraction study](../research/prototype-extraction.md) describes (357 returns, a table inside it); it is one row.
- **1,844 cited addresses are not starts**: call sites, mid-function labels and loop heads (for example 0x41F048). They
  stay in the documents; the census lists functions only.
- **The code section includes the C runtime and library code** (the part from about 0x580000 on); the census does not
  separate it, `range` and the callers' counts do. DirectX, Miles and Bink are imports and not in the list.

## Function triage

V1 of the [function audit plan](../research/function-audit-plan.md#5-recommendation) gives every census function that the registry lacked one state, slice by slice (address bands). Slices A to F cover the bands 0x400000-0x4FFFFF with a census row, H1 and H2 the bands 0x500000-0x59FFFF, G and I the export functions the census lacks; each row gives the counts of its own ledger section on the day it merged (a later task may have changed a state in the registry, which holds the current one). The states: **not relevant** with a reason class (recorded in the slice's ledger section only), **read and implemented** (a registry row, `full` or `partial`, and an `[OriginalFunction]` mark in code), **read, not implemented** (a `not implemented` row) and **not yet read** (an F0 row). Read functions start at F1; F2 only where an existing document holds two independent readings.

| Slice | Band | Functions triaged | Ledger section |
| --- | --- | --- | --- |
| A (ARC-RES-117) | 0x400000-0x41FFFF | 402 added: 37 implemented, 68 not implemented, 297 not relevant | [census-triage-a](../research/ledger/census-triage-a.md) |
| B (ARC-RES-118) | 0x420000-0x43FFFF | 364 added (94 of the band's 458 starts had rows): 39 implemented (2 full, 37 partial), 169 not implemented, 156 not relevant, none not yet read | [census-triage-b](../research/ledger/census-triage-b.md) |
| C (ARC-RES-108) | 0x440000-0x45FFFF | 389 added (0x452830 and 0x45A060 got their rows from other PRs): 95 implemented, 68 not implemented, 226 not relevant | [census-triage-0x44-0x45](../research/ledger/census-triage-0x44-0x45.md) |
| D (ARC-RES-110) | 0x460000-0x46FFFF and 0x490000-0x4AFFFF | 381 rows: 22 implemented (1 full, 21 partial), 168 not implemented (88 of them the deferred network game), 191 not relevant, none not yet read | [census-triage-d](../research/ledger/census-triage-d.md) |
| E (ARC-RES-109) | 0x4B0000-0x4CFFFF | 474 added (211 of the band's 685 starts had rows): 145 implemented (12 got a row and a mark, 133 are implemented in several methods or in a host with no mark yet), 2 not implemented, 78 not yet read (F0), 249 not relevant | [function-audit-4b-4c](../research/ledger/function-audit-4b-4c.md) |
| F (ARC-RES-111) | 0x4D0000-0x4FFFFF | 855 of the band's 922 starts triaged (67 had rows): 633 registry rows added (3 implemented, 167 not implemented, 463 not yet read, F0) and 222 not relevant; the state of every address is in [`triage/band-4d-4f.csv`](triage/band-4d-4f.csv) | [census-triage-4d-4f](../research/ledger/census-triage-4d-4f.md) |
| H1 (ARC-RES-119) | 0x500000-0x54FFFF | 933 added (50 of the band's 983 had rows): 17 implemented, 225 not implemented, 691 not relevant | [census-triage-h1](../research/ledger/census-triage-h1.md) |
| H2 (ARC-RES-120) | 0x550000-0x59FFFF | 1,143 added (93 had rows): 41 implemented, 152 not implemented, 950 not relevant | [census-triage-h2](../research/ledger/census-triage-h2.md) |
| G and I (ARC-RES-121) | the export functions that are not census starts: G = 0x470000-0x49FFFF and the other 0x400000-0x4FFFFF, I = 0x500000-0x59FFFF | 1,042 read: 1,011 not relevant, 31 with a rule state (18 `not implemented` rows and 13 implemented rows with marks, ARC-DEV-389) | [census-triage-gi](../research/ledger/census-triage-gi.md), [`triage/groups-g-i.csv`](triage/groups-g-i.csv) |

**Groups G and I (wave 3).** The slices above took the census rows of an address band. The Ghidra text export of ARC-DEV-377 and ARC-DEV-379 lists 6,717 functions with a C file and 139 thunks where the census lists 5,815, so wave 3 gives a state to the 1,042 export functions that are not census starts: **group G** is every such function in 0x470000-0x49FFFF (the census has no row of 0x47 and 0x48 and 30 of 0x49: the giant prototype fill switch, split into case blocks) and every other such function in 0x400000-0x4FFFFF; **group I** is every such function in 0x500000-0x59FFFF. The 44 census starts that have no C file (43 thunks, one inside another function) are a finding of the ledger section, not a group. The reading was docs-only, so its record is the [CSV](triage/groups-g-i.csv) (state, class, callers and pointer holders per address) and the ledger section; the 1,011 not relevant functions stay there and have no registry row. ARC-DEV-389 then gave the 31 functions with a rule state their registry rows (18 `not implemented`, 13 `full` or `partial` with an `[OriginalFunction]` mark); `FunctionRegistryTests` keeps the CSV, the registry and the marks equal.

## Partial rows by risk

V2 of the [function audit plan](../research/function-audit-plan.md#5-recommendation) ranks a domain's `partial` rows by the state they
write, how often the game reaches them and whether a smoke stage exercises them, then reads the top of the list a second time and
compares it with the code. Each domain slice keeps its ranking in `triage/v2-<domain>.csv` (columns `address`, `name`, `score`, `writes`,
`reach`, `smoke`, `slice`, `row`; score = 2 × writes + reach + smoke) and its readings in a ledger section; a difference that writes
other state than the original becomes an issue.

| Slice | Rows | Ranking | Ledger section |
| --- | --- | --- | --- |
| Items 1 (ARC-RES-1104) | 83 ranked, 17 read: 6 from F1 to F2, reasons made exact, gaps #1283 (no-drop on transfers), #1284 (NPC re-wield flag) and #1288 (NPC look-for bits after a drop) | [`triage/v2-items.csv`](triage/v2-items.csv) | [partial-rows-by-risk](../research/ledger/items.md#partial-rows-by-risk-arc-res-1104) |
| Items 2 (ARC-RES-1104) | the other 66 read: 20 from F1 to F2, reasons made exact, gaps #1299 (body decay's destroyed items), #1300 (restock delay, owner decision), #1301 (a broken generate-once container) and #1306 (items sold into a broken container) | [`triage/v2-items.csv`](triage/v2-items.csv) (slice 2) | [partial-rows-by-risk-items-slice-2](../research/ledger/items.md#partial-rows-by-risk-items-slice-2-arc-res-1104) |
| AI 1 (ARC-RES-1104) | 92 ranked, the 21 scored 6 read: 11 from F1 to F2, reasons made exact, gaps #1312 (a waiting companion's death), #1313 (wander goals), #1314 (waypoint index not saved), #1315 (please move before an attack) and #1316 (one change-of-place slot, owner decision) | [`triage/v2-ai.csv`](triage/v2-ai.csv) | [partial-rows-by-risk-ai-slice-1](../research/ledger/npc-ai.md#partial-rows-by-risk-ai-slice-1-arc-res-1104) |

## Address-named methods

ARC-DEV-1106 (V4 of the [function audit plan](../research/function-audit-plan.md#5-recommendation)). A method that names an
executable function in its XML `<summary>` but carries no `OriginalFunction` mark is invisible to the mark and row tests above, so
the root test `FunctionRegistryTests.EveryAddressNamedMethodIsMarkedExemptOrPending` finds those methods and requires each to be in
one of three states:

| State | Where it is written | Meaning |
| --- | --- | --- |
| **marked** | `[OriginalFunction(address, scope)]` on the method, with its registry row | the method implements the function; the tests above check the pairing |
| **exempt** | a row with `state` `exempt` in [`address-methods.csv`](address-methods.csv) and a class: `host` (the method only calls a marked rule or hosts it; one method per address, the mark stays on the rule) or `reference` (the summary cites the address as evidence or context and the method implements none of that function) | the missing mark is deliberate |
| **pending** | a row with `state` `pending` and no class | a known gap: nobody has yet read the method against the function; the V1 and V2 slices turn it into marked or exempt |

How the test reads the code: it reads the tracked `.cs` files under `src/`, `tools/` and `experiments/`, takes each method (a
declaration with a return type, a parameter list and a body that follows the doc comment and its attributes; types, constructors,
properties, fields and interface or abstract declarations are left out) and looks in its `<summary>` for `0x` and six hex digits
that are a function start in [`census.csv`](census.csv) (an address that is not a census start, such as a mid-function label or a
global, does not count). A member name with overloads, or the same name in two types of one file, is one row and counts as marked only when every one is. Plain `//` comments and preprocessor lines between the doc comment and the declaration are skipped; an attribute may share the declaration's line. It fails when

- an unmarked method naming a census function has no row (mark it and add its registry row, or add an `exempt` row with its class);
- a row names a member that is marked now, no longer names a census function in its summary or no longer exists (remove the row);
- the file is malformed (header `file,member,state,class`, rows sorted by file and member, one row per member, an existing file);
- the number of `pending` rows is not exactly `PendingCeiling` in the test. The constant only goes down, with the row that closes: a new method is marked or
  exempt, never pending. The first run (2026-11-12, 744 rows) recorded every unmarked method as pending, because telling a host
  from an unread implementation needs a reading of the function; V1 and V2 close the rows, and the PR that turns a row into marked
  or exempt removes or edits it and lowers the ceiling.

A new `.cs` file is read from the moment it is tracked (`git add`), like the mark scan above.

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
