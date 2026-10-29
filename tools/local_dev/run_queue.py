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

from . import config, run_log, start_check

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
UNKNOWN = "?"  # ps could not answer: never read as "gone"
# The same start-time text in every lane, whatever the caller's locale or time zone.
_PS_ENVIRONMENT = {"LC_ALL": "C", "TZ": "UTC", "PATH": "/bin:/usr/bin"}


def _process_start(pid: int) -> str | None:
    """The process's start time (`ps -o lstart` in the C locale and UTC), cached for 5 s; None when ps says it is
    gone, UNKNOWN when ps failed."""
    cached = _START_CACHE.get(pid)
    if cached and time.monotonic() - cached[0] < 5:
        return cached[1]
    try:
        completed = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True, timeout=5,
                                   env=_PS_ENVIRONMENT)
    except (OSError, subprocess.SubprocessError):
        return UNKNOWN
    value = completed.stdout.strip() or (None if completed.returncode == 1 else UNKNOWN)
    if value is not UNKNOWN:
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
    if start is None or start == UNKNOWN:
        return True
    now = _process_start(pid)
    return now == UNKNOWN or now == start


def _locked(path: Path) -> bool:
    """True while someone holds the flock on path."""
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
        return True
    finally:
        os.close(fd)


def _live(path: Path, ticket: dict[str, Any]) -> bool:
    """A ticket is live while its flock is held by its wrapper or its command. A lock held only by some descendant
    that outlived both (a build server that inherited the descriptor) does not keep a place."""
    if not _locked(path):
        return False
    return _alive(ticket.get("pid"), ticket.get("pidStart")) or _alive(ticket.get("childPid"), ticket.get("childStart"))


def _expired(ticket: dict[str, Any], now: float) -> bool:
    """A run whose wrapper was killed has nobody to enforce its limit: once the limit and the grace have passed, a
    waiter stops its command's process group (SIGTERM, then SIGKILL after KILL_GRACE_SECONDS) and drops it."""
    minutes, started, child = ticket.get("timeoutMinutes"), ticket.get("startedAt"), ticket.get("childPid")
    if not started or not isinstance(minutes, (int, float)) or minutes <= 0 or not isinstance(child, int) or child <= 0:
        return False
    if _alive(ticket.get("pid"), ticket.get("pidStart")):
        return False
    late = now - (started + minutes * 60)
    if late < 0:
        return False
    if _alive(child, ticket.get("childStart")):
        _signal_group(child, signal.SIGKILL if late >= KILL_GRACE_SECONDS else signal.SIGTERM)
    return late >= KILL_GRACE_SECONDS


def _unlink(path: Path) -> None:
    try:
        path.unlink()
    except FileNotFoundError:
        pass


def tickets(prune: bool = True) -> list[tuple[Path, dict[str, Any]]]:
    """Live tickets in queue order (priority, then arrival). Call under the queue lock. A locked ticket this version
    cannot read (another version's format) is never removed and counts as a running export, which blocks everything."""
    result = []
    now = time.time()
    base = _open_folder()
    for path in sorted(base.glob("t-*.json")):
        ticket = _read_json(path)
        if ticket is None:
            if _locked(path):
                seq = path.stem[2:]
                result.append((path, {"schema": SCHEMA, "seq": int(seq) if seq.isascii() and seq.isdigit() else 0,
                                      "kind": "export", "label": "unreadable ticket", "startedAt": now, "enqueuedAt": now,
                                      "priority": FRONT}))
            elif prune:
                _unlink(path)
        elif _live(path, ticket) and not (prune and _expired(ticket, now)):
            result.append((path, ticket))
        elif prune and not (_locked(path) and _alive(ticket.get("pid"), ticket.get("pidStart"))):
            _unlink(path)
    if prune:
        for path in base.glob(".new-*"):
            if not _locked(path):
                _unlink(path)
        for pattern in (".counter-*", ".history-*"):  # left by a process interrupted mid-replace
            for path in base.glob(pattern):
                try:
                    if now - path.stat().st_mtime > 60:
                        _unlink(path)
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
    # Write over the old text first and cut it after: a ticket only grows (null becomes a number), so the file holds
    # complete JSON at every moment, even if this process is killed between the two calls.
    data = json.dumps(ticket, sort_keys=True).encode()
    os.pwrite(fd, data, 0)
    os.ftruncate(fd, len(data))


