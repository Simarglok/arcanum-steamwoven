# Owner checks: step format and parser

The owner's manual checks live in one list, [owner-recheck-list.md](../project/owner-recheck-list.md). This page fixes the
format of a check, so one parser reads it for every consumer: the local `./arcanum qa` page, the status write-back, the
Ctrl+B report tag and the in-game overlay (slices of epic #2185, ARC-DEV-2186 is the first). A row that the parser cannot
read is a failure of `python3 -B tools/docs_check.py`, so a malformed row never reaches a reader.

## The format

- **Section.** A heading `## ARC-TYPE-NNN — title (PR, date)`: the task code first (the type of
  [task naming](task-naming.md), then the number), then free text. A heading without a task code may hold no step table.
- **Step table.** Inside a section the header row is `| # | Step | Expected … | Status |` (the third title may continue, for
  example `Expected (from the original executable)`), then the separator row, then one row per step. A table ends at the
  first line that is not a row; a later row of the same section continues it. A pipe inside a cell is written `\|`.
- **Step id.** `ARC-TYPE-NNN#k`: the section's task code, `#`, and the row number `k` (a positive integer, no leading
  zero) from the `#` column. The cell holds only `k`; a full id is accepted when its code is the section's. `k` is unique
  in a section; the id is unique in the file. Steps are only ever appended or marked, and a number is not reused, so an id
  names one check for good.
- **Step**, **Expected**: non-empty text in game terms (repository text only; no screenshots or game content).
- **Status.** One of `open` (not compared yet), `same` (the owner saw the original do the same), `differs` (the owner saw a
  difference; the note links the issue that follows it) and `cannot-test` (the owner cannot run the step; the note says why).
  A note may follow in `(…)` or `[…]`, for example `same (5 pieces, owner 2026-11-12)` or `differs ([#1234](…))`. Only the
  first word decides the status.

## Ctrl+B tag (ARC-DEV-2189)

The page writes the step the owner has open to `local/qa/active-check.txt`; the viewer's Ctrl+B report then carries the line
`Check: ARC-TYPE-NNN#k` (before its first heading), which the page reads to list a step's reports. The format of the id is the
one above; see [the toolchain](toolchain.md#owner-qa-page-arc-dev-2187) and [the viewer](../rendering/terrain-viewer.md).

A report is tagged only with a live check (ARC-DEV-3130): the active file is younger than 12 hours, the `checks.json` beside it is
younger than 72 hours and lists the step, and the step was not answered more than 12 hours ago; otherwise the report carries no `Check:` line. See
[the toolchain](toolchain.md#owner-qa-page-arc-dev-2187) (`./arcanum qa refresh` rebuilds the list).

## In-game overlay (ARC-DEV-2190)

`./arcanum qa` also writes the list as `local/qa/checks.json` for the overlay of the viewer (Ctrl+Q, on only with
`./arcanum qa overlay on` or `ARC_OWNER_QA=1`), which records `same` / `differs` / `cannot-test` with a note into
`local/qa/results.jsonl` and sends a Ctrl+B report tagged with the step. See [the viewer](../rendering/terrain-viewer.md#owner-checks-overlay-arc-dev-2190).

Known variant of 2026-11-15: a stray second row 4 of ARC-UI-1364 sits after the section's bullets; the parser reads it as
the next free number of that section (`KNOWN_REPEATS` in the parser) until issue #2193 renumbers it.

## The parser

`tools/local_dev/owner_checks.py` (Python standard library only, no writes):

```sh
python3 -B -m tools.local_dev.owner_checks                     # this checkout's list: summary and problems
python3 -B -m tools.local_dev.owner_checks --ref origin/main   # the list as it is on a ref (`git show REF:PATH`)
python3 -B -m tools.local_dev.owner_checks --file PATH         # any file
python3 -B -m tools.local_dev.owner_checks --json              # steps and problems as JSON
```

From Python: `parse(text)`, `parse_file(path)` and `parse_ref(ref)` return `Parsed` (`sections`, `steps`, `problems`);
a `Step` has `id`, `code`, `type`, `task`, `number`, `step`, `expected`, `status`, `note`, `line`, `section_title` and
`section_line`; a `Problem` has the 1-based `line` and a `message`. `parse_step_id(text)` splits an id. A reader in another
language (the C# overlay) takes the `--json` output, or the same format: the step id pattern is
`^(ARC-([A-Z]+)-(\d+))#([1-9]\d*)$` and the status words are fixed above. The exit code is 1 when a row cannot be read.

Reading from a ref or a file never changes a checkout; a writer of statuses (`./arcanum qa apply`) is a later slice and
works through a docs PR. The local page that lists the steps and records the owner's results is `./arcanum qa`
([owner QA page](toolchain.md#owner-qa-page-arc-dev-2187), ARC-DEV-2187).

Tests: `python3 -B -m unittest tools.local_dev.test_owner_checks` (synthetic lists only).
