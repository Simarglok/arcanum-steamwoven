"""ARC-DEV-033: one FIFO launch queue for the Godot runs, exports and dotnet runs of every worktree on this Mac.

The rule is docs/workflow/toolchain.md#launch-queue-arc-dev-033. Every Godot run (window, headless, export) takes the
single Godot slot in arrival order; an export also runs quiet: once it is next for the slot, new dotnet runs wait and
running ones finish first. dotnet runs share DOTNET_SLOTS places (ARC-DEV-300): they run side by side with Godot
runs, wait only for an export and for a free dotnet place, and leave no build servers behind (BUILD_ENVIRONMENT,
shutdown_build_servers). ARC-DEV-390: the Godot slot is no longer strictly first come first served: every run declares
how long it expects to take, and a run counts as having arrived ORDER_WEIGHT of its estimates later than it did, so a
12 s run is not queued behind a 10 minute run that arrived a minute before it, while no run is passed by runs that arrive
after that delay; ARC-DEV-400: the Godot slot is three lanes (one heavy lane for a run declared longer than HEAVY_SECONDS,
SHORT_LANES short lanes; an export runs alone, and ARCANUM_QUEUE_LANES=0 returns to one slot); and a `quick` run (a project import, a headless self-test: short Godot runs with no window) takes a
place of its own beside the Godot slot, as a dotnet run does. There is no daemon. Each ticket is a file in the queue folder whose flock its wrapper (and,
for Godot kinds, the command it started) holds for its whole life, so the kernel frees the place of a run that crashed
or was killed; a waiter checks the queue every POLL_SECONDS, so the next run starts within that of the last one ending.
While lanes still use the retired `lockf /tmp/arcanum-godot-window.lock` rule, a Godot holder takes that lock too.
"""
from __future__ import annotations

import ctypes
import errno
import fcntl
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from typing import Any, Callable, Sequence

from . import config, menubar, run_log, start_check

