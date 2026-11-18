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
import signal
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


SHUTDOWN_LEAK = re.compile(r"Leaked unsafe reference|ObjectDB instances were leaked")
PASS_LINE = re.compile(r"\bPASS\b")


CRASH_SIGNALS = (signal.SIGSEGV, signal.SIGABRT, signal.SIGBUS, signal.SIGILL, signal.SIGFPE)


# ARC-DEV-2412: Godot 4.7.2 .NET sometimes dies on the .NET finalizer thread inside a Godot type's Finalize (seen in
# `Godot.StringName.Finalize` -> `DisposablesTracker.UnregisterDisposable`, as signal 11 or as an AccessViolationException),
# only under load (about 1 in 4 of the cave smoke's runs with four Godot runs at once, never in 10 runs alone). The engine's
# crash handler then also prints "The caller thread can't call the function `propagate_notification()`": that is the handler
# itself, running on the finalizer thread; no viewer code calls the scene tree from another thread.
CRASH_EVIDENCE = re.compile(r"handle_crash: Program crashed with signal|Fatal error\. System\.AccessViolationException")
GODOT_FINALIZE = re.compile(r"\bGodot\.\w+\.Finalize\(\)")


def engine_finalizer_crash(lines: Sequence[str]) -> bool:
    """True when the log shows a crash (the engine's crash handler or a fatal AccessViolationException) and a Godot type's
    Finalize in the backtrace: the finalizer-thread crash of ARC-DEV-2412, not a failed check."""
    return any(CRASH_EVIDENCE.search(line) for line in lines) and any(GODOT_FINALIZE.search(line) for line in lines)


def signal_note(lines: Sequence[str], exit_code: int) -> str | None:
    """ARC-DEV-2360: a command a signal ended, with exit code -N (the command itself died on it) or 128+N (a shell or
    wrapper passing its child's status on), gets a `note:` line. For a crash signal (SIGSEGV, SIGABRT, SIGBUS, SIGILL,
    SIGFPE) it says where the log's last PASS line is, and when only Godot's shutdown leak lines follow it (no other
    error line) that this looks like a crash at engine shutdown, seen once in 31 headless self-tests and gone on the
    retry. The exit code stays as it is. The note claims no more than the log shows: it does not say the checks passed."""
    number = -exit_code if exit_code < 0 else exit_code - 128
    crash = next((item for item in CRASH_SIGNALS if item.value == number), None)
    if exit_code >= 0 and crash is None:
        return None
    try:
        name = signal.Signals(number).name
    except ValueError:
        name = f"signal {number}"
    if crash is None:
        return f"note: ended by {name}, not a crash signal"
    if engine_finalizer_crash(lines):
        return (f"note: killed by {name} in a Godot type's Finalize on the .NET finalizer thread (ARC-DEV-2412, an engine crash "
                "seen under load): not a failed check; the smoke runner runs such a step once more")
    passes = [index for index, line in enumerate(lines) if PASS_LINE.search(line)]
    if not passes:
        return f"note: killed by {name} and the log has no PASS line: a crash, not a shutdown crash"
    last = passes[-1]
    after = lines[last + 1:]
    others = [line for line in after if is_error(line) and not SHUTDOWN_LEAK.search(line)]
    if others:
        return (f"note: killed by {name}; an error line follows the last PASS line (line {last + 1}), so this is not a "
                "shutdown crash alone: read it")
    if any(SHUTDOWN_LEAK.search(line) for line in after):
        return (f"note: killed by {name} after the last PASS line (line {last + 1}) and Godot's shutdown leak lines, with "
                "no other error line between them: looks like a crash at engine shutdown (ARC-DEV-2360); retry once, "
                "a repeat is a real failure")
    return f"note: killed by {name} after the last PASS line (line {last + 1}) with no shutdown leak line: read what follows it"


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
    note = signal_note(lines, exit_code)
    if note is not None:
        result.append(note)
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
