"""ARC-DEV-037: the quiet output of ./arcanum run.

A queued command writes its whole output (stdout and stderr) to one log file per ticket; the wrapper prints only its
ARCANUM_QUEUE lines (stderr), the log's path and, at the end, PASS or FAIL with the first error line and its context
(stdout). Every tool output stays in an agent session's context and is re-read on each later step, so a Godot or
dotnet run that prints thousands of lines costs far more than the few lines that decide it. The rule is
docs/workflow/toolchain.md#quiet-output-arc-dev-037.
"""
from __future__ import annotations

from pathlib import Path
import re
from typing import Sequence

from . import config

KEEP_LOGS = 200  # the newest logs kept; older ones are removed when a new run starts
CONTEXT_BEFORE, CONTEXT_LINES = 3, 40  # at most 40 lines shown around the first error: 3 before it, the rest after
MAX_WIDTH = 300
# Godot (ERROR:, SCRIPT ERROR:, USER ERROR:), dotnet (error CS0103:, MSB, NU, NETSDK; a failed test; a non-zero error
# count), Python (Traceback, ValueError: ...), .NET exceptions, the shell and git, and the FAIL markers and failure
# codes of smokes and self-tests (`... FAIL`, `native-smoke failure mode=... stage=... code=...`, `AREA_CAPTURE_FAILED`).
ERROR = re.compile(r"(?:^|[\s\[(])(?:SCRIPT ERROR|USER ERROR|ERROR|FATAL|FAIL(?:ED)?|Failed|Unhandled exception)\b"
                   r"|\berror (?:CS|MSB|NU|NETSDK)\d+|^Traceback \(most recent call last\)|\b[A-Z]\w*(?:Exception|Error)\b:"
                   r"|\bfailure (?:mode|stage)=|[A-Z]_FAILED\b|\b[1-9]\d* Error\(s\)|\b(?:Errors?|failures?)[:=]\s*[1-9]"
                   r"|^(?:fatal|error): |command not found|Segmentation fault")
# Counts of zero (`0 Error(s)`, `Failed:     0`, `errors=0`) are removed before the search, so a summary line that only
# counts zero errors is not an error line while `Failed: 0, Errors: 3` still is.
ZERO_COUNT = re.compile(r"\b0 Error\(s\)|\b(?:Failed|FAILED|Errors?|ERRORS?|failures?)[:=]\s*0\b")


def folder() -> Path:
    return config.home() / "run-logs"


def open_log(seq: int, kind: str) -> Path:
    """A new log path for ticket seq; removes all but the newest KEEP_LOGS - 1 older logs first."""
    path = folder()
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if path.is_symlink() or not path.is_dir():
        raise ValueError(f"the run log folder {path} must be a real folder")
    old = sorted((item for item in path.glob("run-*.log") if item.is_file()), key=lambda item: item.name)
    for item in old[:max(0, len(old) - (KEEP_LOGS - 1))]:
        try:
            item.unlink()
        except FileNotFoundError:
            pass
    return path / f"run-{seq:012d}-{kind}.log"


def is_error(line: str) -> bool:
    return bool(ERROR.search(ZERO_COUNT.sub("", line)))


def _cut(line: str) -> str:
    return line if len(line) <= MAX_WIDTH else line[:MAX_WIDTH] + " ..."


def first_error(lines: Sequence[str]) -> tuple[int, list[str]] | None:
    """The first error line's index and at most CONTEXT_LINES lines around it, or None when no line is an error."""
    for index, line in enumerate(lines):
        if is_error(line):
            start = max(0, index - CONTEXT_BEFORE)
            return index, [_cut(item) for item in lines[start:start + CONTEXT_LINES]]
    return None


def summary(log: Path, exit_code: int, ran: float | None) -> list[str]:
    """The stdout lines of a finished quiet run: PASS or FAIL, and the first error with its context (and the last error
    line when it lies beyond that context: an early harmless Godot ERROR must not hide the failure at the end).

    A run that passed but logged error lines (Godot often exits 0 after a SCRIPT ERROR) names their count and the first
    one. A failed run without an error line shows its last CONTEXT_LINES lines instead."""
    try:
        lines = log.read_text(errors="replace").splitlines()
    except FileNotFoundError:
        lines = []
    timing = f" ran={ran:.0f}s" if ran is not None else ""
    found = first_error(lines)
    marked = [index for index, line in enumerate(lines) if is_error(line)]
    errors = len(marked)
    if exit_code == 0:
        result = [f"PASS exit=0{timing} lines={len(lines)} log={log}"]
        if found is not None:
            result.append(f"note: {errors} error line(s) in the log; first (line {found[0] + 1}): "
                          f"{_cut(lines[found[0]].strip())}")
        return result
    result = [f"FAIL exit={exit_code}{timing} lines={len(lines)} log={log}"]
    if found is not None:
        index, context = found
        result.append(f"first error at line {index + 1} of {len(lines)} ({errors} error line(s)):")
        result.extend(context)
        if marked[-1] >= max(0, index - CONTEXT_BEFORE) + len(context):  # the last error is not in the context shown
            result.append(f"last error (line {marked[-1] + 1}): {_cut(lines[marked[-1]].strip())}")
    elif lines:
        result.append(f"no error line found; the last {min(len(lines), CONTEXT_LINES)} lines:")
        result.extend(_cut(item) for item in lines[-CONTEXT_LINES:])
    else:
        result.append("the command printed nothing")
    return result