SCHEMA = "arcanum-run-queue-v1"
KINDS = ("window", "headless", "export", "dotnet", "quick")
GODOT_KINDS = frozenset({"window", "headless", "export"})
HOLD_KINDS = GODOT_KINDS | {"quick"}  # the kinds whose command is Godot and holds the ticket lock
SIDE_KINDS = frozenset({"dotnet", "quick"})  # the runs beside the Godot slot: they wait only for an export and a place of their kind
# Minutes a run may hold its place; --timeout replaces it, 0 means no limit (the owner's own viewer launches).
DEFAULT_TIMEOUT_MINUTES = {"window": 30.0, "headless": 30.0, "export": 180.0, "dotnet": 45.0, "quick": 30.0}
# ARC-DEV-390: `quick` runs (Godot without a window that ends by itself in seconds: the project import, the headless self-test)
# that may run at once; they wait only for an export, never for the Godot slot. The pinned Godot of each checkout is its
# own project folder, so two of them never write the same `.godot` folder.
QUICK_SLOTS = 2
# ARC-DEV-300: dotnet runs that may run at once. Every dotnet process of a run costs 50-950 MB; on 2026-11-08 about
# 150 orphaned MSBuild nodes and compiler servers of many lanes exhausted the Mac's memory and froze it.
DOTNET_SLOTS = 2
# ARC-DEV-390: the seconds a run is expected to take when its caller declares none (a smoke declares its own, see
# smoke.estimate), and how many estimates later than its arrival a run counts in the queue order.
DEFAULT_ESTIMATE_SECONDS = {"window": 60.0, "headless": 60.0, "export": 300.0, "dotnet": 60.0, "quick": 15.0}
ORDER_WEIGHT = 3.0
MAX_ESTIMATE_SECONDS = 86400.0  # a day: a longer declaration is capped, so no run puts itself last for ever
# ARC-DEV-300: no build server may outlive its run: no reused MSBuild worker nodes, no MSBuild server, no shared
# Roslyn compiler (VBCSCompiler, about 1 GB and shared by every worktree).
BUILD_ENVIRONMENT = {"MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "UseSharedCompilation": "false"}
SHUTDOWN_SECONDS = 60.0
SHUTDOWN_ENV = "ARCANUM_QUEUE_BUILD_SERVER_SHUTDOWN"  # "0" turns the shutdown off: tests only, never touch real build servers
POLL_SECONDS = 0.25
# ARC-DEV-400: the Godot slot as lanes. A window or headless run declared longer than HEAVY_SECONDS takes the one heavy
# lane, a shorter one a short lane (DEFAULT_SHORT_LANES of them); an export runs alone, and a run of the rollback
# (ARCANUM_QUEUE_LANES=0) or of a wrapper from before the lanes does too. Lanes never share a checkout's project folder.
LANES_ENV = "ARCANUM_QUEUE_LANES"  # the number of short lanes; 0 (or off, or anything unreadable) is the old single slot
DEFAULT_SHORT_LANES = 2
MAX_SHORT_LANES = 8
HEAVY_SECONDS = 90.0
HEAVY, SHORT, EXPORT, SINGLE = "heavy", "short", "export", "single"
EXCLUSIVE_LANES = frozenset({EXPORT, SINGLE})
# ARC-DEV-400: a run that ended with one of these was stopped on purpose (SIGHUP, SIGINT, SIGTERM of its wrapper): not red.
STOPPED_EXITS = frozenset({128 + signal.SIGHUP, 128 + signal.SIGINT, 128 + signal.SIGTERM})
KILL_GRACE_SECONDS = 10.0
TIMEOUT_EXIT = 124
FRONT, NORMAL = 0, 1  # ticket priority: FRONT for the owner's ./arcanum area|world|map launches and ./arcanum package (ARC-DEV-400)
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


# ARC-DEV-1344: the pause is a ticket nobody can read. A locked ticket file a wrapper cannot read (another version's format)
# counts as a running export, which blocks everything (tickets() below, and the same rule since ARC-DEV-033), so the pause
# is one such file held by a small `lockf` process: every wrapper of every checkout, old ones too, waits behind it, and the
# runs already running finish. Resume removes the file; the holder notices within a second and ends, and a crash of the
# holder (or a reboot) frees the lock, so a pause never outlives the machine's processes.
PAUSE_NAME = "t-pause.json"
PAUSE_SCHEMA = "arcanum-run-queue-pause-v1"  # not SCHEMA: that is what makes the ticket unreadable to the queue
LOCKF = "/usr/bin/lockf"
HOLDER_SCRIPT = 'while [ -e "$0" ] || [ -e "$1" ]; do sleep 1; done'  # $0 the file being made, $1 its final name (a rename keeps one)


def pause_file() -> Path:
    return folder() / PAUSE_NAME


def holder_command(hidden: Path, final: Path) -> list[str]:
    return [LOCKF, "-k", str(hidden), "/bin/sh", "-c", HOLDER_SCRIPT, str(hidden), str(final)]


def pause_state() -> dict[str, Any] | None:
    """The pause ticket's content ({"since": epoch seconds, "by": who}) while the queue is paused, else None. A pause
    file nobody holds is stale (the queue removes it like any dead ticket), so it does not pause."""
    path = pause_file()
    if not _locked(path):
        return None
    try:
        value = json.loads(path.read_text())
    except (OSError, ValueError):
        return {}
    return value if isinstance(value, dict) else {}


def set_paused(on: bool, by: str = "cli") -> bool:
    """Pause or resume; True when the state changed. While paused no waiting run starts (running runs finish, nested runs
    are not queued): a waiter checks the queue every POLL_SECONDS, so a resume starts the next run within that. Raises
    OSError when the holder cannot be started."""
    base = _open_folder()
    path = base / PAUSE_NAME
    if not on:
        was = pause_state() is not None
        path.unlink(missing_ok=True)  # also a stale file nobody holds
        return was
    if pause_state() is not None:
        return False
    hidden = base / f".pause-{os.getpid()}"  # visible to the queue only once it is locked, like any ticket
    hidden.write_text(json.dumps({"schema": PAUSE_SCHEMA, "since": time.time(), "by": by}))
    os.chmod(hidden, 0o600)
    holder = subprocess.Popen(holder_command(hidden, path), start_new_session=True, stdin=subprocess.DEVNULL,
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, close_fds=True)
    try:
        deadline = time.monotonic() + 5.0
        while not _locked(hidden):
            if holder.poll() is not None or time.monotonic() > deadline:
                raise OSError("the pause could not be held (is /usr/bin/lockf there?)")
            time.sleep(0.02)
        os.replace(hidden, path)
    except BaseException:  # never leave a holder looping on a file nobody will name (it ends when the file is gone)
        hidden.unlink(missing_ok=True)
        if holder.poll() is None:
            holder.kill()
            holder.wait()
        raise
    holder.returncode = 0  # it outlives this process on purpose: no "still running" warning when the Popen object goes
    return True


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
UNKNOWN = "?"  # the start time could not be read: never read as "gone"
# The same start-time text in every lane, whatever the caller's locale or time zone.
_PS_ENVIRONMENT = {"LC_ALL": "C", "TZ": "UTC", "PATH": "/bin:/usr/bin"}
PS_CACHE_SECONDS = 30.0  # the spawning fallback is cached longer: a start time never changes while the pid lives
PROC_PIDTBSDINFO = 3  # proc_pidinfo flavor of struct proc_bsdinfo (sys/proc_info.h)


class _ProcBsdInfo(ctypes.Structure):
    """struct proc_bsdinfo of macOS (sys/proc_info.h, 136 bytes); only the start time is read."""
    _fields_ = [("pbi_flags", ctypes.c_uint32), ("pbi_status", ctypes.c_uint32), ("pbi_xstatus", ctypes.c_uint32),
                ("pbi_pid", ctypes.c_uint32), ("pbi_ppid", ctypes.c_uint32), ("pbi_uid", ctypes.c_uint32),
                ("pbi_gid", ctypes.c_uint32), ("pbi_ruid", ctypes.c_uint32), ("pbi_rgid", ctypes.c_uint32),
                ("pbi_svuid", ctypes.c_uint32), ("pbi_svgid", ctypes.c_uint32), ("rfu_1", ctypes.c_uint32),
                ("pbi_comm", ctypes.c_char * 16), ("pbi_name", ctypes.c_char * 32), ("pbi_nfiles", ctypes.c_uint32),
                ("pbi_pgid", ctypes.c_uint32), ("pbi_pjobc", ctypes.c_uint32), ("e_tdev", ctypes.c_uint32),
                ("e_tpgid", ctypes.c_uint32), ("pbi_nice", ctypes.c_int32), ("pbi_start_tvsec", ctypes.c_uint64),
                ("pbi_start_tvusec", ctypes.c_uint64)]


def _load_libproc() -> Any:
    """The macOS libproc with proc_pidinfo typed, or None where it is not there (the ps fallback then answers)."""
    if sys.platform != "darwin" or ctypes.sizeof(_ProcBsdInfo) != 136:
        return None
    try:
        lib = ctypes.CDLL("/usr/lib/libproc.dylib", use_errno=True)
        lib.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
        lib.proc_pidinfo.restype = ctypes.c_int
    except (OSError, AttributeError):
        return None
    return lib


_LIBPROC = _load_libproc()


_DAYS = ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")
_MONTHS = ("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")


def _start_text(seconds: int) -> str:
    """The text `ps -o lstart=` prints in the C locale and UTC (`Thu Nov 12 17:07:19 2026`, day padded by a space)."""
    moment = time.gmtime(seconds)  # fixed English names: strftime would follow a locale the importing process set
    return (f"{_DAYS[moment.tm_wday]} {_MONTHS[moment.tm_mon - 1]} {moment.tm_mday:2d} "
            f"{moment.tm_hour:02d}:{moment.tm_min:02d}:{moment.tm_sec:02d} {moment.tm_year}")


def _process_start_libproc(pid: int) -> str | None:
    """The start time through proc_pidinfo, no process spawned: None when the pid is gone, UNKNOWN when the kernel
    would not tell (another user's process)."""
    info = _ProcBsdInfo()
    ctypes.set_errno(0)
    size = _LIBPROC.proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, ctypes.byref(info), ctypes.sizeof(info))
    if size == ctypes.sizeof(info) and info.pbi_pid == pid and info.pbi_start_tvsec:
        return _start_text(int(info.pbi_start_tvsec))
    return None if ctypes.get_errno() == errno.ESRCH else UNKNOWN


