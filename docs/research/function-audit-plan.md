# Function audit against the original: plan

Status: **Live plan, accepted by the owner** (2026-11-10), work item ARC-RES-105. V0 (census) and V4a (matrix refresh, ARC-RES-107) are
done; V1 (triage) is in progress by waves; V2 runs by domain (the items domain's first slice is done, ARC-RES-1104), V3 is open and V4 has one gate left (a census address marked implemented has a row). Each slice is handed out as its own task: it takes its
task code from the coordinator, its own thread and its own PR; the labels `V0`, `V1` and so on are planning labels, not task codes.
The state counts (registry rows by status and level, census size, oracle receipts) are kept in the
[verification README](../verification/README.md), not here.

The owner's question was whether every function of ours has to be checked against the original executable. The answer is **no blanket
function-by-function check**: it would spend most of its effort on code that decides nothing (the C runtime, DirectX glue, interface
painting, the editor, the network layer) and would not find the defects that mattered, because the [architecture
audit](../architecture/gap-register.md) found those in how objects are addressed, kept and moved, not in a formula. A selective,
risk-ranked check is planned instead:

## 5. Recommendation

| Slice | Content | Where | Size | Needs |
| --- | --- | --- | --- | --- |
| **V0 Census** | **Done (ARC-RES-106).** List the executable's functions, tag each by range and call reach, keep addresses and tags only in [`docs/verification/census.csv`](../verification/census.csv) (no bytes, no listings, no names taken from any reconstruction), add the counts to the verification README. Made with `llvm-objdump` and a direct-call, data-pointer and prologue scan (`tools/research/exe_census.py`). | Mac | M | none |
| **V1 Triage** | For the cited-but-unregistered addresses and the census's "reached from a game-loop root" set, give each a state: not relevant (with the reason class), read and implemented without a mark (add row and mark), read and not implemented (add a `not implemented` row), not yet read (F0 row). Work by call reach, the gameplay ranges first; one slice per domain. Wave 3 (ARC-RES-121, groups G and I of the Ghidra export, 1,042 functions the census did not list) is read: see the [ledger section](ledger/census-triage-gi.md) and the [group definition](../verification/README.md#function-triage); 31 carry a rule and have registry rows since ARC-DEV-389 (18 not implemented, 13 implemented). | Mac, reads | L (about 8 domain slices) | V0 |
| **V2 Partial rows by risk** | Rank the `partial` rows by (a) the state it writes (saved, shared by other systems), (b) how often the game reaches it, (c) whether a smoke stage already exercises it; close the top of the list. Magic and technology: the RES-103 and RES-104 walks add or correct their registry rows as an output; they are not repeated here. AI, items, combat and skills: one slice per domain after V1 shows which rows are dead. Items slice 1 is done (ARC-RES-1104): the 83 items rows ranked in [`triage/v2-items.csv`](../verification/triage/v2-items.csv), the top 17 read twice and compared with the code in the [ledger section](ledger/items.md#partial-rows-by-risk-arc-res-1104) (six rows from F1 to F2, three gaps as issues #1283, #1284 and #1288); the domain order of the next slices is in issue #1104. | Mac | L | V1 for ranking, RES-103/104 for magic and technology |
| **V3 Oracle growth** | Take the pure numeric functions among the `full` F2 rows and the V1 results (rolls, tables, caps, cost and time formulas) and run them in the oracle in batches of 5-10 per PR; an F2 `full` of that kind moves to F3. Rows whose function needs the world stay at F2 and get a smoke stage instead. The oracle reaches only pure functions (a stack frame, globals and a table; service calls are stubs), so it cannot check lifecycle or ordering. | Mac | M per batch | V1 |
| **V4 Gates and matrix** | The matrix refresh is done (V4a, ARC-RES-107, E0-a of the [missing systems plan](../project/missing-systems-plan.md#1-answer)). The root test that finds methods whose summary names an executable address but carry no mark (unmarked code is invisible to the mark/row tests), with the written three states marked, exempt and pending, is done (ARC-DEV-1106, [verification README](../verification/README.md#address-named-methods)). Open: one that a census address with state "implemented" has a row. | cloud/Mac | S-M | V0, V1 |

Order: V1, then V2 and V3. A blanket check of the roughly 4,700 call targets would take months of work that adds no game feature and delay
the missing systems; the shortest route to the same safety is V1 plus the behaviour walks.

**Rules for every slice:** original behaviour is settled by the executable, not by a reconstruction; a row is added with
its level at the time (F1 or higher for anything implemented); no original bytes, listings or oracle outputs in git; an
anomaly is classified (reconstruction slip or original bug) under [D8](npc-combat-ai-plan.md#owner-decisions-2026-10-16);
a change that implements or changes a requirement updates its coverage row in the same PR.

## 7. What this plan does not change

- The [missing systems plan](../project/missing-systems-plan.md) keeps its order; the audit slices do not touch game code and can run
  beside any epic.
- The running RES-103 and RES-104 walks keep their scope.
