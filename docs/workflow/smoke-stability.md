# Smoke stability baseline (ARC-DEV-2227)

Owner request 2026-11-15: smoke tests on `main` break often. A read-only audit
classified the cases; this page keeps its numbers as the **baseline** so the
owner can compare the statistics about a week later (around 2026-11-22). The
scope rules themselves stay in [smoke scope](tasks.md#smoke-scope) and the
[smoke runner](toolchain.md#smoke-runner-arc-dev-061).

## Baseline of 2026-11-15

Sample: 61 issues about smoke failures or a red `main` (#1187 to #2209). The
GitHub issue backlog starts only on 2026-11-11, so this is about 4 days of
issues; before that, `main` has 4 smoke-fix commits. The queue journal
(`run-queue/smoke-stages.jsonl`) holds 5,323 smoke steps from 2026-11-12 17:07
to 2026-11-15 13:50. `main` takes about 140 to 170 merges a day (170, 151 and
139 on 11-12 to 11-14). Fix-PR bodies were read for 31 of the 61 issues.

| Class | Issues | Meaning |
|---|---|---|
| A | 15 | The PR ran a hand-picked stage list narrower than its diff affects, or skipped self-test or the map battery; a deterministic regression landed on main |
| B | 8 | A configuration run only by the complete smoke or by opt-in (town/ruins/bates/cave batteries, HD, Original rules) |
| C | 9 | State leaking between stages of one process (RETIRED_GENERATION_ALIVE three times, each with a new viewer field) |
| D | 14 | "Flakes" from load or timing; 5 of the 8 closed ones were real ordering bugs |
| E | 8 | Bugs of the runner or of tool tests (not in the root checks) |
| F | 5 | Root `dotnet test` red on main, or main broken between two merges |
| G | 2 | Real-data pins (opt-in `DAT_ROOT`) |

| Number | Baseline |
|---|---|
| Found by another thread's PR run vs by the daily build | 57 vs 4 |
| Time to fix, 46 closed issues | median 2.6 h, p75 6.8 h, max 24.8 h |
| Issues still open (as of 2026-11-15, all matching issues, not only the sample) | 16 |
| Re-runs on a clean main checkout in 3 days (queue steps in `mainwt`, `base-main`, `main-check`, `mainclean`, `before*`) | about 120 |
| Complete smoke wall time | 54 min (83 steps, 11-13), 74 min (85 steps, 11-14) |
| PR smoke session (232 sessions of at least 5 steps) | median 11.8 min, p90 39.7 min |
| Last daily build (11-15) | no smoke: the dotnet root check timed out after 2700 s |

Limits: the queue steps include work in progress of branches, so step failure
rates are not main-only; the causes of #2172, #1855, #1911 and the open flakes
come from issue text only.

## Measures (owner decision 2026-11-15)

Rows are in launch order; the numbers are those of the audit (measure 5, the
automatic leak guard, and measure 7 were not chosen yet).

| Measure | Issue |
|---|---|
| 1. `--auto` selection is a lower bound checked before merge | #2223 (done: `./arcanum smoke verify-pr`, [PR floor](tasks.md#smoke-scope)) |
| 6. The daily runs smoke even when the dotnet checks fail or time out | #2224 (done: `./arcanum daily` runs the smoke after red or timed-out root checks, reports both and still blocks the package) |
| 3. `tools/local_dev` unit tests and the smoke self-test in the root checks | #2225 (done: `tools/local_dev/run_tests.py`, about 90 s, and `smoke self-test --headless`, about 15 s, in CLAUDE.md section 2; #2199 and #2209 fixed) |
| 2. Rolling smoke on `origin/main` with an automatic issue and a red-on-main flag | #2226 |
| 4. Flake register (retry once, log FLAKY, tracked list); later, after measure 2 | not filed yet |

Not chosen: serializing merges or a full smoke per merge (about 150 merges a
day times 12 min would be about 30 h of Mac time a day).

## How to recompute the numbers

- Issues: `gh issue list --state all --limit 300 --json number,title,labels,state,createdAt,closedAt`,
  filtered by smoke, flaky, stage, "fails on main", RETIRED_GENERATION; classify
  each by its fix PR (`gh pr view N --json title,body`).
- Time to fix: `closedAt - createdAt` over the closed issues of the sample.
- Found by whom: the issue text names the thread or the daily run that saw it.
- Merges a day: `git log origin/main --first-parent --since=... --format=%cs | sort | uniq -c`.
- Smoke times and clean-main re-runs: the queue journal `run-queue/smoke-stages.jsonl`
  (steps grouped by checkout name and session).
