#!/usr/bin/env python3
"""The owner check list parser (ARC-DEV-2186, slice 1 of the owner QA epic #2185).

One reader of ``docs/project/owner-recheck-list.md``: the ``./arcanum qa`` page, the status write-back, the Ctrl+B
report tag and the in-game overlay all take their steps from here, so a malformed row can never reach them. The format
is written down in the rules of the list itself and in ``docs/workflow/owner-checks.md``; the short form is:

* a section is a ``## ARC-TYPE-NNN — title`` heading followed by a table ``| # | Step | Expected ... | Status |``;
* a step id is the section code, ``#`` and the row number: ``ARC-TYPE-NNN#k`` (the ``#`` cell holds ``k``);
* a status is ``open``, ``same``, ``differs`` or ``cannot-test``, optionally followed by a note in ``(...)`` or ``[...]``.

The parser is stdlib only and never writes: it reads a file path or ``git show REF:PATH``. Run it directly::

    python3 -B -m tools.local_dev.owner_checks                      # the list in this checkout: summary and problems
    python3 -B -m tools.local_dev.owner_checks --ref origin/main    # the list as it is on that ref
    python3 -B -m tools.local_dev.owner_checks --json               # the steps as JSON (for a C# or page reader)
    python3 -B -m tools.local_dev.owner_checks --self-test

The exit code is 1 when the list has a problem.
"""
from __future__ import annotations

import argparse
from dataclasses import asdict, dataclass, field
import json
from pathlib import Path
import re
import subprocess
import sys

LIST_PATH = "docs/project/owner-recheck-list.md"
STATUSES = ("open", "same", "differs", "cannot-test")
STEP_ID = re.compile(r"^(ARC-([A-Z]+)-(\d+))#([1-9]\d*)$")
SECTION_CODE = re.compile(r"^(ARC-[A-Z]+-\d+)(?=\s|$)")
# the status word, then nothing or a note that starts with ( or [ (a space may come before it)
STATUS = re.compile(r"^(" + "|".join(STATUSES) + r")(?:\s*([(\[].*))?$", re.DOTALL)
COLUMNS = ("#", "Step", "Expected", "Status")
# Known row variants of the list as it stands on 2026-11-15 (the existing rows are not changed by ARC-DEV-2186):
# a row of a section that follows a break of its table (a blank line and bullets) continues that table, and a row
# whose number the section already uses is read as the next free number. The repeat below is the only one; any other
# repeated number is a problem, and the entry goes when the row is renumbered (issue #2193).
KNOWN_REPEATS = frozenset({("ARC-UI-1364", 4)})


@dataclass(frozen=True)
class Step:
    id: str          # ARC-TYPE-NNN#k
    code: str        # ARC-TYPE-NNN
    type: str        # TYPE
    task: int        # NNN
    number: int      # k
    step: str
    expected: str
    status: str      # open | same | differs | cannot-test
    note: str        # the text after the status word, "" when there is none (for example "(owner 2026-11-12)")
    line: int        # 1-based line of the row in the file
    section_title: str
    section_line: int


@dataclass(frozen=True)
class Section:
    code: str
    title: str       # the heading text after "## "
    line: int
    steps: tuple[Step, ...] = ()


@dataclass(frozen=True)
class Problem:
    line: int
    message: str

    def format(self, path: str = LIST_PATH) -> str:
        return f"{path}:{self.line}: {self.message}"


@dataclass
class Parsed:
    sections: list[Section] = field(default_factory=list)
    problems: list[Problem] = field(default_factory=list)

    @property
    def steps(self) -> list[Step]:
        return [step for section in self.sections for step in section.steps]

    def by_id(self) -> dict[str, Step]:
        return {step.id: step for step in self.steps}


def parse_step_id(text: str) -> tuple[str, str, int, int] | None:
    """``ARC-TYPE-NNN#k`` -> (code, type, task number, row number); None when the text is not a step id."""
    match = STEP_ID.match(text.strip())
    return (match.group(1), match.group(2), int(match.group(3)), int(match.group(4))) if match else None