HISTORY_BYTES, HISTORY_KEEP = 2_000_000, 5000


def _history(entry: dict[str, Any]) -> None:
    path = _open_folder() / "history.jsonl"
    with _QueueLock():
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
        try:
            os.write(fd, (json.dumps(entry, sort_keys=True) + "\n").encode())
        finally:
            os.close(fd)
        if path.stat().st_size > HISTORY_BYTES:  # keep the newest runs only
            kept = path.read_text(errors="replace").splitlines()[-HISTORY_KEEP:]
            hidden = path.parent / f".history-{os.getpid()}"
            hidden.write_text("\n".join(kept) + "\n")
            os.chmod(hidden, 0o600)
            os.replace(hidden, path)


def _enqueue(kind: str, label: str, command: Sequence[str], priority: int, timeout: float) -> tuple[int, Path, dict[str, Any]]:
    with _QueueLock():
        base = _open_folder()
        counter = base / "counter"
        try:
            last = int(counter.read_text().strip() or "0")
        except (FileNotFoundError, ValueError):
            last = 0
        existing = [int(path.stem[2:]) for path in base.glob("t-*.json") if path.stem[2:].isascii() and path.stem[2:].isdigit()]
        seq = max([last, *existing]) + 1
        hidden_counter = base / f".counter-{os.getpid()}"
        hidden_counter.write_text(f"{seq}\n")
        os.replace(hidden_counter, counter)
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


# Which nested kinds a running parent's place already covers: an export covers everything, a Godot run covers Godot
# runs and dotnet runs (no export can drain while it holds the slot), a dotnet run covers dotnet runs.
COVERS = {"export": frozenset(KINDS), "window": frozenset({"window", "headless", "dotnet"}),
          "headless": frozenset({"window", "headless", "dotnet"}), "dotnet": frozenset({"dotnet"})}


def nested(kind: str) -> bool:
    """True inside a run whose place covers this kind (./arcanum run -- ./arcanum map ...): run directly, no new
    ticket. A kind the parent does not cover would wait for its own parent, so it is refused."""
    value = os.environ.get(TICKET_ENV, "")
    if not (value.isascii() and value.isdigit()):
        return False
    with _QueueLock():
        parent = next((item for _, item in tickets(prune=False) if item["seq"] == int(value) and item.get("startedAt")), None)
    if parent is None:
        return False
    if kind not in COVERS.get(parent["kind"], frozenset()):
        raise ValueError(f"a {kind} run inside a {parent['kind']} run (ticket {value}) would wait for its own parent; "
                         f"queue it on its own instead")
    return True


def pinned_sdk(environment: dict[str, str]) -> Path:
    """The pinned .NET SDK folder: DOTNET_ROOT, else ~/.local/share/dotnet (docs/workflow/toolchain.md)."""
    return Path(environment.get("DOTNET_ROOT") or Path.home() / ".local" / "share" / "dotnet")


def command_environment(kind: str, seq: int, base: dict[str, str] | None = None) -> dict[str, str]:
    """ARC-DEV-041: the environment of a queued command. Its ticket and kind (for nested runs); the pinned SDK first on
    PATH with DOTNET_ROOT set, so `dotnet` in any kind is the global.json SDK and never another one earlier on PATH
    (Homebrew's); PYTHONUNBUFFERED, so a Python command (the export endpoint, the validate tools) writes each line to the
    log as it prints it instead of when it exits. A missing pinned SDK leaves PATH as it is."""
    environment = dict(os.environ if base is None else base)
    environment.update({TICKET_ENV: str(seq), KIND_ENV: kind})
    environment.setdefault("PYTHONUNBUFFERED", "1")
    sdk = pinned_sdk(environment)
    if os.access(sdk / "dotnet", os.X_OK):
        rest = [item for item in environment.get("PATH", "").split(os.pathsep) if item and Path(item) != sdk]
        environment.update({"DOTNET_ROOT": str(sdk), "PATH": os.pathsep.join([str(sdk), *rest])})
    elif kind == "dotnet":
        say(f"note: no pinned SDK at {sdk / 'dotnet'}; `dotnet` comes from PATH")
    return environment