def _process_start(pid: int) -> str | None:
    """The process's start time as `ps -o lstart` prints it in the C locale and UTC; None when the process is gone,
    UNKNOWN when it could not be read. ARC-DEV-1401: read through libproc, so the queue spawns no process; only where
    libproc is missing does `ps` answer, cached for PS_CACHE_SECONDS."""
    if _LIBPROC is not None:
        return _process_start_libproc(pid)
    cached = _START_CACHE.get(pid)
    if cached and time.monotonic() - cached[0] < PS_CACHE_SECONDS:
        return cached[1]
    try:
        completed = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True, timeout=5,
                                   env=_PS_ENVIRONMENT)
    except (OSError, subprocess.SubprocessError):
        return UNKNOWN
    value = completed.stdout.strip() or (None if completed.returncode == 1 else UNKNOWN)
    if value is not UNKNOWN:
        if len(_START_CACHE) > 256:
            _START_CACHE.clear()
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
                                      "priority": FRONT, **({"paused": True} if path.name == PAUSE_NAME else {})}))
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
        for pattern in (".counter-*", ".history-*", ".settings-*"):  # left by a process interrupted mid-replace
            for path in base.glob(pattern):
                try:
                    if now - path.stat().st_mtime > 60:
                        _unlink(path)
                except FileNotFoundError:
                    pass
    order = order_of([item for _, item in result])
    result.sort(key=lambda item: order(item[1]))
    return result


def estimate_of(ticket: dict[str, Any]) -> float:
    """The seconds a ticket declared (a ticket of an older wrapper declared none: its kind's default)."""
    value = ticket.get("estimateSeconds")
    if isinstance(value, (int, float)) and not isinstance(value, bool) and value >= 0:
        return float(value)
    return DEFAULT_ESTIMATE_SECONDS.get(ticket.get("kind", ""), DEFAULT_ESTIMATE_SECONDS["window"])


def checkout_of(ticket: dict[str, Any]) -> str | None:
    """The checkout a run belongs to (the folder holding `.git` above the folder it was queued from, else that folder):
    two runs of one checkout share its Godot project folder and its `.godot` import. A ticket of an older wrapper has none."""
    value = ticket.get("checkout")
    return value if isinstance(value, str) and value else ticket.get("cwd")


def _find_checkout(path: str) -> str:
    here = Path(path)
    for folder_ in (here, *here.parents):
        if (folder_ / ".git").exists():
            return str(folder_)
    return path


def _key(ticket: dict[str, Any]) -> tuple[int, float, int]:
    """The queue order (ARC-DEV-390): the owner's launches first, then by the arrival time plus ORDER_WEIGHT of the run's
    estimates, then by number. The key never changes while the ticket waits, so every waiter computes the same order;
    a run is passed only by runs whose own delayed arrival is earlier than its, so it is never passed by a run that
    arrives more than ORDER_WEIGHT x its estimate after it, and none waits for ever."""
    return (ticket.get("priority", NORMAL), ticket.get("enqueuedAt", 0.0) + ORDER_WEIGHT * estimate_of(ticket),
            ticket["seq"])


def _arrival_key(ticket: dict[str, Any]) -> tuple[int, float, int]:
    """The order of a wrapper from before ARC-DEV-390: by number only (the middle part is constant)."""
    return ticket.get("priority", NORMAL), 0.0, ticket["seq"]


def order_of(live: Sequence[dict[str, Any]]) -> Callable[[dict[str, Any]], tuple[int, float, int]]:
    """The key function for the waiting runs among `live`. A waiting ticket with no `estimateSeconds` comes from a wrapper
    of the old code (another checkout that has not merged ARC-DEV-390 yet), which waits for every smaller number; ordering
    by length while one is queued could make a new and an old waiter wait for each other for ever (new A passed by a
    shorter B, old B behind the smaller number A), so while any such ticket waits everyone orders by number, as before."""
    if any(not ticket.get("startedAt") and "estimateSeconds" not in ticket for ticket in live):
        return _arrival_key
    return _key