def split_row(line: str) -> list[str] | None:
    """The cells of a table row (``\\|`` is a literal pipe); None when the line is not a row."""
    text = line.strip()
    if not text.startswith("|"):
        return None
    if text.endswith("|") and not text.endswith("\\|"):
        text = text[:-1]
    cells, current, escaped = [], [], False
    for char in text[1:]:
        if escaped:
            current.append("|" if char == "|" else "\\" + char)
            escaped = False
        elif char == "\\":
            escaped = True
        elif char == "|":
            cells.append("".join(current).strip())
            current = []
        else:
            current.append(char)
    if escaped:
        current.append("\\")
    cells.append("".join(current).strip())
    return cells


def _is_separator(cells: list[str]) -> bool:
    return bool(cells) and all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells)


def _is_header(cells: list[str]) -> bool:
    return (len(cells) == 4 and cells[0] == COLUMNS[0] and cells[1] == COLUMNS[1]
            and cells[2].startswith(COLUMNS[2]) and cells[3] == COLUMNS[3])


def parse(text: str) -> Parsed:
    """Read the list text; every row that cannot be read is a Problem with its 1-based line number."""
    result = Parsed()
    lines = text.split("\n")
    code = title = ""
    section_line = 0
    steps: list[Step] = []
    seen: dict[int, int] = {}
    state = "none"       # none | header | rows  (inside a section: before / after the table header)
    had_table = False    # the section already had a step table, so a row after a break continues it
    fence = ""

    def close() -> None:
        nonlocal steps
        if code:
            result.sections.append(Section(code, title, section_line, tuple(steps)))
        steps = []

    for index, raw in enumerate(lines, 1):
        stripped = raw.lstrip()
        opener = re.match(r"(`{3,}|~{3,})", stripped)
        if fence:
            if opener and opener.group(1)[0] == fence[0] and len(opener.group(1)) >= len(fence):
                fence = ""
            continue
        if opener:
            fence = opener.group(1)
            continue
        heading = re.match(r"^##\s+(.*?)\s*$", raw)
        if heading and not raw.startswith("###"):
            close()
            title = heading.group(1)
            section_line = index
            match = SECTION_CODE.match(title)
            code = match.group(1) if match else ""
            seen = {}
            state = "none"
            had_table = False
            continue
        cells = split_row(raw)
        if cells is None:
            state = "none"           # a table ends at the first line that is not a row
            continue
        if not code:
            # a table row outside a coded section
            if not _is_separator(cells):
                result.problems.append(Problem(index, "table row outside a section whose heading starts with a task code ARC-TYPE-NNN"))
            continue
        if _is_header(cells):
            state = "header"
            had_table = True
            continue
        if _is_separator(cells):
            if state == "header":
                state = "rows"
            else:
                result.problems.append(Problem(index, "table separator without a step table header (# | Step | Expected | Status)"))
            continue
        if state != "rows" and not had_table:
            result.problems.append(Problem(index, "table row without the step table header (# | Step | Expected | Status) and separator before it"))
            continue
        step = _read_row(cells, index, code, title, section_line, seen, result.problems)
        if step is not None:
            steps.append(step)
    close()
    return result


