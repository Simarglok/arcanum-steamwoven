# Function audit against the original: state and plan

Status: **Plan, accepted by the owner** (2026-11-10), work item ARC-RES-105. Docs-only: no code change and no checks run
(the numbers below come from a read of the repository and a static scan of the retail `Arcanum.exe`, see
[method and limits](#2-method-and-limits)). Each slice is handed out as its own task: it takes its task code from the
coordinator, its own thread and its own PR; the labels `V0`, `V1` and so on are planning labels, not task codes.

The owner's question: what is the state of the audit of our code against the original executable, and does every
function of ours have to be checked?

## 1. Answer

- **No blanket function-by-function check.** A selective, risk-ranked one, behind one preparatory step: a list of all
  functions of the executable, which the repository does not have, so no coverage can be counted against it.
- **What exists is sound but covers about a sixth of the executable.** The [registry](../verification/functions.csv) holds
  869 functions, 777 of the roughly 4,700 functions the executable calls directly (16 %); in the game-logic range
  0x40xxxx–0x4Cxxxx 587 of the about 2,130 call targets are registered (28 %).
- **Most of the rest is not game logic** (C runtime, DirectX, interface drawing, editor, network, start-up), and the defects
  found so far were not function-level slips. The [architecture audit](architecture-audit-plan.md) found them in object
  identity, residency, persistence, time and goals: places no per-function comparison looks at.
- **Plan (section 5):** V0 a function census (list, domains, call reach); V1 triage of the addresses the documents cite but the
  registry lacks; V2 deepen the partial rows by risk, with magic and technology ridden on the running RES-103 and RES-104 walks;
  V3 grow the oracle over pure formulas in batches; V4 refresh the requirement matrix and add the gates that keep the registry
  honest. Cost in section 6.

## 2. Method and limits

- **Read:** [`functions.csv`](../verification/functions.csv), its [README](../verification/README.md), the root tests in
  `tests/Arcanum.Session.Tests/Verification`, the 38 oracle receipts, the [oracle](../../tools/original-oracle/README.md), the
  [original executable check](original-executable-check.md), the [verification ledger](original-verification-ledger.md) and
  its area files in `docs/research/ledger/`, the [requirement matrix](../requirements/coverage.md), the
  [architecture audit](architecture-audit-plan.md), the [gap register](../architecture/gap-register.md), the
  [missing systems plan](../project/missing-systems-plan.md) and the smoke runner (`tools/local_dev/smoke.py`).
- **Counted on the owner's Mac** with `llvm-objdump -d` over the retail 1.0.7.4 file (SHA-256 `9bd80511…3678d52`): the
  distinct targets of direct `call` instructions inside `.text` (4,731). Nothing of the listing was stored. Limits: functions
  reached only through a pointer (tables, callbacks, vtables) are not in this count, which is why Ghidra's 5,551
  ([oracle research](original-oracle-research.md#findings)) is higher; a call target may be a thunk or a shared tail; address
  ranges are a rough sort, not a domain label.
- **"Cited" is weak evidence.** An address that appears in any Markdown document (2,852 of the 4,731 call targets, 774 of them registered) was named, not
  necessarily read; only a ledger row, a registry row or a research study with a reading counts as a reading.
- **Not done:** no Ghidra run, no oracle run, no smoke run, no read of any function.

## 3. What exists

| Instrument | What it gives | Size now |
| --- | --- | --- |
| Registry [`functions.csv`](../verification/functions.csv) | One row per implemented or named function: status, reason, level F0–F4, evidence, implementing method | 869 rows: 277 `full`, 563 `partial`, 29 `not implemented`; F3 38, F2 685, F1 146 |
| `[OriginalFunction]` marks | The code is tied to the row; root tests fail on a mark without a row, a wrong scope or a `full`/`partial` row without a mark | 840 marked methods |
| Executable readings, two readers | A static reading of a function, a second blind reading for decision inputs | [ledger](original-verification-ledger.md) plus 14 area files under `docs/research/ledger/` |
| Function oracle | The original's function run in an emulator on 10,000+ seeded inputs, compared with ours, mutants planted | 38 receipts at F3, 12 of them `full` |
| `Arcanum.Executable` | Interprets the integer subset of x86 so that the original's own prototype code runs, not a transcription | prototypes only ([doc](../Arcanum.Executable.md)) |
| Smoke stages | The viewer drives a scenario on real data and asserts | 64 map stages (`./arcanum smoke pr`, `full`) |
| Requirement matrix | A status per functional requirement | 191 rows, fully re-checked 2026-10-20 (partial corrections to 2026-10-27): 4 implemented, 154 partial in the viewer |
| Architecture audit | Mismatches at the level of identity, residency, persistence, time, goals, scripts | closed (R1–R6, G1, MIG1–MIG16) |
| Behaviour walks | A person-like pass through a school or discipline against the original | RES-103 (magic by school) and RES-104 (technology by discipline), running |

Counts by level of the registry's `full` rows: 219 at F2, 46 at F1, 12 at F3. So 277 functions are claimed complete, and only
12 of them were executed against the original.

## 4. Gaps

1. **No denominator in git.** The Ghidra list of 5,551 functions lived in `research-private/`, which is not in the
   repository. Coverage cannot be stated, and a function nobody named is invisible.
2. **The registry only lists what was implemented or named.** 2,078 call targets are cited in documents but have no row:
   each is a reading with no registry state (implemented without a mark, not implemented, or not relevant). A further
   1,876 are not cited anywhere; most are probably runtime, interface or editor code, which is an estimate until V0.
3. **Unmarked code is invisible.** The root tests check mark against row and row against mark. Nothing finds code that
   implements an original function but carries no mark.
4. **`full` is mostly a two-reader reading.** 219 of the 277 are F2: read twice, never executed. That is the right bar for
   a branchy function that touches the world, and too weak for a pure formula.
5. **563 `partial` rows are the real backlog.** By domain: magic 105, AI 70, items 53, combat 34, interface 30, audio 26,
   goals 22, characters 22, skills 20, dialogue 19. Each row's `reason` says what is left out, so the list is already
   written; it is not ranked by risk.
6. **29 `not implemented` rows** are named and known: 14 interface functions (hover, pick, object windows), 8 item and trap functions (trap devices, use on a tile,
   inventory window), 3 magic (movement component, mana item recharge), 2 navigation, one script, one
   reaction.
7. **The oracle reaches only pure functions** (a stack frame, globals and a table; service calls are stubs). It cannot
   check lifecycle or ordering, which is where the audit found the systemic defects.
8. **The requirement matrix is behind the code** (2026-10-20); the
   [missing systems plan](../project/missing-systems-plan.md#1-answer) already lists this as E0-a.

## 5. Recommendation

A blanket check of every function would cost most of its effort on code that decides nothing: the C runtime, DirectX glue,
interface painting, the editor and the network layer that single player never reaches. It would also not find the defects
that mattered, because those were in how objects are addressed, kept and moved, not in a formula. Instead, in this order:

| Slice | Content | Where | Size | Needs |
| --- | --- | --- | --- | --- |
| **V0 Census** | List the executable's functions (Ghidra headless, plus the direct-call scan), tag each by range and call reach (reached from a registered function, from a game-loop root, or from neither), keep addresses and tags only in `docs/verification/census.csv` (no bytes, no listings, no names taken from any reconstruction), and add the counts to the verification README. | Mac | M | none |
| **V1 Triage** | For the cited-but-unregistered addresses (2,078 call targets in the first count, 2,656 function starts in the V0 census) and the V0 "reached from a game-loop root" set, give each a state: not relevant (with the reason class), read and implemented without a mark (add row and mark), read and not implemented (add a `not implemented` row), not yet read (F0 row). Work by call reach, the gameplay ranges first; one slice per domain. | Mac, reads | L (about 8 domain slices) | V0 |
| **V2 Partial rows by risk** | Rank the 563 partial rows by (a) the state it writes (saved, shared by other systems), (b) how often the game reaches it, (c) whether a smoke stage already exercises it; close the top of the list. Magic and technology: RES-103 and RES-104 add or correct their registry rows as an output; they are not repeated here. AI, items, combat and skills: one slice per domain after V1 shows which rows are dead. | Mac | L | V1 for ranking, RES-103/104 for magic and technology |
| **V3 Oracle growth** | Take the pure numeric functions among the `full` F2 rows and the V1 results (rolls, tables, caps, cost and time formulas) and run them in the oracle in batches of 5–10 per PR; an F2 `full` of that kind moves to F3. Rows whose function needs the world stay at F2 and get a smoke stage instead. | Mac | M per batch | V1 |
| **V4 Gates and matrix** | Refresh the requirement matrix against code (E0-a of the missing systems plan); add a root test that finds methods whose summary names an executable address but carry no mark, and one that a census address with state "implemented" has a row; document the three-state vocabulary. | cloud/Mac | S–M | V0, V1 |

Order: V0, then V1 and the matrix refresh in parallel, then V2 and V3. V0 and the matrix refresh can start now and need no
other slice.

**V0 is done** (ARC-RES-106): [`census.csv`](../verification/census.csv) lists 5,815 function starts with their call reach, and the
[verification README](../verification/README.md#function-census) gives the counts and what they do not say. It was made
with `llvm-objdump` and a direct-call, data-pointer and prologue scan (`tools/research/exe_census.py`), not Ghidra (not installed on the Mac, and not needed); the scan finds 867 of the registry's 871 functions on its own. V1 starts from the census:
the cited-but-unregistered rows are now 2,652 (any citation, not only call targets), 1,632 of them reached by a direct
call chain from a registry function or a game-loop root.

**Rules for every slice:** original behaviour is settled by the executable, not by a reconstruction; a row is added with
its level at the time (F1 or higher for anything implemented); no original bytes, listings or oracle outputs in git; an
anomaly is classified (reconstruction slip or original bug) under [D8](npc-combat-ai-plan.md#owner-decisions-2026-10-16);
a change that implements or changes a requirement updates its coverage row in the same PR.

## 6. Cost

- **V0:** one thread, small (a script plus one CSV and a doc paragraph); the scan itself runs in minutes.
- **V1:** the largest unknown. The 2,078 cited call targets (2,656 cited starts in the V0 census) is a ceiling: many are the callees of one function already read, and
  the V0 call reach cuts the list before any reading starts. Eight domain slices of a few hundred functions each, most rows
  being cheap sorting, is the working estimate; a real reading of a function takes the same effort as in earlier slices.
- **V2:** per domain, one or two slices of the size of the earlier slices on the same domain; the ranking decides how many.
- **V3:** one PR per batch; each function needs its model and a planted-mutant test, as the 38 existing ones.
- **A blanket check, for comparison:** about 4,700 functions at the pace of the existing registry (869 rows since the
  registry began on 2026-10-17, alongside the implementation) would take several months of work that adds no game feature
  and delay the missing systems ([plan](../project/missing-systems-plan.md)); the shortest route to the same safety is V0 and V1 plus the behaviour walks.

## 7. What this plan does not change

- The [missing systems plan](../project/missing-systems-plan.md) keeps its order; V0 and the matrix refresh do not touch
  game code and can run beside any epic.
- No coverage row changes in this PR.
- The running RES-103 and RES-104 walks keep their scope.
