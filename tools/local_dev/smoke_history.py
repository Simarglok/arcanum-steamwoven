"""ARC-DEV-399: what each smoke step ran and how long each stage took, beside the launch queue's history.

The queue's `history.jsonl` says when a run was enqueued, started and ended, not what it ran: the command is cut at 400
characters, so the stage list (`ARC_AREA_MAP_SMOKE_ONLY`) is lost, and nothing inside a run is timed. The viewer prints
`ARC-DEV-399 stage-time NAME ms=N` when a map-smoke stage ends (TerrainViewerSmokeStageSet); the smoke runner reads those
lines from the step's log after the step and appends one line to `smoke-stages.jsonl` in the queue folder, keyed by the
ticket number of `history.jsonl` (so the two files join on `seq`), with the queue wait and the run time read from the
history line. Nothing here changes a step's result: a failure to record is a note, never a failed smoke.

    python3 -B -m tools.local_dev.smoke_history [N]    the last N recorded steps as a table (default 20)

The rule is docs/workflow/toolchain.md#smoke-runner-arc-dev-061.
"""
from __future__ import annotations

import fcntl
import json
import os
from pathlib import Path
import re
import sys
from typing import Any, Mapping, Sequence

from . import run_queue

SCHEMA = "arcanum-smoke-stages-v1"
FILE = "smoke-stages.jsonl"
STAGE_TIME = re.compile(r"^ARC-DEV-399 stage-time (?P<stage>[a-z][a-z0-9-]*) ms=(?P<ms>\d+)\s*$", re.MULTILINE)
LOG_NAME = re.compile(r"^run-(?P<seq>\d+)-[a-z]+\.log$")
LOOKBACK_LINES = 400  # the newest history lines searched for the step's ticket
FILE_BYTES, FILE_KEEP = 2_000_000, 5000


def ticket_of(log: Path | None) -> int | None:
    """The queue ticket number a quiet run's log belongs to (its file name `run-<seq>-<kind>.log`)."""
    match = LOG_NAME.match(log.name) if log is not None else None
    return int(match["seq"]) if match else None


def stage_times(log: Path | None) -> dict[str, int]:
    """The milliseconds of each stage the log's viewer timed, in the order it ran them (a stage printed twice adds up)."""
    try:
        text = log.read_text(encoding="utf-8", errors="replace") if log is not None else ""
    except OSError:
        return {}
    times: dict[str, int] = {}
    for match in STAGE_TIME.finditer(text):
        times[match["stage"]] = times.get(match["stage"], 0) + int(match["ms"])
    return times


def queue_line(seq: int, directory: Path | None = None) -> dict[str, Any] | None:
    """The queue history line of ticket seq (the run wrapper writes it before it returns), or None."""
    path = (directory or run_queue.folder()) / "history.jsonl"
    try:
        with path.open("rb") as handle:
            handle.seek(0, os.SEEK_END)
            handle.seek(max(0, handle.tell() - 700 * LOOKBACK_LINES))  # a history line is up to about 700 bytes
            lines = handle.read().decode(errors="replace").splitlines()[-LOOKBACK_LINES:]
    except OSError:
        return None
    for line in reversed(lines):
        try:
            entry = json.loads(line)
        except ValueError:
            continue
        if isinstance(entry, dict) and entry.get("seq") == seq:
            return entry
    return None


def entry_of(name: str, kind: str, requested: Sequence[str], exit_code: int, log: Path | None,
             directory: Path | None = None) -> dict[str, Any] | None:
    """The record of one finished step, or None when its log is not a queue log (a nested run, a test's runner)."""
    seq = ticket_of(log)
    if seq is None:
        return None
    queued = queue_line(seq, directory) or {}
    started, enqueued, ended = queued.get("startedAt"), queued.get("enqueuedAt"), queued.get("endedAt")
    times = stage_times(log)
    waited = round(started - enqueued, 1) if started is not None and enqueued is not None else None
    ran = round(ended - started, 1) if started is not None and ended is not None else None
    return {"schema": SCHEMA, "seq": seq, "step": name, "kind": kind, "checkout": Path(queued.get("cwd", "?")).name,
            "stages": list(requested) or list(times), "stageMs": times, "waitSeconds": waited, "ranSeconds": ran,
            # the part of the run no stage clock covers: the process start, the map load, the exit
            "outsideStagesSeconds": None if ran is None else round(max(0.0, ran - sum(times.values()) / 1000), 1),
            "exit": exit_code, "enqueuedAt": enqueued, "startedAt": started, "endedAt": ended}


def append(entry: Mapping[str, Any], directory: Path | None = None) -> None:
    """Adds one line to smoke-stages.jsonl, keeping the newest FILE_KEEP lines once it passes FILE_BYTES."""
    folder = directory or run_queue.folder()
    folder.mkdir(mode=0o700, parents=True, exist_ok=True)
    if folder.is_symlink() or not folder.is_dir():
        raise ValueError(f"the launch queue folder {folder} must be a real folder")
    path = folder / FILE
    lock = os.open(folder / ".smoke-stages.lock", os.O_RDWR | os.O_CREAT | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX)
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
        try:
            os.write(fd, (json.dumps(entry, sort_keys=True) + "\n").encode())
        finally:
            os.close(fd)
        if path.stat().st_size > FILE_BYTES:
            kept = path.read_text(errors="replace").splitlines()[-FILE_KEEP:]
            hidden = folder / f".smoke-stages-{os.getpid()}"
            fd = os.open(hidden, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            try:
                os.write(fd, ("\n".join(kept) + "\n").encode())
            finally:
                os.close(fd)
            os.replace(hidden, path)
    finally:
        os.close(lock)


def table(entry: Mapping[str, Any]) -> list[str]:
    """The lines a step prints in the smoke summary: queue wait, run time, and each stage's time."""
    waited, ran = entry.get("waitSeconds"), entry.get("ranSeconds")
    head = f"  {entry['step']}: waited {_s(waited)}, ran {_s(ran)}"
    times: Mapping[str, int] = entry.get("stageMs") or {}
    if times:
        head += f" (outside the stages {_s(entry.get('outsideStagesSeconds'))})"
    lines = [head]
    for stage, ms in times.items():
        lines.append(f"    {stage:<18} {ms / 1000:7.1f}s")
    return lines


def _s(seconds: float | None) -> str:
    return "?" if seconds is None else f"{seconds:.0f}s"


def recent(count: int, directory: Path | None = None) -> list[Mapping[str, Any]]:
    try:
        text = ((directory or run_queue.folder()) / FILE).read_text(errors="replace")
    except OSError:
        return []
    entries = []
    for line in text.splitlines()[-count:]:
        try:
            item = json.loads(line)
        except ValueError:
            continue
        if isinstance(item, dict) and item.get("schema") == SCHEMA:
            entries.append(item)
    return entries


def main(argv: Sequence[str]) -> int:
    try:
        count = int(argv[0]) if argv else 20
    except ValueError:
        print("usage: python3 -B -m tools.local_dev.smoke_history [N]  (N: how many recorded steps to show)")
        return 2
    entries = recent(count)
    if not entries:
        print("no smoke steps recorded yet")
        return 0
    for item in entries:
        print(f"#{item['seq']} {item.get('checkout')} exit={item.get('exit')}")
        print("\n".join(table(item)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