_LANE_NOTE = [False]
SETTINGS_FILE = "settings.json"  # ARC-DEV-1353: the owner's caps, in the queue folder: {"shortLanes": 0..8, "maxLoad": number}


def read_settings() -> dict[str, Any]:
    """The owner's caps from the queue folder (./arcanum queue --set-lanes / --set-max-load); {} when none or unreadable.
    Read only: it never creates the folder. Every wrapper reads it, so one setting caps every thread's runs."""
    try:
        value = json.loads((folder() / SETTINGS_FILE).read_text())
    except (OSError, ValueError):
        return {}
    return value if isinstance(value, dict) else {}


def write_settings(changes: dict[str, Any]) -> dict[str, Any]:
    """Merge the changes into the settings file (a value of None removes the key); the write is atomic."""
    with _QueueLock():
        settings = read_settings()
        for key, value in changes.items():
            if value is None:
                settings.pop(key, None)
            else:
                settings[key] = value
        path = _open_folder() / SETTINGS_FILE
        temp = path.parent / f".settings-{os.getpid()}"
        temp.write_text(json.dumps(settings, sort_keys=True))
        os.replace(temp, path)
    return settings


def _whole_lanes(value: Any) -> int | None:
    if isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= MAX_SHORT_LANES:
        return value
    return None


def lanes_setting(environment: dict[str, str] | None = None) -> tuple[int, str]:
    """The number of short lanes and where it comes from: LANES_ENV (this wrapper's environment), else the owner's file,
    else DEFAULT_SHORT_LANES. 0 is the rollback (one Godot slot); an unreadable variable is read as the rollback, with a
    note once. The file is read only for the real environment (environment=None): a given mapping is a test's."""
    value = (os.environ if environment is None else environment).get(LANES_ENV)
    if value is None or not value.strip():
        stored = _whole_lanes(read_settings().get("shortLanes")) if environment is None else None
        return (DEFAULT_SHORT_LANES, "default") if stored is None else (stored, "settings file")
    text = value.strip().lower()
    if text == "off":
        return 0, "environment"
    if text.isascii() and text.isdigit() and int(text) <= MAX_SHORT_LANES:
        return int(text), "environment"
    if not _LANE_NOTE[0]:
        _LANE_NOTE[0] = True
        say(f"note: {LANES_ENV}={value!r} is not a number from 0 to {MAX_SHORT_LANES}; using one Godot slot")
    return 0, "environment"


def short_lanes(environment: dict[str, str] | None = None) -> int:
    """The number of short Godot lanes (see lanes_setting)."""
    return lanes_setting(environment)[0]


def max_load() -> float | None:
    """The owner's load guard (a one-minute load average), None when none is set."""
    value = read_settings().get("maxLoad")
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        try:
            limit = float(value)
        except OverflowError:  # a hand-edited, absurdly large integer
            return None
        return limit if 0 < limit < math.inf else None
    return None


def too_loaded(peers: int, load: float | None = None) -> float | None:
    """The limit a run that is next for its place must wait for: the load guard, when another Godot run is already running
    and the one-minute load is above it. A lone run (no peer) always starts, so nothing waits for ever."""
    limit = max_load()
    if limit is None or peers <= 0:
        return None
    current = _load() if load is None else load
    return limit if current is not None and current > limit else None


def lane_for(kind: str, estimate: float, lanes: int | None = None) -> str | None:
    """The lane a new run takes: None for a run beside the Godot slot (`dotnet`, `quick`)."""
    if kind not in GODOT_KINDS:
        return None
    if kind == "export":
        return EXPORT
    if (short_lanes() if lanes is None else lanes) <= 0:
        return SINGLE
    return HEAVY if estimate > HEAVY_SECONDS else SHORT


def lane_of(ticket: dict[str, Any]) -> str | None:
    """The lane of a ticket. A ticket with no lane comes from a wrapper from before ARC-DEV-400, which counts on running
    alone: it takes the whole Godot slot (an export always does)."""
    if ticket.get("kind") not in GODOT_KINDS:
        return None
    lane = ticket.get("lane")
    if ticket["kind"] == "export":
        return EXPORT
    return lane if lane in (HEAVY, SHORT, SINGLE) else SINGLE


