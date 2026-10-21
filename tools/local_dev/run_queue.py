"""ARC-DEV-033: one FIFO launch queue for the Godot runs, exports and dotnet runs of every worktree on this Mac.

The rule is docs/workflow/toolchain.md#launch-queue-arc-dev-033. Every Godot run (window, headless, export) takes the
single Godot slot in arrival order; an export also runs quiet: once it is next for the slot, new dotnet runs wait and
running ones finish first. dotnet runs only register: they run side by side with each other and with Godot runs and
wait only for an export. There is no daemon. Each ticket is a file in the queue folder whose flock its wrapper (and,
for Godot kinds, the command it started) holds for its whole life, so the kernel frees the place of a run that crashed
or was killed; a waiter checks the queue every POLL_SECONDS, so the next run starts within that of the last one ending.
While lanes still use the retired `lockf /tmp/arcanum-godot-window.lock` rule, a Godot holder takes that lock too.
"""
from __future__ import annotations

import errno
import fcntl
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from typing import Any, Callable, Sequence

from . import config

SCHEMA = "arcanum-run-queue-v1"
KINDS = ("window", "headless", "export", "dotnet")
GODOT_KINDS = frozenset({"window", "headless", "export"})
# Minutes a run may hold its place; --timeout replaces it, 0 means no limit (the owner's own viewer launches).
DEFAULT_TIMEOUT_MINUTES = {"window": 30.0, "headless": 30.0, "export": 180.0, "dotnet": 45.0}
POLL_SECONDS = 0.25
KILL_GRACE_SECONDS = 10.0
TIMEOUT_EXIT = 124
FRONT, NORMAL = 0, 1  # ticket priority: FRONT only for the owner's ./arcanum area|world|map launches
LEGACY_WINDOW_LOCK = Path("/tmp/arcanum-godot-window.lock")
TICKET_ENV, KIND_ENV = "ARCANUM_QUEUE_TICKET", "ARCANUM_QUEUE_KIND"
MAX_LABEL, MAX_COMMAND = 120, 400


def folder() -> Path:
    return config.home() / "run-queue"


def legacy_lock() -> Path:
    override = os.environ.get("ARCANUM_QUEUE_LEGACY_LOCK")  # tests only: never touch the shared lock from a test
    return Path(override) if override else LEGACY_WINDOW_LOCK


def say(message: str) -> None:
    print(f"ARCANUM_QUEUE {message}", file=sys.stderr, flush=True)


def _open_folder() -> Path:
    path = folder()
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if path.is_symlink() or not path.is_dir():
        raise ValueError(f"the launch queue folder {path} must be a real folder")
    return path


class _QueueLock:
    """The short lock under which tickets are numbered, admitted and pruned."""

    def __enter__(self) -> "_QueueLock":
        self.fd = os.open(_open_folder() / "queue.lock", os.O_RDWR | os.O_CREAT | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
        fcntl.flock(self.fd, fcntl.LOCK_EX)
        return self

    def __exit__(self, *_: object) -> None:
        fcntl.flock(self.fd, fcntl.LOCK_UN)
        os.close(self.fd)


def _read_json(path: Path) -> dict[str, Any] | None:
    try:
        value = json.loads(path.read_text())
    except (OSError, ValueError):
        return None
    return value if isinstance(value, dict) and value.get("schema") == SCHEMA and isinstance(value.get("seq"), int) else None


_START_CACHE: dict[int, tuple[float, str | None]] = {}


def _process_start(pid: int) -> str | None:
    """The process's start time (`ps -o lstart`), cached for 5 s; None when it is gone."""
    cached = _START_CACHE.get(pid)
    if cached and time.monotonic() - cached[0] < 5:
        return cached[1]
    try:
        value = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True, timeout=5).stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        value = None
    _START_CACHE[pid] = (time.monotonic(), value)
    return value


def _alive(pid: Any, start: Any) -> bool:
    if not isinstance(pid, int) or pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        pass
    return start is None or _process_start(pid) == start


