#!/usr/bin/env python3
"""Summarise controller-test evidence logs (ARC-GPAD-1376, docs/input/controller-schemes.md section 0.8 and 5).

An evidence file is written by the viewer when the owner turns the Evidence Log line on in Options. One JSON object per line with the
closed tokens below (time in ms since the session start, no wall-clock time, no path, name or text). This tool only reads: it prints, for each
test task and scheme, the task time and the counts of inputs, wrong targets, cancels, refused actions and pauses, then the session totals.
A line that does not match the format exactly is counted as rejected and never echoed.

    python3 -B tools/input/evidence_summary.py FILE [FILE...] [--json]
    python3 -B tools/input/evidence_summary.py --self-test
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass, field

MAGIC = "arcanum-evidence-v1"
MAX_LINE_BYTES = 256
MAX_TASK = 99
# Closed sets: keep equal to src/Arcanum.Input/EvidenceLog.cs (a C# test compares them).
KINDS = ["session-start", "session-end", "task-start", "task-end", "command", "pause-on", "pause-off"]
COMMANDS = ["none", "walk", "stop", "face", "examine", "attack", "use", "talk", "take", "open-container", "move-item", "equip", "drop",
            "open-window", "close-window", "page", "select", "confirm", "back", "hotkey", "radial", "target", "turn-based-toggle", "end-turn",
            "quick-save", "quick-load", "menu", "other"]
SCHEMES = ["original", "direct", "pointer", "touch-pad"]
DEVICES = ["keyboard-mouse", "pad", "touch"]
OUTCOMES = ["ok", "cancelled", "refused", "wrong-target"]
KEYS = {"t", "k", "c", "s", "d", "o", "n"}


@dataclass
class Event:
    millis: int
    kind: str
    command: str
    scheme: str
    device: str
    outcome: str
    task: int


def parse_line(line: str) -> Event | None:
    """The event of a line, or None for a blank, header, oversized or malformed line or one with an unknown key or token."""
    line = line.strip()
    if not line or len(line.encode("utf-8")) > MAX_LINE_BYTES:
        return None
    try:
        raw = json.loads(line, object_pairs_hook=_no_duplicates)
    except (ValueError, RecursionError):
        return None
    if not isinstance(raw, dict) or set(raw) != KEYS:
        return None
    t, n = raw["t"], raw["n"]
    if type(t) is not int or t < 0 or type(n) is not int or not 0 <= n <= MAX_TASK:
        return None
    for key, allowed in (("k", KINDS), ("c", COMMANDS), ("s", SCHEMES), ("d", DEVICES), ("o", OUTCOMES)):
        if not isinstance(raw[key], str) or raw[key] not in allowed:
            return None
    return Event(t, raw["k"], raw["c"], raw["s"], raw["d"], raw["o"], n)


def _no_duplicates(pairs):
    keys = [key for key, _ in pairs]
    if len(set(keys)) != len(keys):
        raise ValueError("duplicate key")
    return dict(pairs)


def is_header(line: str) -> bool:
    if len(line.encode("utf-8")) > MAX_LINE_BYTES:
        return False
    try:
        raw = json.loads(line)
    except (ValueError, RecursionError):
        return False
    return isinstance(raw, dict) and raw == {"format": MAGIC}


@dataclass
class Stats:
    inputs: int = 0
    wrong_targets: int = 0
    cancels: int = 0
    refused: int = 0
    pauses: int = 0

    def add(self, e: Event) -> None:
        if e.kind == "pause-on":
            self.pauses += 1
        if e.kind != "command":
            return
        self.inputs += 1
        if e.outcome == "wrong-target":
            self.wrong_targets += 1
        elif e.outcome == "cancelled":
            self.cancels += 1
        elif e.outcome == "refused":
            self.refused += 1


@dataclass
class TaskRun:
    task: int
    scheme: str
    start_ms: int
    end_ms: int | None = None
    stats: Stats = field(default_factory=Stats)

    @property
    def seconds(self) -> float | None:
        return None if self.end_ms is None else max(0, self.end_ms - self.start_ms) / 1000


@dataclass
class Session:
    source: str
    rejected: int = 0
    header: bool = False
    duration_ms: int = 0
    schemes: set[str] = field(default_factory=set)
    devices: set[str] = field(default_factory=set)
    totals: Stats = field(default_factory=Stats)
    tasks: list[TaskRun] = field(default_factory=list)


def summarise_lines(source: str, lines) -> Session:
    s = Session(source)
    current: TaskRun | None = None
    last = 0
    for number, line in enumerate(lines):
        if number == 0 and is_header(line.strip()):
            s.header = True
            continue
        e = parse_line(line)
        if e is None:
            if line.strip():
                s.rejected += 1
            continue
        last = max(last, e.millis)
        s.schemes.add(e.scheme)
        s.devices.add(e.device)
        s.totals.add(e)
        if e.kind == "task-start":
            if current is not None and current.end_ms is None:
                current.end_ms = e.millis
            current = TaskRun(e.task, e.scheme, e.millis)
            s.tasks.append(current)
        elif e.kind == "task-end":
            if current is not None and current.end_ms is None:
                current.end_ms = e.millis
            current = None
        elif current is not None and current.end_ms is None:
            current.stats.add(e)
    s.duration_ms = last
    return s


def summarise_file(path: str) -> Session:
    with open(path, encoding="utf-8", errors="replace") as handle:
        return summarise_lines(path.replace("\\", "/").rsplit("/", 1)[-1], handle)


def as_dict(sessions: list[Session]) -> dict:
    return {"format": MAGIC, "sessions": [{
        "file": s.source, "headerOk": s.header, "rejectedLines": s.rejected, "seconds": s.duration_ms / 1000,
        "schemes": sorted(s.schemes), "devices": sorted(s.devices),
        "totals": vars(s.totals),
        "tasks": [{"task": t.task, "scheme": t.scheme, "seconds": t.seconds, **vars(t.stats)} for t in s.tasks],
    } for s in sessions]}


def render(sessions: list[Session]) -> str:
    out: list[str] = []
    for s in sessions:
        out.append(f"{s.source}: {s.duration_ms / 1000:.1f} s, schemes {','.join(sorted(s.schemes)) or '-'}, devices {','.join(sorted(s.devices)) or '-'}"
                   + ("" if s.header else ", NO HEADER") + (f", {s.rejected} rejected lines" if s.rejected else ""))
        out.append(f"  total: inputs {s.totals.inputs}, wrong targets {s.totals.wrong_targets}, cancels {s.totals.cancels}, "
                   f"refused {s.totals.refused}, pauses {s.totals.pauses}")
        out.append("  task  scheme      time     inputs  wrong  cancels  refused  pauses")
        for t in s.tasks:
            time = "unfinished" if t.seconds is None else f"{t.seconds:7.1f}s"
            out.append(f"  {t.task:>4}  {t.scheme:<10}  {time:>10}  {t.stats.inputs:>6}  {t.stats.wrong_targets:>5}  {t.stats.cancels:>7}  "
                       f"{t.stats.refused:>7}  {t.stats.pauses:>6}")
        if not s.tasks:
            out.append("  (no task markers: step the Evidence Log line in Options to the task before each task)")
    return "\n".join(out)


# A synthetic session shared with the C# test (tests/Arcanum.TerrainViewer.Tests/EvidenceLogTests.cs, Sample): same lines.
SAMPLE = """{"format":"arcanum-evidence-v1"}
{"t":0,"k":"session-start","c":"none","s":"direct","d":"pad","o":"ok","n":0}
{"t":1000,"k":"task-start","c":"none","s":"direct","d":"pad","o":"ok","n":1}
{"t":1500,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":1}
{"t":2500,"k":"command","c":"examine","s":"direct","d":"pad","o":"wrong-target","n":1}
{"t":3000,"k":"command","c":"examine","s":"direct","d":"pad","o":"ok","n":1}
{"t":3500,"k":"pause-on","c":"none","s":"direct","d":"pad","o":"ok","n":1}
{"t":4000,"k":"pause-off","c":"none","s":"direct","d":"pad","o":"ok","n":1}
{"t":5000,"k":"task-end","c":"none","s":"direct","d":"pad","o":"ok","n":1}
{"t":6000,"k":"task-start","c":"none","s":"direct","d":"pad","o":"ok","n":2}
{"t":6500,"k":"command","c":"open-window","s":"direct","d":"pad","o":"refused","n":2}
{"t":7000,"k":"command","c":"target","s":"direct","d":"pad","o":"cancelled","n":2}
{"t":9500,"k":"task-end","c":"none","s":"direct","d":"pad","o":"ok","n":2}
{"t":10000,"k":"task-start","c":"none","s":"direct","d":"pad","o":"ok","n":3}
{"t":10500,"k":"command","c":"quick-save","s":"direct","d":"pad","o":"ok","n":3}
{"t":12000,"k":"session-end","c":"none","s":"direct","d":"pad","o":"ok","n":0}
not a line
{"t":1,"k":"command","c":"C:/Users/someone/secret","s":"direct","d":"pad","o":"ok","n":0}
"""


def self_test() -> int:
    session = summarise_lines("sample.jsonl", SAMPLE.splitlines())
    checks = [
        (session.header, "header"), (session.rejected == 2, f"rejected {session.rejected}"), (session.duration_ms == 12000, "duration"),
        (session.totals.inputs == 6, f"inputs {session.totals.inputs}"), (session.totals.wrong_targets == 1, "wrong"),
        (session.totals.cancels == 1, "cancels"), (session.totals.refused == 1, "refused"), (session.totals.pauses == 1, "pauses"),
        (len(session.tasks) == 3, "tasks"),
        (session.tasks[0].seconds == 4.0 and session.tasks[0].stats.inputs == 3 and session.tasks[0].stats.wrong_targets == 1
         and session.tasks[0].stats.pauses == 1, "task 1"),
        (session.tasks[1].seconds == 3.5 and session.tasks[1].stats.refused == 1 and session.tasks[1].stats.cancels == 1, "task 2"),
        (session.tasks[2].seconds is None and session.tasks[2].stats.inputs == 1, "task 3 unfinished"),
        (parse_line('{"t":1,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":0,"x":1}') is None, "extra key"),
        (parse_line('{"t":-1,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":0}') is None, "negative time"),
        (parse_line('{"t":1,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":100}') is None, "task range"),
        (parse_line('{"t":true,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":0}') is None, "bool is not a time"),
        (parse_line("{" + '"t":1,' * 100 + "}") is None, "oversized"),
        (parse_line('{"t":1,"t":2,"k":"command","c":"walk","s":"direct","d":"pad","o":"ok","n":0}') is None, "duplicate key"),
        (not is_header("[" * 100000) and summarise_lines("x", ["[" * 100000]).rejected == 1, "deeply nested first line"),
    ]
    text = render([session]) + json.dumps(as_dict([session]))
    checks.append(("secret" not in text and "Users" not in text, "rejected text is never echoed"))
    failed = [name for ok, name in checks if not ok]
    if failed:
        print("evidence_summary self-test FAILED: " + ", ".join(failed))
        return 1
    print(f"evidence_summary self-test passed ({len(checks)} checks)")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Summarise controller-test evidence logs.")
    parser.add_argument("files", nargs="*", help="evidence-*.jsonl files")
    parser.add_argument("--json", action="store_true", help="print JSON instead of a table")
    parser.add_argument("--self-test", action="store_true", help="check the tool on a synthetic session and exit")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    if not args.files:
        parser.error("no evidence file given")
    sessions = []
    for path in args.files:
        try:
            sessions.append(summarise_file(path))
        except OSError as error:
            print(f"cannot read {path}: {error.strerror}", file=sys.stderr)
            return 2
    print(json.dumps(as_dict(sessions), indent=2) if args.json else render(sessions))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