def blocker(mine: dict[str, Any], live: Sequence[dict[str, Any]]) -> dict[str, Any] | None:
    """The ticket this one waits for, or None when it may start now (the admission rule)."""
    others = [ticket for ticket in live if ticket["seq"] != mine["seq"]]
    running = [ticket for ticket in others if ticket.get("startedAt")]
    order = order_of(live)
    ahead = [ticket for ticket in others if not ticket.get("startedAt") and order(ticket) < order(mine)]
    mine_checkout = checkout_of(mine)
    if mine["kind"] in GODOT_KINDS:
        # a quick run of the same checkout (its project folder) goes before a Godot run that is later in the order, and
        # while it runs holds that checkout: a window run there would open the project in the middle of its import
        for ticket in running + ahead:
            if ticket["kind"] == "quick" and mine_checkout is not None and checkout_of(ticket) == mine_checkout:
                return ticket
        godot = [ticket for ticket in running + ahead if ticket["kind"] in GODOT_KINDS]
        lane = lane_of(mine)
        if lane in EXCLUSIVE_LANES:
            # an export (or a run of the rollback, or of an old wrapper) runs alone: it waits for every Godot run of every
            # lane that is running or earlier in the order
            if godot:
                return godot[0]
            if mine["kind"] == "export":
                return next((ticket for ticket in running if ticket["kind"] in SIDE_KINDS), None)
            return None
        # a lane run waits for an exclusive run that is running or earlier in the order (an export is passed only by runs
        # that arrive well before it), and for a run of its own checkout (a project folder is opened by one run at a time)
        for ticket in godot:
            if lane_of(ticket) in EXCLUSIVE_LANES:
                return ticket
        for ticket in godot:
            if mine_checkout is not None and checkout_of(ticket) == mine_checkout:
                return ticket
        # then for a free place of its lane: the running runs of the lane and those of it ahead fill its places; a run
        # ahead that waits only for the checkout of another run of the lane fills none
        holders, busy = [], set()
        for ticket in godot:  # the running runs first, then the runs ahead in order, of every lane
            checkout = checkout_of(ticket)
            held = not ticket.get("startedAt") and checkout is not None and checkout in busy
            if lane_of(ticket) == lane and not held:
                holders.append(ticket)
            if checkout is not None:
                busy.add(checkout)
        slots = 1 if lane == HEAVY else max(1, short_lanes())
        if len(holders) >= slots:
            return holders[len(holders) - slots]
        return None
    # dotnet and quick runs wait for a running export or one that is draining (next for the Godot slot), then for a free
    # place of their own kind: the running runs of the kind and those of it ahead fill its slots (ARC-DEV-300, 390).
    for ticket in running:
        if ticket["kind"] == "export":
            return ticket
    for ticket in others:
        if ticket["kind"] == "export" and not ticket.get("startedAt"):
            waits_for = blocker(ticket, live)
            # draining: the export waits for nothing, or only for a side run that is already running (a quick run of its
            # own checkout that is still queued is not that: it would wait for the export in turn)
            if waits_for is None or (waits_for["kind"] in SIDE_KINDS and waits_for.get("startedAt")):
                return ticket
    if mine["kind"] == "quick" and mine_checkout is not None:
        # the same project folder is never imported or run twice at once: wait for a Godot or quick run of this checkout
        for ticket in running + ahead:
            if ticket["kind"] in GODOT_KINDS | {"quick"} and checkout_of(ticket) == mine_checkout:
                return ticket
    slots = DOTNET_SLOTS if mine["kind"] == "dotnet" else QUICK_SLOTS
    holders = [ticket for ticket in running + ahead if ticket["kind"] == mine["kind"]]
    if mine["kind"] == "quick":
        # a quick run held back by a run of its own checkout fills no place (it does not start before that run ends): it
        # must not keep another checkout's import waiting behind a long window run
        busy = {checkout_of(ticket) for ticket in running if ticket["kind"] in GODOT_KINDS | {"quick"}} - {None}
        holders = [ticket for ticket in holders if ticket.get("startedAt") or checkout_of(ticket) not in busy]
    if len(holders) >= slots:
        return holders[len(holders) - slots]  # the one whose end frees the place this run takes
    return None


def _describe(ticket: dict[str, Any]) -> str:
    if ticket.get("paused"):
        return "queue PAUSED (./arcanum queue --resume)"
    lane = lane_of(ticket)
    return f"#{ticket['seq']} {ticket['kind']}{'/' + lane if lane else ''} {Path(ticket.get('cwd', '?')).name} \"{ticket.get('label', '')}\""


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


