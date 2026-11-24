"""ARC-DEV-3115: read-only reader of a macOS `.WindowServer-*.ips` watchdog report.

The reports (`/Library/Logs/DiagnosticReports/.WindowServer-DATE.ips`, a hidden file) hold a stackshot whose JSON is cut off
at the end. This tool repairs the cut (closes the open brackets after the last complete `},`), then prints

* the wait chain of the WindowServer main thread (`ws_main_thread`): who it blocks on, hop by hop, until a thread that waits on
  nothing with a turnstile;
* every thread in an uninterruptible kernel wait (`TH_UNINT`) with its process ancestry (`Python <- bash <- codex ...`), the
  resident size, the wait event and the first kernel frames, and which of them share the wait event of the chain's last thread.

It only reads the file given on the command line and writes nothing. How to read the result: docs/workflow/mac-crash-forensics.md.

    python3 -B tools/local_dev/windowserver_report.py [--frames N] REPORT.ips [REPORT.ips ...]

Field names (`processByPid`, `threadById`, `turnstileInfo`, `waitInfo`, `waitEvent`, `kernelFrames`, ...) are the ones read by hand
from the four reports of 2026-11-08..21; a few spelling variants are accepted, and a missing field is printed as `?`, never an error.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass, field
import json
from pathlib import Path
import re
import sys
from typing import Any

DEFAULT_FRAMES = 8
MAX_CHAIN = 32
MAX_REPAIR_TRIES = 64
MAX_ANCESTRY = 16

NAME_KEYS = ("procname", "name", "processName")
PARENT_KEYS = ("parentPid", "ppid", "parentpid", "parent_pid")
RSS_KEYS = ("residentMemoryBytes", "residentSize", "residentBytes", "rss")
THREAD_NAME_KEYS = ("name", "threadName")
STATE_KEYS = ("state", "threadState")
FRAME_KEYS = ("kernelFrames", "kernelStack", "kernelFrame")


@dataclass(frozen=True)
class Thread:
    pid: str
    process: str
    tid: str
    name: str
    states: tuple[str, ...]
    wait_event: str
    wait_text: str
    kernel_offsets: tuple[int, ...]

    @property
    def uninterruptible(self) -> bool:
        return any("UNINT" in state for state in self.states)


@dataclass(frozen=True)
class Process:
    pid: str
    name: str
    parent: str
    rss: str
    threads: tuple[Thread, ...]


@dataclass
class Report:
    header: dict[str, Any] = field(default_factory=dict)
    repaired: bool = False
    processes: dict[str, Process] = field(default_factory=dict)

    def threads(self) -> list[Thread]:
        return [thread for process in self.processes.values() for thread in process.threads]


def _scan_closers(text: str) -> list[tuple[int, str]]:
    """Every `}` followed (after blanks) by a comma, outside strings: (end of the brace, closers still open after it)."""
    stack: list[str] = []
    found: list[tuple[int, str]] = []
    in_string = False
    escaped = False
    length = len(text)
    for index, char in enumerate(text):
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
        elif char == "{":
            stack.append("}")
        elif char == "[":
            stack.append("]")
        elif char in "}]":
            if stack:
                stack.pop()
            if char == "}":
                probe = index + 1
                while probe < length and text[probe] in " \t\r\n":
                    probe += 1
                if probe < length and text[probe] == ",":
                    found.append((index + 1, "".join(reversed(stack))))
    return found


def repair_json(text: str) -> tuple[Any, bool]:
    """Parse JSON; when it is cut off, keep the text up to the last complete `},` and close the open brackets.

    Returns (value, repaired). Raises ValueError when no cut point gives valid JSON."""
    try:
        return json.loads(text), False
    except json.JSONDecodeError:
        pass
    candidates = _scan_closers(text)
    for end, closers in reversed(candidates[-MAX_REPAIR_TRIES:]):
        try:
            return json.loads(text[:end] + closers), True
        except json.JSONDecodeError:
            continue
    raise ValueError("the report JSON cannot be repaired (no complete '},' cut point gives valid JSON)")


def _first(source: dict[str, Any], keys: tuple[str, ...], default: Any = None) -> Any:
    for key in keys:
        if key in source and source[key] is not None:
            return source[key]
    return default


def _find_key(value: Any, key: str, depth: int = 0) -> Any:
    if depth > 5:
        return None
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = _find_key(child, key, depth + 1)
            if found is not None:
                return found
    return None


def _text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        return value
    return json.dumps(value, sort_keys=True)


def _states(value: Any) -> tuple[str, ...]:
    if isinstance(value, (list, tuple)):
        return tuple(str(item) for item in value)
    if isinstance(value, str):
        return tuple(part for part in re.split(r"[|,\s]+", value) if part)
    return ()


def _offsets(value: Any) -> tuple[int, ...]:
    if not isinstance(value, list):
        return ()
    offsets: list[int] = []
    for frame in value:
        if isinstance(frame, bool):
            continue
        if isinstance(frame, int):
            offsets.append(frame)
        elif isinstance(frame, (list, tuple)) and frame and isinstance(frame[-1], int):
            offsets.append(frame[-1])
        elif isinstance(frame, dict):
            number = _first(frame, ("imageOffset", "offset", "address"))
            if isinstance(number, int) and not isinstance(number, bool):
                offsets.append(number)
    return tuple(offsets)


def _wait_text(thread: dict[str, Any]) -> str:
    parts = [_text(thread[key]) for key in ("turnstileInfo", "waitInfo") if thread.get(key)]
    return " | ".join(part for part in parts if part)


def build_report(body: Any, header: dict[str, Any] | None = None, repaired: bool = False) -> Report:
    by_pid = _find_key(body, "processByPid")
    if not isinstance(by_pid, dict):
        raise ValueError("no stackshot.processByPid in the report")
    processes: dict[str, Process] = {}
    for pid, raw in by_pid.items():
        if not isinstance(raw, dict):
            continue
        name = str(_first(raw, NAME_KEYS, "?"))
        by_id = raw.get("threadById") if isinstance(raw.get("threadById"), dict) else {}
        threads = tuple(
            Thread(
                pid=str(pid), process=name, tid=str(tid),
                name=str(_first(entry, THREAD_NAME_KEYS, "")),
                states=_states(_first(entry, STATE_KEYS)),
                wait_event=_text(entry.get("waitEvent")),
                wait_text=_wait_text(entry),
                kernel_offsets=_offsets(_first(entry, FRAME_KEYS)),
            )
            for tid, entry in by_id.items() if isinstance(entry, dict))
        processes[str(pid)] = Process(str(pid), name, _text(_first(raw, PARENT_KEYS, "")), _text(_first(raw, RSS_KEYS, "?")), threads)
    return Report(header or {}, repaired, processes)


def parse_ips(text: str) -> Report:
    """An .ips file is a one-line JSON header followed by the JSON body (cut off at the end for a watchdog stackshot)."""
    head, _, rest = text.partition("\n")
    header: dict[str, Any] = {}
    body_text = text
    try:
        parsed = json.loads(head)
        if isinstance(parsed, dict) and rest.strip():
            header, body_text = parsed, rest
    except json.JSONDecodeError:
        pass
    body, repaired = repair_json(body_text)
    return build_report(body, header, repaired)


def ancestry(report: Report, pid: str) -> list[str]:
    names: list[str] = []
    seen: set[str] = set()
    current = pid
    while current and current in report.processes and current not in seen and len(names) < MAX_ANCESTRY:
        seen.add(current)
        process = report.processes[current]
        names.append(f"{process.name}[{process.pid}]")
        current = process.parent
    if current and current not in report.processes and current not in seen and current not in ("0", ""):
        names.append(f"?[{current}]")
    return names


def main_thread(report: Report, process_name: str = "WindowServer") -> Thread | None:
    threads = [thread for process in report.processes.values() if process.name == process_name for thread in process.threads]
    for thread in threads:
        if "ws_main_thread" in thread.name:
            return thread
    return threads[0] if threads else None


def wait_chain(report: Report, start: Thread) -> list[Thread]:
    """Follow the thread ids named in turnstileInfo/waitInfo until a thread names no known thread (or a loop)."""
    index = {thread.tid: thread for thread in report.threads()}
    chain = [start]
    seen = {start.tid}
    while len(chain) < MAX_CHAIN:
        current = chain[-1]
        next_thread = next((index[tid] for tid in re.findall(r"thread\s+(\d+)", current.wait_text)
                            if tid != current.tid and tid in index), None)
        if next_thread is None or next_thread.tid in seen:
            break
        chain.append(next_thread)
        seen.add(next_thread.tid)
    return chain


def _describe(thread: Thread, frames: int) -> str:
    offsets = ",".join(str(offset) for offset in thread.kernel_offsets[:frames]) or "?"
    return (f"thread {thread.tid} '{thread.name or '?'}' in {thread.process}[{thread.pid}] "
            f"state={'|'.join(thread.states) or '?'} waitEvent={thread.wait_event or '?'} kernel[{offsets}]")


def render(report: Report, frames: int = DEFAULT_FRAMES) -> str:
    out: list[str] = []
    header = report.header
    out.append("report: " + (", ".join(f"{key}={header[key]}" for key in ("bug_type", "os_version", "timestamp") if key in header) or "no header")
               + f"; {len(report.processes)} processes, {len(report.threads())} threads"
               + ("; JSON was cut off and repaired" if report.repaired else ""))
    start = main_thread(report)
    chain: list[Thread] = []
    out.append("")
    out.append("WindowServer main-thread wait chain:")
    if start is None:
        out.append("  no WindowServer process in the stackshot")
    else:
        chain = wait_chain(report, start)
        for hop, thread in enumerate(chain):
            out.append(f"  {hop}: {_describe(thread, frames)}")
            if thread.wait_text:
                out.append(f"     wait: {thread.wait_text}")
        last = chain[-1]
        out.append(f"  ends at thread {last.tid} in {last.process}[{last.pid}]"
                   + ("" if last.wait_text else " (it names no further thread)"))
    event = chain[-1].wait_event if chain else ""
    stuck = [thread for thread in report.threads() if thread.uninterruptible]
    out.append("")
    out.append(f"uninterruptible (TH_UNINT) threads: {len(stuck)}")
    for thread in sorted(stuck, key=lambda item: (item.wait_event, item.process, item.tid)):
        process = report.processes[thread.pid]
        shared = " [same wait event as chain end]" if event and thread.wait_event == event else ""
        out.append(f"  {_describe(thread, frames)}{shared}")
        out.append(f"     ancestry: {' <- '.join(ancestry(report, thread.pid))}; rss={process.rss}")
    signatures = {thread.kernel_offsets[:frames] for thread in stuck if thread.kernel_offsets}
    if stuck:
        out.append("")
        out.append(f"distinct kernel signatures (first {frames} frames) among them: {len(signatures)}")
    return "\n".join(out)


def read_report(path: Path) -> Report:
    return parse_ips(path.read_bytes().decode("utf-8", errors="replace"))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("reports", nargs="+", type=Path, help=".WindowServer-*.ips file(s); read only")
    parser.add_argument("--frames", type=int, default=DEFAULT_FRAMES, help="kernel frames to print per thread")
    args = parser.parse_args(argv)
    status = 0
    for path in args.reports:
        print(f"== {path.name}")
        try:
            print(render(read_report(path), max(1, args.frames)))
        except (OSError, ValueError) as error:
            print(f"cannot read: {error}", file=sys.stderr)
            status = 1
    return status


if __name__ == "__main__":
    raise SystemExit(main())
