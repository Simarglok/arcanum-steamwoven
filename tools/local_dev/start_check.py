"""ARC-DEV-059: `./arcanum run` checks that its command started the intended mode.

Every Godot or launcher run had to be checked by hand within its first minute (ARC-DEV-029): read the log for the
mode's start line, look for a usage error, then wait for the result in another step. The quiet wrapper now does it: it
reads the run's log as it fills and, before the command's start line appears, stops the run as a failed start when a
start failure line comes (a usage error) or when no start line came within the window (the Godot project manager, a
viewer that never loaded). The rule is docs/workflow/toolchain.md#checking-that-a-run-started-arc-dev-029.
"""
from __future__ import annotations

from dataclasses import dataclass
import re
from typing import Sequence

DEFAULT_WINDOW_SECONDS = 60.0
FAILED_START_EXIT = 125  # the wrapper stopped a command that did not start, or that ended before its start line
GRACE_SECONDS = 5.0  # after a start failure line the command may still exit by itself (and print its own reason)
MAX_SHOWN = 200
MAX_PARTIAL = 65536  # bytes of a line without a newline kept before it is scanned as it is

# The start lines of the project's Godot and export commands: the viewer's resolved mode (ARC-DEV-059; the load line
# of a build from before it), the viewer self-test's first stage, the export endpoint, the terrain export's steps and
# the player package (their phase lines, never their failure lines), and, for a command with `--import`, Godot's
# import progress (a first import also shows it before a viewer reads its arguments). A
# command whose output does not reach the log by then (a wrapper that sends Godot's output elsewhere, the WORLD
# validation, which prints only its summary at the end) gives its own line with --expect.
GODOT_START = (r"ARC-DEV-059 viewer start |ARC-RENDER-004 load stage=begin|ARC-DEV-043 self-test stage "
               r"|^export-endpoint: validate (?!FAILED)|^validate-macos: step (?!.*FAILED)"
               r"|^package: (?!could not start|stopped|FAIL)")
IMPORT_START = r"^\[ *\d+% \] "
REMEDY = "give the command's own start line with --expect, or --start-within 0"

# Lines that mean the command did not start what was asked: a launcher or tool usage error (argparse), the viewer's
# argument or game-folder failure (printed since ARC-DEV-059; before, it was drawn on the HUD only), a command or script
# the shell or Python could not find (only their own messages: an engine line about some missing file is not one).
START_FAILURE = (r"^usage: |^[\w./-]+: error: (?:unrecognized arguments|the following arguments are required|argument "
                 r"|invalid choice)|failure stage=(?:Arguments|GameFolder) "
                 r"|^(?:\S*/)?(?:sh|zsh|bash|dash|env)(?::\d+|: line \d+)?: .*(?:command not found|No such file or directory)"
                 r"|^\S*python[\d.]*: can't open file |Invalid project path|Couldn't load project")


@dataclass(frozen=True)
class StartCheck:
    """What a run's start looks like: the start line pattern, the failure pattern and the seconds allowed."""
    start: re.Pattern[str]
    failure: re.Pattern[str]
    seconds: float = DEFAULT_WINDOW_SECONDS
    expected: bool = False  # the start line was given with --expect: an end with 0 before it is a failed start too

    def describe(self) -> str:
        return self.start.pattern if len(self.start.pattern) <= MAX_SHOWN else self.start.pattern[:MAX_SHOWN] + " ..."


def for_run(kind: str, expect: str | None, seconds: float | None, command: Sequence[str] = ()) -> StartCheck | None:
    """The check of a `./arcanum run`: --expect replaces the default start lines; Godot kinds (window, headless, export, quick, solo)
    have defaults, dotnet has none (a build or test ends by itself and its quiet output may print nothing for minutes).
    seconds 0 turns the check off."""
    window = DEFAULT_WINDOW_SECONDS if seconds is None else seconds
    if window < 0:
        raise ValueError("--start-within must be 0 (no start check) or a positive number of seconds")
    if window == 0:
        return None
    if expect is not None:
        try:
            start = re.compile(expect)
        except re.error as error:
            raise ValueError(f"--expect is not a valid regular expression: {error}") from error
    elif kind in ("window", "headless", "export", "quick", "solo"):
        start = re.compile(GODOT_START + ("|" + IMPORT_START if "--import" in command else ""))
    else:
        return None
    return StartCheck(start, re.compile(START_FAILURE), window, expected=expect is not None)


def scan(check: StartCheck, lines: list[str]) -> tuple[str, str] | None:
    """("started", line) at the first start line, ("failed", line) at a start failure line before it, else None."""
    for line in lines:
        if check.start.search(line):
            return "started", line
        if check.failure.search(line):
            return "failed", line
    return None


def cut(line: str) -> str:
    line = line.strip()
    return line if len(line) <= MAX_SHOWN else line[:MAX_SHOWN] + " ..."