def _enqueue(kind: str, label: str, command: Sequence[str], priority: int, timeout: float,
             estimate: float) -> tuple[int, Path, dict[str, Any]]:
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
                  "command": " ".join(command)[:MAX_COMMAND], "cwd": os.getcwd(), "checkout": _find_checkout(os.getcwd()),
                  "pid": os.getpid(),
                  "pidStart": _process_start(os.getpid()), "timeoutMinutes": timeout, "estimateSeconds": estimate,
                  "enqueuedAt": time.time(),
                  "startedAt": None, "childPid": None, "childStart": None}
        lane = lane_for(kind, estimate)
        if lane is not None:
            ticket["lane"] = lane  # ARC-DEV-400: what the other wrappers read; a ticket with none is a single-slot run
        hidden = base / f".new-{seq}-{os.getpid()}"
        fd = os.open(hidden, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        fcntl.flock(fd, fcntl.LOCK_EX)
        _write(fd, ticket)
        path = base / f"t-{seq:012d}.json"
        os.rename(hidden, path)  # visible only once it is locked and complete
    return fd, path, ticket


def _peers(live: Sequence[dict[str, Any]], own_seq: int) -> int:
    """The other runs of Godot (any lane, quick runs too) that are running now."""
    return sum(1 for item in live if item["seq"] != own_seq and item.get("startedAt") and item["kind"] in HOLD_KINDS
               and not item.get("paused"))


def _load() -> float | None:
    """The one-minute load average, None when the system gives none."""
    try:
        return round(os.getloadavg()[0], 1)
    except (OSError, AttributeError):
        return None


def _peers_now(own_seq: int) -> int | None:
    try:
        with _QueueLock():
            return _peers([item for _, item in tickets(prune=False)], own_seq)
    except (OSError, ValueError):
        return None


def _wait_turn(fd: int, ticket: dict[str, Any], sleep: Callable[[float], None]) -> None:
    last = None
    while True:
        with _QueueLock():
            live = [item for _, item in tickets()]
            waiting_for = blocker(ticket, live)
            # the guard holds the runs with a window or headless (GODOT_KINDS) behind running ones of those kinds only: a
            # `quick` run (an import, a self-test) never waits for a long viewer window because of the load
            limit = None
            if waiting_for is None and ticket["kind"] in GODOT_KINDS:
                limit = too_loaded(sum(1 for item in live if item["seq"] != ticket["seq"] and item.get("startedAt")
                                       and item["kind"] in GODOT_KINDS))
            if waiting_for is None and limit is None:
                ticket["startedAt"] = time.time()
                if ticket["kind"] in HOLD_KINDS:  # ARC-DEV-400: the conditions it starts under, for the lane accounting
                    ticket["peers"] = _peers(live, ticket["seq"])
                    ticket["loadStart"] = _load()
                _write(fd, ticket)
                return
        order = order_of(live)
        position = sum(1 for item in live if not item.get("startedAt") and order(item) < order(ticket))
        state = (waiting_for["seq"] if waiting_for else ("load", limit), position)
        if state != last:
            if waiting_for is None:  # ARC-DEV-1353: its turn has come, but the machine is busier than the owner's guard
                say(f"queued ticket={ticket['seq']} kind={ticket['kind']} ahead={position} waiting-for=load below {limit:g} "
                    f"(now {_load()}; ./arcanum queue --set-max-load off removes the guard)")
            else:
                say(f"queued ticket={ticket['seq']} kind={ticket['kind']} ahead={position} waiting-for={_describe(waiting_for)}")
            last = state
        sleep(POLL_SECONDS)


def _legacy_hold() -> int:
    """Take the retired lockf lock so a lane still on the lockf rule cannot open a window over this run. Like
    lockf, retry when the file was replaced while waiting (lockf removes its file when its command ends). Shared
    (ARC-DEV-400): the runs of the queue's own Godot lanes hold it side by side, a lockf user (exclusive) still waits for
    all of them and they wait for it."""
    path = legacy_lock()
    announced = False
    while True:
        try:
            fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_CLOEXEC | os.O_NOFOLLOW, 0o666)
        except OSError as error:
            raise ValueError(f"cannot open the lockf lock {path}: {error.strerror}") from error
        try:
            fcntl.flock(fd, fcntl.LOCK_SH | fcntl.LOCK_NB)
        except OSError:
            if not announced:
                say(f"waiting for a lane still on the lockf rule ({path})")
                announced = True
            fcntl.flock(fd, fcntl.LOCK_SH)
        try:
            same = os.path.samestat(os.fstat(fd), os.stat(path))
        except FileNotFoundError:
            same = False
        if same:
            return fd
        os.close(fd)


# Which nested kinds a running parent's place already covers: an export covers everything, a Godot run covers Godot
# runs, quick runs and dotnet runs (no export can drain while it holds the slot), a quick run covers quick runs, a dotnet
# run covers dotnet runs.
COVERS = {"export": frozenset(KINDS), "window": frozenset({"window", "headless", "dotnet", "quick"}),
          "headless": frozenset({"window", "headless", "dotnet", "quick"}), "dotnet": frozenset({"dotnet"}),
          "quick": frozenset({"quick"})}


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
    environment.update(BUILD_ENVIRONMENT)  # ARC-DEV-300: every kind, a Godot run builds its C# project too
    environment.setdefault("PYTHONUNBUFFERED", "1")
    sdk = pinned_sdk(environment)
    if os.access(sdk / "dotnet", os.X_OK):
        rest = [item for item in environment.get("PATH", "").split(os.pathsep) if item and Path(item) != sdk]
        environment.update({"DOTNET_ROOT": str(sdk), "PATH": os.pathsep.join([str(sdk), *rest])})
    elif kind == "dotnet":
        say(f"note: no pinned SDK at {sdk / 'dotnet'}; `dotnet` comes from PATH")
    return environment


def shutdown_build_servers(environment: dict[str, str], live: Sequence[dict[str, Any]], own_seq: int,
                           call: Callable[..., Any] = subprocess.run) -> str | None:
    """ARC-DEV-300: `dotnet build-server shutdown` with the pinned SDK of `environment` (never the first `dotnet` on the
    owner's PATH, which may be Homebrew's other SDK and fails against global.json), run from this repository's folder.
    It runs only when no other dotnet run holds a place, so it never stops the servers of a run in progress. Returns a
    note for the log when it did not succeed or was skipped for a missing SDK, else None. Never raises."""
    if environment.get(SHUTDOWN_ENV) == "0" or any(ticket["kind"] == "dotnet" and ticket["seq"] != own_seq for ticket in live):
        return None
    sdk = pinned_sdk(environment)
    dotnet = sdk / "dotnet"
    if not os.access(dotnet, os.X_OK):
        return f"build servers not shut down: no pinned SDK at {dotnet}"
    try:
        result = call([str(dotnet), "build-server", "shutdown"], env=environment, cwd=Path(__file__).resolve().parents[2],
                      stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True,
                      timeout=SHUTDOWN_SECONDS)
    except (OSError, subprocess.SubprocessError) as error:
        return f"build servers not shut down: {error}"
    if result.returncode != 0:
        first = (result.stderr or "").strip().splitlines()[:1]
        return f"build servers not shut down: exit {result.returncode} {first[0] if first else ''}".rstrip()
    return None