def run(kind: str, command: Sequence[str], *, label: str = "", timeout: float | None = None, priority: int = NORMAL,
        hold: Callable[[], None] | None = None, sleep: Callable[[float], None] = time.sleep, quiet: bool = False,
        start: start_check.StartCheck | None = None, log_sink: Callable[[Path], None] | None = None) -> int:
    """Wait for this run's turn, run the command, give the place to the next run. Returns the command's exit code,
    TIMEOUT_EXIT when it ran out of time, or 128+N when this wrapper was stopped by signal N. `hold`, called after the
    command succeeded, keeps the place while something the command left running is still open (the detached viewer).
    `quiet` (ARC-DEV-037) writes the command's output to a log file and prints only PASS or FAIL with the first error
    (run_log.summary) on stdout; a nested run ignores it, its output goes wherever its parent's goes. `start`
    (ARC-DEV-059) watches a quiet run's log for its start line and stops a run that did not start (FAILED_START_EXIT);
    a nested or --verbose run is not checked. `log_sink` is told a quiet run's log file (ARC-DEV-063: the smoke runner
    reads its stages' PASS lines there)."""
    if kind not in KINDS:
        raise ValueError(f"kind must be one of {', '.join(KINDS)}")
    if not command:
        raise ValueError("give the command after --")
    minutes = DEFAULT_TIMEOUT_MINUTES[kind] if timeout is None else timeout
    if minutes < 0:
        raise ValueError("--timeout must be 0 (no limit) or a positive number of minutes")
    if nested(kind):
        say(f"nested ticket={os.environ[TICKET_ENV]} (runs inside its place)")
        code = subprocess.call(list(command))
        if code == 0 and hold is not None:
            hold()
        return code
    fd, path, ticket = _enqueue(kind, label, command, priority, minutes)
    log = None
    reason: list[str] = []
    if quiet:
        try:
            log = run_log.open_log(ticket["seq"], kind)
        except BaseException:
            path.unlink(missing_ok=True)
            os.close(fd)
            raise
        say(f"log ticket={ticket['seq']} {log}")
        if log_sink is not None:
            log_sink(log)
    child: subprocess.Popen[bytes] | None = None
    legacy = None
    stopped: list[int] = []
    critical = [False]  # while starting or stopping the command a signal is only recorded, never raised

    def stop(number: int, _frame: object) -> None:
        stopped.append(number)
        if child is not None and child.poll() is None:
            _signal_group(child.pid, signal.SIGTERM)
        if not critical[0]:
            raise KeyboardInterrupt

    previous = {number: signal.signal(number, stop) for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
    exit_code = 1
    try:
        _wait_turn(fd, ticket, sleep)
        if kind in GODOT_KINDS:
            legacy = _legacy_hold()
        waited = ticket["startedAt"] - ticket["enqueuedAt"]
        say(f"start ticket={ticket['seq']} kind={kind} waited={waited:.0f}s")
        environment = command_environment(kind, ticket["seq"])
        # Godot kinds hand the ticket's lock to the command, so a run whose wrapper was killed keeps its place while
        # its command still runs (until its limit, see _expired); dotnet runs do not (their build servers would
        # inherit it). The lockf lock stays with the wrapper: a leftover descendant holding it would block lockf
        # lanes and the next Godot run with no way to tell it from a live run.
        critical[0] = True
        if log is None:
            child = subprocess.Popen(list(command), env=environment, start_new_session=True,
                                     pass_fds=(fd,) if kind in GODOT_KINDS else ())
        else:
            output = os.open(log, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            try:  # the child keeps its own copy of the descriptor
                child = subprocess.Popen(list(command), env=environment, start_new_session=True, stdin=subprocess.DEVNULL,
                                         stdout=output, stderr=subprocess.STDOUT,
                                         pass_fds=(fd,) if kind in GODOT_KINDS else ())
            finally:
                os.close(output)
        critical[0] = False
        if stopped:
            raise KeyboardInterrupt
        with _QueueLock():
            ticket["childPid"], ticket["childStart"] = child.pid, _process_start(child.pid)
            _write(fd, ticket)
        exit_code = _supervise(child, minutes, ticket["seq"], start if log is not None else None, log, reason, sleep)
        if exit_code == 0 and hold is not None:
            hold()
        if exit_code == TIMEOUT_EXIT:
            say(f"timeout ticket={ticket['seq']} after {minutes:g} min; stopped its process group")
    except KeyboardInterrupt:
        critical[0] = True
        reason.clear()  # a stopped wrapper's exit is the signal's, not a start check's
        if child is not None and child.poll() is None:
            _stop_group(child)
        exit_code = 128 + (stopped[0] if stopped else signal.SIGINT)
    finally:
        critical[0] = True
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
    if log is not None and ticket.get("startedAt"):
        lines = run_log.summary(log, exit_code, ended - ticket["startedAt"])
        if reason:  # ARC-DEV-059: why the start check failed the run, right under the FAIL line
            lines.insert(1, f"start check: {reason[0]}")
        print("\n".join(lines), flush=True)
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


def _supervise(child: subprocess.Popen[bytes], minutes: float, seq: int = 0, check: start_check.StartCheck | None = None,
               log: Path | None = None, reason: list[str] | None = None,
               sleep: Callable[[float], None] = time.sleep) -> int:
    began = time.monotonic()
    if check is not None and log is not None:
        code = _watch_start(child, minutes, seq, check, log, began, reason if reason is not None else [], sleep)
        if code is not None:
            return code
    try:
        if minutes <= 0:
            return child.wait()
        return child.wait(max(0.0, minutes * 60 - (time.monotonic() - began)))
    except subprocess.TimeoutExpired:
        _stop_group(child)
        return TIMEOUT_EXIT


def _watch_start(child: subprocess.Popen[bytes], minutes: float, seq: int, check: start_check.StartCheck, log: Path,
                 began: float, reason: list[str], sleep: Callable[[float], None]) -> int | None:
    """ARC-DEV-059: read the log as it fills until the start line. None once the command started or ended with 0
    (the caller collects its end); otherwise the exit code: the command's own when it ended non-zero before its start
    line, FAILED_START_EXIT when it was stopped, TIMEOUT_EXIT when its limit came first. A start line within the grace
    after a start failure line still counts as a start (the failure line was a harmless early line)."""
    limit = minutes * 60 if minutes > 0 else None
    offset, partial, failed_at, failure = 0, b"", None, ""
    while True:
        ended = child.poll() is not None  # before the read: everything an ended command printed is then in the file
        try:
            with open(log, "rb") as handle:
                handle.seek(offset)
                data = handle.read()
        except FileNotFoundError:
            data = b""
        offset += len(data)
        chunk = (partial + data).replace(b"\r", b"\n")  # a progress line rewritten with \r is a line too
        complete, _, partial = chunk.rpartition(b"\n")
        if ended or len(partial) > start_check.MAX_PARTIAL:  # the last line without \n, or one that never ends
            complete, partial = chunk, b""
        for line in complete.decode(errors="replace").splitlines():
            found = start_check.scan(check, [line])
            if found is not None and found[0] == "started":
                say(f"started ticket={seq} after={time.monotonic() - began:.0f}s line={start_check.cut(line)}")
                return None
            if found is not None and failed_at is None:
                failed_at, failure = time.monotonic(), start_check.cut(line)
                say(f"start-failed ticket={seq} line={failure}")
        now = time.monotonic()
        if ended:
            code = child.returncode
            if code == 0 and failed_at is None and not check.expected:
                say(f"note: ticket={seq} ended with 0 before a start line; its mode was not checked")
                return None
            reason.append(f"the command did not start: {failure}" if failed_at is not None else
                          f"the command ended before a start line ({check.describe()}); {start_check.REMEDY}")
            say(f"start-failed ticket={seq} {reason[0]}")
            return code if code != 0 else start_check.FAILED_START_EXIT
        if limit is not None and now - began >= limit:
            _stop_group(child)
            return TIMEOUT_EXIT
        if failed_at is not None and now - failed_at >= start_check.GRACE_SECONDS:
            say(f"start-failed ticket={seq} stopped its process group")
            _stop_group(child)
            reason.append(f"the command did not start: {failure}; stopped it")
            return start_check.FAILED_START_EXIT
        if failed_at is None and now - began >= check.seconds:
            say(f"start-failed ticket={seq} no start line within {check.seconds:g}s; stopped its process group")
            _stop_group(child)
            reason.append(f"no start line within {check.seconds:g}s ({check.describe()}); stopped it; "
                          f"{start_check.REMEDY}")
            return start_check.FAILED_START_EXIT
        sleep(POLL_SECONDS)


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