def _live(path: Path, ticket: dict[str, Any]) -> bool:
    """A ticket is live while its flock is held by its wrapper or its command. A lock held only by some descendant
    that outlived both (a build server that inherited the descriptor) does not keep a place."""
    try:
        fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW)
    except OSError:
        return False
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return False
    except OSError as error:
        if error.errno not in (errno.EWOULDBLOCK, errno.EAGAIN):
            raise
    finally:
        os.close(fd)
    return _alive(ticket.get("pid"), ticket.get("pidStart")) or _alive(ticket.get("childPid"), ticket.get("childStart"))


def tickets(prune: bool = True) -> list[tuple[Path, dict[str, Any]]]:
    """Live tickets in queue order (priority, then arrival). Call under the queue lock when deciding admission."""
    result = []
    for path in sorted(_open_folder().glob("t-*.json")):
        ticket = _read_json(path)
        if ticket is not None and _live(path, ticket):
            result.append((path, ticket))
        elif prune:
            try:
                path.unlink()
            except FileNotFoundError:
                pass
    result.sort(key=lambda item: (item[1].get("priority", NORMAL), item[1]["seq"]))
    return result


def _key(ticket: dict[str, Any]) -> tuple[int, int]:
    return ticket.get("priority", NORMAL), ticket["seq"]


def blocker(mine: dict[str, Any], live: Sequence[dict[str, Any]]) -> dict[str, Any] | None:
    """The ticket this one waits for, or None when it may start now (the admission rule)."""
    others = [ticket for ticket in live if ticket["seq"] != mine["seq"]]
    running = [ticket for ticket in others if ticket.get("startedAt")]
    ahead = [ticket for ticket in others if not ticket.get("startedAt") and _key(ticket) < _key(mine)]
    if mine["kind"] in GODOT_KINDS:
        for ticket in running + ahead:
            if ticket["kind"] in GODOT_KINDS:
                return ticket
        if mine["kind"] == "export":
            return next((ticket for ticket in running if ticket["kind"] == "dotnet"), None)
        return None
    # dotnet waits only for a running export or one that is draining (next for the Godot slot).
    for ticket in running:
        if ticket["kind"] == "export":
            return ticket
    for ticket in others:
        if ticket["kind"] == "export" and not ticket.get("startedAt"):
            waits_for = blocker(ticket, live)
            if waits_for is None or waits_for["kind"] == "dotnet":
                return ticket
    return None


def _describe(ticket: dict[str, Any]) -> str:
    return f"#{ticket['seq']} {ticket['kind']} {Path(ticket.get('cwd', '?')).name} \"{ticket.get('label', '')}\""


def _write(fd: int, ticket: dict[str, Any]) -> None:
    data = json.dumps(ticket, sort_keys=True).encode()
    os.lseek(fd, 0, os.SEEK_SET)
    os.ftruncate(fd, 0)
    os.write(fd, data)