def _read_row(cells: list[str], line: int, code: str, title: str, section_line: int, seen: dict[int, int],
              problems: list[Problem]) -> Step | None:
    if len(cells) != 4:
        problems.append(Problem(line, f"row has {len(cells)} columns, expected 4 (# | Step | Expected | Status); a pipe in a cell is written \\|"))
        return None
    number_cell, step_text, expected, status_cell = cells
    ok = True
    number = 0
    if re.fullmatch(r"[1-9]\d*", number_cell):
        number = int(number_cell)
    else:
        parsed = parse_step_id(number_cell)
        if parsed is not None and parsed[0] == code:
            number = parsed[3]
        else:
            ok = False
            problems.append(Problem(line, f"bad step id: '{number_cell}' (the # cell is the row number k, giving {code}#k)"))
    if ok and number in seen:
        if (code, number) in KNOWN_REPEATS:
            number = max(seen) + 1
        else:
            ok = False
            problems.append(Problem(line, f"step id {code}#{number} repeats the row at line {seen[number]}"))
    if not step_text:
        ok = False
        problems.append(Problem(line, "missing Step text"))
    if not expected:
        ok = False
        problems.append(Problem(line, "missing Expected text"))
    status_match = STATUS.match(status_cell)
    if status_match is None:
        ok = False
        problems.append(Problem(line, f"unknown status: '{status_cell}' (one of {', '.join(STATUSES)}, then an optional note in (...) or [...])"))
    if not ok:
        return None
    seen[number] = line
    _, kind, task = STEP_ID.match(f"{code}#1").groups()[:3]  # the section code always has the ARC-TYPE-NNN shape
    return Step(f"{code}#{number}", code, kind, int(task), number, step_text, expected, status_match.group(1),
                (status_match.group(2) or "").strip(), line, title, section_line)


def parse_file(path: str | Path) -> Parsed:
    return parse(Path(path).read_text(encoding="utf-8"))


def read_ref(ref: str, path: str = LIST_PATH, repo: str | Path | None = None) -> str:
    """The list text as ``git show REF:PATH`` gives it; nothing in the checkout is touched."""
    command = ["git"] + (["-C", str(repo)] if repo else []) + ["show", f"{ref}:{path}"]
    done = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    if done.returncode != 0:
        raise RuntimeError(f"git show {ref}:{path} failed: {done.stderr.strip()}")
    return done.stdout


def parse_ref(ref: str, path: str = LIST_PATH, repo: str | Path | None = None) -> Parsed:
    return parse(read_ref(ref, path, repo))


def to_json(parsed: Parsed) -> str:
    """Steps and problems as JSON; the keys are the field names of Step and Problem."""
    return json.dumps({"steps": [asdict(step) for step in parsed.steps],
                       "problems": [asdict(problem) for problem in parsed.problems]}, indent=1, ensure_ascii=False)


def summary(parsed: Parsed) -> str:
    counts = {status: sum(1 for step in parsed.steps if step.status == status) for status in STATUSES}
    parts = ", ".join(f"{count} {status}" for status, count in counts.items() if count)
    return f"{len(parsed.sections)} sections, {len(parsed.steps)} steps ({parts or 'none'}), {len(parsed.problems)} problem(s)"


def _self_test() -> int:
    import unittest
    from tools.local_dev import test_owner_checks
    suite = unittest.defaultTestLoader.loadTestsFromModule(test_owner_checks)
    outcome = unittest.TextTestRunner(verbosity=0).run(suite)
    return 0 if outcome.wasSuccessful() else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--file", help=f"read this file (default: {LIST_PATH} of this checkout)")
    source.add_argument("--ref", help="read the list with `git show REF:PATH`, for example origin/main")
    parser.add_argument("--path", default=LIST_PATH, help="the list path inside the repository (with --ref)")
    parser.add_argument("--json", action="store_true", help="print the steps and problems as JSON")
    parser.add_argument("--self-test", action="store_true", help="run the unit tests")
    args = parser.parse_args(argv)
    if args.self_test:
        return _self_test()
    repo = Path(__file__).resolve().parents[2]
    shown = args.file or (f"{args.ref}:{args.path}" if args.ref else LIST_PATH)
    try:
        parsed = parse_ref(args.ref, args.path, repo) if args.ref else parse_file(args.file or repo / LIST_PATH)
    except (OSError, RuntimeError) as error:
        print(f"owner_checks: {error}", file=sys.stderr)
        return 2
    if args.json:
        print(to_json(parsed))
    else:
        for problem in parsed.problems:
            print(problem.format(shown))
        print(f"owner_checks: {summary(parsed)}")
    return 1 if parsed.problems else 0


if __name__ == "__main__":
    sys.exit(main())
