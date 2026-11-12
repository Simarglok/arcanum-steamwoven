"""ARC-DEV-1344: the launch queue's menu-bar app (tools/menubar/ArcanumQueue.swift), built here and started by the queue.

The rule is docs/workflow/toolchain.md#queue-menu-bar-app-arc-dev-1344. The app is one Swift file with no dependencies,
compiled on this Mac by `swiftc` into the local home's `menubar` folder (never into git), keyed by the source's hash, so a
changed source is built again. Every run that takes a queue place calls ensure_running(): when the app does not hold its
lock file it starts a detached helper (`python -m tools.local_dev.menubar start`) that builds the app when needed and
becomes it. Nothing here blocks or fails a run: a missing compiler or a failed build only writes `menubar.log`.
"""
from __future__ import annotations

import errno
import fcntl
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from typing import Sequence

from . import config

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "tools" / "menubar" / "ArcanumQueue.swift"
ENV = "ARCANUM_QUEUE_MENUBAR"  # 0/off/no/false: never start the app; 1/on/yes/true: start it even under ARCANUM_LOCAL_HOME
OFF = frozenset({"0", "off", "no", "false"})
ON = frozenset({"1", "on", "yes", "true"})


def app_dir() -> Path:
    return config.home() / "menubar"


def lock_path() -> Path:
    return app_dir() / "app.lock"


def log_path() -> Path:
    return app_dir() / "menubar.log"


def enabled(environment: dict[str, str] | None = None, platform: str | None = None) -> bool:
    """Whether a queued run starts the app: on macOS, unless ENV turns it off. A run under ARCANUM_LOCAL_HOME (the tests'
    own home) does not start it unless ENV turns it on, so a test never opens a menu-bar item."""
    environment = os.environ if environment is None else environment
    if (sys.platform if platform is None else platform) != "darwin":
        return False
    value = environment.get(ENV, "").strip().lower()
    if value in OFF:
        return False
    return value in ON or not environment.get("ARCANUM_LOCAL_HOME")


def _held(path: Path) -> bool:
    """True while some process holds the flock on path (the app holds its lock for its whole life)."""
    try:
        fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW)
    except OSError:
        return False
    try:
        fcntl.flock(fd, fcntl.LOCK_SH | fcntl.LOCK_NB)
        fcntl.flock(fd, fcntl.LOCK_UN)
        return False
    except OSError as error:
        if error.errno in (errno.EWOULDBLOCK, errno.EAGAIN):
            return True
        raise
    finally:
        os.close(fd)


def running() -> bool:
    return _held(lock_path())


def binary_path(source: Path = SOURCE) -> Path:
    return app_dir() / f"ArcanumQueue-{hashlib.sha256(source.read_bytes()).hexdigest()[:12]}"


FAILURE_PAUSE_SECONDS = 600.0  # after a failed build the next try waits this long, so a broken compiler is not run by every queued run
KEEP_BUILDS_SECONDS = 14 * 86400.0  # a build other checkouts' sources may still want is kept this long after it was last made


def failure_marker(source: Path | None = None) -> Path:
    return app_dir() / f"build-failed-{hashlib.sha256((source or SOURCE).read_bytes()).hexdigest()[:12]}"


def recently_failed(source: Path | None = None, now: float | None = None) -> bool:
    try:
        return (time.time() if now is None else now) - failure_marker(source).stat().st_mtime < FAILURE_PAUSE_SECONDS
    except OSError:
        return False


def compile_command(swiftc: str, target: Path, source: Path = SOURCE) -> tuple[str, ...]:
    return (swiftc, "-O", "-warnings-as-errors", str(source), "-o", str(target))


def build(source: Path = SOURCE, *, run=subprocess.run, which=shutil.which) -> tuple[Path | None, str]:
    """Build the app unless the build for this source exists: (binary, "") or (None, why not)."""
    if not source.is_file():
        return None, f"no source at {source}"
    target = binary_path(source)
    if target.exists():
        return target, ""
    swiftc = which("swiftc")
    if swiftc is None:
        return None, "no swiftc (install the Xcode command line tools)"
    app_dir().mkdir(mode=0o700, parents=True, exist_ok=True)
    marker = failure_marker(source)
    hidden = target.with_name(f".build-{os.getpid()}")
    try:
        done = run(compile_command(swiftc, hidden, source), capture_output=True, text=True, timeout=600)
    except (OSError, subprocess.TimeoutExpired) as error:
        hidden.unlink(missing_ok=True)
        marker.touch()
        return None, f"swiftc did not run: {error}"
    if done.returncode != 0:
        hidden.unlink(missing_ok=True)
        marker.touch()
        return None, f"swiftc failed ({done.returncode}): {(done.stderr or done.stdout).strip()[:600]}"
    os.replace(hidden, target)
    marker.unlink(missing_ok=True)
    now = time.time()
    for old in app_dir().glob("ArcanumQueue-*"):  # builds of other sources (another checkout's) go only when long unused
        try:
            if old != target and now - old.stat().st_mtime > KEEP_BUILDS_SECONDS:
                old.unlink(missing_ok=True)
        except OSError:
            pass
    return target, ""


def ensure_running(*, spawn=subprocess.Popen) -> bool:
    """Start the app (detached) when it is enabled and not running; True when a start was asked for. Never raises."""
    try:
        if not enabled() or running() or recently_failed():
            return False
        app_dir().mkdir(mode=0o700, parents=True, exist_ok=True)
        with open(log_path(), "ab") as log:
            spawn([sys.executable, "-B", "-m", "tools.local_dev.menubar", "start"], cwd=ROOT, start_new_session=True,
                  stdin=subprocess.DEVNULL, stdout=log, stderr=log, close_fds=True)
        return True
    except (OSError, ValueError, RuntimeError):
        return False


def start(extra: Sequence[str] = ()) -> int:
    """Build when needed, then become the app (exec). A second helper that finds another one building or the app running
    just ends. Exits non-zero with the reason on stderr when the app cannot be built."""
    app_dir().mkdir(mode=0o700, parents=True, exist_ok=True)
    fd = os.open(app_dir() / "start.lock", os.O_RDWR | os.O_CREAT | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        return 0  # another helper is building or starting it
    if running() or recently_failed():
        return 0
    target, why = build()
    if target is None:
        print(f"menubar: {why}", file=sys.stderr)
        return 1
    os.execv(target, [str(target), *extra])
    return 1  # unreachable


def main(argv: Sequence[str]) -> int:
    if argv and argv[0] == "start":
        return start(argv[1:])
    print("usage: python3 -B -m tools.local_dev.menubar start [APP ARGS]", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