def _history(entry: dict[str, Any]) -> None:
    fd = os.open(_open_folder() / "history.jsonl", os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
    try:
        os.write(fd, (json.dumps(entry, sort_keys=True) + "\n").encode())
    finally:
        os.close(fd)


def _enqueue(kind: str, label: str, command: Sequence[str], priority: int, timeout: float) -> tuple[int, Path, dict[str, Any]]:
    with _QueueLock():
        base = _open_folder()
        counter = base / "counter"
        try:
            seq = int(counter.read_text().strip() or "0") + 1
        except (FileNotFoundError, ValueError):
            seq = 1
        counter.write_text(f"{seq}\n")
        ticket = {"schema": SCHEMA, "seq": seq, "kind": kind, "label": label[:MAX_LABEL], "priority": priority,
                  "command": " ".join(command)[:MAX_COMMAND], "cwd": os.getcwd(), "pid": os.getpid(),
                  "pidStart": _process_start(os.getpid()), "timeoutMinutes": timeout, "enqueuedAt": time.time(),
                  "startedAt": None, "childPid": None, "childStart": None}
        hidden = base / f".new-{seq}-{os.getpid()}"
        fd = os.open(hidden, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        fcntl.flock(fd, fcntl.LOCK_EX)
        _write(fd, ticket)
        path = base / f"t-{seq:012d}.json"
        os.rename(hidden, path)  # visible only once it is locked and complete
    return fd, path, ticket


def _wait_turn(fd: int, ticket: dict[str, Any], sleep: Callable[[float], None]) -> None:
    last = None
    while True:
        with _QueueLock():
            live = [item for _, item in tickets()]
            waiting_for = blocker(ticket, live)
            if waiting_for is None:
                ticket["startedAt"] = time.time()
                _write(fd, ticket)
                return
        position = sum(1 for item in live if not item.get("startedAt") and _key(item) < _key(ticket))
        state = (waiting_for["seq"], position)
        if state != last:
            say(f"queued ticket={ticket['seq']} kind={ticket['kind']} ahead={position} waiting-for={_describe(waiting_for)}")
            last = state
        sleep(POLL_SECONDS)


def _legacy_hold() -> int:
    """Take the retired lockf lock so a lane still on the lockf rule cannot open a window over this run. Like
    lockf, retry when the file was replaced while waiting (lockf removes its file when its command ends)."""
    path = legacy_lock()
    announced = False
    while True:
        try:
            fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_CLOEXEC | os.O_NOFOLLOW, 0o666)
        except OSError as error:
            raise ValueError(f"cannot open the lockf lock {path}: {error.strerror}") from error
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            if not announced:
                say(f"waiting for a lane still on the lockf rule ({path})")
                announced = True
            fcntl.flock(fd, fcntl.LOCK_EX)
        try:
            same = os.path.samestat(os.fstat(fd), os.stat(path))
        except FileNotFoundError:
            same = False
        if same:
            return fd
        os.close(fd)


def nested() -> bool:
    """True inside a run that already holds its place (./arcanum run -- ./arcanum map ...): run directly, no new ticket."""
    value = os.environ.get(TICKET_ENV, "")
    if not value.isdigit():
        return False
    with _QueueLock():
        return any(item["seq"] == int(value) and item.get("startedAt") for _, item in tickets(prune=False))


def run(kind: str, command: Sequence[str], *, label: str = "", timeout: float | None = None, priority: int = NORMAL,
        hold: Callable[[], None] | None = None, sleep: Callable[[float], None] = time.sleep) -> int:
    """Wait for this run's turn, run the command, give the place to the next run. Returns the command's exit code,
    TIMEOUT_EXIT when it ran out of time, or 128+N when this wrapper was stopped by signal N. `hold`, called after the
    command succeeded, keeps the place while something the command left running is still open (the detached viewer)."""
    if kind not in KINDS:
        raise ValueError(f"kind must be one of {', '.join(KINDS)}")
    if not command:
        raise ValueError("give the command after --")
    minutes = DEFAULT_TIMEOUT_MINUTES[kind] if timeout is None else timeout
    if minutes < 0:
        raise ValueError("--timeout must be 0 (no limit) or a positive number of minutes")
    if nested():
        say(f"nested ticket={os.environ[TICKET_ENV]} (runs inside its place)")
        code = subprocess.call(list(command))
        if code == 0 and hold is not None:
            hold()
        return code
    fd, path, ticket = _enqueue(kind, label, command, priority, minutes)
    child: subprocess.Popen[bytes] | None = None
    legacy = None
    stopped: list[int] = []

    def stop(number: int, _frame: object) -> None:
        stopped.append(number)
        if child is not None and child.poll() is None:
            _signal_group(child.pid, signal.SIGTERM)
        raise KeyboardInterrupt

    previous = {number: signal.signal(number, stop) for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
    exit_code = 1
    try:
        _wait_turn(fd, ticket, sleep)
        if kind in GODOT_KINDS:
            legacy = _legacy_hold()
        waited = ticket["startedAt"] - ticket["enqueuedAt"]
        say(f"start ticket={ticket['seq']} kind={kind} waited={waited:.0f}s")
        environment = dict(os.environ, **{TICKET_ENV: str(ticket["seq"]), KIND_ENV: kind})
        # Godot kinds hand the ticket's lock to the command, so a run whose wrapper was killed keeps its place
        # while its command still runs; dotnet runs do not (their build servers would inherit it).
        child = subprocess.Popen(list(command), env=environment, start_new_session=True,
                                 pass_fds=(fd,) if kind in GODOT_KINDS else ())
        with _QueueLock():
            ticket["childPid"], ticket["childStart"] = child.pid, _process_start(child.pid)
            _write(fd, ticket)
        exit_code = _supervise(child, minutes)
        if exit_code == 0 and hold is not None:
            hold()
        if exit_code == TIMEOUT_EXIT:
            say(f"timeout ticket={ticket['seq']} after {minutes:g} min; stopped its process group")
    except KeyboardInterrupt:
        if child is not None and child.poll() is None:
            _stop_group(child)
        exit_code = 128 + (stopped[0] if stopped else signal.SIGINT)
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)
        ended = time.time()
        if ticket.get("startedAt"):
            ran = ended - ticket["startedAt"]
            say(f"end ticket={ticket['seq']} exit={exit_code} ran={ran:.0f}s")
        _history({"seq": ticket["seq"], "kind": kind, "label": ticket["label"], "cwd": ticket["cwd"],
                  "command": ticket["command"], "enqueuedAt": ticket["enqueuedAt"], "startedAt": ticket.get("startedAt"),
                  "endedAt": ended, "exit": exit_code})
        try:
            path.unlink()
        except FileNotFoundError:
            pass
        os.close(fd)
        if legacy is not None:
            os.close(legacy)
    return exit_code


def wait_unlocked(path: Path) -> None:
    """Block until nobody holds the flock on path (the ordinary launcher's lock, which the viewer holds while open)."""
    try:
        fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW)
    except FileNotFoundError:
        return
    try:
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            say("holding the place while the viewer is open; close it to let the next run start")
            fcntl.flock(fd, fcntl.LOCK_EX)
        fcntl.flock(fd, fcntl.LOCK_UN)
    finally:
        os.close(fd)