def run(kind: str, command: Sequence[str], *, label: str = "", timeout: float | None = None, priority: int = NORMAL,
        hold: Callable[[], None] | None = None, sleep: Callable[[float], None] = time.sleep, quiet: bool = False,
        start: start_check.StartCheck | None = None, log_sink: Callable[[Path], None] | None = None,
        estimate: float | None = None) -> int:
    """Wait for this run's turn, run the command, give the place to the next run. Returns the command's exit code,
    TIMEOUT_EXIT when it ran out of time, or 128+N when this wrapper was stopped by signal N. `hold`, called after the
    command succeeded, keeps the place while something the command left running is still open (the detached viewer).
    `quiet` (ARC-DEV-037) writes the command's output to a log file and prints only PASS or FAIL with the first error
    (run_log.summary) on stdout; a nested run ignores it, its output goes wherever its parent's goes. `start`
    (ARC-DEV-059) watches a quiet run's log for its start line and stops a run that did not start (FAILED_START_EXIT);
    a nested or --verbose run is not checked. `log_sink` is told a quiet run's log file (ARC-DEV-063: the smoke runner
    reads its stages' PASS lines there). `estimate` (ARC-DEV-390) is the seconds the command is expected to take, for the
    queue order (the kind's default when None): a short run is not queued behind a long one that arrived a little
    before it."""
    if kind not in KINDS:
        raise ValueError(f"kind must be one of {', '.join(KINDS)}")
    if not command:
        raise ValueError("give the command after --")
    minutes = DEFAULT_TIMEOUT_MINUTES[kind] if timeout is None else timeout
    if minutes < 0:
        raise ValueError("--timeout must be 0 (no limit) or a positive number of minutes")
    seconds = DEFAULT_ESTIMATE_SECONDS[kind] if estimate is None else estimate
    if not math.isfinite(seconds) or seconds < 0:
        raise ValueError("--estimate must be a finite number of seconds, 0 or more")
    seconds = min(seconds, MAX_ESTIMATE_SECONDS)
    if nested(kind):
        say(f"nested ticket={os.environ[TICKET_ENV]} (runs inside its place)")
        code = subprocess.call(list(command))
        if code == 0 and hold is not None:
            hold()
        return code
    fd, path, ticket = _enqueue(kind, label, command, priority, minutes, seconds)
    menubar.ensure_running()  # (never raises) ARC-DEV-1344: the menu-bar view of the queue starts with the first run it shows
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
    environment: dict[str, str] = {}
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
                                     pass_fds=(fd,) if kind in HOLD_KINDS else ())
        else:
            output = os.open(log, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            try:  # the child keeps its own copy of the descriptor
                child = subprocess.Popen(list(command), env=environment, start_new_session=True, stdin=subprocess.DEVNULL,
                                         stdout=output, stderr=subprocess.STDOUT,
                                         pass_fds=(fd,) if kind in HOLD_KINDS else ())
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
        entry = {"seq": ticket["seq"], "kind": kind, "label": ticket["label"], "cwd": ticket["cwd"],
                 "command": ticket["command"], "estimateSeconds": ticket["estimateSeconds"], "enqueuedAt": ticket["enqueuedAt"],
                 "startedAt": ticket.get("startedAt"),
                 "endedAt": ended, "exit": exit_code}
        if ticket.get("startedAt") and kind in HOLD_KINDS:  # ARC-DEV-400: what a red run is compared by, per lane
            entry.update({"lane": ticket.get("lane", kind), "peersStart": ticket.get("peers"),
                          "peersEnd": _peers_now(ticket["seq"]), "loadStart": ticket.get("loadStart"), "loadEnd": _load()})
        _history(entry)
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
    if kind == "dotnet" and ticket.get("startedAt"):
        # ARC-DEV-300: after the result is printed, so a slow shutdown delays nothing the caller reads; whatever goes
        # wrong here is a note, never a change of the command's exit code.
        try:
            with _QueueLock():
                live = [item for _, item in tickets()]
            note = shutdown_build_servers(environment or command_environment(kind, ticket["seq"]), live, ticket["seq"])
        except Exception as error:  # noqa: BLE001 - a cleanup step must not mask the run's result
            note = f"build servers not shut down: {error}"
        except KeyboardInterrupt:
            note = "build server shutdown interrupted"
        if note:
            say(f"note: {note}")
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
    pause = next((ticket for ticket in live if ticket.get("paused")), None)
    live = [ticket for ticket in live if not ticket.get("paused")]  # the pause is a ticket for the queue, not a run to list
    result = []
    if pause is not None:
        state = pause_state() or {}
        since = state.get("since")
        result.append("PAUSED: no waiting run starts" + (f" (for {_age(now - since)})" if isinstance(since, (int, float)) else "")
                      + "; running runs finish; ./arcanum queue --resume")
    if not live:
        return [*result, "queue empty"]
    for ticket in live:
        if ticket.get("startedAt"):
            result.append(f"running {_describe(ticket)} for {_age(now - ticket['startedAt'])} pid={ticket.get('childPid')}")
    for ticket in live:
        if not ticket.get("startedAt"):
            waiting_for = blocker(ticket, [*live, *([pause] if pause is not None else [])])
            behind = "" if waiting_for is None else " (paused)" if waiting_for.get("paused") else f" behind #{waiting_for['seq']}"
            result.append(f"waiting {_describe(ticket)} for {_age(now - ticket['enqueuedAt'])}" + (behind or " (starting)"))
    return result


def settings_line() -> str:
    """The caps in force, for ./arcanum queue (ARC-DEV-1353): the short lanes (the heavy lane and an export come on top)."""
    lanes, source = lanes_setting()
    limit = max_load()
    guard = f"load guard {limit:g} (now {_load()})" if limit else "load guard off"
    most = "1 Godot run at a time" if lanes <= 0 else f"up to {lanes} short + 1 heavy Godot run at once"
    return f"limits: short lanes {lanes} from {source} ({most}); {guard}"


def is_red(exit_code: Any) -> bool:
    """A finished run is red when it ended with a non-zero exit that is not its wrapper being stopped on purpose."""
    return isinstance(exit_code, int) and not isinstance(exit_code, bool) and exit_code != 0 and exit_code not in STOPPED_EXITS


def lane_of_entry(entry: dict[str, Any]) -> str:
    """The lane a history entry of a Godot run (window, headless, export) ran in. An entry from before ARC-DEV-400 has none: it is put in the lane its kind and
    declared length would take now, so the red rate of the old single slot is the baseline of each lane."""
    lane = entry.get("lane")
    if isinstance(lane, str) and lane:
        return lane
    kind = entry.get("kind", "?")
    if kind in ("window", "headless"):
        estimate = entry.get("estimateSeconds")
        longer = isinstance(estimate, (int, float)) and not isinstance(estimate, bool) and estimate > HEAVY_SECONDS
        return HEAVY if longer else SHORT
    return EXPORT if kind == "export" else str(kind)


def lane_accounting(entries: Sequence[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    """ARC-DEV-400: per lane, the runs that started, how many were red, and the same split by whether another Godot run was
    running beside it at its start (`peersStart` above 0; an entry with none recorded is `unknown`). A run that never
    started (stopped while queued) is not counted."""
    lanes: dict[str, dict[str, Any]] = {}
    for entry in entries:
        if not entry.get("startedAt") or entry.get("kind") not in GODOT_KINDS:  # dotnet and quick runs have no lane
            continue
        row = lanes.setdefault(lane_of_entry(entry), {"runs": 0, "red": 0, "stopped": 0, "alone": [0, 0],
                                                       "beside": [0, 0], "unknown": [0, 0], "reds": []})
        red = is_red(entry.get("exit"))
        row["runs"] += 1
        row["red"] += red
        row["stopped"] += entry.get("exit") in STOPPED_EXITS
        peers = entry.get("peersStart")
        bucket = row["unknown"] if not isinstance(peers, int) else row["beside"] if peers > 0 else row["alone"]
        bucket[0] += 1
        bucket[1] += red
        if red:
            row["reds"].append(entry)
    return lanes


def _percent(part: int, whole: int) -> str:
    return f"{100 * part / whole:.1f}%" if whole else "-"


def _load_text(entry: dict[str, Any]) -> str:
    start, end = entry.get("loadStart"), entry.get("loadEnd")
    return "load " + "/".join(f"{value:g}" if isinstance(value, (int, float)) else "?" for value in (start, end))


def lane_report(history: int = 0, recent: int = 8) -> list[str]:
    """Lines for ./arcanum queue --lanes: per lane the runs and red runs (alone and beside other Godot runs), then the
    latest red runs with the conditions they ran under: other Godot runs at the start and the end, the load average
    at the start and the end, and the run's number (its log is `run-<number>-*` in the logs folder)."""
    try:
        lines = (_open_folder() / "history.jsonl").read_text(errors="replace").splitlines()
    except FileNotFoundError:
        return ["no finished runs yet"]
    entries = []
    for line in lines[-history:] if history else lines:
        try:
            entry = json.loads(line)
        except ValueError:
            continue
        if isinstance(entry, dict) and entry.get("kind") in GODOT_KINDS:  # a dotnet or quick run has no lane
            entries.append(entry)
    lanes = lane_accounting(entries)
    if not lanes:
        return ["no finished runs yet"]
    result = [f"lanes over the last {len(entries)} Godot runs (red = a non-zero exit that is not a stop; runs from before ARC-DEV-400 "
              f"are put in the lane they would take now, with no peer count)",
              f"{'lane':<10}{'runs':>6}{'red':>5}{'rate':>8}   alone (red/runs)   beside others   unknown"]
    for name in sorted(lanes):
        row = lanes[name]
        result.append(f"{name:<10}{row['runs']:>6}{row['red']:>5}{_percent(row['red'], row['runs']):>8}"
                      f"   {row['alone'][1]}/{row['alone'][0]:<15} {row['beside'][1]}/{row['beside'][0]:<13} "
                      f"{row['unknown'][1]}/{row['unknown'][0]}")
    reds = sorted((item for row in lanes.values() for item in row["reds"]), key=lambda item: item.get("endedAt", 0))[-recent:]
    for entry in reds:
        stamp = time.strftime("%m-%d %H:%M", time.localtime(entry.get("enqueuedAt", 0)))
        peers = f"{entry.get('peersStart', '?')}/{entry.get('peersEnd', '?')}"
        result.append(f"red {stamp} #{entry.get('seq')} {lane_of_entry(entry)} exit={entry.get('exit')} peers {peers} "
                      f"{_load_text(entry)} {Path(entry.get('cwd', '?')).name} \"{entry.get('label', '')}\"")
    return result