def _signal_group(pid: int, number: int) -> None:
    try:
        os.killpg(pid, number)
    except (ProcessLookupError, PermissionError):
        pass


def _stop_group(child: subprocess.Popen[bytes]) -> None:
    _signal_group(child.pid, signal.SIGTERM)
    try:
        child.wait(KILL_GRACE_SECONDS)
    except subprocess.TimeoutExpired:
        _signal_group(child.pid, signal.SIGKILL)
        child.wait()


def _supervise(child: subprocess.Popen[bytes], minutes: float) -> int:
    try:
        return child.wait(minutes * 60 if minutes > 0 else None)
    except subprocess.TimeoutExpired:
        _stop_group(child)
        return TIMEOUT_EXIT


def _age(seconds: float) -> str:
    seconds = max(0, int(seconds))
    return f"{seconds // 60}m{seconds % 60:02d}s"


def status(history: int = 0) -> list[str]:
    """Lines for ./arcanum queue: the runs holding a place, then the waiters in order; or the last finished runs."""
    if history:
        try:
            lines = (_open_folder() / "history.jsonl").read_text().splitlines()[-history:]
        except FileNotFoundError:
            return ["no finished runs yet"]
        result = []
        for line in lines:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            started, enq, end = entry.get("startedAt"), entry.get("enqueuedAt", 0), entry.get("endedAt", 0)
            waited = _age((started or end) - enq)
            ran = _age(end - started) if started else "-"
            stamp = time.strftime("%m-%d %H:%M", time.localtime(enq))
            result.append(f"{stamp} #{entry.get('seq')} {entry.get('kind')} waited={waited} ran={ran} exit={entry.get('exit')} "
                          f"{Path(entry.get('cwd', '?')).name} \"{entry.get('label', '')}\"")
        return result
    with _QueueLock():
        live = [item for _, item in tickets()]
    now = time.time()
    if not live:
        return ["queue empty"]
    result = []
    for ticket in live:
        if ticket.get("startedAt"):
            result.append(f"running {_describe(ticket)} for {_age(now - ticket['startedAt'])} pid={ticket.get('childPid')}")
    for ticket in live:
        if not ticket.get("startedAt"):
            waiting_for = blocker(ticket, live)
            result.append(f"waiting {_describe(ticket)} for {_age(now - ticket['enqueuedAt'])}"
                          + (f" behind #{waiting_for['seq']}" if waiting_for else " (starting)"))
    return result
